# VisiCore（视枢）提交前凭据检查（由 tools/git-hooks/pre-commit 调用）
#
# 退出码：0 = 通过；1 = 检出疑似凭据，阻止提交。
# 目的：在凭据进入 Git 历史之前拦截。历史一旦推送，清理成本远高于拦截。

$ErrorActionPreference = 'Stop'
$root = (& git rev-parse --show-toplevel 2>$null)
if (-not $root) { exit 0 }
Set-Location $root

# 1) 禁止提交受保护的敏感文件（无论是否被 .gitignore 覆盖）。
$forbidden = @('HANDOFF.md', '.secrets/', '.p0-backup/')
$staged = & git diff --cached --name-only
foreach ($pattern in $forbidden) {
    $hit = @($staged | Where-Object { $_ -like "$pattern*" })
    if ($hit.Count -gt 0) {
        Write-Host "拒绝提交：检测到受保护的敏感文件：$($hit -join ', ')" -ForegroundColor Red
        Write-Host '这些文件仅保存在内网工作区，不得进入版本控制。' -ForegroundColor Red
        exit 1
    }
}

# 2) 对暂存差异做模式匹配（不依赖 gitleaks 是否安装）。
#    只检查"新增行"（diff 中的 + 行）：被删除的旧凭据出现在 - 行是修复本身，
#    不应导致提交被拒。
$patterns = @(
    'os\.environ\.get\(\s*["''][A-Z0-9_]+["'']\s*,\s*["''][^"'']{3,}["'']\s*\)',
    # 文档中的明文口令；排除已脱敏的"见未跟踪凭据文件"写法。
    '密码\s*[:：]\s*[`''"]?(?!见未跟踪凭据文件)[^\s`''"，。）]{4,}',
    # 形如 <脚本变量> 被赋值为字面量口令；用 \b 避免误伤
    # VIDEO_PLATFORM_SSH_PASSWORD 这类"以 _SSH_PASSWORD 结尾的变量名"。
    '(?i)\b(ssh|admin|ipc|nvr|device)_password\s*[=:]\s*["''][^"''$<{]',
    # 命令行/示例中的 --password 后跟字面量（排除占位符与变量引用）。
    '(?i)--password\s+(?!<|\$\{|%|请)[A-Za-z0-9@._-]{4,}'
)
$diff = & git diff --cached -U0
$added = @($diff | Where-Object { $_ -like '+*' -and $_ -notlike '+++*' })
$violations = @()
foreach ($p in $patterns) {
    $m = @($added | Select-String -Pattern $p -AllMatches)
    if ($m.Count -gt 0) { $violations += $m }
}
if ($violations.Count -gt 0) {
    Write-Host '拒绝提交：暂存内容疑似包含明文凭据：' -ForegroundColor Red
    $violations | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Yellow }
    Write-Host '请改用环境变量或未跟踪的 .secrets/deploy-credentials.txt。' -ForegroundColor Red
    exit 1
}

# 3) 若安装了 gitleaks，则额外执行一次暂存区规则检查。
$gitleaks = Get-Command gitleaks -ErrorAction SilentlyContinue
if ($gitleaks) {
    & gitleaks protect --staged --config .gitleaks.toml --redact --no-banner
    if ($LASTEXITCODE -ne 0) {
        Write-Host '拒绝提交：gitleaks 检出疑似凭据（见上方报告）。' -ForegroundColor Red
        exit 1
    }
}

exit 0
