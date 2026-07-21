using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 独立于业务 Agent 的资源治理器。Host Agent 即使配置了资源上限，也不受自身策略影响。
/// </summary>
public sealed class HostResourcePolicyWorker(
    HostAgentOptions options,
    ILogger<HostResourcePolicyWorker> logger) : BackgroundService
{
    private readonly EdgeNodeResourcePolicyStatusStore statusStore = new(
        options.ManagedResourcePolicyStatusPath ?? string.Empty);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            await WriteStatusAsync("disabled", options.ResourcePolicy, null, stoppingToken);
            return;
        }

        if (!options.ResourcePolicy.TryValidate(out var validationFailure))
        {
            await WriteStatusAsync("failed", options.ResourcePolicy, validationFailure, stoppingToken);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await ApplyLinuxPolicyAsync(stoppingToken);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            await ApplyWindowsPolicyLoopAsync(stoppingToken);
            return;
        }

        await WriteStatusAsync("unsupported", options.ResourcePolicy, "resource_platform_unsupported", stoppingToken);
    }

    private async Task ApplyLinuxPolicyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.ResourcePolicyComposeOverridePath))
            {
                await WriteStatusAsync("failed", options.ResourcePolicy, "resource_override_path_invalid", cancellationToken);
                return;
            }

            await LinuxResourcePolicyComposeWriter.WriteAsync(
                options.ResourcePolicyComposeOverridePath,
                options.ResourcePolicy,
                cancellationToken);
            await WriteStatusAsync(
                options.ResourcePolicy.HasHardLimit ? "applied" : "unlimited",
                options.ResourcePolicy,
                null,
                cancellationToken);
        }
        catch (IOException)
        {
            await WriteStatusAsync("failed", options.ResourcePolicy, "resource_override_write_failed", cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            await WriteStatusAsync("failed", options.ResourcePolicy, "resource_override_write_failed", cancellationToken);
        }
    }

    private async Task ApplyWindowsPolicyLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var job = WindowsEdgeResourceJob.Open();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var processId = WindowsEdgeResourceJob.TryGetServiceProcessId("VisiCore Edge Agent");
                    if (processId is null)
                    {
                        await WriteStatusAsync("awaiting_agent", options.ResourcePolicy, null, stoppingToken);
                    }
                    else
                    {
                        WindowsEdgeResourceJob.Apply(job, processId.Value, options.ResourcePolicy);
                        await WriteStatusAsync(
                            options.ResourcePolicy.HasHardLimit ? "applied" : "unlimited",
                            options.ResourcePolicy,
                            null,
                            stoppingToken);
                    }
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogWarning("Windows 边缘资源策略未能生效，失败类别 {FailureKind}。", exception.Message);
                    await WriteStatusAsync("failed", options.ResourcePolicy, "resource_job_apply_failed", stoppingToken);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    await WriteStatusAsync("failed", options.ResourcePolicy, "resource_job_apply_failed", stoppingToken);
                }

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
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Windows 边缘资源治理器不可用，失败类别 {FailureKind}。", exception.Message);
            await WriteStatusAsync("failed", options.ResourcePolicy, "resource_job_create_failed", stoppingToken);
        }
    }

    private Task WriteStatusAsync(string status, EdgeNodeResourcePolicy policy, string? failureKind, CancellationToken cancellationToken) =>
        statusStore.WriteAsync(new EdgeNodeResourcePolicyStatus(status, policy, failureKind, DateTimeOffset.UtcNow), cancellationToken);
}

internal static class WindowsEdgeResourceJob
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const int JobObjectCpuRateControlInformationClass = 15;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectCpuRateControlEnable = 0x00000001;
    private const uint JobObjectCpuRateControlHardCap = 0x00000004;

    public static SafeFileHandle Open()
    {
        var handle = CreateJobObject(IntPtr.Zero, "VisiCore.EdgeAgent.ResourcePolicy");
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new InvalidOperationException("resource_job_create_failed");
        }
        return handle;
    }

    public static uint? TryGetServiceProcessId(string serviceName)
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) return null;
        try
        {
            var service = OpenService(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero) return null;
            try
            {
                var length = Marshal.SizeOf<ServiceStatusProcess>();
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, (uint)length, out _)) return null;
                    var status = Marshal.PtrToStructure<ServiceStatusProcess>(buffer);
                    return status.ProcessId == 0 ? null : status.ProcessId;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static void Apply(SafeFileHandle job, uint processId, EdgeNodeResourcePolicy policy)
    {
        var extended = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = policy.MemoryLimitMiB is null ? 0u : JobObjectLimitJobMemory
            },
            JobMemoryLimit = policy.MemoryLimitMiB is { } memoryLimitMiB
                ? (UIntPtr)((ulong)memoryLimitMiB * 1024UL * 1024UL)
                : UIntPtr.Zero
        };
        SetInformation(job, JobObjectExtendedLimitInformationClass, extended);

        var cpu = new JobObjectCpuRateControlInformation
        {
            ControlFlags = policy.CpuLimitPercent is null
                ? 0u
                : JobObjectCpuRateControlEnable | JobObjectCpuRateControlHardCap,
            CpuRate = policy.CpuLimitPercent is { } cpuLimitPercent
                ? (uint)(cpuLimitPercent * 100)
                : 0u
        };
        SetInformation(job, JobObjectCpuRateControlInformationClass, cpu);

        using var process = Process.GetProcessById((int)processId);
        if (!AssignProcessToJobObject(job, process.Handle))
        {
            throw new InvalidOperationException("resource_job_assign_failed");
        }
    }

    private static void SetInformation<T>(SafeFileHandle job, int informationClass, T value) where T : struct
    {
        var length = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            if (!SetInformationJobObject(job, informationClass, buffer, (uint)length))
            {
                throw new InvalidOperationException("resource_job_configuration_failed");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectCpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }
}
