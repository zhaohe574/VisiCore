using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed record RecorderEndpointRegistration(
    RecorderEndpointProtocol Protocol,
    string? Host,
    int Port,
    string? CredentialReference,
    bool UseTls = false,
    string? CertificateThumbprint = null);

public static class RecorderRegistrationValidator
{
    public static bool TryValidateForPlugin(
        DevicePluginManifest manifest,
        IReadOnlyCollection<RecorderEndpointRegistration> endpoints,
        out string validationError)
    {
        if (!TryValidateCommon(endpoints, out validationError))
        {
            return false;
        }

        var requiredProtocols = manifest.Endpoints
            .Where(item => item.Required)
            .Select(item => item.Protocol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var configuredProtocols = endpoints
            .Select(item => item.Protocol.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requiredProtocols.IsSubsetOf(configuredProtocols))
        {
            validationError = "设备端点缺少协议插件要求的必填协议。";
            return false;
        }
        var declaredProtocols = manifest.Endpoints
            .Select(item => item.Protocol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (configuredProtocols.Any(item => !declaredProtocols.Contains(item)))
        {
            validationError = "设备端点包含协议插件未声明的协议。";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private static bool TryValidateCommon(
        IReadOnlyCollection<RecorderEndpointRegistration> endpoints,
        out string validationError)
    {
        if (endpoints.Count == 0)
        {
            validationError = "至少配置一个设备端点。";
            return false;
        }
        if (endpoints.Any(item => !Enum.IsDefined(typeof(RecorderEndpointProtocol), item.Protocol) ||
                                  !IsValidHost(item.Host) || item.Port is < 1 or > 65535 ||
                                  !IsValidCredentialReference(item.CredentialReference)))
        {
            validationError = "设备端点的协议、地址、端口或凭据引用无效。";
            return false;
        }
        if (endpoints.Any(item => !string.IsNullOrWhiteSpace(item.CertificateThumbprint) &&
                                  !DeviceCertificatePolicy.TryNormalizeSha256Thumbprint(item.CertificateThumbprint, out _)))
        {
            validationError = "设备 TLS 证书指纹必须是 SHA-256 十六进制值。";
            return false;
        }
        if (endpoints.Any(item => item.UseTls && item.Protocol is RecorderEndpointProtocol.Onvif &&
                                  !DeviceCertificatePolicy.TryNormalizeSha256Thumbprint(item.CertificateThumbprint, out _)))
        {
            validationError = "启用 TLS 的 ONVIF 或 ISAPI 端点必须配置 SHA-256 叶证书指纹。";
            return false;
        }
        if (endpoints.Select(item => item.Protocol).Distinct().Count() != endpoints.Count)
        {
            validationError = "每种协议只能配置一个端点。";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private static bool IsValidHost(string? value) => RecorderEndpointHostPolicy.IsValidHost(value);

    private static bool IsValidCredentialReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
}
