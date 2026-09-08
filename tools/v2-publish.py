"""校验桌面安装包，通过候选 HTTPS API 发布并验证下载；默认仅本地校验。"""

import argparse
import base64
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import shlex
import signal
import socket
import ssl
import sys
import time
from urllib.parse import urlsplit
import uuid


ROOT = Path(__file__).resolve().parent.parent
CHUNK = 1024 * 1024


class PublishError(Exception):
    """只携带可公开的定位信息，不包含响应正文或凭据。"""


def require(condition, message):
    if not condition:
        raise PublishError(message)


def version(value):
    if not re.fullmatch(r"\d+\.\d+(?:\.\d+){0,2}", value):
        raise argparse.ArgumentTypeError("版本号必须包含二至四段非负整数。")
    if len(value) > 32 or any(int(part) > 2147483647 for part in value.split(".")):
        raise argparse.ArgumentTypeError("版本号超出支持范围。")
    return value


def version_key(value):
    parts = tuple(int(part) for part in version(value).split("."))
    return parts + (0,) * (4 - len(parts))


def https_base(value):
    try:
        parsed = urlsplit(value)
        valid = (parsed.scheme == "https" and parsed.hostname and not parsed.username
                 and not parsed.password and parsed.path in ("", "/")
                 and not parsed.query and not parsed.fragment and parsed.port != 0)
    except ValueError:
        valid = False
    if not valid:
        raise argparse.ArgumentTypeError("API 地址必须为无凭据、无查询参数的 HTTPS 根地址。")
    return value.rstrip("/")


def positive(value):
    try:
        number = int(value)
    except ValueError:
        raise argparse.ArgumentTypeError("必须为正整数。") from None
    if number <= 0:
        raise argparse.ArgumentTypeError("必须为正整数。")
    return number


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    mode = result.add_mutually_exclusive_group()
    mode.add_argument("--execute", action="store_true", help="实际上传、发布并验证；未指定时不连接服务器")
    mode.add_argument("--validate-only", action="store_true", help="仅校验本地清单、大小与 SHA256（默认）")
    result.add_argument("--manifest", type=Path, default=ROOT / "artifacts/v2-desktop/release-manifest.json")
    result.add_argument("--minimum-version", type=version, default="2.0.0", help="最低客户端版本，默认 2.0.0")
    result.add_argument("--force", action="store_true", help="仅 MSI 显式强制更新，ZIP 始终为 false")
    result.add_argument("--notes", default="", help="发布说明，最多 4096 字符；不要包含敏感信息")
    result.add_argument("--host", default="10.37.200.74", help="SSH 主机")
    result.add_argument("--port", type=positive, default=22, help="SSH 端口")
    result.add_argument("--user", default="liteware", help="SSH 用户")
    result.add_argument("--api-base", type=https_base, default="https://10.37.200.74:8443")
    result.add_argument("--config", default="/home/liteware/.config/video-platform/v2-production.json", help="远程 production 配置路径")
    result.add_argument("--ca-file", help="远程受信任 CA 文件；未指定时使用远程系统信任库，始终校验证书及主机名")
    result.add_argument("--local-ca-file", type=Path, help="上传已核实的本地公开 CA 证书，仅用于本次 HTTPS 校验")
    result.add_argument("--known-hosts", type=Path, help="额外 SSH known_hosts 文件；默认使用本机 known_hosts")
    result.add_argument("--host-key-sha256", help="可选 SSH 公钥 SHA256 指纹，用于尚未进入 known_hosts 的主机")
    result.add_argument("--timeout", type=positive, default=180, help="每次网络读写超时秒数，默认 180")
    result.add_argument("--remote-timeout", type=positive, default=3600, help="远程工作总等待上限秒数，默认 3600")
    result.add_argument("--report", type=Path, help="本地 JSON 报告路径；默认 artifacts/v2-qa/publish/唯一编号.json")
    return result


def file_digest(path):
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as source:
        while block := source.read(CHUNK):
            size += len(block)
            digest.update(block)
    return size, digest.hexdigest()


