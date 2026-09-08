[CmdletBinding()]
param(
    [string]$Dotnet = "$env:LOCALAPPDATA/VideoPlatform/dotnet/dotnet.exe",
    [string]$OutputDirectory,
    [switch]$VerifyInstallation,
    [switch]$SkipPublish,
    [switch]$VerifyArtifactsOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts/v2-desktop' }
$publish = Join-Path $output 'win-x64'
$updaterOutput = Join-Path $output 'updater-win-x64'
$wix = Join-Path $root 'tools/wix6/wix.exe'
$extension = Join-Path $root '.wix/extensions/WixToolset.UI.wixext/6.0.2/wixext6/WixToolset.UI.wixext.dll'
$definition = Join-Path $root 'deploy/VideoPlatform.Desktop.wxs'
$desktopProject = [xml](Get-Content -LiteralPath (Join-Path $root 'src/VideoPlatform.Desktop/VideoPlatform.Desktop.csproj') -Raw)
$version = ([version]$desktopProject.Project.PropertyGroup.Version).ToString(3)
$payloadNames = @('VideoPlatform.Desktop.exe', 'VideoPlatform.Desktop.dll', 'VideoPlatform.Client.dll', 'VideoPlatform.Updater.exe')
$manifestPath = Join-Path $output 'release-manifest.json'
if ($VerifyArtifactsOnly -and $VerifyInstallation) { throw '只读产物校验不能同时生成安装验收包。' }

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "命令执行失败：$Executable，退出码 $LASTEXITCODE。" }
}

