#!/usr/bin/env bash
# Shared definitions of `maran release`: verdict lines, usage, architecture names, the manifest field writers and require_tool.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

VERDICT_PREFIX="RELEASE VERDICT"

release_ok() {
  echo "${VERDICT_PREFIX}: OK — $*"
}

release_failed() {
  echo "${VERDICT_PREFIX}: FAILED — $*" >&2
  exit 1
}

usage() {
  cat >&2 <<'USAGE'
usage: maran release <build|sign|verify|package|sbom|changelog|rebuild|openapi|selftest|package-selftest|sbom-selftest|changelog-selftest|rebuild-selftest|reproducible-selftest|openapi-selftest|deploy-local|deploy-local-selftest> [arguments]

  build     [--version <ver>] [--channel stable|beta] [--security] [--out <dir>] [--arch <list>|both] [--publish]
            [--source-date-epoch <n>] [--notes-url <https url>]
            assemble api+agent+frontend into a manifest'd bundle dir. --arch defaults to this
            host's; `both` builds x86_64 and aarch64, which is what a PUBLISHED release needs
            (preflight accepts both, so a release with one leaves ARM64 operators nothing to
            install). --security signs `"security": true` into the manifest: this release fixes a
            known vulnerability and a server with the automatic security-update timer may apply it
            unattended (docs/operations/panel-update.md); without it the flag is false.
            --publish declares the bundle is meant to be served and REFUSES anything but
            `both`; only a --publish build is shipping (the panel build then needs all five pinned
            public keys of installer/keys/, and the manifest lists them as `pinned_keys`). A single-architecture bundle also gets the unsuffixed tarball names the
            offline installer path reads.
  sign      --key <path> [--bundle <dir>] [--publish] [--openapi <dir>]
                                                sign the bundle's manifest.json with an ECDSA P-384 key;
                                                --publish first refuses a manifest lacking either arch, or
                                                one whose pinned_keys differ from installer/keys/;
                                                --openapi also signs <dir>/openapi.json into openapi.json.sig
  verify    --bundle <dir> [--pubkey <path>] [--publish] [--openapi <dir>]
                                                verify signature+checksums the way the installer does;
                                                --publish also requires api/agent/frontend for BOTH arches
                                                and the pinned_keys of installer/keys/ (manifest and api archive);
                                                --openapi also refuses a missing or wrong openapi.json.sig
  package   [--bundle <dir>] [--arch <list>|both] [--out <dir>|<file>.tar.gz] [--pubkey <path>]
                                                one offline tarball per architecture, each extracted
                                                and verified with the installer's own checks before it
                                                is reported (default: every arch the manifest holds,
                                                into <bundle>-offline/maran-offline-<ver>-<arch>.tar.gz;
                                                a single arch may take --out <file>.tar.gz)
  sbom|changelog|rebuild [arguments]            CycloneDX SBOM per component, release notes from merged
                                                pull requests, rebuild of the agent from a tag
  openapi   --out <dir>                         write the tree's OpenAPI document as <dir>/openapi.json and
                                                print its sha256; refuses a document that was not generated
                                                or describes no operation. Signed by `sign --openapi`.
  sbom-selftest|changelog-selftest|rebuild-selftest|reproducible-selftest|openapi-selftest
                                                the matching selftests; none needs docker (the last
                                                names that blind spot itself)
  deploy-local [[user@]host] [options]          build, sign with a TEST key, copy and install on a development
                                                host over ssh (docs/operations/deploy-local.md); refuses the
                                                production key; --dry-run prints the commands and runs nothing
  deploy-local-selftest                         the same verb against fake ssh and scp, every refusal paired
                                                with an inverse control
  package-selftest                              plant a two-architecture signed bundle, package it,
                                                prove each result verifies AND that every broken
                                                bundle is refused (rules/testing.md inverse controls)
  selftest                                      build, sign with a throwaway key, verify OK, then
                                                 prove a tampered byte and a missing signature are
                                                 BOTH refused (rules/testing.md: "a refusing gate
                                                 needs an inverse control")
USAGE
  exit 2
}

# arch_name: matches installer/install.sh:310-312's own mapping, so a bundle built here
# names components the same way MARAN_ARCH does at install time.
arch_name() {
  case "${1:-$(uname -m)}" in
    x86_64|amd64) echo "x86_64" ;;
    aarch64|arm64) echo "aarch64" ;;
    *) echo "unsupported" ;;
  esac
}

