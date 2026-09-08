"""运行独立 Worker 回归，仅使用 SSH 隧道连接隔离 PostgreSQL 测试库。"""

import os
from pathlib import Path
import runpy
import subprocess


ROOT = Path(__file__).resolve().parents[2]
DATABASE = "video_platform_v2_test"


def main():
    if not os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD"):
        raise RuntimeError("请通过进程环境变量 VIDEO_PLATFORM_SSH_PASSWORD 提供 SSH 凭据。")
    existing = runpy.run_path(str(ROOT / "tools" / "v2-environment.py"))
    # 复用导出回归的只读身份及清理检查，不调用其测试入口。
    inspect = runpy.run_path(str(ROOT / "tools" / "VideoPlatform.Export.Tests" / "run-postgres.py"))["inspect_database"]
    client = existing["connect"]()
    server = None
    before = None
    try:
        config = existing["configuration"](client)
        before = inspect(client)
        print(f"Worker 隔离回归：{before['database']}；PostgreSQL：{before['version']}；会话时区：{before['timezone']}。", flush=True)
        server = existing["tunnel"](client, 0)
        env = existing["environment"](config, server.server_address[1])
        if f"Database={DATABASE};Username={DATABASE};" not in env["PLATFORM_DATABASE_URL"]:
            raise RuntimeError("拒绝使用非隔离测试数据库连接串。")
        env["WORKER_TEST_DATABASE_URL"] = env["PLATFORM_DATABASE_URL"]
        env.pop("VIDEO_PLATFORM_SSH_PASSWORD", None)
        dotnet = str(Path(env["DOTNET_ROOT"]) / "dotnet.exe")
        return subprocess.call([
            dotnet, "test", str(ROOT / "tools" / "VideoPlatform.Worker.Tests" / "VideoPlatform.Worker.Tests.csproj"),
            "-c", "Release", "--no-restore", "--logger", "console;verbosity=normal",
            "--logger", "trx;LogFileName=worker-postgres.trx",
            "--results-directory", str(ROOT / "artifacts" / "worker-tests")
        ], cwd=ROOT, env=env)
    finally:
        try:
            if before is not None:
                after = inspect(client)
                remaining = set(after["schemas"]) - set(before["schemas"])
                if remaining:
                    raise RuntimeError("检测到新增测试 schema 残留：" + ", ".join(sorted(remaining)))
                print("清理检查通过：隔离库没有新增测试 schema 残留。", flush=True)
        finally:
            if server is not None:
                server.shutdown()
                server.server_close()
            client.close()


if __name__ == "__main__":
    raise SystemExit(main())
