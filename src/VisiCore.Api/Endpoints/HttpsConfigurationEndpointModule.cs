using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VisiCore.Api;

/// <summary>
/// 核心 HTTPS 设置、证书上传和受控重启端点。
/// </summary>
public sealed class HttpsConfigurationEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var https = endpoints.MapGroup("/api/v1/admin/https-configuration")
            .RequireAuthorization()
            .RequireSystemAdministrator();

        https.MapGet("/", (HttpsConfigurationService httpsConfigurationService) =>
        {
            try
            {
                return Results.Ok(httpsConfigurationService.GetStatus());
            }
            catch (HttpsConfigurationValidationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "HTTPS 配置不可用", "https_configuration_unavailable", exception.Message);
            }
        });

        https.MapPut("/", async (HttpsConfigurationUpdate request, ClaimsPrincipal principal, HttpsConfigurationService httpsConfigurationService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await httpsConfigurationService.SaveAsync(request, cancellationToken);
                await auditService.WriteAsync(principal, "https_configuration.save", "https_configuration", HttpsConfigurationService.AuditResourceId, new { status.PendingEnabled, status.PendingPublicBaseUri }, cancellationToken);
                return Results.Ok(status);
            }
            catch (HttpsConfigurationValidationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "HTTPS 配置无效", "https_configuration_invalid", exception.Message, new Dictionary<string, string[]> { ["publicBaseUri"] = [exception.Message] });
            }
            catch (IOException)
            {
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, "HTTPS 配置保存失败", "https_configuration_storage_unavailable", "无法安全写入核心配置卷，请检查中心容器的配置卷权限。");
            }
            catch (UnauthorizedAccessException)
            {
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, "HTTPS 配置保存失败", "https_configuration_storage_unavailable", "无法安全写入核心配置卷，请检查中心容器的配置卷权限。");
            }
        });

        https.MapPost("/certificate", async (HttpContext context, IFormFile? certificate, IFormFile? privateKey, ClaimsPrincipal principal, HttpsConfigurationService httpsConfigurationService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            if (!HttpsCertificateUploadTransportPolicy.Allows(context))
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "拒绝明文私钥上传", "https_certificate_transport_rejected", "证书上传仅允许 HTTPS，或本机回环 HTTP 请求。");
            }
            if (certificate is null || privateKey is null || certificate.Length <= 0 || privateKey.Length <= 0 || certificate.Length > 1024 * 1024 || privateKey.Length > 1024 * 1024)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "证书文件无效", "https_certificate_invalid", errors: new Dictionary<string, string[]> { ["certificate"] = ["请同时提供不超过 1 MiB 的 PEM 证书链和未加密 PEM 私钥。"] });
            }

            try
            {
                await using var certificateStream = certificate.OpenReadStream();
                await using var privateKeyStream = privateKey.OpenReadStream();
                var status = await httpsConfigurationService.UploadCertificateAsync(certificateStream, privateKeyStream, cancellationToken);
                await auditService.WriteAsync(principal, "https_certificate.upload", "https_configuration", HttpsConfigurationService.AuditResourceId, new { status.PendingEnabled, FingerprintSha256 = status.PendingCertificate.FingerprintSha256, ExpiresAt = status.PendingCertificate.NotAfter }, cancellationToken);
                return Results.Ok(status);
            }
            catch (HttpsConfigurationValidationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "证书无效", "https_certificate_invalid", exception.Message, new Dictionary<string, string[]> { ["certificate"] = [exception.Message] });
            }
            catch (IOException)
            {
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, "证书保存失败", "https_certificate_storage_unavailable", "无法安全写入核心配置卷，现有证书未被修改。");
            }
            catch (UnauthorizedAccessException)
            {
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, "证书保存失败", "https_certificate_storage_unavailable", "无法安全写入核心配置卷，现有证书未被修改。");
            }
        }).DisableAntiforgery();

        https.MapPost("/apply", async (ClaimsPrincipal principal, HttpsConfigurationService httpsConfigurationService, AuditService auditService, IHostApplicationLifetime applicationLifetime, CancellationToken cancellationToken) =>
        {
            try
            {
                var validation = httpsConfigurationService.ValidatePendingForApply();
                await auditService.WriteAsync(principal, "https_configuration.apply", "https_configuration", HttpsConfigurationService.AuditResourceId, new { validation.Configuration.Enabled, validation.Configuration.PublicBaseUri, FingerprintSha256 = validation.Certificate?.FingerprintSha256, ExpiresAt = validation.Certificate?.NotAfter }, cancellationToken);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    applicationLifetime.StopApplication();
                });
                return Results.Accepted("/api/v1/admin/https-configuration", new { state = "restarting" });
            }
            catch (HttpsConfigurationValidationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "HTTPS 配置不能应用", "https_configuration_apply_rejected", exception.Message);
            }
        });
    }
}
