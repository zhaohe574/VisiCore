using System.Security.Cryptography;
using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class ReleaseEndpoints
{
    public static void MapReleaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/releases").RequireAuthorization().WithTags("客户端发布");
        group.MapGet("", async (HttpContext context, Database db, AccessService access, PlatformOptions options) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            var (_, size, offset) = ApiSupport.Pagination(context);
            var rows = await db.QueryAsync("select * from releases order by created_at desc limit @size offset @offset", new { size, offset });
            foreach (var row in rows) PublicRow(row, options);
            var count = await db.OneAsync("select count(*) as count from releases");
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListReleases");
        group.MapPost("", async (HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            Rules.Require(context.Request.HasFormContentType, "请选择安装包");
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            Rules.Require(file is not null && file.Length is > 0 and <= 1073741824, "安装包不能为空且不能超过 1 GB");
            var fileName = Path.GetFileName(file!.FileName);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            Rules.Require(extension is ".msi" or ".zip", "只接受 MSI 安装包和 ZIP 便携包");
            var version = Rules.Text(form["version"], "版本号", 32);
            Rules.Require(Version.TryParse(version, out _), "版本号格式无效");
            var minimum = form["minimumVersion"].ToString();
            if (minimum.Length > 0) Rules.Require(Version.TryParse(minimum, out _) && Compare(minimum, version) <= 0, "最低版本必须是有效版本且不高于发布版本");
            var notes = form["releaseNotes"].ToString();
            Rules.Require(notes.Length <= 4096, "更新说明不能超过 4096 个字符");
            var force = bool.TryParse(form["forceUpdate"], out var parsed) && parsed;
            Rules.Require(!force || extension == ".msi", "强制更新只支持 MSI 安装包");
            var storage = Guid.NewGuid().ToString("N") + extension;
            var path = Path.Combine(options.ReleasesPath, storage);
            try
            {
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    await file.CopyToAsync(output, context.RequestAborted);
                await using var input = File.OpenRead(path);
                var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(input, context.RequestAborted));
                var row = await db.OneAsync("insert into releases(version,file_name,storage_name,sha256,file_size,release_notes,minimum_version,force_update) values(@version,@fileName,@storage,@sha256,@length,@notes,@minimum,@force) returning *", new { version, fileName, storage, sha256, length = file.Length, notes, minimum = minimum.Length > 0 ? minimum : null, force });
                await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.upload", row.Id().ToString(), $"上传版本 {version}", ApiSupport.Ip(context));
                return Results.Created($"/api/v2/releases/{row.Id()}", PublicRow(row!, options));
            }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
        }).DisableAntiforgery().WithName("UploadRelease");
        group.MapPost("/{id:long}/publish", async (long id, PublishRequest request, HttpContext context, Database db, AccessService access, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            var release = await db.OneAsync("select * from releases where id=@id", new { id }) ?? throw new PlatformException(404, "release.missing", "版本不存在");
            if (!string.IsNullOrEmpty(request.MinimumVersion)) Rules.Require(Version.TryParse(request.MinimumVersion, out _) && Compare(request.MinimumVersion, release.Text("version")) <= 0, "最低版本格式无效或高于当前版本");
            Rules.Require(!request.ForceUpdate || release.Text("fileName").EndsWith(".msi", StringComparison.OrdinalIgnoreCase), "强制升级只支持 MSI 安装包");
            await db.ExecuteAsync("update releases set status='published',minimum_version=@minimumVersion,force_update=@forceUpdate,published_at=now() where id=@id", new { id, minimumVersion = string.IsNullOrEmpty(request.MinimumVersion) ? null : request.MinimumVersion, request.ForceUpdate });
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.publish", id.ToString(), $"发布版本 {release.Text("version")}", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("PublishRelease");
        group.MapPost("/{id:long}/revoke", async (long id, HttpContext context, Database db, AccessService access, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            await db.ExecuteAsync("update releases set status='revoked' where id=@id", new { id });
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.revoke", id.ToString(), "撤回客户端版本", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("RevokeRelease");
        app.MapGet("/api/v2/public/releases/latest", LatestAsync).WithTags("公开下载").WithName("GetLatestRelease");
        app.MapGet("/api/desktop-releases/latest", LatestAsync).ExcludeFromDescription();
        app.MapGet("/api/v2/releases/{id:long}/download", DownloadAsync).WithTags("公开下载").WithName("DownloadRelease");
        app.MapGet("/api/desktop-releases/{id:long}/download", DownloadAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> LatestAsync(Database db, PlatformOptions options, string? currentVersion, string? packageType)
    {
        var releases = await db.QueryAsync("select * from releases where status='published'");
        Rules.Require(packageType is null or "msi" or "zip", "安装包类型只支持 msi 或 zip");
        var latest = releases.Where(r => packageType is null || r.Text("fileName").EndsWith("." + packageType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => Version.TryParse(r.Text("version"), out var v) ? v : new Version())
            .ThenByDescending(r => r.Text("fileName").EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.Id()).FirstOrDefault();
        if (latest is null) return Results.NoContent();
        PublicRow(latest, options);
        latest["packages"] = new JsonArray(releases.Where(r => Compare(r.Text("version"), latest.Text("version")) == 0)
            .Select(r => (JsonNode)PublicRow((JsonObject)r.DeepClone(), options)).ToArray());
        latest["updateAvailable"] = currentVersion is null || Compare(latest.Text("version"), currentVersion) > 0;
        if (currentVersion is not null && !string.IsNullOrEmpty(latest.Text("minimumVersion")))
            latest["forceUpdate"] = latest.Flag("forceUpdate") || Compare(currentVersion, latest.Text("minimumVersion")) < 0;
        return Results.Ok(latest);
    }

    private static async Task<IResult> DownloadAsync(long id, HttpContext context, Database db, PlatformOptions options, AccessService access)
    {
        var release = await db.OneAsync("select * from releases where id=@id", new { id }) ?? throw new PlatformException(404, "release.missing", "安装包不存在");
        if (release.Text("status") != "published")
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "desktop.release.manage");
        }
        var path = Path.Combine(options.ReleasesPath, Path.GetFileName(release.Text("storageName")));
        Rules.Require(File.Exists(path), "安装包文件不存在", "release.file", 404);
        await db.ExecuteAsync("update releases set download_count=download_count+1 where id=@id", new { id });
        return Results.File(path, "application/octet-stream", release.Text("fileName"), enableRangeProcessing: true);
    }

    public static JsonObject PublicRow(JsonObject row, PlatformOptions options)
    {
        row.Remove("storageName");
        row["downloadUrl"] = $"{options.PublicBase}/api/v2/releases/{row.Id()}/download";
        return row;
    }
    public static int Compare(string left, string right)
    {
        static Version Normalize(string value)
        {
            if (!Version.TryParse(value, out var v)) return new Version(0, 0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }
        return Normalize(left).CompareTo(Normalize(right));
    }
}
