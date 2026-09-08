"""真实平台容量验收：默认 800 个普通账号、100 个并发登录、100 路实时和 20 路回放，默认仅检查参数。"""

import argparse
import base64
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone
import hashlib
import http.client
import json
import math
import os
from pathlib import Path
import re
import secrets
import shlex
import signal
import socket
import ssl
import sys
import threading
import time
from urllib.parse import parse_qs, quote, urlencode, urlsplit
import uuid


ROOT = Path(__file__).resolve().parent.parent
USERS = 100
PLAYBACKS = 20
QUERY_LATENCY_TARGET_MS = 500
UPSTREAM_CLEANUP_TIMEOUT = 60


def timestamp():
    return datetime.now(timezone.utc).isoformat()


class CapacityError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise CapacityError(message)


def integer(low, high):
    def parse(value):
        try:
            number = int(value)
        except ValueError:
            raise argparse.ArgumentTypeError("参数必须为整数。") from None
        if not low <= number <= high:
            raise argparse.ArgumentTypeError(f"参数必须在 {low} 至 {high} 之间。")
        return number
    return parse


def root_url(value):
    try:
        uri = urlsplit(value)
        valid = (uri.scheme == "https" and uri.hostname and uri.path in ("", "/") and not uri.query
                 and not uri.fragment and not uri.username and not uri.password and uri.port != 0)
    except ValueError:
        valid = False
    if not valid:
        raise argparse.ArgumentTypeError("公共地址必须是无凭据、无查询参数的 HTTPS 根地址，支持 443 或候选端口。")
    return value.rstrip("/")


def local_url(value):
    try:
        uri = urlsplit(value)
        valid = (uri.scheme == "http" and uri.hostname in ("127.0.0.1", "::1", "localhost")
                 and uri.path in ("", "/") and not uri.query and not uri.fragment
                 and not uri.username and not uri.password and uri.port != 0)
    except ValueError:
        valid = False
    if not valid:
        raise argparse.ArgumentTypeError("内部只读诊断地址必须为服务器本机 HTTP 根地址。")
    return value.rstrip("/")


def capacity_prefix(value):
    if not re.fullmatch(r"capacity_[0-9a-f]{20}", value):
        raise argparse.ArgumentTypeError("复用前缀必须为 capacity_ 加 20 位小写十六进制字符。")
    return value


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--execute", action="store_true", help="显式允许实际创建临时账号并运行真实容量测试")
    result.add_argument("--account-count", type=integer(100, 10000), default=800, help="通过真实 API 建立的普通账号总数，默认 800，至少 100")
    result.add_argument("--reuse-prefix", type=capacity_prefix, help="复用指定容量测试前缀下已禁用且无角色的全部账号，数量必须精确匹配 account-count")
    result.add_argument("--prepare-concurrency", type=integer(1, 16), default=8, help="创建账号的有界并发数，默认 8；登录并发固定 100")
    mode = result.add_mutually_exclusive_group()
    mode.add_argument("--live-only", action="store_true", help="只测 100 路实时，创建指定总数账号，其中固定 100 个并发登录")
    mode.add_argument("--playback-only", action="store_true", help="只测 20 路回放，创建指定总数账号，其中固定 100 个并发登录")
    result.add_argument("--duration", type=integer(30, 3600), default=30, help="全连接就绪后的持续测量秒数，至少 30 秒")
    result.add_argument("--public-url", type=root_url, default="https://10.37.200.74:8443")
    result.add_argument("--live-channel-id", type=integer(1, 9223372036854775807), help="实时全局通道 ID；默认选择录像设备的通道 8")
    result.add_argument("--playback-channel-id", type=integer(1, 9223372036854775807), help="回放全局通道 ID；未指定则选择设备通道 8")
    result.add_argument("--device-id", type=integer(1, 9223372036854775807), help="多个设备具有通道 8 时必须指定设备")
    result.add_argument("--host", default="10.37.200.74")
    result.add_argument("--port", type=integer(1, 65535), default=22)
    result.add_argument("--user", default="liteware")
    result.add_argument("--config", default="/home/liteware/.config/video-platform/v2-production.json")
    trust = result.add_mutually_exclusive_group()
    trust.add_argument("--ca-file", help="远程受信任 CA 文件；默认远程系统信任库，始终校验证书和主机名")
    trust.add_argument("--local-ca-file", type=Path, help="本机公开 CA 证书，临时 SFTP 上传用于远程 TLS 验证后清理")
    result.add_argument("--known-hosts", type=Path, help="额外本机 SSH known_hosts 文件")
    result.add_argument("--host-key-sha256", help="经独立核对的 SSH 公钥 SHA256 指纹")
    result.add_argument("--zlm-url", type=local_url, default="http://127.0.0.1:18082")
    result.add_argument("--adapter-url", type=local_url, default="http://127.0.0.1:5092")
    result.add_argument("--request-timeout", type=integer(5, 180), default=60)
    result.add_argument("--stream-timeout", type=integer(5, 30), default=10, help="连续媒体读取的无数据超时")
    result.add_argument("--startup-timeout", type=integer(30, 600), default=120, help="全连接就绪的等待上限，不计入 duration")
    result.add_argument("--query-interval", type=integer(1, 60), default=2, help="每个用户查询间隔秒数")
    result.add_argument("--remote-timeout", type=integer(300, 14400), default=3600)
    result.add_argument("--report", type=Path, help="本地 JSON 报告路径，同目录同时写入中文 Markdown 报告")
    return result


