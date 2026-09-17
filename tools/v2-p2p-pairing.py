#!/usr/bin/env python3
"""VisiCore 移动端 P2P 扫码配对凭据生成工具。

功能：
1. 读取服务端 P2P 配置 (/etc/video-platform/p2p-config.toml 或指定的本地配置)
2. 生成符合 VisiCore 手机客户端规范的配对数据结构
3. 在控制台打印 ASCII 二维码供移动端直接扫描
4. 将配对二维码 PNG 导出到 artifacts/ 目录
"""

import argparse
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SERVER_CONFIG = Path("/etc/video-platform/p2p-config.toml")
DEV_CONFIG = ROOT / "deploy" / "p2p-config.example.toml"
ARTIFACTS_DIR = ROOT / "artifacts"

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass


def parse_simple_toml(path: Path) -> dict:
    """简单解析 TOML 键值对，无需第三方 toml 依赖。"""
    data = {}
    peers = []
    in_peers = False
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("peers"):
            in_peers = True
            continue
        if in_peers:
            if line.startswith("]"):
                in_peers = False
                continue
            item = line.strip(', "')
            if item:
                peers.append(item)
            continue
        if "=" in line:
            key, val = line.split("=", 1)
            key = key.strip()
            val = val.strip().strip('"').strip("'")
            data[key] = val
    if peers:
        data["peers"] = peers
    return data


def generate_pairing_payload(config: dict, server_name: str = "VisiCore 监控服务") -> dict:
    virtual_ip = config.get("ipv4", "10.144.1.1")
    return {
        "version": 1,
        "protocol": "easytier-userspace",
        "serverName": server_name,
        "networkName": config.get("network_name", "visicore-default"),
        "networkSecret": config.get("network_secret", ""),
        "serverIp": "10.37.200.74",
        "lanIp": "10.37.200.74",
        "virtualIp": virtual_ip,
        "apiPort": 443,
        "apiPrefix": "/api/v2",
        "publicPeers": config.get("peers", ["tcp://public.easytier.top:11010"])
    }


def main():
    parser = argparse.ArgumentParser(description="VisiCore 移动端配对二维码生成工具")
    parser.add_argument("--config", help="P2P 配置文件路径", type=Path)
    parser.add_argument("--output", help="PNG 保存路径", type=Path)
    parser.add_argument("--server-name", default="VisiCore 生产服务", help="服务展示名称")
    parser.add_argument("--raw", action="store_true", help="只输出 JSON 字符串")
    args = parser.parse_args()

    config_path = args.config
    if not config_path:
        if DEFAULT_SERVER_CONFIG.exists():
            config_path = DEFAULT_SERVER_CONFIG
        elif DEV_CONFIG.exists():
            config_path = DEV_CONFIG
        else:
            # 自动生成示例开发配置
            DEV_CONFIG.parent.mkdir(parents=True, exist_ok=True)
            DEV_CONFIG.write_text(
                'instance_name = "visicore-dev"\n'
                'ipv4 = "10.144.1.1"\n'
                'network_name = "visicore-lan-dev"\n'
                'network_secret = "dev_secret_visicore_2026"\n'
                'peers = [\n    "tcp://public.easytier.top:11010"\n]\n',
                encoding="utf-8"
            )
            config_path = DEV_CONFIG

    if not config_path.exists():
        print(f"错误: 找不到配置文件 {config_path}", file=sys.stderr)
        sys.exit(1)

    config = parse_simple_toml(config_path)
    payload = generate_pairing_payload(config, server_name=args.server_name)
    payload_json = json.dumps(payload, ensure_ascii=False, indent=2)

    if args.raw:
        print(payload_json)
        return

    print("======================================================")
    print("  VisiCore 移动端扫码配对凭据 (用户态免权限直连)")
    print("======================================================")
    print(f"网络代号: {payload['networkName']}")
    print(f"服务端虚拟 IP: {payload['serverIp']}:{payload['apiPort']}")
    print("------------------------------------------------------")

    try:
        import qrcode
        qr = qrcode.QRCode(
            version=None,
            error_correction=qrcode.constants.ERROR_CORRECT_M,
            box_size=10,
            border=2,
        )
        qr.add_data(json.dumps(payload, separators=(',', ':')))
        qr.make(fit=True)

        # 控制台打印 ASCII 二维码
        print("\n请使用 VisiCore 手机客户端【扫一扫】扫描以下二维码：\n")
        qr.print_ascii(invert=True)

        # 保存为 PNG 图像文件
        output_png = args.output or (ARTIFACTS_DIR / "visicore-pairing-qr.png")
        output_png.parent.mkdir(parents=True, exist_ok=True)
        img = qr.make_image(fill_color="black", back_color="white")
        img.save(output_png)
        print(f"\n配对二维码已保存至: {output_png}")

        # 同时保存 JSON 文本文件供复制
        output_json = output_png.with_suffix(".json")
        output_json.write_text(payload_json, encoding="utf-8")
        print(f"配对数据已同步保存至: {output_json}")

    except ImportError:
        print("提示: 未安装 qrcode 库，打印原始 JSON 内容：")
        print(payload_json)


if __name__ == "__main__":
    main()
