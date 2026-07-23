using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

/// <summary>
/// 创建与推进固定三批灰度计划。真正的宿主升级仍由独立 Host Agent 在本机验签后执行。
/// </summary>
public sealed class UpgradePlanService(PlatformDbContext dbContext)
{
    public async Task<UpgradePlanEntity> CreateAsync(
        Guid releaseCatalogId,
        string targetScope,
        IReadOnlyCollection<Guid> selectedEdgeAgentIds,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var release = await dbContext.ReleaseCatalog.SingleOrDefaultAsync(
            item => item.Id == releaseCatalogId && item.Status == "available", cancellationToken)
            ?? throw new UpgradePlanException("release_not_available");
        if (!ReleaseCatalogService.TryReadDescriptor(release, out var descriptor) || descriptor.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new UpgradePlanException("release_descriptor_expired");
        }
        if (targetScope is not ("core" or "edge"))
        {
            throw new UpgradePlanException("upgrade_target_scope_invalid");
        }

        var now = DateTimeOffset.UtcNow;
        var plan = new UpgradePlanEntity
        {
            Id = Guid.NewGuid(),
            ReleaseCatalogId = release.Id,
            TargetScope = targetScope,
            Status = "draft",
            RequestedBy = string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : requestedBy[..Math.Min(128, requestedBy.Length)],
            RequestedAt = now
        };
        dbContext.UpgradePlans.Add(plan);

        if (targetScope == "core")
        {
            var component = ResolveCoreArtifact(descriptor);
            var activeAgentVersions = await dbContext.EdgeAgents.AsNoTracking()
                .Where(item => item.DisabledAt == null)
                .Select(item => item.AgentVersion)
                .ToListAsync(cancellationToken);
            if (activeAgentVersions.Any(version => !IsVersionAtLeast(version, descriptor.MinimumEdgeVersion)))
            {
                throw new UpgradePlanException("core_upgrade_edge_incompatible");
            }
            dbContext.UpgradeTargets.Add(new UpgradeTargetEntity
            {
                Id = Guid.NewGuid(),
                UpgradePlanId = plan.Id,
                TargetType = "core",
                Component = component.Component,
                Batch = 1,
                Status = "pending",
                ExpectedVersion = descriptor.ProductVersion,
                RequestedAt = now
            });
        }
        else
        {
            if (selectedEdgeAgentIds.Count == 0)
            {
                throw new UpgradePlanException("upgrade_targets_required");
            }
            var agents = await dbContext.EdgeAgents
                .Where(item => selectedEdgeAgentIds.Contains(item.Id) && item.DisabledAt == null)
                .OrderBy(item => item.Name)
                .ToListAsync(cancellationToken);
            if (agents.Count != selectedEdgeAgentIds.Count)
            {
                throw new UpgradePlanException("upgrade_target_not_found");
            }
            if (agents.Any(item => item.LastSeenAt < now.AddMinutes(-2)))
            {
                throw new UpgradePlanException("upgrade_target_offline");
            }
            if (agents.Any(item => !IsHostUpgradeReady(item.CapabilitiesJson)))
            {
                throw new UpgradePlanException("upgrade_target_initialization_required");
            }

            var batches = ResolveBatches(agents.Count);
            for (var index = 0; index < agents.Count; index++)
            {
                var agent = agents[index];
                var component = agent.Platform == "windows" ? "edge-windows" : "edge-docker";
                var architecture = GetArchitecture(agent.CapabilitiesJson);
                var artifact = descriptor.FindArtifacts(component, agent.Platform, architecture).SingleOrDefault();
                if (artifact is null)
                {
                    throw new UpgradePlanException("upgrade_target_incompatible");
                }
                dbContext.UpgradeTargets.Add(new UpgradeTargetEntity
                {
                    Id = Guid.NewGuid(),
                    UpgradePlanId = plan.Id,
                    EdgeAgentId = agent.Id,
                    TargetType = "edge",
                    Component = component,
                    Batch = batches[index],
                    Status = "pending",
                    ExpectedVersion = descriptor.ProductVersion,
                    PreviousVersion = agent.AgentVersion,
                    PreviousArtifactJson = JsonSerializer.Serialize(new { agent.AgentVersion, agent.Platform, architecture }),
                    RequestedAt = now
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    public async Task<UpgradePlanEntity> StartAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await dbContext.UpgradePlans.SingleOrDefaultAsync(item => item.Id == planId, cancellationToken)
            ?? throw new UpgradePlanException("upgrade_plan_not_found");
        // 失败或人工暂停后的计划不能直接越过当前批次继续执行，必须先处理回退并重新创建计划。
        if (plan.Status != "draft")
        {
            throw new UpgradePlanException("upgrade_plan_not_startable");
        }

        plan.Status = "running";
        plan.StartedAt ??= DateTimeOffset.UtcNow;
        var firstBatch = await dbContext.UpgradeTargets
            .Where(item => item.UpgradePlanId == plan.Id && item.Status == "pending")
            .OrderBy(item => item.Batch)
            .Select(item => (int?)item.Batch)
            .FirstOrDefaultAsync(cancellationToken);
        if (firstBatch is null)
        {
            throw new UpgradePlanException("upgrade_plan_has_no_targets");
        }
        await dbContext.UpgradeTargets
            .Where(item => item.UpgradePlanId == plan.Id && item.Batch == firstBatch && item.Status == "pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "queued")
                .SetProperty(item => item.StartedAt, DateTimeOffset.UtcNow), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    public async Task PauseAsync(Guid planId, string failureSummary, CancellationToken cancellationToken)
    {
        var plan = await dbContext.UpgradePlans.SingleOrDefaultAsync(item => item.Id == planId, cancellationToken)
            ?? throw new UpgradePlanException("upgrade_plan_not_found");
        plan.Status = "paused";
        plan.FailureSummary = string.IsNullOrWhiteSpace(failureSummary) ? "upgrade_paused" : failureSummary[..Math.Min(512, failureSummary.Length)];
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 金丝雀批次稳定后，后续批次必须由运维人员明确确认，避免调度器自动扩大影响范围。
    /// </summary>
    public async Task<UpgradePlanEntity> ApproveBatchAsync(Guid planId, int batch, CancellationToken cancellationToken)
    {
        if (batch <= 1)
        {
            throw new UpgradePlanException("upgrade_batch_invalid");
        }

        var plan = await dbContext.UpgradePlans.SingleOrDefaultAsync(item => item.Id == planId, cancellationToken)
            ?? throw new UpgradePlanException("upgrade_plan_not_found");
        if (plan.Status != "running")
        {
            throw new UpgradePlanException("upgrade_plan_not_running");
        }

        var activeBatch = await dbContext.UpgradeTargets
            .Where(item => item.UpgradePlanId == plan.Id &&
                           (item.Status == "queued" || item.Status == "dispatched" || item.Status == "verifying"))
            .Select(item => (int?)item.Batch)
            .MinAsync(cancellationToken);
        if (activeBatch is not null)
        {
            throw new UpgradePlanException("upgrade_batch_still_active");
        }

        var expectedBatch = await dbContext.UpgradeTargets
            .Where(item => item.UpgradePlanId == plan.Id && item.Status == "awaiting_approval")
            .Select(item => (int?)item.Batch)
            .MinAsync(cancellationToken);
        if (expectedBatch is null || expectedBatch != batch)
        {
            throw new UpgradePlanException("upgrade_batch_not_waiting_approval");
        }

        var targets = await dbContext.UpgradeTargets
            .Where(item => item.UpgradePlanId == plan.Id && item.Batch == batch && item.Status == "awaiting_approval")
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var target in targets)
        {
            target.Status = "queued";
            target.StartedAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return plan;
    }

    private static ReleaseArtifactDescriptor ResolveCoreArtifact(ReleaseDescriptor descriptor)
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            ? "arm64"
            : "amd64";
        return descriptor.FindArtifacts("core", "linux", architecture).SingleOrDefault()
            ?? throw new UpgradePlanException("core_release_not_available_for_host");
    }

    private static int[] ResolveBatches(int count)
    {
        var result = new int[count];
        if (count == 0) return result;
        result[0] = 1;
        var secondBatchCount = count == 1 ? 0 : Math.Max(1, (int)Math.Ceiling(count * 0.1));
        for (var index = 1; index < count; index++)
        {
            result[index] = index <= secondBatchCount ? 2 : 3;
        }
        return result;
    }

    private static string GetArchitecture(string capabilitiesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(capabilitiesJson);
            if (document.RootElement.TryGetProperty("architecture", out var architecture) && architecture.ValueKind == JsonValueKind.String)
            {
                return architecture.GetString() switch
                {
                    "x64" => "amd64",
                    "amd64" => "amd64",
                    "arm64" => "arm64",
                    _ => string.Empty
                };
            }
        }
        catch (JsonException)
        {
            // 无效能力声明会在兼容性检查中被拒绝。
        }
        return string.Empty;
    }

    private static bool IsHostUpgradeReady(string capabilitiesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(capabilitiesJson);
            return document.RootElement.TryGetProperty("hostUpgradeReady", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsVersionAtLeast(string value, string minimum)
    {
        var normalizedValue = value.Split('-', 2)[0];
        var normalizedMinimum = minimum.Split('-', 2)[0];
        return Version.TryParse(normalizedValue, out var current) && Version.TryParse(normalizedMinimum, out var required) && current >= required;
    }
}

public sealed class UpgradePlanException(string failureKind) : InvalidOperationException(failureKind)
{
    public string FailureKind { get; } = failureKind;
}
