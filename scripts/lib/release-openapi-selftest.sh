#!/usr/bin/env bash
# Selftest for `maran release openapi` (issue #143): `maran release openapi-selftest`.
#
# SOURCED by scripts/lib/release-bundle.sh after scripts/lib/release-openapi.sh. It replaces
# openapi_emit with planted writers, so it needs node and nothing else (no .NET SDK, no build), and
# drives the real release_openapi_main through each outcome of the generator:
#
#   0  a document with an operation        ACCEPTED, openapi.json written, its sha256 printed (inverse control)
#   1  the generator exits 0, writes nothing           REFUSED, no file
#   2  the generator exits 0, writes text that is not JSON   REFUSED, no file
#   3  a valid document with no operation              REFUSED (vacuous), no file
#   4  the generator exits 69 (not registered)         REFUSED as a failure, no file
#   5  the generator exits 77 (root)                   DID NOT RUN (status 2), no file
#
# Every refusal asserts that <out>/openapi.json does not exist, so "refused" means nothing was
# attached and not merely that a message was printed.
#
# UNOBSERVED HERE: the real generator, the Release build and the CLI verb are not run; the document is
# planted. The first tagged run of the release workflow is where the real route is observed.

# release_openapi_selftest_case <label> <expect: ok|refused|didnotrun> <emit body>: runs the real main
# against a replaced openapi_emit in a subshell and checks the outcome and the file.
release_openapi_selftest_case() {
  local label="$1" expect="$2" emitter="$3" dir out status=0 log
  dir="$(mktemp -d)"
  out="$dir/out"
  log="$dir/log"
  (
    # shellcheck disable=SC2317
    require_tool() { :; }
    # shellcheck disable=SC2317
    suite_require_toolchain() { return 0; }
    # shellcheck disable=SC2317
    openapi_emit() { eval "$emitter"; }
    release_openapi_main --out "$out"
  ) >"$log" 2>&1 || status=$?
  case "$expect" in
    ok)
      [ "$status" -eq 0 ] || { cat "$log" >&2; release_failed "openapi selftest: ${label}: a good document was REFUSED (status ${status})"; }
      [ -s "$out/openapi.json" ] || release_failed "openapi selftest: ${label}: accepted but no openapi.json was written"
      grep -qE "^openapi\.json sha256 [0-9a-f]{64}$" "$log" \
        || release_failed "openapi selftest: ${label}: the sha256 line was not printed"
      [ "$(sha256sum "$out/openapi.json" | awk '{print $1}')" = "$(grep -E '^openapi\.json sha256 ' "$log" | awk '{print $3}')" ] \
        || release_failed "openapi selftest: ${label}: the printed sha256 is not the file's"
      ;;
    refused)
      [ "$status" -eq 1 ] || { cat "$log" >&2; release_failed "openapi selftest: ${label}: expected a refusal (status 1), got ${status}"; }
      [ ! -e "$out/openapi.json" ] || release_failed "openapi selftest: ${label}: refused, but a document was left in the output"
      grep -q 'RELEASE VERDICT: FAILED' "$log" || release_failed "openapi selftest: ${label}: no FAILED verdict was printed"
      ;;
    didnotrun)
      [ "$status" -eq "$SUITE_STATUS_DID_NOT_RUN" ] || { cat "$log" >&2; release_failed "openapi selftest: ${label}: expected DID NOT RUN (status 2), got ${status}"; }
      [ ! -e "$out/openapi.json" ] || release_failed "openapi selftest: ${label}: did not run, but a document was left in the output"
      grep -q 'DID NOT RUN' "$log" || release_failed "openapi selftest: ${label}: no DID NOT RUN line was printed"
      ;;
  esac
  rm -rf "$dir"
  release_ok "openapi selftest: ${label} (expected)."
}

# release_openapi_selftest: the cases above.
release_openapi_selftest() {
  require_tool node "runs the document checks"
  local good='printf %s "{\"openapi\":\"3.0.1\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{\"/api/v1/ping\":{\"get\":{\"responses\":{\"200\":{\"description\":\"ok\"}}}}}}" > "$1"'
  local vacuous='printf %s "{\"openapi\":\"3.0.1\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{}}" > "$1"'
  release_openapi_selftest_case "a document with an operation is attached and its sha256 printed" ok "$good"
  release_openapi_selftest_case "a generator that wrote nothing is refused" refused ': > "$1"'
  release_openapi_selftest_case "a generator that wrote text that is not JSON is refused" refused 'printf %s "not json" > "$1"'
  release_openapi_selftest_case "a document that describes no operation is refused" refused "$vacuous"
  release_openapi_selftest_case "a generator that exits 69 is a failure" refused 'return 69'
  release_openapi_selftest_case "a generator that exits 77 (root) did not run" didnotrun 'return 77'
  echo "RELEASE OPENAPI SELFTEST: UNOBSERVED HERE — the real generator, the Release build and the CLI verb; the document is planted."
  release_ok "openapi selftest: every case behaved as expected."
}
