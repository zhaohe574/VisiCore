[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CandidateTag
)

$ErrorActionPreference = 'Stop'

if ($CandidateTag -notmatch '^v(?<version>[0-9]+\.[0-9]+\.[0-9]+)-rc\.(?<number>[1-9][0-9]*)$') {
    throw '候选标签格式无效。'
}

$platform = 'windows-x64'
$sourceCommit = (git rev-parse HEAD).Trim()
$repository = $env:GITHUB_REPOSITORY
$workspace = if ($env:VISICORE_STAGING_WORKSPACE) { $env:VISICORE_STAGING_WORKSPACE } else { 'C:\VisiCoreStaging' }
$apiBaseUrl = $env:VISICORE_STAGING_API_BASE_URL
$edgeService = if ($env:VISICORE_STAGING_EDGE_SERVICE_NAME) { $env:VISICORE_STAGING_EDGE_SERVICE_NAME } else { 'VisiCore Edge Agent' }
$hostService = if ($env:VISICORE_STAGING_EDGE_HOST_SERVICE_NAME) { $env:VISICORE_STAGING_EDGE_HOST_SERVICE_NAME } else { 'VisiCore Edge Host Agent' }
$edgeAgentName = if ($env:VISICORE_STAGING_EDGE_AGENT_NAME) { $env:VISICORE_STAGING_EDGE_AGENT_NAME } else { 'visicore-staging-windows-x64-edge' }
$resultFile = Join-Path $PSScriptRoot '..\..\artifacts\staging\windows-x64-result.json'
$resultFile = [IO.Path]::GetFullPath($resultFile)
$assetDirectory = Join-Path $workspace 'candidate-assets'
$governanceDirectory = Join-Path $workspace 'candidate-governance'
$coreReference = ''
$coreDigest = ''
$edgeReference = ''
$edgeDigest = ''
$edgeWindowsFile = ''
$edgeWindowsSha256 = ''
$viewerFile = ''
$viewerSha256 = ''
$failureCode = ''

function Write-Result([string]$Result) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resultFile) | Out-Null
    $payload = [ordered]@{
        schemaVersion = 1
        platform = $platform
        candidateTag = $CandidateTag
        sourceCommit = $sourceCommit
        result = $Result
        failureCode = if ($failureCode) { $failureCode } else { $null }
        artifacts = [ordered]@{
            core = [ordered]@{ reference = $coreReference; digest = $coreDigest }
            edge = [ordered]@{ reference = $edgeReference; digest = $edgeDigest }
            edgeWindows = [ordered]@{ file = $edgeWindowsFile; sha256 = $edgeWindowsSha256 }
            viewer = [ordered]@{ file = $viewerFile; sha256 = $viewerSha256 }
        }
        workflowRun = "https://github.com/$repository/actions/runs/$($env:GITHUB_RUN_ID)"
    }
    $payload | ConvertTo-Json -Depth 6 -Compress | Set-Content -LiteralPath $resultFile -Encoding ascii
}

