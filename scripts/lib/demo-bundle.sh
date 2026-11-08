#!/usr/bin/env bash
# `maran demo` - builds, signs and verifies the public demonstration's own bundle, and drives its
# seed and browser smoke.
#
#   maran demo build   [--version <ver>] [--out <dir>]    publish Maran.Host.Demo + the demo SPA flavour
#   maran demo sign    --key <path> [--bundle <dir>]      sign the bundle's manifest (ECDSA P-384, SHA-384)
#   maran demo verify  --bundle <dir> [--pubkey <path>]   verify it the way bin/maran-demo-deploy does
#   maran demo selftest                                   prove the product installer REFUSES a demo bundle
#   maran demo seed                                       seed the configured database (demo-seed verb)
#   maran demo smoke                                      the demo browser suite (frontend, playwright.demo.config.ts)
#
# THE DEMO BUNDLE IS DELIBERATELY NOT A PRODUCT RELEASE. It holds two components (api, frontend) and no
# agent, its manifest carries `"flavour": "demo"` and no channel, and it is signed by a DIFFERENT key than
# installer/keys/release-signing.pub. The product installer and `maran update` verify only against that
# product key, so they refuse a demo bundle by signature - the property `maran demo selftest` measures,
# with a control. A leak of the demo key therefore endangers the demonstration and nothing else.
#
# The private key NEVER lives in this repository: `sign` takes it from --key or
# $MARAN_DEMO_SIGNING_KEY, refuses a path inside the working tree, and nothing here generates or prints
# key material for real use (rules/security.md items 8 and 9).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/demo-selftest.sh"

VERDICT_PREFIX="DEMO VERDICT"
readonly MARAN_DEMO_SIGNATURE_DIGEST="sha384"

demo_ok() {
  echo "${VERDICT_PREFIX}: OK - $*"
}

demo_failed() {
  echo "${VERDICT_PREFIX}: FAILED - $*" >&2
  exit 1
}

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

require_tool() {
  command -v "$1" >/dev/null 2>&1 || {
    suite_did_not_run "$VERDICT_PREFIX" "$1 is not on PATH ($2). source scripts/dev first, and see: maran check"
    exit "$SUITE_STATUS_DID_NOT_RUN"
  }
}

sha256_of() {
  sha256sum "$1" | awk '{ print $1 }'
}

# write_demo_manifest <dir> <version>: the manifest of the bundle in <dir>. The shape is the product
# manifest's reader shape ("artifacts" keyed by component, each with url and sha256) plus one additive
# signed field, "flavour". There is no channel: a missing channel is accepted by the product verifier's
# signature check and refused by its network path, and neither would ever be reached because the
# signature does not match the product key.
write_demo_manifest() {
  local dir="$1" version="$2" released
  released="$(date -u +%Y-%m-%d)"
  cat > "${dir}/manifest.json" <<JSON
{
  "version": "${version}",
  "flavour": "demo",
  "released": "${released}",
  "artifacts": {
    "api": {
      "url": "api.tar.gz",
      "sha256": "$(sha256_of "${dir}/api.tar.gz")"
    },
    "frontend": {
      "url": "frontend.tar.gz",
      "sha256": "$(sha256_of "${dir}/frontend.tar.gz")"
    }
  }
}
JSON
}

# demo_verify_bundle <dir> <pubkey>: signature first, then each archive against the signed checksum.
# Silent on success; prints the reason and returns 1 on any refusal.
demo_verify_bundle() {
  local dir="$1" pubkey="$2" component expected actual
  [ -f "$pubkey" ] || { echo "verify: public key not found at ${pubkey}" >&2; return 1; }
  [ -f "${dir}/manifest.json" ] || { echo "verify: no manifest.json in ${dir}" >&2; return 1; }
  [ -f "${dir}/manifest.json.sig" ] || { echo "verify: bundle is UNSIGNED" >&2; return 1; }
  openssl dgst -"$MARAN_DEMO_SIGNATURE_DIGEST" -verify "$pubkey" -signature "${dir}/manifest.json.sig" \
    "${dir}/manifest.json" >/dev/null 2>&1 \
    || { echo "verify: the manifest signature does NOT verify against ${pubkey}" >&2; return 1; }
  for component in api frontend; do
    [ -f "${dir}/${component}.tar.gz" ] || { echo "verify: ${component}.tar.gz is missing" >&2; return 1; }
    expected="$(awk -v key="\"${component}\"" '
      $0 ~ key { in_block = 1 }
      in_block && /"sha256"/ { match($0, /"[^"]*"[[:space:]]*,?[[:space:]]*$/); v = substr($0, RSTART, RLENGTH); gsub(/[",[:space:]]/, "", v); print v; exit }
    ' "${dir}/manifest.json")"
    actual="$(sha256_of "${dir}/${component}.tar.gz")"
    [ -n "$expected" ] && [ "$expected" = "$actual" ] \
      || { echo "verify: checksum mismatch for ${component}.tar.gz" >&2; return 1; }
  done
}

