param(
    [string]$CertificatePath = "$PSScriptRoot\..\artifacts\video-platform.crt"
)

$resolved = (Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop).Path
Import-Certificate -FilePath $resolved -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
Write-Host '已将 VisiCore（视枢）内部 HTTPS 证书导入当前用户的受信任根证书颁发机构。'
