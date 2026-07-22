[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$QtRoot,
    [Parameter(Mandatory = $true)]
    [string]$MpvRoot,
    [Parameter(Mandatory = $true)]
    [string]$MpvVersion,
    [Parameter(Mandatory = $true)]
    [string]$MpvSource,
    [Parameter(Mandatory = $true)]
    [string]$MpvSha256,
    [Parameter(Mandatory = $true)]
    [string]$MpvLicenseFile,
    [Parameter(Mandatory = $true)]
    [string]$QtLicenseFile,
    [Parameter(Mandatory = $true)]
    [string]$MinGwLicenseDirectory,
    [string]$OutputRoot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$viewerSource = Join-Path $repositoryRoot 'src\VisiCore.QtViewer'
$version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'versions\viewer.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'versions/viewer.txt 必须是 x.y.z 格式。'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\windows\viewer'
}
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$buildDirectory = Join-Path $output 'build'
$stageDirectory = Join-Path $output 'stage'

$qt = [System.IO.Path]::GetFullPath($QtRoot)
$mpv = [System.IO.Path]::GetFullPath($MpvRoot)
$license = [System.IO.Path]::GetFullPath($MpvLicenseFile)
$qtLicense = [System.IO.Path]::GetFullPath($QtLicenseFile)
$minGwLicenses = [System.IO.Path]::GetFullPath($MinGwLicenseDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $qt 'bin\windeployqt.exe'))) {
    throw "QtRoot 未包含 windeployqt.exe：$qt"
}
if (-not (Test-Path -LiteralPath $license)) {
    throw "未找到 libmpv 许可证文件：$license"
}
if (-not (Test-Path -LiteralPath $qtLicense -PathType Leaf)) {
    throw "未找到 Qt 许可证文件：$qtLicense"
}
if (-not (Test-Path -LiteralPath $minGwLicenses -PathType Container)) {
    throw "未找到 MinGW 许可证目录：$minGwLicenses"
}

$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if ($null -eq $cmake) {
    $localCmake = Join-Path $repositoryRoot '.local-sdk\qt-tooling\Scripts\cmake.exe'
    if (-not (Test-Path -LiteralPath $localCmake)) {
        throw '未找到 cmake。请安装 CMake 或准备本仓库 .local-sdk 工具链。'
    }
    $cmakePath = $localCmake
} else {
    $cmakePath = $cmake.Source
}

$ninja = Get-Command ninja -ErrorAction SilentlyContinue
if ($null -eq $ninja) {
    $localNinja = Join-Path $repositoryRoot '.local-sdk\qt-tooling\Scripts\ninja.exe'
    if (-not (Test-Path -LiteralPath $localNinja)) {
        throw '未找到 Ninja。'
    }
    $ninjaPath = $localNinja
} else {
    $ninjaPath = $ninja.Source
}

& $cmakePath -S $viewerSource -B $buildDirectory -G Ninja `
    "-DCMAKE_BUILD_TYPE=Release" `
    "-DCMAKE_PREFIX_PATH=$qt" `
    "-DCMAKE_MAKE_PROGRAM=$ninjaPath" `
    "-DCMAKE_INSTALL_PREFIX=$stageDirectory" `
    "-DVISICORE_RELEASE_BUILD=ON" `
    "-DVISICORE_MPV_ROOT=$mpv" `
    "-DVISICORE_MPV_RUNTIME_VERSION=$MpvVersion" `
    "-DVISICORE_MPV_RUNTIME_SOURCE=$MpvSource" `
    "-DVISICORE_MPV_RUNTIME_SHA256=$MpvSha256" `
    "-DVISICORE_MPV_RUNTIME_LICENSE_FILE=$license"
if ($LASTEXITCODE -ne 0) { throw '查看端 CMake 配置失败。' }

