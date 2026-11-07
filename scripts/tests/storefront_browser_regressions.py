"""Run the real store against an in-memory order API; never use hosted credentials.

Prerequisites: dotnet test Shink.Tests/Shink.Tests.csproj, Node/npm, and a
Playwright CLI browser. Run: python3 scripts/tests/storefront_browser_regressions.py
"""

import json
import os
from pathlib import Path
import socket
import subprocess
import threading
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.error import URLError
from urllib.parse import parse_qs, urlparse
from urllib.request import urlopen
from uuid import uuid4


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "output/storefront-upgrade/browser-tests"


class OrderFixture(BaseHTTPRequestHandler):
    orders = {}

    def log_message(self, *_args):
        pass

    def respond(self, body, status=200):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        parsed = urlparse(self.path)
        if parsed.path == "/rest/v1/store_orders":
            reference = parse_qs(parsed.query).get("order_reference", [""])[0].removeprefix("eq.")
            self.respond([self.orders[reference]] if reference in self.orders else [])
        elif parsed.path == "/_test/orders":
            self.respond([{"reference": reference, "status": row["payment_status"]}
                          for reference, row in self.orders.items()])
        else:
            self.respond([])

    def do_POST(self):
        payload = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))) or b"null")
        path = urlparse(self.path).path
        if path == "/rest/v1/store_orders":
            row = payload[0]
            row["order_id"] = str(uuid4())
            self.orders[row["order_reference"]] = row
            self.respond([row])
        elif path == "/_test/paid" and payload["reference"] in self.orders:
            self.orders[payload["reference"]]["payment_status"] = "paid"
            self.respond({"paid": payload["reference"]})
        else:
            self.respond([], 404)


def free_port():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


class StorefrontBrowserTests(unittest.TestCase):
    def test_checkout_regressions(self):
        OUTPUT.mkdir(parents=True, exist_ok=True)
        fixture = ThreadingHTTPServer(("127.0.0.1", 0), OrderFixture)
        OrderFixture.orders = {}
        threading.Thread(target=fixture.serve_forever, daemon=True).start()
        fixture_url = f"http://127.0.0.1:{fixture.server_port}"
        base_url = f"http://127.0.0.1:{free_port()}"
        session = f"store-regression-{uuid4().hex[:10]}"
        cli = ["npx", "--yes", "--package", "@playwright/cli", "playwright-cli", f"-s={session}"]
        env = {**os.environ, **{
            "ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": base_url,
            "Supabase__Url": fixture_url, "Supabase__SecretKey": "local-test-only",
            "Supabase__PublishableKey": "", "BackgroundJobs__Enabled": "false",
            "Resend__ApiKey": "", "RESEND_API_KEY": "", "MobilePush__Enabled": "false",
            "Paystack__SecretKey": "", "Paystack__InitializeUrl": fixture_url + "/_disabled/paystack",
            "Paystack__VerifyUrl": fixture_url + "/_disabled/paystack", "Paystack__PublicBaseUrl": base_url,
            "Geoapify__ApiKey": "", "PostHog__ProjectApiKey": "", "POSTHOG_PROJECT_API_KEY": "",
            "POSTHOG_API_KEY": "", "PostHog__HostUrl": "", "POSTHOG_HOST_URL": "", "POSTHOG_HOST": "",
        }}
        with (OUTPUT / "app.log").open("w") as log:
            app = subprocess.Popen(["dotnet", "run", "--project", str(ROOT / "Shink/Shink.csproj"),
                                    "--no-build", "--no-launch-profile"], cwd=ROOT, env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 30
                while True:
                    self.assertIsNone(app.poll(), "The local store exited; see output/storefront-upgrade/browser-tests/app.log")
                    try:
                        with urlopen(base_url + "/winkel", timeout=1) as response:
                            if response.status == 200:
                                break
                    except (URLError, TimeoutError):
                        pass
                    self.assertLess(time.monotonic(), deadline, "Local store startup timed out.")
                    time.sleep(0.1)
                opened = subprocess.run(cli + ["open", base_url + "/winkel?__fixture=" + fixture_url],
                                        cwd=OUTPUT, capture_output=True, text=True, timeout=60)
                self.assertEqual(opened.returncode, 0, opened.stdout + opened.stderr)
                result = subprocess.run(cli + ["run-code", "--filename", str(ROOT / "scripts/tests/storefront-browser-regressions.js")],
                                        cwd=OUTPUT, capture_output=True, text=True, timeout=180)
                (OUTPUT / "results.log").write_text(result.stdout + result.stderr)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertNotIn("### Error", result.stdout, result.stdout)
                self.assertIn('"failed":0', result.stdout.replace(" ", ""), result.stdout)
                print(result.stdout)
            finally:
                subprocess.run(cli + ["close"], cwd=OUTPUT, capture_output=True, timeout=30)
                app.terminate()
                try:
                    app.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    app.kill()
                    app.wait(timeout=5)
                fixture.shutdown()
                fixture.server_close()


if __name__ == "__main__":
    unittest.main()
