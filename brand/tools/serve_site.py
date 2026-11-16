"""Serves site/ the way GitHub Pages will: at /frosty-neon-studio/, with 404.html for
missing pages. Local only (127.0.0.1).

    python brand/tools/serve_site.py [port]     then open http://127.0.0.1:8000/frosty-neon-studio/
"""
import http.server
import os
import sys

SITE = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "site")
PREFIX = "/frosty-neon-studio"


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=SITE, **k)

    def translate_path(self, path):
        if path.startswith(PREFIX):
            path = path[len(PREFIX):] or "/"
        else:
            path = "/__missing__"
        return super().translate_path(path)

    def send_error(self, code, message=None, explain=None):
        if code == 404:
            body = open(os.path.join(SITE, "404.html"), "rb").read()
            self.send_response(404)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        else:
            super().send_error(code, message, explain)


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
    print(f"http://127.0.0.1:{port}{PREFIX}/")
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
