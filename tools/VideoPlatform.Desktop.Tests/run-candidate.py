"""使用服务器内现有凭据运行桌面实机验收，不在终端或文件中记录密码。"""

import argparse
import json
import os
from pathlib import Path
import subprocess

import paramiko


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='https://10.37.200.74:8443')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    try:
        ssh.connect("10.37.200.74", username="liteware", password=os.environ["VIDEO_PLATFORM_SSH_PASSWORD"],
                    timeout=15, look_for_keys=False, allow_agent=False)
        with ssh.open_sftp() as sftp:
            with sftp.open("/home/liteware/.config/video-platform/v2-production.json") as source:
                config = json.load(source)
    finally:
        ssh.close()
    env = os.environ.copy()
    env.pop("VIDEO_PLATFORM_SSH_PASSWORD", None)
    env["VP_CANDIDATE_PASSWORD"] = config["adminPassword"]
    env["VP_CANDIDATE_URL"] = args.base_url
    dotnet = Path(os.environ["LOCALAPPDATA"]) / "VideoPlatform/dotnet/dotnet.exe"
    return subprocess.call([
        str(dotnet), "test", str(root / "tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj"),
        "-c", "Release", "--no-restore", "--filter", "Category=Candidate",
        "--logger", "console;verbosity=normal", "--logger", "trx;LogFileName=candidate.trx",
        "--results-directory", str(root / "artifacts/v2-qa/desktop"),
        "--blame-hang-timeout", "90s", "--blame-hang-dump-type", "mini"
    ], cwd=root, env=env)


if __name__ == "__main__":
    raise SystemExit(main())
