using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;

/// <summary>
/// 加密平台备份的创建、上传、下载、恢复与删除端点。
/// </summary>
public sealed class BackupEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var backups = endpoints.MapGroup("/api/v1/admin/backups")
            .RequireAuthorization()
            .RequireSystemPermission(SystemPermission.ManageOperations);

        backups.MapGet("/", async (PlatformDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var values = await dbContext.PlatformBackups.AsNoTracking().OrderByDescending(item => item.CreatedAt).Take(200).ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });

        backups.MapPost("/", async (ClaimsPrincipal principal, PlatformBackupService backupService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                var backup = await backupService.CreateAsync("manual", cancellationToken);
                await auditService.WriteAsync(principal, "platform_backup.create", "platform_backup", backup.Id, new { backup.Kind, backup.SizeBytes, backup.Sha256 }, cancellationToken);
                return Results.Created($"/api/v1/admin/backups/{backup.Id}", ToResponse(backup));
            }
            catch (Exception exception)
            {
                return ApiProblems.Create(StatusCodes.Status502BadGateway, "备份创建失败", "backup_create_failed", exception.Message);
            }
        });

        backups.MapPost("/upload", async (IFormFile backup, ClaimsPrincipal principal, PlatformBackupService backupService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                var stored = await backupService.StoreUploadedAsync(backup, cancellationToken);
                await auditService.WriteAsync(principal, "platform_backup.upload", "platform_backup", stored.Id, new { stored.SizeBytes, stored.Sha256 }, cancellationToken);
                return Results.Created($"/api/v1/admin/backups/{stored.Id}", ToResponse(stored));
            }
            catch (InvalidDataException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "备份上传无效", "backup_upload_invalid", exception.Message, new Dictionary<string, string[]> { ["backup"] = [exception.Message] });
            }
        }).DisableAntiforgery();

        backups.MapGet("/{backupId:guid}/download", async (Guid backupId, ClaimsPrincipal principal, PlatformDbContext dbContext, PlatformBackupService backupService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                var backup = await dbContext.PlatformBackups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == backupId, cancellationToken);
                if (backup is null) return Results.NotFound();
                var path = await backupService.GetDownloadPathAsync(backupId, cancellationToken);
                await auditService.WriteAsync(principal, "platform_backup.download", "platform_backup", backupId, new { backup.SizeBytes, backup.Sha256 }, cancellationToken);
                return Results.File(path, "application/octet-stream", backup.FileName, enableRangeProcessing: true);
            }
            catch (FileNotFoundException exception)
            {
                return ApiProblems.Create(StatusCodes.Status410Gone, "备份文件不可用", "backup_file_missing", exception.Message);
            }
        });

        backups.MapPost("/{backupId:guid}/restore", async (Guid backupId, RestorePlatformBackupRequest request, ClaimsPrincipal principal, PlatformBackupService backupService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(request.Confirmation?.Trim(), $"恢复 {backupId}", StringComparison.OrdinalIgnoreCase))
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "恢复确认无效", "backup_restore_confirmation_invalid", errors: new Dictionary<string, string[]> { ["confirmation"] = ["请准确输入恢复确认文本。"] });
            }
            try
            {
                await backupService.RequestRestoreAsync(backupId, request.RecoveryKey ?? string.Empty, principal.Identity?.Name ?? "unknown", cancellationToken);
                await auditService.WriteAsync(principal, "platform_backup.restore.request", "platform_backup", backupId, new { source = "admin" }, cancellationToken);
                return Results.Accepted($"/api/v1/admin/backups/{backupId}", new { state = "restarting" });
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or FileNotFoundException or KeyNotFoundException)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "恢复请求已拒绝", "backup_restore_rejected", exception.Message);
            }
        });

        backups.MapDelete("/{backupId:guid}", async (Guid backupId, ClaimsPrincipal principal, PlatformBackupService backupService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                await backupService.DeleteAsync(backupId, cancellationToken);
                await auditService.WriteAsync(principal, "platform_backup.delete", "platform_backup", backupId, new { }, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }

    private static PlatformBackupResponse ToResponse(PlatformBackupEntity backup) => new(
        backup.Id,
        backup.Kind,
        backup.Status,
        backup.FileName,
        backup.SizeBytes,
        backup.Sha256,
        backup.CreatedAt,
        backup.RetainUntil,
        backup.LastRestoredAt,
        backup.FailureDetail);
}