class Evidence:
    def __init__(self, prefix):
        self.lock = threading.RLock()
        self.secrets = set()
        self.samples = []
        self.report = {"runPrefix": prefix, "status": "running", "startedAt": timestamp(), "stage": "init",
                       "errors": [], "media": [], "users": [], "zlm": [], "cleanup": []}

    def remember(self, value):
        if isinstance(value, str) and value:
            with self.lock:
                self.secrets.update((value, quote(value, safe="")))

    def redact(self, value):
        text = str(value)
        with self.lock:
            for secret in sorted(self.secrets, key=len, reverse=True):
                text = text.replace(secret, "[已隐藏]")
        text = re.sub(r"(?i)(bearer\s+)[^\s\"'<>]+", r"\1[已隐藏]", text)
        text = re.sub(r"(?i)([\"']?(?:accessToken|password|secret|token|authorization|key)[\"']?\s*[:=]\s*)[\"']?[^\s,;&\"'<>]+[\"']?", r"\1[已隐藏]", text)
        text = re.sub(r"(?i)(https?|rtsps?)://[^\s/]+@", r"\1://[已隐藏]@", text)
        text = re.sub(r"(https?://[^\s\"'<>?]+)\?[^\s\"'<>]*", r"\1?[已隐藏]", text)
        return text

    def error(self, operation, error, status=0, body=None, user=None):
        raw = body if body is not None else str(error)
        code = None
        try:
            parsed = json.loads(raw)
            code = parsed.get("code") if isinstance(parsed, dict) else None
        except (ValueError, TypeError):
            pass
        text = self.redact(raw)
        marker = (str(code) + " " + text).lower()
        category = ("TLS" if isinstance(error, ssl.SSLError) else
                    "超时" if isinstance(error, (TimeoutError, socket.timeout)) else
                    "SDK限制或错误" if "sdk" in marker or "net_dvr" in marker else
                    "设备拒绝或媒体不可用" if any(word in marker for word in ("device", "设备", "拒绝", "media_not_ready", "playback_failed")) else
                    "平台限流或配额" if status == 429 else
                    "认证或权限" if status in (401, 403) else
                    "HTTP错误" if status else "验收断言或运行错误")
        record = {"at": timestamp(), "operation": operation, "category": category, "httpStatus": status,
                  "code": self.redact(code) if code else None, "type": type(error).__name__, "detail": text, "user": user}
        with self.lock:
            self.report["errors"].append(record)
        return record

    def sample(self, operation, started, status, success):
        phase = ("measurement" if operation.startswith("query.") else "cleanup" if operation.startswith("cleanup.")
                 else "prepare" if operation.startswith(("setup.", "login.")) else "media")
        with self.lock:
            self.samples.append({"phase": phase, "operation": operation, "milliseconds": round((time.monotonic() - started) * 1000, 3),
                                 "httpStatus": status, "success": success})

    def query_latency(self):
        with self.lock:
            queries = [sample for sample in self.samples if sample["phase"] == "measurement" and sample["operation"].startswith("query.")]
            values = sorted(sample["milliseconds"] for sample in queries)
            observed = values[math.ceil(len(values) * .95) - 1] if values else None
            return {"querySampleCount": len(queries), "queryFailureCount": sum(not sample["success"] for sample in queries),
                    "queryLatencyTargetMs": QUERY_LATENCY_TARGET_MS, "observedQueryP95Ms": observed,
                    "queryLatencyTargetMet": observed is not None and observed < QUERY_LATENCY_TARGET_MS}

    def snapshot(self):
        with self.lock:
            result = json.loads(json.dumps(self.report))
            groups = {}
            for sample in self.samples:
                groups.setdefault((sample["phase"], sample["operation"]), []).append(sample)
            result["metrics"] = []
            for (phase, operation), samples in sorted(groups.items()):
                values = sorted(item["milliseconds"] for item in samples)
                successful = sorted(item["milliseconds"] for item in samples if item["success"])
                result["metrics"].append({"phase": phase, "operation": operation, "requests": len(samples),
                    "failures": len(samples) - len(successful), "p95Ms": values[math.ceil(len(values) * .95) - 1],
                    "successP95Ms": successful[math.ceil(len(successful) * .95) - 1] if successful else None,
                    "maxMs": values[-1]})
            result["requestSamples"] = list(self.samples)
            result.update(self.query_latency())
            return result


class RecordedError(CapacityError):
    """错误已经进入证据，避免并发调度层重复记录。"""


class Api:
    def __init__(self, base, evidence, timeout, ca_file=None, internal=False):
        self.uri = urlsplit(base)
        self.evidence = evidence
        self.timeout = timeout
        self.context = ssl.create_default_context(cafile=ca_file) if not internal else None

    def connection(self, timeout=None):
        if self.context:
            return http.client.HTTPSConnection(self.uri.hostname, self.uri.port or 443, timeout=timeout or self.timeout, context=self.context)
        return http.client.HTTPConnection(self.uri.hostname, self.uri.port or 80, timeout=timeout or self.timeout)

    def call(self, method, path, body=None, token=None, operation=None, user=None, headers=None):
        operation = operation or method + " " + path.split("?")[0]
        started = time.monotonic()
        status = 0
        success = False
        connection = self.connection()
        try:
            request_headers = {"Content-Type": "application/json", **(headers or {})}
            if token:
                request_headers["Authorization"] = "Bearer " + token
            data = json.dumps(body).encode("utf-8") if body is not None else (b"" if method == "POST" else None)
            connection.request(method, path, body=data, headers=request_headers)
            response = connection.getresponse()
            status = response.status
            raw = response.read(4 * 1024 * 1024 + 1)
            require(len(raw) <= 4 * 1024 * 1024, "JSON 响应超过 4 MB 限制。")
            if not 200 <= status < 300:
                self.evidence.error(operation, CapacityError("HTTP 请求失败"), status, raw.decode("utf-8", "replace"), user)
                raise RecordedError("HTTP 错误已记录。")
            result = json.loads(raw) if raw else None
            success = True
            return result
        except RecordedError:
            raise
        except Exception as error:
            self.evidence.error(operation, error, status, user=user)
            raise RecordedError("请求错误已记录。") from None
        finally:
            connection.close()
            self.evidence.sample(operation, started, status, success)


def paginate(api, path, token, operation):
    rows = []
    for page in range(1, 10001):
        result = api.call("GET", path + ("&" if "?" in path else "?") + f"page={page}&pageSize=200", token=token, operation=operation)
        rows.extend(result["items"])
        if len(rows) >= int(result["total"]):
            return rows
        require(bool(result["items"]), "分页提前返回空页。")
    raise CapacityError("分页超过限制。")


def reusable_accounts(rows, prefix, count):
    capacity_prefix(prefix)
    expected = {f"{prefix}_{index:05}" for index in range(count)}
    own = [row for row in rows if str(row.get("username", "")).startswith(prefix + "_")]
    require(len(own) == count and {row.get("username") for row in own} == expected,
            "复用账号数量或精确用户名集合不匹配，未修改任何账号。")
    require(all(re.fullmatch(r"capacity_[0-9a-f]{20}_[0-9]{5}", row["username"])
                and row.get("status") == "disabled" and row.get("roleIds") == [] for row in own),
            "复用账号必须全部符合测试命名规则、已禁用且无角色，未修改任何账号。")
    require(len({row["id"] for row in own}) == count, "复用账号 ID 不唯一，未修改任何账号。")
    return {row["username"]: row for row in own}


