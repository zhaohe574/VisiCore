using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/exports").RequireAuthorization().WithTags("录像导出");
        group.MapGet("", async (HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "export.create");
            var manage = await access.HasPermissionAsync(actor.UserId, "export.manage");
            var (_, size, offset) = ApiSupport.Pagination(context);
            var args = new { actor.UserId, manage, size, offset };
            var rows = await db.QueryAsync($"select j.id,j.channel_id,c.name as channel_name,j.start_at as start,j.end_at as end,j.state,j.progress,j.error,j.file_name,j.file_size,j.created_at,j.expires_at from export_jobs j join channels c on c.id=j.channel_id join devices d on d.id=c.device_id left join units un on un.id=c.unit_id left join areas ar on ar.id=un.parent_id where (j.user_id=@userId or @manage) and ({AccessService.ChannelPredicate}) order by j.created_at desc limit @size offset @offset", args);
            var count = await db.OneAsync($"select count(*) as count from export_jobs j join channels c on c.id=j.channel_id join devices d on d.id=c.device_id left join units un on un.id=c.unit_id left join areas ar on ar.id=un.parent_id where (j.user_id=@userId or @manage) and ({AccessService.ChannelPredicate})", args);
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListExports");
        group.MapPost("", async (RecordingRequest request, HttpContext context, Database db, AccessService access, ISettingsStore settings, PlatformOptions options, AuditStore audit) =>
        {
            Rules.TimeRange(request.Start, request.End);
            var actor = ApiSupport.Actor(context);
            var channel = await access.ChannelAsync(actor, request.ChannelId, "export.create");
            var configuration = await settings.ReadAsync();
            var drive = new DriveInfo(Path.GetPathRoot(options.ExportsPath)!);
            Rules.Require(drive.AvailableFreeSpace >= drive.TotalSize / 10, "磁盘剩余空间不足，暂时不能创建导出任务", "export.disk", 409);
            var used = Directory.EnumerateFiles(options.ExportsPath, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
            Rules.Require(used < configuration.ExportQuotaGb * 1024L * 1024L * 1024L, "导出存储配额已用完", "export.quota", 429);
            var id = Guid.NewGuid();
            await db.TransactionAsync(async tx =>
            {
                await tx.ExecuteAsync("select pg_advisory_xact_lock(72002003)");
                var count = await tx.OneAsync("select count(*) as count from export_jobs where user_id=@userId and state in('queued','running')", new { actor.UserId });
                Rules.Require(count.Id("count") < 20, "个人待处理导出任务最多 20 个", "export.queue", 429);
                // Npgsql 的 timestamptz 参数要求零偏移；转换为 UTC 时保留请求对应的实际时刻。
                await tx.ExecuteAsync("insert into export_jobs(id,user_id,auth_session_id,channel_id,device_id,start_at,end_at) values(@id,@userId,@authId,@channelId,@deviceId,@start,@end)", new { id, actor.UserId, authId = actor.SessionId, request.ChannelId, deviceId = channel.Id("deviceId"), start = request.Start.ToUniversalTime(), end = request.End.ToUniversalTime() });
                return true;
            });
            await audit.WriteAsync(actor.UserId, "export.create", id.ToString(), $"通道 {request.ChannelId}，时间 {request.Start:O} 至 {request.End:O}", ApiSupport.Ip(context));
            return Results.Accepted($"/api/v2/exports/{id}", new { id, state = "queued", progress = 0 });
        }).WithName("CreateExport");
        group.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext context, Database db, AccessService access, IDeviceAdapter adapter, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            var job = await OwnedAsync(id, actor, db, access);
            var changed = await db.ExecuteAsync("update export_jobs set state='cancelled',error='用户取消' where id=@id and state in('queued','running')", new { id });
            if (changed > 0)
            {
                try { await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{job.Id("deviceId")}/exports/{id}"); } catch (PlatformException) { }
                await audit.NotifyAsync("export.changed", id.ToString(), job.Id("userId"), job.Id("channelId"));
            }
            return Results.NoContent();
        }).WithName("CancelExport");
        group.MapPost("/{id:guid}/retry", async (Guid id, HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            var job = await OwnedAsync(id, actor, db, access);
            await access.ChannelAsync(actor, job.Id("channelId"), "export.create");
            Rules.Require(job.Text("state") is "failed" or "cancelled", "只有失败或已取消任务可以重试", "export.state", 409);
            Rules.Require(job["workerId"] is null, "上一次导出仍在等待设备确认停止，请稍后重试", "export.stopping", 409);
            var changed = await db.ExecuteAsync("update export_jobs set state='queued',auth_session_id=@authId,error=null,progress=0,worker_id=null,lease_until=null where id=@id and state in('failed','cancelled') and worker_id is null", new { id, authId = actor.SessionId });
            Rules.Require(changed == 1, "导出状态已变化，请刷新后重试", "export.state", 409);
            return Results.Accepted($"/api/v2/exports/{id}", new { id, state = "queued" });
        }).WithName("RetryExport");
        group.MapGet("/{id:guid}/download", async (Guid id, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            var job = await OwnedAsync(id, actor, db, access);
            await access.ChannelAsync(actor, job.Id("channelId"), "export.create");
            Rules.Require(job.Text("state") == "completed" && job["expiresAt"] is not null && job.Time("expiresAt") > DateTimeOffset.UtcNow, "导出文件尚未就绪或已过期", "export.unavailable", 409);
            var path = Path.GetFullPath(job.Text("path"));
            Rules.Require(path.StartsWith(options.ExportsPath + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && File.Exists(path), "导出文件不存在", "export.missing", 404);
            await audit.WriteAsync(actor.UserId, "export.download", id.ToString(), "下载录像文件", ApiSupport.Ip(context));
            return Results.File(path, Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "video/mp4", job.Text("fileName"), enableRangeProcessing: true);
        }).WithName("DownloadExport");
    }

    private static async Task<System.Text.Json.Nodes.JsonObject> OwnedAsync(Guid id, Actor actor, Database db, AccessService access)
    {
        var job = await db.OneAsync("select * from export_jobs where id=@id", new { id }) ?? throw new PlatformException(404, "export.missing", "导出任务不存在");
        Rules.Require(job.Id("userId") == actor.UserId || await access.HasPermissionAsync(actor.UserId, "export.manage"), "不能操作其他用户的导出任务", "export.denied", 403);
        return job;
    }
}
