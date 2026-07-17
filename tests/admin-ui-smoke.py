import json
from urllib.parse import urlparse

from playwright.sync_api import sync_playwright


def api_response(route):
    path = urlparse(route.request.url).path
    if path.endswith("/admin/playback-exports"):
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
    elif path.endswith("/admin/playback-exports/cameras"):
        body = [{
            "id": "22222222-2222-2222-2222-222222222222",
            "code": "CAM-WH-001",
            "alias": "仓库入口",
            "regionId": "33333333-3333-3333-3333-333333333333",
            "connectivity": "Online"
        }]
    else:
        body = []
    route.fulfill(status=200, content_type="application/json", body=json.dumps(body))


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 960})
    page.add_init_script("""
        sessionStorage.setItem('video-platform-admin-token', 'smoke-test-token');
        sessionStorage.setItem('video-platform-admin-user', 'smoke-admin');
    """)
    page.route("**/api/**", api_response)
    browser_errors = []
    page.on("console", lambda message: browser_errors.append(message.text) if message.type == "error" else None)
    page.goto("http://127.0.0.1:5173/admin", wait_until="networkidle")
    page.get_by_role("button", name="录像导出").click()
    page.get_by_role("heading", name="录像导出").wait_for(state="visible", timeout=5000)
    assert not browser_errors, browser_errors
    assert page.get_by_text("CAM-WH-001").is_visible()
    page.get_by_role("button", name="新建导出").click()
    assert page.get_by_role("dialog", name="新建录像导出").is_visible()
    assert page.locator("select[name='cameraId'] option").count() == 2
    browser.close()
