using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace VisiCore.Api;

/// <summary>
/// 管理中心 HTTPS 的待应用配置和证书。私钥仅在上传请求和受限文件中短暂存在，
/// 本服务的状态对象、异常和审计调用方均不包含 PEM 内容。
/// </summary>
public sealed class HttpsConfigurationService
{
    private const string CertificatePointerFileName = "certificate.json";
    private readonly HttpsConfigurationPaths paths;
    private readonly HttpsConfigurationSnapshot appliedConfiguration;
    private readonly HttpsCertificateStatus appliedCertificate;

    public HttpsConfigurationService(HttpsConfigurationPaths paths)
    {
        this.paths = paths;
        appliedConfiguration = ReadConfiguration();
        appliedCertificate = ReadCertificateStatus(CertificateSourceForCurrentProcess());
    }

    public static readonly Guid AuditResourceId = new("a7e63a78-2b60-4b1d-837e-e0e10e45f59b");

    public HttpsConfigurationStatus GetStatus()
    {
        var pendingConfiguration = ReadConfiguration();
        var pendingCertificate = ReadCertificateStatus(CertificateSourceForCurrentProcess());
        var restartRequired = !Equals(appliedConfiguration, pendingConfiguration) ||
                              !string.Equals(appliedCertificate.FingerprintSha256, pendingCertificate.FingerprintSha256, StringComparison.OrdinalIgnoreCase) ||
                              !string.Equals(appliedCertificate.Source, pendingCertificate.Source, StringComparison.Ordinal);

        return new HttpsConfigurationStatus(
            appliedConfiguration.Enabled,
            appliedConfiguration.PublicBaseUri,
            pendingConfiguration.Enabled,
            pendingConfiguration.PublicBaseUri,
            appliedCertificate,
            pendingCertificate,
            restartRequired);
    }

    public async Task<HttpsConfigurationStatus> SaveAsync(HttpsConfigurationUpdate request, CancellationToken cancellationToken)
    {
        var configuration = CreateValidatedConfiguration(request.Enabled, request.PublicBaseUri);
        object document = configuration.Enabled
            ? new StoredHttpsConfiguration(
                new StoredHttpsSection(true, configuration.PublicBaseUri),
                new StoredStreamGatewaySection(
                    new Uri(new Uri(configuration.PublicBaseUri!, UriKind.Absolute), "stream/").ToString(),
                    false,
                    [],
                    []),
                new StoredGatewaySection(false, [], []))
            : new StoredDisabledHttpsConfiguration(new StoredHttpsSection(false, null));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        await WriteAtomicallyAsync(paths.ConfigurationPath, bytes, cancellationToken);
        return GetStatus();
    }

    public async Task<HttpsConfigurationStatus> UploadCertificateAsync(Stream certificateStream, Stream privateKeyStream, CancellationToken cancellationToken)
    {
        var certificatePem = await ReadPemAsync(certificateStream, cancellationToken);
        var privateKeyPem = await ReadPemAsync(privateKeyStream, cancellationToken);
        var metadata = ValidateCertificatePair(certificatePem, privateKeyPem);

        Directory.CreateDirectory(paths.UploadedTlsDirectory);
        RestrictDirectory(paths.UploadedTlsDirectory);

        var version = Guid.NewGuid().ToString("N");
        var versionDirectory = Path.Combine(paths.UploadedTlsDirectory, version);
        Directory.CreateDirectory(versionDirectory);
        RestrictDirectory(versionDirectory);
        try
        {
            var certificatePath = Path.Combine(versionDirectory, "tls.crt");
            var privateKeyPath = Path.Combine(versionDirectory, "tls.key");
            await WriteAtomicallyAsync(certificatePath, Encoding.UTF8.GetBytes(certificatePem), cancellationToken);
            await WriteAtomicallyAsync(privateKeyPath, Encoding.UTF8.GetBytes(privateKeyPem), cancellationToken);

            // 从待切换的文件再次读取，避免只验证内存内容而遗漏落盘异常。
            ValidateCertificatePair(await File.ReadAllTextAsync(certificatePath, cancellationToken), await File.ReadAllTextAsync(privateKeyPath, cancellationToken));
            var pointer = JsonSerializer.SerializeToUtf8Bytes(new StoredCertificatePointer(version, metadata), JsonOptions);
            await WriteAtomicallyAsync(Path.Combine(paths.UploadedTlsDirectory, CertificatePointerFileName), pointer, cancellationToken);
        }
        catch
        {
            TryDeleteDirectory(versionDirectory);
            throw;
        }

        return GetStatus();
    }

