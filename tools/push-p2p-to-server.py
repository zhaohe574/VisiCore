#!/usr/bin/env python3
"""推送 P2P 守护配置及服务到 Linux 正式机 (10.37.200.74) 并自动激活。

使用方法：
  python tools/push-p2p-to-server.py [--password <SSH密码>]
或先在环境变量设置：
  $env:VIDEO_PLATFORM_SSH_PASSWORD="<密码>"
  python tools/push-p2p-to-server.py
"""

import argparse
import getpass
import os
from pathlib import Path
import socket
import sys
import paramiko

ROOT = Path(__file__).resolve().parent.parent
HOST = "10.37.200.74"
USER = "liteware"


def main():
    parser = argparse.ArgumentParser(description="推送 P2P 穿透服务到 Linux 正式机")
    parser.add_argument("--password", help="SSH 登录密码 (留空则从环境变量或交互式输入读取)")
    parser.add_argument("--bind-ip", help="网卡绑定 IP (可选)")
    args = parser.parse_args()

    password = args.password or os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD")
    if not password:
        password = getpass.getpass(f"请输入正式机 [{USER}@{HOST}] 的 SSH/Sudo 密码: ")

    if not password:
        print("错误: 密码不能为空", file=sys.stderr)
        sys.exit(1)

    print(f"=== 正在连接正式机 {USER}@{HOST}:22 ===")
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())

    sock = None
    bind_ip = args.bind_ip or os.environ.get("VIDEO_PLATFORM_BIND_IP", "10.37.6.210" if os.name == "nt" else None)
    if bind_ip:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.bind((bind_ip, 0))
            s.connect((HOST, 22))
            sock = s
        except Exception:
            sock = None

    try:
        client.connect(
            HOST,
            username=USER,
            password=password,
            sock=sock,
            timeout=15,
            look_for_keys=False,
            allow_agent=False,
        )
    except Exception as e:
        print(f"SSH 连接失败: {e}", file=sys.stderr)
        sys.exit(1)

    print("已成功建立 SSH 连接，正在上传 P2P 部署脚本与配对生成工具...")
    remote_dir = "/home/liteware/p2p-deploy"

    def sudo_exec(cmd):
        stdin, stdout, stderr = client.exec_command(f"sudo -S -p '' {cmd}", timeout=180)
        stdin.write(password + "\n")
        stdin.flush()
        stdin.channel.shutdown_write()
        out = stdout.read().decode("utf-8", "replace")
        err = stderr.read().decode("utf-8", "replace")
        code = stdout.channel.recv_exit_status()
        if code != 0:
            raise RuntimeError(f"命令执行失败 (退出码 {code}):\n{err}\n{out}")
        return out

    try:
        # 1. 建立远端目录
        client.exec_command(f"mkdir -p {remote_dir}")

        # 2. SFTP 上传
        with client.open_sftp() as sftp:
            local_setup = ROOT / "deploy" / "p2p-setup.sh"
            local_pairing = ROOT / "tools" / "v2-p2p-pairing.py"
            sftp.put(str(local_setup), f"{remote_dir}/p2p-setup.sh")
            sftp.put(str(local_pairing), f"{remote_dir}/v2-p2p-pairing.py")
            sftp.chmod(f"{remote_dir}/p2p-setup.sh", 0o755)
            sftp.chmod(f"{remote_dir}/v2-p2p-pairing.py", 0o755)

            # 如果本地已有下载好的 easytier 二进制，直接上传以避免服务器联网下载受阻
            bin_dir = ROOT / "deploy" / "bin" / "easytier-linux-x86_64"
            if (bin_dir / "easytier-core").exists():
                print("检测到本地已预下载 EasyTier Linux 二进制，正在直接上传到服务器...", flush=True)
                sftp.put(str(bin_dir / "easytier-core"), f"{remote_dir}/easytier-core")
                sftp.put(str(bin_dir / "easytier-cli"), f"{remote_dir}/easytier-cli")
                sftp.chmod(f"{remote_dir}/easytier-core", 0o755)
                sftp.chmod(f"{remote_dir}/easytier-cli", 0o755)
                sudo_exec(f"install -m 755 {remote_dir}/easytier-core /usr/local/bin/easytier-core")
                sudo_exec(f"install -m 755 {remote_dir}/easytier-cli /usr/local/bin/easytier-cli")
                print("EasyTier 二进制已直接安装至 /usr/local/bin/", flush=True)

        print("上传完成，正在服务器端执行安装配置并启动 P2P 守护进程 (需要 root 权限)...", flush=True)
        out = sudo_exec(f"bash {remote_dir}/p2p-setup.sh")
        print("服务端安装日志输出：", flush=True)
        print(out.strip(), flush=True)

        print("\n正在生成正式机专属配对凭据与二维码...", flush=True)
        pairing_out = sudo_exec(f"python3 {remote_dir}/v2-p2p-pairing.py --output {remote_dir}/visicore-pairing-qr.png")
        print(pairing_out.strip(), flush=True)

        # 回传配对文件到本地 artifacts
        with client.open_sftp() as sftp:
            local_artifacts = ROOT / "artifacts"
            local_artifacts.mkdir(parents=True, exist_ok=True)
            try:
                sftp.get(f"{remote_dir}/visicore-pairing-qr.png", str(local_artifacts / "production-pairing-qr.png"))
                print(f"\n已将正式机二维码图片下载至: {local_artifacts / 'production-pairing-qr.png'}", flush=True)
            except Exception as e:
                print(f"下载二维码图片提示: {e}", flush=True)
            try:
                sftp.get(f"{remote_dir}/visicore-pairing-qr.json", str(local_artifacts / "production-pairing-qr.json"))
                print(f"已将正式机配对配置下载至: {local_artifacts / 'production-pairing-qr.json'}", flush=True)
            except Exception:
                pass

        print("\n=== 正式机 P2P 守护服务已成功部署并启动！ ===", flush=True)

    finally:
        client.close()


if __name__ == "__main__":
    main()