class Zlm:
    def __init__(self, options, evidence, secret):
        self.api = Api(options["zlm_url"], evidence, options["request_timeout"], internal=True)
        self.secret = secret

    def call(self, action, *, operation_prefix="zlm", **values):
        require(action in ("getMediaList", "getMediaPlayerList"), "只允许 ZLM 只读查询。")
        path = "/index/api/" + action + "?" + urlencode({"secret": self.secret, **values})
        result = self.api.call("GET", path, operation=operation_prefix + "." + action)
        if result.get("code") != 0:
            self.api.evidence.error(operation_prefix + "." + action, CapacityError("ZLM 查询失败"), body=json.dumps(result, ensure_ascii=False))
            raise RecordedError("ZLM 原始错误已记录。")
        data = result.get("data")
        # ZLM 在没有媒体或播放连接时可能返回 null，不能当作协议错误。
        require(data is None or isinstance(data, list), "ZLM 只读查询的 data 必须为列表或 null。")
        return data if data is not None else []

    def snapshot(self, app, stream, *, rows=None, operation_prefix="zlm"):
        if rows is None:
            rows = self.call("getMediaList", app=app, stream=stream, operation_prefix=operation_prefix)
        rows = [row for row in rows if row.get("app") == app and row.get("stream") == stream]
        if not rows:
            return {"app": app, "stream": stream, "sources": [], "readerCount": 0, "playerIds": [],
                    "playerIdentifierFields": [], "originEvidence": [], "rtspPullVerified": False}
        ts = [row for row in rows if row.get("schema") == "ts"]
        require(len(ts) == 1, "ZLM 必须提供唯一 TS 媒体源，不能用其他协议数量替代。")
        vhost = ts[0].get("vhost", "__defaultVhost__")
        players = self.call("getMediaPlayerList", schema="ts", vhost=vhost, app=app, stream=stream, operation_prefix=operation_prefix)
        player_ids = []
        player_fields = set()
        for row in players:
            require(isinstance(row, dict), "ZLM 播放连接项必须为对象。")
            # 不同版本使用 identifier 或 id；只取连接标识，不保存 params、URL 或令牌。
            field = next((key for key in ("identifier", "id")
                          if isinstance(row.get(key), (str, int)) and not isinstance(row.get(key), bool)
                          and str(row[key]).strip()), None)
            require(field is not None, "ZLM 未提供非空 identifier 或 id 播放连接标识。")
            player_fields.add(field)
            player_ids.append(str(row[field]))
        require(len(set(player_ids)) == len(player_ids), "ZLM 播放连接标识重复，不能据此证明独立连接数。")
        sources = {(row.get("vhost"), row["app"], row["stream"]) for row in rows}
        origin_types = sorted({str(row.get("originType")) for row in rows})
        origin_evidence = []
        for row in rows:
            # 来源枚举编号不能跨版本推断协议；pull 名称与 RTSP 地址必须同时成立。
            raw_url = row.get("originUrl")
            scheme = None
            valid_rtsp_url = False
            if isinstance(raw_url, str) and raw_url:
                try:
                    uri = urlsplit(raw_url)
                    scheme = uri.scheme.lower()
                    valid_rtsp_url = scheme in ("rtsp", "rtsps") and bool(uri.hostname) and uri.port != 0
                except ValueError:
                    pass
            origin_name = row.get("originTypeStr")
            origin_evidence.append({"schema": row.get("schema"), "originType": str(row.get("originType")),
                "originTypeStr": self.api.evidence.redact(origin_name) if isinstance(origin_name, str) else None,
                "originScheme": scheme,
                # 设备凭据、地址、路径与查询参数均不写入报告，只保存完整来源的不可逆摘要。
                "originHash": hashlib.sha256(raw_url.encode()).hexdigest() if isinstance(raw_url, str) and raw_url else None,
                "rtspPullVerified": origin_name == "pull" and valid_rtsp_url})
        origins = sorted({item["originHash"] for item in origin_evidence if item["originHash"]})
        rtsp_pull_verified = (len(sources) == 1 and len(origin_types) == 1 and len(origins) == 1
                              and all(item["rtspPullVerified"] for item in origin_evidence))
        return {"app": app, "stream": stream, "sources": [list(value) for value in sources],
                "originTypes": origin_types, "originHashes": origins, "readerCount": int(ts[0].get("readerCount", -1)),
                "originEvidence": origin_evidence, "rtspPullVerified": rtsp_pull_verified,
                "totalReaderCount": ts[0].get("totalReaderCount"), "playerIds": sorted(player_ids),
                "playerIdentifierFields": sorted(player_fields)}


