"""HTTP contract tests: starts the server in-process on a free port."""
import json
import os
import sys
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from mock_provider import MockAiProvider  # noqa: E402
from server import make_handler  # noqa: E402

with open(os.path.join(HERE, "fixtures", "context.json"), encoding="utf-8") as f:
    CONTEXT = json.load(f)


class ServerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(MockAiProvider()))
        cls.base = "http://127.0.0.1:{}".format(cls.server.server_address[1])
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()

    def post(self, path, payload):
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(self.base + path, data=data, headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request) as response:
                return response.status, json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as ex:
            return ex.code, json.loads(ex.read().decode("utf-8"))

    def test_health(self):
        with urllib.request.urlopen(self.base + "/health") as response:
            self.assertEqual(json.loads(response.read().decode("utf-8"))["status"], "ok")

    def test_generate_returns_proposal_with_hebrew_text(self):
        status, body = self.post("/generate", {"prompt": "עובדים מתחת לגיל 18", "context": CONTEXT})
        self.assertEqual(status, 200)
        self.assertEqual(body["status"], "ok")
        self.assertEqual(body["description"], "עובדים מתחת לגיל 18")

    def test_missing_prompt_is_400(self):
        status, body = self.post("/generate", {"context": CONTEXT})
        self.assertEqual(status, 400)
        self.assertIn("error", body)

    def test_unknown_path_is_404(self):
        status, _ = self.post("/other", {})
        self.assertEqual(status, 404)


if __name__ == "__main__":
    unittest.main()
