#!/usr/bin/env bash
set -euo pipefail

# 首次部署仅安装受限 Host Agent 和本地配置通道；不写入中心地址、注册码或发行密钥。
ROOT_DIRECTORY="${VISICORE_EDGE_ROOT:-/var/lib/visicore/edge-node}"
COMPOSE_FILE="${VISICORE_EDGE_COMPOSE_FILE:-/opt/visicore/edge/edge-agent.compose.yaml}"
HOST_BINARY="${VISICORE_EDGE_HOST_BINARY:-/opt/visicore/edge-host-agent/VisiCore.EdgeHostAgent}"
CONFIG_GROUP="${VISICORE_EDGE_CONFIG_GROUP:-visicore-edge-config}"
CONFIG_GID="${VISICORE_EDGE_CONFIG_GID:-1654}"
HOST_USER="${VISICORE_EDGE_HOST_USER:-visicore-host-agent}"
SCRIPT_DIRECTORY="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
case "$(uname -m)" in
  x86_64) HOST_ARCHITECTURE="linux-x64" ;;
  aarch64|arm64) HOST_ARCHITECTURE="linux-arm64" ;;
  *) echo "不支持的 Host Agent 架构：$(uname -m)" >&2; exit 1 ;;
esac
HOST_BINARY_SOURCE="${VISICORE_EDGE_HOST_BINARY_SOURCE:-${SCRIPT_DIRECTORY}/host-agent/${HOST_ARCHITECTURE}/VisiCore.EdgeHostAgent}"

if [ ! -x "${HOST_BINARY_SOURCE}" ]; then
  echo "未找到当前架构的 Host Agent 可执行文件：${HOST_BINARY_SOURCE}" >&2
  exit 1
fi

if ! getent group "${CONFIG_GROUP}" >/dev/null; then
  groupadd --system --gid "${CONFIG_GID}" "${CONFIG_GROUP}"
fi
if ! id "${HOST_USER}" >/dev/null 2>&1; then
  useradd --system --no-create-home --gid "${CONFIG_GROUP}" --groups docker --shell /usr/sbin/nologin "${HOST_USER}"
fi
usermod -aG docker "${HOST_USER}"

install -d -o "${HOST_USER}" -g "${CONFIG_GROUP}" -m 0770 "${ROOT_DIRECTORY}/config" "${ROOT_DIRECTORY}/state" "${ROOT_DIRECTORY}/host-exchange"
install -d -o "${HOST_USER}" -g "${CONFIG_GROUP}" -m 0750 /var/lib/visicore/edge-host-agent/{inbox,receipts,state,releases}
install -d -o root -g "${CONFIG_GROUP}" -m 0770 /etc/visicore/edge-host-agent
install -d -o root -g root -m 0755 "$(dirname "${HOST_BINARY}")"
install -m 0755 "${HOST_BINARY_SOURCE}" "${HOST_BINARY}"
if [ ! -f "${ROOT_DIRECTORY}/config/access.token" ]; then
  umask 0077
  openssl rand -hex 32 > "${ROOT_DIRECTORY}/config/access.token"
  chown "${HOST_USER}:${CONFIG_GROUP}" "${ROOT_DIRECTORY}/config/access.token"
  chmod 0640 "${ROOT_DIRECTORY}/config/access.token"
fi

cat > /etc/visicore/edge-host-agent/edge-host-agent.json <<EOF
{
  "HostAgent": {
    "Enabled": false,
    "AllowExecution": false,
    "OperationInboxDirectory": "/var/lib/visicore/edge-host-agent/inbox",
    "OperationReceiptDirectory": "/var/lib/visicore/edge-host-agent/receipts",
    "OperationStateDirectory": "/var/lib/visicore/edge-host-agent/state",
    "ReleaseArtifactDirectory": "/var/lib/visicore/edge-host-agent/releases",
    "DockerComposeExecutablePath": "/usr/bin/docker",
    "ComposeFilePath": "${COMPOSE_FILE}",
    "ActiveReleaseComposeOverridePath": "${COMPOSE_FILE%/*}/compose.release.yaml",
    "ResourcePolicyComposeOverridePath": "${COMPOSE_FILE%/*}/compose.resources.yaml",
    "ComposeEnvironmentFilePath": "${COMPOSE_FILE%/*}/.env",
    "ConfigurationSocketPath": "${ROOT_DIRECTORY}/config/host-agent.sock",
    "ConfigurationTokenPath": "${ROOT_DIRECTORY}/config/access.token",
    "ManagedEdgeAgentConfigurationPath": "${ROOT_DIRECTORY}/state/edge-agent.json",
    "ManagedEdgeAgentBootstrapPath": "${ROOT_DIRECTORY}/state/bootstrap/bootstrap.json",
    "ManagedHostAgentConfigurationPath": "/etc/visicore/edge-host-agent/edge-host-agent.json",
    "ManagedResourcePolicyStatusPath": "${ROOT_DIRECTORY}/state/resource-policy-status.json",
    "ResourcePolicy": { "DiskWarningPercent": 85 },
    "ExecutionTimeoutSeconds": 600
  }
}
EOF
chown root:"${CONFIG_GROUP}" /etc/visicore/edge-host-agent/edge-host-agent.json
chmod 0660 /etc/visicore/edge-host-agent/edge-host-agent.json

install -m 0644 "${SCRIPT_DIRECTORY}/visicore-edge-host-agent.service" /etc/systemd/system/visicore-edge-host-agent.service
systemctl daemon-reload
systemctl enable --now visicore-edge-host-agent.service