class Scenario:
    def __init__(self, options, directory):
        self.options = options
        self.directory = directory
        self.evidence = Evidence(options["prefix"])
        self.evidence.report["targets"] = {"accounts": options["account_count"], "concurrentLogins": USERS, "live": 0 if options["playback_only"] else USERS,
                                           "playback": 0 if options["live_only"] else PLAYBACKS, "durationSeconds": options["duration"]}
        self.api = Api(options["public_url"], self.evidence, options["request_timeout"], options["ca_file"])
        self.admin = None
        self.role = None
        self.accounts = []
        self.clients = []
        self.grants = []
        self.stop = threading.Event()
        self.stream_stop = threading.Event()
        self.renew_stop = threading.Event()
        self.query_stop = threading.Event()
        self.stream_threads = []
        self.query_threads = []
        self.renew_thread = None
        self.baseline = None
        self.zlm = None
        self.adapter = Api(options["adapter_url"], self.evidence, options["request_timeout"], internal=True)
        self.adapter_key = None
        self.expected_names = {f"{options['prefix']}_{index:05}" for index in range(options["account_count"])}
        self.reuse_validated = not options.get("reuse_prefix")

    def checkpoint(self, stage):
        with self.evidence.lock:
            self.evidence.report["stage"] = stage
            self.evidence.report["media"] = [{"id": grant["id"], "kind": grant["kind"], "userId": grant["client"]["id"],
                "stream": grant.get("stream"), "bytes": grant["bytes"], "tsValid": grant["tsValid"], "ended": grant["ended"]} for grant in self.grants]
        write_reports(self.directory / "report.json", self.evidence.snapshot())

    def guard(self, operation, action, user=None):
        try:
            return action()
        except RecordedError:
            return None
        except Exception as error:
            self.evidence.error(operation, error, user=user)
            return None

    def parallel(self, items, action, operation, workers=100):
        with ThreadPoolExecutor(max_workers=workers) as pool:
            futures = [pool.submit(self.guard, operation, lambda item=item: action(item), item.get("username") if isinstance(item, dict) else None) for item in items]
            return [future.result() for future in as_completed(futures)]

    def login(self, username, password, operation):
        value = self.api.call("POST", "/api/v2/auth/login", {"username": username, "password": password,
                              "clientType": "desktop", "clientVersion": self.options["prefix"]}, operation=operation, user=username)
        token = value.get("accessToken")
        require(bool(token), "登录响应没有访问令牌。")
        self.evidence.remember(token)
        return token, value["user"]

    def setup(self):
        config = json.loads(Path(self.options["config"]).read_text(encoding="utf-8"))
        for value in config.values():
            self.evidence.remember(value)
        self.zlm = Zlm(self.options, self.evidence, config["zlmSecret"])
        self.adapter_key = config["adapterKey"]
        self.admin, _ = self.login("admin", config["adminPassword"], "setup.admin-login")
        config.clear()
        health = self.adapter.call("GET", "/health", operation="setup.adapter-health", headers={"X-Adapter-Key": self.adapter_key})
        require(health.get("detail", {}).get("simulated") is False, "适配器必须明确标记 simulated=false，拒绝把模拟容量当作实机结果。")
        self.evidence.report["adapterSimulated"] = False
        channels = paginate(self.api, "/api/v2/channels", self.admin, "setup.channels")

        def select(explicit, label):
            candidates = [row for row in channels if (int(row["id"]) == explicit if explicit else int(row["deviceChannel"]) == 8)
                          and (not self.options["device_id"] or int(row["deviceId"]) == self.options["device_id"])]
            require(len(candidates) == 1, label + "通道不唯一或不存在，请明确指定全局 ID 或 device-id。")
            require(candidates[0].get("status") == "online", label + "通道不在线。")
            return candidates[0]

        self.playback_channel = select(self.options["playback_channel_id"], "回放") if not self.options["live_only"] else None
        self.live_channel = select(self.options["live_channel_id"], "实时") if not self.options["playback_only"] else None
        scope = sorted({int(row["id"]) for row in (self.live_channel, self.playback_channel) if row})
        self.scope = scope
        self.evidence.report["channels"] = {name: {key: row[key] for key in ("id", "deviceId", "deviceChannel", "name")}
            for name, row in (("live", self.live_channel), ("playback", self.playback_channel)) if row}
        if self.live_channel:
            self.live_stream = f"vp2_d{self.live_channel['deviceId']}_c{self.live_channel['deviceChannel']}_s2_native"
            self.baseline = self.zlm.snapshot("live", self.live_stream)
            self.evidence.report["liveBaseline"] = self.baseline
        end = datetime.now(timezone.utc) - timedelta(minutes=10)
        self.recording_range = {"start": (end - timedelta(seconds=120)).isoformat(), "end": end.isoformat()}
        if self.playback_channel:
            recordings = self.api.call("POST", "/api/v2/recordings/search", {"channelId": int(self.playback_channel["id"]), **self.recording_range},
                                       self.admin, operation="setup.recording-search")
            require(isinstance(recordings, list) and bool(recordings), "所选通道最近十分钟前的 120 秒范围没有录像，不能执行回放容量验收。")
            self.evidence.report["recordingRange"] = {**self.recording_range, "matches": len(recordings)}
        reuse = {}
        if self.options.get("reuse_prefix"):
            # 全量验证通过前不修改账号，清理也不得接管验证失败的已有账号或角色。
            existing = paginate(self.api, "/api/v2/users?search=" + quote(self.options["prefix"]), self.admin, "setup.reuse-preflight")
            reuse = reusable_accounts(existing, self.options["prefix"], self.options["account_count"])
            roles = self.api.call("GET", "/api/v2/roles", token=self.admin, operation="setup.reuse-role-preflight")
            require(not any(row.get("code") == self.options["prefix"] for row in roles), "复用前缀仍有关联角色，未修改任何账号或角色。")
            self.reuse_validated = True
        self.evidence.report["accountPreparationMode"] = "reuse" if reuse else "create"
        prepare_operation = "setup.user-reuse" if reuse else "setup.user-create"
        self.checkpoint("create-role")
        permissions = ["channel.read"] + ([] if self.options["playback_only"] else ["live.view"]) + ([] if self.options["live_only"] else ["playback.view"])
        self.role = self.api.call("POST", "/api/v2/roles", {"name": self.options["prefix"], "code": self.options["prefix"],
                                  "status": "active", "permissionCodes": permissions}, self.admin, operation="setup.role")
        self.evidence.report["roleId"] = self.role["id"]
        self.api.call("PUT", f"/api/v2/scopes/role/{self.role['id']}", {"allChannels": False,
                      "scopes": [{"type": "channel", "id": identifier} for identifier in scope]}, self.admin, operation="setup.exact-scope")
        actual = self.api.call("GET", f"/api/v2/scopes/role/{self.role['id']}", token=self.admin, operation="setup.verify-scope")
        require(actual.get("allChannels") is False and {int(item["id"]) for item in actual["scopes"] if item["type"] == "channel"} == set(scope)
                and len(actual["scopes"]) == len(scope), "临时角色的数据范围不是精确通道集合。")
        self.checkpoint("create-users")

        def create_user(name):
            require(not self.stop.is_set(), "作业已请求停止。")
            password = "Capacity-A9!" + secrets.token_urlsafe(24)
            self.evidence.remember(password)
            body = {"username": name, "password": password, "displayName": name,
                    "phone": "", "status": "active", "roleIds": [self.role["id"]]}
            row = self.api.call("PUT" if reuse else "POST", f"/api/v2/users/{reuse[name]['id']}" if reuse else "/api/v2/users",
                                body, self.admin, operation=prepare_operation)
            require(not reuse or row["id"] == reuse[name]["id"], "复用响应没有返回原账号 ID。")
            with self.evidence.lock:
                self.accounts.append({"id": row["id"], "username": name, "password": password, "token": None})
                self.evidence.report["users"].append({"id": row["id"], "username": name, "roleIds": row.get("roleIds"), "channelIds": scope})
            require(row.get("roleIds") == [self.role["id"]] and row.get("status") == "active", "新账号没有绑定本次精确范围普通角色。")

        # 账号准备与登录目标独立，全部通过真实 API 创建或重新启用，但仅选择固定 100 个登录。
        with ThreadPoolExecutor(max_workers=self.options["prepare_concurrency"]) as pool:
            futures = [pool.submit(self.guard, prepare_operation, lambda name=name: create_user(name), name) for name in sorted(self.expected_names)]
            completed = 0
            for future in as_completed(futures):
                future.result()
                completed += 1
                if completed % 10 == 0 or completed == self.options["account_count"]:
                    self.checkpoint("create-users:" + str(completed))
        require(len(self.accounts) == self.options["account_count"], "真实 API 准备的账号数未达到目标。")
        actual_users = paginate(self.api, "/api/v2/users?search=" + quote(self.options["prefix"]), self.admin, "setup.verify-account-count")
        actual_users = [row for row in actual_users if row["username"] in self.expected_names]
        require(len(actual_users) == self.options["account_count"] and all(row.get("roleIds") == [self.role["id"]] and row.get("status") == "active" for row in actual_users),
                "真实 API 复查的账号数量、启用状态或角色范围不符合目标。")
        self.evidence.report["preparedAccounts"] = len(actual_users)
        self.evidence.report["exactChannelScope"] = {"roleId": self.role["id"], "allChannels": False, "channelIds": scope}
        self.clients = sorted(self.accounts, key=lambda row: row["username"])[:USERS]
        selected = {row["username"] for row in self.clients}
        for account in self.accounts:
            if account["username"] not in selected:
                account.pop("password", None)

        def login_user(client):
            token, user = self.login(client["username"], client.pop("password"), "login.concurrent")
            client["token"] = token
            require(set(user.get("permissions", [])) == set(permissions), "临时账号权限不符合普通用户角色。")
            visible = paginate(self.api, "/api/v2/channels", token, "setup.user-scope")
            require({int(row["id"]) for row in visible} == set(scope), "普通账号可见通道范围不符合精确授权。")
            client["verified"] = True

        self.checkpoint("concurrent-100-logins")
        self.parallel(self.clients, login_user, "login.verify")
        require(sum(bool(client.get("verified")) for client in self.clients) == USERS, "100 个普通账号未全部登录并通过授权范围验证。")
        self.evidence.report["loggedInUsers"] = USERS

    def start_grant(self, client, kind):
        require(not self.stop.is_set(), "停止请求后不再创建新媒体会话。")
        channel = self.live_channel if kind == "live" else self.playback_channel
        body = {"channelId": int(channel["id"]), "profile": "native"}
        body.update({"streamType": 2} if kind == "live" else self.recording_range)
        state = self.api.call("POST", f"/api/v2/{kind}-sessions", body, client["token"], operation=kind + ".create", user=client["username"])
        grant = {"id": str(uuid.UUID(state["id"])), "kind": kind, "client": client, "state": state,
                 "bytes": 0, "lastData": None, "firstData": None, "tsValid": False, "connection": None, "ended": False}
        with self.evidence.lock:
            self.grants.append(grant)
        if kind == "playback":
            deadline = time.monotonic() + self.options["startup_timeout"]
            while state.get("state") not in ("playing", "failed", "stopped", "completed") and time.monotonic() < deadline and not self.stop.is_set():
                self.stop.wait(.5)
                state = self.api.call("GET", f"/api/v2/playback-sessions/{grant['id']}", token=client["token"], operation="playback.ready", user=client["username"])
            if state.get("state") != "playing":
                detail = self.adapter.call("GET", f"/internal/devices/{channel['deviceId']}/playback/{grant['id']}",
                           operation="playback.device-detail", headers={"X-Adapter-Key": self.adapter_key})
                self.evidence.error("playback.state", CapacityError("回放未就绪"), body=json.dumps(detail, ensure_ascii=False), user=client["username"])
                raise RecordedError("设备回放状态已记录。")
        require(kind != "live" or int(state.get("streamType", 0)) == 2, "设备回退主码流，不能计为 native 子码流容量通过。")
        require(not state.get("transcoded"), "native 会话不应转码。")
        media = urlsplit(state.get("httpTsUrl", ""))
        for values in parse_qs(media.query).values():
            for value in values:
                self.evidence.remember(value)
        require(media.scheme == "https" and media.hostname == self.api.uri.hostname and not media.username and not media.password,
                "媒体必须提供同主机 HTTPS TS 地址。")
        match = re.fullmatch(r"/media/(live|playback)/(vp2_[A-Za-z0-9_]+)\.live\.ts", media.path)
        require(match is not None and match[1] == kind and bool(media.query), "HTTPS TS 地址不符合会话契约。")
        grant["stream"] = match[2]
        require(kind != "live" or grant["stream"] == self.live_stream, "实时会话没有共享指定 native 子码流。")
        grant["path"] = media.path + "?" + media.query
        grant["state"] = state
        thread = threading.Thread(target=lambda: self.guard(kind + ".https-stream", lambda: self.consume(grant), client["username"]), daemon=True)
        with self.evidence.lock:
            self.stream_threads.append(thread)
        thread.start()

    def consume(self, grant):
        connection = self.api.connection(self.options["stream_timeout"])
        grant["connection"] = connection
        started = time.monotonic()
        sample = bytearray()
        try:
            # 只替换为用户指定的候选／正式端口，媒体查询令牌仅留在内存。
            connection.request("GET", grant["path"], headers={"Accept-Encoding": "identity"})
            response = connection.getresponse()
            if response.status != 200:
                self.evidence.error(grant["kind"] + ".https-stream", CapacityError("HTTPS 收流失败"), response.status,
                                    response.read(65536).decode("utf-8", "replace"), grant["client"]["username"])
                raise RecordedError("媒体 HTTP 错误已记录。")
            while not self.stream_stop.is_set():
                block = response.read1(65536)
                if not block:
                    raise CapacityError("媒体在测量结束前 EOF，不能计为持续收流通过。")
                now = time.monotonic()
                with self.evidence.lock:
                    grant["bytes"] += len(block)
                    grant["lastData"] = now
                    if grant["firstData"] is None:
                        grant["firstData"] = now
                        self.evidence.sample(grant["kind"] + ".first-byte", started, 200, True)
                    if len(sample) < 65536:
                        sample.extend(block[:65536 - len(sample)])
                    if len(sample) >= 188 * 5:
                        grant["tsValid"] = any(all(sample[offset + packet * 188] == 0x47 for packet in range(5)) for offset in range(min(188, len(sample) - 188 * 4)))
        except Exception:
            if not self.stream_stop.is_set():
                raise
        finally:
            grant["ended"] = True
            connection.close()

    def renew(self):
        while not self.renew_stop.wait(45):
            with self.evidence.lock:
                grants = list(self.grants)
            self.parallel(grants, lambda grant: self.api.call("POST", f"/api/v2/{grant['kind']}-sessions/{grant['id']}/renew",
                          token=grant["client"]["token"], operation=grant["kind"] + ".renew"), "media.renew", 16)

    def query(self, client):
        count = 0
        while not self.query_stop.is_set():
            path, label = ("/api/v2/channels?page=1&pageSize=50", "query.channels") if count % 2 == 0 else ("/api/v2/auth/me", "query.me")
            self.guard(label, lambda: self.api.call("GET", path, token=client["token"], operation=label, user=client["username"]))
            count += 1
            self.query_stop.wait(self.options["query_interval"])

    def inspect(self):
        if self.live_channel:
            snapshot = self.zlm.snapshot("live", self.live_stream)
            self.evidence.report["zlm"].append({"at": timestamp(), **snapshot})
            require(snapshot.get("rtspPullVerified") is True,
                    "ZLM 未证明唯一 RTSP 拉流上游：各协议条目须具有一致来源摘要、originTypeStr=pull 及有效 rtsp/rtsps 来源地址，详见 originEvidence。")
            added = set(snapshot["playerIds"]) - set(self.baseline["playerIds"])
            require(len(added) == USERS and snapshot["readerCount"] == self.baseline["readerCount"] + USERS,
                    "ZLM 未确认新增 100 个 TS 播放连接，外部连接变化也会导致验收失败。")
        playback = [grant for grant in self.grants if grant["kind"] == "playback"]
        if self.playback_channel:
            require(len({grant.get("stream") for grant in playback}) == PLAYBACKS, "20 个回放没有独立 stream。")
            require(len({grant["client"]["id"] for grant in playback}) == PLAYBACKS, "20 个回放没有使用不同用户。")
            for grant in playback:
                snapshot = self.zlm.snapshot("playback", grant["stream"])
                self.evidence.report["zlm"].append({"at": timestamp(), **snapshot})
                require(len(snapshot["sources"]) == 1 and snapshot["readerCount"] == 1 and len(snapshot["playerIds"]) == 1,
                        "ZLM 未确认独立回放 TS 连接。")

    def run(self):
        prepared = time.monotonic()
        self.setup()
        self.evidence.report["preparationSeconds"] = round(time.monotonic() - prepared, 3)
        media_started = time.monotonic()
        self.checkpoint("create-real-media")
        self.renew_thread = threading.Thread(target=self.renew, daemon=True)
        self.renew_thread.start()
        jobs = [(client, "live") for client in self.clients] if self.live_channel else []
        jobs += [(client, "playback") for client in self.clients[:PLAYBACKS]] if self.playback_channel else []
        self.parallel(jobs, lambda job: self.start_grant(*job), "media.start")
        deadline = time.monotonic() + self.options["startup_timeout"]
        ready = 0
        while time.monotonic() < deadline and not self.stop.is_set():
            with self.evidence.lock:
                ready = sum(grant["tsValid"] and not grant["ended"] for grant in self.grants)
                failed = any(grant["ended"] for grant in self.grants)
            if ready == len(jobs) or failed:
                break
            self.stop.wait(.2)
        require(len(self.grants) == len(jobs) and ready == len(jobs), "目标媒体会话没有全部建立并取得真实 TS 数据。")
        self.inspect()
        self.evidence.report["mediaStartupSeconds"] = round(time.monotonic() - media_started, 3)
        initial = {grant["id"]: grant["bytes"] for grant in self.grants}
        started = time.monotonic()
        for client in self.clients:
            thread = threading.Thread(target=self.query, args=(client,), daemon=True)
            self.query_threads.append(thread)
            thread.start()
        self.checkpoint("measure")
        while time.monotonic() - started < self.options["duration"]:
            require(not self.stop.wait(max(0, min(5, self.options["duration"] - (time.monotonic() - started)))), "作业收到停止请求。")
            with self.evidence.lock:
                require(all(not grant["ended"] and time.monotonic() - grant["lastData"] <= self.options["stream_timeout"] for grant in self.grants),
                        "持续测量期间出现媒体断开或停流。")
            self.inspect()
            self.checkpoint("measure")
        self.evidence.report["measuredSeconds"] = round(time.monotonic() - started, 3)
        for grant in self.grants:
            require(grant["bytes"] > initial[grant["id"]], "媒体仅在启动时有数据，测量期间未继续收流。")
        self.query_stop.set()
        # 等待在途查询结束后再验收，不能漏掉最慢的一批请求或把空样本当作通过。
        query_deadline = time.monotonic() + self.options["request_timeout"] + 2
        for thread in self.query_threads:
            thread.join(max(0, query_deadline - time.monotonic()))
        require(all(not thread.is_alive() for thread in self.query_threads), "查询线程未按时退出，无法完成 P95 验收。")
        latency = self.evidence.query_latency()
        self.evidence.report.update(latency)
        require(latency["queryLatencyTargetMet"], "普通查询样本为空或 P95 未严格小于 500 毫秒。")
        self.evidence.report["status"] = "passed" if not self.evidence.report["errors"] else "failed"

    def verify_upstream_cleanup(self):
        result = {"status": "running", "timeoutSeconds": UPSTREAM_CLEANUP_TIMEOUT, "startedAt": timestamp(), "observations": []}
        self.evidence.report["upstreamCleanup"] = result
        self.evidence.report["upstreamReleaseVerified"] = False
        # 即使回放起流失败，已取得的会话 ID 仍能定位对应的原生流。
        playback_streams = {grant.get("stream") or
            f"vp2_d{self.playback_channel['deviceId']}_c{self.playback_channel['deviceChannel']}_playback_{uuid.UUID(grant['id']).hex}_native"
            for grant in self.grants if grant["kind"] == "playback"}
        result["expectedPlaybackStreams"] = sorted(playback_streams)
        result["liveBaseline"] = self.baseline
        if self.baseline is None and not playback_streams:
            result["status"] = "not-applicable"
            return
        require(self.zlm is not None, "缺少 ZLM 客户端，不能确认真实上游释放。")
        started = time.monotonic()
        deadline = started + UPSTREAM_CLEANUP_TIMEOUT
        original_timeout = self.zlm.api.timeout
        try:
            while time.monotonic() < deadline:
                # 每轮只读批量拉取媒体表，避免逐个回放串行等待使总清理时间无限增长。
                self.zlm.api.timeout = max(.01, min(5, deadline - time.monotonic()))
                rows = self.zlm.call("getMediaList", operation_prefix="cleanup.zlm")
                require(isinstance(rows, list), "ZLM 清理核对没有返回媒体列表。")
                remaining = sorted({row["stream"] for row in rows if row.get("app") == "playback" and row.get("stream") in playback_streams})
                current = None
                live_restored = self.baseline is None
                if self.baseline is not None:
                    live_rows = [row for row in rows if row.get("app") == "live" and row.get("stream") == self.live_stream]
                    current = {"sources": sorted({(row.get("vhost", "__defaultVhost__"), row["app"], row["stream"]) for row in live_rows}),
                               "tsSourceCount": sum(row.get("schema") == "ts" for row in live_rows)}
                    if not self.baseline["sources"]:
                        # 原先没有上游时，要求所有协议的源都消失，不能只看读者数归零。
                        live_restored = not live_rows
                    elif current["tsSourceCount"] == 1 and time.monotonic() < deadline:
                        self.zlm.api.timeout = max(.01, min(5, deadline - time.monotonic()))
                        current = self.zlm.snapshot("live", self.live_stream, rows=live_rows, operation_prefix="cleanup.zlm")
                        live_restored = (set(map(tuple, current["sources"])) == set(map(tuple, self.baseline["sources"]))
                            and current["readerCount"] == self.baseline["readerCount"]
                            and set(current["playerIds"]) == set(self.baseline["playerIds"])
                            and current.get("originTypes") == self.baseline.get("originTypes")
                            and current.get("originHashes") == self.baseline.get("originHashes")
                            and current.get("rtspPullVerified") == self.baseline.get("rtspPullVerified")
                            and {(item["schema"], item["originTypeStr"], item["originScheme"], item["originHash"])
                                 for item in current.get("originEvidence", [])}
                                == {(item["schema"], item["originTypeStr"], item["originScheme"], item["originHash"])
                                    for item in self.baseline.get("originEvidence", [])}
                            and current.get("totalReaderCount") == self.baseline.get("totalReaderCount"))
                result["observations"].append({"at": timestamp(), "live": current,
                    "liveBaselineRestored": live_restored, "remainingPlaybackStreams": remaining})
                if live_restored and not remaining and time.monotonic() <= deadline:
                    result["status"] = "passed"
                    self.evidence.report["upstreamReleaseVerified"] = True
                    return
                time.sleep(max(0, min(2, deadline - time.monotonic())))
            raise CapacityError("60 秒内 ZLM 实时源未恢复基线或独立回放流仍存在，HTTP DELETE 成功不能代替真实释放。")
        finally:
            self.zlm.api.timeout = original_timeout
            if result["status"] == "running":
                result["status"] = "failed"
            result["waitedSeconds"] = round(time.monotonic() - started, 3)
            result["finishedAt"] = timestamp()

    def cleanup(self):
        self.guard("cleanup.checkpoint", lambda: self.checkpoint("cleanup"))
        self.query_stop.set()
        self.renew_stop.set()
        self.stream_stop.set()
        for grant in self.grants:
            connection = grant.get("connection")
            if connection:
                try:
                    if connection.sock:
                        connection.sock.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass
                connection.close()
        for thread in self.stream_threads + self.query_threads + ([self.renew_thread] if self.renew_thread else []):
            thread.join(self.options["request_timeout"] + 2)
            if thread.is_alive():
                self.evidence.error("cleanup.thread", CapacityError("工作线程未按时停止。"))
        self.parallel(self.grants, lambda grant: self.api.call("DELETE", f"/api/v2/{grant['kind']}-sessions/{grant['id']}",
                      token=grant["client"]["token"], operation="cleanup.media-stop"), "cleanup.media-stop", 16)
        self.parallel([client for client in self.clients if client["token"]], lambda client: self.api.call("POST", "/api/v2/auth/logout",
                      token=client["token"], operation="cleanup.logout", user=client["username"]), "cleanup.logout", 16)
        if self.admin and self.reuse_validated:
            # 按精确随机用户名恢复已提交但响应丢失的账号，不按模糊前缀修改他人账号。
            found = self.guard("cleanup.discover-users", lambda: paginate(self.api, "/api/v2/users?search=" + quote(self.options["prefix"]), self.admin, "cleanup.find-users"))
            users = {account["id"]: account for account in self.accounts}
            for row in found or []:
                if row["username"] in self.expected_names:
                    users[row["id"]] = row

            def disable(row):
                self.api.call("PUT", f"/api/v2/users/{row['id']}", {"username": row["username"], "password": None,
                              "displayName": row["username"], "phone": "", "status": "disabled", "roleIds": []}, self.admin, operation="cleanup.disable-user")
                self.evidence.report["cleanup"].append({"userId": row["id"], "username": row["username"], "disabled": True, "roleIds": []})

            self.parallel(list(users.values()), disable, "cleanup.disable-user", 16)
            roles = self.guard("cleanup.discover-role", lambda: self.api.call("GET", "/api/v2/roles", token=self.admin, operation="cleanup.roles"))
            own_roles = [row for row in roles or [] if row.get("code") == self.options["prefix"]]
            for role in own_roles:
                self.guard("cleanup.delete-role", lambda role=role: self.api.call("DELETE", f"/api/v2/roles/{role['id']}", token=self.admin, operation="cleanup.delete-role"))
            remaining_users = self.guard("cleanup.verify-users", lambda: paginate(self.api, "/api/v2/users?search=" + quote(self.options["prefix"]), self.admin, "cleanup.verify-users"))
            if remaining_users is not None:
                self.guard("cleanup.verify-users", lambda: require(all(row.get("status") == "disabled" and row.get("roleIds") == []
                    for row in remaining_users if row["username"] in self.expected_names), "仍有本次账号未停用或仍关联角色。"))
            remaining_roles = self.guard("cleanup.verify-roles", lambda: self.api.call("GET", "/api/v2/roles", token=self.admin, operation="cleanup.verify-roles"))
            if remaining_roles is not None:
                self.guard("cleanup.verify-roles", lambda: require(not any(row.get("code") == self.options["prefix"] for row in remaining_roles), "随机临时角色仍存在。"))
            sessions = self.guard("cleanup.sessions", lambda: paginate(self.api, "/api/v2/sessions?search=" + quote(self.options["prefix"]), self.admin, "cleanup.sessions"))
            # 管理员会话也带本次 clientVersion，普通用户会话应全部撤销；管理员最后注销。
            if sessions is not None:
                self.guard("cleanup.verify-sessions", lambda: require(not any(row.get("username") in self.expected_names for row in sessions), "仍有临时普通用户会话未撤销。"))
        self.guard("cleanup.verify-upstreams", self.verify_upstream_cleanup)
        self.logout_admin()
        for account in self.accounts:
            account["token"] = None
            account.pop("password", None)
        self.evidence.report["media"] = [{"id": grant["id"], "kind": grant["kind"], "userId": grant["client"]["id"],
            "stream": grant.get("stream"), "bytes": grant["bytes"], "tsValid": grant["tsValid"], "ended": grant["ended"]} for grant in self.grants]
        self.evidence.report.update(self.evidence.query_latency())
        if (self.evidence.report["errors"] or not self.evidence.report["queryLatencyTargetMet"]
                or not self.evidence.report.get("upstreamReleaseVerified", False)):
            self.evidence.report["status"] = "failed"
        self.evidence.report["finishedAt"] = timestamp()
        self.checkpoint("finished")

    def logout_admin(self):
        if self.admin:
            self.guard("cleanup.admin-logout", lambda: self.api.call("POST", "/api/v2/auth/logout", token=self.admin, operation="cleanup.admin-logout"))
            self.admin = None


