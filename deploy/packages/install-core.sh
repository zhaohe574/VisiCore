#!/usr/bin/env bash
set -Eeuo pipefail

# Core Linux 部署包入口。包内仅包含部署资料和 Host Agent，不包含 Docker 镜像层。
PACKAGE_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
INSTALL_ROOT="${VISICORE_CORE_INSTALL_ROOT:-/opt/visicore}"
CONFIG_ROOT="${VISICORE_CORE_CONFIG_ROOT:-/etc/visicore}"
SERVICE_NAME="visicore-core-host-agent.service"
COMMAND="${1:-install}"

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    echo "请以 root 或 sudo 执行该命令。" >&2
    exit 77
  fi
}

require_package() {
  for path in package-manifest.json release/release-descriptor.json release/release-descriptor.signature.base64 release/release-public-key.pem compose.yaml host-agent/VisiCore.CoreHostAgent; do
    [[ -s "${PACKAGE_ROOT}/${path}" ]] || { echo "部署包缺少必要文件：${path}" >&2; exit 65; }
  done
}

detect_platform() {
  [[ -r /etc/os-release ]] || { echo "无法识别 Linux 发行版。" >&2; exit 69; }
  # shellcheck disable=SC1091
  source /etc/os-release
  case "${ID}:${VERSION_ID:-}" in
    ubuntu:22.04|ubuntu:24.04|debian:12|rhel:9*|rocky:9*|almalinux:9*) ;;
    *) echo "当前仅支持 Ubuntu 22.04/24.04、Debian 12、RHEL/Rocky/AlmaLinux 9：${ID} ${VERSION_ID:-unknown}" >&2; exit 69 ;;
  esac
  case "$(uname -m)" in
    x86_64) package_architecture="amd64" ;;
    aarch64|arm64) package_architecture="arm64" ;;
    *) echo "不支持的 CPU 架构：$(uname -m)" >&2; exit 69 ;;
  esac
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
  manifest_architecture="$(jq -r '.architecture' "${PACKAGE_ROOT}/package-manifest.json")"
  [[ "${package_architecture}" == "${manifest_architecture}" ]] || { echo "部署包架构与宿主机不一致。" >&2; exit 69; }
  local available_kib
  available_kib="$(df -Pk /opt | awk 'NR == 2 {print $4}')"
  [[ "${available_kib}" =~ ^[0-9]+$ && "${available_kib}" -ge 10485760 ]] || { echo "部署目录至少需要 10 GiB 可用空间。" >&2; exit 70; }
  if command -v ss >/dev/null && ss -ltn '( sport = :8080 or sport = :8443 )' | grep -q LISTEN; then
    echo "8080 或 8443 已被占用；请通过环境变量调整监听端口后再部署。" >&2
    exit 71
  fi
  verify_release_signature
  jq -e --arg component core --arg architecture "${package_architecture}" '
    .component == $component and .architecture == $architecture and (.coreImage | test("^visicore/visicore-core@sha256:[0-9a-f]{64}$"))
  ' "${PACKAGE_ROOT}/package-manifest.json" > /dev/null
  echo "预检通过：${ID} ${VERSION_ID:-}，${package_architecture}。"
}

install_docker() {
  if command -v docker >/dev/null && docker compose version >/dev/null 2>&1; then
    return
  fi
  echo "正在通过 Docker 官方软件源安装 Docker Engine 与 Compose 插件。"
  case "${ID}" in
    ubuntu|debian)
      apt-get update
      apt-get install -y ca-certificates curl gnupg
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
      command -v dnf >/dev/null || { echo "需要 dnf 才能安装 Docker。" >&2; exit 72; }
      dnf -y install dnf-plugins-core
      dnf config-manager --add-repo "https://download.docker.com/linux/centos/docker-ce.repo"
      dnf -y install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
      ;;
  esac
  systemctl enable --now docker
  docker compose version >/dev/null
}

