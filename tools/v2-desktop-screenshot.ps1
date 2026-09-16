<#
.SYNOPSIS
抓取 VisiCore 桌面端主窗口截图，用于人工核对浅色/深色主题的可读性与布局。

.DESCRIPTION
按主窗口句柄抓取窗口矩形内的屏幕内容（含顶栏、状态栏等 WPF 外层），这是 RenderTargetBitmap
渲染图无法覆盖的部分。配合 VISICORE_STRESS 压力测试模式使用可自动完成登录与分屏加载。

.EXAMPLE
# 先以压力测试模式启动客户端，再抓图
pwsh -File tools/v2-desktop-screenshot.ps1 -Output artifacts/v2-desktop-profile/shots/light-4ch.png
#>
[CmdletBinding()]
param(
    [string]$ProcessName = 'VideoPlatform.Desktop',
    [Parameter(Mandatory = $true)][string]$Output,
    [int]$WaitSeconds = 35
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('VisiCoreShot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VisiCoreShot {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
}

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($null -eq $process) {
    Write-Host "未找到带主窗口的 $ProcessName，等待 $WaitSeconds 秒后重试..."
    for ($i = 0; $i -lt $WaitSeconds; $i++) {
        Start-Sleep -Seconds 1
        $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($process) { break }
    }
}
if ($null -eq $process) { Write-Error "仍未找到 $ProcessName 主窗口，放弃截图。"; exit 2 }

$process.Refresh()
$handle = $process.MainWindowHandle
Write-Host "目标：$ProcessName (PID $($process.Id)) 标题「$($process.MainWindowTitle)」"

[VisiCoreShot]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Seconds 2

$rect = New-Object VisiCoreShot+RECT
[VisiCoreShot]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { Write-Error "窗口矩形无效：${width}x${height}"; exit 3 }

$directory = Split-Path -Parent $Output
if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
Write-Host "已保存：$Output（${width}x${height}）"
