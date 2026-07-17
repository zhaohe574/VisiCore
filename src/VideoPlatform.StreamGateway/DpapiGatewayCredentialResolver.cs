using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using VideoPlatform.Core;

namespace VideoPlatform.StreamGateway;

public sealed class DpapiGatewayCredentialResolver
{
    public NetworkCredential Resolve(WorkerProtectedCredential credential)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("流网关必须与设备 Worker 部署在同一台 Windows 主机。");
        }
        if (!credential.ProtectionMode.Equals("WindowsDpapiLocalMachine", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("流网关不支持当前设备凭据保护方式。");
        }

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(
                Convert.FromBase64String(credential.CiphertextBase64),
                null,
                DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("设备凭据不能由当前流网关主机解密。", exception);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<GatewayCredentialPayload>(plaintext)
                ?? throw new InvalidOperationException("设备凭据格式无效。");
            if (string.IsNullOrWhiteSpace(payload.Username) || string.IsNullOrWhiteSpace(payload.Password))
            {
                throw new InvalidOperationException("设备凭据缺少用户名或密码。");
            }
            return new NetworkCredential(payload.Username, payload.Password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

public sealed record GatewayCredentialPayload(string Username, string Password);
