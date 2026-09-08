"""真实设备连续值守：本地通过 SSH 管理，服务器由独立 systemd 服务运行。"""
import argparse
from datetime import datetime, timezone
import ipaddress
import json
import math
import os
from pathlib import Path
import re
import shlex
import shutil
import signal
import socket
import ssl
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


CONFIG_DIR = Path("/home/liteware/.config/video-platform")
STATE_DIR = Path("/var/lib/video-platform-v2-soak")
UNIT = "video-platform-v2-soak.service"
SERVICES = {part: f"video-platform-v2-{part}.service" for part in ("api", "worker", "adapter", "zlm")}
API_TIMEOUT = 15
MEDIA_TIMEOUT = 10
INTERVAL = 60


class SoakError(Exception):
    """只携带工具预定义的错误类别，禁止携带接口正文或请求地址。"""

    def __init__(self, code):
        self.code = code
        super().__init__(code)


class AuthenticationRejected(SoakError):
    """认证或授权已被拒绝，本轮不得重新登录或自动恢复权限。"""


def error_code(error):
    if isinstance(error, SoakError):
        return error.code
    if isinstance(error, urllib.error.HTTPError):
        return f"http_{error.code}"
    if isinstance(error, (TimeoutError, socket.timeout)):
        return "timeout"
    if isinstance(error, ssl.SSLError):
        return "tls_validation_or_transport"
    if isinstance(error, urllib.error.URLError):
        return "tls_validation_or_transport" if isinstance(error.reason, ssl.SSLError) else "network"
    if isinstance(error, OSError):
        return "operating_system"
    return "unexpected_error"


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def atomic_json(path, value):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8") as stream:
            os.chmod(temporary, 0o600)
            json.dump(value, stream, ensure_ascii=False, allow_nan=False)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
    finally:
        temporary.unlink(missing_ok=True)


def environment_values(path):
    result = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        value = value.strip()
        if value.startswith('"') and value.endswith('"'):
            value = re.sub(r'\\([\\"$`])', r'\1', value[1:-1])
        elif value.startswith("'") and value.endswith("'"):
            value = value[1:-1]
        result[key] = value
    return result


def public_origin(value):
    parsed = urllib.parse.urlsplit(value)
    if (parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment or parsed.port not in (None, 443, 8443)):
        raise SoakError("invalid_public_origin")
    return value.rstrip("/")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        raise SoakError("redirect_rejected")


def opener(context):
    return urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(), urllib.request.HTTPSHandler(context=context))


def request_json(context, url, method="GET", body=None, headers=None):
    actual_headers = {"Content-Type": "application/json", **(headers or {})}
    content = json.dumps(body).encode() if body is not None else (b"" if method == "POST" else None)
    request = urllib.request.Request(url, data=content, headers=actual_headers, method=method)
    with opener(context).open(request, timeout=API_TIMEOUT) as response:
        content = response.read(4 * 1024 * 1024 + 1)
        if len(content) > 4 * 1024 * 1024:
            raise SoakError("response_too_large")
        return json.loads(content) if content else None


class ApiClient:
    """每个读取线程独占登录会话，避免续期和重连之间的令牌竞争。"""

    def __init__(self, origin, context, username, password, stop=None):
        self.origin, self.context = origin, context
        self.username, self.password = username, password
        self.token = None
        self.refresh_at = 0
        self.stop = stop if stop is not None else threading.Event()
        self.authentication_failure = None

    def request(self, method, path, body=None, phase="control"):
        if self.authentication_failure is not None:
            raise self.authentication_failure
        headers = {"Authorization": "Bearer " + self.token} if self.token else None
        try:
            return request_json(self.context, self.origin + path, method, body, headers)
        except urllib.error.HTTPError as error:
            if error.code not in (401, 403):
                raise
            self.authentication_failure = AuthenticationRejected(f"auth_{phase}_http_{error.code}")
            self.stop.set()
            raise self.authentication_failure from None

    def authenticate(self):
        if self.authentication_failure is not None:
            raise self.authentication_failure
        if self.stop.is_set():
            raise SoakError("run_stopping")
        if self.token is None:
            result = self.request("POST", "/api/v2/auth/login", {
                "username": self.username, "password": self.password,
                "clientType": "desktop", "clientVersion": "2.0.0-real-device-soak"}, phase="login")
            self.token = result.get("accessToken")
            if not isinstance(self.token, str) or not self.token:
                self.token = None
                raise SoakError("missing_login_token")
            self.refresh_at = time.monotonic() + 3600
        elif time.monotonic() >= self.refresh_at:
            result = self.request("POST", "/api/v2/auth/refresh", phase="refresh")
            self.token = result.get("accessToken") or self.token
            self.refresh_at = time.monotonic() + 3600

    def call(self, method, path, body=None):
        self.authenticate()
        if self.stop.is_set():
            raise SoakError("run_stopping")
        return self.request(method, path, body)

    def cleanup_call(self, method, path):
        # 清理只使用已有令牌，不续期、不创建登录，也不绕过主动撤销。
        if self.token is None:
            raise SoakError("cleanup_auth_unavailable")
        return self.request(method, path, phase="cleanup")

    def logout(self):
        if self.token is None:
            return
        self.request("POST", "/api/v2/auth/logout", phase="logout")
        self.token = None


