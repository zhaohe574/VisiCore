"""在服务器验证恢复的资产 API，只读取业务数据，登录凭据留在内存中。"""
import argparse
import json
from pathlib import Path
import urllib.error
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://127.0.0.1:5082")
    parser.add_argument("--config", default="/home/liteware/.config/video-platform/v2-production.json")
    args = parser.parse_args()
    configuration = json.loads(Path(args.config).read_text())
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    token = None
    checks = []

    def request(path, body=None):
        headers = {"Accept": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        if body is not None:
            headers["Content-Type"] = "application/json"
        query = urllib.request.Request(args.url.rstrip("/") + path,
                                       data=json.dumps(body).encode() if body is not None else None,
                                       headers=headers)
        try:
            with opener.open(query, timeout=40) as response:
                raw = response.read()
                return response.status, json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            return error.code, None

    try:
        status, login = request("/api/v2/auth/login", {
            "username": "admin", "password": configuration["adminPassword"],
            "clientType": "desktop", "clientVersion": "2.1.7-restore-validation"})
        if status != 200 or not login or not login.get("accessToken"):
            raise RuntimeError("验证登录失败，HTTP " + str(status))
        token = login["accessToken"]
        endpoints = ["/api/v2/computers", "/api/v2/ip-pools", "/api/v2/network-devices",
                     "/api/v2/terminal-boxes", "/api/v2/pon/onus?page=1&pageSize=20",
                     "/api/v2/pon/statistics", "/api/v2/pon/dashboard/kanban",
                     "/api/v2/pon/dashboard/history", "/api/v2/pon/olts",
                     "/api/v2/pon/health-index", "/api/v2/digital-twin/scenes",
                     "/api/v2/organization", "/api/v2/channels?page=1&pageSize=20",
                     "/api/v2/favorites", "/api/v2/layouts", "/api/v2/devices"]
        boxes = []
        for path in endpoints:
            status, data = request(path)
            result = {"path": path, "status": status}
            if isinstance(data, list):
                result["count"] = len(data)
            elif isinstance(data, dict) and "total" in data:
                result["total"] = data["total"]
            if path.startswith("/api/v2/channels") and status == 200:
                result["passwordHidden"] = all("password" not in item for item in data["items"])
            if path == "/api/v2/terminal-boxes" and status == 200:
                boxes = data
            checks.append(result)
        for box in sorted(boxes, key=lambda item: item.get("onuCount", 0), reverse=True)[:1]:
            path = f"/api/v2/terminal-boxes/{box['id']}/topology"
            status, data = request(path)
            checks.append({"path": path, "status": status,
                           "onuCount": len(data.get("onus", [])) if isinstance(data, dict) else None})
        print(json.dumps({"checks": checks, "passed": all(c["status"] == 200 and c.get("passwordHidden", True) for c in checks)}, ensure_ascii=False, indent=2))
        return 0 if all(c["status"] == 200 and c.get("passwordHidden", True) for c in checks) else 1
    finally:
        if token:
            request("/api/v2/auth/logout", {})


if __name__ == "__main__":
    raise SystemExit(main())
