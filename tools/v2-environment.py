"""第二版隔离测试环境。凭据只在进程内和受限服务器配置文件中传递。"""
import argparse
import json
import os
from pathlib import Path
import select
import socketserver
import subprocess
import threading
import paramiko


ROOT = Path(__file__).resolve().parent.parent
REMOTE_CONFIG = "/home/liteware/.config/video-platform/v2-test.json"


def connect():
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect("10.37.200.74", username="liteware", password=os.environ["VIDEO_PLATFORM_SSH_PASSWORD"], timeout=15, look_for_keys=False, allow_agent=False)
    return client


def setup(client):
    # 仅创建独立测试角色与数据库；不修改现有平台数据库或用户。
    script = r'''
import json, pathlib, secrets, subprocess, os
path=pathlib.Path("/home/liteware/.config/video-platform/v2-test.json")
if not path.exists():
    password=secrets.token_hex(24)
    config={"databasePassword":password,"adminPassword":secrets.token_urlsafe(24),"adapterKey":secrets.token_hex(24)}
    subprocess.run(["runuser","-u","postgres","--","psql","-X","-v","ON_ERROR_STOP=1","-c",f"CREATE ROLE video_platform_v2_test LOGIN PASSWORD '{password}';"],check=True,capture_output=True)
    subprocess.run(["runuser","-u","postgres","--","createdb","-O","video_platform_v2_test","video_platform_v2_test"],check=True,capture_output=True)
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(config),encoding="utf-8")
    os.chmod(path,0o600)
    import pwd
    account=pwd.getpwnam("liteware")
    os.chown(path,account.pw_uid,account.pw_gid)
print("隔离测试数据库配置已就绪")
'''
    import base64
    payload = base64.b64encode(script.encode()).decode()
    command = "sudo -S -p '' python3 -c \"import base64;exec(base64.b64decode('" + payload + "'))\""
    stdin, stdout, stderr = client.exec_command(command, timeout=60)
    stdin.write(os.environ["VIDEO_PLATFORM_SSH_PASSWORD"] + "\n")
    stdin.flush()
    stdin.channel.shutdown_write()
    out = stdout.read().decode("utf-8", "replace")
    error = stderr.read().decode("utf-8", "replace")
    if stdout.channel.recv_exit_status() != 0:
        raise RuntimeError("隔离测试环境初始化失败：" + error)
    print(out.strip(), flush=True)


def configuration(client):
    with client.open_sftp() as sftp:
        with sftp.open(REMOTE_CONFIG) as stream:
            return json.loads(stream.read())


class Tunnel(socketserver.ThreadingTCPServer):
    daemon_threads = True
    allow_reuse_address = True


def tunnel(client, local_port):
    class Handler(socketserver.BaseRequestHandler):
        def handle(self):
            channel = client.get_transport().open_channel("direct-tcpip", ("127.0.0.1", 5432), self.request.getpeername())
            try:
                while True:
                    ready, _, _ = select.select([self.request, channel], [], [], 10)
                    if self.request in ready:
                        data = self.request.recv(65536)
                        if not data:
                            break
                        channel.sendall(data)
                    if channel in ready:
                        data = channel.recv(65536)
                        if not data:
                            break
                        self.request.sendall(data)
            finally:
                channel.close()
    server = Tunnel(("127.0.0.1", local_port), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def environment(config, port):
    env = os.environ.copy()
    env.update({
        "PLATFORM_DATABASE_URL": f"Host=127.0.0.1;Port={port};Database=video_platform_v2_test;Username=video_platform_v2_test;Password={config['databasePassword']};Maximum Pool Size=100",
        "PLATFORM_ADMIN_USER": "admin", "PLATFORM_ADMIN_PASSWORD": config["adminPassword"],
        "PLATFORM_DATA_PATH": str(ROOT / "artifacts" / "v2-local-state"),
        "PLATFORM_API_URL": "http://127.0.0.1:5082", "HIK_ADAPTER_API_URL": "http://127.0.0.1:5092",
        "HIK_ADAPTER_INTERNAL_KEY": config["adapterKey"], "PLATFORM_PUBLIC_URL": "https://localhost:8443",
        "DOTNET_ROOT": str(Path(os.environ["LOCALAPPDATA"]) / "VideoPlatform" / "dotnet"),
    })
    return env


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["setup", "api", "worker", "tests"])
    parser.add_argument("--port", type=int, default=55434)
    args = parser.parse_args()
    client = connect()
    server = None
    try:
        setup(client)
        if args.action == "setup":
            return
        config = configuration(client)
        server = tunnel(client, args.port)
        env = environment(config, args.port)
        dotnet = str(Path(env["DOTNET_ROOT"]) / "dotnet.exe")
        if args.action == "tests":
            command = [dotnet, "run", "--project", str(ROOT / "tools" / "VideoPlatform.IntegrationTests"), "-c", "Release", "-p:BuildProjectReferences=false"]
        else:
            project = "VideoPlatform.Api" if args.action == "api" else "VideoPlatform.Worker"
            command = [dotnet, "run", "--project", str(ROOT / "src" / project), "-c", "Release", "--no-launch-profile"]
        print(f"启动第二版{args.action}，数据库连接仅经 SSH 隧道", flush=True)
        raise SystemExit(subprocess.call(command, cwd=ROOT, env=env))
    finally:
        if server:
            server.shutdown()
        client.close()


if __name__ == "__main__":
    main()
