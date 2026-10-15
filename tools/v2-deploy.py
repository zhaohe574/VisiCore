"""上传已构建产物并安装候选版，凭据从环境读取。"""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import posixpath
import stat
import paramiko

ROOT = Path(__file__).resolve().parent.parent
# 本机未跟踪的凭据文件；不含任何口令的模板见 docs/凭据模板.example.txt。
# 与 tools/v2-refactor-verify.py 使用同一文件，避免口令出现在命令行或环境变量里。
LOCAL_CREDENTIALS = ROOT / ".secrets" / "deploy-credentials.txt"


def read_local_credential(name):
    """从 .secrets/deploy-credentials.txt 读取 KEY=VALUE（该文件已加入 .gitignore）。"""
    if not LOCAL_CREDENTIALS.exists():
        return None
    for line in LOCAL_CREDENTIALS.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line.startswith(name + "="):
            value = line.split("=", 1)[1].strip()
            if value:
                return value
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--switch", action="store_true")
    parser.add_argument("--release")
    parser.add_argument("--config-only", action="store_true")
    parser.add_argument("--password")
    parser.add_argument("--bind-ip")
    args = parser.parse_args()
    release = args.release or ("2.2.0-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"))
    if any(c not in "0123456789.-" for c in release): raise RuntimeError("版本目录名无效")
    client=paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    sock = None
    bind_ip = args.bind_ip or os.environ.get("VIDEO_PLATFORM_BIND_IP", "10.37.6.210" if os.name == "nt" else None)
    if bind_ip:
        import socket
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.bind((bind_ip, 0))
            s.connect(("10.37.200.74", 22))
            sock = s
        except Exception:
            sock = None
    ssh_password = args.password or os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD") or read_local_credential("VIDEO_PLATFORM_SSH_PASSWORD")
    if not ssh_password:
        raise RuntimeError(
            "缺少 SSH 口令。请任选一种方式提供（源码中不保留默认口令）：\n"
            f"  1) 在 {LOCAL_CREDENTIALS} 中写入 VIDEO_PLATFORM_SSH_PASSWORD=<口令>（该文件已被 .gitignore 忽略）\n"
            "  2) 设置环境变量 VIDEO_PLATFORM_SSH_PASSWORD\n"
            "  3) 传入 --password <口令>（会出现在命令行历史中，不推荐）"
        )
    client.connect("10.37.200.74",username="liteware",password=ssh_password,sock=sock,timeout=15,look_for_keys=False,allow_agent=False)
    remote = "/opt/video-platform-v2/releases/" + release
    def sudo(command):
        stdin,out,err=client.exec_command("sudo -S -p '' " + command,timeout=180)
        stdin.write(ssh_password+"\n");stdin.flush();stdin.channel.shutdown_write()
        output=out.read().decode("utf-8","replace");error=err.read().decode("utf-8","replace")
        if out.channel.recv_exit_status(): raise RuntimeError("部署操作失败："+error[-5000:]+output[-1000:])
        if output.strip(): print(output.strip(),flush=True)
    try:
        sudo("install -d -o liteware -g liteware -m 755 " + remote)
        import tarfile, tempfile
        tar_path = Path(tempfile.gettempdir()) / f"release-{release}.tar.gz"
        print("正在打包部署产物...", flush=True)
        with tarfile.open(tar_path, "w:gz") as tar:
            if not args.config_only:
                for part in ["api", "worker", "adapter"]:
                    source = ROOT / "artifacts/v2" / part
                    if not source.is_dir(): raise RuntimeError("缺少发布产物：" + str(source))
                    tar.add(str(source), arcname=part)
                    print("已归档 " + part, flush=True)
                tar.add(str(ROOT / "web-admin/dist"), arcname="web")
                print("已归档 web-admin", flush=True)
            tar.add(str(ROOT / "deploy/v2-install.py"), arcname="deploy/v2-install.py")
            tar.add(str(ROOT / "deploy/nginx-v2.conf"), arcname="deploy/nginx-v2.conf")
        print("打包完成，正在上传至服务器...", flush=True)
        with client.open_sftp() as sftp:
            sftp.put(str(tar_path), f"/tmp/release-{release}.tar.gz")
        print("上传成功，正在解压...", flush=True)
        sudo(f"tar -xzf /tmp/release-{release}.tar.gz -C {remote}")
        sudo(f"chown -R liteware:liteware {remote}")
        sudo(f"rm -f /tmp/release-{release}.tar.gz")
        tar_path.unlink(missing_ok=True)
        print("解压完成，正在执行安装与切换...", flush=True)
        sudo("python3 "+remote+"/deploy/v2-install.py "+remote+(" --switch" if args.switch else ""))
        if not args.switch:
            import ipaddress
            source_ip = str(ipaddress.ip_address(client.get_transport().sock.getsockname()[0]))
            sudo("ufw allow from " + source_ip + " to any port 8443 proto tcp comment 'VideoPlatform v2 validation'")
        print("候选版本目录："+remote,flush=True)
    finally: client.close()


if __name__ == "__main__": main()
