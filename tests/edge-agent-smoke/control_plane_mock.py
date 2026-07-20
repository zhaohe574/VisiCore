import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class ControlPlaneHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        return

    def do_POST(self):
        if self.path == "/api/v1/edge-agents/enroll":
            payload = json.loads(self.read_request_body() or b"{}")
            public_key = payload.get("publicKey") or payload.get("PublicKey") or {}
            agent_id = public_key.get("agentId") or public_key.get("AgentId")
            if not agent_id:
                self.send_error(400)
                return
            self.respond({
                "agentId": agent_id,
                "workerId": "00000000-0000-0000-0000-000000000001",
                "workerToken": "smoke-worker-token-0123456789012345",
                "configurationVersion": "smoke-v1"
            })
            return
        self.respond({})

    def do_PUT(self):
        self.respond({})

    def do_GET(self):
        if self.path.endswith("/configuration"):
            self.respond({
                "version": "smoke-v1",
                "configurationJson": json.dumps({
                    "schemaVersion": 1,
                    "inventorySyncIntervalSeconds": 60,
                    "clockSyncIntervalSeconds": 300,
                    "onvifEnabled": True,
                    "directRtspEnabled": True
                }),
                "status": "available"
            })
            return
        if self.path.endswith("/credentials") or self.path.endswith("/operations") or self.path.endswith("/assignments"):
            self.respond([])
            return
        self.respond({})

    def respond(self, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def read_request_body(self):
        if self.headers.get("Transfer-Encoding", "").lower() != "chunked":
            return self.rfile.read(int(self.headers.get("Content-Length", "0")))
        chunks = []
        while True:
            chunk_length = int(self.rfile.readline().strip(), 16)
            if chunk_length == 0:
                self.rfile.readline()
                return b"".join(chunks)
            chunks.append(self.rfile.read(chunk_length))
            self.rfile.read(2)


ThreadingHTTPServer(("0.0.0.0", 18080), ControlPlaneHandler).serve_forever()