class Recorder:
    def __init__(self, directory, target_seconds):
        self.directory = directory
        self.lock = threading.RLock()
        self.journal_lock = threading.Lock()
        self.checkpoint_lock = threading.Lock()
        self.persistence_failed = threading.Event()
        self.started = time.monotonic()
        self.observation_start = None
        self.stop_time = None
        self.report = {
            "schemaVersion": 1, "runId": directory.name, "startedAt": utc_now(), "updatedAt": utc_now(),
            "status": "starting", "passed": False, "realDeviceVerified": False,
            "targetSeconds": target_seconds, "failureCount": 0, "failuresByCategory": {},
            "streams": [], "metrics": {}, "samples": 0, "cleanupComplete": False,
        }
        self.journal = (directory / "events.jsonl").open("a", encoding="utf-8")

    def event(self, kind, **values):
        record = {"at": utc_now(), "elapsedSeconds": round(time.monotonic() - self.started, 3), "event": kind, **values}
        with self.journal_lock:
            try:
                self.journal.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")
                self.journal.flush()
                os.fsync(self.journal.fileno())
            except OSError:
                self.persistence_failed.set()

    def failure(self, category, count=1, channel_id=None):
        with self.lock:
            self.report["failureCount"] += count
            categories = self.report["failuresByCategory"]
            categories[category] = categories.get(category, 0) + count
        self.event("failure", category=category, count=count, channelId=channel_id)

    def checkpoint(self):
        with self.checkpoint_lock:
            self._checkpoint()

    def _checkpoint(self):
        with self.lock:
            now = self.stop_time or time.monotonic()
            self.report["updatedAt"] = utc_now()
            self.report["elapsedSeconds"] = round(now - self.started, 3)
            observed = max(0, now - self.observation_start) if self.observation_start is not None else 0
            self.report["observedSeconds"] = round(observed, 3)
            self.report["targetReached"] = observed >= self.report["targetSeconds"]
            self.report["duration24hReached"] = observed >= 86400
            self.report["disconnectCount"] = sum(item["disconnects"] for item in self.report["streams"])
            self.report["recoveryCount"] = sum(item["recoveries"] for item in self.report["streams"])
            self.report["reconnectionCount"] = sum(item["reconnections"] for item in self.report["streams"])
            snapshot = json.loads(json.dumps(self.report))
        self.event("checkpoint", progress=snapshot)
        if self.persistence_failed.is_set():
            snapshot.update(passed=False, status="failed", persistenceFailed=True)
        try:
            atomic_json(self.directory / "progress.json", snapshot)
        except OSError:
            self.persistence_failed.set()


class TsFraming:
    """只核验 MPEG-TS 的 188 字节包同步，不把传输检查冒充画面解码检查。"""

    def __init__(self):
        self.buffer = bytearray()
        self.aligned = False

    def consume(self, data):
        self.buffer.extend(data)
        if not self.aligned:
            if len(self.buffer) < 188 * 6:
                return 0
            offset = next((offset for offset in range(188) if all(self.buffer[offset + 188 * index] == 0x47 for index in range(5))), None)
            if offset is None:
                raise SoakError("invalid_mpeg_ts")
            del self.buffer[:offset]
            self.aligned = True
        packets = len(self.buffer) // 188
        if any(value != 0x47 for value in self.buffer[:packets * 188:188]):
            raise SoakError("mpeg_ts_sync_lost")
        del self.buffer[:packets * 188]
        return packets


