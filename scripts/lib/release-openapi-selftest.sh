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
# And the signature (issue #552), release_openapi_signature_selftest, with a throwaway P-384 key:
#
#   6  a document signed with the key              ACCEPTED by openapi_verify (inverse control)
#   7  one byte appended after signing             REFUSED
#   8  the signature file removed                  REFUSED
#   9  a signature made by another key             REFUSED
#  10  no document to sign                         REFUSED, no signature written
#  11  `verify --publish` / `sign --publish` without --openapi   REFUSED for that reason; with --openapi that
#      reason is gone (inverse control)
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

# release_openapi_signature_selftest: cases 6 to 11. Drives the real openapi_sign and openapi_verify, the
# functions `maran release sign --openapi` and `maran release verify --openapi` call.
release_openapi_signature_selftest() {
  require_tool openssl "signs the planted document with a throwaway key"
  local dir keys doc tampered unsigned
  dir="$(mktemp -d)"
  keys="$dir/keys"
  doc="$dir/doc"
  install -d -m 0700 "$keys" "$doc"
  openssl ecparam -name secp384r1 -genkey -noout -out "$keys/a.key" >/dev/null 2>&1
  openssl pkey -in "$keys/a.key" -pubout -out "$keys/a.pub" >/dev/null 2>&1
  openssl ecparam -name secp384r1 -genkey -noout -out "$keys/b.key" >/dev/null 2>&1
  printf %s '{"openapi":"3.0.1","info":{"title":"t","version":"1"},"paths":{"/api/v1/ping":{"get":{"responses":{"200":{"description":"ok"}}}}}}' > "$doc/openapi.json"

  openapi_sign "$keys/a.key" "$doc" || release_failed "openapi selftest: a good document could not be signed"
  [ -s "$doc/openapi.json.sig" ] || release_failed "openapi selftest: signing wrote no openapi.json.sig"
  openapi_verify "$keys/a.pub" "$doc" 2>/dev/null \
    || release_failed "openapi selftest: a document signed with the key was REFUSED — the verifier refuses everything"
  release_ok "openapi selftest: a signed document is accepted (expected)."

  tampered="$dir/tampered"
  cp -r "$doc" "$tampered"
  printf ' ' >> "$tampered/openapi.json"
  if openapi_verify "$keys/a.pub" "$tampered" 2>/dev/null; then
    release_failed "openapi selftest: verify ACCEPTED a document changed after signing — the signature check is decoration."
  fi
  release_ok "openapi selftest: a document changed after signing is refused (expected)."

  unsigned="$dir/unsigned"
  cp -r "$doc" "$unsigned"
  rm -f "$unsigned/openapi.json.sig"
  if openapi_verify "$keys/a.pub" "$unsigned" 2>/dev/null; then
    release_failed "openapi selftest: verify ACCEPTED a document with no openapi.json.sig."
  fi
  release_ok "openapi selftest: a document with no signature is refused (expected)."

  sign_release_file "$keys/b.key" "$doc/openapi.json" "$unsigned/openapi.json.sig" \
    || release_failed "openapi selftest: could not sign the wrong-key fixture"
  if openapi_verify "$keys/a.pub" "$unsigned" 2>/dev/null; then
    release_failed "openapi selftest: verify ACCEPTED a signature made by another key."
  fi
  release_ok "openapi selftest: a signature by another key is refused (expected)."

  rm -f "$doc/openapi.json" "$doc/openapi.json.sig"
  if openapi_sign "$keys/a.key" "$doc" 2>/dev/null; then
    release_failed "openapi selftest: sign succeeded with no document."
  fi
  [ ! -e "$doc/openapi.json.sig" ] || release_failed "openapi selftest: a signature was written for a document that does not exist"
  release_ok "openapi selftest: signing with no document is refused and writes nothing (expected)."

  local verb log
  for verb in sign verify; do
    log="$dir/${verb}.log"
    if ( "cmd_${verb}" --bundle "$dir/no-bundle" --publish ) >"$log" 2>&1; then
      release_failed "openapi selftest: release ${verb} --publish without --openapi SUCCEEDED."
    fi
    grep -q -- "--publish requires --openapi" "$log" \
      || { cat "$log" >&2; release_failed "openapi selftest: release ${verb} --publish without --openapi was not refused for the missing --openapi"; }
    ( "cmd_${verb}" --bundle "$dir/no-bundle" --publish --openapi "$dir/doc" ) >"$log" 2>&1 || true
    ! grep -q -- "--publish requires --openapi" "$log" \
      || release_failed "openapi selftest: release ${verb} --publish --openapi was still refused for the missing --openapi (the refusal fires always)"
  done
  release_ok "openapi selftest: --publish without --openapi is refused for sign and verify (expected)."
  rm -rf "$dir"
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
  release_openapi_signature_selftest
  echo "RELEASE OPENAPI SELFTEST: UNOBSERVED HERE — the real generator, the Release build and the CLI verb; the document is planted."
  release_ok "openapi selftest: every case behaved as expected."
}
