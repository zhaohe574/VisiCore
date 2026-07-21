#!/usr/bin/env bash
set -Eeuo pipefail

binary_path="${1:?请传入已发布的 VisiCore.CoreHostAgent 二进制路径}"
install_root="${VISICORE_CORE_INSTALL_ROOT:-/opt/visicore}"
config_directory="${VISICORE_CORE_HOST_AGENT_CONFIG_DIRECTORY:-/etc/visicore}"
exchange_directory="${VISICORE_UPGRADE_EXCHANGE_DIRECTORY:-${install_root}/upgrade-exchange}"
compose_file="${VISICORE_CORE_COMPOSE_FILE:-${install_root}/compose.yaml}"

if [[ ! -f "${binary_path}" ]]; then
    echo "未找到 Core Host Agent 二进制。" >&2
    exit 2
fi

install -d -m 0750 "${install_root}" "${exchange_directory}" "${config_directory}" /usr/local/lib/visicore
install -m 0755 "${binary_path}" /usr/local/lib/visicore/VisiCore.CoreHostAgent
if [[ ! -f "${config_directory}/core-host-agent.json" ]]; then
    install -m 0640 deploy/core/core-host-agent.json.example "${config_directory}/core-host-agent.json"
fi
install -m 0644 deploy/core/visicore-core-host-agent.service /etc/systemd/system/visicore-core-host-agent.service

# 首次在线升级前将正在运行的不可变镜像记为已知良好版本，拒绝以可变标签作为自动回退源。
container_id="$(/usr/bin/docker compose --file "${compose_file}" ps --quiet visicore-core)"
if [[ -z "${container_id}" ]]; then
    echo "未找到正在运行的 visicore-core 容器，拒绝启用在线升级。" >&2
    exit 2
fi
image_id="$(/usr/bin/docker inspect --format '{{.Image}}' "${container_id}")"
image_reference="$(/usr/bin/docker image inspect --format '{{index .RepoDigests 0}}' "${image_id}")"
if [[ "${image_reference}" != visicore/*@sha256:* ]]; then
    echo "当前中心镜像不是可验证的 Docker Hub digest，先完成一次受控基线部署后再启用在线升级。" >&2
    exit 2
fi
cat > "${install_root}/compose.known-good.yaml" <<EOF
services:
  visicore-core:
    image: ${image_reference}
EOF
chmod 0640 "${install_root}/compose.known-good.yaml"
systemctl daemon-reload
systemctl enable --now visicore-core-host-agent.service
