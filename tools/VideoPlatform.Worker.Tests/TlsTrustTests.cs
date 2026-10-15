using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VideoPlatform.Infrastructure;
using Xunit;

namespace VideoPlatform.Worker.Tests;

/// <summary>
/// TLS 信任策略的指纹语义测试。
///
/// 存在意义：生产环境曾出现"已登记 PLATFORM_TRUSTED_CERT_SHA256 指纹，但自签名证书
/// 仍被拒绝"的故障。根因是 <c>X509Certificate2.Thumbprint</c> 返回的是 **SHA-1**，
/// 而设置名与运维用 <c>openssl x509 -fingerprint -sha256</c> 取得的都是 **SHA-256**，
/// 二者永不相等。此前的验证用例在两侧都用了 Thumbprint（同为 SHA-1），
/// 因而"内部自洽但语义错误"，未能暴露缺陷。
///
/// 本测试显式断言：SHA-256 指纹匹配成功、SHA-1 指纹**必须**不匹配。
/// </summary>
public sealed class TlsTrustTests
{
    private static X509Certificate2 CreateSelfSigned(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Fact]
    public void Sha256FingerprintMatchesOpenSslFormat()
    {
        using var cert = CreateSelfSigned("trusted.local");
        var expected = cert.GetCertHashString(HashAlgorithmName.SHA256);   // 与 SslService 一致的既有写法
        var actual = TlsTrust.Sha256Fingerprint(cert.RawData);
        Assert.Equal(expected, actual);
        // 大写十六进制、无分隔符（与 Normalize 的输出格式一致）
        Assert.Equal(actual.ToUpperInvariant(), actual);
        Assert.DoesNotContain(":", actual);
        Assert.Equal(64, actual.Length);
    }

    [Fact]
    public void PinnedSha256FingerprintIsAcceptedWhileOthersAreRejected()
    {
        using var trusted = CreateSelfSigned("trusted.local");
        using var other = CreateSelfSigned("attacker.local");

        var pinned = TlsTrust.PinnedCallback([TlsTrust.Normalize(trusted.GetCertHashString(HashAlgorithmName.SHA256))]);
        Assert.NotNull(pinned);

        // 链错误（自签名）时，仅登记指纹的证书放行。
        const SslPolicyErrors chainError = SslPolicyErrors.RemoteCertificateChainErrors;
        Assert.True(pinned(null!, trusted, null!, chainError));
        Assert.False(pinned(null!, other, null!, chainError));
        Assert.False(pinned(null!, null, null!, chainError));

        // 名称不匹配也应由指纹放行（桌面端连 IP 而证书只含域名时的关键路径）。
        Assert.True(pinned(null!, trusted, null!, SslPolicyErrors.RemoteCertificateNameMismatch));

        // 系统信任链通过时始终放行。
        Assert.True(pinned(null!, other, null!, SslPolicyErrors.None));
    }

    /// <summary>
    /// 回归防护：登记 SHA-1 指纹**不应**被接受 —— 这正是曾经的生产缺陷。
    /// 若将来有人把实现改回 <c>X509Certificate2.Thumbprint</c>，本用例会立即失败。
    /// </summary>
    [Fact]
    public void Sha1ThumbprintMustNotBeAcceptedAsSha256Fingerprint()
    {
        using var cert = CreateSelfSigned("trusted.local");
        var sha1 = TlsTrust.Normalize(cert.Thumbprint);                     // SHA-1，40 字符
        var sha256 = TlsTrust.Sha256Fingerprint(cert.RawData);              // SHA-256，64 字符
        Assert.NotEqual(sha1, sha256);
        Assert.Equal(40, sha1.Length);

        // 只登记 SHA-1 时，链错误的证书必须被拒绝（不得因"指纹相等"而放行）。
        var pinnedWithSha1Only = TlsTrust.PinnedCallback([sha1]);
        Assert.NotNull(pinnedWithSha1Only);
        Assert.False(pinnedWithSha1Only(null!, cert, null!, SslPolicyErrors.RemoteCertificateChainErrors));

        // 登记正确的 SHA-256 后才放行。
        var pinnedWithSha256 = TlsTrust.PinnedCallback([sha256]);
        Assert.NotNull(pinnedWithSha256);
        Assert.True(pinnedWithSha256(null!, cert, null!, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void EmptyConfigurationYieldsNoCallbackSoSystemTrustChainApplies()
    {
        Assert.Null(TlsTrust.PinnedCallback([]));
    }

    [Fact]
    public void FingerprintComparisonToleratesColonsSpacesAndCase()
    {
        using var cert = CreateSelfSigned("trusted.local");
        var sha256 = cert.GetCertHashString(HashAlgorithmName.SHA256);
        var colonSeparated = string.Join(":", Enumerable.Range(0, sha256.Length / 2).Select(i => sha256.Substring(i * 2, 2))).ToLowerInvariant();

        var pinned = TlsTrust.PinnedCallback([colonSeparated]);
        Assert.NotNull(pinned);
        Assert.True(pinned(null!, cert, null!, SslPolicyErrors.RemoteCertificateChainErrors));
    }
}
