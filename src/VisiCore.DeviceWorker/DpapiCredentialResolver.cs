using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.DeviceWorker;

public sealed class DpapiCredentialResolver(WorkerCredentialStore credentialStore) : IRecorderCredentialResolver
{
    public Task<NetworkCredential> ResolveAsync(RecorderCredentialTarget recorder, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("设备 Worker 仅支持在 Windows 上使用 DPAPI 解密设备凭据。 ");
        }

        var credential = credentialStore.Get(recorder.CredentialReference)
            ?? throw new InvalidOperationException("录像机引用的受保护凭据不存在。 ");
        if (!credential.ProtectionMode.Equals("WindowsDpapiLocalMachine", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("录像机凭据保护方式不受当前 Worker 支持。 ");
        }

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(Convert.FromBase64String(credential.CiphertextBase64), null, DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("设备凭据不能由当前 Windows Worker 解密。请在该受控主机重新生成密文。", exception);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<DeviceCredentialPayload>(plaintext)
                ?? throw new InvalidOperationException("设备凭据格式无效。 ");
            if (string.IsNullOrWhiteSpace(payload.Username) || string.IsNullOrWhiteSpace(payload.Password))
            {
                throw new InvalidOperationException("设备凭据缺少用户名或密码。 ");
            }

            return Task.FromResult(new NetworkCredential(payload.Username, payload.Password));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

public sealed class WorkerCredentialStore
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, WorkerProtectedCredential> _credentials = new Dictionary<string, WorkerProtectedCredential>(StringComparer.Ordinal);

    public void Replace(IEnumerable<WorkerRecorderAssignment> assignments)
    {
        var credentials = assignments.SelectMany(item => item.Credentials)
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        lock (_gate)
        {
            _credentials = credentials;
        }
    }

    public WorkerProtectedCredential? Get(string credentialReference)
    {
        lock (_gate)
        {
            return _credentials.GetValueOrDefault(credentialReference);
        }
    }
}

public sealed record DeviceCredentialPayload(string Username, string Password)
{
    public static string ProtectForCurrentMachine(string username, string password)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("仅支持在 Windows 主机生成 DPAPI 密文。 ");
        }
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("用户名和密码不能为空。 ");
        }

        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new DeviceCredentialPayload(username, password)));
        try
        {
            var ciphertext = ProtectedData.Protect(plaintext, null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
