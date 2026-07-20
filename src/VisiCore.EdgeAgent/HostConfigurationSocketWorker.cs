using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 仅 Linux Host Agent 监听的本地配置通道。它只接受固定配置对象，
/// 不能传递 shell、Docker 参数或发行操作。
/// </summary>
public sealed class HostConfigurationSocketWorker(
    HostAgentOptions options,
    DockerComposeHostOperationExecutor composeExecutor,
    ILogger<HostConfigurationSocketWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsLinux() ||
            string.IsNullOrWhiteSpace(options.ConfigurationSocketPath) ||
            string.IsNullOrWhiteSpace(options.ConfigurationTokenPath))
        {
            return;
        }

        var socketPath = options.ConfigurationSocketPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
            if (File.Exists(socketPath)) File.Delete(socketPath);
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            listener.Listen(8);

            while (!stoppingToken.IsCancellationRequested)
            {
                using var connection = await listener.AcceptAsync(stoppingToken);
                await ProcessConnectionAsync(connection, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            logger.LogError("Host Agent 配置通道不可用，失败类别 {FailureKind}。", "configuration_socket_unavailable");
        }
        catch (SocketException)
        {
            logger.LogError("Host Agent 配置通道不可用，失败类别 {FailureKind}。", "configuration_socket_unavailable");
        }
        finally
        {
            if (File.Exists(socketPath)) File.Delete(socketPath);
        }
    }

    private async Task ProcessConnectionAsync(Socket connection, CancellationToken cancellationToken)
    {
        await using var stream = new NetworkStream(connection, ownsSocket: false);
        var command = await ReadAsync<EdgeNodeConfigurationCommand>(stream, cancellationToken);
        var result = command is null
            ? new EdgeNodeConfigurationCommandResult(false, "configuration_request_invalid")
            : await HandleAsync(command, cancellationToken);
        await WriteAsync(stream, result, cancellationToken);
        if (result.HostRestarting)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                Environment.Exit(75);
            });
        }
    }

    private async Task<EdgeNodeConfigurationCommandResult> HandleAsync(EdgeNodeConfigurationCommand command, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(command.AccessToken, cancellationToken))
        {
            return new EdgeNodeConfigurationCommandResult(false, "configuration_access_denied");
        }

        if (command.Action.Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryValidateAgentInput(command.ControlPlaneBaseUri, out var failureKind))
            {
                return new EdgeNodeConfigurationCommandResult(false, failureKind);
            }
            if (command.Host is not null && !TryBuildHostConfiguration(command.Host, out _, out _, out var hostFailureKind))
            {
                return new EdgeNodeConfigurationCommandResult(false, hostFailureKind);
            }
            return new EdgeNodeConfigurationCommandResult(true);
        }
        string? applyFailureKind = "configuration_request_invalid";
        if (!command.Action.Equals("apply", StringComparison.OrdinalIgnoreCase) ||
            !TryValidateAgentInput(command.ControlPlaneBaseUri, out applyFailureKind) ||
            string.IsNullOrWhiteSpace(command.EnrollmentCode))
        {
            return new EdgeNodeConfigurationCommandResult(false, applyFailureKind ?? "configuration_request_invalid");
        }
        if (string.IsNullOrWhiteSpace(options.ManagedEdgeAgentConfigurationPath) ||
            string.IsNullOrWhiteSpace(options.ManagedEdgeAgentBootstrapPath))
        {
            return new EdgeNodeConfigurationCommandResult(false, "configuration_paths_invalid");
        }

        try
        {
            var normalizedUri = NormalizeControlPlaneUri(command.ControlPlaneBaseUri!);
            WriteJsonAtomically(options.ManagedEdgeAgentConfigurationPath, new
            {
                EdgeAgent = new
                {
                    ControlPlaneBaseUri = normalizedUri,
                    BootstrapFilePath = options.ManagedEdgeAgentBootstrapPath
                }
            });
            WriteJsonAtomically(options.ManagedEdgeAgentBootstrapPath, new { enrollmentCode = command.EnrollmentCode.Trim() });
            SetAgentConfigurationPermissions(options.ManagedEdgeAgentConfigurationPath);
            SetAgentConfigurationPermissions(options.ManagedEdgeAgentBootstrapPath);

            var hostRestarting = false;
            if (command.Host is not null)
            {
                if (!TryBuildHostConfiguration(command.Host, out var hostConfiguration, out var publicKeyPem, out var hostFailureKind))
                {
                    return new EdgeNodeConfigurationCommandResult(false, hostFailureKind);
                }
                if (publicKeyPem is not null)
                {
                    var publicKeyPath = hostConfiguration.SigningPublicKeyPath!;
                    WriteTextAtomically(publicKeyPath, publicKeyPem);
                    SetOwnerOnlyPermissions(publicKeyPath);
                }
                if (string.IsNullOrWhiteSpace(options.ManagedHostAgentConfigurationPath))
                {
                    return new EdgeNodeConfigurationCommandResult(false, "host_configuration_path_invalid");
                }
                WriteJsonAtomically(options.ManagedHostAgentConfigurationPath, new { HostAgent = hostConfiguration });
                SetOwnerOnlyPermissions(options.ManagedHostAgentConfigurationPath);
                hostRestarting = true;
            }

            var restart = await composeExecutor.RestartConfiguredEdgeAgentAsync(cancellationToken);
            return restart.Succeeded
                ? new EdgeNodeConfigurationCommandResult(true, HostRestarting: hostRestarting)
                : new EdgeNodeConfigurationCommandResult(false, restart.FailureKind, hostRestarting);
        }
        catch (CryptographicException)
        {
            return new EdgeNodeConfigurationCommandResult(false, "host_public_key_invalid");
        }
        catch (IOException)
        {
            return new EdgeNodeConfigurationCommandResult(false, "configuration_storage_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return new EdgeNodeConfigurationCommandResult(false, "configuration_storage_failed");
        }
    }

    private bool TryBuildHostConfiguration(
        EdgeNodeHostConfigurationInput input,
        out HostAgentOptions configuration,
        out string? publicKeyPem,
        out string failureKind)
    {
        publicKeyPem = null;
        failureKind = "host_configuration_invalid";
        if (input.AllowExecution && !input.Enabled)
        {
            configuration = new HostAgentOptions();
            return false;
        }
        var publicKeyPath = options.SigningPublicKeyPath ?? "/etc/visicore/edge-host-agent/release-public-key.pem";
        var hosts = input.AllowedArtifactHosts.Select(host => host.Trim()).Where(host => host.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!string.IsNullOrWhiteSpace(input.SigningPublicKeyPem))
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(input.SigningPublicKeyPem);
            publicKeyPem = input.SigningPublicKeyPem;
        }
        configuration = new HostAgentOptions
        {
            Enabled = input.Enabled,
            AllowExecution = input.AllowExecution,
            OperationInboxDirectory = options.OperationInboxDirectory,
            OperationReceiptDirectory = options.OperationReceiptDirectory,
            OperationStateDirectory = options.OperationStateDirectory,
            ReleaseArtifactDirectory = options.ReleaseArtifactDirectory,
            DockerComposeExecutablePath = options.DockerComposeExecutablePath,
            ComposeFilePath = options.ComposeFilePath,
            RollbackComposeFilePath = options.RollbackComposeFilePath,
            SigningPublicKeyPath = publicKeyPem is null ? options.SigningPublicKeyPath : publicKeyPath,
            SigningPublicKeyId = string.IsNullOrWhiteSpace(input.SigningPublicKeyId) ? options.SigningPublicKeyId : input.SigningPublicKeyId.Trim(),
            AllowedArtifactHosts = hosts,
            MaximumArtifactBytes = input.MaximumArtifactBytes,
            ExecutionTimeoutSeconds = input.ExecutionTimeoutSeconds,
            PollIntervalSeconds = options.PollIntervalSeconds,
            ConfigurationSocketPath = options.ConfigurationSocketPath,
            ConfigurationTokenPath = options.ConfigurationTokenPath,
            ManagedEdgeAgentConfigurationPath = options.ManagedEdgeAgentConfigurationPath,
            ManagedEdgeAgentBootstrapPath = options.ManagedEdgeAgentBootstrapPath,
            ManagedHostAgentConfigurationPath = options.ManagedHostAgentConfigurationPath
        };
        if (!configuration.TryValidate(out _)) return false;
        return true;
    }

    private async Task<bool> IsAuthorizedAsync(string providedToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providedToken) || string.IsNullOrWhiteSpace(options.ConfigurationTokenPath) || !File.Exists(options.ConfigurationTokenPath))
        {
            return false;
        }
        try
        {
            var expectedToken = (await File.ReadAllTextAsync(options.ConfigurationTokenPath, cancellationToken)).Trim();
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedToken), Encoding.UTF8.GetBytes(expectedToken));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryValidateAgentInput(string? value, out string? failureKind)
    {
        failureKind = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            failureKind = "control_plane_invalid";
            return false;
        }
        return true;
    }

    private static string NormalizeControlPlaneUri(string value) => new Uri(value.TrimEnd('/') + "/", UriKind.Absolute).AbsoluteUri;

    private static void WriteJsonAtomically(string path, object value) =>
        WriteTextAtomically(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static void WriteTextAtomically(string path, string value)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, value);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void SetOwnerOnlyPermissions(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void SetAgentConfigurationPermissions(string path)
    {
        if (!OperatingSystem.IsLinux()) return;

        // Host 与无特权容器同属受限配置组：Agent 只读配置，且仅能在其 bootstrap 目录中删除一次性凭证。
        var directory = Path.GetDirectoryName(path)!;
        File.SetUnixFileMode(directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute);
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
    }

    private static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        if (!await ReadExactlyAsync(stream, prefix, cancellationToken)) return default;
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 1 or > 131_072) return default;
        var payload = new byte[length];
        return !await ReadExactlyAsync(stream, payload, cancellationToken)
            ? default
            : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}