class StreamWorker(threading.Thread):
    def __init__(self, channel, api, recorder, stop):
        super().__init__(name="真实主码流-" + str(channel["id"]), daemon=True)
        self.api, self.recorder, self.stop = api, recorder, stop
        self.channel_id = int(channel["id"])
        self.session_id = None
        self.must_logout = False
        self.response = None
        self.response_lock = threading.Lock()
        self.last_data = None
        self.outage_start = None
        self.fatal = False
        self.stats = {"channelId": self.channel_id, "deviceId": int(channel["deviceId"]), "deviceChannel": int(channel["deviceChannel"]),
                      "streamType": 1, "profile": "native", "transport": "https-mpeg-ts", "state": "starting",
                      "bytes": 0, "tsPackets": 0, "attempts": 0, "connectionsEstablished": 0, "reconnections": 0,
                      "failures": 0, "disconnects": 0, "outages": 0, "recoveries": 0, "renewals": 0,
                      "renewFailures": 0, "outageSeconds": 0, "lastDataAt": None, "cleanupComplete": False}
        recorder.report["streams"].append(self.stats)

    def close_response(self):
        with self.response_lock:
            response, self.response = self.response, None
        if response is not None:
            response.close()

    def cleanup_session(self):
        if self.session_id is not None:
            self.api.cleanup_call("DELETE", "/api/v2/live-sessions/" + self.session_id)
            self.session_id = None
        if self.must_logout:
            self.api.logout()
            self.must_logout = False

    def receive(self):
        self.cleanup_session()
        with self.recorder.lock:
            self.stats["attempts"] += 1
            self.stats["state"] = "connecting"
        # POST 超时可能已经创建会话；未取得编号时，重试前必须注销该独立登录。
        self.must_logout = True
        grant = self.api.call("POST", "/api/v2/live-sessions", {"channelId": self.channel_id, "streamType": 1, "profile": "native"})
        self.session_id = str(uuid.UUID(grant["id"]))
        self.must_logout = False
        if grant.get("streamType") != 1 or grant.get("transcoded") is not False:
            raise SoakError("native_main_stream_not_honored")
        media_url = grant.get("httpTsUrl", "")
        parsed = urllib.parse.urlsplit(media_url)
        origin = urllib.parse.urlsplit(self.api.origin)
        if (parsed.scheme != "https" or parsed.hostname != origin.hostname or (parsed.port or 443) != (origin.port or 443)
                or parsed.username or parsed.password or parsed.fragment
                or not re.fullmatch(r"/media/live/[A-Za-z0-9_-]+\.live\.ts", parsed.path)):
            raise SoakError("invalid_https_ts_grant")
        response = opener(self.api.context).open(urllib.request.Request(media_url), timeout=MEDIA_TIMEOUT)
        with self.response_lock:
            self.response = response
        if response.status != 200:
            raise SoakError("media_status_not_ok")
        framing = TsFraming()
        next_renew = time.monotonic() + INTERVAL
        packet_deadline = time.monotonic() + MEDIA_TIMEOUT
        established = False
        while not self.stop.is_set():
            if time.monotonic() >= next_renew:
                try:
                    renewed = self.api.call("POST", "/api/v2/live-sessions/" + self.session_id + "/renew")
                    if renewed.get("state") in ("stopped", "stopping", "failed", "completed"):
                        raise SoakError("renewed_session_not_playing")
                except Exception:
                    with self.recorder.lock:
                        self.stats["renewFailures"] += 1
                    raise
                with self.recorder.lock:
                    self.stats["renewals"] += 1
                next_renew += INTERVAL
            data = response.read1(65536)
            if not data:
                raise SoakError("media_eof")
            packets = framing.consume(data)
            now = time.monotonic()
            if packets:
                packet_deadline = now + MEDIA_TIMEOUT
            elif now >= packet_deadline:
                raise SoakError("mpeg_ts_packet_timeout")
            with self.recorder.lock:
                self.stats["bytes"] += len(data)
                self.stats["tsPackets"] += packets
                if packets:
                    self.last_data = now
                    self.stats["lastDataAt"] = utc_now()
                    if not established:
                        established = True
                        self.stats["connectionsEstablished"] += 1
                        if self.stats["connectionsEstablished"] > 1:
                            self.stats["reconnections"] += 1
                        if self.outage_start is not None:
                            self.stats["recoveries"] += 1
                            self.stats["outageSeconds"] += now - self.outage_start
                            self.outage_start = None
                        self.stats["state"] = "streaming"
                        self.recorder.event("stream_established", channelId=self.channel_id,
                                            recoveryCount=self.stats["recoveries"], reconnectionCount=self.stats["reconnections"])

    def run(self):
        delay = 5
        try:
            while not self.stop.is_set():
                try:
                    self.receive()
                except Exception as error:
                    auth_rejected = isinstance(error, AuthenticationRejected) or (
                        isinstance(error, urllib.error.HTTPError) and error.code in (401, 403))
                    if self.stop.is_set() and not auth_rejected:
                        break
                    now = time.monotonic()
                    with self.recorder.lock:
                        self.stats["failures"] += 1
                        if self.stats["state"] == "streaming":
                            self.stats["disconnects"] += 1
                            delay = 5
                        if self.outage_start is None:
                            self.stats["outages"] += 1
                            self.outage_start = self.last_data or now
                        self.stats["state"] = "recovering"
                    self.recorder.failure("stream_" + error_code(error), channel_id=self.channel_id)
                    if auth_rejected:
                        self.fatal = True
                        self.stop.set()
                finally:
                    self.close_response()
                if not self.stop.is_set():
                    self.stop.wait(delay)
                    delay = min(30, delay * 2)
        finally:
            cleanup_ok = False
            for attempt in range(3):
                try:
                    self.cleanup_session()
                    self.api.logout()
                    cleanup_ok = True
                    break
                except Exception as error:
                    self.recorder.failure("stream_cleanup_" + error_code(error), channel_id=self.channel_id)
                    if isinstance(error, AuthenticationRejected):
                        self.fatal = True
                        self.stop.set()
                        break
            with self.recorder.lock:
                if self.outage_start is not None:
                    self.stats["outageSeconds"] += time.monotonic() - self.outage_start
                self.stats["state"] = "stopped"
                self.stats["cleanupComplete"] = cleanup_ok