cmd_build() {
  local version="0.0.0-demo.0" out="/tmp/maran-demo-bundle" staging
  while [ $# -gt 0 ]; do
    case "$1" in
      --version) version="${2:?--version needs a value}"; shift 2 ;;
      --out) out="${2:?--out needs a value}"; shift 2 ;;
      *) usage ;;
    esac
  done
  require_tool dotnet "publishes the demonstration"
  require_tool npm "builds the demo SPA"
  staging="$(mktemp -d)"
  # Expanded NOW: `staging` is local, and the EXIT trap runs after this function has returned, where
  # `set -u` would make the unbound name fail the trap and the whole command.
  trap "rm -rf -- '${staging}'" EXIT

  dotnet publish "$root/backend/src/Maran.Host.Demo/Maran.Host.Demo.csproj" \
    -c Release -o "${staging}/api" --self-contained true -r linux-x64 -p:PublishTrimmed=false \
    >"${staging}/.api-publish.log" 2>&1 \
    || { cat "${staging}/.api-publish.log" >&2; demo_failed "dotnet publish failed for Maran.Host.Demo (log above)"; }
  [ -f "${staging}/api/Maran.Host.Demo" ] || demo_failed "dotnet publish produced no Maran.Host.Demo apphost"
  [ -f "${staging}/api/libhostfxr.so" ] || demo_failed "the publish is framework-dependent; the server has no .NET runtime"

  # build:demo sets VITE_MARAN_DEMO=1 and writes dist-demo, never dist: a stale normal build in dist can
  # not be what gets packed, and the demo flavour (banner, published sign-ins, read-only notes) is built.
  rm -rf -- "$root/frontend/dist-demo"
  ( cd "$root/frontend" && npm run build:demo ) >"${staging}/.frontend-build.log" 2>&1 \
    || { cat "${staging}/.frontend-build.log" >&2; demo_failed "npm run build:demo failed in frontend/ (log above)"; }
  [ -d "$root/frontend/dist-demo" ] || demo_failed "npm run build:demo produced no frontend/dist-demo"
  # The demo SPA is the one that asks the API whether it is a demo; a bundle without that request is the
  # normal panel, whose visitors would not be told the data is shared, public and reset.
  grep -rqF -- 'demo/status' "$root/frontend/dist-demo" \
    || demo_failed "frontend/dist-demo does not contain the demo status request; it is not the demo SPA"
  cp -r "$root/frontend/dist-demo" "${staging}/frontend"

  rm -rf -- "$out"
  mkdir -p "$out"
  ( cd "${staging}/api" && tar -czf "${out}/api.tar.gz" . )
  ( cd "${staging}/frontend" && tar -czf "${out}/frontend.tar.gz" . )
  write_demo_manifest "$out" "$version"
  demo_ok "built ${version} into ${out}; unsigned until: maran demo sign --key <path> --bundle ${out}"
}

cmd_sign() {
  local key="${MARAN_DEMO_SIGNING_KEY:-}" bundle="/tmp/maran-demo-bundle"
  while [ $# -gt 0 ]; do
    case "$1" in
      --key) key="${2:?--key needs a value}"; shift 2 ;;
      --bundle) bundle="${2:?--bundle needs a value}"; shift 2 ;;
      *) usage ;;
    esac
  done
  require_tool openssl "signs the manifest"
  [ -n "$key" ] || demo_failed "no signing key given (--key or MARAN_DEMO_SIGNING_KEY); this script generates none"
  [ -f "$key" ] || demo_failed "signing key not found at ${key}"
  case "$(cd "$(dirname "$key")" && pwd)/$(basename "$key")" in
    "$root"/*) demo_failed "refusing a signing key path inside this repository (${key}) - rules/security.md item 8" ;;
  esac
  [ -f "${bundle}/manifest.json" ] || demo_failed "no manifest.json in ${bundle}; run: maran demo build"
  openssl dgst -"$MARAN_DEMO_SIGNATURE_DIGEST" -sign "$key" -out "${bundle}/manifest.json.sig" "${bundle}/manifest.json"
  demo_ok "signed ${bundle}/manifest.json"
}

cmd_verify() {
  local bundle="" pubkey="$root/demo/keys/demo-release-signing.pub"
  while [ $# -gt 0 ]; do
    case "$1" in
      --bundle) bundle="${2:?--bundle needs a value}"; shift 2 ;;
      --pubkey) pubkey="${2:?--pubkey needs a value}"; shift 2 ;;
      *) usage ;;
    esac
  done
  require_tool openssl "verifies the signature"
  [ -n "$bundle" ] || usage
  demo_verify_bundle "$bundle" "$pubkey" || demo_failed "the bundle is not trusted"
  demo_ok "${bundle} verifies against ${pubkey}"
}

cmd_seed() {
  require_tool dotnet "runs the demo binary"
  exec dotnet run --project "$root/backend/src/Maran.Host.Demo" -- --cli demo-seed "$@"
}

cmd_smoke() {
  require_tool npm "runs the browser suite"
  cd "$root/frontend"
  exec npx playwright test -c playwright.demo.config.ts "$@"
}

command_name="${1:-}"
[ $# -gt 0 ] && shift
case "$command_name" in
  build) cmd_build "$@" ;;
  sign) cmd_sign "$@" ;;
  verify) cmd_verify "$@" ;;
  selftest) cmd_selftest "$@" ;;
  seed) cmd_seed "$@" ;;
  smoke) cmd_smoke "$@" ;;
  *) usage ;;
esac
