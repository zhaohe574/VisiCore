using System.Text.Json;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VisiCore.Core;
using VisiCore.EdgeAgent;
using Xunit;

namespace VisiCore.EdgeAgent.Tests;

public sealed class EdgeAgentRuntimeTests
{
    [Fact(DisplayName = "运行时只接受 v1 允许字段并原子保留上一个有效配置")]
    public void RuntimeConfigurationOnlyAppliesValidatedV1()
    {
        var settings = new EdgeAgentRuntimeSettings(new EdgeAgentOptions
        {
            StateDirectory = Path.GetTempPath(),
            ControlPlaneBaseUri = "https://center.example/"
        });

        Assert.True(settings.TryApply("{\"schemaVersion\":1,\"inventorySyncIntervalSeconds\":120,\"onvifEnabled\":false}", out _));
        var applied = settings.Snapshot();
        Assert.Equal(120, applied.InventorySyncIntervalSeconds);
        Assert.False(applied.OnvifEnabled);

        Assert.False(settings.TryApply("{\"schemaVersion\":1,\"credential\":\"forbidden\"}", out var failureKind));
        Assert.Equal("configuration_schema_invalid", failureKind);
        Assert.Equal(applied, settings.Snapshot());
    }

    [Fact(DisplayName = "Windows 身份材料必须以 DPAPI 本机保护格式保存")]
    public void WindowsIdentityMaterialIsDpapiProtected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "visicore-edge-agent-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EdgeAgentIdentityStore(new EdgeAgentOptions
            {
                StateDirectory = directory,
                ControlPlaneBaseUri = "https://center.example/"
            });
            using var identity = store.LoadOrCreate();
            var persisted = File.ReadAllText(Path.Combine(directory, "identity.json"));
            Assert.StartsWith("dpapi-local-machine:", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(identity.Identity.AgentId, persisted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Host 操作交换区只返回最小回执")]
    public async Task HostOperationExchangeUsesOperationAndReceiptFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-exchange-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new HostAgentOptions
            {
                OperationInboxDirectory = Path.Combine(root, "inbox"),
                OperationReceiptDirectory = Path.Combine(root, "receipts")
            };
            var exchange = new HostOperationExchange(options);
            var id = Guid.NewGuid();
            await exchange.SubmitAsync(new EdgeOperation(id, "deployment", "{}"), CancellationToken.None);
            Assert.True(File.Exists(Path.Combine(options.OperationInboxDirectory, $"{id:N}.operation.json")));

            Directory.CreateDirectory(options.OperationReceiptDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(options.OperationReceiptDirectory, $"{id:N}.receipt.json"),
                JsonSerializer.Serialize(new HostOperationReceipt(id, false, "signature_invalid", DateTimeOffset.UtcNow)));
            var receipt = await exchange.TryReadReceiptAsync(id, CancellationToken.None);
            Assert.Equal("signature_invalid", receipt?.FailureKind);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "发行清单必须绑定当前可识别的平台、制品哈希和有效期")]
    public void ReleaseManifestRejectsUntrustedShape()
    {
        var now = DateTimeOffset.UtcNow;
        var valid = JsonSerializer.Serialize(new
        {
            releaseId = "v0.3.0-windows-x64",
            operationType = "deployment",
            targetPlatform = "windows",
            targetArchitecture = "x64",
            artifactUrl = "https://releases.example/edge-node.msi",
            artifactSha256 = new string('a', 64),
            minimumHostAgentVersion = "0.3.0",
            signingPublicKeyId = "release-key-2026",
            issuedAt = now,
            expiresAt = now.AddHours(1)
        });
        Assert.True(EdgeReleaseManifest.TryParse(valid, out _, out _));
        Assert.False(EdgeReleaseManifest.TryParse(valid.Replace("https://", "http://", StringComparison.Ordinal), out _, out _));
    }

    [Fact(DisplayName = "Host Agent 回滚前必须再次验证已暂存制品哈希")]
    public async Task PersistedArtifactMustStillMatchVerifiedHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-artifact-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var artifactPath = Path.Combine(root, "edge-node.msi");
            await File.WriteAllTextAsync(artifactPath, "verified-release");
            string hash;
            await using (var stream = File.OpenRead(artifactPath))
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            }
            var verifier = new HostReleaseArtifactVerifier(new HostAgentOptions
            {
                ReleaseArtifactDirectory = root
            });
            var artifact = new HostVerifiedReleaseArtifact(artifactPath, null, hash);

            Assert.True(await verifier.VerifyPersistedAsync(artifact, CancellationToken.None));

            await File.WriteAllTextAsync(artifactPath, "tampered-release");
            Assert.False(await verifier.VerifyPersistedAsync(artifact, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Host 配置通道可在未导入发行信任根时运行，但实际升级必须导入信任根")]
    public void HostAgentSeparatesConfigurationFromReleaseExecution()
    {
        var root = Path.Combine(Path.GetTempPath(), "visicore-host-options-tests", Guid.NewGuid().ToString("N"));
        var options = new HostAgentOptions
        {
            Enabled = true,
            OperationInboxDirectory = Path.Combine(root, "inbox"),
            OperationReceiptDirectory = Path.Combine(root, "receipts"),
            OperationStateDirectory = Path.Combine(root, "state"),
            ReleaseArtifactDirectory = Path.Combine(root, "releases")
        };

        Assert.True(options.TryValidate(out _));
        var executionOptions = new HostAgentOptions
        {
            Enabled = true,
            AllowExecution = true,
            OperationInboxDirectory = options.OperationInboxDirectory,
            OperationReceiptDirectory = options.OperationReceiptDirectory,
            OperationStateDirectory = options.OperationStateDirectory,
            ReleaseArtifactDirectory = options.ReleaseArtifactDirectory
        };
        Assert.False(executionOptions.TryValidate(out _));
    }

    [Fact(DisplayName = "Linux 配置 Socket 只接受持有访问令牌的固定校验请求")]
    public async Task LinuxConfigurationSocketRequiresAccessToken()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = Path.Combine(Path.GetTempPath(), "visicore-host-configuration-tests", Guid.NewGuid().ToString("N"));
        var socketPath = Path.Combine(root, "host-agent.sock");
        var tokenPath = Path.Combine(root, "access.token");
        var composePath = Path.Combine(root, "compose.yaml");
        var agentConfigurationPath = Path.Combine(root, "state", "edge-agent.json");
        var bootstrapPath = Path.Combine(root, "state", "bootstrap", "bootstrap.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(tokenPath, "test-access-token");
        await File.WriteAllTextAsync(composePath, "services: {}\n");
        using var cancellation = new CancellationTokenSource();
        var options = new HostAgentOptions
        {
            ConfigurationSocketPath = socketPath,
            ConfigurationTokenPath = tokenPath,
            DockerComposeExecutablePath = "/bin/true",
            ComposeFilePath = composePath,
            ManagedEdgeAgentConfigurationPath = agentConfigurationPath,
            ManagedEdgeAgentBootstrapPath = bootstrapPath
        };
        var worker = new HostConfigurationSocketWorker(
            options,
            new DockerComposeHostOperationExecutor(options),
            NullLogger<HostConfigurationSocketWorker>.Instance);

        try
        {
            await worker.StartAsync(cancellation.Token);
            for (var attempt = 0; attempt < 20 && !File.Exists(socketPath); attempt++)
            {
                await Task.Delay(50, cancellation.Token);
            }
            Assert.True(File.Exists(socketPath));

            var accepted = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "test-access-token", "test", "https://center.example/"));
            Assert.True(accepted.Succeeded);

            var rejected = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "invalid-token", "test", "https://center.example/"));
            Assert.False(rejected.Succeeded);
            Assert.Equal("configuration_access_denied", rejected.FailureKind);

            var applied = await SendConfigurationCommandAsync(socketPath, new EdgeNodeConfigurationCommand(
                "test-access-token", "apply", "https://center.example/", "one-time-code"));
            Assert.True(applied.Succeeded);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(agentConfigurationPath));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(bootstrapPath));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(bootstrapPath)!));
        }
        finally
        {
            cancellation.Cancel();
            await worker.StopAsync(CancellationToken.None);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<EdgeNodeConfigurationCommandResult> SendConfigurationCommandAsync(string socketPath, EdgeNodeConfigurationCommand command)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();

        var responsePrefix = new byte[4];
        await stream.ReadExactlyAsync(responsePrefix);
        var response = new byte[BinaryPrimitives.ReadInt32LittleEndian(responsePrefix)];
        await stream.ReadExactlyAsync(response);
        return JsonSerializer.Deserialize<EdgeNodeConfigurationCommandResult>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
