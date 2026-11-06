#!/usr/bin/env bash
# The OpenAPI document of a release, for `maran release openapi` (issue #143).
#
# SOURCED by scripts/lib/release-bundle.sh, which supplies release_failed, release_ok, require_tool and
# VERDICT_PREFIX. Usage:
#   maran release openapi --out <dir>
#
# WHAT IT WRITES. `<dir>/openapi.json`: the OpenAPI document generated from the tree it runs in, by
# the same function the `maran openapi` gate uses (scripts/lib/openapi-emit.sh), in the gate's
# canonical form, so the attached file and the committed baseline are comparable byte for byte. It
# prints the file's sha256. It REFUSES, writing nothing into <dir>, when the document was not
# generated, is not JSON, or describes no operation: a release never carries a document that is
# empty or that came from somewhere else.
#
# NOT SIGNED. The document is attached to the release and listed in SHA256SUMS with the other assets,
# and nothing more: the manifest signature and the integrity manifest do not cover it (owner
# decision, issue #143). SHA256SUMS is a convenience for downloads, not a trust anchor.
#
# UNOBSERVED HERE: that the document matches what a deployed panel serves (it is generated from the
# controllers of the tagged tree, not read from a server), and anything the generator does not list.

# release_openapi_main: parses the arguments and writes the document. Exits through release_failed.
release_openapi_main() {
  local out="" work generate_status=0 sum
  while [ $# -gt 0 ]; do
    case "$1" in
      --out) out="${2:?--out requires a value}"; shift 2 ;;
      *) echo "release openapi: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ -n "$out" ] || { echo "release openapi: --out DIR is required" >&2; usage; }

  require_tool node "canonicalises and checks the document"
  suite_require_toolchain backend "$root" \
    || { suite_did_not_run "$VERDICT_PREFIX" "the .NET SDK this command needs is absent or too old, so no document was generated."; exit "$SUITE_STATUS_DID_NOT_RUN"; }

  work="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf '$work'" EXIT
  openapi_generate "$work/raw.json" "$work/emit.log" || generate_status=$?
  if [ "$generate_status" -ne 0 ]; then
    tail -n 20 "$work/emit.log" >&2
    if [ "$generate_status" -eq 2 ]; then
      suite_did_not_run "$VERDICT_PREFIX" "$OPENAPI_GENERATE_REASON"
      exit "$SUITE_STATUS_DID_NOT_RUN"
    fi
    release_failed "openapi: $OPENAPI_GENERATE_REASON"
  fi
  node "$root/scripts/lib/openapi-diff.mjs" canon "$work/raw.json" "$work/openapi.json" \
    || release_failed "openapi: the generated file is not a readable JSON document"
  node "$root/scripts/lib/openapi-diff.mjs" diff "$work/openapi.json" "$work/openapi.json" >"$work/self.log" 2>&1 \
    || release_failed "openapi: refusing to attach a document that describes no operation: $(tail -n 1 "$work/self.log")"

  mkdir -p "$out"
  cp "$work/openapi.json" "$out/openapi.json"
  sum="$(sha256sum "$out/openapi.json" | awk '{print $1}')"
  echo "openapi.json sha256 ${sum}"
  release_ok "openapi.json written to ${out} ($(tail -n 1 "$work/self.log")); NOT signed, listed in SHA256SUMS only."
}