def write_reports(path, report):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)
    lines = ["# 真实媒体容量报告", "", "结果：" + report.get("status", "unknown") + "。", "",
             "运行标识：" + report.get("runPrefix", "未创建") + "。", "",
             "边界：真实 HTTPS 收流和 ZLM 只读统计；不代表 100 台独立终端或硬件解码能力。", "",
             "真实准备账号数：" + str(report.get("preparedAccounts", 0)) + "；实际并发登录数：" + str(report.get("loggedInUsers", 0)) + "。", "",
             "账号准备方式：" + {"reuse": "复用既有测试账号", "create": "新建测试账号"}.get(report.get("accountPreparationMode"), "尚未准备") + "。", "",
             "准备阶段秒数：" + str(report.get("preparationSeconds", 0)) + "；媒体起流秒数：" + str(report.get("mediaStartupSeconds", 0)) + "。", "",
             "持续测量秒数：" + str(report.get("measuredSeconds", 0)) + "。", "",
             "普通查询 P95：" + str(report.get("observedQueryP95Ms")) + " 毫秒；样本数：" + str(report.get("querySampleCount", 0))
             + "；门槛：严格小于 500 毫秒；达标：" + ("是" if report.get("queryLatencyTargetMet") else "否") + "。", "",
             "真实上游释放核对：" + ("通过" if report.get("upstreamReleaseVerified") else "未通过或尚未完成") + "。", "",
             "| 阶段 | 查询或操作 | 请求数 | 失败数 | P95 毫秒（含失败） | 成功 P95 毫秒 |", "| --- | --- | ---: | ---: | ---: | ---: |"]
    for metric in report.get("metrics", []):
        phase = {"prepare": "准备", "measurement": "查询测量", "media": "媒体", "cleanup": "清理"}[metric["phase"]]
        lines.append(f"| {phase} | {metric['operation']} | {metric['requests']} | {metric['failures']} | {metric['p95Ms']} | {metric['successP95Ms']} |")
    lines.extend(["", "## 实时来源证据", "",
                  "来源判定同时要求唯一上游、一致来源摘要、originTypeStr=pull 和有效 RTSP／RTSPS 来源协议，数字枚举仅供诊断。", ""])
    for snapshot in report.get("zlm", []):
        if snapshot.get("app") == "live":
            origins = snapshot.get("originEvidence", [])
            lines.append("- " + str(snapshot.get("at", "")) + "：originTypes=" + str(snapshot.get("originTypes", []))
                         + "，originTypeStr=" + str(sorted({str(item.get("originTypeStr")) for item in origins}))
                         + "，scheme=" + str(sorted({str(item.get("originScheme")) for item in origins}))
                         + "，唯一 RTSP 拉流来源核对=" + ("通过" if snapshot.get("rtspPullVerified") else "未通过") + "。")
    lines.extend(["", "## 错误明细", ""])
    for error in report.get("errors", []):
        lines.append(f"- {error['operation']}：{error['category']}，HTTP {error['httpStatus']}，{error['detail']}")
    lines.extend(["", "完整连接证据、ZLM 快照、临时用户 ID、清理结果及每次请求耗时见同名 JSON。", ""])
    path.with_suffix(".md").write_text("\n".join(lines), encoding="utf-8")


