using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class PublicOfflineDeviceService(PlatformDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<PublicOfflineDeviceListResponse> ListAsync(
        PublicOfflineDeviceQuery query,
        CancellationToken cancellationToken)
    {
        var regions = await dbContext.Regions.AsNoTracking().ToListAsync(cancellationToken);
        var regionPaths = BuildRegionPaths(regions);
        var candidates = new List<OfflineDeviceCandidate>();

        var cameras = await dbContext.Cameras.AsNoTracking()
            .Where(item => item.Connectivity == CameraConnectivity.Offline)
            .Select(item => new
            {
                item.Alias,
                item.RegionId,
                item.LastStateChangedAt,
                item.SuspectedAt,
                item.LastVerifiedAt,
                item.CreatedAt
            })
            .ToListAsync(cancellationToken);
        candidates.AddRange(cameras.Select(item => new OfflineDeviceCandidate(
            item.Alias,
            RegionName(regionPaths, item.RegionId),
            DeviceKinds.Camera,
            ResolveOfflineSince(item.LastStateChangedAt, item.SuspectedAt, item.LastVerifiedAt, item.CreatedAt))));

        var recorders = await dbContext.Recorders.AsNoTracking()
            .Where(item => item.Connectivity == CameraConnectivity.Offline && item.DeviceKind != DeviceKinds.Camera)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.DeviceKind,
                item.LastStateChangedAt,
                item.SuspectedAt,
                item.LastVerifiedAt,
                item.CreatedAt
            })
            .ToListAsync(cancellationToken);
        if (recorders.Count > 0)
        {
            var recorderIds = recorders.Select(item => item.Id).ToList();
            var regionByRecorder = await dbContext.DeviceWorkerAssignments.AsNoTracking()
                .Where(item => recorderIds.Contains(item.RecorderId))
                .ToDictionaryAsync(item => item.RecorderId, item => item.DefaultRegionId, cancellationToken);
            candidates.AddRange(recorders.Select(item => new OfflineDeviceCandidate(
                item.Name,
                regionByRecorder.TryGetValue(item.Id, out var regionId) ? RegionName(regionPaths, regionId) : PublicOfflineDeviceConstants.UnassignedRegion,
                NormalizeDeviceType(item.DeviceKind),
                ResolveOfflineSince(item.LastStateChangedAt, item.SuspectedAt, item.LastVerifiedAt, item.CreatedAt))));
        }

        var availableRegions = candidates
            .Select(item => item.Region)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var availableDeviceTypes = candidates
            .Select(item => item.DeviceType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IEnumerable<OfflineDeviceCandidate> filtered = candidates;
        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            filtered = filtered.Where(item => item.Region.Equals(query.Region, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            filtered = filtered.Where(item => item.Name.Contains(query.Name, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.DeviceType))
        {
            filtered = filtered.Where(item => item.DeviceType.Equals(query.DeviceType, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderBy(item => item.OfflineSince)
            .ThenBy(item => item.Region, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var now = timeProvider.GetUtcNow();
        var items = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(item => new PublicOfflineDeviceResponse(
                item.Name,
                item.Region,
                item.DeviceType,
                Math.Max(0, (long)Math.Floor((now - item.OfflineSince).TotalSeconds))))
            .ToList();

        return new PublicOfflineDeviceListResponse(
            now,
            ordered.Count,
            query.Page,
            query.PageSize,
            items,
            availableRegions,
            availableDeviceTypes);
    }

    private static DateTimeOffset ResolveOfflineSince(
        DateTimeOffset? lastStateChangedAt,
        DateTimeOffset? suspectedAt,
        DateTimeOffset? lastVerifiedAt,
        DateTimeOffset createdAt) => lastStateChangedAt ?? suspectedAt ?? lastVerifiedAt ?? createdAt;

    private static string NormalizeDeviceType(string value) => DeviceKinds.Known.Contains(value)
        ? value.Trim().ToLowerInvariant()
        : DeviceKinds.Other;

    private static string RegionName(IReadOnlyDictionary<Guid, string> regionPaths, Guid regionId) =>
        regionPaths.TryGetValue(regionId, out var path) ? path : PublicOfflineDeviceConstants.UnassignedRegion;

    private static IReadOnlyDictionary<Guid, string> BuildRegionPaths(IReadOnlyList<RegionEntity> regions)
    {
        var regionById = regions.ToDictionary(item => item.Id);
        var paths = new Dictionary<Guid, string>();
        foreach (var region in regions)
        {
            if (paths.ContainsKey(region.Id))
            {
                continue;
            }

            var names = new Stack<string>();
            var visited = new HashSet<Guid>();
            var current = region;
            while (visited.Add(current.Id))
            {
                names.Push(current.Name);
                if (current.ParentId is not { } parentId || !regionById.TryGetValue(parentId, out var parent))
                {
                    break;
                }
                current = parent;
            }
            paths[region.Id] = string.Join(" / ", names);
        }
        return paths;
    }

    private sealed record OfflineDeviceCandidate(string Name, string Region, string DeviceType, DateTimeOffset OfflineSince);
}

public sealed record PublicOfflineDeviceQuery(
    string? Region,
    string? Name,
    string? DeviceType,
    int Page = 1,
    int PageSize = PublicOfflineDeviceConstants.DefaultPageSize)
{
    public static bool TryCreate(
        string? region,
        string? name,
        string? deviceType,
        int? page,
        int? pageSize,
        out PublicOfflineDeviceQuery query,
        out string error)
    {
        query = new PublicOfflineDeviceQuery(null, null, null);
        error = string.Empty;
        if (!TryNormalizeFilter(region, "区域筛选条件", out var normalizedRegion, out error) ||
            !TryNormalizeFilter(name, "名称筛选条件", out var normalizedName, out error) ||
            !TryNormalizeFilter(deviceType, "设备类型筛选条件", out var normalizedDeviceType, out error))
        {
            return false;
        }
        if (normalizedDeviceType is not null && !DeviceKinds.Known.Contains(normalizedDeviceType))
        {
            error = "设备类型筛选条件无效。";
            return false;
        }

        var normalizedPage = page ?? 1;
        var normalizedPageSize = pageSize ?? PublicOfflineDeviceConstants.DefaultPageSize;
        if (normalizedPage is < 1 or > PublicOfflineDeviceConstants.MaximumPage)
        {
            error = "页码超出允许范围。";
            return false;
        }
        if (normalizedPageSize is < 1 or > PublicOfflineDeviceConstants.MaximumPageSize)
        {
            error = "每页数量超出允许范围。";
            return false;
        }

        query = new PublicOfflineDeviceQuery(
            normalizedRegion,
            normalizedName,
            normalizedDeviceType?.ToLowerInvariant(),
            normalizedPage,
            normalizedPageSize);
        return true;
    }

    private static bool TryNormalizeFilter(string? value, string label, out string? normalized, out string error)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        error = string.Empty;
        if (normalized is not null && normalized.Length > PublicOfflineDeviceConstants.MaximumFilterLength)
        {
            error = $"{label}不能超过 {PublicOfflineDeviceConstants.MaximumFilterLength} 个字符。";
            return false;
        }
        return true;
    }
}

public sealed record PublicOfflineDeviceListResponse(
    DateTimeOffset GeneratedAt,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<PublicOfflineDeviceResponse> Items,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> DeviceTypes);

public sealed record PublicOfflineDeviceResponse(
    string Name,
    string Region,
    string DeviceType,
    long OfflineDurationSeconds);

public static class PublicOfflineDeviceConstants
{
    public const string UnassignedRegion = "未归属";
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;
    public const int MaximumPage = 1_000;
    public const int MaximumFilterLength = 128;
}
