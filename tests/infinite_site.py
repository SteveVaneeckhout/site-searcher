#!/usr/bin/env python3
"""Endless fixture site for the Ctrl+C / unlimited-crawl test.

Every /page/N links to /page/2N+1 and /page/2N+2 (an infinite binary tree),
and even-numbered pages contain the keyword "wombat". The crawl can never
finish on its own, so the only way to stop it is cancellation.
"""
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

KEYWORD = "wombat"


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            n = int(self.path.rstrip("/").split("/")[-1])
        except ValueError:
            n = 0
        word = KEYWORD if n % 2 == 0 else "nothing"
        html = (
            f"<html><body><p>Page {n} talks about {word}.</p>"
            f'<a href="/page/{2 * n + 1}">Left</a> '
            f'<a href="/page/{2 * n + 2}">Right</a></body></html>'
        )
        body = html.encode()
        try:
            self.send_response(200)
            self.send_header("Content-Type", "text/html")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass  # crawler was interrupted mid-request; not an error

    def log_message(self, *args):
        pass  # keep the test output clean


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8124
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
