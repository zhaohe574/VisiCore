"""上传已构建产物并安装候选版，凭据从环境读取。"""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import posixpath
import stat
import paramiko

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--switch", action="store_true")
    parser.add_argument("--release")
    parser.add_argument("--config-only", action="store_true")
    args = parser.parse_args()
    release = args.release or ("2.0.0-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"))
    if any(c not in "0123456789.-" for c in release): raise RuntimeError("版本目录名无效")
    client=paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect("10.37.200.74",username="liteware",password=os.environ["VIDEO_PLATFORM_SSH_PASSWORD"],timeout=15,look_for_keys=False,allow_agent=False)
    remote = "/opt/video-platform-v2/releases/" + release
    def sudo(command):
        stdin,out,err=client.exec_command("sudo -S -p '' " + command,timeout=180)
        stdin.write(os.environ["VIDEO_PLATFORM_SSH_PASSWORD"]+"\n");stdin.flush();stdin.channel.shutdown_write()
        output=out.read().decode("utf-8","replace");error=err.read().decode("utf-8","replace")
        if out.channel.recv_exit_status(): raise RuntimeError("部署操作失败："+error[-5000:]+output[-1000:])
        if output.strip(): print(output.strip(),flush=True)
    try:
        sudo("install -d -o liteware -g liteware -m 755 " + remote)
        with client.open_sftp() as sftp:
            def mkdir(path):
                try: sftp.stat(path)
                except FileNotFoundError:
                    mkdir(posixpath.dirname(path));sftp.mkdir(path)
            def upload_tree(local, dest):
                for path in local.rglob("*"):
                    if path.is_file():
                        target=dest+"/"+path.relative_to(local).as_posix()
                        mkdir(posixpath.dirname(target));sftp.put(str(path),target)
            for part in ([] if args.config_only else ["api","worker","adapter"]):
                source=ROOT/"artifacts/v2"/part
                if not source.is_dir(): raise RuntimeError("缺少发布产物："+str(source))
                upload_tree(source,remote+"/"+part)
                print("已上传 "+part,flush=True)
            if not args.config_only: upload_tree(ROOT/"web-admin/dist",remote+"/web")
            mkdir(remote+"/deploy")
            for name in ["v2-install.py","nginx-v2.conf"]:
                sftp.put(str(ROOT/"deploy"/name),remote+"/deploy/"+name)
        sudo("python3 "+remote+"/deploy/v2-install.py "+remote+(" --switch" if args.switch else ""))
        if not args.switch:
            import ipaddress
            source_ip = str(ipaddress.ip_address(client.get_transport().sock.getsockname()[0]))
            sudo("ufw allow from " + source_ip + " to any port 8443 proto tcp comment 'VideoPlatform v2 validation'")
        print("候选版本目录："+remote,flush=True)
    finally: client.close()


if __name__ == "__main__": main()
