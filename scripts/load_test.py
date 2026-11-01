#!/usr/bin/env python3
"""
Load tester for Pixous Document Intelligence.

What it does:
  1. Logs in (registering the test account first run) to get a portal JWT.
  2. Uploads one PDF synchronously to get a jobId with a real stitched envelope.
  3. Fires concurrent requests at the agent tools (ask / summarize / validate-rules)
     for N seconds with C concurrent workers, and reports latency/throughput/error stats.

Usage:
  pip install requests
  python scripts/load_test.py --base-url http://localhost:8080 --pdf output/documents/DOC-20261028-18ACD68D.pdf
  python scripts/load_test.py --concurrency 20 --duration 30 --tool ask
  python scripts/load_test.py --tool all --concurrency 10 --duration 15

While it runs, watch container RAM/CPU in another terminal:
  docker stats pixous_api
"""

import argparse
import random
import statistics
import sys
import time
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass, field

import requests

QUESTIONS = [
    "What is the total payment amount recorded in this document?",
    "Which organization made the payment?",
    "What is the reconciliation status of the payments?",
    "Are there any unallocated amounts? If so, how much?",
    "Summarize the payment methods used.",
]

VALIDATE_RULES_PAYLOAD = {
    "rules": [
        {"ruleId": "R1", "description": "Document identifies a customer organization", "category": "Completeness", "maxMarks": 10, "isKnockout": False},
        {"ruleId": "R2", "description": "Payment amount is a positive number", "category": "Validity", "maxMarks": 10, "isKnockout": True},
    ],
}


@dataclass
class Result:
    endpoint: str
    status: int
    elapsed_ms: float
    ok: bool
    error: str = ""


@dataclass
class Stats:
    results: list = field(default_factory=list)

    def add(self, r: Result):
        self.results.append(r)

    def report(self, wall_seconds: float):
        if not self.results:
            print("No requests completed.")
            return
        latencies = [r.elapsed_ms for r in self.results]
        ok = [r for r in self.results if r.ok]
        failed = [r for r in self.results if not r.ok]
        status_counts = {}
        for r in self.results:
            status_counts[r.status] = status_counts.get(r.status, 0) + 1

        latencies.sort()
        n = len(latencies)

        def pct(p):
            idx = min(n - 1, int(n * p))
            return latencies[idx]

        print("\n" + "=" * 60)
        print(" LOAD TEST REPORT")
        print("=" * 60)
        print(f"Total requests:     {n}")
        print(f"Successful:         {len(ok)} ({100*len(ok)/n:.1f}%)")
        print(f"Failed:             {len(failed)} ({100*len(failed)/n:.1f}%)")
        print(f"Wall clock time:    {wall_seconds:.2f}s")
        print(f"Throughput:         {n / wall_seconds:.2f} req/s")
        print(f"\nLatency (ms):")
        print(f"  min:    {min(latencies):.1f}")
        print(f"  avg:    {statistics.mean(latencies):.1f}")
        print(f"  median: {statistics.median(latencies):.1f}")
        print(f"  p95:    {pct(0.95):.1f}")
        print(f"  p99:    {pct(0.99):.1f}")
        print(f"  max:    {max(latencies):.1f}")
        print(f"\nStatus code breakdown:")
        for code, count in sorted(status_counts.items()):
            print(f"  {code}: {count}")
        if failed:
            print(f"\nSample errors (up to 5):")
            for r in failed[:5]:
                print(f"  [{r.endpoint}] status={r.status} error={r.error[:200]}")
        print("=" * 60)


def login_or_register(base_url: str, email: str, password: str) -> str:
    session = requests.Session()
    resp = session.post(f"{base_url}/api/auth/login", json={"email": email, "password": password}, timeout=15)
    if resp.status_code == 200:
        return resp.json()["token"]

    resp = session.post(
        f"{base_url}/api/auth/register",
        json={"email": email, "password": password, "name": "Load Test User"},
        timeout=15,
    )
    resp.raise_for_status()
    return resp.json()["token"]


def upload_document(base_url: str, token: str, pdf_path: str) -> str:
    headers = {"Authorization": f"Bearer {token}"}
    with open(pdf_path, "rb") as f:
        files = {"File": (pdf_path.split("/")[-1], f, "application/pdf")}
        data = {"Tier": "fast", "Mode": "schema"}
        print(f"Uploading {pdf_path} to {base_url}/api/documents/process ...")
        resp = requests.post(f"{base_url}/api/documents/process", headers=headers, files=files, data=data, timeout=300)
    resp.raise_for_status()
    body = resp.json()
    job_id = body.get("jobId") or body.get("JobId")
    if not job_id:
        raise RuntimeError(f"No jobId in process response: {body}")
    print(f"Document processed. jobId={job_id}")
    return job_id