def command(*args):
    result = subprocess.run(args, capture_output=True, text=True, timeout=20)
    if result.returncode:
        raise SoakError("service_command_failed")
    return result.stdout


def unit_properties(unit, properties):
    text = command("systemctl", "show", unit, "--property=" + ",".join(properties))
    return dict(line.split("=", 1) for line in text.splitlines() if "=" in line)


def numeric(value):
    if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value):
        return value
    raise SoakError("metric_missing")


class Sampler(threading.Thread):
    def __init__(self, api, context, adapter_key, zlm_secret, recorder, stop):
        super().__init__(name="真实设备资源采样", daemon=True)
        self.api, self.context, self.adapter_key, self.zlm_secret = api, context, adapter_key, zlm_secret
        self.recorder, self.stop = recorder, stop
        self.previous = {}
        self.restart_counts = {name: 0 for name in SERVICES}
        self.fatal = False
        self.cleanup_complete = False

    def adapter_health(self):
        health = request_json(self.context, "http://127.0.0.1:5092/health", headers={"X-Adapter-Key": self.adapter_key})
        if health.get("service") != "hikvision-adapter" or health.get("detail", {}).get("simulated") is not False:
            self.fatal = True
            raise SoakError("real_device_not_verified")
        return health

    def service_metrics(self, name, unit):
        values = unit_properties(unit, ("MainPID", "ActiveState", "InvocationID", "NRestarts", "ControlGroup", "MemoryCurrent", "CPUUsageNSec"))
        pid = int(values.get("MainPID") or 0)
        if values.get("ActiveState") != "active" or pid <= 0:
            raise SoakError("service_inactive")
        fields = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
        process_cpu = (int(fields[11]) + int(fields[12])) / os.sysconf("SC_CLK_TCK")
        memory = int(fields[21]) * os.sysconf("SC_PAGE_SIZE")
        identity = (values.get("InvocationID"), pid, fields[19])
        cgroup = Path("/sys/fs/cgroup") / values.get("ControlGroup", "").lstrip("/")
        group_cpu = None
        group_memory = None
        if (cgroup / "cpu.stat").is_file() and (cgroup / "memory.current").is_file():
            cpu_values = dict(line.split() for line in (cgroup / "cpu.stat").read_text().splitlines())
            group_cpu = int(cpu_values["usage_usec"]) / 1000000
            group_memory = int((cgroup / "memory.current").read_text())
        elif values.get("CPUUsageNSec", "").isdigit() and values.get("MemoryCurrent", "").isdigit():
            group_cpu = int(values["CPUUsageNSec"]) / 1000000000
            group_memory = int(values["MemoryCurrent"])
        current = {"at": time.monotonic(), "identity": identity, "autoRestarts": int(values.get("NRestarts") or 0),
                   "processCpu": process_cpu, "groupCpu": group_cpu}
        previous = self.previous.get(name)
        process_percent = group_percent = None
        if previous:
            delta = max(int(previous["identity"] != identity), max(0, current["autoRestarts"] - previous["autoRestarts"]))
            if delta:
                self.restart_counts[name] += delta
                self.recorder.failure("service_restart_" + name, count=delta)
            if previous["identity"] == identity:
                elapsed = current["at"] - previous["at"]
                process_percent = max(0, (process_cpu - previous["processCpu"]) / elapsed * 100)
                if group_cpu is not None and previous["groupCpu"] is not None and group_cpu >= previous["groupCpu"]:
                    group_percent = (group_cpu - previous["groupCpu"]) / elapsed * 100
        self.previous[name] = current
        if group_cpu is None or group_memory is None:
            self.recorder.failure("cgroup_metric_unavailable_" + name)
        return {"pid": pid, "state": "active", "rssBytes": memory, "cpuPercent": process_percent,
                "cgroupMemoryBytes": group_memory, "cgroupCpuPercent": group_percent,
                "systemdAutoRestarts": current["autoRestarts"], "observedRestarts": self.restart_counts[name]}

    def sample(self):
        metrics = {"sampledAt": utc_now(), "services": {}, "disk": {}}
        for name, unit in SERVICES.items():
            try:
                metrics["services"][name] = self.service_metrics(name, unit)
            except Exception as error:
                metrics["services"][name] = {"available": False, "observedRestarts": self.restart_counts[name]}
                self.recorder.failure("metric_" + name + "_" + error_code(error))
        for name, path in (("root", "/"), ("data", "/var/lib/video-platform/v2"), ("reports", str(self.recorder.directory))):
            try:
                disk = shutil.disk_usage(path)
                metrics["disk"][name] = {"totalBytes": disk.total, "usedBytes": disk.used, "freeBytes": disk.free}
                if disk.free < 1024 * 1024 * 1024:
                    self.recorder.failure("disk_low_" + name)
            except Exception as error:
                self.recorder.failure("disk_" + error_code(error))
        try:
            dashboard = self.api.call("GET", "/api/v2/dashboard")
            metrics["platform"] = {key: numeric(dashboard.get(key)) for key in ("liveSessions", "playbackSessions", "onlineSessions", "onlineDevices", "onlineChannels")}
            system = self.api.call("GET", "/api/v2/system")
            if any(service.get("status") != "online" for service in system.get("services", [])):
                self.recorder.failure("platform_service_degraded")
        except Exception as error:
            self.recorder.failure("platform_metrics_" + error_code(error))
            if isinstance(error, AuthenticationRejected):
                self.fatal = True
                self.stop.set()
        try:
            self.adapter_health()
            sessions = request_json(self.context, "http://127.0.0.1:5092/internal/sessions", headers={"X-Adapter-Key": self.adapter_key})
            metrics["adapter"] = {key + "Count": len(sessions[key]) for key in ("devices", "live", "playback") if isinstance(sessions.get(key), list)}
        except Exception as error:
            self.recorder.failure("adapter_metrics_" + error_code(error))
            if self.fatal:
                self.stop.set()
        try:
            # 密钥放在 POST 正文，避免出现在查询字符串或进程命令行。
            data = urllib.parse.urlencode({"secret": self.zlm_secret}).encode()
            request = urllib.request.Request("http://127.0.0.1:18082/index/api/getAllSession", data=data,
                                             headers={"Content-Type": "application/x-www-form-urlencoded"})
            with opener(self.context).open(request, timeout=API_TIMEOUT) as response:
                result = json.loads(response.read(4 * 1024 * 1024))
            if result.get("code") != 0 or not isinstance(result.get("data"), list):
                raise SoakError("zlm_connections_unavailable")
            metrics["zlmConnectionCount"] = len(result["data"])
        except Exception as error:
            self.recorder.failure("zlm_metrics_" + error_code(error))
        with self.recorder.lock:
            self.recorder.report["metrics"] = metrics
            self.recorder.report["samples"] += 1
        self.recorder.event("metrics", values=metrics)

    def run(self):
        try:
            while not self.stop.is_set():
                started = time.monotonic()
                self.sample()
                if time.monotonic() - started > INTERVAL:
                    self.recorder.failure("sampling_interval_overrun")
                self.stop.wait(max(0, INTERVAL - (time.monotonic() - started)))
        except Exception as error:
            self.fatal = True
            self.recorder.failure("sampler_" + error_code(error))
            self.stop.set()
        finally:
            for attempt in range(3):
                try:
                    self.api.logout()
                    self.cleanup_complete = True
                    break
                except Exception as error:
                    self.recorder.failure("sampler_cleanup_" + error_code(error))
                    if isinstance(error, AuthenticationRejected):
                        self.fatal = True
                        self.stop.set()
                        break