try {
    if ([string]::IsNullOrWhiteSpace($repository) -or [string]::IsNullOrWhiteSpace($apiBaseUrl)) { throw '缺少 staging 仓库或中心地址。' }
    if (-not $workspace.StartsWith('C:\VisiCoreStaging', [StringComparison]::OrdinalIgnoreCase)) { throw 'Windows staging 工作目录不安全。' }
    if ($apiBaseUrl -notmatch '^https://[^/]*staging[^/]*/?$') { throw '中心地址必须是 staging 内部 TLS DNS。' }
    if ([string]::IsNullOrWhiteSpace($env:VISICORE_STAGING_ADMIN_USERNAME) -or [string]::IsNullOrWhiteSpace($env:VISICORE_STAGING_ADMIN_PASSWORD)) { throw '缺少 staging 管理员凭据。' }
    if ($edgeAgentName -notmatch '^visicore-staging-') { throw 'Windows Edge 名称必须使用 visicore-staging 前缀。' }
    foreach ($serviceName in @($edgeService, $hostService)) {
        if ((Get-Service -Name $serviceName -ErrorAction Stop).Status -ne 'Running') { throw "staging 服务未运行：$serviceName" }
    }

    Remove-Item -LiteralPath $assetDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $governanceDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $governanceDirectory | Out-Null
    gh release download $CandidateTag --repo $repository --dir $assetDirectory --clobber
    if ($LASTEXITCODE -ne 0) { throw '无法下载 RC Release 资产。' }
    $releaseRuns = gh run list --repo $repository --workflow Release --branch $CandidateTag --status success --limit 1 --json databaseId | ConvertFrom-Json
    $releaseRunId = $releaseRuns | Select-Object -First 1 -ExpandProperty databaseId
    if ([string]::IsNullOrWhiteSpace([string]$releaseRunId)) { throw '未找到候选 Release Actions 运行。' }
    gh run download $releaseRunId --repo $repository --name visicore-release-governance --dir $governanceDirectory
    if ($LASTEXITCODE -ne 0) { throw '无法下载候选内部治理证据。' }

    $descriptor = Get-Content -LiteralPath (Join-Path $governanceDirectory 'release-descriptor.json') -Raw | ConvertFrom-Json
    if ($descriptor.releaseId -ne $CandidateTag -or $descriptor.sourceCommit -ne $sourceCommit) { throw 'RC 描述与候选提交不一致。' }
    $core = $descriptor.artifacts | Where-Object { $_.component -eq 'core' -and $_.platform -eq 'linux' -and $_.architecture -eq 'amd64' } | Select-Object -First 1
    $edge = $descriptor.artifacts | Where-Object { $_.component -eq 'edge-docker' -and $_.platform -eq 'linux' -and $_.architecture -eq 'amd64' } | Select-Object -First 1
    $edgeWindows = $descriptor.artifacts | Where-Object { $_.component -eq 'edge-windows' -and $_.platform -eq 'windows' -and $_.architecture -eq 'x64' } | Select-Object -First 1
    $evidence = Get-Content -LiteralPath (Join-Path $governanceDirectory 'release-evidence.json') -Raw | ConvertFrom-Json
    $coreReference = $core.artifactReference
    $coreDigest = $core.artifactSha256
    $edgeReference = $edge.artifactReference
    $edgeDigest = $edge.artifactSha256
    $edgeWindowsFile = Split-Path -Leaf $edgeWindows.artifactReference
    $edgeWindowsSha256 = $edgeWindows.artifactSha256
    $viewerFile = $evidence.artifacts.viewer.file
    $viewerSha256 = $evidence.artifacts.viewer.sha256
    if ((Get-FileHash -LiteralPath (Join-Path $assetDirectory $edgeWindowsFile) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $edgeWindowsSha256.ToLowerInvariant()) { throw 'Windows Edge MSI 摘要不匹配。' }
    if ((Get-FileHash -LiteralPath (Join-Path $assetDirectory $viewerFile) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $viewerSha256.ToLowerInvariant()) { throw 'Viewer MSI 摘要不匹配。' }

    $login = Invoke-RestMethod -Method Post -Uri "$($apiBaseUrl.TrimEnd('/'))/api/v1/auth/login" -ContentType 'application/json' -Body (@{ username = $env:VISICORE_STAGING_ADMIN_USERNAME; password = $env:VISICORE_STAGING_ADMIN_PASSWORD } | ConvertTo-Json -Compress)
    if ([string]::IsNullOrWhiteSpace($login.accessToken)) { throw 'staging 管理员登录未返回会话。' }
    $headers = @{ Authorization = "Bearer $($login.accessToken)" }
    $agents = Invoke-RestMethod -Method Get -Uri "$($apiBaseUrl.TrimEnd('/'))/api/v1/admin/edge-agents" -Headers $headers
    $target = $agents | Where-Object { $_.name -eq $edgeAgentName -and $_.platform -eq 'windows' -and $null -eq $_.disabledAt } | Select-Object -First 1
    if ($null -eq $target) { throw "未找到已登记的 staging Windows Edge：$edgeAgentName" }
    if (-not (($target.capabilitiesJson | ConvertFrom-Json).hostUpgradeReady)) { throw 'Windows Edge Host Agent 尚未处于可升级状态。' }

    $catalog = Invoke-RestMethod -Method Get -Uri "$($apiBaseUrl.TrimEnd('/'))/api/v1/admin/release-catalog" -Headers $headers
    $release = $catalog | Where-Object { $_.releaseId -eq $CandidateTag -and $_.channel -eq 'rc' } | Select-Object -First 1
    if ($null -eq $release) { throw 'Linux amd64 staging 演练尚未登记候选发行描述。' }
    $planRequest = @{ releaseCatalogId = $release.id; targetScope = 'edge'; edgeAgentIds = @($target.id) } | ConvertTo-Json -Compress
    $plan = Invoke-RestMethod -Method Post -Uri "$($apiBaseUrl.TrimEnd('/'))/api/v1/admin/upgrade-plans" -Headers $headers -ContentType 'application/json' -Body $planRequest
    Invoke-RestMethod -Method Post -Uri "$($apiBaseUrl.TrimEnd('/'))/api/v1/admin/upgrade-plans/$($plan.id)/start" -Headers $headers | Out-Null
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(25)
    do {
        Start-Sleep -Seconds 10
        $plans = Invoke-RestMethod -Method Get -Uri "$($apiBaseUrl.TrimEnd('/'))/api/v1/admin/upgrade-plans" -Headers $headers
        $currentPlan = $plans | Where-Object { $_.id -eq $plan.id } | Select-Object -First 1
        if ($null -eq $currentPlan) { throw '无法读取 Windows Edge 升级计划。' }
        if ($currentPlan.status -eq 'succeeded') { break }
        if ($currentPlan.status -eq 'paused') { throw "Windows Edge 升级计划已暂停：$($currentPlan.failureSummary)" }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($currentPlan.status -ne 'succeeded') { throw '等待 Windows Edge 观察窗口超时。' }
    foreach ($serviceName in @($edgeService, $hostService)) {
        if ((Get-Service -Name $serviceName).Status -ne 'Running') { throw "Windows Edge 服务升级后未运行：$serviceName" }
    }

    $viewerInstall = Start-Process msiexec.exe -ArgumentList @('/i', (Join-Path $assetDirectory $viewerFile), '/qn', '/norestart') -Wait -PassThru
    if ($viewerInstall.ExitCode -notin 0, 3010) { throw "Viewer MSI 安装失败：$($viewerInstall.ExitCode)" }
    $viewer = Join-Path $env:ProgramFiles 'VisiCore\Viewer\VisiCoreQtViewer.exe'
    if (-not (Test-Path -LiteralPath $viewer)) { throw 'Viewer MSI 未安装可执行文件。' }
    & $viewer --verify-mpv-runtime
    if ($LASTEXITCODE -ne 0) { throw 'Viewer libmpv 运行时校验失败。' }
    & $viewer --verify-login-shell
    if ($LASTEXITCODE -ne 0) { throw 'Viewer 登录壳验证失败。' }
    Write-Result 'passed'
} catch {
    $failureCode = 'staging_windows_validation_failed'
    Write-Result 'failed'
    throw
}
