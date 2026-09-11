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
            var msiFile = form.Files.GetFile("msiFile") ?? form.Files.GetFile("msi") ?? form.Files.FirstOrDefault(f => f.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            var zipFile = form.Files.GetFile("zipFile") ?? form.Files.GetFile("zip") ?? form.Files.FirstOrDefault(f => f.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            var singleFile = form.Files.GetFile("file");

            var version = Rules.Text(form["version"], "版本号", 32).Trim();
            Rules.Require(System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:\.\d+)?$") && Version.TryParse(version, out var parsedVer) && parsedVer.Major >= 0, "版本号格式无效，必须为数字版本号（例如 2.0.0）");

            var minimum = form["minimumVersion"].ToString().Trim();
            if (minimum.Length > 0) Rules.Require(System.Text.RegularExpressions.Regex.IsMatch(minimum, @"^\d+\.\d+\.\d+(?:\.\d+)?$") && Version.TryParse(minimum, out _) && Compare(minimum, version) <= 0, "最低版本必须是有效版本且不高于发布版本");

            var notes = form["releaseNotes"].ToString();
            Rules.Require(notes.Length <= 4096, "更新说明不能超过 4096 个字符");
            var force = bool.TryParse(form["forceUpdate"], out var parsed) && parsed;

            // 双包同时上传分支（一个版本同时上传 MSI 和 ZIP）
            if (msiFile is not null && zipFile is not null && !ReferenceEquals(msiFile, zipFile))
            {
                var existingCount = (await db.OneAsync("select count(*) as count from releases where version=@version", new { version })).Id("count");
                Rules.Require(existingCount == 0, $"版本 {version} 已存在，不能重复创建", "data.duplicate", 409);

                Rules.Require(msiFile.Length is > 0 and <= 1073741824, "MSI 安装包不能为空且不能超过 1 GB");
                var msiFileName = Path.GetFileName(msiFile.FileName);
                Rules.Require(Path.GetExtension(msiFileName).Equals(".msi", StringComparison.OrdinalIgnoreCase), "MSI 安装包扩展名必须为 .msi");

                Rules.Require(zipFile.Length is > 0 and <= 1073741824, "ZIP 便携包不能为空且不能超过 1 GB");
                var zipFileName = Path.GetFileName(zipFile.FileName);
                Rules.Require(Path.GetExtension(zipFileName).Equals(".zip", StringComparison.OrdinalIgnoreCase), "ZIP 便携包扩展名必须为 .zip");

                var msiStorage = Guid.NewGuid().ToString("N") + ".msi";
                var zipStorage = Guid.NewGuid().ToString("N") + ".zip";
                var msiPath = Path.Combine(options.ReleasesPath, msiStorage);
                var zipPath = Path.Combine(options.ReleasesPath, zipStorage);
                try
                {
                    await using (var output = new FileStream(msiPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                        await msiFile.CopyToAsync(output, context.RequestAborted);
                    await using var msiInput = File.OpenRead(msiPath);
                    var msiSha = Convert.ToHexString(await SHA256.HashDataAsync(msiInput, context.RequestAborted));

                    await using (var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                        await zipFile.CopyToAsync(output, context.RequestAborted);
                    await using var zipInput = File.OpenRead(zipPath);
                    var zipSha = Convert.ToHexString(await SHA256.HashDataAsync(zipInput, context.RequestAborted));

                    var msiRow = await db.OneAsync("insert into releases(version,file_name,storage_name,sha256,file_size,release_notes,minimum_version,force_update) values(@version,@fileName,@storage,@sha256,@length,@notes,@minimum,@force) returning *", new { version, fileName = msiFileName, storage = msiStorage, sha256 = msiSha, length = msiFile.Length, notes, minimum = minimum.Length > 0 ? minimum : null, force });
                    var zipRow = await db.OneAsync("insert into releases(version,file_name,storage_name,sha256,file_size,release_notes,minimum_version,force_update) values(@version,@fileName,@storage,@sha256,@length,@notes,@minimum,@force) returning *", new { version, fileName = zipFileName, storage = zipStorage, sha256 = zipSha, length = zipFile.Length, notes, minimum = minimum.Length > 0 ? minimum : null, force = false });

                    await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.upload", $"{msiRow!.Id()},{zipRow!.Id()}", $"上传版本 {version}（MSI+ZIP）", ApiSupport.Ip(context));
                    var res = PublicRow((JsonObject)msiRow!.DeepClone(), options);
                    res["packages"] = new JsonArray(PublicRow((JsonObject)msiRow!.DeepClone(), options), PublicRow((JsonObject)zipRow!.DeepClone(), options));
                    return Results.Created($"/api/v2/releases/{msiRow.Id()}", res);
                }
                catch
                {
                    if (File.Exists(msiPath)) File.Delete(msiPath);
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    throw;
                }
            }

            // 单包兼容分支（保持已有单包上传测试与兼容）
            var file = singleFile ?? msiFile ?? zipFile;
            Rules.Require(file is not null && file.Length is > 0 and <= 1073741824, "安装包不能为空且不能超过 1 GB");
            var fileName = Path.GetFileName(file!.FileName);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            Rules.Require(extension is ".msi" or ".zip", "只接受 MSI 安装包和 ZIP 便携包");
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
        group.MapPut("/{id:long}", async (long id, ReleaseUpdateRequest request, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            var release = await db.OneAsync("select * from releases where id=@id", new { id }) ?? throw new PlatformException(404, "release.missing", "版本不存在");
            Rules.Require(release.Text("status") == "draft", "已发布或已撤回的版本不可修改，仅未发布的草稿允许修改", "release.status");

            var oldVersion = release.Text("version");
            var newVersion = string.IsNullOrWhiteSpace(request.Version) ? oldVersion : request.Version.Trim();
            Rules.Require(System.Text.RegularExpressions.Regex.IsMatch(newVersion, @"^\d+\.\d+\.\d+(?:\.\d+)?$") && Version.TryParse(newVersion, out var pv) && pv.Major >= 0, "版本号格式无效，例如 2.0.0");

            if (newVersion != oldVersion)
            {
                var conflict = await db.OneAsync("select count(*) as count from releases where version=@newVersion and version != @oldVersion", new { newVersion, oldVersion });
                Rules.Require(conflict.Id("count") == 0, $"版本 {newVersion} 已存在", "data.duplicate", 409);
            }

            var minimum = request.MinimumVersion?.Trim();
            if (!string.IsNullOrEmpty(minimum))
            {
                Rules.Require(System.Text.RegularExpressions.Regex.IsMatch(minimum, @"^\d+\.\d+\.\d+(?:\.\d+)?$") && Version.TryParse(minimum, out _) && Compare(minimum, newVersion) <= 0, "最低版本格式无效或高于版本号");
            }

            var notes = request.ReleaseNotes ?? release.Text("releaseNotes");
            Rules.Require(notes.Length <= 4096, "更新说明不能超过 4096 个字符");

            var force = request.ForceUpdate ?? release.Flag("forceUpdate");

            await db.ExecuteAsync(@"update releases 
                set version=@newVersion, release_notes=@notes, minimum_version=@minimum, force_update=(case when lower(file_name) like '%.msi' then @force else false end) 
                where version=@oldVersion and status='draft'", 
                new { newVersion, notes, minimum = string.IsNullOrEmpty(minimum) ? null : minimum, force, oldVersion });

            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.update", id.ToString(), $"修改未发布版本 {oldVersion} -> {newVersion}", ApiSupport.Ip(context));
            var updated = await db.OneAsync("select * from releases where id=@id", new { id });
            return Results.Ok(PublicRow(updated!, options));
        }).WithName("UpdateRelease");
        group.MapDelete("/{id:long}", async (long id, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            var release = await db.OneAsync("select * from releases where id=@id", new { id }) ?? throw new PlatformException(404, "release.missing", "版本不存在");
            Rules.Require(release.Text("status") == "draft", "已发布或已撤回的版本不可删除，仅未发布的草稿允许删除；如需下线请使用撤回", "release.status");

            var version = release.Text("version");
            var draftRows = await db.QueryAsync("select id, storage_name from releases where version=@version and status='draft'", new { version });
            foreach (var row in draftRows)
            {
                var storage = row.Text("storageName");
                if (!string.IsNullOrEmpty(storage))
                {
                    var path = Path.Combine(options.ReleasesPath, storage);
                    if (File.Exists(path))
                    {
                        try { File.Delete(path); } catch { }
                    }
                }
                await db.ExecuteAsync("delete from releases where id=@id", new { id = row.Id() });
            }

            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.delete", id.ToString(), $"删除未发布版本 {version} 及其安装包", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("DeleteRelease");
        group.MapDelete("/version/{version}", async (string version, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            var releases = await db.QueryAsync("select * from releases where version=@version", new { version });
            if (releases.Count == 0) throw new PlatformException(404, "release.missing", "版本不存在");
            Rules.Require(releases.All(r => r.Text("status") == "draft"), "已发布或已撤回的版本不可删除，仅未发布的草稿允许删除；如需下线请使用撤回", "release.status");

            foreach (var row in releases)
            {
                var storage = row.Text("storageName");
                if (!string.IsNullOrEmpty(storage))
                {
                    var path = Path.Combine(options.ReleasesPath, storage);
                    if (File.Exists(path))
                    {
                        try { File.Delete(path); } catch { }
                    }
                }
            }
            await db.ExecuteAsync("delete from releases where version=@version and status='draft'", new { version });
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.delete", version, $"删除未发布版本 {version} 及其安装包", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("DeleteReleaseByVersion");
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
        group.MapPost("/version/{version}/publish", async (string version, PublishRequest request, HttpContext context, Database db, AccessService access, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            var releases = await db.QueryAsync("select * from releases where version=@version", new { version });
            if (releases.Count == 0) throw new PlatformException(404, "release.missing", "版本不存在");
            Rules.Require(releases.Any(r => r.Text("status") == "draft"), "该版本没有待发布的草稿安装包", "release.status");
            if (!string.IsNullOrEmpty(request.MinimumVersion)) Rules.Require(Version.TryParse(request.MinimumVersion, out _) && Compare(request.MinimumVersion, version) <= 0, "最低版本格式无效或高于当前版本");

            await db.ExecuteAsync(@"update releases 
                set status='published', minimum_version=@minimumVersion, force_update=(case when lower(file_name) like '%.msi' then @forceUpdate else false end), published_at=now() 
                where version=@version and status='draft'", 
                new { version, minimumVersion = string.IsNullOrEmpty(request.MinimumVersion) ? null : request.MinimumVersion, request.ForceUpdate });

            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.publish", version, $"发布版本 {version}（全包）", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("PublishReleaseByVersion");
        group.MapPost("/{id:long}/revoke", async (long id, HttpContext context, Database db, AccessService access, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "desktop.release.manage");
            await db.ExecuteAsync("update releases set status='revoked' where id=@id", new { id });
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "release.revoke", id.ToString(), "撤回客户端版本", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("RevokeRelease");
        app.MapGet("/api/v2/public/releases", async (Database db, PlatformOptions options) =>
        {
            var releases = await db.QueryAsync("select * from releases where status='published' order by published_at desc, id desc");
            var grouped = releases.GroupBy(r => r.Text("version"))
                .OrderByDescending(g => Version.TryParse(g.Key, out var v) ? v : new Version());

            var result = new JsonArray();
            foreach (var group in grouped)
            {
                var primary = group.OrderByDescending(r => r.Text("fileName").EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(r => r.Id()).First();
                var item = PublicRow((JsonObject)primary.DeepClone(), options);
                item["packages"] = new JsonArray(group.Select(r => (JsonNode)PublicRow((JsonObject)r.DeepClone(), options)).ToArray());
                result.Add(item);
            }
            return Results.Ok(result);
        }).WithTags("公开下载").WithName("ListPublicReleases");
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
