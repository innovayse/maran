#!/usr/bin/env bash
# `maran e2e-real` — the REAL-STACK browser lane (issue #519): a real Chromium against a panel and
# agent that are really running, with no stub anywhere, reaching every screen by clicking.
#
# Why it exists. The everyday suite (`maran test spa`) stubs every API route, so it is blind to a
# backend or agent failure (#498, a 500 from the agent's chmod), to an authorization mismatch
# between the SPA and the panel (#513, #515), to a stub that drifted from the API, and to a flow
# that needs several real steps (#514). A click-through by hand found fifteen defects in one pass
# that the suite never could. This lane is that pass, repeatable: journeys per role in
# frontend/e2e/real/, guarded by frontend/e2e/fixtures/network-guard.ts, which fails a test on a
# 4xx/5xx the journey did not announce, a console error, a body that disagrees with
# docs/api/openapi-baseline.json, and English chrome on a Russian or Armenian page.
#
# It needs a panel it did not start. That is deliberate: a deploy-local host (`maran deploy-local`,
# docs/operations/deploy-local.md) or an installer-polygon panel published on a port
# (`maran installer --publish`) is the only honest subject, and a hand-assembled backend on a
# runner would be a gate nobody could show observing anything (.github/workflows/nightly.yml says
# the same of the golden path). So the panel and four logins come in through the environment:
#
#   MARAN_REAL_URL                                       https://host:port of the running panel
#   MARAN_REAL_ADMIN_USERNAME / _PASSWORD                an administrator
#   MARAN_REAL_RESELLER_USERNAME / _PASSWORD             a reseller who has accepted the invitation
#   MARAN_REAL_CUSTOMER_USERNAME / _PASSWORD             a customer owner
#
# THREE ANSWERS, not two (rules/testing.md, scripts/lib/suite.sh):
#
#   E2E-REAL VERDICT: OK            exit 0   every journey ran and passed, and none was missing
#   E2E-REAL VERDICT: FAILED        exit 1   the panel was measured and a journey found a defect
#   E2E-REAL VERDICT: DID NOT RUN   exit 2   nothing was measured: no toolchain, no panel, no login
#
# A missing login, an unset URL and a panel that does not answer are refusals and not failures:
# "this machine could not look" must never read as "the product is broken", and must never read as
# a pass either. The verdict is the printed line plus the counted tests, never an exit status:
# Playwright exits non-zero for a run that collected nothing, but "12 passed" against a run that
# listed 12 is the line that says every journey executed.
#
# Usage:
#   maran e2e-real [--url URL]
#
# UNOBSERVED HERE: the lane signs in with the logins it is given and cannot tell that they are the
# right ones; a reseller login that was never invited signs in as nothing and fails the journey.
# It also cannot see a panel it was not pointed at: one host, one language set (en, ru, hy), one
# browser (Chromium).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

usage() {
  echo "usage: maran e2e-real [--url https://host:port]" >&2
  echo "       the panel and the role logins come from MARAN_REAL_* (see the header of this script)" >&2
  exit 2
}

panel_url="${MARAN_REAL_URL:-}"
while [ $# -gt 0 ]; do
  case "$1" in
    --url) panel_url="${2:-}"; shift $(( $# < 2 ? $# : 2 )) ;;
    -h|--help) usage ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

PREFIX="E2E-REAL VERDICT"

# The fewest tests the lane may collect. Re-derive with:
#   grep -cE '^test\(|^  test\(' frontend/e2e/real/*.spec.ts   (the translated-pages loop counts once
#   per language, so add one)
# A run that lists fewer lost a journey file to a rename or a bad import, which exits 0 on the
# remaining ones and is the false pass this number exists to refuse.
MINIMUM_TESTS=12

refuse() {
  suite_did_not_run "$PREFIX" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}

# Toolchain first, before anything that could kill the script with no output (rules/testing.md).
if ! suite_require_toolchain spa "$root"; then
  refuse "the SPA toolchain is incomplete (see the REFUSED lines above); no browser opened a panel."
fi
command -v curl >/dev/null 2>&1 || refuse "curl is not on PATH, so the panel's reachability cannot be proved before the browser starts."

[ -n "$panel_url" ] || refuse "MARAN_REAL_URL is unset (and no --url was given), so there is no running panel to open."
case "$panel_url" in
  http://*|https://*) ;;
  *) refuse "the panel address '$panel_url' is not an http(s) URL." ;;
