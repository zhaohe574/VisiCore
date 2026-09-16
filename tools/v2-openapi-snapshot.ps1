<#
.SYNOPSIS
从仓库内的 OpenAPI 快照生成一份「与服务端当前 DTO 一致」的规范副本，用于离线跑通客户端生成器。

.DESCRIPTION
tools/VideoPlatform.ClientGenerator/.runtime/metadata-openapi.json 是上一次成功生成时的完整 OpenAPI 文档快照，
但它早于 2.1.0 的契约变更（媒体参数、回放码流档位）。本脚本只对快照做**已知的、与服务端一致的**增量补丁，
产出一份临时规范，供 generate.mjs --input 离线生成与漂移检测使用。

补丁内容（与服务端 Contracts/ApiResponses 一一对应）：
  1. PlaybackRequest.streamType —— 回放按档位选录像码流（B4）
  2. LiveSessionDto.width / height / bitrateKbps —— 真实媒体参数（B2），可空
  PlaybackSessionDto 继承 LiveSessionDto，补丁会同步补到子类型上。

.EXAMPLE
pwsh -File tools/v2-openapi-snapshot.ps1 -Output artifacts/v2/generated/openapi.source.json
#>
[CmdletBinding()]
param(
    [string]$Snapshot = '',
    [Parameter(Mandatory = $true)][string]$Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Snapshot)) {
    $Snapshot = Join-Path $root 'tools/VideoPlatform.ClientGenerator/.runtime/metadata-openapi.json'
}
if (-not (Test-Path -LiteralPath $Snapshot)) {
    Write-Error "找不到 OpenAPI 快照：$Snapshot"
    exit 2
}

$document = Get-Content -LiteralPath $Snapshot -Raw | ConvertFrom-Json

function New-NullableIntSchema {
    # 与服务端 int? 属性在 OpenAPI 中的既有形态保持一致。
    return [pscustomobject][ordered]@{
        type   = @('null', 'integer')
        format = 'int32'
    }
}

$patched = [System.Collections.Generic.List[string]]::new()

# 1) PlaybackRequest.streamType：回放码流档位（1 主码流 / 2 子码流）。
$playbackRequest = $document.components.schemas.PlaybackRequest
if ($playbackRequest.properties.PSObject.Properties.Name -notcontains 'streamType') {
    $playbackRequest.properties | Add-Member -NotePropertyName 'streamType' -NotePropertyValue ([pscustomobject][ordered]@{
        pattern = '^-?(?:0|[1-9]\d*)$'
        type    = @('integer', 'string')
        format  = 'int32'
        default = 1
    })
    $patched.Add('PlaybackRequest.streamType')
}

# 2) 媒体参数：分辨率与码率。适配器探测不到时为 null，客户端按“未知”显示。
foreach ($schemaName in 'LiveSessionDto', 'PlaybackSessionDto') {
    $schema = $document.components.schemas.$schemaName
    if ($null -eq $schema) { continue }
    foreach ($property in 'width', 'height', 'bitrateKbps') {
        if ($schema.properties.PSObject.Properties.Name -contains $property) { continue }
        $schema.properties | Add-Member -NotePropertyName $property -NotePropertyValue (New-NullableIntSchema)
        $patched.Add("$schemaName.$property")
    }
}

# 3) 快照早于当前代码：清空会话的两个 DELETE 现在返回 204（MediaEndpoints 用 Results.NoContent()），
#    而旧快照仍记录为无正文的 200，会让生成器判定“缺少响应类型”。
foreach ($route in '/api/v2/live-sessions', '/api/v2/playback-sessions') {
    $operation = $document.paths.$route.delete
    if ($null -eq $operation) { continue }
    foreach ($status in @($operation.responses.PSObject.Properties.Name | Where-Object { $_ -match '^2' })) {
        if ($status -ne '204') { $operation.responses.PSObject.Properties.Remove($status) }
    }
    if ($operation.responses.PSObject.Properties.Name -notcontains '204') {
        $operation.responses | Add-Member -NotePropertyName '204' -NotePropertyValue ([pscustomobject][ordered]@{ description = '操作完成，无响应正文' })
        $patched.Add("$route DELETE -> 204")
    }
}

# 4) 清空会话端点的 all 查询参数：端点从 HttpContext 读取，OpenAPI 无法自动发现，
#    契约目录已显式声明；快照里补上，使生成器能产出可用的 StopActiveliveSessions/all 参数。
foreach ($route in '/api/v2/live-sessions', '/api/v2/playback-sessions') {
    $operation = $document.paths.$route.delete
    if ($null -eq $operation) { continue }
    $operation.parameters ??= @()
    $hasAll = @($operation.parameters | Where-Object { $_.in -eq 'query' -and $_.name -eq 'all' }).Count -gt 0
    if (-not $hasAll) {
        $operation.parameters = @($operation.parameters) + [pscustomobject][ordered]@{
            name = 'all'; in = 'query'; required = $false
            schema = [pscustomobject][ordered]@{ type = 'boolean' }
        }
        $patched.Add("$route DELETE all 查询参数")
    }
}

$directory = Split-Path -Parent $Output
if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$document | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $Output -Encoding UTF8

Write-Host "已生成离线规范：$Output"
Write-Host "应用补丁 $($patched.Count) 项：$($patched -join '、')"

