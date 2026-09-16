<#
.SYNOPSIS
采样 VisiCore 桌面端的 CPU、GPU、内存与媒体会话负载，产出可比较的 JSON 报告。

.DESCRIPTION
用于「桌面 2.1.0 性能优化」前后的基线对比。脚本只做只读采样：
  - CPU：Process.TotalProcessorTime 相邻采样差值 / 墙钟 / 逻辑核数。
  - GPU：'\GPU Engine(*)\Utilization Percentage' 中属于目标进程 PID 的引擎实例求和。
  - 内存：Working Set 与 Private Memory。
  - 媒体会话：桌面日志中的活动会话记录数量（live/playback 合计）。
  - UI 延迟：本机未接入运行时探针时记 null，不伪造为 0。

以管理员身份运行可获得更完整的 GPU 引擎实例；无权限时脚本会记录 gpuAvailable=false。

.EXAMPLE
pwsh -File tools/v2-desktop-profile.ps1 -Label "16路-优化前" -DurationSeconds 180 -Notes "主码流"
#>
[CmdletBinding()]
param(
    [string]$ProcessName = 'VideoPlatform.Desktop',
    [int]$DurationSeconds = 180,
    [int]$SampleSeconds = 5,
    [string]$Label = '',
    [string]$Notes = '',
    [string]$OutputDirectory = '',
    [string]$LogPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-OutputDirectory {
    param([string]$Requested)
    $root = if ([string]::IsNullOrWhiteSpace($Requested)) {
        Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/v2-desktop-profile'
    } else { $Requested }
    if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Path $root -Force | Out-Null }
    return (Resolve-Path -LiteralPath $root).Path
}

function Get-DesktopLogPath {
    param([string]$Requested)
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    return Join-Path $env:LOCALAPPDATA 'VideoPlatform/desktop.log'
}

function Get-ActiveSessionCount {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $file = Join-Path (Split-Path -Parent $Path) 'active_sessions.json'
        if (-not (Test-Path -LiteralPath $file)) { return 0 }
        $map = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        return @($map.PSObject.Properties).Count
    } catch { return $null }
}

function Get-GpuPercent {
    param([int]$ProcessId, [bool]$Available)
    if (-not $Available) { return $null }
    try {
        $samples = Get-Counter -Counter '\GPU Engine(*)\Utilization Percentage' -SampleInterval 1 -MaxSamples 1 -ErrorAction Stop
        $total = 0.0
        foreach ($sample in $samples.CounterSamples) {
            # 实例名形如 pid_1234_luid_0x00000000_0x0000BEEF_phys_0_eng_0_engtype_3D
            if ($sample.InstanceName -match "^pid_$ProcessId`_") { $total += [double]$sample.CookedValue }
        }
        return [math]::Round($total, 2)
    } catch { return $null }
}

function Get-Percentile {
    param([double[]]$Values, [double]$Percentile)
    if ($null -eq $Values -or $Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $index = [math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $sorted.Count) { $index = $sorted.Count - 1 }
    return [math]::Round($sorted[$index], 2)
}

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $process) {
    Write-Error "未找到进程 $ProcessName。请先启动桌面端并完成登录与分屏加载，再运行本脚本。"
    exit 2
}

$directory = Resolve-OutputDirectory -Requested $OutputDirectory
$desktopLog = Get-DesktopLogPath -Requested $LogPath
$logicalCores = [Environment]::ProcessorCount

$gpuAvailable = $false
try {
    $null = Get-Counter -ListSet 'GPU Engine' -ErrorAction Stop
    $gpuAvailable = $true
} catch { $gpuAvailable = $false }

Write-Host "目标进程：$ProcessName (PID $($process.Id))"
Write-Host "逻辑核数：$logicalCores；采样：$SampleSeconds 秒 × $([math]::Floor($DurationSeconds / $SampleSeconds)) 次"
Write-Host "GPU 计数器：$(if ($gpuAvailable) { '可用' } else { '不可用（将记录 gpuAvailable=false）' })"
Write-Host "输出目录：$directory"
Write-Host ''

$samples = New-Object System.Collections.Generic.List[object]
$cpuSamples = New-Object System.Collections.Generic.List[double]
$gpuSamples = New-Object System.Collections.Generic.List[double]
$startedAt = Get-Date
$previousCpu = $process.TotalProcessorTime
$previousAt = [datetime]::UtcNow
$iterations = [math]::Max(1, [math]::Floor($DurationSeconds / $SampleSeconds))

