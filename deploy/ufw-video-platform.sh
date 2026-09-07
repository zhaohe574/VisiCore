#!/usr/bin/env bash
set -euo pipefail

# 仅开放平台入口和客户端实际使用的媒体端口，数据库、适配器和 ZLMediaKit 管理端口不对外开放。
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp comment 'SSH 运维'
ufw allow 80/tcp comment '平台 Web 和 API'
ufw allow 18080/tcp comment 'ZLMediaKit HTTP-FLV/HLS'
ufw allow 18554/tcp comment 'ZLMediaKit RTSP'
ufw --force enable
ufw status verbose
