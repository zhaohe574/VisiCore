"""验证值守在认证拒绝后停止，不以重复登录扩大账号锁定。"""
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
import urllib.error
from unittest.mock import Mock, patch


spec = importlib.util.spec_from_file_location("v2_soak", Path(__file__).with_name("v2-soak.py"))
soak = importlib.util.module_from_spec(spec)
spec.loader.exec_module(soak)


def rejected(code=401):
    return urllib.error.HTTPError("https://example.test/api", code, "认证拒绝", {}, None)


class AuthenticationStopTests(unittest.TestCase):
    def client(self, stop=None, authenticated=False):
        client = soak.ApiClient("https://example.test", None, "test", "test-only", stop)
        if authenticated:
            client.token = "existing-test-token"
            client.refresh_at = time.monotonic() + 3600
        return client

    def test_rejected_password_is_sent_once(self):
        for code in (401, 403):
            with self.subTest(code=code):
                client = self.client()
                with patch.object(soak, "request_json", side_effect=rejected(code)) as request:
                    for _ in range(3):
                        with self.assertRaises(soak.AuthenticationRejected):
                            client.call("GET", "/api/v2/dashboard")
                    client.logout()
                self.assertEqual(request.call_count, 1)
                self.assertTrue(client.stop.is_set())

    def test_refresh_rejection_never_falls_back_to_login(self):
        client = self.client(authenticated=True)
        client.refresh_at = 0
        with patch.object(soak, "request_json", side_effect=rejected()) as request:
            for _ in range(2):
                with self.assertRaises(soak.AuthenticationRejected):
                    client.call("GET", "/api/v2/dashboard")
        self.assertEqual(request.call_count, 1)
        self.assertTrue(request.call_args.args[1].endswith("/auth/refresh"))
        self.assertTrue(client.stop.is_set())

    def test_revoked_control_request_stops_other_clients(self):
        stop = threading.Event()
        current, other = self.client(stop, True), self.client(stop)
        with patch.object(soak, "request_json", side_effect=rejected()) as request:
            with self.assertRaises(soak.AuthenticationRejected):
                current.call("POST", "/api/v2/live-sessions/example/renew")
            with self.assertRaises(soak.SoakError):
                other.call("GET", "/api/v2/channels")
        self.assertEqual(request.call_count, 1)

    def test_inflight_login_cannot_start_media_after_peer_rejection(self):
        stop, entered, resume = threading.Event(), threading.Event(), threading.Event()
        current, other = self.client(stop), self.client(stop)
        errors, paths = [], []

        def server(context, url, method, body, headers):
            paths.append(url)
            if threading.current_thread().name == "在途登录":
                entered.set()
                if not resume.wait(2):
                    raise AssertionError("等待并发认证拒绝超时")
                return {"accessToken": "late-token"}
            raise rejected()

        def pending():
            try:
                other.call("POST", "/api/v2/live-sessions", {"channelId": 2})
            except soak.SoakError as error:
                errors.append(error.code)

        with patch.object(soak, "request_json", side_effect=server):
            thread = threading.Thread(target=pending, name="在途登录")
            thread.start()
            try:
                self.assertTrue(entered.wait(2))
                with self.assertRaises(soak.AuthenticationRejected):
                    current.authenticate()
            finally:
                resume.set()
                thread.join(2)
        self.assertFalse(thread.is_alive())
        self.assertEqual(errors, ["run_stopping"])
        self.assertEqual(len(paths), 2)
        self.assertTrue(all(path.endswith("/auth/login") for path in paths))

    def test_stopping_cleanup_uses_existing_token_without_refresh(self):
        client = self.client(authenticated=True)
        client.refresh_at = 0
        client.stop.set()
        with patch.object(soak, "request_json", return_value=None) as request:
            client.cleanup_call("DELETE", "/api/v2/live-sessions/example")
            client.logout()
        paths = [call.args[1] for call in request.call_args_list]
        self.assertEqual(paths, ["https://example.test/api/v2/live-sessions/example",
                                 "https://example.test/api/v2/auth/logout"])
        self.assertIsNone(client.token)

    def test_missing_token_cleanup_cannot_create_login(self):
        client = self.client()
        with patch.object(soak, "request_json") as request:
            with self.assertRaises(soak.SoakError):
                client.cleanup_call("DELETE", "/api/v2/live-sessions/example")
        request.assert_not_called()

    def test_revoked_cleanup_retains_unverified_resource_and_stops(self):
        stop = threading.Event()
        client = self.client(stop, True)
        with tempfile.TemporaryDirectory() as directory:
            recorder = soak.Recorder(Path(directory), 86400)
            worker = soak.StreamWorker({"id": 1, "deviceId": 1, "deviceChannel": 1}, client, recorder, stop)
            worker.session_id = "known-session"
            stop.set()
            try:
                with patch.object(soak, "request_json", side_effect=rejected()) as request:
                    worker.run()
                self.assertEqual(request.call_count, 1)
                self.assertEqual(worker.session_id, "known-session")
                self.assertFalse(worker.stats["cleanupComplete"])
                self.assertTrue(worker.fatal)
                self.assertEqual(recorder.report["failureCount"], 1)
            finally:
                recorder.journal.close()

    def test_media_authorization_rejection_ends_worker(self):
        stop = threading.Event()
        with tempfile.TemporaryDirectory() as directory:
            recorder = soak.Recorder(Path(directory), 86400)
            worker = soak.StreamWorker({"id": 1, "deviceId": 1, "deviceChannel": 1}, self.client(stop), recorder, stop)
            try:
                with patch.object(worker, "receive", side_effect=rejected(403)) as receive:
                    worker.run()
                receive.assert_called_once()
                self.assertTrue(stop.is_set())
                self.assertTrue(worker.fatal)
                self.assertEqual(recorder.report["failureCount"], 1)
            finally:
                recorder.journal.close()

    def test_sampler_auth_rejection_stops_instead_of_resetting_login(self):
        stop = threading.Event()
        client = self.client(stop, True)
        with tempfile.TemporaryDirectory() as directory:
            recorder = soak.Recorder(Path(directory), 86400)
            sampler = soak.Sampler(client, None, "test-key", "test-key", recorder, stop)
            try:
                with patch.object(soak, "SERVICES", {}), patch.object(soak.shutil, "disk_usage", return_value=Mock(total=10**10, used=0, free=10**10)), \
                        patch.object(sampler, "adapter_health", return_value={}), \
                        patch.object(soak, "request_json", side_effect=rejected()), \
                        patch.object(soak, "opener", side_effect=soak.SoakError("test_network_unavailable")), \
                        patch.object(client, "logout") as logout:
                    sampler.sample()
                self.assertTrue(stop.is_set())
                self.assertTrue(sampler.fatal)
                logout.assert_not_called()
                self.assertEqual(recorder.report["failuresByCategory"]["platform_metrics_auth_control_http_401"], 1)
            finally:
                recorder.journal.close()

    def test_transient_network_failure_keeps_existing_session_retriable(self):
        client = self.client(authenticated=True)
        with patch.object(soak, "request_json", side_effect=[urllib.error.URLError("测试断网"), {"ok": True}]) as request:
            with self.assertRaises(urllib.error.URLError):
                client.call("GET", "/api/v2/dashboard")
            self.assertEqual(client.call("GET", "/api/v2/dashboard"), {"ok": True})
        self.assertFalse(client.stop.is_set())
        self.assertEqual(request.call_count, 2)
        self.assertTrue(all(call.args[1].endswith("/dashboard") for call in request.call_args_list))

    def test_runner_records_authentication_termination_as_failed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run_id = "20260908T000000Z-000000000000"
            directory = root / "runs" / run_id
            directory.mkdir(parents=True)
            (root / "v2-production.json").write_text(json.dumps({"adminPassword": "test-only"}), encoding="utf-8")
            recorder = soak.Recorder(directory, 86400)
            args = SimpleNamespace(run_id=run_id, hours=24, public_url=None, ca_file="test-ca", channels=None)
            values = {"PLATFORM_PUBLIC_URL": "https://example.test", "HIK_ADAPTER_INTERNAL_KEY": "test", "ZLM_API_SECRET": "test"}
            paths = []

            def server(context, url, *arguments, **keywords):
                paths.append(url)
                if url.endswith("/health"):
                    return {"service": "video-platform-api", "status": "ok"}
                raise rejected()

            try:
                with patch.object(soak, "STATE_DIR", root), patch.object(soak, "CONFIG_DIR", root), \
                        patch.object(soak, "Recorder", return_value=recorder), patch.object(recorder, "checkpoint"), \
                        patch.object(soak.signal, "signal"), patch.object(soak.ssl, "create_default_context"), \
                        patch.object(soak, "environment_values", return_value=values), \
                        patch.object(soak.Sampler, "adapter_health", return_value={}), patch.object(soak.Sampler, "sample"), \
                        patch.object(soak, "select_channels", return_value=[{"id": n, "deviceId": 1, "deviceChannel": n} for n in (1, 2)]), \
                        patch.object(soak, "request_json", side_effect=server):
                    result = soak.run_soak(args)
                self.assertEqual(result, 2)
                self.assertEqual(recorder.report["status"], "failed")
                self.assertFalse(recorder.report["passed"])
                self.assertEqual(recorder.report["failuresByCategory"]["stream_auth_login_http_401"], 1)
                self.assertEqual(sum(path.endswith("/auth/login") for path in paths), 1)
            finally:
                recorder.journal.close()


if __name__ == "__main__":
    unittest.main()
