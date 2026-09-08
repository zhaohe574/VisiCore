"""在目标 Linux 服务器安装独立第二版候选实例。"""
import argparse
import configparser
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import secrets
import shlex
import signal
import ssl
import stat
import subprocess
import sys
import time
import urllib.parse
import urllib.request
import uuid


ROOT = Path("/opt/video-platform-v2")
CURRENT = ROOT / "current"
CONFIG = Path("/home/liteware/.config/video-platform")
ENV = CONFIG / "v2.env"
DATA = Path("/var/lib/video-platform/v2")
STATE = Path("/var/lib/video-platform-v2-deploy")
MAIN_SITE = Path("/etc/nginx/sites-enabled/video-platform")
CANDIDATE_SITE = Path("/etc/nginx/sites-enabled/video-platform-v2-candidate")
SERVICES = [f"video-platform-v2-{part}.service" for part in ("zlm", "api", "adapter", "worker")]
UNIT_DIR = Path("/etc/systemd/system")
UNITS = [UNIT_DIR / name for name in SERVICES]
TLS_DIRECTIVE = re.compile(r"^\s*(ssl_certificate|ssl_certificate_key|ssl_trusted_certificate)\s+([^;]+);\s*(?:#.*)?$", re.M)


def run(*args):
    result = subprocess.run(args, capture_output=True, text=True, timeout=90)
    if result.returncode:
        # 命令参数和标准错误可能携带密钥，只输出操作名与退出码。
        raise RuntimeError(f"{args[0]} 操作失败，退出码 {result.returncode}")
    return result.stdout.strip()


def atomic_bytes(path, content, mode=0o600, owner=None):
    path = Path(path)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("xb") as stream:
            os.chmod(temporary, mode)
            if owner is not None:
                os.chown(temporary, *owner)
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def write_json(path, value):
    atomic_bytes(path, json.dumps(value, ensure_ascii=False, indent=2).encode())


def replace_link(path, target):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.symlink_to(target)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def write_config(path, content):
    target = path.resolve() if path.is_symlink() else path
    previous = target.stat() if target.exists() else None
    atomic_bytes(target, content.encode(), stat.S_IMODE(previous.st_mode) if previous else 0o644,
                 (previous.st_uid, previous.st_gid) if previous else None)


def service_state(name):
    output = run("systemctl", "show", name, "--property=LoadState,ActiveState,UnitFileState,InvocationID")
    values = dict(line.split("=", 1) for line in output.splitlines() if "=" in line)
    if values.get("ActiveState") in ("activating", "deactivating", "reloading"):
        raise RuntimeError(f"服务正在切换状态，请稍后重试：{name}")
    return values


