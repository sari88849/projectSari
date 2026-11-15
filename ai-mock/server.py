"""HTTP front for the AI provider (Python standard library only).

  POST /generate   body: { "prompt": "...", "context": { ...metadata context from the backend... } }
                   200 : proposal JSON (shared/ai-response.schema.json)
  GET  /health     200 : { "status": "ok", "provider": "mock" }

Run:  python server.py [--host 127.0.0.1] [--port 5005] [--provider mock]
"""
import argparse
import json
import logging
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from ai_provider import create_provider

MAX_BODY_BYTES = 2 * 1024 * 1024
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("ai-mock")


def make_handler(provider):
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path.rstrip("/") == "/health":
                self._send(200, {"status": "ok", "provider": provider.name})
            else:
                self._send(404, {"error": "not found"})

        def do_POST(self):
            try:
                length = int(self.headers.get("Content-Length") or 0)
            except ValueError:
                length = -1
            if length < 0 or length > MAX_BODY_BYTES:
                self.close_connection = True
                self._send(400, {"error": "invalid body size"})
                return
            raw = self.rfile.read(length) if length else b""   # always consume the body before replying

            if self.path.rstrip("/") != "/generate":
                self._send(404, {"error": "not found"})
                return
            try:
                body = json.loads(raw.decode("utf-8"))
                prompt = body.get("prompt")
                context = body.get("context")
                if not isinstance(prompt, str) or not prompt.strip() or not isinstance(context, dict):
                    self._send(400, {"error": "'prompt' (string) and 'context' (object) are required"})
                    return
                result = provider.generate(prompt, context)
                log.info("prompt=%r -> status=%s, conditions=%d", prompt, result.get("status"), len(result.get("conditions", [])))
                self._send(200, result)
            except (ValueError, KeyError, TypeError) as ex:
                log.warning("bad request: %s", ex)
                self._send(400, {"error": "invalid request: {}".format(ex)})
            except Exception:  # never leak a stack trace to the caller
                log.exception("provider failure")
                self._send(500, {"error": "provider failure"})

        def _send(self, status, payload):
            data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, fmt, *args):  # route access logs through logging
            log.debug(fmt, *args)

    return Handler


def main():
    parser = argparse.ArgumentParser(description="AI mock service for the validation system")
    parser.add_argument("--host", default=os.environ.get("AI_MOCK_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("AI_MOCK_PORT", "5005")))
    parser.add_argument("--provider", default=os.environ.get("AI_PROVIDER", "mock"))
    args = parser.parse_args()

    provider = create_provider(args.provider)
    server = ThreadingHTTPServer((args.host, args.port), make_handler(provider))
    log.info("AI service (%s provider) listening on http://%s:%d", provider.name, args.host, args.port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