& $cmakePath --build $buildDirectory --parallel 1
if ($LASTEXITCODE -ne 0) { throw '查看端编译失败。' }
if (-not $SkipTests) {
    & $cmakePath --build $buildDirectory --target test
    if ($LASTEXITCODE -ne 0) { throw '查看端 CTest 失败。' }
}
& $cmakePath --install $buildDirectory
if ($LASTEXITCODE -ne 0) { throw '查看端运行时部署失败。' }

$stageMpvLibrary = Join-Path $stageDirectory 'libmpv-2.dll'
if (-not (Test-Path -LiteralPath $stageMpvLibrary)) {
    throw '查看端部署目录中缺少受控 libmpv-2.dll。'
}
$stageMpvHash = (Get-FileHash -LiteralPath $stageMpvLibrary -Algorithm SHA256).Hash
if ($stageMpvHash -ne $MpvSha256.ToUpperInvariant()) {
    throw '部署后的 libmpv-2.dll SHA-256 与受控输入不匹配。'
}

$licensesDirectory = Join-Path $stageDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $licensesDirectory | Out-Null
Copy-Item -LiteralPath $qtLicense -Destination (Join-Path $licensesDirectory 'Qt-LGPL-3.0-only.txt') -Force
Copy-Item -LiteralPath $minGwLicenses -Destination (Join-Path $licensesDirectory 'MinGW') -Recurse -Force

function Copy-DependencyLicense {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$SourceDirectory
    )

    foreach ($candidate in @('LICENSE', 'LICENSE.txt', 'LICENSE.md', 'COPYING', 'COPYING.txt')) {
        $source = Join-Path $SourceDirectory $candidate
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $licensesDirectory "$Name-$candidate") -Force
            return
        }
    }
    throw "未找到 $Name 的许可证文件：$SourceDirectory"
}

Copy-DependencyLicense -Name 'Qlementine' -SourceDirectory (Join-Path $buildDirectory '_deps\qlementine-src')
Copy-DependencyLicense -Name 'Qt-ADS' -SourceDirectory (Join-Path $buildDirectory '_deps\qt_ads-src')
Copy-DependencyLicense -Name 'Lucide' -SourceDirectory (Join-Path $buildDirectory '_deps\lucide-src')

$viewerExecutable = Join-Path $stageDirectory 'VisiCoreQtViewer.exe'
if (-not (Test-Path -LiteralPath $viewerExecutable)) {
    throw '查看端部署目录中缺少 VisiCoreQtViewer.exe。'
}
& $viewerExecutable --verify-mpv-runtime
if ($LASTEXITCODE -ne 0) { throw '部署后的 libmpv 运行时校验失败。' }

& dotnet build (Join-Path $repositoryRoot 'deploy\windows\VisiCore.Viewer.Installer.wixproj') --configuration Release `
    "-p:ProductVersion=$version" `
    "-p:ViewerPublishDir=$stageDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Viewer MSI 构建失败。' }

$msiSource = Join-Path $repositoryRoot 'deploy\windows\bin\x64\Release\VisiCore.Viewer.Installer.msi'
if (-not (Test-Path -LiteralPath $msiSource)) {
    throw '未找到 Viewer MSI 输出。'
}
$msiOutput = Join-Path $output "visicore-viewer-v$version.msi"
Copy-Item -LiteralPath $msiSource -Destination $msiOutput -Force
$hashOutput = Join-Path $output 'viewer-sha256.txt'
$msiHash = (Get-FileHash -LiteralPath $msiOutput -Algorithm SHA256).Hash
$runtimeHash = $stageMpvHash
Set-Content -LiteralPath $hashOutput -Encoding ascii -Value @(
    "$msiHash  $(Split-Path -Leaf $msiOutput)",
    "$runtimeHash  libmpv-2.dll"
)
Copy-Item -LiteralPath (Join-Path $stageDirectory 'visicore-viewer-runtime.json') -Destination (Join-Path $output 'visicore-viewer-runtime.json') -Force

Write-Output "MSI: $msiOutput"
Write-Output "SHA256: $hashOutput"