function Get-PayloadHashes {
    param([string]$Directory)
    $hashes = @{}
    foreach ($name in $payloadNames) {
        $path = Join-Path $Directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "发布目录缺少必要文件：$name。" }
        $hashes[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $hashes
}

function Assert-PayloadHashes {
    param([hashtable]$Expected, [hashtable]$Actual, [string]$Label)
    foreach ($name in $payloadNames) {
        if ($Expected[$name] -cne $Actual[$name]) { throw "$Label 内容不一致：$name，SHA256 与发布目录不符。" }
    }
}

function Test-FinalPayloads {
    param([string]$ZipPath, [string]$MsiPath, [hashtable]$Expected, [string]$Evidence)
    New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
    $zipHashes = @{}
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($name in $payloadNames) {
            $entries = @($archive.Entries | Where-Object { $_.FullName -ceq $name })
            if ($entries.Count -ne 1) { throw "ZIP 必须包含且仅包含一个根目录文件：$name。" }
            $stream = $entries[0].Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $zipHashes[$name] = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
            finally { $sha.Dispose(); $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    Assert-PayloadHashes $Expected $zipHashes '最终 ZIP'
    # 管理解包不会注册或安装正式产品，比较的是最终 MSI 内嵌 CAB 的实际文件。
    $extract = Join-Path $Evidence 'msi-extracted'
    $log = Join-Path $Evidence 'msi-extract.log'
    $arguments = '/a "' + $MsiPath + '" /qn /norestart TARGETDIR="' + $extract + '" /L*v "' + $log + '"'
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "最终 MSI 解包失败，退出码 $($process.ExitCode)，日志：$log。" }
    $msiHashes = @{}
    foreach ($name in $payloadNames) {
        $matches = @(Get-ChildItem -LiteralPath $extract -Recurse -File -Filter $name)
        if ($matches.Count -ne 1) { throw "MSI 解包必须包含且仅包含一个文件：$name。" }
        $msiHashes[$name] = (Get-FileHash -LiteralPath $matches[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    Assert-PayloadHashes $Expected $msiHashes '最终 MSI'
    Assert-PayloadHashes $Expected (Get-PayloadHashes $publish) '校验完成时发布目录'
    foreach ($name in $payloadNames) {
        [pscustomobject]@{ fileName = $name; publishSha256 = $Expected[$name]; zipSha256 = $zipHashes[$name]; msiSha256 = $msiHashes[$name] }
    }
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
if (-not $SkipPublish -and -not $VerifyArtifactsOnly) {
    Invoke-Checked $Dotnet @('test', (Join-Path $root 'tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj'), '-c', 'Release', '--nologo', '--filter', 'Category!=Package&Category!=Candidate')
    Invoke-Checked $Dotnet @('publish', (Join-Path $root 'src/VideoPlatform.Desktop/VideoPlatform.Desktop.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publish)
    Invoke-Checked $Dotnet @('publish', (Join-Path $root 'src/VideoPlatform.Updater/VideoPlatform.Updater.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $updaterOutput)
    Copy-Item -LiteralPath (Join-Path $updaterOutput 'VideoPlatform.Updater.exe') -Destination $publish -Force
}
foreach ($required in ($payloadNames + @('VideoPlatform.Desktop.runtimeconfig.json'))) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) { throw "发布目录缺少必要文件：$required。" }
}
$expectedPayload = Get-PayloadHashes $publish
$zip = Join-Path $output "VideoPlatform.Desktop-$version-win-x64.zip"
$msi = Join-Path $output "VideoPlatform.Desktop-$version-x64.msi"
$evidence = Join-Path $output ('payload-qa-' + [Guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $VerifyArtifactsOnly) {
$readme = @"
VisiCore（视枢）$version，Windows x64。
便携版：解压完整目录后运行 VideoPlatform.Desktop.exe，无需另装 .NET。
安装版：运行同版本 MSI，支持安装目录选择、开始菜单和桌面快捷方式。
自动更新：通过 HTTPS 下载 MSI，校验大小与 SHA-256，失败时保留并恢复旧版本。
首次连接内部平台时，必须按组织流程安装可信证书，客户端不会跳过 TLS 验证。
此版本没有代码签名证书，EXE 和 MSI 均未签名。
个人设置和 DPAPI 加密会话位于当前用户的 LocalAppData/VideoPlatform 目录。
"@
$readme | Set-Content -LiteralPath (Join-Path $publish '使用说明.txt') -Encoding utf8
# ZIP 和安装包都从同一个自包含发布目录生成。
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$cabcache = Join-Path $output ('cabcache-' + [Guid]::NewGuid().ToString('N'))
# 每轮缓存使用新目录，禁止将相同文件版本号的旧 DLL 从历史 CAB 带入新包。
Invoke-Checked $wix @('build', $definition, '-arch', 'x64', '-ext', $extension, '-culture', 'zh-CN', '-d', "PublishDir=$publish", '-d', "PackageVersion=$version", '-cc', $cabcache, '-o', $msi)
}
$files = foreach ($path in @($zip, $msi)) {
    $file = Get-Item -LiteralPath $path
    $signature = if ($file.Extension -eq '.msi') { (Get-AuthenticodeSignature -LiteralPath $path).Status.ToString() } else { 'NotApplicable' }
    [pscustomobject][ordered]@{ fileName = $file.Name; fileSize = $file.Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); signature = $signature }
}
$payload = @(Test-FinalPayloads $zip $msi $expectedPayload $evidence)
if ($VerifyArtifactsOnly) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($file in $files) {
        $entry = @($manifest.files | Where-Object { $_.fileName -ceq $file.fileName })
        if ($entry.Count -ne 1 -or $entry[0].sha256 -cne $file.sha256 -or $entry[0].fileSize -ne $file.fileSize) { throw "发布清单与实际产物不一致：$($file.fileName)。" }
    }
}

if ($VerifyInstallation) {
    $qa = Join-Path $output ('qa-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $qa -Force | Out-Null
    $qaCode = [Guid]::NewGuid().ToString('B').ToUpperInvariant()
    $qaRegistry = 'Software\Liteware\VideoPlatform.Desktop.QA\' + $qaCode.Trim('{}')
    $qaName = 'VisiCore 视枢安装验收 ' + $qaCode.Substring(1, 8)
    $common = @('build', $definition, '-arch', 'x64', '-ext', $extension, '-culture', 'zh-CN', '-d', "PublishDir=$publish", '-d', "PackageName=$qaName", '-d', "PackageUpgradeCode=$qaCode", '-d', 'PackageScope=perUser', '-d', 'PackageRoot=LocalAppDataFolder', '-d', 'RegistryRoot=HKCU', '-d', "RegistryKey=$qaRegistry", '-d', ('InstallFolderName=VideoPlatform.Desktop.QA-' + $qaCode.Substring(1, 8)), '-cc', $cabcache)
    Invoke-Checked $wix ($common + @('-d', 'PackageVersion=2.0.0', '-o', (Join-Path $qa 'base.msi')))
    Invoke-Checked $wix ($common + @('-d', 'PackageVersion=2.0.1', '-o', (Join-Path $qa 'success.msi')))
    Invoke-Checked $wix ($common + @('-d', 'PackageVersion=2.0.2', '-d', 'InjectFailure=1', '-o', (Join-Path $qa 'failure.msi')))
    Invoke-Checked $Dotnet @('publish', (Join-Path $root 'tools/VideoPlatform.Desktop.Tests/Fixtures/RestartProbe/RestartProbe.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-o', (Join-Path $qa 'probe'))
    $env:VP_PACKAGE_QA = $qa
    $env:VP_PACKAGE_UPGRADE_CODE = $qaCode
    $env:VP_PACKAGE_REGISTRY = $qaRegistry
    $env:VP_PACKAGE_UPDATER = Join-Path $publish 'VideoPlatform.Updater.exe'
    $previousPackageOutput = $env:VP_PACKAGE_OUTPUT
    # 正式清单在全部校验后才更新，安装阶段只运行隔离 MSI 和陈旧文件回归；最终包由上方独立校验。
    Remove-Item Env:VP_PACKAGE_OUTPUT -ErrorAction SilentlyContinue
    try { Invoke-Checked $Dotnet @('test', (Join-Path $root 'tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj'), '-c', 'Release', '--nologo', '--filter', 'Category=Package&FullyQualifiedName!~FinalMsiAndZipPayloadsMatchPublishedFiles', '--logger', 'trx', '--results-directory', $qa) }
    finally {
        Remove-Item Env:VP_PACKAGE_QA,Env:VP_PACKAGE_UPGRADE_CODE,Env:VP_PACKAGE_REGISTRY,Env:VP_PACKAGE_UPDATER -ErrorAction SilentlyContinue
        if ($null -ne $previousPackageOutput) { $env:VP_PACKAGE_OUTPUT = $previousPackageOutput }
    }
}
Assert-PayloadHashes $expectedPayload (Get-PayloadHashes $publish) '验收完成时发布目录'
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath (Join-Path $output $file.fileName) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "验收期间产物被其他进程修改：$($file.fileName)。" }
}
if (-not $VerifyArtifactsOnly) {
    # 所有必要校验完成后才更新正式清单。
    [ordered]@{ version = $version; platform = 'win-x64'; selfContained = $true; signed = $false; files = @($files) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
$manifestSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
[ordered]@{ manifestSha256 = $manifestSha; files = @($files); payload = $payload } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'payload-verification.json') -Encoding utf8
$files | Format-Table fileName, fileSize, signature
Write-Host "最终 MSI、ZIP 与发布目录的 $($payloadNames.Count) 项文件 SHA256 一致。"
Write-Host "清单 SHA256：$manifestSha"
Write-Host "产物校验记录：$evidence"
Write-Host "发布产物：$output"