# docker_platform: the same architecture spelled the way `docker --platform` spells it, for the
# agent builder. Beside the other two spellings so all three names for one architecture move
# together — the release now builds for an architecture that is NOT necessarily the host's, and a
# mismatch between these would silently produce a bundle whose agent is for the wrong machine.
docker_platform() {
  case "${1:-$(uname -m)}" in
    x86_64|amd64) echo "linux/amd64" ;;
    aarch64|arm64) echo "linux/arm64" ;;
    *) echo "unsupported" ;;
  esac
}

# dotnet_rid: the same machine, spelled the way `dotnet publish -r` spells it. Kept beside
# arch_name rather than derived from it at the call site, so the two names for one architecture
# move together.
dotnet_rid() {
  case "${1:-$(uname -m)}" in
    x86_64|amd64) echo "linux-x64" ;;
    aarch64|arm64) echo "linux-arm64" ;;
    *) echo "unsupported" ;;
  esac
}

# sha256_of: one place for the checksum command, matching
# cli_release_check_sha256 (installer/cli/lib/release-verify.sh) exactly (sha256sum,
# first whitespace-separated field).
sha256_of() {
  sha256sum "$1" | awk '{print $1}'
}

# MARAN_PUBLISH_ARCHES / MARAN_COMPONENTS: the full set a PUBLISHED release must carry. preflight
# accepts both architectures, so a release holding one passes an operator's gate and then leaves
# them nothing to install (issue #53). Named once so the build refusal and the manifest check below
# cannot disagree about what "complete" means.
readonly MARAN_PUBLISH_ARCHES="x86_64 aarch64"
readonly MARAN_COMPONENTS="api agent frontend"

# MARAN_NOTES_URL_MAX: the longest notes_url a manifest may carry. Equals the ceiling the CLI holds
# the field to when it prints it (cli_release_notes_url_valid, installer/cli/lib/release-verify.sh).
readonly MARAN_NOTES_URL_MAX=512

# notes_url_valid: <url>. Succeeds when the URL is one the CLI will print: https, a limited ASCII
# alphabet with no whitespace, control character, quote or backslash, and within MARAN_NOTES_URL_MAX.
# The same rule as cli_release_notes_url_valid, held here so a build refuses what the CLI would drop.
notes_url_valid() {
  local LC_ALL=C
  [ "${#1}" -le "$MARAN_NOTES_URL_MAX" ] \
    && [[ "$1" =~ ^https://[A-Za-z0-9][A-Za-z0-9._~:/?#@\&+,=%-]*$ ]]
}

# manifest_release_fields: <notes_url> prints the two additive signed fields that follow
# "channel" in the manifest, each line ending in a comma: "notes_url" (where the release notes are)
# and "released" (the UTC build date, YYYY-MM-DD). The date is taken from SOURCE_DATE_EPOCH when set,
# so a reproducible build writes the same manifest bytes twice, and from the clock otherwise. Refuses
# a notes_url the CLI would not print.
manifest_release_fields() {
  local notes_url="$1" released
  notes_url_valid "$notes_url" \
    || release_failed "notes URL '${notes_url}' is not an https URL of at most ${MARAN_NOTES_URL_MAX} plain characters; pass a valid one with --notes-url"
  if [ "${SOURCE_DATE_EPOCH+x}" = "x" ]; then
    released="$(date -u -d "@${SOURCE_DATE_EPOCH}" +%Y-%m-%d)"
  else
    released="$(date -u +%Y-%m-%d)"
  fi
  echo "  \"notes_url\": \"${notes_url}\","
  echo "  \"released\": \"${released}\","
}

# manifest_security_field: <0|1>. Prints the signed top-level "security" line that follows "channel"
# in the manifest, ending in a comma: `true` when this release fixes a known vulnerability and may be
# applied by the unattended timer (`maran update --security-only`), `false` otherwise. Always written,
# so a manifest without it is a manifest built before the field existed; the CLI reads anything but a
# literal true as "not a security release" (installer/cli/lib/release-verify.sh).
manifest_security_field() {
  case "$1" in
    1) echo '  "security": true,' ;;
    0) echo '  "security": false,' ;;
    *) release_failed "manifest_security_field: expected 0 or 1, got '${1}'" ;;
  esac
}

