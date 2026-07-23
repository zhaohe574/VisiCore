"""Linux Docker 部署包的离线结构回归测试。"""

import json
import base64
import os
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BUILD_SCRIPT = ROOT / "tools" / "build-linux-deployment-packages.sh"
BASH_EXECUTABLE = os.environ.get("VISICORE_BASH", "bash")


class LinuxDeploymentPackageTests(unittest.TestCase):
    def run_command(self, *arguments: str, cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
        return subprocess.run(arguments, cwd=cwd or ROOT, check=True, text=True, capture_output=True)

    def run_binary_command(self, *arguments: str) -> subprocess.CompletedProcess[bytes]:
        return subprocess.run(arguments, cwd=ROOT, check=True, capture_output=True)

    def test_shell_scripts_parse(self):
        scripts = (
            "deploy/packages/install-core.sh",
            "deploy/packages/install-edge.sh",
            "tools/build-linux-deployment-packages.sh",
            "tools/verify-release-promotion.sh",
        )
        self.run_command(BASH_EXECUTABLE, "-n", *scripts)

    def test_builder_creates_deploy_only_archives_without_clearing_output(self):
        for executable in ("bash", "jq", "openssl", "tar"):
            if shutil.which(executable) is None:
                self.skipTest(f"测试环境缺少 {executable}")

        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            descriptor = workspace / "release-descriptor.json"
            descriptor.write_text(
                json.dumps(
                    {"signingPublicKeyId": "test-key", "sourceCommit": "a" * 40},
                    ensure_ascii=True,
                    separators=(",", ":"),
                    sort_keys=True,
                ),
                encoding="utf-8",
            )
            private_key = workspace / "signing-key.pem"
            public_key = workspace / "release-public-key.pem"
            signature = workspace / "release-descriptor.signature.base64"
            self.run_command("openssl", "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(private_key))
            self.run_command("openssl", "pkey", "-in", str(private_key), "-pubout", "-out", str(public_key))
            signed = self.run_binary_command(
                "openssl", "dgst", "-sha256", "-sign", str(private_key), "-sigopt", "rsa_padding_mode:pss",
                "-sigopt", "rsa_pss_saltlen:-1", str(descriptor),
            )
            signature.write_bytes(base64.b64encode(signed.stdout))

            core_root = workspace / "core"
            edge_root = workspace / "edge"
            for root, executable in ((core_root, "VisiCore.CoreHostAgent"), (edge_root, "VisiCore.EdgeHostAgent")):
                for runtime in ("linux-x64", "linux-arm64"):
                    directory = root / runtime
                    directory.mkdir(parents=True)
                    target = directory / executable
                    target.write_text("#!/usr/bin/env sh\nexit 0\n", encoding="ascii")
                    target.chmod(0o755)

            output = workspace / "output"
            output.mkdir()
            sentinel = output / "already-present.txt"
            sentinel.write_text("保留", encoding="utf-8")
            digest = "b" * 64
            self.run_command(
                BASH_EXECUTABLE, str(BUILD_SCRIPT),
                "--release-tag", "v0.1.6-rc.1",
                "--core-image", f"visicore/visicore-core@sha256:{digest}",
                "--edge-image", f"visicore/visicore-edge@sha256:{digest}",
                "--descriptor", str(descriptor),
                "--descriptor-signature", str(signature),
                "--public-key", str(public_key),
                "--core-agent-root", str(core_root),
                "--edge-agent-root", str(edge_root),
                "--output", str(output),
            )
            self.assertTrue(sentinel.is_file())
            expected = {
                "visicore-core-0.1.6-rc.1-linux-amd64.tar.gz",
                "visicore-core-0.1.6-rc.1-linux-arm64.tar.gz",
                "visicore-edge-0.1.6-rc.1-linux-amd64.tar.gz",
                "visicore-edge-0.1.6-rc.1-linux-arm64.tar.gz",
            }
            self.assertEqual(expected, {path.name for path in output.glob("*.tar.gz")})

            for archive in output.glob("*.tar.gz"):
                component = "core" if "-core-" in archive.name else "edge"
                with tarfile.open(archive, "r:gz") as package:
                    names = set(package.getnames())
                    root = f"visicore-{component}"
                    self.assertIn(f"{root}/install.sh", names)
                    self.assertIn(f"{root}/package-manifest.json", names)
                    self.assertIn(f"{root}/release/release-descriptor.json", names)
                    compose_name = "compose.yaml" if component == "core" else "edge-agent.compose.yaml"
                    compose = package.extractfile(f"{root}/{compose_name}").read().decode("utf-8")
                    self.assertNotIn("build:", compose)
                    manifest = json.loads(package.extractfile(f"{root}/package-manifest.json").read())
                    self.assertEqual(component, manifest["component"])
                    self.assertIn(manifest["architecture"], {"amd64", "arm64"})


if __name__ == "__main__":
    unittest.main()