write_configuration() {
  local image key_id
  image="$(jq -r '.coreImage' "${PACKAGE_ROOT}/package-manifest.json")"
  key_id="$(jq -r '.signingPublicKeyId' "${PACKAGE_ROOT}/package-manifest.json")"
  install -d -m 0750 "${INSTALL_ROOT}" "${INSTALL_ROOT}/upgrade-exchange" "${CONFIG_ROOT}" /usr/local/lib/visicore
  install -m 0644 "${PACKAGE_ROOT}/compose.yaml" "${INSTALL_ROOT}/compose.yaml"
  cat > "${INSTALL_ROOT}/.env" <<EOF
VISICORE_CORE_IMAGE=${image}
VISICORE_CORE_UPGRADE_ENABLED=true
VISICORE_UPGRADE_EXCHANGE_DIRECTORY=${INSTALL_ROOT}/upgrade-exchange
VISICORE_POSTGRES_VOLUME=visicore_postgres-data
VISICORE_CONFIG_VOLUME=visicore_visicore-config
VISICORE_BACKUPS_VOLUME=visicore_visicore-backups
VISICORE_EXPORTS_VOLUME=visicore_api-exports
VISICORE_NETWORK=visicore-network
ADMIN_HTTP_BIND_ADDRESS=127.0.0.1
ADMIN_HTTP_PORT=8080
ADMIN_HTTPS_BIND_ADDRESS=127.0.0.1
ADMIN_HTTPS_PORT=8443
EOF
  chmod 0640 "${INSTALL_ROOT}/.env"
  install -m 0755 "${PACKAGE_ROOT}/host-agent/VisiCore.CoreHostAgent" /usr/local/lib/visicore/VisiCore.CoreHostAgent
  install -m 0640 "${PACKAGE_ROOT}/release/release-public-key.pem" "${CONFIG_ROOT}/release-public-key.pem"
  cat > "${CONFIG_ROOT}/core-host-agent.json" <<EOF
{
  "CoreHostAgent": {
    "Enabled": true,
    "ExchangeDirectory": "${INSTALL_ROOT}/upgrade-exchange",
    "SigningPublicKeyPath": "${CONFIG_ROOT}/release-public-key.pem",
    "SigningPublicKeyId": "${key_id}",
    "DockerExecutablePath": "$(command -v docker)",
    "ComposeFilePath": "${INSTALL_ROOT}/compose.yaml",
    "ComposeOverridePath": "${INSTALL_ROOT}/compose.release.yaml",
    "CoreServiceName": "visicore-core",
    "HealthUrl": "http://127.0.0.1:8080/healthz",
    "ExecutionTimeoutSeconds": 900,
    "HealthTimeoutSeconds": 300
  }
}
EOF
  chmod 0640 "${CONFIG_ROOT}/core-host-agent.json"
  install -m 0644 "${PACKAGE_ROOT}/systemd/visicore-core-host-agent.service" /etc/systemd/system/visicore-core-host-agent.service
}

wait_for_health() {
  local deadline=$((SECONDS + 300))
  until curl --fail --silent --show-error --max-time 5 http://127.0.0.1:8080/healthz > /dev/null; do
    (( SECONDS < deadline )) || { echo "等待 Core 健康检查超时。" >&2; return 1; }
    sleep 5
  done
}

install_core() {
  require_root
  detect_platform
  ensure_package_tools
  preflight
  install_docker
  if [[ -f "${INSTALL_ROOT}/compose.yaml" && "${VISICORE_ALLOW_EXISTING_INSTALL:-false}" != true ]]; then
    echo "检测到已有 Core 部署。升级请使用后台受签名升级计划；如确需重新安装，请显式设置 VISICORE_ALLOW_EXISTING_INSTALL=true。" >&2
    exit 73
  fi
  write_configuration
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/compose.yaml" pull visicore-core
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/compose.yaml" up --detach visicore-core
  wait_for_health
  local container_id image_reference
  container_id="$(docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/compose.yaml" ps --quiet visicore-core)"
  image_reference="$(docker inspect --format '{{index .RepoDigests 0}}' "$(docker inspect --format '{{.Image}}' "${container_id}")")"
  [[ "${image_reference}" == visicore/visicore-core@sha256:* ]] || { echo "Core 未以 Docker Hub digest 运行。" >&2; exit 74; }
  printf 'services:\n  visicore-core:\n    image: %s\n' "${image_reference}" > "${INSTALL_ROOT}/compose.known-good.yaml"
  chmod 0640 "${INSTALL_ROOT}/compose.known-good.yaml"
  systemctl daemon-reload
  systemctl enable --now "${SERVICE_NAME}"
  echo "Core 已部署。请在本机访问 http://127.0.0.1:8080/admin 完成初始化。"
}

status() {
  if [[ -f "${INSTALL_ROOT}/compose.yaml" ]]; then
    docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/compose.yaml" ps
  else
    echo "未找到 Core 部署：${INSTALL_ROOT}" >&2
    exit 75
  fi
}

uninstall() {
  require_root
  [[ -f "${INSTALL_ROOT}/compose.yaml" ]] || { echo "未找到 Core 部署。" >&2; exit 75; }
  systemctl disable --now "${SERVICE_NAME}" || true
  docker compose --env-file "${INSTALL_ROOT}/.env" --file "${INSTALL_ROOT}/compose.yaml" down --remove-orphans
  echo "Core 容器已停止。命名卷、备份和配置保持不变；确认完成备份恢复演练后再手动删除。"
}

case "${COMMAND}" in
  preflight) preflight ;;
  install) install_core ;;
  upgrade|rollback)
    echo "${COMMAND} 必须通过后台受签名升级计划执行，以保留保护备份和回滚事务。" >&2
    exit 78
    ;;
  status) status ;;
  uninstall) uninstall ;;
  *) echo "用法：$0 {preflight|install|upgrade|rollback|status|uninstall}" >&2; exit 64 ;;
esac