def validate_manifest(path, minimum):
    require(path.is_file(), "未找到本地发布清单。")
    manifest = json.loads(path.read_text(encoding="utf-8-sig"))
    release_version = version(manifest["version"])
    require(version_key(minimum) <= version_key(release_version), "最低版本不能高于清单版本。")
    items = manifest.get("files")
    require(isinstance(items, list) and len(items) == 2, "清单必须恰好包含一个 MSI 和一个 ZIP。")
    kinds = set()
    verified = []
    for item in items:
        name = item.get("fileName", "")
        require(isinstance(name, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*\.(?:msi|zip)", name, re.I), "安装包文件名无效。")
        kind = Path(name).suffix.lower()
        require(kind not in kinds, "清单中的安装包类型重复。")
        kinds.add(kind)
        size = item.get("fileSize")
        sha = item.get("sha256", "")
        require(type(size) is int and 0 < size <= 1073741824, "安装包大小必须大于零且不超过 1 GB。")
        require(isinstance(sha, str) and re.fullmatch(r"[a-fA-F0-9]{64}", sha), "清单 SHA256 无效。")
        package = path.parent / name
        require(package.is_file() and package.resolve().parent == path.parent.resolve(), "安装包缺失或解析后超出清单目录。")
        actual_size, actual_sha = file_digest(package)
        require(actual_size == size and actual_sha == sha.lower(), "本地大小或 SHA256 不匹配：" + name)
        verified.append({"fileName": name, "fileSize": size, "sha256": actual_sha})
    return {"version": release_version, "files": verified}


def safe_error(error):
    if isinstance(error, PublishError):
        return str(error)
    if isinstance(error, (ssl.SSLError, ssl.CertificateError)):
        return "HTTPS 证书验证或 TLS 连接失败。"
    if isinstance(error, (TimeoutError, socket.timeout)):
        return "网络操作超时。"
    if isinstance(error, KeyboardInterrupt):
        return "操作被中断。"
    # 第三方异常可能含请求头、密码或响应正文，只记录异常类型。
    return "操作失败，异常类型：" + type(error).__name__


def stamp():
    return datetime.now(timezone.utc).isoformat()


def save_report(path, report):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)


class Api:
    def __init__(self, base, timeout, ca_file):
        self.origin = urlsplit(base)
        self.timeout = timeout
        self.context = ssl.create_default_context(cafile=ca_file)
        self.token = None

    @contextmanager
    def response(self, method, path, body=None, headers=None, authorized=False):
        require(path.startswith("/api/") and "\r" not in path and "\n" not in path, "API 路径无效。")
        request_headers = {"Accept-Encoding": "identity", **(headers or {})}
        if authorized:
            require(bool(self.token), "管理员会话不可用。")
            request_headers["Authorization"] = "Bearer " + self.token
        connection = http.client.HTTPSConnection(self.origin.hostname, self.origin.port or 443,
                                                  timeout=self.timeout, context=self.context)
        try:
            connection.request(method, path, body=body, headers=request_headers)
            yield connection.getresponse()
        finally:
            connection.close()

    def json(self, method, path, body=None, authorized=False, expected=(200,)):
        data = json.dumps(body).encode("utf-8") if body is not None else (b"" if method == "POST" else None)
        with self.response(method, path, data, {"Content-Type": "application/json"}, authorized) as response:
            require(response.status in expected, f"API 请求失败：{method} {path}，HTTP {response.status}。")
            raw = response.read(8 * CHUNK + 1)
            require(len(raw) <= 8 * CHUNK, "API JSON 响应超过限制。")
            return json.loads(raw) if raw else None

    def upload(self, path, metadata, options):
        boundary = "VideoPlatform" + uuid.uuid4().hex
        fields = {"version": options["version"], "minimumVersion": options["minimumVersion"],
                  "forceUpdate": str(options["force"] and path.suffix.lower() == ".msi").lower(),
                  "releaseNotes": options["notes"]}
        prefix = b"".join((f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n').encode("utf-8")
                          for key, value in fields.items())
        prefix += (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\n'
                   'Content-Type: application/octet-stream\r\n\r\n').encode("ascii")
        suffix = f"\r\n--{boundary}--\r\n".encode("ascii")

        def content():
            yield prefix
            with path.open("rb") as source:
                while block := source.read(CHUNK):
                    yield block
            yield suffix

        headers = {"Content-Type": "multipart/form-data; boundary=" + boundary,
                   "Content-Length": str(len(prefix) + metadata["fileSize"] + len(suffix))}
        with self.response("POST", "/api/v2/releases", content(), headers, True) as response:
            require(response.status == 201, f"安装包 API 上传失败，HTTP {response.status}。")
            raw = response.read(CHUNK + 1)
            require(len(raw) <= CHUNK, "安装包响应超过限制。")
            return json.loads(raw)


def release_id(row):
    value = str(row.get("id", ""))
    require(bool(re.fullmatch(r"[1-9]\d*", value)), "API 返回的版本 ID 无效。")
    return value


def matching_file(row, item):
    return (str(row.get("sha256", "")).lower() == item["sha256"]
            and int(row.get("fileSize", -1)) == item["fileSize"]
            and Path(str(row.get("fileName", ""))).suffix.lower() == Path(item["fileName"]).suffix.lower())


def verify_metadata(row, item, job):
    require(matching_file(row, item), "服务端版本大小或 SHA256 不匹配：" + item["fileName"])
    require(version_key(row["version"]) == version_key(job["version"]), "服务端版本号与清单不一致。")
    require(row.get("minimumVersion") == job["minimumVersion"], "已存在版本的 minimumVersion 与本次参数不同。")
    force = job["force"] and item["fileName"].lower().endswith(".msi")
    require(row.get("forceUpdate") is force, "已存在版本的 forceUpdate 与本次参数不同。")


def list_releases(api):
    rows = []
    page = 1
    while True:
        result = api.json("GET", f"/api/v2/releases?page={page}&pageSize=200", authorized=True)
        items = result["items"]
        require(isinstance(items, list), "版本分页响应无效。")
        rows.extend(items)
        if len(rows) >= int(result["total"]):
            return rows
        require(bool(items) and page < 10000, "版本分页未正常结束。")
        page += 1


def verify_download(api, path, item, full=True):
    end = min(1023, item["fileSize"] - 1)
    with api.response("GET", path, headers={"Range": f"bytes=0-{end}"}) as response:
        require(response.status == 206, f"Range 下载未返回 206：{path}。")
        require(response.getheader("Content-Range") == f"bytes 0-{end}/{item['fileSize']}", "Content-Range 不匹配。")
        sample = response.read(end + 2)
        require(len(sample) == end + 1, "Range 下载长度不匹配。")
    if not full:
        return
    with api.response("GET", path) as response:
        require(response.status == 200, f"完整下载未返回 200：{path}。")
        require(response.getheader("Content-Length") == str(item["fileSize"]), "完整下载 Content-Length 不匹配。")
        digest = hashlib.sha256()
        size = 0
        while block := response.read(CHUNK):
            size += len(block)
            require(size <= item["fileSize"], "完整下载超过清单大小。")
            digest.update(block)
        require(size == item["fileSize"] and digest.hexdigest() == item["sha256"], "完整下载大小或 SHA256 不匹配。")


def verify_latest(api, job, selected):
    for path in ("/api/v2/public/releases/latest", "/api/desktop-releases/latest"):
        latest = api.json("GET", path)
        require(release_id(latest) == selected[".msi"][0], "latest 未优先返回本次 MSI。")
        for item in job["files"]:
            kind = Path(item["fileName"]).suffix.lower()
            package = next((row for row in latest.get("packages", []) if str(row.get("id")) == selected[kind][0]), None)
            require(package is not None and package.get("status") == "published", "latest.packages 缺少已发布安装包。")
            verify_metadata(package, item, job)
    for kind, (identifier, item) in selected.items():
        latest = api.json("GET", "/api/v2/public/releases/latest?packageType=" + kind[1:])
        require(release_id(latest) == identifier, "指定 packageType 的 latest 未返回本次版本。")
        verify_metadata(latest, item, job)
        download = urlsplit(latest.get("downloadUrl", ""))
        # 候选服务可能返回正式 443 的 PublicBase，只在同主机上保留路径并固定请求候选 HTTPS 端口。
        require(not download.query and not download.fragment and not download.username and not download.password
                and (not download.netloc or (download.scheme == "https" and download.hostname == api.origin.hostname)),
                "下载地址必须是同主机 HTTPS 地址或相对路径。")
        require(download.path == f"/api/v2/releases/{identifier}/download", "下载地址不符合版本 API 契约。")
        verify_download(api, download.path, item)
        verify_download(api, f"/api/desktop-releases/{identifier}/download", item)


def remote_main(job_path):
    directory = job_path.parent.resolve()
    report_path = directory / "report.json"
    report = {"status": "running", "startedAt": stamp(), "stage": "remote-init", "releases": [], "checks": []}
    api = None
    job = None

    def interrupted(signum, frame):
        raise PublishError("远程作业超时或收到终止信号，已进入注销和清理。")

    def checkpoint(stage):
        report["stage"] = stage
        save_report(report_path, report)

    try:
        job = json.loads(job_path.read_text(encoding="utf-8"))
        for name in ("SIGALRM", "SIGTERM", "SIGINT", "SIGHUP"):
            signal.signal(getattr(signal, name), interrupted)
        signal.alarm(job["remoteTimeout"])
        checkpoint("remote-hash-check")
        for item in job["files"]:
            name = item["fileName"]
            require(Path(name).name == name and (directory / name).resolve().parent == directory, "远程临时文件路径无效。")
            require(file_digest(directory / name) == (item["fileSize"], item["sha256"]), "SFTP 上传后的大小或 SHA256 不匹配。")
        api = Api(https_base(job["apiBase"]), job["timeout"], job["caFile"])
        checkpoint("https-login")
        config = json.loads(Path(job["config"]).read_text(encoding="utf-8"))
        password = config.pop("adminPassword")
        config.clear()
        try:
            login = api.json("POST", "/api/v2/auth/login", {"username": "admin", "password": password,
                             "clientType": "desktop", "clientVersion": "2.0.0-publish"})
        finally:
            password = None
        api.token = login.pop("accessToken", None)
        require(bool(api.token), "登录未返回管理员令牌。")
        require("desktop.release.manage" in login.get("user", {}).get("permissions", []), "当前管理员没有客户端发布权限。")
        login.clear()
        checkpoint("release-preflight")
        existing = list_releases(api)
        require(not any(row.get("status") == "published" and version_key(row["version"]) > version_key(job["version"])
                        for row in existing), "已有更高版本发布，本工具不会覆盖最新版本。")
        selected = {}
        # 先核查两个包的冲突，再执行任何发布写入。
        candidates = []
        for item in sorted(job["files"], key=lambda value: value["fileName"].lower().endswith(".msi")):
            same = [row for row in existing if version_key(row["version"]) == version_key(job["version"])
                    and Path(str(row.get("fileName", ""))).suffix.lower() == Path(item["fileName"]).suffix.lower()]
            require(not any(row.get("status") == "published" and not matching_file(row, item) for row in same),
                    "同版本同类型已发布不同内容，拒绝覆盖：" + item["fileName"])
            matches = [row for row in same if matching_file(row, item) and row.get("status") in ("published", "draft")]
            row = max(matches, key=lambda value: (value.get("status") == "published", int(release_id(value)))) if matches else None
            if row and row.get("status") == "published":
                verify_metadata(row, item, job)
            candidates.append((item, row))
        for item, row in candidates:
            checkpoint("upload:" + item["fileName"])
            if row is None:
                row = api.upload(directory / item["fileName"], item, job)
            identifier = release_id(row)
            record = {**item, "id": identifier, "action": "skipped" if row.get("status") == "published" else "pending-publish"}
            report["releases"].append(record)
            checkpoint("verify-upload:" + identifier)
            require(matching_file(row, item) and version_key(row["version"]) == version_key(job["version"]), "上传响应的版本、大小或 SHA256 不匹配。")
            if row.get("status") != "published":
                checkpoint("publish:" + identifier)
                api.json("POST", f"/api/v2/releases/{identifier}/publish", {"minimumVersion": job["minimumVersion"],
                         "forceUpdate": job["force"] and item["fileName"].lower().endswith(".msi")}, True, (204,))
                record["action"] = "published"
            selected[Path(item["fileName"]).suffix.lower()] = (identifier, item)
            checkpoint("published:" + identifier)
        checkpoint("verify-latest-and-downloads")
        verify_latest(api, job, selected)
        report["checks"] = ["latest 优先 MSI", "packages 包含 MSI 和 ZIP", "旧 latest 入口", "按包类型筛选",
                            "新旧下载入口 Range 206", "新旧入口两个包完整下载大小与 SHA256"]
        report["status"] = "passed"
    except (Exception, KeyboardInterrupt) as error:
        report["status"] = "failed"
        report["error"] = safe_error(error)
    finally:
        signal.alarm(0)
        # 收尾期间不接受重复终止信号，以便注销及删除临时包文件有机会完成。
        for name in ("SIGTERM", "SIGINT", "SIGHUP"):
            signal.signal(getattr(signal, name), signal.SIG_IGN)
        if api and api.token:
            try:
                api.timeout = min(api.timeout, 30)
                api.json("POST", "/api/v2/auth/logout", authorized=True, expected=(204,))
                report["logout"] = "passed"
            except Exception as error:
                report["status"] = "failed"
                report["logout"] = safe_error(error)
            finally:
                api.token = None
        else:
            report["logout"] = "no-session"
        try:
            names = [item["fileName"] for item in job["files"]] if job else []
            for name in names + ["worker.py", "job.json", "trusted-ca.pem"]:
                target = directory / name
                require(target.resolve().parent == directory, "拒绝清理超出本次临时目录的文件。")
                target.unlink(missing_ok=True)
            report["temporaryFiles"] = "cleaned-report-retained"
        except Exception as error:
            report["status"] = "failed"
            report["temporaryFiles"] = safe_error(error)
        report["finishedAt"] = stamp()
        save_report(report_path, report)
    return 0 if report["status"] == "passed" else 1


def execute(args, manifest, report, report_path):
    import paramiko

    password = os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD")
    require(bool(password), "缺少 VIDEO_PLATFORM_SSH_PASSWORD 环境变量。")
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    if args.known_hosts:
        client.load_host_keys(str(args.known_hosts))
    if args.host_key_sha256:
        class PinnedKey(paramiko.MissingHostKeyPolicy):
            def missing_host_key(self, client, hostname, key):
                actual = "SHA256:" + base64.b64encode(hashlib.sha256(key.asbytes()).digest()).decode("ascii").rstrip("=")
                require(actual == args.host_key_sha256, "SSH 主机公钥指纹不匹配。")
        client.set_missing_host_key_policy(PinnedKey())
    else:
        client.set_missing_host_key_policy(paramiko.RejectPolicy())
    remote = "/tmp/video-platform-publish-" + uuid.uuid4().hex
    channel = None
    created = False
    try:
        report["stage"] = "ssh-connect"
        save_report(report_path, report)
        client.connect(args.host, port=args.port, username=args.user, password=password, timeout=15,
                       auth_timeout=15, banner_timeout=15, look_for_keys=False, allow_agent=False)
        password = None
        with client.open_sftp() as sftp:
            sftp.get_channel().settimeout(args.timeout)
            sftp.mkdir(remote, mode=0o700)
            created = True
            report["remoteTemporaryDirectory"] = remote
            job = {**manifest, "apiBase": args.api_base, "minimumVersion": args.minimum_version, "force": args.force,
                   "notes": args.notes, "config": args.config, "caFile": args.ca_file, "timeout": args.timeout,
                   "remoteTimeout": args.remote_timeout}
            if args.local_ca_file:
                sftp.put(str(args.local_ca_file), remote + "/trusted-ca.pem")
                sftp.chmod(remote + "/trusted-ca.pem", 0o600)
                job["caFile"] = remote + "/trusted-ca.pem"
            for item in manifest["files"]:
                report["stage"] = "sftp:" + item["fileName"]
                save_report(report_path, report)
                sftp.put(str(args.manifest.parent / item["fileName"]), remote + "/" + item["fileName"])
                sftp.chmod(remote + "/" + item["fileName"], 0o600)
            sftp.put(str(Path(__file__).resolve()), remote + "/worker.py")
            with sftp.open(remote + "/job.json", "w") as output:
                output.write(json.dumps(job, ensure_ascii=False).encode("utf-8"))
            report["stage"] = "remote-workflow"
            save_report(report_path, report)
            # 凭据从不进入 SSH 命令行或 SFTP 作业文件，远程以当前账号直接读取 production 配置。
            command = "python3 " + shlex.quote(remote + "/worker.py") + " --remote-job " + shlex.quote(remote + "/job.json")
            _, stdout, _ = client.exec_command(command, timeout=args.remote_timeout + 60)
            channel = stdout.channel
            deadline = time.monotonic() + args.remote_timeout + 60
            while not channel.exit_status_ready():
                # 丢弃远程原始输出，避免意外异常正文进入本地日志。
                while channel.recv_ready():
                    channel.recv(65536)
                while channel.recv_stderr_ready():
                    channel.recv_stderr(65536)
                require(time.monotonic() < deadline, "远程发布等待超时；需按报告临时目录核查远程作业及会话。")
                time.sleep(0.2)
            exit_code = channel.recv_exit_status()
            with sftp.open(remote + "/report.json", "r") as source:
                report["remote"] = json.load(source)
            require(exit_code == 0 and report["remote"].get("status") == "passed", "远程发布或验证失败，详见报告 remote 字段。")
            report["status"] = "passed"
    finally:
        password = None
        # 仅删除本次随机目录内已知文件，不操作正式 releases 存储或修改发布状态。
        if created:
            try:
                with client.open_sftp() as sftp:
                    sftp.get_channel().settimeout(args.timeout)
                    if channel is not None and not channel.exit_status_ready():
                        report["cleanup"] = "远程工作仍可能运行，临时目录保留，避免破坏正在读取的文件。"
                        report["status"] = "failed"
                    else:
                        if "remote" not in report:
                            try:
                                with sftp.open(remote + "/report.json", "r") as source:
                                    report["remote"] = json.load(source)
                            except OSError:
                                pass
                        for name in [item["fileName"] for item in manifest["files"]] + ["worker.py", "job.json", "trusted-ca.pem", "report.json", "report.json.tmp"]:
                            try:
                                sftp.remove(remote + "/" + name)
                            except FileNotFoundError:
                                pass
                        sftp.rmdir(remote)
                        report["cleanup"] = "passed"
            except Exception as error:
                report["cleanup"] = safe_error(error)
                report["status"] = "failed"
        client.close()


def main(argv=None):
    args = parser().parse_args(argv)
    if args.port > 65535:
        parser().error("SSH 端口不能超过 65535。")
    if len(args.notes) > 4096:
        parser().error("发布说明不能超过 4096 字符。")
    if args.local_ca_file and args.ca_file:
        parser().error("本地与远程 CA 文件不能同时指定。")
    if args.local_ca_file:
        ssl.create_default_context(cafile=str(args.local_ca_file))
    if args.host_key_sha256 and not re.fullmatch(r"SHA256:[A-Za-z0-9+/]{43}", args.host_key_sha256):
        parser().error("SSH 指纹必须采用 SHA256: 后接 43 位 Base64 的格式。")
    args.manifest = args.manifest.resolve()
    report_path = args.report or ROOT / "artifacts/v2-qa/publish" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8] + ".json")
    report = {"status": "running", "startedAt": stamp(), "mode": "execute" if args.execute else "validate-only", "stage": "local-validation"}
    try:
        save_report(report_path, report)
        manifest = validate_manifest(args.manifest, args.minimum_version)
        report["manifest"] = manifest
        report["minimumVersion"] = args.minimum_version
        report["forceMsi"] = args.force
        report["status"] = "validated"
        if args.execute:
            execute(args, manifest, report, report_path)
    except (Exception, KeyboardInterrupt) as error:
        report["status"] = "failed"
        report["error"] = safe_error(error)
    finally:
        report["finishedAt"] = stamp()
        try:
            save_report(report_path, report)
        except OSError:
            print("无法写入本地报告，请检查报告目录权限。", file=sys.stderr)
            return 1
    print("结果：" + report["status"] + "；报告：" + str(report_path.resolve()))
    return 1 if report["status"] == "failed" else 0


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    if len(sys.argv) == 3 and sys.argv[1] == "--remote-job":
        raise SystemExit(remote_main(Path(sys.argv[2])))
    raise SystemExit(main())
