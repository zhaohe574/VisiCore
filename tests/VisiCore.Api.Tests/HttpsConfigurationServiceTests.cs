using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using VisiCore.Api;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class HttpsConfigurationServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"visicore-https-tests-{Guid.NewGuid():N}");

    public HttpsConfigurationServiceTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task 保存启用配置后只写入非敏感运行时覆盖()
    {
        var service = CreateService();

        var status = await service.SaveAsync(new HttpsConfigurationUpdate(true, "https://center.example.test"), CancellationToken.None);
        var stored = await File.ReadAllTextAsync(Path.Combine(directory, "https-configuration.json"));

        Assert.True(status.PendingEnabled);
        Assert.True(status.RestartRequired);
        Assert.Equal("https://center.example.test/", status.PendingPublicBaseUri);
        Assert.Contains("https://center.example.test/stream/", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("password", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 上传匹配的有效证书会保留待应用元信息()
    {
        var service = CreateService();
        var pair = CreateCertificatePair(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));

        var status = await UploadAsync(service, pair.CertificatePem, pair.PrivateKeyPem);

        Assert.True(status.PendingCertificate.Present);
        Assert.True(status.PendingCertificate.PrivateKeyMatches);
        Assert.Equal("uploaded", status.PendingCertificate.Source);
        Assert.NotNull(status.PendingCertificate.FingerprintSha256);
        Assert.DoesNotContain("PRIVATE KEY", status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 过期证书会被拒绝且不生成待应用证书()
    {
        var service = CreateService();
        var pair = CreateCertificatePair(DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddMinutes(-1));

        var exception = await Assert.ThrowsAsync<HttpsConfigurationValidationException>(() =>
            UploadAsync(service, pair.CertificatePem, pair.PrivateKeyPem));

        Assert.Contains("过期", exception.Message);
        Assert.False(File.Exists(Path.Combine(directory, "tls", "certificate.json")));
    }

    [Fact]
    public async Task 不匹配私钥会被拒绝且保留旧证书指针()
    {
        var service = CreateService();
        var validPair = CreateCertificatePair(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        var otherPair = CreateCertificatePair(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        await UploadAsync(service, validPair.CertificatePem, validPair.PrivateKeyPem);
        var pointerPath = Path.Combine(directory, "tls", "certificate.json");
        var oldPointer = await File.ReadAllTextAsync(pointerPath);

        await Assert.ThrowsAsync<HttpsConfigurationValidationException>(() =>
            UploadAsync(service, validPair.CertificatePem, otherPair.PrivateKeyPem));

        Assert.Equal(oldPointer, await File.ReadAllTextAsync(pointerPath));
    }

    [Fact]
    public async Task 禁用后运行时覆盖不再包含公网流地址()
    {
        var service = CreateService();
        await service.SaveAsync(new HttpsConfigurationUpdate(true, "https://center.example.test/"), CancellationToken.None);

        await service.SaveAsync(new HttpsConfigurationUpdate(false, "https://ignored.example.test/"), CancellationToken.None);
        var stored = await File.ReadAllTextAsync(Path.Combine(directory, "https-configuration.json"));

        Assert.Contains("\"enabled\":false", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamGateway", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stream/", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 旧环境变量启用时会显示首次安装的HTTPS地址()
    {
        var runtimePath = Path.Combine(directory, "runtime.json");
        await File.WriteAllTextAsync(runtimePath, "{\"StreamGateway\":{\"PublicBaseUri\":\"https://center.example.test/stream/\"}}");
        var service = new HttpsConfigurationService(new HttpsConfigurationPaths(
            Path.Combine(directory, "https-configuration.json"),
            Path.Combine(directory, "tls"),
            Path.Combine(directory, "deployment", "tls.crt"),
            Path.Combine(directory, "deployment", "tls.key"),
            runtimePath,
            true));

        var status = service.GetStatus();

        Assert.True(status.CurrentEnabled);
        Assert.Equal("https://center.example.test/", status.CurrentPublicBaseUri);
    }

    [Fact]
    public void Docker桌面本机回环HTTP上传会被允许()
    {
        var context = CreateUploadContext("127.0.0.1:8080", "192.168.65.1", "http");

        Assert.True(HttpsCertificateUploadTransportPolicy.Allows(context));
    }

    [Fact]
    public void 局域网明文HTTP上传会被拒绝()
    {
        var context = CreateUploadContext("192.168.1.20:8080", "192.168.1.24", "http");

        Assert.False(HttpsCertificateUploadTransportPolicy.Allows(context));
    }

    [Fact]
    public void HTTPS代理上传会被允许()
    {
        var context = CreateUploadContext("center.example.test", "192.168.1.24", "https");

        Assert.True(HttpsCertificateUploadTransportPolicy.Allows(context));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private HttpsConfigurationService CreateService() => new(new HttpsConfigurationPaths(
        Path.Combine(directory, "https-configuration.json"),
        Path.Combine(directory, "tls"),
        Path.Combine(directory, "deployment", "tls.crt"),
        Path.Combine(directory, "deployment", "tls.key"),
        Path.Combine(directory, "runtime.json"),
        false));

    private static Task<HttpsConfigurationStatus> UploadAsync(HttpsConfigurationService service, string certificatePem, string privateKeyPem) =>
        service.UploadCertificateAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(certificatePem)),
            new MemoryStream(Encoding.UTF8.GetBytes(privateKeyPem)),
            CancellationToken.None);

    private static DefaultHttpContext CreateUploadContext(string host, string forwardedAddress, string forwardedProtocol)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = HostString.FromUriComponent(host);
        context.Request.Headers["X-Forwarded-For"] = forwardedAddress;
        context.Request.Headers["X-Forwarded-Proto"] = forwardedProtocol;
        return context;
    }

    private static (string CertificatePem, string PrivateKeyPem) CreateCertificatePair(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=visicore.example.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }
}