esac

# Every login the journeys sign in with. Named, not looped over a pattern, so the refusal can say
# exactly which one is absent. Values are never printed.
for variable in \
  MARAN_REAL_ADMIN_USERNAME MARAN_REAL_ADMIN_PASSWORD \
  MARAN_REAL_RESELLER_USERNAME MARAN_REAL_RESELLER_PASSWORD \
  MARAN_REAL_CUSTOMER_USERNAME MARAN_REAL_CUSTOMER_PASSWORD; do
  [ -n "${!variable:-}" ] || refuse "$variable is unset, so a role the lane signs in as has no login."
done

# The panel must answer before a browser is started against it; -k because a deployed dev panel's
# certificate is self-signed by design, which is not what this lane measures.
if ! curl -ksS --max-time 20 -o /dev/null "${panel_url%/}/health"; then
  refuse "the panel at ${panel_url} did not answer /health, so no journey could be driven."
fi

log="$(mktemp "${TMPDIR:-/tmp}/maran-e2e-real.XXXXXX")"
trap 'rm -f "$log"' EXIT

export MARAN_REAL_URL="${panel_url%/}"

listing_status=0
(cd "$root/frontend" && npx playwright test -c playwright.real.config.ts --list) >"$log" 2>&1 || listing_status=$?
listed="$(sed -nE 's/^Total: ([0-9]+) tests? in [0-9]+ files?.*/\1/p' "$log" | tail -1)"
if [ "$listing_status" -ne 0 ] || [ -z "$listed" ]; then
  cat "$log" >&2
  refuse "the lane could not list its journeys (status ${listing_status}), so nothing was run."
fi
if [ "$listed" -lt "$MINIMUM_TESTS" ]; then
  echo "$PREFIX: FAILED — the lane lists ${listed} journeys, fewer than the ${MINIMUM_TESTS} it holds."
  echo "  A journey file stopped being collected (rename, bad import, testMatch). Nothing was run."
  exit 1
fi

run_status=0
(cd "$root/frontend" && npx playwright test -c playwright.real.config.ts) 2>&1 | tee "$log" >&2 || run_status=${PIPESTATUS[0]}

passed="$(sed -nE 's/^[[:space:]]*([0-9]+) passed.*/\1/p' "$log" | tail -1)"
failed="$(sed -nE 's/^[[:space:]]*([0-9]+) failed.*/\1/p' "$log" | tail -1)"
passed="${passed:-0}"
failed="${failed:-0}"

if [ "$failed" -gt 0 ]; then
  echo "$PREFIX: FAILED — ${failed} journey(s) failed, ${passed} passed, ${listed} listed."
  grep -aE '^[[:space:]]+[0-9]+\) ' "$log" | head -20 || true
  exit 1
fi
if [ "$passed" -ne "$listed" ]; then
  echo "$PREFIX: FAILED — ${passed} journeys passed but ${listed} were listed; the rest did not run."
  exit 1
fi
if [ "$run_status" -ne 0 ]; then
  echo "$PREFIX: FAILED — the counts agree (${passed} of ${listed}) but the runner exited ${run_status}."
  echo "  The two disagree, so neither is evidence."
  exit 1
fi

echo "$PREFIX: OK — ${passed} of ${listed} journeys passed against ${MARAN_REAL_URL}."
suite_note "the logins are taken on trust; one host, one browser (Chromium), three languages."
exit 0
