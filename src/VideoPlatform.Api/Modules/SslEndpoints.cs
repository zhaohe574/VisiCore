using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class SslEndpoints
{
    public static void MapSslEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/ssl").RequireAuthorization().WithTags("SSL与域名管控");

        // 总览
        group.MapGet("/overview", async (HttpContext context, SslService ssl) =>
            Results.Ok(await ssl.OverviewAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<SslOverviewDto>().WithName("GetSslOverview");

        // 域名/网址管理
        group.MapGet("/domains", async (HttpContext context, SslService ssl) =>
            Results.Ok(await ssl.DomainsAsync(ApiSupport.Actor(context), ApiSupport.Search(context), context.RequestAborted)))
            .Produces<IReadOnlyList<SslDomainDto>>().WithName("GetSslDomains");

        group.MapPost("/domains", async (HttpContext context, SslDomainRequest request, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            var result = await ssl.SaveDomainAsync(actor, null, request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/ssl/domains/{result.Id}", result);
        }).Produces<SslDomainDto>(201).WithName("CreateSslDomain");

        group.MapPut("/domains/{id:long}", async (HttpContext context, long id, SslDomainRequest request, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            var result = await ssl.SaveDomainAsync(actor, id, request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(result);
        }).Produces<SslDomainDto>().WithName("UpdateSslDomain");

        group.MapPut("/domains/{id:long}/primary", async (HttpContext context, long id, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            var result = await ssl.SetPrimaryDomainAsync(actor, id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(result);
        }).Produces<SslDomainDto>().WithName("SetPrimarySslDomain");

        group.MapDelete("/domains/{id:long}", async (HttpContext context, long id, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            await ssl.DeleteDomainAsync(actor, id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("DeleteSslDomain");

        // 证书管理
        group.MapGet("/certificates", async (HttpContext context, SslService ssl) =>
            Results.Ok(await ssl.CertificatesAsync(ApiSupport.Actor(context), ApiSupport.Search(context), context.RequestAborted)))
            .Produces<IReadOnlyList<SslCertificateDto>>().WithName("GetSslCertificates");

        group.MapGet("/certificates/{id:long}", async (HttpContext context, long id, SslService ssl) =>
            Results.Ok(await ssl.CertificateByIdAsync(ApiSupport.Actor(context), id, context.RequestAborted)))
            .Produces<SslCertificateDto>().WithName("GetSslCertificate");

        group.MapPost("/certificates", async (HttpContext context, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            SslCertificateUploadRequest uploadRequest;

            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var name = form["name"].ToString();
                var certFile = form.Files.GetFile("certFile") ?? form.Files.GetFile("cert") ?? form.Files.FirstOrDefault(f => f.FileName.EndsWith(".crt", StringComparison.OrdinalIgnoreCase) || f.FileName.EndsWith(".cer", StringComparison.OrdinalIgnoreCase) || f.FileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase));
                var keyFile = form.Files.GetFile("keyFile") ?? form.Files.GetFile("key") ?? form.Files.FirstOrDefault(f => f.FileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase));

                var certPem = form["certPem"].ToString();
                var keyPem = form["keyPem"].ToString();

                if (certFile is not null)
                {
                    Rules.Require(certFile.Length is > 0 and <= 2097152, "证书文件大小不能超过 2 MB");
                    using var stream = certFile.OpenReadStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    certPem = await reader.ReadToEndAsync(context.RequestAborted);
                }

                if (keyFile is not null)
                {
                    Rules.Require(keyFile.Length is > 0 and <= 2097152, "私钥文件大小不能超过 2 MB");
                    using var stream = keyFile.OpenReadStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    keyPem = await reader.ReadToEndAsync(context.RequestAborted);
                }

                if (string.IsNullOrWhiteSpace(name) && certFile is not null)
                {
                    name = Path.GetFileNameWithoutExtension(certFile.FileName);
                }

                uploadRequest = new SslCertificateUploadRequest(name, certPem, keyPem);
            }
            else
            {
                var jsonBody = await context.Request.ReadFromJsonAsync<SslCertificateUploadRequest>(JsonDefaults.Options, context.RequestAborted);
                Rules.Require(jsonBody is not null, "请求正文无效");
                uploadRequest = jsonBody!;
            }

            var result = await ssl.UploadCertificateAsync(actor, uploadRequest, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/ssl/certificates/{result.Id}", result);
        }).Produces<SslCertificateDto>(201).WithName("UploadSslCertificate");

        group.MapPut("/certificates/{id:long}/active", async (HttpContext context, long id, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            var result = await ssl.SetActiveCertificateAsync(actor, id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(result);
        }).Produces<SslCertificateDto>().WithName("SetActiveSslCertificate");

        group.MapDelete("/certificates/{id:long}", async (HttpContext context, long id, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            await ssl.DeleteCertificateAsync(actor, id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("DeleteSslCertificate");

        group.MapGet("/certificates/{id:long}/download", async (HttpContext context, long id, SslService ssl) =>
        {
            var actor = ApiSupport.Actor(context);
            var (fileName, bytes) = await ssl.DownloadCertificateAsync(actor, id, context.RequestAborted);
            return Results.File(bytes, "application/x-x509-ca-cert", fileName);
        }).WithName("DownloadSslCertificate");

        // Nginx 配置预览
        group.MapGet("/nginx-config", async (HttpContext context, SslService ssl) =>
            Results.Ok(await ssl.GenerateNginxConfigAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<NginxConfigDto>().WithName("GetNginxConfig");
    }
}