def remote_main(path):
    options = json.loads(path.read_text(encoding="utf-8"))
    scenario = Scenario(options, path.parent)

    def halt(signum, frame):
        scenario.stop.set()
        raise CapacityError("远程工作超时或收到终止信号。")

    for name in ("SIGALRM", "SIGTERM", "SIGINT", "SIGHUP"):
        signal.signal(getattr(signal, name), halt)
    signal.alarm(options["remote_timeout"])
    try:
        scenario.run()
    except (Exception, KeyboardInterrupt) as error:
        scenario.evidence.report["status"] = "failed"
        if not isinstance(error, RecordedError):
            scenario.evidence.error(scenario.evidence.report["stage"], error)
    finally:
        signal.alarm(0)
        for name in ("SIGTERM", "SIGINT", "SIGHUP"):
            signal.signal(getattr(signal, name), signal.SIG_IGN)
        try:
            scenario.cleanup()
        except Exception as error:
            scenario.evidence.error("cleanup", error)
            scenario.evidence.report["status"] = "failed"
            scenario.logout_admin()
            write_reports(path.parent / "report.json", scenario.evidence.snapshot())
        path.unlink(missing_ok=True)
        if options.get("uploaded_ca"):
            (path.parent / "trusted-ca.pem").unlink(missing_ok=True)
    return 0 if scenario.evidence.report["status"] == "passed" else 1


