#!/usr/bin/env bash
set -Eeuo pipefail

# Edge Linux 部署包入口。业务容器不获得 Docker Socket，升级权只在 Host Agent 中存在。
PACKAGE_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
INSTALL_ROOT="${VISICORE_EDGE_INSTALL_ROOT:-/opt/visicore/edge}"
STATE_ROOT="${VISICORE_EDGE_STATE_ROOT:-/var/lib/visicore/edge-node}"
HOST_ROOT="${VISICORE_EDGE_HOST_ROOT:-/var/lib/visicore/edge-host-agent}"
CONFIG_ROOT="${VISICORE_EDGE_CONFIG_ROOT:-/etc/visicore/edge-host-agent}"
COMMAND="${1:-install}"

require_root() { [[ "${EUID}" -eq 0 ]] || { echo "请以 root 或 sudo 执行该命令。" >&2; exit 77; }; }

require_package() {
  for path in package-manifest.json release/release-descriptor.json release/release-descriptor.signature.base64 release/release-public-key.pem edge-agent.compose.yaml host-agent/VisiCore.EdgeHostAgent; do
    [[ -s "${PACKAGE_ROOT}/${path}" ]] || { echo "部署包缺少必要文件：${path}" >&2; exit 65; }
  done
}

detect_platform() {
  [[ -r /etc/os-release ]] || { echo "无法识别 Linux 发行版。" >&2; exit 69; }
  # shellcheck disable=SC1091
  source /etc/os-release
  case "${ID}:${VERSION_ID:-}" in
    ubuntu:22.04|ubuntu:24.04|debian:12|rhel:9*|rocky:9*|almalinux:9*) ;;
    *) echo "当前仅支持 Ubuntu 22.04/24.04、Debian 12、RHEL/Rocky/AlmaLinux 9。" >&2; exit 69 ;;
  esac
  case "$(uname -m)" in x86_64) package_architecture=amd64 ;; aarch64|arm64) package_architecture=arm64 ;; *) echo "不支持的 CPU 架构。" >&2; exit 69 ;; esac
}

ensure_package_tools() {
  case "${ID}" in
    ubuntu|debian)
      apt-get update
      apt-get install -y ca-certificates curl gnupg jq openssl
      ;;
    rhel|rocky|almalinux)
      dnf -y install ca-certificates curl jq openssl
      ;;
  esac
}

install_docker() {
  if command -v docker >/dev/null && docker compose version >/dev/null 2>&1; then
    return
  fi
  case "${ID}" in
    ubuntu|debian)
      install -m 0755 -d /etc/apt/keyrings
      curl -fsSL "https://download.docker.com/linux/${ID}/gpg" -o /etc/apt/keyrings/docker.asc
      chmod a+r /etc/apt/keyrings/docker.asc
      local codename="${VERSION_CODENAME:-${UBUNTU_CODENAME:-}}"
      [[ -n "${codename}" ]] || { echo "无法确定 APT 发行代号。" >&2; exit 72; }
      printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' "$(dpkg --print-architecture)" "${ID}" "${codename}" > /etc/apt/sources.list.d/docker.list
      apt-get update
      apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
      ;;
    rhel|rocky|almalinux)
      dnf -y install dnf-plugins-core
      dnf config-manager --add-repo "https://download.docker.com/linux/centos/docker-ce.repo"
      dnf -y install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
      ;;
  esac
  systemctl enable --now docker
}

verify_release_signature() {
  local canonical signature
  canonical="$(mktemp)"
  signature="$(mktemp)"
  trap 'rm -f "${canonical}" "${signature}"' RETURN
  jq -cS -j . "${PACKAGE_ROOT}/release/release-descriptor.json" > "${canonical}"
  base64 --decode "${PACKAGE_ROOT}/release/release-descriptor.signature.base64" > "${signature}"
  openssl dgst -sha256 -verify "${PACKAGE_ROOT}/release/release-public-key.pem" -signature "${signature}" -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:-1 "${canonical}" > /dev/null
  rm -f "${canonical}" "${signature}"
  trap - RETURN
}

