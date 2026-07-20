import json
import re
from pathlib import Path

from playwright.sync_api import sync_playwright


def response_for(path: str):
    if path.endswith("/auth/login"):
        return {"accessToken": "visual-qa-token", "expiresAt": "2026-07-14T00:00:00Z", "username": "qa-admin"}
    if path.endswith("/admin/users"):
        return [{"id": "10000000-0000-0000-0000-000000000001", "username": "qa-admin", "isSystemAdministrator": True, "disabledAt": None, "roleIds": ["20000000-0000-0000-0000-000000000001"]}]
    if path.endswith("/admin/roles"):
        return [{"id": "20000000-0000-0000-0000-000000000001", "code": "ASSET-OPS", "name": "资产运维", "systemPermissions": 23, "cameraScopes": []}]
    if path.endswith("/admin/regions"):
        return [{"id": "30000000-0000-0000-0000-000000000001", "parentId": None, "code": "HQ", "name": "总部"}]
    if path.endswith("/admin/cameras"):
        return []
    if path.endswith("/admin/recorders"):
        return []
    if path.endswith("/admin/device-workers"):
        return []
    if path.endswith("/admin/alert-incidents"):
        return []
    if path.endswith("/admin/notification-deliveries"):
        return []
    return []


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 960}, device_scale_factor=1)
    console_errors = []
    created_roles = []
    page.on("console", lambda message: console_errors.append(message.text) if message.type == "error" else None)

    def route_handler(route):
        request = route.request
        if request.method == "POST" and request.url.endswith("/auth/login"):
            route.fulfill(status=200, content_type="application/json", body='{"accessToken":"visual-qa-token","expiresAt":"2026-07-14T00:00:00Z","username":"qa-admin"}')
            return
        if request.method == "POST" and request.url.endswith("/admin/roles"):
            created_roles.append(json.loads(request.post_data or "{}"))
        route.fulfill(status=200, content_type="application/json", body=json.dumps(response_for(request.url.split("?")[0])))

    page.route("**/api/**", route_handler)
    page.goto("http://127.0.0.1:5178/admin", wait_until="networkidle")
    page.locator('input[name="username"]').fill("qa-admin")
    page.locator('input[name="password"]').fill("visual-qa-password")
    page.get_by_role("button", name="登录控制台").click()
    page.wait_for_load_state("networkidle")
    page.get_by_text("账号与权限", exact=True).click()
    page.wait_for_load_state("networkidle")
    page.get_by_role("button", name=re.compile(r"^角色")).click()
    page.get_by_role("button", name="新增角色").click()
    dialog = page.get_by_role("dialog", name="新增角色")
    dialog.wait_for()
    assert dialog.locator('input[name="systemPermissions"]').count() == 6
    for label in ["资产管理", "边缘节点", "通知配置", "运维处置", "审计查看", "录像导出"]:
        assert dialog.get_by_text(label, exact=True).count() == 1

    screenshot = Path(__file__).resolve().parents[1] / ".local-sdk" / "admin-system-permissions-qa.png"
    screenshot.parent.mkdir(exist_ok=True)
    page.screenshot(path=str(screenshot), full_page=True)
    dialog.locator('input[name="code"]').fill("QA-EXPORT")
    dialog.locator('input[name="name"]').fill("界面回归角色")
    dialog.locator('input[value="1"]').check()
    dialog.locator('input[value="32"]').check()
    dialog.get_by_role("button", name="创建").click()
    page.get_by_text("角色已创建", exact=True).wait_for()
    assert created_roles[-1]["systemPermissions"] == 33
    assert not console_errors, console_errors
    browser.close()
