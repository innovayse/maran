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
            `both`. A single-architecture bundle also gets the unsuffixed tarball names the
            offline installer path reads.
  sign      --key <path> [--bundle <dir>] [--publish]
                                                sign the bundle's manifest.json with an ECDSA P-384 key;
                                                --publish first refuses a manifest lacking either arch
  verify    --bundle <dir> [--pubkey <path>] [--publish]
                                                verify signature+checksums the way the installer does;
                                                --publish also requires api/agent/frontend for BOTH arches
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
                                                or describes no operation. Not signed.
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
