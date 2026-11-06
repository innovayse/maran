#!/usr/bin/env bash
# `maran openapi` — the OpenAPI document of the panel's HTTP API, held against a committed baseline.
#
# The document is produced by the panel itself (never served at runtime: rules/security.md item 10
# forbids a new surface), compared with docs/api/openapi-baseline.json by openapi-diff.mjs, and the
# verdict is whether a client written against the baseline would still work. Additive changes pass;
# a removed endpoint, a removed or retyped response property, a new required request member and the
# rest of the list in openapi-diff.mjs fail. It is the gate `maran api` is for the SPA, aimed at the
# integrators who consume the provisioning API.
#
#   maran openapi             check (the default)
#   maran openapi --accept    record the generated document as the new baseline (never run in CI)
#   maran openapi selftest    run only the planted controls; needs node and nothing else
#
# THREE ANSWERS, the same vocabulary as `maran test` and `maran drift` (scripts/lib/suite.sh):
#
#   OPENAPI VERDICT: OK            exit 0   a document was generated and holds no breaking change
#   OPENAPI VERDICT: FAILED        exit 1   a breaking change, a missing baseline, a document that
#                                           could not be produced from this tree, or a vacuous one
#   OPENAPI VERDICT: DID NOT RUN   exit 2   node or the .NET SDK is missing: nothing was generated
#
# The toolchain guard sits BEFORE the first stage and nothing above it may kill the script, because
# a missing tool that dies inside a stage prints zero bytes and returns 127 (rules/testing.md,
# "Toolchain prerequisites").
#
# WHAT THIS CHECK CANNOT SEE is printed on every verdict, because a green line from a gate that
# reads a description is not a statement about the running server.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
baseline="$root/docs/api/openapi-baseline.json"
differ="$root/scripts/lib/openapi-diff.mjs"

# shellcheck disable=SC1091
. "$root/scripts/dev"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/openapi-emit.sh"

# openapi_blind_spots: stated after every verdict, FAILED included.
openapi_blind_spots() {
  echo "OPENAPI: UNOBSERVED HERE — runtime-only behaviour: the document describes the contract, it does not exercise it."
  echo "OPENAPI: UNOBSERVED HERE — authentication that the document does not describe (a scheme or policy absent from it is invisible)."
  echo "OPENAPI: UNOBSERVED HERE — endpoints the generator does not cover: non-controller (minimal) endpoints appear only if the generator lists them."
  echo "OPENAPI: UNOBSERVED HERE — validation keywords (length, range, pattern, uniqueItems, additionalProperties) and security scheme definitions."
  echo "OPENAPI: UNOBSERVED HERE — oneOf/anyOf are compared by position; headers, links, callbacks and x- extensions are not compared."
}

openapi_verdict_failed() {
  echo "OPENAPI VERDICT: FAILED — $*"
  openapi_blind_spots
  exit 1
}

verb="${1:-check}"
case "$verb" in
  check|--check|--accept|accept|selftest) ;;
  *)
    echo "usage: maran openapi [--check | --accept | selftest]" >&2
    exit 1
    ;;
esac

# THE GUARD. The selftest needs only node; generating a document needs the .NET SDK as well.
if ! command -v node >/dev/null 2>&1; then
  suite_did_not_run "OPENAPI VERDICT" "node is not on PATH, so no document was compared. This is the environment, not the API."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi
if [ "$verb" != "selftest" ] && ! suite_require_toolchain backend "$root"; then
  suite_did_not_run "OPENAPI VERDICT" \
    "the .NET SDK this command needs is absent or too old, so no document was generated. This is the environment, not the API."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

# The planted controls run before anything is measured, every time: a comparison whose detector is
# blind must not be the thing that certifies the API.
if ! node "$differ" selftest; then
  openapi_verdict_failed "the breaking-change detector failed its own planted controls, so it cannot be trusted to judge the API."
fi

if [ "$verb" = "selftest" ]; then
  echo "OPENAPI SELFTEST: OK"
  exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
generated="$work/openapi.json"

echo "generating the document from the panel"
generate_status=0
openapi_generate "$work/raw.json" "$work/emit.log" || generate_status=$?
if [ "$generate_status" -ne 0 ]; then
  tail -n 20 "$work/emit.log" >&2
  if [ "$generate_status" -eq 2 ]; then
    suite_did_not_run "OPENAPI VERDICT" "$OPENAPI_GENERATE_REASON"
    exit "$SUITE_STATUS_DID_NOT_RUN"
  fi
  openapi_verdict_failed "$OPENAPI_GENERATE_REASON"
fi
node "$differ" canon "$work/raw.json" "$generated" \
  || openapi_verdict_failed "the generated file is not a readable JSON document."

case "$verb" in
  --accept|accept)
    # Recording is refused for a vacuous document, never for a breaking one: --accept is how a
    # deliberate break is recorded, in its own reviewed commit.
    node "$differ" diff "$generated" "$generated" >"$work/self.log" 2>&1 \
      || openapi_verdict_failed "refusing to record a document that describes no operation: $(tail -n 1 "$work/self.log")"
    if [ -f "$baseline" ]; then
      node "$differ" diff "$baseline" "$generated" || true
    fi
    mkdir -p "$(dirname "$baseline")"
    cp "$generated" "$baseline"
    echo "openapi: recorded into ${baseline#"$root"/} — $(tail -n 1 "$work/self.log")"
    echo "OPENAPI VERDICT: OK — baseline recorded; commit it on its own."
    openapi_blind_spots
    exit 0
    ;;
esac

[ -f "$baseline" ] || openapi_verdict_failed "docs/api/openapi-baseline.json is missing — record it with 'maran openapi --accept'."

status=0
node "$differ" diff "$baseline" "$generated" >"$work/diff.log" 2>&1 || status=$?
cat "$work/diff.log"
if [ "$status" -ne 0 ]; then
  echo "  If the change is intended, record it with 'maran openapi --accept' in its own commit."
  openapi_verdict_failed "the generated document breaks a client written against the baseline (findings above), or could not be compared."
fi

if grep -q '^ADDITIVE' "$work/diff.log"; then
  echo "openapi: the baseline is behind the API (additive changes above); record them with 'maran openapi --accept'."
fi
echo "OPENAPI VERDICT: OK"
openapi_blind_spots