preflight() {
  require_package
  detect_platform
  command -v jq >/dev/null || { echo "缺少 jq；请执行 install 自动准备依赖。" >&2; exit 72; }
  command -v openssl >/dev/null || { echo "缺少 openssl；请执行 install 自动准备依赖。" >&2; exit 72; }
  [[ "$(jq -r '.architecture' "${PACKAGE_ROOT}/package-manifest.json")" == "${package_architecture}" ]] || { echo "部署包架构与宿主机不一致。" >&2; exit 69; }
  verify_release_signature
  jq -e --arg architecture "${package_architecture}" '.component == "edge" and .architecture == $architecture and (.edgeImage | test("^visicore/visicore-edge@sha256:[0-9a-f]{64}$"))' "${PACKAGE_ROOT}/package-manifest.json" > /dev/null
  command -v docker >/dev/null || { echo "缺少 Docker Engine；请执行 install 自动准备依赖。" >&2; exit 72; }
  docker compose version >/dev/null
  echo "预检通过：${ID} ${VERSION_ID:-}，${package_architecture}。"
}

write_configuration() {
  local image key_id
  image="$(jq -r '.edgeImage' "${PACKAGE_ROOT}/package-manifest.json")"
  key_id="$(jq -r '.signingPublicKeyId' "${PACKAGE_ROOT}/package-manifest.json")"
  if ! getent group visicore-edge-config >/dev/null; then
    groupadd --system --gid 1654 visicore-edge-config
  fi
  if ! id visicore-host-agent >/dev/null 2>&1; then
    useradd --system --no-create-home --gid visicore-edge-config --groups docker --shell /usr/sbin/nologin visicore-host-agent
  fi
  usermod -aG docker visicore-host-agent
  install -d -o visicore-host-agent -g visicore-edge-config -m 0770 "${STATE_ROOT}/config" "${STATE_ROOT}/state" "${HOST_ROOT}"
  install -d -o visicore-host-agent -g visicore-edge-config -m 0750 "${HOST_ROOT}"/{inbox,receipts,state,releases}
  install -d -o root -g visicore-edge-config -m 0750 "${INSTALL_ROOT}" "${CONFIG_ROOT}" /opt/visicore/edge-host-agent
  if [[ ! -f "${STATE_ROOT}/config/access.token" ]]; then
    umask 0077
    openssl rand -hex 32 > "${STATE_ROOT}/config/access.token"
    chown visicore-host-agent:visicore-edge-config "${STATE_ROOT}/config/access.token"
    chmod 0640 "${STATE_ROOT}/config/access.token"
  fi
  install -m 0644 "${PACKAGE_ROOT}/edge-agent.compose.yaml" "${INSTALL_ROOT}/edge-agent.compose.yaml"
  cat > "${INSTALL_ROOT}/.env" <<EOF
VISICORE_EDGE_NODE_IMAGE=${image}
EDGE_STATE_DIRECTORY=${STATE_ROOT}/state
EDGE_HOST_EXCHANGE_DIRECTORY=${HOST_ROOT}
EDGE_HOST_CONFIG_DIRECTORY=${STATE_ROOT}/config
EDGE_CONFIG_PORT=18081
EOF
  chmod 0640 "${INSTALL_ROOT}/.env"
  install -m 0755 "${PACKAGE_ROOT}/host-agent/VisiCore.EdgeHostAgent" /opt/visicore/edge-host-agent/VisiCore.EdgeHostAgent
  install -m 0640 "${PACKAGE_ROOT}/release/release-public-key.pem" "${CONFIG_ROOT}/release-public-key.pem"
  cat > "${CONFIG_ROOT}/edge-host-agent.json" <<EOF
{
  "HostAgent": {
    "Enabled": true,
    "AllowExecution": true,
    "OperationInboxDirectory": "${HOST_ROOT}/inbox",
    "OperationReceiptDirectory": "${HOST_ROOT}/receipts",
    "OperationStateDirectory": "${HOST_ROOT}/state",
    "ReleaseArtifactDirectory": "${HOST_ROOT}/releases",
    "AllowedArtifactHosts": ["github.com", "objects.githubusercontent.com"],
    "MaximumArtifactBytes": 2147483648,
    "SigningPublicKeyPath": "${CONFIG_ROOT}/release-public-key.pem",
    "SigningPublicKeyId": "${key_id}",
    "DockerComposeExecutablePath": "$(command -v docker)",
    "ComposeFilePath": "${INSTALL_ROOT}/edge-agent.compose.yaml",
    "ActiveReleaseComposeOverridePath": "${INSTALL_ROOT}/compose.release.yaml",
    "ResourcePolicyComposeOverridePath": "${INSTALL_ROOT}/compose.resources.yaml",
    "ComposeEnvironmentFilePath": "${INSTALL_ROOT}/.env",
    "ConfigurationSocketPath": "${STATE_ROOT}/config/host-agent.sock",
    "ConfigurationTokenPath": "${STATE_ROOT}/config/access.token",
    "ManagedEdgeAgentConfigurationPath": "${STATE_ROOT}/state/edge-agent.json",
    "ManagedEdgeAgentBootstrapPath": "${STATE_ROOT}/state/bootstrap/bootstrap.json",
    "ManagedHostAgentConfigurationPath": "${CONFIG_ROOT}/edge-host-agent.json",
    "ManagedResourcePolicyStatusPath": "${STATE_ROOT}/state/resource-policy-status.json",
    "ResourcePolicy": { "DiskWarningPercent": 85 },
    "ExecutionTimeoutSeconds": 600
  }
}
EOF
  chmod 0640 "${CONFIG_ROOT}/edge-host-agent.json"
  install -m 0644 "${PACKAGE_ROOT}/systemd/visicore-edge-host-agent.service" /etc/systemd/system/visicore-edge-host-agent.service
}

