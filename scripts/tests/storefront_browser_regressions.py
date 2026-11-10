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
    protocol_version = "HTTP/1.1"
    orders = {}
    products = None
    product_revision = 1
    preferences = {}
    sessions = {}
    profiles = {
        "buyer@example.invalid": {"subscriber_id": "00000000-0000-0000-0000-000000000001", "email": "buyer@example.invalid", "first_name": "Buyer", "last_name": "Profile", "mobile_number": "0831234567"},
        "other@example.invalid": {"subscriber_id": "00000000-0000-0000-0000-000000000002", "email": "other@example.invalid", "first_name": "Other", "last_name": "Account", "mobile_number": "0841234567"},
        "admin@example.invalid": {"subscriber_id": "00000000-0000-0000-0000-000000000003", "email": "admin@example.invalid", "first_name": "Local", "last_name": "Admin"},
    }

    def log_message(self, *_args):
        pass

    def respond(self, body, status=200, content_range=None):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        if content_range is not None:
            self.send_header("Content-Range", content_range)
        self.end_headers()
        self.wfile.write(data)

    def read_json(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            chunks = []
            while True:
                size = int(self.rfile.readline().split(b";", 1)[0], 16)
                if size == 0:
                    while self.rfile.readline().strip():
                        pass
                    break
                chunks.append(self.rfile.read(size))
                self.rfile.read(2)
            data = b"".join(chunks)
        else:
            data = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        return json.loads(data or b"null")

    def do_GET(self):
        parsed = urlparse(self.path)
        if parsed.path == "/rest/v1/store_orders":
            query = parse_qs(parsed.query)
            if "order_reference" in query:
                reference = query["order_reference"][0].removeprefix("eq.")
                self.respond([self.orders[reference]] if reference in self.orders else [])
            elif "order_id" in query:
                order_id = query["order_id"][0].removeprefix("eq.")
                self.respond([row for row in self.orders.values() if row["order_id"] == order_id])
            elif "customer_email" in query:
                email = query["customer_email"][0].removeprefix("eq.")
                self.respond([row for row in self.orders.values() if row.get("customer_email") == email])
            else:
                if self.server.fail_order_page.is_set():
                    self.server.fail_order_page.clear()
                    self.respond({"error": "Temporary fixture outage"}, 503)
                    return
                rows = sorted(self.orders.values(), key=lambda row: (row["created_at"], row["order_id"]), reverse=True)
                offset = int(query.get("offset", [0])[0])
                limit = int(query.get("limit", [25])[0])
                if offset > 0 and offset >= len(rows):
                    self.respond({"code": "PGRST103"}, 416, content_range=f"*/{len(rows)}")
                    return
                page = rows[offset:offset + limit]
                content_range = f"{offset}-{offset + len(page) - 1}/{len(rows)}" if page else f"*/{len(rows)}"
                if self.server.pause_order_page.is_set():
                    self.server.pause_order_page.clear()
                    self.server.order_page_paused.set()
                    self.server.release_order_page.wait(timeout=10)
                try:
                    self.respond(page, content_range=content_range)
                except (BrokenPipeError, ConnectionResetError):
                    pass  # A newer browser request can cancel this page load.
        elif parsed.path == "/rest/v1/subscribers":
            email = parse_qs(parsed.query).get("email", [""])[0].removeprefix("eq.")
            self.respond([self.profiles[email]] if email in self.profiles else [])
        elif parsed.path == "/rest/v1/store_products":
            # The checkout suites retain their established fallback catalogue.
            self.respond(self.products or [], 200 if self.products is not None else 404)
        elif parsed.path == "/rest/v1/public_content_cache_revisions":
            self.respond([{"revision": self.product_revision}] if self.products is not None else [])
        elif parsed.path == "/rest/v1/admin_users":
            if self.server.fail_admin_lookup.is_set():
                self.server.fail_admin_lookup.clear()
                self.respond({"error": "Temporary fixture outage"}, 503)
                return
            email = parse_qs(parsed.query).get("email", [""])[0].removeprefix("eq.")
            self.respond([{"email": email}] if email == "admin@example.invalid" and self.server.admin_access else [])
        elif parsed.path == "/rest/v1/store_customer_details":
            email = parse_qs(parsed.query).get("account_email", [""])[0].removeprefix("eq.")
            self.respond([self.preferences[email]] if email in self.preferences else [])
        elif parsed.path == "/rest/v1/auth_sessions":
            query = parse_qs(parsed.query)
            email = query.get("email", [""])[0].removeprefix("eq.")
            session_id = query.get("session_id", [""])[0].removeprefix("eq.")
            self.respond([row for row in self.sessions.values() if row["email"] == email and not row.get("revoked_at")
                          and (not session_id or row["session_id"] == session_id)])
        elif parsed.path == "/_test/preferences":
            self.respond(self.preferences)
        elif parsed.path == "/_test/orders":
            self.respond([{"reference": reference, "status": row["payment_status"],
                           "analytics_visitor_id": row.get("analytics_visitor_id"), "shipped_at": row.get("shipped_at")}
                          for reference, row in self.orders.items()])
        elif parsed.path == "/_test/order-page-paused":
            self.respond({"paused": self.server.order_page_paused.wait(timeout=5)})
        else:
            self.respond([])

    def do_POST(self):
        payload = self.read_json()
        path = urlparse(self.path).path
        if path == "/rest/v1/store_orders":
            row = payload[0]
            row["order_id"] = str(uuid4())
            self.orders[row["order_reference"]] = row
            self.respond([row])
        elif path == "/auth/v1/token":
            email = payload.get("email", "")
            if email in self.profiles and payload.get("password") == "Local-checkout-test-123!":
                self.respond({"user": {"email": email}})
            else:
                self.respond({"error": "invalid local test credentials"}, 400)
        elif path == "/rest/v1/subscribers":
            email = payload.get("email", "")
            if email in self.profiles:
                self.profiles[email].update(payload)
                self.respond([self.profiles[email]])
            else:
                self.respond([], 400)
        elif path == "/rest/v1/auth_sessions":
            for row in payload:
                self.sessions[row["session_id"]] = row
            self.respond([])
        elif path == "/rest/v1/store_customer_details":
            self.preferences[payload["account_email"]] = payload
            self.respond([])
        elif path == "/_test/paid" and payload["reference"] in self.orders:
            self.orders[payload["reference"]]["payment_status"] = "paid"
            self.respond({"paid": payload["reference"]})
        elif path == "/_test/fail-order-page":
            self.server.fail_order_page.set()
            self.respond({})
        elif path == "/_test/admin-access":
            self.server.admin_access = payload["enabled"]
            self.respond({})
        elif path == "/_test/fail-admin-lookup":
            self.server.fail_admin_lookup.set()
            self.respond({})
        elif path == "/_test/trim-orders":
            newest_first = sorted(self.orders, key=lambda reference: self.orders[reference]["created_at"], reverse=True)
            for reference in newest_first[:max(0, len(newest_first) - payload["remaining"])]:
                del self.orders[reference]
            self.respond({})
        elif path == "/_test/pause-order-page":
            self.orders[payload["reference"]]["customer_name"] = payload["customer_name"]
            self.server.order_page_paused.clear()
            self.server.release_order_page.clear()
            self.server.pause_order_page.set()
            self.respond({})
        elif path == "/_test/release-order-page":
            self.server.release_order_page.set()
            self.respond({})
        else:
            self.respond([], 404)

    def do_PATCH(self):
        payload = self.read_json() or {}
        parsed = urlparse(self.path)
        if parsed.path == "/rest/v1/auth_sessions":
            query = parse_qs(parsed.query)
            email = query.get("email", [""])[0].removeprefix("eq.")
            selected = query.get("session_id", [""])[0]
            ids = set(selected[4:-1].split(",")) if selected.startswith("in.(") else {selected.removeprefix("eq.")} if selected else None
            for row in self.sessions.values():
                if row["email"] == email and (ids is None or row["session_id"] in ids):
                    row.update(payload)
        elif parsed.path == "/rest/v1/store_products" and self.products is not None:
            product_id = parse_qs(parsed.query).get("store_product_id", [""])[0].removeprefix("eq.")
            for row in self.products:
                if row["store_product_id"] == product_id:
                    row.update(payload)
                    type(self).product_revision += 1
                    self.respond([row])
                    return
        elif parsed.path == "/rest/v1/store_orders":
            query = parse_qs(parsed.query)
            if query.get("payment_status") != ["eq.paid"] or query.get("shipped_at") != ["is.null"]:
                self.respond({"error": "Shipment must match a paid, unshipped order"}, 400)
                return
            order_id = query.get("order_id", [""])[0].removeprefix("eq.")
            for row in self.orders.values():
                if row["order_id"] == order_id and row["payment_status"] == "paid" and row.get("shipped_at") is None:
                    row["shipped_at"] = payload["shipped_at"]
                    self.respond([{"shipped_at": row["shipped_at"]}])
                    return
        self.respond([])


def free_port():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


class StorefrontBrowserTests(unittest.TestCase):
    def test_checkout_regressions(self):
        self.run_scenarios("core", 10)

    def test_account_checkout_regressions(self):
        self.run_scenarios("account", 4)

    def test_product_collections(self):
        self.run_scenarios("collections", 4)

    def test_winkel_analytics(self):
        self.run_scenarios("analytics", 2)

    def test_winkel_orders(self):
        self.run_scenarios("orders", 7)

    def run_scenarios(self, suite, expected_passed):
        # Separate app instances keep both suites inside the real checkout rate limit.
        OUTPUT.mkdir(parents=True, exist_ok=True)
        fixture = ThreadingHTTPServer(("127.0.0.1", 0), OrderFixture)
        fixture.admin_access = True
        fixture.fail_admin_lookup = threading.Event()
        fixture.fail_order_page = threading.Event()
        fixture.pause_order_page = threading.Event()
        fixture.order_page_paused = threading.Event()
        fixture.release_order_page = threading.Event()
        OrderFixture.orders = {}
        if suite == "orders":
            for number in range(26):
                reference = f"fixture-older-{number:02}"
                OrderFixture.orders[reference] = {
                    "order_id": str(uuid4()), "order_reference": reference, "customer_name": "Older Guest",
                    "total_price_zar": 100, "payment_status": "paid" if number == 0 else "cancelled",
                    "created_at": f"2026-01-{number + 1:02}T12:00:00Z",
                }
            OrderFixture.orders["fixture-older-00"].update(
                product_slug="long-product-name", product_name="LongProductName" * 9, quantity=1, unit_price_zar=100,
            )
        OrderFixture.preferences = {}
        OrderFixture.sessions = {}
        OrderFixture.product_revision = 1
        OrderFixture.products = [
            {"store_product_id": f"00000000-0000-0000-0000-{index:012d}",
             "slug": slug, "name": name, "description": "Plaaslike versamelingstoets",
             "image_path": "http://127.0.0.1:9/local-test-image.png", "alt_text": name, "unit_price_zar": price,
             "sort_order": index, "is_enabled": enabled, "waives_delivery_fee": free,
             "collection_slug": collection}
            for index, (slug, name, price, collection, free, enabled) in enumerate([
                ("toets-boek", "Toetsboek", 180, "kinderboeke", False, True),
                ("toets-tjommie", "Toetstjommie", 150, "storietjommies", False, True),
                ("toets-pakket", "Toetspakket", 500, "pakkette", True, True),
                ("af-boek", "Afboek", 100, "kinderboeke", False, False),
            ], 1)
        ] if suite == "collections" else None
        threading.Thread(target=fixture.serve_forever, daemon=True).start()
        fixture_url = f"http://127.0.0.1:{fixture.server_port}"
        base_url = f"http://127.0.0.1:{free_port()}"
        session = f"store-regression-{uuid4().hex[:10]}"
        cli = ["npx", "--yes", "--package", "@playwright/cli", "playwright-cli", f"-s={session}"]
        env = {**os.environ, **{
            "ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": base_url,
            "Supabase__Url": fixture_url, "Supabase__SecretKey": "local-test-only",
            "Supabase__PublishableKey": "local-test-only", "BackgroundJobs__Enabled": "false",
            "Resend__ApiKey": "", "RESEND_API_KEY": "", "MobilePush__Enabled": "false",
            "Paystack__SecretKey": "", "Paystack__InitializeUrl": fixture_url + "/_disabled/paystack",
            "Paystack__VerifyUrl": fixture_url + "/_disabled/paystack", "Paystack__PublicBaseUrl": base_url,
            "Geoapify__ApiKey": "", "PostHog__ProjectApiKey": "", "POSTHOG_PROJECT_API_KEY": "",
            "POSTHOG_API_KEY": "", "PostHog__HostUrl": "", "POSTHOG_HOST_URL": "", "POSTHOG_HOST": "",
        }}
        if suite == "analytics":
            env.update({"PostHog__ProjectApiKey": "phc_local_winkel_test", "PostHog__HostUrl": fixture_url})
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
                opened = subprocess.run(cli + ["open", base_url + "/winkel?__fixture=" + fixture_url + "&__suite=" + suite],
                                        cwd=OUTPUT, capture_output=True, text=True, timeout=60)
                self.assertEqual(opened.returncode, 0, opened.stdout + opened.stderr)
                result = subprocess.run(cli + ["run-code", "--filename", str(ROOT / "scripts/tests/storefront-browser-regressions.js")],
                                        cwd=OUTPUT, capture_output=True, text=True, timeout=180)
                (OUTPUT / f"results-{suite}.log").write_text(result.stdout + result.stderr)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertNotIn("### Error", result.stdout, result.stdout)
                self.assertIn('"failed":0', result.stdout.replace(" ", ""), result.stdout)
                self.assertIn(f'"passed":{expected_passed}', result.stdout.replace(" ", ""), result.stdout)
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
