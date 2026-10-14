"""读取正式机对账基线，或使用临时进程凭据运行桌面实机验收。"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shlex
import socket
import subprocess
import sys
import paramiko

ROOT = Path(__file__).resolve().parent.parent
CONFIG = "/home/liteware/.config/video-platform/v2-production.json"


def connect():
    password = os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD")
    if not password:
        password = re.search(r"SSH 用户.*?密码：[ `]*([^`，）\s]+)", (ROOT / "HANDOFF.md").read_text(encoding="utf-8")).group(1)
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    sock = socket.socket()
    sock.bind((os.environ.get("VIDEO_PLATFORM_BIND_IP", "10.37.6.210"), 0))
    sock.settimeout(15)
    sock.connect(("10.37.200.74", 22))
    client.connect("10.37.200.74", username="liteware", password=password, sock=sock, timeout=15, look_for_keys=False, allow_agent=False)
    return client, password


def inventory(client, password, output):
    tables = ["devices", "channels", "workshops", "areas", "units", "computers", "terminal_boxes", "onu_devices", "network_devices", "virtual_machines", "ip_pools", "device_ports", "device_links", "map_scenes", "map_coordinates", "map_cable_paths", "audit_logs", "alarm_events"]
    parts = [f"select '{table}' as name,count(*) as total,min(id)::text as first_id,max(id)::text as last_id,md5(coalesce(string_agg(id::text,',' order by id),'')) as id_hash from {table}" for table in tables]
    sql = "select json_agg(t) from (" + " union all ".join(parts) + ") t"
    command = "sudo -S -p '' runuser -u postgres -- psql -X -At -v ON_ERROR_STOP=1 -d video_platform_v2 -c " + shlex.quote(sql)
    stdin, stdout, _ = client.exec_command(command, timeout=60)
    stdin.write(password + "\n"); stdin.flush(); stdin.channel.shutdown_write()
    data = stdout.read()
    if stdout.channel.recv_exit_status() != 0:
        raise RuntimeError("正式数据库只读对账失败。")
    report = {"observedAt": datetime.now(timezone.utc).isoformat(), "tables": json.loads(data)}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"数据对账基线已保存：{output}", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["inventory", "native", "live", "playback"])
    parser.add_argument("--url", default="https://10.37.200.74:8443")
    parser.add_argument("--seconds", type=int)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    client, password = connect()
    try:
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        output = args.output or ROOT / "artifacts/refactor/evidence" / f"{args.mode}-{stamp}"
        if args.mode == "inventory":
            inventory(client, password, output if output.suffix == ".json" else output / "baseline.json")
            return 0
        with client.open_sftp() as sftp, sftp.open(CONFIG) as stream:
            config = json.loads(stream.read())
        env = os.environ.copy()
        env.update({"VP_CANDIDATE_PASSWORD": config["adminPassword"], "VP_CANDIDATE_USER": config.get("adminUser", "admin"),
                    "VP_CANDIDATE_URL": args.url, "VP_CANDIDATE_SOURCE_ADDRESS": "10.37.6.210"})
        env.pop("VIDEO_PLATFORM_SSH_PASSWORD", None)
        if args.mode != "native":
            env.update({"VP_CAPACITY_MODE": args.mode, "VP_CAPACITY_OUTPUT": str(output.resolve())})
            if args.seconds is not None:
                if args.seconds <= 0: raise ValueError("测试时长必须大于零。")
                env["VP_CAPACITY_SECONDS"] = str(args.seconds)
        dotnet = Path(os.environ["LOCALAPPDATA"]) / "VideoPlatform/dotnet/dotnet.exe"
        target = "CandidateTests" if args.mode == "native" else "CapacityTests"
        output.mkdir(parents=True, exist_ok=True)
        print(f"开始桌面实机验收：{args.mode}，输出目录：{output}", flush=True)
        return subprocess.call([str(dotnet), "test", str(ROOT / "tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj"),
                                "-c", "Release", "--no-build", "--filter", f"FullyQualifiedName~{target}",
                                "--logger", f"trx;LogFileName={args.mode}.trx", "--results-directory", str(output)], env=env, cwd=ROOT)
    finally:
        client.close()


if __name__ == "__main__":
    try: sys.exit(main())
    except Exception as error:
        print(f"实机验收未完成：{type(error).__name__}。凭据和远程配置未写入报告。", file=sys.stderr)
        sys.exit(1)
