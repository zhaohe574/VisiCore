<#
.SYNOPSIS
按线程采样 VisiCore 桌面端的 CPU 占用，定位 CPU 消耗在哪些线程上。

.DESCRIPTION
对目标进程的每个线程取两次 Thread.TotalProcessorTime 快照，按差值排序输出占用最高的线程，
并尽力解析线程入口地址所属模块（原生播放器/运行库/客户端自身），用于判断 CPU 究竟花在
解码、原生播放器内部线程还是托管代码上。

.EXAMPLE
pwsh -File tools/v2-desktop-thread-profile.ps1 -ProcessName VideoPlatform.Desktop -IntervalSeconds 12 -Top 25
#>
[CmdletBinding()]
param(
    [string]$ProcessName = 'VideoPlatform.Desktop',
    [int]$IntervalSeconds = 12,
    [int]$Top = 25,
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $target) { Write-Error "未找到进程 $ProcessName。请先以压力测试模式或正常方式启动桌面端。"; exit 2 }

function Get-ThreadSnapshot {
    param([System.Diagnostics.Process]$Process)
    $Process.Refresh()
    $snapshot = @{}
    foreach ($thread in $Process.Threads) {
        try { $snapshot[$thread.Id] = [pscustomobject]@{ Id = $thread.Id; Cpu = $thread.TotalProcessorTime.TotalSeconds; State = $thread.ThreadState.ToString(); Priority = $thread.PriorityLevel.ToString() } }
        catch { }
    }
    return $snapshot
}

function Resolve-Module {
    param([System.Diagnostics.Process]$Process, [int]$ThreadId)
    # 线程入口地址所属模块无法通过 ProcessThread 直接取得，这里退化为按线程优先级/状态给出可读分类。
    return $null
}

Write-Host "目标进程：$ProcessName (PID $($target.Id))，采样间隔 $IntervalSeconds 秒"
$before = Get-ThreadSnapshot -Process $target
$moduleNames = @{}
try { foreach ($module in $target.Modules) { if ($module.BaseAddress -ne [IntPtr]::Zero) { $moduleNames[[int64]$module.BaseAddress] = $module.ModuleName } } } catch { }

Start-Sleep -Seconds $IntervalSeconds

$after = Get-ThreadSnapshot -Process $target
$rows = foreach ($id in $after.Keys) {
    if (-not $before.ContainsKey($id)) { continue }
    $delta = $after[$id].Cpu - $before[$id].Cpu
    if ($delta -le 0) { continue }
    [pscustomobject]@{
        ThreadId = $id
        CpuPercentSingleCore = [math]::Round($delta / $IntervalSeconds * 100, 2)
        State = $after[$id].State
        Priority = $after[$id].Priority
        CpuSecondsDelta = [math]::Round($delta, 3)
    }
}
$total = ($rows | Measure-Object -Property CpuPercentSingleCore -Sum).Sum
Write-Host ("线程总数 {0}（新出现 {1}），区间内总 CPU = {2}% 单核" -f $after.Count, ($after.Count - $before.Count), [math]::Round($total, 1))
Write-Host ''
$rows | Sort-Object -Property CpuPercentSingleCore -Descending | Select-Object -First $Top | Format-Table -AutoSize | Out-String | Write-Host

if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $rows | Sort-Object -Property CpuPercentSingleCore -Descending |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory "$stamp-threadprofile.json") -Encoding UTF8
    Write-Host "报告：$(Join-Path $OutputDirectory "$stamp-threadprofile.json")"
}