def call_ask(base_url: str, token: str, job_id: str) -> Result:
    question = random.choice(QUESTIONS)
    headers = {"Authorization": f"Bearer {token}"}
    start = time.perf_counter()
    try:
        resp = requests.post(
            f"{base_url}/api/agent/jobs/{job_id}/ask",
            headers=headers,
            json={"question": question},
            timeout=60,
        )
        elapsed = (time.perf_counter() - start) * 1000
        return Result("ask", resp.status_code, elapsed, resp.ok, "" if resp.ok else resp.text)
    except requests.RequestException as e:
        elapsed = (time.perf_counter() - start) * 1000
        return Result("ask", 0, elapsed, False, str(e))


def call_summarize(base_url: str, token: str, job_id: str) -> Result:
    headers = {"Authorization": f"Bearer {token}"}
    start = time.perf_counter()
    try:
        resp = requests.post(
            f"{base_url}/api/agent/jobs/{job_id}/summarize",
            headers=headers,
            json={"focusArea": "payments and reconciliation"},
            timeout=60,
        )
        elapsed = (time.perf_counter() - start) * 1000
        return Result("summarize", resp.status_code, elapsed, resp.ok, "" if resp.ok else resp.text)
    except requests.RequestException as e:
        elapsed = (time.perf_counter() - start) * 1000
        return Result("summarize", 0, elapsed, False, str(e))


def call_validate(base_url: str, token: str, job_id: str) -> Result:
    headers = {"Authorization": f"Bearer {token}"}
    start = time.perf_counter()
    try:
        resp = requests.post(
            f"{base_url}/api/agent/jobs/{job_id}/validate-rules",
            headers=headers,
            json=VALIDATE_RULES_PAYLOAD,
            timeout=60,
        )
        elapsed = (time.perf_counter() - start) * 1000
        return Result("validate-rules", resp.status_code, elapsed, resp.ok, "" if resp.ok else resp.text)
    except requests.RequestException as e:
        elapsed = (time.perf_counter() - start) * 1000
        return Result("validate-rules", 0, elapsed, False, str(e))


TOOL_FUNCS = {
    "ask": call_ask,
    "summarize": call_summarize,
    "validate-rules": call_validate,
}


def worker_loop(stop_at: float, base_url: str, token: str, job_id: str, tools: list) -> list:
    out = []
    while time.perf_counter() < stop_at:
        tool = random.choice(tools)
        fn = TOOL_FUNCS[tool]
        out.append(fn(base_url, token, job_id))
    return out


def main():
    parser = argparse.ArgumentParser(description="Load test the Pixous Document Intelligence API")
    parser.add_argument("--base-url", default="http://localhost:8080", help="API base URL")
    parser.add_argument("--pdf", default="output/documents/DOC-20261028-18ACD68D.pdf", help="PDF to upload for the test job")
    parser.add_argument("--job-id", default=None, help="Reuse an existing jobId instead of uploading")
    parser.add_argument("--email", default=None, help="Test account email (random by default)")
    parser.add_argument("--password", default="LoadTest123!", help="Test account password")
    parser.add_argument("--concurrency", type=int, default=10, help="Number of concurrent workers")
    parser.add_argument("--duration", type=int, default=20, help="Test duration in seconds")
    parser.add_argument(
        "--tool",
        choices=["ask", "summarize", "validate-rules", "all"],
        default="all",
        help="Which agent tool(s) to hammer",
    )
    args = parser.parse_args()

    email = args.email or f"loadtest-{uuid.uuid4().hex[:8]}@example.com"
    print(f"Using account: {email}")

    token = login_or_register(args.base_url, email, args.password)
    print("Authenticated.")

    job_id = args.job_id or upload_document(args.base_url, token, args.pdf)

    tools = list(TOOL_FUNCS.keys()) if args.tool == "all" else [args.tool]
    print(f"\nStarting load test: concurrency={args.concurrency}, duration={args.duration}s, tools={tools}")
    print("Watch RAM/CPU with: docker stats pixous_api\n")

    stats = Stats()
    stop_at = time.perf_counter() + args.duration
    wall_start = time.perf_counter()

    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [
            pool.submit(worker_loop, stop_at, args.base_url, token, job_id, tools)
            for _ in range(args.concurrency)
        ]
        for fut in as_completed(futures):
            for r in fut.result():
                stats.add(r)

    wall_elapsed = time.perf_counter() - wall_start
    stats.report(wall_elapsed)


if __name__ == "__main__":
    try:
        main()
    except requests.RequestException as e:
        print(f"HTTP error: {e}", file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        print("\nInterrupted.")
        sys.exit(130)
