"""协调真实 API 和管理控制台回归，只操作本轮新建的隔离数据库。"""

import argparse
import json
import os
from pathlib import Path
import re
import runpy
import shlex
import shutil
import socket
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid


ROOT = Path(__file__).resolve().parent.parent
PROJECTS = {
    "api": ROOT / "src/VideoPlatform.Api/VideoPlatform.Api.csproj",
    "integration": ROOT / "tools/VideoPlatform.IntegrationTests/VideoPlatform.IntegrationTests.csproj",
    "admin": ROOT / "tools/VideoPlatform.Admin.Tests/VideoPlatform.Admin.Tests.csproj",
}


def remote(client, arguments):
    command = "sudo -S -p '' runuser -u postgres -- " + shlex.join(arguments)
    stdin, stdout, stderr = client.exec_command(command, timeout=45)
    stdin.write(os.environ["VIDEO_PLATFORM_SSH_PASSWORD"] + "\n")
    stdin.flush()
    stdin.channel.shutdown_write()
    output = stdout.read().decode("utf-8", "replace")
    error = stderr.read().decode("utf-8", "replace")
    if stdout.channel.recv_exit_status() != 0:
        raise RuntimeError("隔离数据库管理命令失败：" + error)
    return output.strip()


def free_port():
    with socket.socket() as reserved:
        reserved.bind(("127.0.0.1", 0))
        return reserved.getsockname()[1]


def stop(process):
    if process.poll() is None:
        # 只终止本脚本持有的进程及其子进程，避免遗留 dotnet run 启动的宿主。
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True, check=False)
        else:
            process.terminate()
        process.wait(timeout=15)


class LoggedProcess:
    def __init__(self, command, env, path, secrets, echo=True):
        self.lines = []
        self.command = command
        self.secrets = [value for value in secrets if value]
        self.path = path
        self.echo = echo
        self.process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                                        stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.reader.start()

    def read(self):
        with self.path.open("w", encoding="utf-8") as log:
            for line in self.process.stdout:
                for secret in self.secrets:
                    line = line.replace(secret, "[已隐藏]")
                self.lines.append(line)
                log.write(line)
                log.flush()
                if self.echo:
                    print(line, end="", flush=True)

    def wait(self, timeout=300):
        try:
            return self.process.wait(timeout=timeout)
        finally:
            stop(self.process)
            self.reader.join(timeout=10)

    def close(self):
        stop(self.process)
        self.reader.join(timeout=10)