def select_channels(api, requested, stop):
    channels = []
    deadline = time.monotonic() + 300
    for page in range(1, 10001):
        if stop.is_set() or time.monotonic() >= deadline:
            raise SoakError("channel_selection_interrupted")
        result = api.call("GET", f"/api/v2/channels?online=true&pageSize=200&page={page}")
        items = result.get("items", [])
        channels.extend(items)
        if (not requested and len(channels) >= 2) or (requested and set(requested).issubset({item["id"] for item in channels})):
            break
        if len(channels) >= int(result["total"]) or not items:
            break
    if requested:
        chosen = [next((item for item in channels if item["id"] == identifier), None) for identifier in requested]
        if any(item is None for item in chosen):
            raise SoakError("requested_channel_not_online")
    else:
        chosen = channels[:2]
    if len(chosen) != 2 or len({int(item["id"]) for item in chosen}) != 2:
        raise SoakError("two_distinct_online_channels_required")
    for device_id in {int(item["deviceId"]) for item in chosen}:
        device = api.call("GET", f"/api/v2/devices/{device_id}")
        if device.get("enabled") is not True or device.get("status") != "online":
            raise SoakError("real_device_not_online")
        try:
            address = ipaddress.ip_address(device["host"])
        except ValueError:
            if str(device.get("host", "")).lower() in ("localhost", ""):
                raise SoakError("loopback_device_rejected") from None
        else:
            if address.is_loopback or address.is_unspecified:
                raise SoakError("loopback_device_rejected")
    return chosen


