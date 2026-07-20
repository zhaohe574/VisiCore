using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VisiCore.Core;
using Xunit;

namespace VisiCore.DeviceWorker.Tests;

public sealed class DeviceCertificatePolicyTests
{
    [Fact(DisplayName = "TLS 设备证书必须同时通过系统校验和 SHA-256 指纹匹配")]
    public void PinnedCertificateRequiresTrustedChainAndMatchingThumbprint()
    {
        using var certificate = CreateCertificate();
        var endpoint = new WorkerRecorderEndpoint(
            "Onvif",
            "nvr.example.internal",
            443,
            true,
            "nvr-credential",
            Convert.ToHexString(SHA256.HashData(certificate.RawData)));

        Assert.True(DeviceCertificatePolicy.IsServerCertificateAccepted(endpoint, certificate, SslPolicyErrors.None, false));
        Assert.False(DeviceCertificatePolicy.IsServerCertificateAccepted(endpoint, certificate, SslPolicyErrors.RemoteCertificateNameMismatch, false));

        using var anotherCertificate = CreateCertificate();
        Assert.False(DeviceCertificatePolicy.IsServerCertificateAccepted(endpoint, anotherCertificate, SslPolicyErrors.None, false));
    }

    [Fact(DisplayName = "远程 TLS 端点不能通过开发证书豁免绕过指纹固定")]
    public void RemoteTlsEndpointCannotUseDevelopmentCertificateBypass()
    {
        var endpoint = new WorkerRecorderEndpoint("Onvif", "nvr.example.internal", 443, true, "nvr-credential");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DeviceCertificatePolicy.EnsureTlsConfiguration(endpoint, true));

        Assert.Contains("SHA-256", exception.Message);
    }

    [Fact(DisplayName = "本机回环 TLS 开发可显式使用临时证书")]
    public void LoopbackTlsEndpointCanUseExplicitDevelopmentCertificateBypass()
    {
        using var certificate = CreateCertificate();
        var endpoint = new WorkerRecorderEndpoint("Onvif", "127.0.0.1", 443, true, "nvr-credential");

        DeviceCertificatePolicy.EnsureTlsConfiguration(endpoint, true);

        Assert.True(DeviceCertificatePolicy.IsServerCertificateAccepted(endpoint, certificate, SslPolicyErrors.RemoteCertificateChainErrors, true));
        Assert.False(DeviceCertificatePolicy.IsServerCertificateAccepted(endpoint, certificate, SslPolicyErrors.RemoteCertificateChainErrors, false));
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=visicore-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
