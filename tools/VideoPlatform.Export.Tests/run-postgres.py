"""通过已有 SSH 配置执行导出专用回归，仅连接隔离测试库。"""

import json
import os
from pathlib import Path
import runpy
import shlex
import subprocess


ROOT = Path(__file__).resolve().parents[2]
DATABASE = "video_platform_v2_test"


def inspect_database(client):
    # 只读检查数据库身份与测试 schema，禁止在生产库执行初始化或清理。
    sql = """
        select json_build_object(
          'database', current_database(),
          'version', current_setting('server_version'),
          'timezone', current_setting('TimeZone'),
          'schemas', (select coalesce(json_agg(nspname order by nspname), '[]'::json)
                     from pg_namespace where nspname like 'worker_test_%'))
    """
    command = "sudo -S -p '' runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 -At -d " + DATABASE + " -c " + shlex.quote(sql)
    stdin, stdout, stderr = client.exec_command(command, timeout=30)
    stdin.write(os.environ["VIDEO_PLATFORM_SSH_PASSWORD"] + "\n")
    stdin.flush()
    stdin.channel.shutdown_write()
    output = stdout.read().decode("utf-8")
    error = stderr.read().decode("utf-8", "replace")
    if stdout.channel.recv_exit_status() != 0:
        raise RuntimeError("隔离数据库只读检查失败：" + error)
    result = json.loads(output)
    if result["database"] != DATABASE:
        raise RuntimeError("数据库身份不符合隔离测试要求。")
    return result


def main():
    if not os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD"):
        raise RuntimeError("请通过进程环境变量 VIDEO_PLATFORM_SSH_PASSWORD 提供 SSH 凭据。")
    existing = runpy.run_path(str(ROOT / "tools" / "v2-environment.py"))
    client = existing["connect"]()
    server = None
    before = None
    try:
        config = existing["configuration"](client)
        before = inspect_database(client)
        print(f"隔离库：{before['database']}；PostgreSQL：{before['version']}；会话时区：{before['timezone']}。", flush=True)
        server = existing["tunnel"](client, 0)
        env = existing["environment"](config, server.server_address[1])
        # 既有环境函数固定使用隔离测试库，连接串只传给测试子进程。
        if f"Database={DATABASE};Username={DATABASE};" not in env["PLATFORM_DATABASE_URL"]:
            raise RuntimeError("拒绝使用非隔离测试数据库连接串。")
        env["WORKER_TEST_DATABASE_URL"] = env["PLATFORM_DATABASE_URL"]
        env.pop("VIDEO_PLATFORM_SSH_PASSWORD", None)
        dotnet = str(Path(env["DOTNET_ROOT"]) / "dotnet.exe")
        return subprocess.call([
            dotnet, "test", str(ROOT / "tools" / "VideoPlatform.Export.Tests" / "VideoPlatform.Export.Tests.csproj"),
            "-c", "Release", "--logger", "console;verbosity=normal", "--logger", "trx;LogFileName=export-time-postgres.trx",
            "--results-directory", str(ROOT / "artifacts" / "export-time-tests")
        ], cwd=ROOT, env=env)
    finally:
        try:
            if before is not None:
                after = inspect_database(client)
                remaining = set(after["schemas"]) - set(before["schemas"])
                if remaining:
                    raise RuntimeError("检测到新增测试 schema 残留，请检查测试清理结果：" + ", ".join(sorted(remaining)))
                print("清理检查通过：隔离库没有新增测试 schema 残留。", flush=True)
        finally:
            if server is not None:
                server.shutdown()
                server.server_close()
            client.close()


if __name__ == "__main__":
    raise SystemExit(main())