# The five public keys a release pins (issue #544): the first two are embedded in the panel build, the
# other three are installed beside it. The MSBuild check of the Licensing project names the same five
# (PinnedKeys.targets); the selftest cross-checks this list, that file and docs/operations/licensing.md.
readonly MARAN_PINNED_KEYS="licence-verification.pub lease-verification.pub marketplace-metadata.pub module-signing.pub release-signing.pub"

# pinned_key_shape_ok: <file>. Succeeds for a file that looks like a public key, by the same rule as the
# Licensing project's PinnedKeys.targets applies to the whole file text: it begins (after optional
# whitespace) with `-----BEGIN PUBLIC KEY-----`, or it is the 64 hexadecimal characters of an Ed25519 public
# key surrounded only by whitespace (no whitespace INSIDE the hex). An empty file or a placeholder is no key:
# it would be embedded or hashed into the signed manifest and yield the panel that trusts nothing, which is
# the defect #544 closes. The selftest drives both sides with the same fixtures.
pinned_key_shape_ok() {
  local text
  [ -f "$1" ] && [ -s "$1" ] || return 1
  text="$(cat -- "$1")" || return 1
  [[ "$text" =~ ^[[:space:]]*-----BEGIN\ PUBLIC\ KEY----- ]] && return 0
  [[ "$text" =~ ^[[:space:]]*[0-9a-fA-F]{64}[[:space:]]*$ ]]
}

# pinned_keys_lines: <keys dir>. Prints `<file name> <sha256>` for each pinned key, one per line, in the
# fixed order above; the hash of the file's bytes, never its content. Refuses (release_failed) when any
# of the five is missing, empty or not shaped like a key, or cannot be hashed, because a release that pins
# fewer than five is the build error #544 closes and a signed manifest must never carry an empty hash.
pinned_keys_lines() {
  local dir="$1" key hash
  for key in $MARAN_PINNED_KEYS; do
    [ -f "${dir}/${key}" ] \
      || release_failed "the pinned public key installer/keys/${key} is missing; a published release pins all five (issue #544)"
    pinned_key_shape_ok "${dir}/${key}" \
      || release_failed "the pinned public key installer/keys/${key} is empty or not a public key (a PEM block or 64 hexadecimal characters); never a placeholder (issue #544)"
    # A separate assignment, so a failing hash aborts here instead of being swallowed by the
    # substitution inside printf's arguments.
    hash="$(sha256_of "${dir}/${key}")" || release_failed "could not hash installer/keys/${key}"
    [[ "$hash" =~ ^[0-9a-f]{64}$ ]] || release_failed "hashing installer/keys/${key} did not give a SHA-256 (got '${hash}')"
    printf '%s %s\n' "$key" "$hash"
  done
}

# manifest_pinned_keys_field: <keys dir>. Prints the signed top-level "pinned_keys" object that precedes
# "artifacts" in a published manifest: file name to sha256, flat, so the installer's brace-depth reader
# (which only looks for `<component>-<arch>` blocks) never confuses it with an artifact. The object is
# closed with `},` because "artifacts" follows.
manifest_pinned_keys_field() {
  local lines name sha count=0 total
  lines="$(pinned_keys_lines "$1")" || exit 1  # the failure was already printed inside the substitution
  total="$(wc -l <<<"$lines")"
  echo '  "pinned_keys": {'
  while read -r name sha; do
    count=$((count + 1))
    if [ "$count" -lt "$total" ]; then
      echo "    \"${name}\": \"${sha}\","
    else
      echo "    \"${name}\": \"${sha}\""
    fi
  done <<<"$lines"
  echo '  },'
}

