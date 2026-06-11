#!/usr/bin/env bash
# End-to-end tests for sitesearcher: crawls two local fixture sites and checks
# matching, exporting, prompting, exit codes, Ctrl+C handling and --max-pages.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/bin/Release/net10.0/sitesearcher"
FIXTURE_PORT="${FIXTURE_PORT:-8123}"
INFINITE_PORT="${INFINITE_PORT:-8124}"
BASE="http://127.0.0.1:$FIXTURE_PORT"

cleanup() { kill "${SRV:-}" "${INF:-}" 2>/dev/null || true; }
trap cleanup EXIT

fail() { echo "FAILED: $1" >&2; exit 1; }

echo "== build =="
dotnet build -c Release "$ROOT" --nologo -v q

echo "== start fixture server on :$FIXTURE_PORT =="
python3 -m http.server "$FIXTURE_PORT" --bind 127.0.0.1 --directory "$ROOT/tests/fixture" >/dev/null 2>&1 &
SRV=$!
for _ in $(seq 1 50); do
  curl -fs "$BASE/index.html" >/dev/null 2>&1 && break
  sleep 0.1
done

echo "== 1. argument mode + --output export =="
"$BIN" "$BASE/index.html" wombat -o /tmp/ss-out.txt >/tmp/ss-console.txt 2>&1
grep -qxF "$BASE/index.html"      /tmp/ss-out.txt || fail "index.html missing from export"
grep -qxF "$BASE/contact.html"    /tmp/ss-out.txt || fail "contact.html (uppercase match) missing from export"
grep -qxF "$BASE/decoy.html"      /tmp/ss-out.txt || fail "decoy.html (raw-HTML match) missing from export"
grep -qxF "$BASE/blog/post1.html" /tmp/ss-out.txt || fail "blog/post1.html missing from export"
[ "$(wc -l </tmp/ss-out.txt)" -eq 4 ] || fail "expected exactly 4 exported URLs"
grep -qE 'about\.html|post2|notes\.txt|external' /tmp/ss-out.txt && fail "non-matching URL leaked into export"
grep -qF 'Found 4 page(s) containing "wombat"' /tmp/ss-console.txt || fail "summary line missing"

echo "== 2. interactive prompts via stdin =="
printf '%s\nwombat\n' "$BASE/index.html" | "$BIN" >/tmp/ss-interactive.txt 2>&1
grep -qF "$BASE/contact.html" /tmp/ss-interactive.txt || fail "prompt mode did not find contact.html"
printf 'not a url\n%s\nwombat\n' "$BASE/index.html" | "$BIN" >/tmp/ss-reprompt.txt 2>&1
grep -qF "$BASE/contact.html" /tmp/ss-reprompt.txt || fail "re-prompt after invalid URL did not work"
grep -qi "valid" /tmp/ss-reprompt.txt || fail "invalid-URL message missing"

echo "== 3. exit codes =="
set +e
"$BIN" ':::bad' wombat >/dev/null 2>&1
[ $? -eq 2 ] || fail "invalid URL argument should exit 2"
"$BIN" "http://127.0.0.1:9/" wombat >/dev/null 2>&1
[ $? -eq 1 ] || fail "unreachable site should exit 1"
"$BIN" "$BASE/index.html" zzznosuchword >/dev/null 2>&1
[ $? -eq 0 ] || fail "zero matches should still exit 0"
"$BIN" </dev/null >/dev/null 2>&1
[ $? -eq 2 ] || fail "EOF during prompts should exit 2"
set -e

echo "== 4. Ctrl+C (SIGINT) shows partial results =="
python3 "$ROOT/tests/infinite_site.py" "$INFINITE_PORT" &
INF=$!
for _ in $(seq 1 50); do
  curl -fs "http://127.0.0.1:$INFINITE_PORT/page/0" >/dev/null 2>&1 && break
  sleep 0.1
done
# Run in the foreground under `timeout`: a `&` background job would get SIGINT
# set to "ignored", which the .NET runtime honours, so Ctrl+C would never arrive.
rc=0
timeout --signal=INT --kill-after=30 --preserve-status 2 \
  "$BIN" "http://127.0.0.1:$INFINITE_PORT/page/0" wombat </dev/null >/tmp/ss-sigint.txt 2>&1 || rc=$?
[ "$rc" -eq 0 ] || fail "SIGINT run should exit 0, got $rc"
grep -qi "interrupted" /tmp/ss-sigint.txt || fail "interrupted notice missing"
grep -qF "/page/0" /tmp/ss-sigint.txt || fail "partial results missing after SIGINT"

echo "== 5. --max-pages cap =="
"$BIN" "$BASE/index.html" wombat --max-pages 2 >/tmp/ss-cap.txt 2>&1
grep -qi "limit" /tmp/ss-cap.txt || fail "--max-pages limit notice missing"

echo "ALL E2E CHECKS PASSED"