def execute(args, report_path):
    import paramiko

    require(bool(os.environ.get("VIDEO_PLATFORM_SSH_PASSWORD")), "缺少 VIDEO_PLATFORM_SSH_PASSWORD。")
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    if args.known_hosts:
        client.load_host_keys(str(args.known_hosts))
    if args.host_key_sha256:
        class Pin(paramiko.MissingHostKeyPolicy):
            def missing_host_key(self, client, hostname, key):
                actual = "SHA256:" + base64.b64encode(hashlib.sha256(key.asbytes()).digest()).decode().rstrip("=")
                require(actual == args.host_key_sha256, "SSH 主机指纹不匹配。")
        client.set_missing_host_key_policy(Pin())
    else:
        client.set_missing_host_key_policy(paramiko.RejectPolicy())
    prefix = args.reuse_prefix or "capacity_" + uuid.uuid4().hex[:20]
    remote = "/tmp/video-platform-real-capacity-" + uuid.uuid4().hex
    summary = {"runPrefix": prefix, "status": "running", "remoteDirectory": remote, "errors": []}
    finished = False
    uploaded_ca = False
    try:
        write_reports(report_path, summary)
        client.connect(args.host, port=args.port, username=args.user, password=os.environ["VIDEO_PLATFORM_SSH_PASSWORD"],
                       timeout=15, auth_timeout=15, banner_timeout=15, look_for_keys=False, allow_agent=False)
        client.get_transport().set_keepalive(15)
        with client.open_sftp() as sftp:
            sftp.get_channel().settimeout(30)
            sftp.mkdir(remote, mode=0o700)
            sftp.put(str(Path(__file__).resolve()), remote + "/worker.py")
            options = {key: value for key, value in vars(args).items() if key not in ("known_hosts", "host_key_sha256", "report", "local_ca_file")}
            options["prefix"] = prefix
            if args.local_ca_file:
                # 仅上传公开证书，不读取远程私钥，不修改系统 CA 或原始证书权限。
                uploaded_ca = True
                sftp.put(str(args.local_ca_file), remote + "/trusted-ca.pem")
                sftp.chmod(remote + "/trusted-ca.pem", 0o600)
                options["ca_file"] = remote + "/trusted-ca.pem"
            options["uploaded_ca"] = uploaded_ca
            with sftp.open(remote + "/job.json", "w") as target:
                target.write(json.dumps(options).encode("utf-8"))
            _, stdout, _ = client.exec_command("python3 " + shlex.quote(remote + "/worker.py") + " --remote-job " + shlex.quote(remote + "/job.json"), timeout=30)
            channel = stdout.channel
            deadline = time.monotonic() + args.remote_timeout + 600
            last_poll = 0
            while not channel.exit_status_ready():
                while channel.recv_ready():
                    channel.recv(65536)
                while channel.recv_stderr_ready():
                    channel.recv_stderr(65536)
                require(time.monotonic() < deadline, "远程未在总超时及清理宽限期内结束，请按随机标识检查残留账号。")
                if time.monotonic() - last_poll >= 15:
                    try:
                        with sftp.open(remote + "/report.json") as source:
                            summary = json.load(source)
                        summary["remoteDirectory"] = remote
                        write_reports(report_path, summary)
                        print("进度：" + summary.get("stage", "等待远程报告"), flush=True)
                    except FileNotFoundError:
                        pass
                    last_poll = time.monotonic()
                time.sleep(.2)
            code = channel.recv_exit_status()
            with sftp.open(remote + "/report.json") as source:
                summary = json.load(source)
            summary["remoteDirectory"] = remote
            require(code == 0 and summary.get("status") == "passed", "真实容量验收失败，原始脱敏分类及清理结果见报告。")
            finished = True
    except (Exception, KeyboardInterrupt) as error:
        summary["status"] = "failed"
        summary["launcherError"] = str(error) if isinstance(error, CapacityError) else "启动或传输失败：" + type(error).__name__
    finally:
        if uploaded_ca:
            try:
                with client.open_sftp() as sftp:
                    sftp.get_channel().settimeout(30)
                    try:
                        sftp.remove(remote + "/trusted-ca.pem")
                    except FileNotFoundError:
                        pass
                summary["temporaryCaCleanup"] = "passed"
            except Exception as error:
                summary["status"] = "failed"
                summary["temporaryCaCleanup"] = "远程 finally 仍会尝试清理；本机确认失败：" + type(error).__name__
        # 失败时保留远程脱敏报告和脚本以定位；绝不在这里终止其他进程或操作数据库。
        if finished:
            try:
                with client.open_sftp() as sftp:
                    for name in ("worker.py", "job.json", "trusted-ca.pem", "report.json", "report.md", "report.json.tmp"):
                        try:
                            sftp.remove(remote + "/" + name)
                        except FileNotFoundError:
                            pass
                    sftp.rmdir(remote)
                summary["remoteFilesCleanup"] = "passed"
            except Exception as error:
                summary["status"] = "failed"
                summary["remoteFilesCleanup"] = type(error).__name__
        else:
            summary["remoteFilesCleanup"] = "保留脱敏报告以定位，账号和媒体清理由远程 finally 执行；断线时需核查结果。"
        client.close()
        write_reports(report_path, summary)
    return 0 if summary["status"] == "passed" else 1


