using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using VideoPlatform.Core;

namespace VideoPlatform.OnvifEdgeWorker;

public interface IOnvifEdgeCredentialResolver
{
    NetworkCredential Resolve(WorkerProtectedCredential credential);
}

public sealed class OnvifEdgeCredentialResolver : IOnvifEdgeCredentialResolver
{
    public NetworkCredential Resolve(WorkerProtectedCredential credential)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ONVIF 边缘 Worker 只支持 Windows。 ");
        }
        if (!credential.ProtectionMode.Equals("WindowsDpapiLocalMachine", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ONVIF 边缘 Worker 不支持当前设备凭据保护方式。 ");
        }

        var plaintext = ProtectedData.Unprotect(
            Convert.FromBase64String(credential.CiphertextBase64),
            null,
            DataProtectionScope.LocalMachine);
        try
        {
            var payload = JsonSerializer.Deserialize<OnvifEdgeCredentialPayload>(plaintext)
                ?? throw new InvalidOperationException("设备凭据格式无效。 ");
            if (string.IsNullOrWhiteSpace(payload.Username) || string.IsNullOrWhiteSpace(payload.Password))
            {
                throw new InvalidOperationException("设备凭据缺少用户名或密码。 ");
            }
            return new NetworkCredential(payload.Username, payload.Password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

public sealed record OnvifEdgeCredentialPayload(string Username, string Password);