def run_soak(args):
    os.umask(0o077)
    directory = STATE_DIR / "runs" / args.run_id
    recorder = Recorder(directory, args.hours * 3600)
    stop = threading.Event()
    signal.signal(signal.SIGTERM, lambda signum, frame: stop.set())
    signal.signal(signal.SIGINT, lambda signum, frame: stop.set())
    workers = []
    sampler = None
    control_api = None
    reached = False
    failed = False
    checkpoint_stop = threading.Event()
    def write_checkpoints():
        while not checkpoint_stop.wait(INTERVAL):
            recorder.checkpoint()
            if recorder.persistence_failed.is_set():
                stop.set()
    writer = threading.Thread(target=write_checkpoints, name="值守进度持久化", daemon=True)
    recorder.checkpoint()
    writer.start()
    try:
        values = environment_values(CONFIG_DIR / "v2.env")
        production = json.loads((CONFIG_DIR / "v2-production.json").read_text(encoding="utf-8"))
        origin = public_origin(args.public_url or values["PLATFORM_PUBLIC_URL"])
        context = ssl.create_default_context()
        context.load_verify_locations(cafile=args.ca_file)
        if values.get("HIK_ADAPTER_SIMULATOR", "").lower() in ("1", "true"):
            raise SoakError("simulator_configuration_rejected")
        username = values.get("PLATFORM_ADMIN_USER", "admin")
        password = production["adminPassword"]
        def new_api():
            return ApiClient(origin, context, username, password, stop)
        control_api = new_api()
        health = request_json(context, origin + "/health")
        if health.get("service") != "video-platform-api" or health.get("status") != "ok":
            raise SoakError("public_entry_not_v2")
        sampler = Sampler(control_api, context, values["HIK_ADAPTER_INTERNAL_KEY"], values["ZLM_API_SECRET"], recorder, stop)
        sampler.adapter_health()
        channels = select_channels(control_api, args.channels, stop)
        with recorder.lock:
            recorder.report["realDeviceVerified"] = True
            recorder.report["publicPort"] = urllib.parse.urlsplit(origin).port or 443
        workers = [StreamWorker(channel, new_api(), recorder, stop) for channel in channels]
        recorder.event("real_device_selected", channelIds=[int(channel["id"]) for channel in channels])
        sampler.start()
        for worker in workers:
            worker.start()
        recorder.checkpoint()
        startup_deadline = time.monotonic() + 300
        while not stop.wait(0.5):
            now = time.monotonic()
            if recorder.persistence_failed.is_set():
                raise SoakError("report_persistence_failed")
            if any(not worker.is_alive() for worker in workers) or not sampler.is_alive():
                raise SoakError("background_worker_exited")
            with recorder.lock:
                if recorder.observation_start is None and all(worker.stats["state"] == "streaming" for worker in workers):
                    recorder.observation_start = now
                    recorder.report["observationStartedAt"] = utc_now()
                    recorder.report["status"] = "running"
                    recorder.event("observation_started")
                if recorder.observation_start is None and now >= startup_deadline:
                    raise SoakError("two_stream_startup_timeout")
                reached = recorder.observation_start is not None and now - recorder.observation_start >= args.hours * 3600
            if reached:
                break
        failed = sampler.fatal or any(worker.fatal for worker in workers)
    except Exception as error:
        failed = True
        recorder.failure("runner_" + error_code(error))
    finally:
        recorder.stop_time = time.monotonic()
        stop.set()
        with recorder.lock:
            recorder.report["status"] = "stopping"
        recorder.checkpoint()
        cleanup_deadline = time.monotonic() + 150
        for worker in workers:
            if worker.ident is not None:
                worker.join(max(0, cleanup_deadline - time.monotonic()))
        if sampler is not None and sampler.ident is not None:
            sampler.join(max(0, cleanup_deadline - time.monotonic()))
        elif control_api is not None:
            try:
                control_api.logout()
                if sampler:
                    sampler.cleanup_complete = True
            except Exception as error:
                recorder.failure("startup_cleanup_" + error_code(error))
        cleanup_ok = all(not worker.is_alive() and worker.stats["cleanupComplete"] for worker in workers)
        cleanup_ok = cleanup_ok and (sampler is None or (not sampler.is_alive() and sampler.cleanup_complete))
        if not cleanup_ok:
            recorder.failure("cleanup_incomplete")
        checkpoint_stop.set()
        writer.join(5)
        with recorder.lock:
            recorder.report["cleanupComplete"] = cleanup_ok
            failed = (failed or recorder.persistence_failed.is_set() or any(worker.fatal for worker in workers)
                      or (sampler is not None and sampler.fatal))
            passed = not failed and reached and args.hours >= 24 and recorder.report["failureCount"] == 0 and cleanup_ok
            recorder.report["passed"] = passed
            recorder.report["status"] = ("failed" if failed else "stopped" if not reached else "passed" if passed
                                         else "completed_with_failures" if recorder.report["failureCount"] else "short_run_completed")
            recorder.report["finishedAt"] = utc_now()
        recorder.checkpoint()
    return 0 if not recorder.persistence_failed.is_set() and reached and recorder.report["failureCount"] == 0 and cleanup_ok else 2


def valid_run_id(value):
    if not re.fullmatch(r"\d{8}T\d{6}Z-[a-f0-9]{12}", value or ""):
        raise SoakError("invalid_run_id")
    return value


