"""OpenSpec 治理校验器的独立负向测试。"""

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "tools" / "verify-openspec-governance.py"
SPEC = importlib.util.spec_from_file_location("openspec_governance", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(MODULE)


class OpenSpecGovernanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.previous_root = MODULE.ROOT
        MODULE.ROOT = self.root
        self._write_valid_release()

    def tearDown(self):
        MODULE.ROOT = self.previous_root
        self.temp.cleanup()

    def _write(self, relative: str, value: str):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding="utf-8")

    def _write_valid_release(self):
        change = {
            "id": "release-governance-center",
            "status": "verified",
            "releaseId": "v0.1.5"
        }
        self._write("openspec/changes/release-governance-center/change.json", json.dumps(change))
        for name in ("proposal.md", "design.md", "tasks.md", "verification.md"):
            self._write(f"openspec/changes/release-governance-center/{name}", "有效内容\n")
        documents = {
            "proposal": "proposal.md", "compatibility": "compatibility.md", "tasks": "tasks.md",
            "verification": "verification.md", "evidence": "evidence.md", "releaseNotes": "release-notes.md"
        }
        manifest = {"schemaVersion": 1, "releaseId": "v0.1.5", "changeIds": ["release-governance-center"], "documents": documents}
        self._write("docs/releases/v0.1.5/release-manifest.json", json.dumps(manifest))
        for name in documents.values():
            self._write(f"docs/releases/v0.1.5/{name}", "有效内容\n")

    def test_valid_release_passes(self):
        MODULE.validate_release("v0.1.5")

    def test_missing_document_is_rejected(self):
        (self.root / "docs/releases/v0.1.5/evidence.md").unlink()
        with self.assertRaisesRegex(ValueError, "发行档案文档不存在"):
            MODULE.validate_release("v0.1.5")

    def test_unverified_change_is_rejected(self):
        path = self.root / "openspec/changes/release-governance-center/change.json"
        change = json.loads(path.read_text(encoding="utf-8"))
        change["status"] = "implemented"
        path.write_text(json.dumps(change), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "已验证变更"):
            MODULE.validate_release("v0.1.5")

    def test_high_impact_paths_require_change_reference(self):
        event = self.root / "event.json"
        event.write_text(json.dumps({"pull_request": {"body": "OpenSpec-Change: N/A"}}), encoding="utf-8")
        original_changed_files = MODULE.changed_files
        MODULE.changed_files = lambda _: ["src/VisiCore.Api/Program.cs"]
        try:
            with self.assertRaisesRegex(ValueError, "必须在 Pull Request 正文填写"):
                MODULE.validate_pull_request(event, "ignored")
        finally:
            MODULE.changed_files = original_changed_files


if __name__ == "__main__":
    unittest.main()