    public HttpsApplyValidation ValidatePendingForApply()
    {
        var configuration = ReadConfiguration();
        if (!configuration.Enabled)
        {
            return new HttpsApplyValidation(configuration, null);
        }

        var certificate = ReadUploadedCertificate();
        if (certificate is null)
        {
            throw new HttpsConfigurationValidationException("启用 HTTPS 前必须先上传有效的 PEM 证书链和未加密私钥。");
        }

        return new HttpsApplyValidation(configuration, certificate.Metadata);
    }

    private HttpsConfigurationSnapshot ReadConfiguration()
    {
        if (!File.Exists(paths.ConfigurationPath))
        {
            return ReadLegacyConfiguration();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(paths.ConfigurationPath));
            if (!document.RootElement.TryGetProperty("visiCoreHttps", out var section) &&
                !document.RootElement.TryGetProperty("VisiCoreHttps", out section))
            {
                return new HttpsConfigurationSnapshot(false, null);
            }

            var enabled = section.TryGetProperty("enabled", out var enabledValue)
                ? enabledValue.ValueKind == JsonValueKind.True
                : section.TryGetProperty("Enabled", out enabledValue) && enabledValue.ValueKind == JsonValueKind.True;
            var publicBaseUri = section.TryGetProperty("publicBaseUri", out var publicBaseUriValue)
                ? publicBaseUriValue.GetString()
                : section.TryGetProperty("PublicBaseUri", out publicBaseUriValue) ? publicBaseUriValue.GetString() : null;
            return CreateValidatedConfiguration(enabled, publicBaseUri);
        }
        catch (HttpsConfigurationValidationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new HttpsConfigurationValidationException("HTTPS 待应用配置文件无效，拒绝应用该配置。");
        }
    }

    private HttpsConfigurationSnapshot ReadLegacyConfiguration()
    {
        if (!paths.LegacyHttpsEnabled)
        {
            return new HttpsConfigurationSnapshot(false, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(paths.RuntimeConfigurationPath));
            if (!document.RootElement.TryGetProperty("StreamGateway", out var streamGateway) ||
                !streamGateway.TryGetProperty("PublicBaseUri", out var publicStreamUri) ||
                !Uri.TryCreate(publicStreamUri.GetString(), UriKind.Absolute, out var streamUri))
            {
                return new HttpsConfigurationSnapshot(true, null);
            }

            var path = streamUri.AbsolutePath;
            if (path.EndsWith("/stream/", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^"stream/".Length];
            }
            var builder = new UriBuilder(streamUri) { Path = path, Query = string.Empty, Fragment = string.Empty };
            return new HttpsConfigurationSnapshot(true, builder.Uri.GetLeftPart(UriPartial.Authority) + "/");
        }
        catch (Exception)
        {
            return new HttpsConfigurationSnapshot(true, null);
        }
    }

    private HttpsCertificateStatus ReadCertificateStatus(string fallbackSource)
    {
        try
        {
            var uploaded = ReadUploadedCertificate();
            if (uploaded is not null)
            {
                return ToStatus("uploaded", uploaded.Metadata, true);
            }

            if (File.Exists(paths.DeploymentCertificatePath) && File.Exists(paths.DeploymentPrivateKeyPath))
            {
                var metadata = ValidateCertificatePair(
                    File.ReadAllText(paths.DeploymentCertificatePath),
                    File.ReadAllText(paths.DeploymentPrivateKeyPath));
                return ToStatus(fallbackSource, metadata, true);
            }
        }
        catch (HttpsConfigurationValidationException)
        {
            // 外部兼容证书损坏时不回传文件内容；应用阶段会再次校验并拒绝重启。
        }
        catch (CryptographicException)
        {
            // 同上，保持状态接口非敏感。
        }

        return new HttpsCertificateStatus("missing", false, false, null, [], null, null, null, null, null);
    }

    private UploadedCertificate? ReadUploadedCertificate()
    {
        var pointerPath = Path.Combine(paths.UploadedTlsDirectory, CertificatePointerFileName);
        if (!File.Exists(pointerPath))
        {
            return null;
        }

        StoredCertificatePointer? pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<StoredCertificatePointer>(File.ReadAllBytes(pointerPath), JsonOptions);
        }
        catch (Exception)
        {
            throw new HttpsConfigurationValidationException("待应用证书索引无效，拒绝应用 HTTPS 配置。");
        }

        if (pointer is null || !Guid.TryParseExact(pointer.Version, "N", out _))
        {
            throw new HttpsConfigurationValidationException("待应用证书索引无效，拒绝应用 HTTPS 配置。");
        }

        var directory = Path.Combine(paths.UploadedTlsDirectory, pointer.Version);
        var certificatePath = Path.Combine(directory, "tls.crt");
        var privateKeyPath = Path.Combine(directory, "tls.key");
        if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
        {
            throw new HttpsConfigurationValidationException("待应用证书文件不完整，拒绝应用 HTTPS 配置。");
        }

        var metadata = ValidateCertificatePair(File.ReadAllText(certificatePath), File.ReadAllText(privateKeyPath));
        return new UploadedCertificate(pointer.Version, metadata);
    }

    private string CertificateSourceForCurrentProcess() => "deployment";

    private static HttpsConfigurationSnapshot CreateValidatedConfiguration(bool enabled, string? publicBaseUri)
    {
        if (!enabled)
        {
            return new HttpsConfigurationSnapshot(false, null);
        }

        if (string.IsNullOrWhiteSpace(publicBaseUri) ||
            !Uri.TryCreate(publicBaseUri.Trim(), UriKind.Absolute, out var parsed) ||
            !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            parsed.AbsolutePath is not "/" and not "")
        {
            throw new HttpsConfigurationValidationException("HTTPS 公网基础地址必须是无路径、查询参数和片段的 HTTPS 根地址。");
        }

        return new HttpsConfigurationSnapshot(true, parsed.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static HttpsCertificateMetadata ValidateCertificatePair(string certificatePem, string privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(certificatePem) || string.IsNullOrWhiteSpace(privateKeyPem) ||
            !certificatePem.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal) ||
            !privateKeyPem.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            throw new HttpsConfigurationValidationException("证书链或私钥不是有效的 PEM 文件。");
        }

        try
        {
            var chain = new X509Certificate2Collection();
            chain.ImportFromPem(certificatePem);
            if (chain.Count == 0)
            {
                throw new HttpsConfigurationValidationException("证书链不包含可解析的 PEM 证书。");
            }

            using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
            if (!certificate.HasPrivateKey)
            {
                throw new HttpsConfigurationValidationException("私钥与证书不匹配。");
            }

            var now = DateTimeOffset.UtcNow;
            var notBefore = new DateTimeOffset(certificate.NotBefore).ToUniversalTime();
            var notAfter = new DateTimeOffset(certificate.NotAfter).ToUniversalTime();
            if (notAfter <= now)
            {
                throw new HttpsConfigurationValidationException("证书已过期，不能保存或应用。");
            }
            if (notBefore > now)
            {
                throw new HttpsConfigurationValidationException("证书尚未生效，不能保存或应用。");
            }

            return new HttpsCertificateMetadata(
                certificate.Subject,
                ReadSubjectAlternativeNames(certificate),
                certificate.Issuer,
                Convert.ToHexString(SHA256.HashData(certificate.RawData)),
                notBefore,
                notAfter);
        }
        catch (HttpsConfigurationValidationException)
        {
            throw;
        }
        catch (CryptographicException)
        {
            throw new HttpsConfigurationValidationException("证书链无法解析、私钥已加密，或私钥与证书不匹配。");
        }
        catch (ArgumentException)
        {
            throw new HttpsConfigurationValidationException("证书链或私钥不是有效的 PEM 文件。");
        }
    }

    private static IReadOnlyList<string> ReadSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions["2.5.29.17"];
        if (extension is null)
        {
            return [];
        }

        return extension.Format(multiLine: false)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HttpsCertificateStatus ToStatus(string source, HttpsCertificateMetadata metadata, bool privateKeyMatches) =>
        new(source, true, privateKeyMatches, metadata.Subject, metadata.SubjectAlternativeNames, metadata.Issuer,
            metadata.FingerprintSha256, metadata.NotBefore, metadata.NotAfter, null);

    private static async Task<string> ReadPemAsync(Stream stream, CancellationToken cancellationToken)
    {
        const int maxBytes = 1024 * 1024;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > maxBytes)
        {
            throw new HttpsConfigurationValidationException("证书链和私钥文件必须为非空且不超过 1 MiB 的 PEM 文件。");
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer.ToArray());
        }
        catch (DecoderFallbackException)
        {
            throw new HttpsConfigurationValidationException("证书链和私钥文件必须使用 UTF-8 PEM 编码。");
        }
    }

    private static async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("HTTPS 配置路径无效。");
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken);
            RestrictFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            RestrictFile(path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 失败目录没有指针引用，下次上传会使用新的版本目录；不影响当前证书。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed record StoredHttpsConfiguration(
        StoredHttpsSection VisiCoreHttps,
        StoredStreamGatewaySection StreamGateway,
        StoredGatewaySection Gateway);
    private sealed record StoredDisabledHttpsConfiguration(StoredHttpsSection VisiCoreHttps);
    private sealed record StoredHttpsSection(bool Enabled, string? PublicBaseUri);
    private sealed record StoredStreamGatewaySection(
        string PublicBaseUri,
        bool AllowInsecureLoopbackHttpForDevelopment,
        string[] TrustedDevelopmentHttpHosts,
        string[] TrustedInsecureHttpOrigins);
    private sealed record StoredGatewaySection(
        bool AllowInsecureClientHttpForDevelopment,
        string[] TrustedDevelopmentHttpHosts,
        string[] TrustedInsecureHttpOrigins);
    private sealed record StoredCertificatePointer(string Version, HttpsCertificateMetadata Metadata);
    private sealed record UploadedCertificate(string Version, HttpsCertificateMetadata Metadata);
}