def remote_status():
    current = STATE_DIR / "current"
    properties = unit_properties(UNIT, ("LoadState", "ActiveState", "SubState", "Result", "ExecMainStatus"))
    result = {"service": properties, "report": None}
    if current.exists():
        run_id = valid_run_id(current.read_text().strip())
        directory = STATE_DIR / "runs" / run_id
        if (directory / "progress.json").exists():
            result["report"] = json.loads((directory / "progress.json").read_text())
            result["reportDirectory"] = str(directory)
            report = result["report"]
            age = (datetime.now(timezone.utc) - datetime.fromisoformat(report["updatedAt"])).total_seconds()
            result["progressAgeSeconds"] = round(max(0, age), 1)
            unfinished = report.get("status") in ("starting", "running", "stopping")
            result["effectiveStatus"] = ("interrupted" if unfinished and properties.get("ActiveState") not in ("active", "activating", "deactivating")
                                         else "stale" if unfinished and age > 180 else report.get("status"))
            result["passed"] = report.get("passed") is True and result["effectiveStatus"] == "passed"
    return result


def remote_control(args):
    import fcntl
    import pwd
    if os.geteuid() != 0:
        raise SoakError("sudo_required")
    account = pwd.getpwnam("liteware")
    STATE_DIR.mkdir(mode=0o750, parents=True, exist_ok=True)
    os.chown(STATE_DIR, 0, account.pw_gid)
    with (STATE_DIR / "control.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if args.action == "status":
            return remote_status()
        if args.action == "stop":
            # 使用独立进程等待 systemd 的有限清理窗口，不保留长时间 SSH 会话。
            result = subprocess.run(["systemctl", "stop", "--no-block", UNIT], capture_output=True, timeout=20)
            if result.returncode and unit_properties(UNIT, ("LoadState",)).get("LoadState") != "not-found":
                raise SoakError("stop_failed")
            return {"message": "停止请求已提交，请用 status 确认清理结果", **remote_status()}
        state = unit_properties(UNIT, ("ActiveState",))
        if state.get("ActiveState") in ("active", "activating", "deactivating", "reloading"):
            raise SoakError("soak_already_running")
        run_id = valid_run_id(args.run_id)
        directory = STATE_DIR / "runs" / run_id
        directory.mkdir(mode=0o750, parents=True, exist_ok=False)
        os.chown(directory.parent, 0, account.pw_gid)
        os.chmod(directory.parent, 0o750)
        os.chown(directory, account.pw_uid, account.pw_gid)
        runner = directory / "runner.py"
        shutil.copyfile(__file__, runner)
        os.chmod(runner, 0o644)
        # 原证书目录可能仅 root 可读，后台账户只读取本次运行的公开证书副本。
        certificate = Path(args.ca_file).read_bytes()
        if len(certificate) > 1024 * 1024 or b"PRIVATE KEY" in certificate or b"-----BEGIN CERTIFICATE-----" not in certificate:
            raise SoakError("public_certificate_required")
        try:
            ssl.create_default_context().load_verify_locations(cadata=certificate.decode("ascii"))
        except (UnicodeError, ssl.SSLError):
            raise SoakError("invalid_public_certificate") from None
        runtime_ca = directory / "public-ca.pem"
        with runtime_ca.open("xb") as stream:
            os.fchmod(stream.fileno(), 0o600)
            os.fchown(stream.fileno(), account.pw_uid, account.pw_gid)
            stream.write(certificate)
            stream.flush()
            os.fsync(stream.fileno())
        atomic_json(directory / "progress.json", {"runId": run_id, "status": "starting", "updatedAt": utc_now(), "passed": False})
        os.chown(directory / "progress.json", account.pw_uid, account.pw_gid)
        marker = STATE_DIR / ("current." + uuid.uuid4().hex)
        marker.write_text(run_id)
        os.replace(marker, STATE_DIR / "current")
        arguments = ["/usr/bin/python3", "-u", str(runner), "_run", "--run-id", run_id, "--hours", str(args.hours), "--ca-file", str(runtime_ca)]
        if args.channels:
            arguments += ["--channels", *(str(value) for value in args.channels)]
        if args.public_url:
            arguments += ["--public-url", args.public_url]
        properties = ["User=liteware", "Group=liteware", "Type=exec", "Restart=no", "UMask=0077", "KillMode=mixed",
                      "TimeoutStopSec=180", f"RuntimeMaxSec={math.ceil(args.hours * 3600) + 900}", "NoNewPrivileges=yes",
                      "ProtectSystem=strict", "ProtectHome=read-only", "PrivateTmp=yes", "LimitCORE=0",
                      "StandardOutput=null", "StandardError=null", "ReadWritePaths=" + str(directory)]
        command_args = ["systemd-run", "--quiet", "--collect", "--unit=" + UNIT]
        command_args += ["--property=" + value for value in properties]
        result = subprocess.run(command_args + arguments, capture_output=True, timeout=30)
        if result.returncode:
            atomic_json(directory / "progress.json", {"runId": run_id, "status": "failed", "updatedAt": utc_now(), "passed": False,
                                                       "failureCategory": "systemd_start_failed"})
            raise SoakError("systemd_start_failed")
        return {"message": "后台值守已提交，尚未完成验证", "runId": run_id, "reportDirectory": str(directory), "service": UNIT}


def local_control(args):
    import paramiko
    password = os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD")
    if not password or "\n" in password or "\r" in password:
        raise SoakError("ssh_password_missing_or_invalid")
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    uploads = []
    try:
        client.connect(args.host, username=args.ssh_user, password=password, timeout=15, auth_timeout=15,
                       banner_timeout=15, look_for_keys=False, allow_agent=False)
        with client.open_sftp() as sftp:
            staging = sftp.normalize(".") + "/.v2-soak-upload"
            try:
                sftp.mkdir(staging, mode=0o700)
            except OSError:
                if not sftp.stat(staging).st_mode & 0o40000:
                    raise SoakError("invalid_upload_directory") from None
            upload = staging + "/" + uuid.uuid4().hex + ".py"
            uploads.append(upload)
            sftp.put(str(Path(__file__).resolve()), upload)
            sftp.chmod(upload, 0o600)
            remote_ca = args.ca_file
            if args.action == "start" and args.local_ca_file:
                remote_ca = staging + "/" + uuid.uuid4().hex + ".pem"
                uploads.append(remote_ca)
                sftp.put(str(Path(args.local_ca_file).resolve()), remote_ca)
                sftp.chmod(remote_ca, 0o600)
        arguments = ["python3", "-u", upload, "_control", args.action]
        if args.action == "start":
            run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:12]
            arguments += ["--run-id", run_id, "--hours", str(args.hours), "--ca-file", remote_ca]
            if args.channels:
                arguments += ["--channels", *(str(value) for value in args.channels)]
            if args.public_url:
                arguments += ["--public-url", args.public_url]
        stdin, stdout, stderr = client.exec_command("sudo -S -p '' " + shlex.join(arguments), timeout=90)
        stdin.write(password + "\n")
        stdin.flush()
        stdin.channel.shutdown_write()
        output = stdout.read().decode("utf-8", "replace")
        error = stderr.read().decode("utf-8", "replace")
        if stdout.channel.recv_exit_status():
            category = re.search(r"错误类别：([a-z0-9_]+)", error)
            if category and category[1] in {"public_certificate_required", "invalid_public_certificate", "soak_already_running", "systemd_start_failed", "stop_failed", "operating_system"}:
                raise SoakError("remote_" + category[1])
            raise SoakError("remote_control_failed")
        print(json.dumps(json.loads(output), ensure_ascii=False, indent=2))
    finally:
        for upload in uploads:
            try:
                with client.open_sftp() as sftp:
                    sftp.remove(upload)
            except Exception:
                pass
        client.close()


