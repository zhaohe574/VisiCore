import argparse
import json
from urllib.parse import urlparse

from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument("--base-url", default="http://127.0.0.1:5173")
admin_base_url = parser.parse_args().base_url.rstrip("/")
unexpected_paths = []


def api_response(route):
    path = urlparse(route.request.url).path
    if path.endswith("/setup/status"):
        body = {"state": "completed", "defaults": None}
        status = 200
    elif path.endswith("/admin/https-configuration"):
        body = {"title": "无 HTTPS 配置管理权限", "status": 403}
        status = 403
    elif path.endswith("/admin/observability/overview"):
        body = {
            "activeStreamSessions": 12,
            "onlineEdgeAgents": 4,
            "staleEdgeAgents": 1,
            "upgradeFailures": {"signature_invalid": 2},
            "backupResults": {"available": 8},
            "collectedAt": "2026-07-22T01:00:00+00:00"
        }
        status = 200
    elif path.endswith("/admin/platform-operations/overview"):
        body = {
            "edgeAgents": [],
            "edgeAgentCount": 0,
            "onlineEdgeAgentCount": 0,
            "unhealthyEdgeAgentCount": 0,
            "pendingOperationCount": 0,
            "recentOperations": []
        }
        status = 200
    elif path in {
        "/api/v1/admin/platform-operations/deployments",
        "/api/v1/admin/edge-releases",
        "/api/v1/admin/upgrade-plans",
        "/api/v1/admin/edge-agents"
    }:
        body = []
        status = 200
    elif path.endswith("/admin/release-catalog"):
        body = [{
            "id": "44444444-4444-4444-4444-444444444444",
            "productVersion": "0.1.5",
            "channel": "stable",
            "releaseId": "v0.1.5",
            "sourceCommit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "promotedFrom": "v0.1.5-rc.1",
            "databaseMigrationMode": "automatic-backup",
            "rollbackStrategy": "backup-restore",
            "status": "available",
            "signingPublicKeyId": "test-key",
            "publishedAt": "2026-07-23T01:00:00+00:00",
            "expiresAt": "2026-08-23T01:00:00+00:00",
            "artifacts": [],
            "governance": {
                "id": "55555555-5555-5555-5555-555555555555",
                "releaseCatalogId": "44444444-4444-4444-4444-444444444444",
                "changeIds": ["release-governance-center"],
                "sourceCommit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "dossierUrl": "https://github.com/zhaohe574/VisiCore/blob/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/docs/releases/v0.1.5/evidence.md",
                "releaseUrl": "https://github.com/zhaohe574/VisiCore/releases/tag/v0.1.5",
                "workflowRunUrl": "https://github.com/zhaohe574/VisiCore/actions/runs/123456",
                "releaseEvidenceUrl": "https://github.com/zhaohe574/VisiCore/releases/download/v0.1.5/release-evidence.json",
                "stagingEvidenceUrl": "https://github.com/zhaohe574/VisiCore/releases/download/v0.1.5-rc.1/staging-evidence.json",
                "sbomUrl": "https://github.com/zhaohe574/VisiCore/releases/download/v0.1.5/visicore-release-v0.1.5.spdx.json",
                "provenanceUrl": "https://github.com/zhaohe574/VisiCore/attestations/123456",
                "verificationUrl": "https://github.com/zhaohe574/VisiCore/blob/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/docs/releases/v0.1.5/verification.md",
                "recordedBy": "smoke-admin",
                "recordedAt": "2026-07-23T01:00:00+00:00"
            }
        }]
        status = 200
    elif path.endswith("/admin/playback-exports"):
        body = [{
            "id": "11111111-1111-1111-1111-111111111111",
            "cameraId": "22222222-2222-2222-2222-222222222222",
            "status": "Queued",
            "startedAt": "2026-07-14T00:00:00+00:00",
            "endedAt": "2026-07-14T01:00:00+00:00",
            "container": "mp4",
            "requestedAt": "2026-07-14T01:05:00+00:00",
            "artifact": None,
            "failureCode": None
        }]
        status = 200
    elif path.endswith("/admin/playback-exports/cameras"):
        body = [{
            "id": "22222222-2222-2222-2222-222222222222",
            "code": "CAM-WH-001",
            "alias": "仓库入口",
            "regionId": "33333333-3333-3333-3333-333333333333",
            "connectivity": "Online"
        }]
        status = 200
    elif path in {
        "/api/v1/admin/cameras",
        "/api/v1/admin/recorders",
        "/api/v1/admin/device-workers",
        "/api/v1/admin/alert-incidents",
        "/api/v1/admin/notification-deliveries"
    }:
        body = []
        status = 200
    else:
        unexpected_paths.append(path)
        body = {"title": "未声明的 API mock", "status": 500, "detail": path}
        status = 500
    route.fulfill(status=status, content_type="application/json", body=json.dumps(body))


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 960})
    page.add_init_script("""
        sessionStorage.setItem('visicore-admin-token', 'smoke-test-token');
        sessionStorage.setItem('visicore-admin-user', 'smoke-admin');
    """)
    page.route("**/api/**", api_response)
    browser_errors = []
    page.on("console", lambda message: browser_errors.append(message.text) if message.type == "error" and "Failed to load resource" not in message.text else None)
    page.goto(f"{admin_base_url}/admin", wait_until="networkidle")
    page.get_by_role("button", name="运行指标").click()
    page.get_by_role("heading", name="运行指标").wait_for(state="visible", timeout=5000)
    assert page.get_by_text("signature_invalid").is_visible()
    assert page.url.endswith("/admin/observability")
    page.goto(f"{admin_base_url}/admin/observability", wait_until="networkidle")
    page.get_by_role("heading", name="运行指标").wait_for(state="visible", timeout=5000)
    assert page.get_by_text("signature_invalid").is_visible()
    page.get_by_role("button", name="录像导出").click()
    page.get_by_role("heading", name="录像导出").wait_for(state="visible", timeout=5000)
    assert not browser_errors, f"浏览器错误：{browser_errors}；未声明的 API mock：{unexpected_paths}"
    assert page.get_by_text("CAM-WH-001").is_visible()
    page.get_by_role("button", name="新建导出").click()
    assert page.get_by_role("dialog", name="新建录像导出").is_visible()
    assert page.locator("select[name='cameraId'] option").count() == 2
    page.get_by_role("dialog", name="新建录像导出").get_by_role("button", name="关闭").click()
    page.get_by_role("button", name="平台运维").click()
    page.get_by_role("heading", name="版本中心与平台运维").wait_for(state="visible", timeout=5000)
    assert page.get_by_text("发行治理").is_visible()
    page.get_by_role("button", name="查看档案").click()
    assert page.get_by_role("dialog", name="发行治理 · v0.1.5").is_visible()
    release_link = page.get_by_role("link", name="Release")
    assert release_link.get_attribute("target") == "_blank"
    assert "noopener" in (release_link.get_attribute("rel") or "")
    assert not unexpected_paths, f"存在未声明的 API mock：{unexpected_paths}"
    browser.close()
