# 安装 VisiCore 本地 Git 钩子（提交前凭据检查）。
#
# 用法：pwsh -File tools/install-git-hooks.ps1
# 卸载：Remove-Item .git/hooks/pre-commit
#
# 说明：Git 钩子位于 .git/hooks/，不进入版本控制，因此每位协作者需各自执行一次。

$ErrorActionPreference = 'Stop'
$root = (& git rev-parse --show-toplevel 2>$null)
if (-not $root) { throw '当前目录不在 Git 仓库中。' }

$source = Join-Path $root 'tools/git-hooks/pre-commit'
$target = Join-Path $root '.git/hooks/pre-commit'
if (-not (Test-Path $source)) { throw "缺少钩子源文件：$source" }

Copy-Item -LiteralPath $source -Destination $target -Force
Write-Host "已安装提交前凭据检查：$target"

if (Get-Command gitleaks -ErrorAction SilentlyContinue) {
    Write-Host '检测到 gitleaks，将同时执行全量规则扫描。'
} else {
    Write-Host '未检测到 gitleaks，仅执行内置模式检查。建议安装以获得更完整的覆盖：' -ForegroundColor Yellow
    Write-Host '  https://github.com/gitleaks/gitleaks#installing'
}

Write-Host ''
Write-Host '提示：CI 侧另有 .github/workflows/secret-scan.yml 做全历史扫描。'
