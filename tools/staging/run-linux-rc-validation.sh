#!/usr/bin/env bash
set -Eeuo pipefail

# 在隔离 Runner 上恢复 v0.1.2，再验证 Core、同架构 Linux Edge 与失败回滚。
# 所有可变资源都必须位于 visicore-staging 前缀和 /opt/visicore-staging 目录内。

if [[ $# -ne 2 ]]; then
  echo "用法：$0 <候选标签> <linux-amd64|linux-arm64>" >&2
  exit 64
fi

candidate_tag="$1"
platform="$2"
if [[ ! "$candidate_tag" =~ ^v([0-9]+\.[0-9]+\.[0-9]+)-rc\.([1-9][0-9]*)$ ]] || [[ "$platform" != "linux-amd64" && "$platform" != "linux-arm64" ]]; then
  echo "候选标签或平台无效。" >&2
  exit 64
fi

repository="${GITHUB_REPOSITORY:?缺少 GITHUB_REPOSITORY}"
source_commit="$(git rev-parse HEAD)"
workspace="${VISICORE_STAGING_WORKSPACE:-/opt/visicore-staging}"
fixture="${VISICORE_STAGING_BASELINE_BACKUP_PATH:-/opt/visicore-staging/fixtures/v0.1.2.vcbackup}"
compose_file="${VISICORE_STAGING_CORE_COMPOSE_FILE:-${workspace}/core/compose.yaml}"
baseline_env="${VISICORE_STAGING_BASELINE_ENV_FILE:-${workspace}/core/v0.1.2.env}"
project_name="${VISICORE_STAGING_CORE_PROJECT:-visicore-staging-core-${platform#linux-}}"
api_base_url="${VISICORE_STAGING_API_BASE_URL:?缺少 VISICORE_STAGING_API_BASE_URL}"
origin="${VISICORE_STAGING_ORIGIN:-$api_base_url}"
edge_name="${VISICORE_STAGING_EDGE_AGENT_NAME:-visicore-staging-${platform}-edge}"
result_file="artifacts/staging/${platform}-result.json"

core_reference=""
core_digest=""
edge_reference=""
edge_digest=""
backup_id=""
failure_code=""

write_result() {
  local result="$1"
  mkdir -p "$(dirname "$result_file")"
  jq -n \
    --arg platform "$platform" \
    --arg candidate_tag "$candidate_tag" \
    --arg source_commit "$source_commit" \
    --arg result "$result" \
    --arg failure_code "$failure_code" \
    --arg core_reference "$core_reference" \
    --arg core_digest "$core_digest" \
    --arg edge_reference "$edge_reference" \
    --arg edge_digest "$edge_digest" \
    --arg backup_id "$backup_id" \
    --arg workflow_run "https://github.com/${repository}/actions/runs/${GITHUB_RUN_ID:-local}" \
    '{schemaVersion:1,platform:$platform,candidateTag:$candidate_tag,sourceCommit:$source_commit,result:$result,failureCode:(if $failure_code == "" then null else $failure_code end),artifacts:{core:{reference:$core_reference,digest:$core_digest},edge:{reference:$edge_reference,digest:$edge_digest}},backupId:(if $backup_id == "" then null else $backup_id end),workflowRun:$workflow_run}' > "$result_file"
}

on_error() {
  local code="$?"
  failure_code="${failure_code:-staging_linux_validation_failed}"
  write_result failed
  exit "$code"
}
trap on_error ERR

require_staging_prerequisites() {
  [[ "$workspace" == /opt/visicore-staging ]] || [[ "$workspace" == /opt/visicore-staging/* ]] || { echo "staging 工作目录不安全：$workspace" >&2; return 1; }
  [[ "$fixture" == /opt/visicore-staging/fixtures/v0.1.2.vcbackup ]] || { echo "恢复包路径不是固定 staging fixture。" >&2; return 1; }
  [[ "$compose_file" == "$workspace"/* && "$baseline_env" == "$workspace"/* ]] || { echo "Core Compose 或基线环境文件位于 staging 工作目录之外。" >&2; return 1; }
  [[ "$project_name" =~ ^visicore-staging-[a-z0-9-]+$ ]] || { echo "Compose 项目名不是 staging 资源：$project_name" >&2; return 1; }
  [[ "$(hostname -s)" == *staging* ]] || { echo "Runner 主机名必须包含 staging。" >&2; return 1; }
  [[ "$api_base_url" =~ ^https://[^/]*staging[^/]*/?$ ]] || { echo "中心地址必须是 staging 内部 TLS DNS。" >&2; return 1; }
  [[ -f "$fixture" && -s "$fixture" ]] || { echo "缺少 v0.1.2 staging 恢复包。" >&2; return 1; }
  [[ -f "$compose_file" && -f "$baseline_env" ]] || { echo "缺少 staging Core Compose 或 v0.1.2 环境文件。" >&2; return 1; }
  grep -Eq '^VISICORE_CORE_IMAGE=visicore/visicore-core@sha256:[0-9a-f]{64}$' "$baseline_env" || { echo "v0.1.2 基线必须固定为 Docker Hub digest。" >&2; return 1; }
  for volume_key in VISICORE_POSTGRES_VOLUME VISICORE_CONFIG_VOLUME VISICORE_BACKUPS_VOLUME VISICORE_EXPORTS_VOLUME; do
    grep -Eq "^${volume_key}=visicore-staging-[a-z0-9-]+$" "$baseline_env" || { echo "基线卷不是 staging 专用资源：${volume_key}。" >&2; return 1; }
  done
  systemctl is-active --quiet visicore-core-host-agent.service || { echo "Core Host Agent 未运行。" >&2; return 1; }
  [[ -n "${VISICORE_STAGING_ADMIN_USERNAME:-}" && -n "${VISICORE_STAGING_ADMIN_PASSWORD:-}" && -n "${VISICORE_STAGING_RECOVERY_KEY:-}" ]] || { echo "缺少 staging 管理员或恢复凭据。" >&2; return 1; }
  command -v docker >/dev/null && command -v curl >/dev/null && command -v jq >/dev/null && command -v gh >/dev/null
}

wait_for_http() {
  local path="$1"
  local deadline=$((SECONDS + 420))
  until curl --fail --silent --show-error --max-time 10 "${api_base_url%/}${path}" > /dev/null; do
    (( SECONDS < deadline )) || { echo "等待 ${path} 超时。" >&2; return 1; }
    sleep 5
  done
}

api() {
  local method="$1"
  local path="$2"
  local data="${3:-}"
  local args=(--fail-with-body --silent --show-error --max-time 30 -X "$method" "${api_base_url%/}${path}" -H "Authorization: Bearer ${access_token}" -H 'Content-Type: application/json')
  if [[ -n "$data" ]]; then
    args+=(--data "$data")
  fi
  curl "${args[@]}"
}

wait_for_plan() {
  local plan_id="$1"
  local expected_status="$2"
  local deadline=$((SECONDS + 1500))
  local response status
  while true; do
    response="$(api GET "/api/v1/admin/upgrade-plans")"
    status="$(jq -r --arg id "$plan_id" '.[] | select(.id == $id) | .status' <<< "$response")"
    if [[ "$status" == "$expected_status" ]]; then
      printf '%s' "$response"
      return 0
    fi
    if [[ "$status" == "paused" || "$status" == "succeeded" ]]; then
      echo "升级计划状态不符合预期：$status。" >&2
      return 1
    fi
    (( SECONDS < deadline )) || { echo "等待升级计划超时：$plan_id。" >&2; return 1; }
    sleep 10
  done
}

register_descriptor() {
  local descriptor="$1"
  local signature="$2"
  local key_id
  key_id="$(jq -r '.signingPublicKeyId' "$descriptor")"
  api POST /api/v1/admin/release-catalog "$(jq -n --rawfile descriptor "$descriptor" --rawfile signature "$signature" --arg key_id "$key_id" '{descriptorJson:$descriptor,signatureBase64:$signature,publicKeyId:$key_id}')"
}

run_core_plan() {
  local catalog_id="$1"
  local plan response
  plan="$(api POST /api/v1/admin/upgrade-plans "$(jq -n --arg catalog "$catalog_id" '{releaseCatalogId:$catalog,targetScope:"core"}')")"
  local plan_id
  plan_id="$(jq -r '.id' <<< "$plan")"
  api POST "/api/v1/admin/upgrade-plans/${plan_id}/start" > /dev/null
  response="$(wait_for_plan "$plan_id" succeeded)"
  backup_id="$(api GET "/api/v1/admin/upgrade-plans/${plan_id}/timeline" | jq -r '.items[] | select(.protectionBackupId != null) | .protectionBackupId' | head -n 1)"
  [[ "$backup_id" =~ ^[0-9a-f-]{36}$ ]] || { echo "未记录 Core 保护备份 ID。" >&2; return 1; }
}

run_edge_plan() {
  local catalog_id="$1"
  local agents agent_id plan plan_id
  agents="$(api GET /api/v1/admin/edge-agents)"
  agent_id="$(jq -r --arg name "$edge_name" '.[] | select(.name == $name and .platform == "linux" and .disabledAt == null) | .id' <<< "$agents")"
  [[ "$agent_id" =~ ^[0-9a-f-]{36}$ ]] || { echo "未找到已就绪的 staging Edge：$edge_name。" >&2; return 1; }
  jq -e --arg id "$agent_id" '.[] | select(.id == $id) | (.capabilitiesJson | fromjson | .hostUpgradeReady == true)' <<< "$agents" > /dev/null
  plan="$(api POST /api/v1/admin/upgrade-plans "$(jq -n --arg catalog "$catalog_id" --arg agent "$agent_id" '{releaseCatalogId:$catalog,targetScope:"edge",edgeAgentIds:[$agent]}')")"
  plan_id="$(jq -r '.id' <<< "$plan")"
  api POST "/api/v1/admin/upgrade-plans/${plan_id}/start" > /dev/null
  wait_for_plan "$plan_id" succeeded > /dev/null
}

require_staging_prerequisites
mkdir -p artifacts/staging candidate-assets candidate-governance
rm -rf candidate-assets/* candidate-governance/*
gh release download "$candidate_tag" --repo "$repository" --dir candidate-assets --clobber
release_run_id="$(gh run list --repo "$repository" --workflow Release --branch "$candidate_tag" --status success --limit 1 --json databaseId --jq '.[0].databaseId')"
[[ "$release_run_id" =~ ^[0-9]+$ ]] || { echo "未找到候选 Release Actions 运行。" >&2; exit 1; }
gh run download "$release_run_id" --repo "$repository" --name visicore-release-governance --dir candidate-governance
bash tools/verify-release-promotion.sh candidate-assets candidate-governance "$candidate_tag"

core_reference="$(jq -r '.artifacts[] | select(.component == "core" and .platform == "linux" and .architecture == "amd64") | .artifactReference' candidate-governance/release-descriptor.json)"
core_digest="$(jq -r '.artifacts[] | select(.component == "core" and .platform == "linux" and .architecture == "amd64") | .artifactSha256' candidate-governance/release-descriptor.json)"
edge_reference="$(jq -r '.artifacts[] | select(.component == "edge-docker" and .platform == "linux" and .architecture == "amd64") | .artifactReference' candidate-governance/release-descriptor.json)"
edge_digest="$(jq -r '.artifacts[] | select(.component == "edge-docker" and .platform == "linux" and .architecture == "amd64") | .artifactSha256' candidate-governance/release-descriptor.json)"
jq -e --arg commit "$source_commit" '.sourceCommit == $commit' candidate-governance/release-descriptor.json > /dev/null

# 清理只属于 staging 项目名的卷，然后从受控 v0.1.2 备份恢复。
docker compose --project-name "$project_name" --env-file "$baseline_env" -f "$compose_file" down --volumes --remove-orphans
docker compose --project-name "$project_name" --env-file "$baseline_env" -f "$compose_file" up --detach
wait_for_http /api/v1/setup/status
curl --fail-with-body --silent --show-error --max-time 120 \
  -X POST "${api_base_url%/}/api/v1/setup/restore" \
  -H "Origin: ${origin%/}" \
  -F "backup=@${fixture}" \
  -F "recoveryKey=${VISICORE_STAGING_RECOVERY_KEY}" > /dev/null
wait_for_http /healthz
wait_for_http /readyz

access_token="$(curl --fail-with-body --silent --show-error --max-time 30 -X POST "${api_base_url%/}/api/v1/auth/login" -H 'Content-Type: application/json' --data "$(jq -n --arg username "$VISICORE_STAGING_ADMIN_USERNAME" --arg password "$VISICORE_STAGING_ADMIN_PASSWORD" '{username:$username,password:$password}')" | jq -r '.accessToken')"
[[ -n "$access_token" && "$access_token" != null ]] || { echo "staging 管理员登录未返回会话。" >&2; exit 1; }

catalog="$(register_descriptor candidate-governance/release-descriptor.json candidate-governance/release-descriptor.signature.base64)"
catalog_id="$(jq -r '.id' <<< "$catalog")"
run_core_plan "$catalog_id"
wait_for_http /healthz
wait_for_http /readyz
run_edge_plan "$catalog_id"

# 故障描述使用同一发行公钥签名，但指向不存在的 Docker Hub digest；预期 Core Host Agent 自动回滚并暂停金丝雀计划。
fault_catalog="$(register_descriptor candidate-governance/staging-fault-descriptor.json candidate-governance/staging-fault-descriptor.signature.base64)"
fault_catalog_id="$(jq -r '.id' <<< "$fault_catalog")"
fault_plan="$(api POST /api/v1/admin/upgrade-plans "$(jq -n --arg catalog "$fault_catalog_id" '{releaseCatalogId:$catalog,targetScope:"core"}')")"
fault_plan_id="$(jq -r '.id' <<< "$fault_plan")"
api POST "/api/v1/admin/upgrade-plans/${fault_plan_id}/start" > /dev/null
wait_for_plan "$fault_plan_id" paused > /dev/null
fault_timeline="$(api GET "/api/v1/admin/upgrade-plans/${fault_plan_id}/timeline")"
backup_id="$(jq -r '.items[] | select(.protectionBackupId != null) | .protectionBackupId' <<< "$fault_timeline" | head -n 1)"
jq -e '.items[] | select(.failureKind == "core_upgrade_failed_rolled_back" and .protectionBackupId != null)' <<< "$fault_timeline" > /dev/null
wait_for_http /healthz
wait_for_http /readyz

write_result passed
trap - ERR
echo "${platform} staging RC 演练完成：${candidate_tag}"
