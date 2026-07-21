using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 边缘运行时可使用的宿主资源。空值表示不施加该项限制。
/// </summary>
public sealed record EdgeNodeResourcePolicy(
    int? CpuLimitPercent = null,
    int? MemoryLimitMiB = null,
    int DiskWarningPercent = 85)
{
    public bool HasHardLimit => CpuLimitPercent is not null || MemoryLimitMiB is not null;

    public bool TryValidate(out string failureKind)
    {
        if (CpuLimitPercent is < 1 or > 100 ||
            MemoryLimitMiB is < 256 or > 4_194_304 ||
            DiskWarningPercent is < 70 or > 95)
        {
            failureKind = "resource_policy_invalid";
            return false;
        }

        failureKind = string.Empty;
        return true;
    }
}

public sealed record EdgeNodeResourcePolicyStatus(
    string EnforcementStatus,
    EdgeNodeResourcePolicy Policy,
    string? FailureKind,
    DateTimeOffset UpdatedAt)
{
    public static EdgeNodeResourcePolicyStatus NotConfigured() => new(
        "not_configured",
        new EdgeNodeResourcePolicy(),
        null,
        DateTimeOffset.UtcNow);
}

public sealed record EdgeNodeResourceSnapshot(
    double ProcessCpuPercent,
    long ProcessMemoryBytes,
    long StateDirectoryTotalBytes,
    long StateDirectoryAvailableBytes,
    bool DiskWarning,
    EdgeNodeResourcePolicy Policy,
    string EnforcementStatus,
    string? EnforcementFailureKind,
    DateTimeOffset SampledAt);

/// <summary>
/// Host Agent 与无特权 Agent 之间共享的非敏感资源状态。它不包含令牌、路径或命令。
/// </summary>
public sealed class EdgeNodeResourcePolicyStatusStore(string path)
{
    private readonly string path = path;

    public EdgeNodeResourcePolicyStatus ReadOrDefault()
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return EdgeNodeResourcePolicyStatus.NotConfigured();
        }

        try
        {
            return JsonSerializer.Deserialize<EdgeNodeResourcePolicyStatus>(File.ReadAllText(path), SerializerOptions)
                   ?? EdgeNodeResourcePolicyStatus.NotConfigured();
        }
        catch (IOException)
        {
            return EdgeNodeResourcePolicyStatus.NotConfigured();
        }
        catch (JsonException)
        {
            return new EdgeNodeResourcePolicyStatus(
                "failed",
                new EdgeNodeResourcePolicy(),
                "resource_status_invalid",
                DateTimeOffset.UtcNow);
        }
    }

    public async Task WriteAsync(EdgeNodeResourcePolicyStatus value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value, SerializerOptions), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

/// <summary>
/// 仅采集 Agent 进程及状态卷信息，避免把宿主机其他工作负载误计入边缘节点。
/// </summary>
public sealed class EdgeNodeResourceMonitor(
    EdgeAgentOptions options,
    EdgeAgentRuntimeState runtimeState,
    EdgeNodeResourcePolicyStatusStore statusStore) : BackgroundService
{
    private TimeSpan? previousCpu;
    private DateTimeOffset? previousSampledAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            runtimeState.SetResources(CaptureSnapshot());
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private EdgeNodeResourceSnapshot CaptureSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        using var process = Process.GetCurrentProcess();
        var cpu = 0d;
        if (previousCpu is { } priorCpu && previousSampledAt is { } priorSample)
        {
            var elapsed = now - priorSample;
            if (elapsed > TimeSpan.Zero)
            {
                cpu = Math.Clamp(
                    (process.TotalProcessorTime - priorCpu).TotalMilliseconds / elapsed.TotalMilliseconds /
                    Math.Max(Environment.ProcessorCount, 1) * 100d,
                    0d,
                    100d);
            }
        }
        previousCpu = process.TotalProcessorTime;
        previousSampledAt = now;

        var total = 0L;
        var available = 0L;
        try
        {
            var statePath = Path.GetFullPath(options.StateDirectory);
            var root = Path.GetPathRoot(statePath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    total = drive.TotalSize;
                    available = drive.AvailableFreeSpace;
                }
            }
        }
        catch (IOException)
        {
            // 资源状态仍应返回进程信息，磁盘数据以 0 表示不可得。
        }
        catch (UnauthorizedAccessException)
        {
            // 状态卷不可访问时不能泄露路径或系统细节。
        }

        var policyStatus = statusStore.ReadOrDefault();
        var policy = policyStatus.Policy ?? options.ResourcePolicy;
        var usedPercent = total <= 0 ? 0d : (double)(total - available) / total * 100d;
        return new EdgeNodeResourceSnapshot(
            Math.Round(cpu, 1, MidpointRounding.AwayFromZero),
            process.WorkingSet64,
            total,
            available,
            usedPercent >= policy.DiskWarningPercent,
            policy,
            policyStatus.EnforcementStatus,
            policyStatus.FailureKind,
            now);
    }
}

public static class LinuxResourcePolicyComposeWriter
{
    public static async Task WriteAsync(string path, EdgeNodeResourcePolicy policy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException("资源覆盖文件路径无效。");
        }
        if (!policy.TryValidate(out _))
        {
            throw new InvalidOperationException("资源策略无效。");
        }

        var lines = new List<string> { "services:" };
        if (!policy.HasHardLimit)
        {
            lines.Add("  edge-node: {}");
        }
        else
        {
            lines.Add("  edge-node:");
            if (policy.CpuLimitPercent is { } cpuPercent)
            {
                var cpus = Math.Max(Environment.ProcessorCount * cpuPercent / 100d, 0.01d);
                lines.Add($"    cpus: \"{cpus.ToString("0.##", CultureInfo.InvariantCulture)}\"");
            }
            if (policy.MemoryLimitMiB is { } memoryLimitMiB)
            {
                lines.Add($"    mem_limit: {memoryLimitMiB.ToString(CultureInfo.InvariantCulture)}m");
            }
        }

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, string.Join('\n', lines) + '\n', cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
