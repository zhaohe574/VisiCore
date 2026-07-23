#!/usr/bin/env python3
"""验证重要变更档案与发行档案的最小治理契约。"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CHANGE_ID = re.compile(r"^[a-z0-9][a-z0-9-]{1,63}$")
RELEASE_ID = re.compile(r"^v[0-9]+\.[0-9]+\.[0-9]+$")
REQUIRED_CHANGE_FILES = ("proposal.md", "design.md", "tasks.md", "verification.md", "change.json")
REQUIRED_RELEASE_DOCUMENTS = ("proposal", "compatibility", "tasks", "verification", "evidence", "releaseNotes")
HIGH_IMPACT_PREFIXES = (
    "src/VisiCore.Api/", "src/VisiCore.Core/", "src/VisiCore.Persistence/", "src/VisiCore.Edge",
    "src/VisiCore.Setup/", "src/VisiCore.Admin/", "deploy/", ".github/workflows/", "versions/"
)
HIGH_IMPACT_FILES = {"compose.yaml", "VERSION"}


def fail(message: str) -> None:
    raise ValueError(message)


def read_json(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"无法读取 JSON：{path.relative_to(ROOT)}：{error}")
    if not isinstance(value, dict):
        fail(f"JSON 根节点必须为对象：{path.relative_to(ROOT)}")
    return value


def validate_change(change_id: str, expected_release: str | None = None, require_verified: bool = False) -> dict:
    if not CHANGE_ID.fullmatch(change_id):
        fail(f"无效的 OpenSpec change ID：{change_id}")
    directory = ROOT / "openspec" / "changes" / change_id
    for filename in REQUIRED_CHANGE_FILES:
        path = directory / filename
        if not path.is_file() or not path.read_text(encoding="utf-8").strip():
            fail(f"变更档案缺少必需内容：openspec/changes/{change_id}/{filename}")
    change = read_json(directory / "change.json")
    if change.get("id") != change_id:
        fail(f"change.json 的 id 与目录不一致：{change_id}")
    if change.get("status") not in {"proposed", "approved", "implemented", "verified"}:
        fail(f"变更状态无效：{change_id}")
    release_id = change.get("releaseId")
    if not isinstance(release_id, str) or not RELEASE_ID.fullmatch(release_id):
        fail(f"变更未声明有效 releaseId：{change_id}")
    if expected_release and release_id != expected_release:
        fail(f"变更 {change_id} 属于 {release_id}，不能关联 {expected_release}")
    if require_verified and change["status"] != "verified":
        fail(f"RC 发行只能引用已验证变更：{change_id}")
    return change


def validate_release(release_id: str) -> None:
    if not RELEASE_ID.fullmatch(release_id):
        fail(f"发行版本格式无效：{release_id}")
    directory = ROOT / "docs" / "releases" / release_id
    manifest = read_json(directory / "release-manifest.json")
    if manifest.get("schemaVersion") != 1 or manifest.get("releaseId") != release_id:
        fail(f"发行清单版本或 releaseId 无效：{release_id}")
    documents = manifest.get("documents")
    if not isinstance(documents, dict):
        fail(f"发行清单缺少 documents：{release_id}")
    for key in REQUIRED_RELEASE_DOCUMENTS:
        value = documents.get(key)
        if not isinstance(value, str) or not value or Path(value).name != value:
            fail(f"发行清单缺少安全的文档路径：{release_id}/{key}")
        document = directory / value
        if not document.is_file() or not document.read_text(encoding="utf-8").strip():
            fail(f"发行档案文档不存在：{document.relative_to(ROOT)}")
    change_ids = manifest.get("changeIds")
    if not isinstance(change_ids, list) or not change_ids or len(change_ids) > 64:
        fail(f"发行清单必须包含 1 至 64 个 change ID：{release_id}")
    if len(set(change_ids)) != len(change_ids) or not all(isinstance(value, str) for value in change_ids):
        fail(f"发行清单 changeIds 无效：{release_id}")
    for change_id in change_ids:
        validate_change(change_id, release_id, require_verified=True)


def changed_files(base_sha: str) -> list[str]:
    process = subprocess.run(
        ["git", "diff", "--name-only", f"{base_sha}...HEAD"], cwd=ROOT, check=False, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE
    )
    if process.returncode != 0:
        fail(f"无法读取 Pull Request 改动：{process.stderr.strip()}")
    return [line.strip().replace("\\", "/") for line in process.stdout.splitlines() if line.strip()]


def is_high_impact(path: str) -> bool:
    return path in HIGH_IMPACT_FILES or path.startswith(HIGH_IMPACT_PREFIXES)


def validate_pull_request(event_path: Path, base_sha: str) -> None:
    event = read_json(event_path)
    files = changed_files(base_sha)
    if not any(is_high_impact(path) for path in files):
        print("未检测到高影响路径变更，OpenSpec 门禁通过。")
        return
    body = event.get("pull_request", {}).get("body") or ""
    matched = re.search(r"(?im)^\s*OpenSpec-Change:\s*([a-z0-9-]+|N/A)\s*$", body)
    if not matched or matched.group(1) == "N/A":
        fail("高影响变更必须在 Pull Request 正文填写 OpenSpec-Change。")
    validate_change(matched.group(1))
    print(f"OpenSpec change 已验证：{matched.group(1)}")


def normalize_release(value: str) -> str:
    candidate = value.strip()
    match = re.fullmatch(r"(v[0-9]+\.[0-9]+\.[0-9]+)(?:-rc\.[1-9][0-9]*)?", candidate)
    if not match:
        fail(f"无法从 ref 解析发行版本：{value}")
    return match.group(1)


def main() -> int:
    parser = argparse.ArgumentParser(description="验证 VisiCore OpenSpec 发布治理档案")
    parser.add_argument("--release", help="RC 或 stable Git 标签，例如 v0.1.5-rc.1")
    parser.add_argument("--pr-event", type=Path, help="GitHub Pull Request 事件 JSON")
    parser.add_argument("--base-sha", help="Pull Request 基线提交")
    args = parser.parse_args()
    try:
        if args.release:
            validate_release(normalize_release(args.release))
        elif args.pr_event and args.base_sha:
            validate_pull_request(args.pr_event, args.base_sha)
        else:
            fail("必须提供 --release，或同时提供 --pr-event 与 --base-sha。")
    except ValueError as error:
        print(f"OpenSpec 治理校验失败：{error}", file=sys.stderr)
        return 1
    print("OpenSpec 治理校验通过。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
