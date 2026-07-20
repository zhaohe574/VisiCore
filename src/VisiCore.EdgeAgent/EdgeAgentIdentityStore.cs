using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VisiCore.EdgeAgent;

public sealed class EdgeAgentIdentityStore(EdgeAgentOptions options)
{
    private const string IdentityFileName = "identity.json";
    private const string PrivateKeyFileName = "agent-private-key.pem";

    public EdgeAgentIdentityMaterial LoadOrCreate()
    {
        var stateDirectory = Path.GetFullPath(options.StateDirectory);
        Directory.CreateDirectory(stateDirectory);
        TryRestrictDirectoryPermissions(stateDirectory);

        var identityPath = Path.Combine(stateDirectory, IdentityFileName);
        var privateKeyPath = Path.Combine(stateDirectory, PrivateKeyFileName);
        if (File.Exists(identityPath) != File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException("Edge Agent 身份状态不完整，拒绝在未知密钥状态下继续运行。 ");
        }

        if (!File.Exists(identityPath))
        {
            using var generated = RSA.Create(3072);
            var identity = new EdgeAgentIdentity(
                Guid.NewGuid().ToString("N"),
                $"edge-{Guid.NewGuid():N}",
                null,
                null,
                null);
            WriteRestrictedSecret(privateKeyPath, generated.ExportPkcs8PrivateKeyPem());
            WriteRestrictedSecret(identityPath, JsonSerializer.Serialize(identity));
        }

        var persistedIdentity = JsonSerializer.Deserialize<EdgeAgentIdentity>(ReadRestrictedSecret(identityPath))
            ?? throw new InvalidOperationException("Edge Agent 身份状态格式无效。 ");
        persistedIdentity.Validate();

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(ReadRestrictedSecret(privateKeyPath));
            return new EdgeAgentIdentityMaterial(this, persistedIdentity, rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    internal void Persist(EdgeAgentIdentity identity)
    {
        identity.Validate();
        var identityPath = Path.Combine(Path.GetFullPath(options.StateDirectory), IdentityFileName);
        WriteRestrictedSecret(identityPath, JsonSerializer.Serialize(identity));
    }

    private static void WriteRestrictedSecret(string path, string value)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Edge Agent 状态文件路径无效。 ");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, ProtectForCurrentPlatform(value));
            TryRestrictFilePermissions(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            TryRestrictFilePermissions(path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ReadRestrictedSecret(string path)
    {
        var stored = File.ReadAllText(path);
        if (!OperatingSystem.IsWindows())
        {
            return stored;
        }
        const string prefix = "dpapi-local-machine:";
        if (!stored.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Windows Edge Agent 身份状态未受 DPAPI 保护。 ");
        }
        try
        {
            var protectedBytes = Convert.FromBase64String(stored[prefix.Length..]);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
            try
            {
                return Encoding.UTF8.GetString(clearBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Windows Edge Agent 身份状态格式无效。 ", exception);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("Windows Edge Agent 身份状态无法在当前机器解封。 ", exception);
        }
    }

    private static string ProtectForCurrentPlatform(string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return value;
        }
        var clearBytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
            try
            {
                return "dpapi-local-machine:" + Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static void TryRestrictDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void TryRestrictFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

public sealed record EdgeAgentIdentity(
    string AgentId,
    string KeyId,
    string? WorkerId,
    string? WorkerToken,
    string? ConfigurationVersion)
{
    public bool IsEnrolled => !string.IsNullOrWhiteSpace(WorkerToken) && !string.IsNullOrWhiteSpace(WorkerId);

    public void Validate()
    {
        if (!Guid.TryParseExact(AgentId, "N", out _) ||
            string.IsNullOrWhiteSpace(KeyId) || KeyId.Length > 128 ||
            (IsEnrolled && WorkerToken!.Length < 32))
        {
            throw new InvalidOperationException("Edge Agent 身份状态内容无效。 ");
        }
    }
}

public sealed class EdgeAgentIdentityMaterial(
    EdgeAgentIdentityStore store,
    EdgeAgentIdentity identity,
    RSA privateKey) : IDisposable
{
    public EdgeAgentIdentity Identity { get; private set; } = identity;
    public RSA PrivateKey { get; } = privateKey;

    public void UpdateEnrollment(string workerId, string workerToken, string? configurationVersion)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128 ||
            string.IsNullOrWhiteSpace(workerToken) || workerToken.Length < 32)
        {
            throw new InvalidOperationException("控制面返回的 Edge Agent 身份信息无效。 ");
        }

        Identity = Identity with
        {
            WorkerId = workerId,
            WorkerToken = workerToken,
            ConfigurationVersion = configurationVersion
        };
        store.Persist(Identity);
    }

    public void UpdateConfigurationVersion(string? configurationVersion)
    {
        if (string.IsNullOrWhiteSpace(configurationVersion) ||
            string.Equals(Identity.ConfigurationVersion, configurationVersion, StringComparison.Ordinal))
        {
            return;
        }

        Identity = Identity with { ConfigurationVersion = configurationVersion };
        store.Persist(Identity);
    }

    public void Dispose() => PrivateKey.Dispose();
}