# manifest_missing_keys: <manifest.json>. Prints each `<component>-<arch>` key a published release
# needs and the manifest does not hold, one per line. The key is matched as a quoted string, the
# same shape verify_bundle and installer/lib/50-artifacts.sh's reader scan for, so a key that is
# merely mentioned inside a url cannot satisfy it.
manifest_missing_keys() {
  local manifest="$1" component arch
  for arch in $MARAN_PUBLISH_ARCHES; do
    for component in $MARAN_COMPONENTS; do
      grep -qE "^[[:space:]]*\"${component}-${arch}\"[[:space:]]*:" "$manifest" \
        || echo "${component}-${arch}"
    done
  done
}

# require_publishable_manifest: <manifest.json>. Returns 1 naming every missing key. Separate from
# verify_bundle because a local or offline single-architecture bundle is legitimate and must keep
# verifying; only a bundle declared --publish owes both architectures.
require_publishable_manifest() {
  local manifest="$1" missing
  [ -f "$manifest" ] || { echo "publish check: no manifest at ${manifest}" >&2; return 1; }
  missing="$(manifest_missing_keys "$manifest" | tr '\n' ' ')"
  if [ -n "${missing// /}" ]; then
    echo "publish check: ${manifest} lacks: ${missing}— a published release needs api, agent and frontend for BOTH x86_64 and aarch64" >&2
    return 1
  fi
  return 0
}

# require_pinned_keys: <bundle dir> <keys dir>. Returns 1 naming the defect unless a published bundle pins
# exactly the five public keys of <keys dir> (the checkout's installer/keys/): the signed manifest holds a
# `pinned_keys` object that equals what the checkout's files hash to, and each api archive carries a
# `pinned-keys.txt` that agrees. A bundle built without --publish has neither, and without this check could
# still be signed and verified for publication (issue #544).
require_pinned_keys() {
  local bundle="$1" keys_dir="$2" manifest expected_lines actual archive arch listed
  manifest="${bundle}/manifest.json"
  [ -f "$manifest" ] || { echo "publish check: no manifest at ${manifest}" >&2; return 1; }
  grep -qE '^[[:space:]]*"pinned_keys"[[:space:]]*:[[:space:]]*\{' "$manifest" \
    || { echo "publish check: ${manifest} has no pinned_keys object — the bundle was not built with 'maran release build --publish' (issue #544)" >&2; return 1; }
  expected_lines="$(pinned_keys_lines "$keys_dir")" || return 1  # the reason was printed by release_failed
  actual="$(sed -n '/^[[:space:]]*"pinned_keys"[[:space:]]*:/,/^[[:space:]]*},/p' "$manifest")"
  if [ "$actual" != "$(manifest_pinned_keys_field "$keys_dir")" ]; then
    echo "publish check: the pinned_keys object of ${manifest} is not the five keys (names and SHA-256) of ${keys_dir} (issue #544)" >&2
    return 1
  fi
  for arch in $MARAN_PUBLISH_ARCHES; do
    archive="${bundle}/api-${arch}.tar.gz"
    [ -f "$archive" ] || { echo "publish check: no ${archive}" >&2; return 1; }
    listed="$(tar -xzOf "$archive" ./pinned-keys.txt 2>/dev/null || tar -xzOf "$archive" pinned-keys.txt 2>/dev/null)" || listed=""
    if [ "$listed" != "$expected_lines" ]; then
      echo "publish check: ${archive##*/} has no pinned-keys.txt, or it disagrees with the manifest and ${keys_dir} (issue #544)" >&2
      return 1
    fi
  done
  return 0
}

# require_toolchain: named refusal, not a silent skip, per rules/testing.md — a builder
# that quietly omits a component produces a bundle that fails on the OPERATOR's server
# instead of here.
require_tool() {
  local tool="$1" reason="$2"
  command -v "$tool" >/dev/null 2>&1 || {
    suite_did_not_run "$VERDICT_PREFIX" "$tool is not on PATH ($reason). source scripts/dev first, and see: maran check"
    exit "$SUITE_STATUS_DID_NOT_RUN"
  }
}
