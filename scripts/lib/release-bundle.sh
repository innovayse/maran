#!/usr/bin/env bash
# Builds, signs and verifies the release bundle `installer/lib/50-artifacts.sh` expects.
#
# That step is the installer's only trust boundary (see the header comment of
# installer/lib/50-artifacts.sh): it will not unpack anything until a manifest's
# ECDSA P-384 signature verifies against installer/keys/release-signing.pub
# (installer/cli/lib/release-verify.sh, cli_release_verify_manifest) and each component's
# sha256 inside that now-trusted manifest matches the bytes on disk (re-checked by
# cli_layout_install_release in installer/cli/lib/release-layout.sh immediately before it
# unpacks). The `maran update` command goes through the same two libraries.
# Nothing produced a manifest or a signed bundle before this file; see
# docs/superpowers/notes/2026-09-19-release-bundle-signing-threat-note.md for the threat
# model this closes and does not close.
#
# Manifest shape read by cli_release_manifest_field (installer/cli/lib/release-verify.sh, a plain
# awk reader — no jq at install time):
#   {"version": "<ver>", "channel": "stable|beta",
#    "notes_url": "https://...", "released": "YYYY-MM-DD",
#    "artifacts": {"<component>-<arch>": {"url": "...", "sha256": "<hex>"}}}
# "channel" is an additive, signed field the installer's reader never looks at (it keys on the
# `"<component>-<arch>"` blocks); the self-update CLI refuses a manifest whose channel differs from
# the one it asked for. check_manifest_channel (release-package.sh) holds a built bundle to it.
# "notes_url" and "released" (UTC build date) are additive and signed too; `maran update --check`
# prints them after validating each (cli_release_notes_url_valid, cli_release_date_valid), and a
# manifest without them stays valid.
# "url" is meaningless for the offline path (the offline branch of installer/lib/50-artifacts.sh
# and cli_release_verify_offline never read it) but is still written per component so an online
# release channel can serve the same manifest this script produces.
#
# Offline bundle shape read by the installer step `step_artifacts` (installer/lib/50-artifacts.sh)
# and by cli_release_verify_offline (installer/cli/lib/release-verify.sh):
# a single tar.gz whose root holds manifest.json, manifest.json.sig, and
# api.tar.gz / agent.tar.gz / frontend.tar.gz, each of those, once verified, unpacked by
# cli_layout_install_release (installer/cli/lib/release-layout.sh) into
# /usr/local/maran/releases/<version>/<component>, where `current` points at the release and
# /usr/local/maran/<component> reaches it through that link:
#   api/Maran.Host        (installer/systemd/maran-api.service:24 ExecStart=)
#   agent/maran-agent      (installer/systemd/maran-agent.service:70 ExecStart=)
#   frontend/*             (installer/nginx/maran.conf:129 root /usr/local/maran/frontend;)
# so each component tarball's files sit at ITS OWN ROOT, not nested under a version or
# bin/ subdirectory: cli_layout_install_release unpacks each into
# releases/<version>/<component> and adds no path of its own.
#
# Usage:
#   scripts/maran release build   [--version <ver>] [--out <dir>]   assemble the bundle
#   scripts/maran release sign    --key <path> [--bundle <dir>]     sign its manifest
#   scripts/maran release verify  --bundle <dir> [--pubkey <path>]  verify it, as the installer would
#   scripts/maran release package [--bundle <dir>] [--arch <list>|both] [--out <dir>]
#                                                                   one verified offline tarball per arch
#   scripts/maran release selftest                                  build+sign+verify, and prove
#                                                                     tamper and unsigned bundles
#                                                                     are BOTH refused
#   scripts/maran release sbom|changelog|rebuild [arguments]        CycloneDX SBOMs, release notes, and a
#                                                                   rebuild of the agent from a tag
#   scripts/maran release sbom-selftest|changelog-selftest|rebuild-selftest|reproducible-selftest
#                                                                   the matching selftests (no docker)
# SOURCE_DATE_EPOCH (digits), or `build --source-date-epoch <n>` which sets it and wins over the
# environment, makes tarballs byte-reproducible and pins the agent build's clock and path prefixes.
#
# The private signing key NEVER lives in this repository and this script never writes,
# prints or generates one for real use (rules/security.md #8, #9): `sign` takes its path
# from --key or $MARAN_RELEASE_SIGNING_KEY and refuses loudly when neither is set. Deciding
# where that key actually lives is the owner's decision — this file works with whatever
# path it is given and assumes nothing about it.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/lib/signing.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-package.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-package-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-reproducible.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-sbom.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-sbom-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-changelog.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-changelog-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/openapi-emit.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-openapi.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-openapi-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-rebuild.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-rebuild-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/deploy-local-guard.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/deploy-local.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/deploy-local-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-bundle-support.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-build-components.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-integrity.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-cmd-build.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-sign-verify.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-selftest.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-selftest-completeness.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/release-selftest-fields.sh"

command_name="${1:-}"
[ -n "$command_name" ] || usage
shift || true

case "$command_name" in
  build) cmd_build "$@" ;;
  sign) cmd_sign "$@" ;;
  verify) cmd_verify "$@" ;;
  package) cmd_package "$@" ;;
  selftest) cmd_selftest "$@" ;;
  package-selftest) cmd_package_selftest "$@" ;;
  sbom) release_sbom_main "$@" ;;
  sbom-selftest) release_sbom_selftest "$@" ;;
  changelog) release_changelog_main "$@" ;;
  changelog-selftest) release_changelog_selftest "$@" ;;
  rebuild) release_rebuild_main "$@" ;;
  rebuild-selftest) release_rebuild_selftest "$@" ;;
  openapi) release_openapi_main "$@" ;;
  openapi-selftest) release_openapi_selftest "$@" ;;
  reproducible-selftest) release_reproducible_selftest "$@" ;;
  deploy-local) deploy_local_main "$@" ;;
  deploy-local-selftest) deploy_local_selftest "$@" ;;
  -h|--help|help) usage ;;
  *) echo "unknown release subcommand: $command_name" >&2; usage ;;
esac