def main(argv=None):
    args = parser().parse_args(argv)
    if args.remote_timeout < args.duration + args.startup_timeout + 120:
        parser().error("remote-timeout 必须为测量、起流及账号准备保留足够时间。")
    if args.host_key_sha256 and not re.fullmatch(r"SHA256:[A-Za-z0-9+/]{43}", args.host_key_sha256):
        parser().error("SSH 指纹必须为 SHA256: 后接 43 位 Base64。")
    if not args.execute:
        print("参数检查通过，未连接服务器；实际验收必须显式指定 --execute。")
        return 0
    if args.local_ca_file:
        require(args.local_ca_file.is_file(), "未找到本机公开 CA 证书。")
        # 在连接服务器前确认文件可被正常信任库解析，仍保留 CERT_REQUIRED 和主机名验证。
        context = ssl.create_default_context(cafile=str(args.local_ca_file))
        require(context.verify_mode == ssl.CERT_REQUIRED and context.check_hostname, "TLS 验证未正确启用。")
    report = args.report or ROOT / "artifacts/v2-qa/real-capacity" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8] + ".json")
    require(report.suffix.lower() == ".json", "报告路径必须以 .json 结尾。")
    result = execute(args, report)
    print("JSON 报告：" + str(report.resolve()) + "；中文报告：" + str(report.with_suffix(".md").resolve()))
    return result


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    if len(sys.argv) == 3 and sys.argv[1] == "--remote-job":
        raise SystemExit(remote_main(Path(sys.argv[2])))
    raise SystemExit(main())