for ($i = 1; $i -le $iterations; $i++) {
    Start-Sleep -Seconds $SampleSeconds
    $process.Refresh()
    if ($process.HasExited) { Write-Warning '目标进程已退出，采样提前结束。'; break }

    $now = [datetime]::UtcNow
    $elapsed = ($now - $previousAt).TotalSeconds
    $cpuDelta = ($process.TotalProcessorTime - $previousCpu).TotalSeconds
    $previousCpu = $process.TotalProcessorTime
    $previousAt = $now

    $cpuPercent = if ($elapsed -gt 0) { [math]::Round(100.0 * $cpuDelta / $elapsed / $logicalCores, 2) } else { $null }
    $gpuPercent = Get-GpuPercent -ProcessId $process.Id -Available $gpuAvailable
    $sessions = Get-ActiveSessionCount -Path $desktopLog

    if ($null -ne $cpuPercent) { $cpuSamples.Add([double]$cpuPercent) }
    if ($null -ne $gpuPercent) { $gpuSamples.Add([double]$gpuPercent) }

    $row = [pscustomobject]@{
        index            = $i
        at               = $now.ToString('o')
        cpuPercent       = $cpuPercent
        gpuPercent       = $gpuPercent
        workingSetMb     = [math]::Round($process.WorkingSet64 / 1MB, 1)
        privateMemoryMb  = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        threadCount      = $process.Threads.Count
        handleCount      = $process.HandleCount
        activeSessions   = $sessions
    }
    $samples.Add($row)
    Write-Host ("[{0,3}/{1}] CPU {2,6}%  GPU {3,6}%  内存 {4,7} MB  线程 {5,4}  句柄 {6,6}  会话 {7}" -f `
        $i, $iterations, $cpuPercent, $gpuPercent, $row.workingSetMb, $row.threadCount, $row.handleCount, $sessions)
}

$summary = [pscustomobject]@{
    label            = $Label
    notes            = $Notes
    processName      = $ProcessName
    processId        = $process.Id
    startedAt        = $startedAt.ToString('o')
    durationSeconds  = $DurationSeconds
    sampleSeconds    = $SampleSeconds
    logicalCores     = $logicalCores
    gpuAvailable     = $gpuAvailable
    cpuPercentAvg    = if ($cpuSamples.Count -gt 0) { [math]::Round(($cpuSamples | Measure-Object -Average).Average, 2) } else { $null }
    cpuPercentP95    = Get-Percentile -Values $cpuSamples.ToArray() -Percentile 95
    cpuPercentMax    = if ($cpuSamples.Count -gt 0) { [math]::Round(($cpuSamples | Measure-Object -Maximum).Maximum, 2) } else { $null }
    gpuPercentAvg    = if ($gpuSamples.Count -gt 0) { [math]::Round(($gpuSamples | Measure-Object -Average).Average, 2) } else { $null }
    gpuPercentP95    = Get-Percentile -Values $gpuSamples.ToArray() -Percentile 95
    gpuPercentMax    = if ($gpuSamples.Count -gt 0) { [math]::Round(($gpuSamples | Measure-Object -Maximum).Maximum, 2) } else { $null }
    workingSetMbMax  = if ($samples.Count -gt 0) { ($samples | Measure-Object -Property workingSetMb -Maximum).Maximum } else { $null }
    handleCountMax   = if ($samples.Count -gt 0) { ($samples | Measure-Object -Property handleCount -Maximum).Maximum } else { $null }
    activeSessions   = Get-ActiveSessionCount -Path $desktopLog
    uiLatencyP95Ms   = $null
    uiLatencyNote    = '未接入运行时 UI 延迟探针；如需该指标需在桌面端启用诊断采样。'
    samples          = $samples.ToArray()
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$safeLabel = if ([string]::IsNullOrWhiteSpace($Label)) { 'run' } else { ($Label -replace '[^\w\u4e00-\u9fff-]', '_') }
$target = Join-Path $directory "$stamp-$safeLabel.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $target -Encoding UTF8

Write-Host ''
Write-Host '==== 采样汇总 ===='
Write-Host "标签：$Label"
Write-Host "CPU  ：平均 $($summary.cpuPercentAvg)%  P95 $($summary.cpuPercentP95)%  峰值 $($summary.cpuPercentMax)%"
Write-Host "GPU  ：平均 $($summary.gpuPercentAvg)%  P95 $($summary.gpuPercentP95)%  峰值 $($summary.gpuPercentMax)%"
Write-Host "内存 ：峰值 $($summary.workingSetMbMax) MB；句柄峰值 $($summary.handleCountMax)；活动会话 $($summary.activeSessions)"
Write-Host "报告：$target"
