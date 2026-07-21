using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

/// <summary>
/// 将已确认的计划变成受控 Host 操作，并在每批完成十分钟稳定观察后推进下一批。
/// </summary>
public sealed class UpgradePlanDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<UpgradePlanDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan StableWindow = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "升级计划调度器本轮处理失败。 ");
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    internal async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var exchange = scope.ServiceProvider.GetRequiredService<CoreUpgradeExchange>();
        var backupService = scope.ServiceProvider.GetRequiredService<PlatformBackupService>();
        var plans = await dbContext.UpgradePlans
            .Where(item => item.Status == "running")
            .OrderBy(item => item.RequestedAt)
            .ToListAsync(cancellationToken);

        foreach (var plan in plans)
        {
            var release = await dbContext.ReleaseCatalog.SingleOrDefaultAsync(item => item.Id == plan.ReleaseCatalogId, cancellationToken);
            if (release is null || !ReleaseCatalogService.TryReadDescriptor(release, out var descriptor) || descriptor.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await PausePlanAsync(plan, "release_descriptor_expired", dbContext, cancellationToken);
                continue;
            }

            var targets = await dbContext.UpgradeTargets.Where(item => item.UpgradePlanId == plan.Id).OrderBy(item => item.Batch).ToListAsync(cancellationToken);
            await ReconcileTargetsAsync(plan, targets, dbContext, exchange, cancellationToken);
            if (plan.Status != "running")
            {
                continue;
            }
            foreach (var target in targets.Where(item => item.Status == "queued"))
            {
                if (target.TargetType == "core")
                {
                    await DispatchCoreAsync(plan, target, release, descriptor, backupService, exchange, dbContext, cancellationToken);
                }
                else
                {
                    DispatchEdge(target, release, descriptor, dbContext);
                }
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await AdvanceBatchAsync(plan, targets, dbContext, cancellationToken);
        }
    }

    private static async Task ReconcileTargetsAsync(
        UpgradePlanEntity plan,
        IReadOnlyList<UpgradeTargetEntity> targets,
        PlatformDbContext dbContext,
        CoreUpgradeExchange exchange,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets.Where(item => item.Status is "dispatched" or "verifying"))
        {
            if (target.TargetType == "core")
            {
                var receipt = await exchange.TryReadReceiptAsync(target.PlatformOperationId ?? Guid.Empty, cancellationToken);
                if (receipt is null) continue;
                if (!receipt.Succeeded)
                {
                    target.Status = "failed";
                    target.FailureSummary = receipt.FailureKind;
                    target.CompletedAt = receipt.CompletedAt;
                    await PausePlanAsync(plan, receipt.FailureKind ?? "core_upgrade_failed", dbContext, cancellationToken);
                    continue;
                }
                target.Status = "verifying";
                target.StableSince ??= receipt.CompletedAt;
            }
            else
            {
                var operation = target.PlatformOperationId is null ? null : await dbContext.PlatformOperations.SingleOrDefaultAsync(item => item.Id == target.PlatformOperationId, cancellationToken);
                if (operation is null || operation.Status == "pending") continue;
                if (operation.Status == "failed")
                {
                    target.Status = "failed";
                    target.FailureSummary = "edge_upgrade_failed";
                    target.CompletedAt = operation.CompletedAt ?? DateTimeOffset.UtcNow;
                    if (target.PlatformOperationId is { } sourceOperationId && target.PreviousVersion is not null && target.EdgeAgentId is { } edgeAgentId)
                    {
                        dbContext.PlatformOperations.Add(new PlatformOperationEntity
                        {
                            Id = Guid.NewGuid(),
                            EdgeAgentId = edgeAgentId,
                            OperationType = "rollback",
                            Status = "pending",
                            Summary = $"自动回退升级任务：{sourceOperationId:N}",
                            DetailsJson = JsonSerializer.Serialize(new { sourceOperationId }),
                            RequestedAt = DateTimeOffset.UtcNow
                        });
                    }
                    await PausePlanAsync(plan, "edge_upgrade_failed", dbContext, cancellationToken);
                    continue;
                }
                if (operation.Status == "succeeded" && HasRebootRequired(operation.DetailsJson))
                {
                    target.Status = "reboot_required";
                    target.FailureSummary = "reboot_required";
                    target.CompletedAt = operation.CompletedAt ?? DateTimeOffset.UtcNow;
                    await PausePlanAsync(plan, "reboot_required", dbContext, cancellationToken);
                    continue;
                }
                var agent = target.EdgeAgentId is null ? null : await dbContext.EdgeAgents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == target.EdgeAgentId, cancellationToken);
                if (agent is null || !string.Equals(agent.AgentVersion, target.ExpectedVersion, StringComparison.Ordinal)) continue;
                target.Status = "verifying";
                target.StableSince ??= DateTimeOffset.UtcNow;
            }
        }

        foreach (var target in targets.Where(item => item.Status == "verifying" && item.StableSince <= DateTimeOffset.UtcNow.Subtract(StableWindow)))
        {
            target.Status = "succeeded";
            target.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    private static async Task DispatchCoreAsync(
        UpgradePlanEntity plan,
        UpgradeTargetEntity target,
        ReleaseCatalogEntity release,
        ReleaseDescriptor descriptor,
        PlatformBackupService backupService,
        CoreUpgradeExchange exchange,
        PlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "amd64";
        var artifact = descriptor.FindArtifacts("core", "linux", architecture).SingleOrDefault();
        if (artifact is null)
        {
            target.Status = "failed";
            target.FailureSummary = "core_release_not_available_for_host";
            await PausePlanAsync(plan, target.FailureSummary, dbContext, cancellationToken);
            return;
        }
        try
        {
            var backup = await backupService.CreateAsync("upgrade-protection", cancellationToken);
            var operationId = Guid.NewGuid();
            await exchange.SubmitAsync(new CoreUpgradeMessage(
                operationId,
                descriptor.ProductVersion,
                release.DescriptorJson,
                release.SignatureBase64,
                release.SigningPublicKeyId,
                artifact.ArtifactReference,
                artifact.ArtifactSha256,
                backup.Id), cancellationToken);
            target.PlatformOperationId = operationId;
            target.Status = "dispatched";
            target.StartedAt = DateTimeOffset.UtcNow;
        }
        catch (CoreUpgradeExchangeException exception)
        {
            target.Status = "failed";
            target.FailureSummary = exception.FailureKind;
            await PausePlanAsync(plan, exception.FailureKind, dbContext, cancellationToken);
        }
    }

    private static void DispatchEdge(
        UpgradeTargetEntity target,
        ReleaseCatalogEntity release,
        ReleaseDescriptor descriptor,
        PlatformDbContext dbContext)
    {
        if (target.EdgeAgentId is null)
        {
            target.Status = "failed";
            target.FailureSummary = "upgrade_target_invalid";
            return;
        }
        var operation = new PlatformOperationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = target.EdgeAgentId,
            OperationType = "deployment",
            Status = "pending",
            Summary = $"受控升级到 {descriptor.ProductVersion}",
            DetailsJson = JsonSerializer.Serialize(new
            {
                releaseId = descriptor.ProductVersion,
                releaseDescriptorJson = release.DescriptorJson,
                signatureBase64 = release.SignatureBase64,
                publicKeyId = release.SigningPublicKeyId,
                component = target.Component
            }),
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.PlatformOperations.Add(operation);
        target.PlatformOperationId = operation.Id;
        target.Status = "dispatched";
        target.StartedAt = DateTimeOffset.UtcNow;
    }

    private static async Task AdvanceBatchAsync(UpgradePlanEntity plan, IReadOnlyList<UpgradeTargetEntity> targets, PlatformDbContext dbContext, CancellationToken cancellationToken)
    {
        if (plan.Status != "running" || targets.Any(item => item.Status is "failed" or "rolling_back")) return;
        var activeBatch = targets.Where(item => item.Status is "queued" or "dispatched" or "verifying").Select(item => (int?)item.Batch).Min();
        if (activeBatch is not null) return;
        var nextBatch = targets.Where(item => item.Status == "pending").Select(item => (int?)item.Batch).Min();
        if (nextBatch is null)
        {
            plan.Status = "succeeded";
            plan.CompletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        await dbContext.UpgradeTargets
            .Where(item => item.UpgradePlanId == plan.Id && item.Batch == nextBatch && item.Status == "pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "queued").SetProperty(item => item.StartedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    private static async Task PausePlanAsync(UpgradePlanEntity plan, string failureKind, PlatformDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (plan.Status != "paused")
        {
            dbContext.OutboxEvents.Add(new OutboxEventEntity
            {
                Id = Guid.NewGuid(),
                EventType = "upgrade.plan.failed",
                AggregateType = "upgrade_plan",
                AggregateId = plan.Id,
                PayloadJson = JsonSerializer.Serialize(new UpgradePlanFailedAlertPayload(plan.Id, plan.TargetScope, failureKind, now)),
                OccurredAt = now,
                NextAttemptAt = now
            });
        }
        plan.Status = "paused";
        plan.FailureSummary = failureKind;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool HasRebootRequired(string detailsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            return document.RootElement.TryGetProperty("reason", out var reason) &&
                   reason.ValueKind == JsonValueKind.String &&
                   string.Equals(reason.GetString(), "reboot_required", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record UpgradePlanFailedAlertPayload(
    Guid PlanId,
    string TargetScope,
    string FailureKind,
    DateTimeOffset OccurredAt);
