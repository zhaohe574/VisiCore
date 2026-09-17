#!/usr/bin/env bash
# VisiCore P2P 穿透服务端安装配置脚本
# 适用于 Debian / Ubuntu / CentOS / RHEL (Linux x86_64 / aarch64)

set -euo pipefail

P2P_CONFIG_DIR="/etc/video-platform"
P2P_CONFIG_FILE="${P2P_CONFIG_DIR}/p2p-config.toml"
P2P_DATA_DIR="/var/lib/video-platform/p2p"
SERVICE_FILE="/etc/systemd/system/visicore-p2p.service"
SERVER_VIRTUAL_IP="${SERVER_VIRTUAL_IP:-10.144.1.1}"
EASYTIER_VERSION="${EASYTIER_VERSION:-v2.6.4}"

echo "=== VisiCore 服务端 P2P 穿透安装配置 ==="

# 1. 检查 root 权限
if [ "$(id -u)" -ne 0 ]; then
    echo "错误: 请使用 root 或 sudo 运行此脚本" >&2
    exit 1
fi

# 2. 架构检查与二进制下载
ARCH=$(uname -m)
case "${ARCH}" in
    x86_64|amd64)
        EASYTIER_ARCH="x86_64"
        ;;
    aarch64|arm64)
        EASYTIER_ARCH="aarch64"
        ;;
    *)
        echo "错误: 暂不支持的架构: ${ARCH}" >&2
        exit 1
        ;;
esac

echo "[1/4] 检查 easytier-core 二进制文件..."
if ! command -v easytier-core &>/dev/null; then
    echo "正在下载 EasyTier ${EASYTIER_VERSION} (${EASYTIER_ARCH})..."
    DOWNLOAD_URL="https://github.com/EasyTier/EasyTier/releases/download/${EASYTIER_VERSION}/easytier-linux-${EASYTIER_ARCH}-${EASYTIER_VERSION}.zip"
    TMP_DIR=$(mktemp -d)
    
    if command -v curl &>/dev/null; then
        curl -fsSL --connect-timeout 10 -m 60 "${DOWNLOAD_URL}" -o "${TMP_DIR}/easytier.zip" || {
            echo "主源下载超时，尝试使用镜像加速源..."
            curl -fsSL --connect-timeout 10 -m 60 "https://ghproxy.net/${DOWNLOAD_URL}" -o "${TMP_DIR}/easytier.zip"
        }
    else
        wget --timeout=60 -qO "${TMP_DIR}/easytier.zip" "${DOWNLOAD_URL}"
    fi

    unzip -q -o "${TMP_DIR}/easytier.zip" -d "${TMP_DIR}"
    install -m 755 "${TMP_DIR}/easytier-linux-${EASYTIER_ARCH}/easytier-core" /usr/local/bin/easytier-core
    install -m 755 "${TMP_DIR}/easytier-linux-${EASYTIER_ARCH}/easytier-cli" /usr/local/bin/easytier-cli
    rm -rf "${TMP_DIR}"
    echo "已成功安装 /usr/local/bin/easytier-core"
else
    echo "已存在 /usr/local/bin/easytier-core: $(easytier-core --version 2>&1 || true)"
fi

# 3. 准备配置目录和持久化文件
echo "[2/4] 检查与生成配置文件..."
mkdir -p "${P2P_CONFIG_DIR}" "${P2P_DATA_DIR}"

if [ ! -f "${P2P_CONFIG_FILE}" ]; then
    RANDOM_NET_NAME="visicore-$(head -c 4 /dev/urandom | xxd -p)"
    RANDOM_SECRET="$(head -c 16 /dev/urandom | xxd -p)"
    
    cat > "${P2P_CONFIG_FILE}" <<EOF
# VisiCore P2P 网络配置文件
instance_name = "visicore-server"
ipv4 = "${SERVER_VIRTUAL_IP}"

[network_identity]
network_name = "${RANDOM_NET_NAME}"
network_secret = "${RANDOM_SECRET}"

peers = [
    "tcp://public.easytier.top:11010",
    "udp://public.easytier.top:11010"
]

[flags]
enable_p2p = true
enable_vpn_portal = false
EOF
    chmod 600 "${P2P_CONFIG_FILE}"
    echo "已生成全新配置文件: ${P2P_CONFIG_FILE}"
else
    echo "使用现有配置文件: ${P2P_CONFIG_FILE}"
fi

# 4. 配置并启动 Systemd 服务
echo "[3/4] 配置 systemd 服务..."
cat > "${SERVICE_FILE}" <<EOF
[Unit]
Description=VisiCore P2P 穿透守护服务 (EasyTier)
Wants=network-online.target
After=network-online.target nginx.service

[Service]
Type=simple
User=root
WorkingDirectory=${P2P_DATA_DIR}
ExecStart=/usr/local/bin/easytier-core --config-file ${P2P_CONFIG_FILE}
Restart=always
RestartSec=5
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable visicore-p2p
systemctl restart visicore-p2p

echo "[4/4] 验证服务状态..."
sleep 2
if systemctl is-active --quiet visicore-p2p; then
    echo "=== VisiCore P2P 守护服务已正常运行! ==="
    echo "虚拟 IP: ${SERVER_VIRTUAL_IP}"
    echo "配置文件: ${P2P_CONFIG_FILE}"
    echo "你可以运行 'python3 tools/v2-p2p-pairing.py' 生成手机端配对二维码"
else
    echo "警告: visicore-p2p 服务未能正常运行，请使用 journalctl -u visicore-p2p 查看日志" >&2
    exit 1
fi