def run_suite(name, dotnet, env, directory, secrets):
    command = [dotnet, "run", "--project", str(PROJECTS[name]), "-c", "Release",
               "--no-build", "--no-restore", "--no-launch-profile"]
    print(f"开始 {name} 控制台实际回归。", flush=True)
    process = LoggedProcess(command, env, directory / f"{name}.log", secrets)
    code = process.wait()
    output = "".join(process.lines)
    pattern = r"集成测试完成，共 (\d+) 项通过" if name == "integration" else r"管理模块集成测试通过：(\d+) 项"
    match = re.search(pattern, output)
    checks = sum(line.startswith("通过：") for line in process.lines)
    completed = code == 0 and match is not None and int(match[1]) == checks and checks > 0
    result = {"exitCode": code, "checksPassed": checks, "completed": completed,
              "reportedChecks": int(match[1]) if match else None, "command": command}
    print(f"{name}：退出码 {code}，已通过检查 {checks} 项，完整完成：{completed}。", flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description="执行独立隔离库控制台回归。")
    parser.add_argument("--suite", choices=["all", "integration", "admin"], default="all")
    args = parser.parse_args()
    selected = ["integration", "admin"] if args.suite == "all" else [args.suite]
    if not os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD"):
        raise RuntimeError("缺少进程环境变量 VIDEO_PLATFORM_SSH_PASSWORD。")
    dotnet = str(Path(os.environ["LOCALAPPDATA"]) / "VideoPlatform/dotnet/dotnet.exe")
    # 构建串行进行，避免共享项目输出目录互相覆盖。
    for name in (["api"] if "integration" in selected else []) + selected:
        project = PROJECTS[name]
        subprocess.run([dotnet, "restore", str(project), "--source", str(ROOT / "artifacts/nuget-feed")], cwd=ROOT, check=True)
        subprocess.run([dotnet, "build", str(project), "-c", "Release", "--no-restore"], cwd=ROOT, check=True)

    run_id = uuid.uuid4().hex
    database = "vp_regression_" + run_id
    if not re.fullmatch(r"vp_regression_[0-9a-f]{32}", database):
        raise RuntimeError("隔离数据库名称不符合约束。")
    directory = ROOT / "artifacts/v2-regression" / run_id
    directory.mkdir(parents=True)
    runtime = (directory / "runtime").resolve()
    summary = {"database": database, "startedAt": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "results": {}}
    existing = runpy.run_path(str(ROOT / "tools/v2-environment.py"))
    client = existing["connect"]()
    tunnel = None
    api = None
    created = False
    try:
        config = existing["configuration"](client)
        remote(client, ["createdb", "--template=template0", "--owner=video_platform_v2_test", database])
        created = True
        identity = json.loads(remote(client, ["psql", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-d", database, "-c",
            "select json_build_object('database',current_database(),'version',current_setting('server_version'),'timezone',current_setting('TimeZone'))"]))
        if identity["database"] != database:
            raise RuntimeError("隔离数据库身份校验失败。")
        summary["postgres"] = identity
        print(f"已新建独立数据库 {database}，PostgreSQL {identity['version']}。", flush=True)
        tunnel = existing["tunnel"](client, 0)
        env = existing["environment"](config, tunnel.server_address[1])
        # 已有函数固定生成测试角色连接串；只替换确切数据库字段为本轮独立库。
        marker = "Database=video_platform_v2_test;Username=video_platform_v2_test;"
        if marker not in env["PLATFORM_DATABASE_URL"]:
            raise RuntimeError("已有环境配置不符合测试库约束。")
        env["PLATFORM_DATABASE_URL"] = env["PLATFORM_DATABASE_URL"].replace(marker, f"Database={database};Username=video_platform_v2_test;", 1)
        api_port, adapter_port = free_port(), free_port()
        while adapter_port == api_port:
            adapter_port = free_port()
        api_url, adapter_url = f"http://127.0.0.1:{api_port}", f"http://127.0.0.1:{adapter_port}"
        env.update({"PLATFORM_API_URL": api_url, "TEST_API_URL": api_url,
                    "HIK_ADAPTER_API_URL": adapter_url, "TEST_ADAPTER_URL": adapter_url,
                    "PLATFORM_DATA_PATH": str(runtime / "api"), "ADMIN_TEST_DATA_PATH": str(runtime / "admin"),
                    "ADMIN_TEST_DATABASE_URL": env["PLATFORM_DATABASE_URL"],
                    "PLATFORM_PUBLIC_URL": api_url, "PLATFORM_RTSP_URL": "rtsp://127.0.0.1:1",
                    "ZLM_API_URL": "http://127.0.0.1:1", "ZLM_API_SECRET": "",
                    "Logging__LogLevel__Default": "Warning"})
        env.pop("VIDEO_PLATFORM_SSH_PASSWORD", None)
        summary["ports"] = {"api": api_port, "adapter": adapter_port}
        secrets = [str(value) for value in config.values()] + [env["PLATFORM_DATABASE_URL"], os.environ["VIDEO_PLATFORM_SSH_PASSWORD"]]
        if "integration" in selected:
            api = LoggedProcess([dotnet, str(ROOT / "src/VideoPlatform.Api/bin/Release/net10.0/VideoPlatform.Api.dll")],
                                env, directory / "api.log", secrets, echo=False)
            deadline = time.monotonic() + 60
            while True:
                if api.process.poll() is not None:
                    raise RuntimeError("本地 API 提前退出，请检查 api.log。")
                try:
                    with urllib.request.urlopen(api_url + "/health", timeout=2) as response:
                        if response.status == 200:
                            break
                except (OSError, urllib.error.URLError):
                    pass
                if time.monotonic() >= deadline:
                    raise RuntimeError("本地 API 健康检查超时。")
                time.sleep(0.2)
            print(f"本地 API 已就绪：{api_url}；IntegrationTests 模拟适配器将使用 {adapter_url}。", flush=True)
            summary["results"]["integration"] = run_suite("integration", dotnet, env, directory, secrets)
            api.close()
            api = None
        if "admin" in selected:
            summary["results"]["admin"] = run_suite("admin", dotnet, env, directory, secrets)
        return 0 if all(result["completed"] for result in summary["results"].values()) else 1
    finally:
        try:
            if api is not None:
                api.close()
            if created:
                remote(client, ["dropdb", "--force", database])
                remaining = remote(client, ["psql", "-X", "-At", "-d", "postgres", "-c",
                    f"select count(*) from pg_database where datname='{database}'"])
                if remaining != "0":
                    raise RuntimeError("本轮隔离数据库未清理成功。")
                summary["databaseCleaned"] = True
                print("清理检查通过：本轮独立数据库已删除。", flush=True)
            # 只清理本轮生成的运行目录，保留日志和汇总。
            if runtime.parent != directory.resolve() or runtime.name != "runtime":
                raise RuntimeError("本地清理路径不符合本轮目录约束。")
            if runtime.exists():
                shutil.rmtree(runtime)
            summary["runtimeCleaned"] = not runtime.exists()
        finally:
            if tunnel is not None:
                tunnel.shutdown()
                tunnel.server_close()
            client.close()
            summary["finishedAt"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
            (directory / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"回归汇总：{directory / 'summary.json'}", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