def main():
    parser = argparse.ArgumentParser(description="两路真实设备 native 主码流的 24 小时连续值守")
    parser.add_argument("mode", choices=("start", "status", "stop", "_control", "_run"))
    parser.add_argument("action", nargs="?", choices=("start", "status", "stop"))
    parser.add_argument("--host", default="10.37.200.74", help="SSH 目标服务器")
    parser.add_argument("--ssh-user", default="liteware", help="SSH 用户")
    parser.add_argument("--hours", type=float, default=24, help="两路首次同时有效收流后的观测时长，默认 24 小时")
    parser.add_argument("--channels", type=int, nargs=2, metavar=("通道一ID", "通道二ID"), help="两个平台通道 ID，默认选择前两个在线通道")
    parser.add_argument("--public-url", help="默认读取服务器 v2.env，支持 HTTPS 443 或 8443")
    parser.add_argument("--ca-file", default="/etc/nginx/ssl/video-platform.crt", help="由 sudo 读取并复制的服务器公开证书，保留证书链和主机名验证")
    parser.add_argument("--local-ca-file", help="start 上传的本机公开证书，优先于服务器 --ca-file")
    parser.add_argument("--run-id", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if not math.isfinite(args.hours) or not 0 < args.hours <= 168:
        parser.error("值守时长必须大于 0 且不超过 168 小时")
    if args.channels and (min(args.channels) <= 0 or len(set(args.channels)) != 2):
        parser.error("必须指定两个不同的正整数平台通道 ID")
    if args.public_url:
        public_origin(args.public_url)
    if not args.ca_file.startswith("/"):
        parser.error("--ca-file 必须是服务器上的绝对路径")
    if args.local_ca_file and args.mode != "start":
        parser.error("--local-ca-file 仅适用于本地 start 命令")
    if args.local_ca_file and not Path(args.local_ca_file).is_file():
        parser.error("--local-ca-file 指定的本机公开证书不存在")
    if args.mode == "_run":
        valid_run_id(args.run_id)
        return run_soak(args)
    if args.mode == "_control":
        if not args.action:
            parser.error("远程控制缺少动作")
        print(json.dumps(remote_control(args), ensure_ascii=False))
    else:
        if args.action:
            parser.error("本地命令只接受一个动作")
        args.action = args.mode
        local_control(args)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        # 不输出异常正文或堆栈，防止 HTTP 错误携带完整媒体地址或凭据。
        print("连续值守操作失败，错误类别：" + error_code(error), file=sys.stderr)
        raise SystemExit(1)