def tls_references(text):
    references = []
    for match in TLS_DIRECTIVE.finditer(text):
        tokens = shlex.split(match[2])
        if len(tokens) != 1 or not tokens[0].startswith("/") or "$" in tokens[0]:
            raise RuntimeError("证书引用必须是明确的绝对路径")
        path = Path(tokens[0])
        if not path.is_file():
            raise RuntimeError("证书引用不存在：" + str(path))
        references.append({"directive": match[1], "value": match[2].strip(), "path": str(path),
                           "resolved": str(path.resolve()), "link": os.readlink(path) if path.is_symlink() else None,
                           "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    if sum(item["directive"] == "ssl_certificate" for item in references) != 1 or sum(item["directive"] == "ssl_certificate_key" for item in references) != 1:
        raise RuntimeError("站点必须直接声明一组证书和私钥；请先整理多证书或 include 配置")
    return references


def public_url(port=None):
    existing = environment_file(ENV).get("PLATFORM_PUBLIC_URL", "https://10.37.200.74") if ENV.exists() else "https://10.37.200.74"
    parsed = urllib.parse.urlsplit(existing)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password or parsed.path not in ("", "/") or parsed.query or parsed.fragment:
        raise RuntimeError("PLATFORM_PUBLIC_URL 必须是无凭据的 HTTPS 站点地址")
    host = f"[{parsed.hostname}]" if ":" in parsed.hostname else parsed.hostname
    selected_port = port if port is not None else (parsed.port or 443)
    return urllib.parse.urlunsplit(("https", host + (f":{selected_port}" if selected_port != 443 else ""), "", "", ""))


def site_endpoint(path):
    content = path.read_text()
    ports = re.findall(r"^\s*listen\s+(?:[\d.]+:|\[::\]:)?(\d+)\s+ssl\b", content, re.M)
    if not ports or len(set(ports)) != 1:
        raise RuntimeError("无法确定站点 HTTPS 端口：" + str(path))
    refs = tls_references(content)
    return {"url": public_url(int(ports[0])) + "/health",
            "certificates": [ref["path"] for ref in refs if ref["directive"] == "ssl_certificate"]}


def http_json(url, headers=None, certificates=()):
    try:
        context = ssl.create_default_context()
        for certificate in certificates:
            context.load_verify_locations(cafile=certificate)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
        with opener.open(urllib.request.Request(url, headers=headers or {}), timeout=3) as response:
            if response.status != 200 or response.url != url:
                raise ValueError("健康地址状态码或重定向无效")
            return json.loads(response.read(65536))
    except Exception:
        address = urllib.parse.urlsplit(url)
        raise RuntimeError("健康检查失败：" + address.netloc + address.path) from None


def check_services(active):
    invocations = {}
    for name in active:
        state = service_state(name)
        if state.get("ActiveState") != "active" or not state.get("InvocationID"):
            raise RuntimeError("服务未稳定运行：" + name)
        invocations[name] = state["InvocationID"]
    values = environment_file(ENV) if ENV.exists() else {}
    if "video-platform-v2-api.service" in active:
        result = http_json("http://127.0.0.1:5082/health")
        if result.get("status") != "ok" or result.get("service") != "video-platform-api":
            raise RuntimeError("API 健康响应不符合预期")
    if "video-platform-v2-adapter.service" in active:
        result = http_json("http://127.0.0.1:5092/health", {"X-Adapter-Key": values["HIK_ADAPTER_INTERNAL_KEY"]})
        if result.get("status") != "ok" or result.get("service") != "hikvision-adapter":
            raise RuntimeError("适配器健康响应不符合预期")
    if "video-platform-v2-zlm.service" in active:
        result = http_json("http://127.0.0.1:18082/index/api/getApiList?" + urllib.parse.urlencode({"secret": values["ZLM_API_SECRET"]}))
        if result.get("code") != 0:
            raise RuntimeError("流媒体健康响应不符合预期")
    return invocations


def wait_healthy(active, endpoints, timeout=60):
    deadline = time.monotonic() + timeout
    previous = None
    while True:
        try:
            invocations = check_services(active)
            for endpoint in endpoints:
                result = http_json(endpoint["url"], certificates=endpoint["certificates"])
                if any(result.get(key) != value for key, value in endpoint.get("expected", {}).items()):
                    raise RuntimeError("HTTPS 入口未返回预期服务")
            if previous == invocations:
                return
            previous = invocations
        except Exception:
            previous = None
        if time.monotonic() >= deadline:
            raise RuntimeError("服务或 HTTPS 入口在等待期限内未恢复健康")
        time.sleep(1)


def managed_paths():
    return [CURRENT, ENV, CONFIG / "v2-production.json", DATA / "zlm/config.ini", MAIN_SITE, CANDIDATE_SITE, *UNITS]


def capture_paths(directory):
    entries = {}
    def capture(path, follow=True):
        key = str(path)
        if key in entries:
            return
        if not path.exists() and not path.is_symlink():
            entries[key] = {"kind": "absent"}
            return
        info = path.lstat()
        entry = {"mode": stat.S_IMODE(info.st_mode), "uid": info.st_uid, "gid": info.st_gid}
        entries[key] = entry
        if path.is_symlink():
            target = os.readlink(path)
            entry.update(kind="link", target=target)
            if follow:
                capture(Path(os.path.abspath(path.parent / target)))
        elif path.is_file():
            blob = f"file-{len(entries)}"
            atomic_bytes(directory / blob, path.read_bytes())
            entry.update(kind="file", blob=blob)
        else:
            raise RuntimeError("备份路径不是普通文件或符号链接：" + key)
    for path in managed_paths():
        capture(path, follow=path != CURRENT)
    return entries


def save_snapshot(action, check_health=True):
    identifier = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:12]
    directory = STATE / "snapshots" / identifier
    directory.mkdir(parents=True, mode=0o700)
    services = {name: service_state(name) for name in SERVICES}
    endpoints = []
    references = {}
    for site in (MAIN_SITE, CANDIDATE_SITE):
        if site.exists():
            if check_health:
                endpoint = site_endpoint(site)
                result = http_json(endpoint["url"], certificates=endpoint["certificates"])
                endpoint["expected"] = {key: result[key] for key in ("status", "service", "version") if key in result}
                endpoints.append(endpoint)
            if check_health:
                references[str(site)] = tls_references(site.read_text())
            else:
                references[str(site)] = [{"directive": match[1], "value": match[2].strip()} for match in TLS_DIRECTIVE.finditer(site.read_text())]
    active = [name for name, state in services.items() if state.get("ActiveState") == "active"]
    if check_health:
        wait_healthy(active, endpoints)
    snapshot = {"id": identifier, "action": action, "status": "prepared", "parent": read_marker("head"),
                "files": capture_paths(directory), "services": services, "endpoints": endpoints,
                "certificateReferences": references}
    write_json(directory / "snapshot.json", snapshot)
    return snapshot


def read_marker(name):
    path = STATE / name
    return path.read_text().strip() if path.exists() else None


def set_marker(name, identifier):
    path = STATE / name
    if identifier:
        atomic_bytes(path, identifier.encode())
    else:
        path.unlink(missing_ok=True)


def load_snapshot(identifier):
    if not identifier or not re.fullmatch(r"\d{8}T\d{6}Z-[a-f0-9]{12}", identifier):
        raise RuntimeError("没有可回滚的操作，或快照编号无效")
    snapshot = json.loads((STATE / "snapshots" / identifier / "snapshot.json").read_text())
    if snapshot.get("id") != identifier:
        raise RuntimeError("快照编号不一致")
    return snapshot


def record_status(snapshot, status):
    snapshot["status"] = status
    write_json(STATE / "snapshots" / snapshot["id"] / "snapshot.json", snapshot)


def restore_snapshot(snapshot):
    directory = STATE / "snapshots" / snapshot["id"]
    failures = []
    def attempt(operation):
        try:
            operation()
            return True
        except Exception:
            failures.append(True)
            return False
    for name in reversed(SERVICES):
        def stop_service():
            state = service_state(name)
            if state.get("LoadState") != "not-found":
                run("systemctl", "stop", name)
            if state.get("UnitFileState") in ("enabled", "enabled-runtime"):
                run("systemctl", "disable", *( ["--runtime"] if state["UnitFileState"] == "enabled-runtime" else []), name)
        attempt(stop_service)
    # 先恢复链接目标的文件，再恢复链接本身，避免写穿已经改变的链接。
    items = sorted(snapshot["files"].items(), key=lambda item: item[1]["kind"] == "link")
    for filename, entry in items:
        def restore_file():
            path = Path(filename)
            if entry["kind"] == "absent":
                # 首次初始化的数据库和密钥必须成对保留，供失败后的重试使用。
                if path != CONFIG / "v2-production.json":
                    path.unlink(missing_ok=True)
            elif entry["kind"] == "file":
                atomic_bytes(path, (directory / entry["blob"]).read_bytes(), entry["mode"], (entry["uid"], entry["gid"]))
            else:
                replace_link(path, entry["target"])
                os.lchown(path, entry["uid"], entry["gid"])
        attempt(restore_file)
    attempt(lambda: run("systemctl", "daemon-reload"))
    for name, state in snapshot["services"].items():
        enabled = state.get("UnitFileState", "")
        if enabled in ("enabled", "enabled-runtime"):
            attempt(lambda: run("systemctl", "enable", *( ["--runtime"] if enabled == "enabled-runtime" else []), name))
    for name, state in snapshot["services"].items():
        if state.get("ActiveState") == "active":
            attempt(lambda: run("systemctl", "start", name))
    if attempt(lambda: run("nginx", "-t")):
        attempt(lambda: run("systemctl", "reload", "nginx"))
    active = [name for name, state in snapshot["services"].items() if state.get("ActiveState") == "active"]
    attempt(lambda: wait_healthy(active, snapshot["endpoints"]))
    if failures:
        raise RuntimeError("部分恢复步骤失败，配置恢复和入口重载均已尝试")


def transaction(action, operation):
    if read_marker("pending"):
        raise RuntimeError("存在未完成操作，请先执行 rollback，快照：" + read_marker("pending"))
    run("nginx", "-t")
    snapshot = save_snapshot(action)
    for state in snapshot["services"].values():
        if state.get("UnitFileState", "") not in ("", "enabled", "enabled-runtime", "disabled", "static"):
            raise RuntimeError("服务启用状态不支持自动恢复，请先检查 systemd 配置")
    set_marker("pending", snapshot["id"])
    print("切换前快照：" + snapshot["id"], flush=True)
    try:
        operation()
        record_status(snapshot, "committed")
        set_marker("head", snapshot["id"])
        set_marker("pending", None)
    except BaseException:
        try:
            restore_snapshot(snapshot)
            record_status(snapshot, "auto-rolled-back")
            set_marker("head", snapshot["parent"])
            set_marker("pending", None)
        except BaseException:
            record_status(snapshot, "recovery-required")
            raise RuntimeError("操作失败且自动恢复未完成；请执行 rollback，快照：" + snapshot["id"]) from None
        raise RuntimeError("操作失败，已恢复操作前配置、指针、服务和入口；快照：" + snapshot["id"]) from None


def rollback(identifier=None):
    latest = read_marker("pending") or read_marker("head")
    if identifier and identifier != latest:
        raise RuntimeError("仅允许回滚最近一次操作，请按操作顺序逐次回滚")
    snapshot = load_snapshot(identifier or latest)
    safety = save_snapshot("rollback-safety", check_health=False)
    print("回滚前现场快照：" + safety["id"], flush=True)
    set_marker("pending", snapshot["id"])
    try:
        restore_snapshot(snapshot)
        record_status(snapshot, "rolled-back")
        set_marker("head", snapshot["parent"])
        set_marker("pending", None)
    except BaseException:
        record_status(snapshot, "recovery-required")
        raise RuntimeError("回滚未完成，请排除故障后重试 rollback；目标快照：" + snapshot["id"]) from None
    print("已恢复操作前配置、指针、服务及 HTTPS 入口；快照：" + snapshot["id"])


def validate_release(path):
    release = Path(path).resolve()
    if not release.is_relative_to(ROOT / "releases") or not all((release / part / executable).is_file() for part, executable in (
        ("api", "VideoPlatform.Api"), ("worker", "VideoPlatform.Worker"), ("adapter", "Hikvision.Adapter"), ("web", "index.html"), ("deploy", "nginx-v2.conf"))):
        raise RuntimeError("候选版本目录无效")
    return release


def render_site(release, port, certificate_site):
    content = (release / "deploy/nginx-v2.conf").read_text()
    references = tls_references(certificate_site.read_text())
    content = TLS_DIRECTIVE.sub("", content)
    content, count = re.subn(r"^(\s*)ssl_protocols\b", lambda match: "".join(
        f"    {item['directive']} {item['value']};\n" for item in references) + match[0], content, count=1, flags=re.M)
    if count != 1:
        raise RuntimeError("Nginx 模板缺少 TLS 配置位置")
    if port != 443:
        content, plain_count = re.subn(r"\blisten 80;", "listen 127.0.0.1:18083;", content)
        content, tls_count = re.subn(r"\blisten 443 ssl;", f"listen {port} ssl;", content)
        if plain_count != 1 or tls_count != 1:
            raise RuntimeError("Nginx 模板监听端口不符合预期")
        content = content.replace("https://$host$request_uri", f"https://$host:{port}$request_uri")
    return content


def verify_deployment():
    endpoints = []
    for site in (MAIN_SITE, CANDIDATE_SITE):
        if site.exists():
            endpoint = site_endpoint(site)
            endpoint["expected"] = {"status": "ok", "service": "video-platform-api"} if site == CANDIDATE_SITE or public_url() == public_url(443) else {}
            endpoints.append(endpoint)
    wait_healthy(SERVICES, endpoints)


def switch():
    release = validate_release(CURRENT)
    values = environment_file(ENV)
    values["PLATFORM_PUBLIC_URL"] = public_url(443)
    write_environment(ENV, values)
    write_config(MAIN_SITE, render_site(release, 443, MAIN_SITE))
    run("nginx", "-t")
    for name in SERVICES:
        if name != "video-platform-v2-zlm.service":
            run("systemctl", "restart", name)
    wait_healthy(SERVICES, [])
    run("systemctl", "reload", "nginx")
    verify_deployment()


def environment_file(path):
    values = {}
    for line in Path(path).read_text().splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, value = line.split("=", 1)
            value = value.strip()
            if value.startswith('"') and value.endswith('"'):
                value = re.sub(r'\\([\\"$`])', r'\1', value[1:-1])
            elif value.startswith("'") and value.endswith("'"):
                value = value[1:-1]
            values[key] = value
    return values


def owned(path, mode=0o750):
    account = pwd.getpwnam("liteware")
    os.chown(path, account.pw_uid, account.pw_gid)
    os.chmod(path, mode)


def write_environment(path, values):
    account = pwd.getpwnam("liteware")
    content = "".join(f'{key}="{value.replace(chr(92), chr(92)*2).replace(chr(34), chr(92)+chr(34))}"\n' for key, value in values.items())
    atomic_bytes(path.resolve() if path.is_symlink() else path, content.encode(), 0o600, (account.pw_uid, account.pw_gid))


def install(release, switch_entry=False, public_port=8443):
    config_dir = CONFIG
    config_path = config_dir / "v2-production.json"
    if not config_path.exists():
        config = {"databasePassword": secrets.token_hex(24), "adapterKey": secrets.token_hex(32), "zlmSecret": secrets.token_hex(32), "adminPassword": secrets.token_urlsafe(24)}
        old = environment_file(config_dir / "platform-api.env")
        previous_password = old.get("PLATFORM_ADMIN_PASSWORD") or old.get("PLATFORM_BOOTSTRAP_PASSWORD")
        if previous_password and len(previous_password) >= 12:
            config["adminPassword"] = previous_password
        write_json(config_path, config)
        owned(config_path, 0o600)
    config = json.loads(config_path.read_text())
    psql = ("runuser", "-u", "postgres", "--", "psql", "-X", "-v", "ON_ERROR_STOP=1")
    if run(*psql, "-tAc", "SELECT 1 FROM pg_roles WHERE rolname='video_platform_v2'") != "1":
        run(*psql, "-c", f"CREATE ROLE video_platform_v2 LOGIN PASSWORD '{config['databasePassword']}';")
    if run(*psql, "-tAc", "SELECT 1 FROM pg_database WHERE datname='video_platform_v2'") != "1":
        run("runuser", "-u", "postgres", "--", "createdb", "-O", "video_platform_v2", "video_platform_v2")
    data = DATA
    for directory in [data, data / "keys", data / "exports", data / "releases", data / "adapter", data / "zlm"]:
        directory.mkdir(parents=True, exist_ok=True)
        owned(directory, 0o700 if directory.name == "keys" else 0o750)
    values = {
        "PLATFORM_DATABASE_URL": f"Host=127.0.0.1;Database=video_platform_v2;Username=video_platform_v2;Password={config['databasePassword']};Maximum Pool Size=64",
        "PLATFORM_ADMIN_USER": "admin", "PLATFORM_ADMIN_PASSWORD": config["adminPassword"],
        "PLATFORM_DATA_PATH": str(data), "PLATFORM_PUBLIC_URL": public_url(443 if switch_entry else public_port), "PLATFORM_API_URL": "http://127.0.0.1:5082",
        "PLATFORM_RTSP_URL": "rtsp://10.37.200.74:18555", "HIK_ADAPTER_API_URL": "http://127.0.0.1:5092",
        "HIK_ADAPTER_INTERNAL_KEY": config["adapterKey"], "HIK_ADAPTER_DATA_ROOT": str(data / "adapter"), "HIK_EXPORT_ROOT": str(data / "exports"),
        "HIK_SDK_DIR": "/opt/video-platform/sdk/hikvision", "LD_LIBRARY_PATH": "/opt/video-platform/sdk/hikvision",
        "HIK_FFMPEG_PATH": "/usr/bin/ffmpeg", "HIK_FFPROBE_PATH": "/usr/bin/ffprobe", "HIK_TRANSCODE_GLOBAL": "4",
        "ZLM_API_URL": "http://127.0.0.1:18082", "ZLM_API_SECRET": config["zlmSecret"],
        "HIK_ZLM_API_URL": "http://127.0.0.1:18082", "HIK_ZLM_API_SECRET": config["zlmSecret"],
        "HIK_ZLM_RTSP_URL": "rtsp://127.0.0.1:18555", "HIK_ZLM_RTMP_URL": "rtmp://127.0.0.1:11936",
        "Logging__LogLevel__Default": "Warning"
    }
    env_path = config_dir / "v2.env"
    write_environment(env_path, (environment_file(env_path) if env_path.exists() else {}) | values)
    zlm = configparser.ConfigParser(interpolation=None)
    zlm.optionxform = str
    zlm.read("/opt/video-platform/zlm/config.ini")
    def set_values(section, pairs):
        if not zlm.has_section(section): zlm.add_section(section)
        for key, value in pairs.items(): zlm.set(section, key, str(value))
    set_values("api", {"secret": config["zlmSecret"]})
    set_values("general", {"mediaServerId": "video-platform-v2"})
    set_values("http", {"port": 18082, "sslport": 0, "rootPath": str(data / "zlm/www"), "allow_ip_range": "127.0.0.1"})
    set_values("rtsp", {"port": 18555, "sslport": 0})
    set_values("rtmp", {"port": 11936, "sslport": 0})
    set_values("rtc", {"port": 0, "tcpPort": 0})
    set_values("srt", {"port": 0})
    set_values("rtp_proxy", {"port": 0})
    set_values("shell", {"port": 0})
    set_values("protocol", {"enable_hls": 0, "enable_mp4": 0, "enable_ts": 1})
    set_values("hook", {"enable": 1, "on_play": f"http://127.0.0.1:5082/internal/zlm/on-play?key={config['adapterKey']}", "on_stream_changed": f"http://127.0.0.1:5082/internal/zlm/on-stream-changed?key={config['adapterKey']}", "on_publish": "", "on_flow_report": "", "on_stream_none_reader": "", "on_stream_not_found": "", "on_server_started": ""})
    zlm_path = data / "zlm/config.ini"
    with zlm_path.open("w") as stream: zlm.write(stream, space_around_delimiters=False)
    owned(zlm_path, 0o600)
    replace_link(CURRENT, release)
    for part, executable in [("api", "VideoPlatform.Api"), ("worker", "VideoPlatform.Worker"), ("adapter", "Hikvision.Adapter")]:
        path = release / part / executable
        owned(path, 0o755)
        unit = f"""[Unit]
Description=VisiCore（视枢）第二版 {part}
After=network-online.target postgresql.service
Wants=network-online.target
[Service]
Type=simple
User=liteware
Group=liteware
WorkingDirectory=/opt/video-platform-v2/current/{part}
EnvironmentFile={env_path}
{('Environment="PLATFORM_DATABASE_URL=Host=127.0.0.1;Database=video_platform_v2;Username=video_platform_v2;Password=' + config['databasePassword'] + ';Maximum Pool Size=16"') if part == 'worker' else ''}
ExecStart=/opt/video-platform-v2/current/{part}/{executable}
Restart=always
RestartSec=5
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=read-only
ReadWritePaths={data}
KillMode=mixed
TimeoutStopSec=30
[Install]
WantedBy=multi-user.target
"""
        (UNIT_DIR / f"video-platform-v2-{part}.service").write_text(unit)
    zlm_unit = f"""[Unit]
Description=VisiCore（视枢）第二版流媒体
After=network-online.target
[Service]
Type=simple
User=liteware
Group=liteware
WorkingDirectory={data}/zlm
ExecStart=/opt/video-platform/zlm/MediaServer -c {zlm_path} --log-dir {data}/zlm/log --log-size 32 --log-slice 10 -l 2
Restart=always
RestartSec=5
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths={data}/zlm
[Install]
WantedBy=multi-user.target
"""
    (UNIT_DIR / "video-platform-v2-zlm.service").write_text(zlm_unit)
    run("systemctl", "daemon-reload")
    for part in ["zlm", "api", "adapter", "worker"]:
        if part == "worker":
            wait_healthy([name for name in SERVICES if not name.endswith("worker.service")], [])
        run("systemctl", "enable", f"video-platform-v2-{part}")
        run("systemctl", "restart", f"video-platform-v2-{part}")
    if switch_entry:
        write_config(MAIN_SITE, render_site(release, 443, MAIN_SITE))
    certificate_site = CANDIDATE_SITE if CANDIDATE_SITE.exists() else MAIN_SITE
    write_config(CANDIDATE_SITE, render_site(release, public_port, certificate_site))
    run("nginx", "-t")
    wait_healthy(SERVICES, [])
    run("systemctl", "reload", "nginx")
    verify_deployment()


def main():
    parser = argparse.ArgumentParser(description="安装、切换或回滚第二版候选实例")
    parser.add_argument("action", help="候选发布目录，或 switch、rollback")
    parser.add_argument("--switch", action="store_true", help="安装成功后切换正式入口")
    parser.add_argument("--public-port", type=int, default=8443, help="保留的候选 HTTPS 端口，默认 8443")
    parser.add_argument("--snapshot", help="回滚最近一次操作的快照编号")
    args = parser.parse_args()
    if args.snapshot and args.action != "rollback":
        parser.error("--snapshot 仅适用于 rollback")
    if args.action in ("switch", "rollback") and args.switch:
        parser.error("--switch 仅适用于安装")
    if not 1024 <= args.public_port <= 65535 or args.public_port in (5082, 5092, 11936, 18082, 18083, 18555):
        parser.error("候选端口必须是独立的高位 HTTPS 端口，不能使用正式或内部服务端口")
    if os.geteuid() != 0:
        raise RuntimeError("请使用 sudo 执行安装、切换或回滚")
    os.umask(0o077)
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(STATE, 0o700)
    def interrupted(signum, frame):
        raise InterruptedError("操作收到终止信号")
    signal.signal(signal.SIGTERM, interrupted)
    with (STATE / "lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError("另一个安装、切换或回滚操作正在运行") from None
        if args.action == "rollback":
            rollback(args.snapshot)
        elif args.action == "switch":
            transaction("switch", switch)
            print("HTTPS 443 已切换至第二版，公开 URL 已更新；现有候选站点保留")
        else:
            release = validate_release(args.action)
            if CURRENT.exists() and not CURRENT.is_symlink():
                raise RuntimeError("current 必须是符号链接")
            if not args.switch and ENV.exists() and public_url() == public_url(443):
                raise RuntimeError("第二版已使用正式入口，升级请显式携带 --switch")
            transaction("install-switch" if args.switch else "install", lambda: install(release, args.switch, args.public_port))
            print("第二版已安装并通过健康检查" + ("，HTTPS 443 已切换，候选站点保留" if args.switch else "，正式入口保持不变"))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print("部署操作失败：" + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), file=sys.stderr)
        raise SystemExit(1)
