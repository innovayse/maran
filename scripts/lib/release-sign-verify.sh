#!/usr/bin/env bash
# `maran release sign` and `maran release verify`: the signing-key guard, cmd_sign, verify_bundle and cmd_verify.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

require_signing_key() {
  local key="$1"
  if [ -z "$key" ]; then
    echo "50-artifacts.sh's trust boundary is only as good as this key never entering the" >&2
    echo "repository. Pass --key <path> or set MARAN_RELEASE_SIGNING_KEY to an ECDSA P-384" >&2
    echo "private key file (signs with SHA-384) OUTSIDE this working tree. This script will not generate one," >&2
    echo "will not default to a path inside the repository, and will not print key material." >&2
    echo "Where that key lives in production is Innovayse's decision, not this script's." >&2
    release_failed "no signing key given"
  fi
  if [ ! -f "$key" ]; then
    release_failed "signing key not found at ${key}"
  fi
  case "$(cd "$(dirname "$key")" && pwd)/$(basename "$key")" in
    "$root"/*) release_failed "refusing a signing key path inside this repository (${key}) — see rules/security.md #8" ;;
  esac
}

cmd_sign() {
  local key="${MARAN_RELEASE_SIGNING_KEY:-}" bundle_dir="/tmp/maran-release-bundle" publish=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --key) key="${2:?--key requires a value}"; shift 2 ;;
      --bundle) bundle_dir="${2:?--bundle requires a value}"; shift 2 ;;
      --publish) publish=1; shift ;;
      *) echo "release sign: unknown argument: $1" >&2; usage ;;
    esac
  done
  require_tool openssl "signs the manifest"
  require_signing_key "$key"
  [ -f "${bundle_dir}/manifest.json" ] || release_failed "no manifest.json under ${bundle_dir} — run: maran release build"
  # Checked before the signature exists: signing is the act that makes an incomplete manifest look
  # official, so the refusal belongs ahead of it rather than after.
  if [ "$publish" -eq 1 ]; then
    require_publishable_manifest "${bundle_dir}/manifest.json" \
      || release_failed "refusing to sign an incomplete published release (reason printed above)."
  fi

  sign_release_file "$key" "${bundle_dir}/manifest.json" "${bundle_dir}/manifest.json.sig" \
    || release_failed "signing manifest.json failed"

  echo "Signed ${bundle_dir}/manifest.json -> ${bundle_dir}/manifest.json.sig"

  # integrity-manifest.json: same key, same invocation, a SECOND file rather than folded
  # into the manifest.json signature above — see write_integrity_manifest's comment.
  [ -f "${bundle_dir}/integrity-manifest.json" ] || release_failed "no integrity-manifest.json under ${bundle_dir} — run: maran release build (this bundle predates the code-integrity hash list)"

  sign_release_file "$key" "${bundle_dir}/integrity-manifest.json" \
      "${bundle_dir}/integrity-manifest.json.sig" \
    || release_failed "signing integrity-manifest.json failed"

  echo "Signed ${bundle_dir}/integrity-manifest.json -> ${bundle_dir}/integrity-manifest.json.sig"
}

# verify_bundle: the SAME two checks the installer and `maran update` perform: the same
# openssl invocation (cli_release_verify_manifest, installer/cli/lib/release-verify.sh) and the
# same checksum comparison (cli_release_check_sha256, same file). They run here so a bundle
# this script accepts is a bundle the installer accepts, not a bundle that merely looks
# right to a differently-shaped check
# (rules/testing.md "a check must be able to observe what it reports on").
verify_bundle() {
  local bundle_dir="$1" pubkey="$2"
  [ -f "$pubkey" ] || { echo "verify: public key not found at ${pubkey}" >&2; return 1; }
  [ -f "${bundle_dir}/manifest.json" ] || { echo "verify: no manifest.json in ${bundle_dir}" >&2; return 1; }
  [ -f "${bundle_dir}/manifest.json.sig" ] || { echo "verify: no manifest.json.sig in ${bundle_dir} — bundle is UNSIGNED" >&2; return 1; }

  if ! verify_release_file "$pubkey" "${bundle_dir}/manifest.json" "${bundle_dir}/manifest.json.sig"; then
    echo "verify: manifest signature does NOT verify against ${pubkey}" >&2
    return 1
  fi

  # After the signature, so the field read is the signed one: a channel edited after signing is the
  # signature's refusal above, and a manifest signed without one (or with urls for another channel)
  # is refused here.
  check_manifest_channel "${bundle_dir}/manifest.json" || return 1

  # Driven by the MANIFEST's own keys rather than a fixed list, because a published bundle now
  # carries one set per architecture (issue #53) while an offline bundle and the selftest's fixture
  # carry one unsuffixed set. Verifying what the manifest claims is also the more honest check: a
  # manifest entry with no archive behind it is exactly the defect an ARM64 operator met.
  local component expected actual archive
  for component in $(grep -oE '"(api|agent|frontend)-[a-z0-9_]+"' "${bundle_dir}/manifest.json" \
                       | tr -d '"' | sort -u); do
    # `<component>-<arch>.tar.gz` is what a published bundle holds; `<component>.tar.gz` is the
    # unsuffixed name the offline installer path reads, and the one the selftest's fixture writes.
    archive="${bundle_dir}/${component}.tar.gz"
    [ -f "$archive" ] || archive="${bundle_dir}/${component%%-*}.tar.gz"
    [ -f "$archive" ] || { echo "verify: manifest names ${component} but neither ${component}.tar.gz nor ${component%%-*}.tar.gz is in ${bundle_dir}" >&2; return 1; }
    expected="$(awk -v key="\"${component}\"" '
      $0 ~ key { in_block=1 }
      in_block && /"sha256"/ {
        match($0, /"[^"]*"[[:space:]]*$/)
        val = substr($0, RSTART, RLENGTH); gsub(/"/, "", val); print val; exit
      }
      in_block && /}/ { in_block=0 }
    ' "${bundle_dir}/manifest.json")"
    [ -n "$expected" ] || { echo "verify: no sha256 in manifest for ${component}" >&2; return 1; }
    # $archive, not a name rebuilt from $component: the two differ whenever the bundle carries the
    # unsuffixed offline shape, and hashing a path that does not exist would report a mismatch about
    # a file nobody shipped.
    actual="$(sha256_of "$archive")"
    if [ "$actual" != "$expected" ]; then
      echo "verify: checksum mismatch for ${archive##*/}: expected ${expected}, got ${actual}" >&2
      return 1
    fi
  done
  return 0
}

cmd_verify() {
  local bundle_dir="/tmp/maran-release-bundle" pubkey="$root/installer/keys/release-signing.pub" publish=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --bundle) bundle_dir="${2:?--bundle requires a value}"; shift 2 ;;
      --pubkey) pubkey="${2:?--pubkey requires a value}"; shift 2 ;;
      --publish) publish=1; shift ;;
      *) echo "release verify: unknown argument: $1" >&2; usage ;;
    esac
  done
  require_tool openssl "verifies the manifest"
  if [ "$publish" -eq 1 ] && ! require_publishable_manifest "${bundle_dir}/manifest.json"; then
    release_failed "${bundle_dir} is not a complete published release (reason printed above)."
  fi
  if verify_bundle "$bundle_dir" "$pubkey"; then
    release_ok "${bundle_dir} verifies against ${pubkey} (signature + every checksum the manifest names)."
  else
    release_failed "${bundle_dir} did not verify against ${pubkey} (reason printed above)."
  fi
}