install_edge() {
  require_root
  detect_platform
  ensure_package_tools
  install_docker
  preflight
  if [[ -f "${INSTALL_ROOT}/edge-agent.compose.yaml" && "${VISICORE_ALLOW_EXISTING_INSTALL:-false}" != true ]]; then
    echo "检测到已有 Edge 部署。升级和回退请使用后台受签名升级计划。" >&2
    exit 73
  fi
  write_configuration
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/edge-agent.compose.yaml" pull
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/edge-agent.compose.yaml" up --detach
  systemctl daemon-reload
  systemctl enable --now visicore-edge-host-agent.service
  echo "Edge 已部署。请在本机访问 http://127.0.0.1:18081 完成中心配对。"
}

status() {
  [[ -f "${INSTALL_ROOT}/edge-agent.compose.yaml" ]] || { echo "未找到 Edge 部署。" >&2; exit 75; }
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/edge-agent.compose.yaml" ps
}

uninstall() {
  require_root
  [[ -f "${INSTALL_ROOT}/edge-agent.compose.yaml" ]] || { echo "未找到 Edge 部署。" >&2; exit 75; }
  systemctl disable --now visicore-edge-host-agent.service || true
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/edge-agent.compose.yaml" down --remove-orphans
  echo "Edge 容器已停止。状态目录保留，避免意外删除节点身份和回滚资料。"
}

case "${COMMAND}" in
  preflight) preflight ;;
  install) install_edge ;;
  upgrade|rollback) echo "${COMMAND} 必须通过后台受签名升级计划执行。" >&2; exit 78 ;;
  status) status ;;
  uninstall) uninstall ;;
  *) echo "用法：$0 {preflight|install|upgrade|rollback|status|uninstall}" >&2; exit 64 ;;
esac