public sealed record HttpsConfigurationPaths(
    string ConfigurationPath,
    string UploadedTlsDirectory,
    string DeploymentCertificatePath,
    string DeploymentPrivateKeyPath,
    string RuntimeConfigurationPath,
    bool LegacyHttpsEnabled);

public sealed record HttpsConfigurationUpdate(bool Enabled, string? PublicBaseUri);
public sealed record HttpsConfigurationSnapshot(bool Enabled, string? PublicBaseUri);
public sealed record HttpsCertificateMetadata(
    string Subject,
    IReadOnlyList<string> SubjectAlternativeNames,
    string Issuer,
    string FingerprintSha256,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
public sealed record HttpsCertificateStatus(
    string Source,
    bool Present,
    bool PrivateKeyMatches,
    string? Subject,
    IReadOnlyList<string> SubjectAlternativeNames,
    string? Issuer,
    string? FingerprintSha256,
    DateTimeOffset? NotBefore,
    DateTimeOffset? NotAfter,
    string? Detail);
public sealed record HttpsConfigurationStatus(
    bool CurrentEnabled,
    string? CurrentPublicBaseUri,
    bool PendingEnabled,
    string? PendingPublicBaseUri,
    HttpsCertificateStatus CurrentCertificate,
    HttpsCertificateStatus PendingCertificate,
    bool RestartRequired);
public sealed record HttpsApplyValidation(HttpsConfigurationSnapshot Configuration, HttpsCertificateMetadata? Certificate);

public sealed class HttpsConfigurationValidationException(string message) : Exception(message);
