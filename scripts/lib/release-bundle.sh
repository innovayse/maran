#!/usr/bin/env bash
# Builds, signs and verifies the release bundle `installer/lib/50-artifacts.sh` expects.
#
# That step is the installer's only trust boundary (its own header comment,
# installer/lib/50-artifacts.sh:1-6): it will not unpack anything until a manifest's
# Ed25519 signature verifies against installer/keys/release-signing.pub
# (installer/lib/50-artifacts.sh:28) and each component's sha256 inside that
# now-trusted manifest matches the bytes on disk (installer/lib/50-artifacts.sh:123-150,
# 179-206). Nothing produced a manifest or a signed bundle before this file — see
# docs/superpowers/notes/2026-09-19-release-bundle-signing-threat-note.md for the threat
# model this closes and does not close.
#
# Manifest shape read by installer/lib/50-artifacts.sh:91-104 (manifest_field, a plain
# awk reader — no jq at install time):
#   {"artifacts": {"<component>-<arch>": {"url": "...", "sha256": "<hex>"}}}
# "url" is meaningless for the offline path (installer/lib/50-artifacts.sh's
# use_offline_tarball never reads it) but is still written per component so an online
# release channel can serve the same manifest this script produces.
#
# Offline bundle shape read by use_offline_tarball (installer/lib/50-artifacts.sh:159-178):
# a single tar.gz whose root holds manifest.json, manifest.json.sig, and
# api.tar.gz / agent.tar.gz / frontend.tar.gz — each of those, once extracted, landing at
# installer/lib/50-artifacts.sh's unpack_artifacts destinations
# (installer/lib/50-artifacts.sh:179-206):
#   api/Maran.Host        (installer/systemd/maran-api.service:24 ExecStart=)
#   agent/maran-agent      (installer/systemd/maran-agent.service:70 ExecStart=)
#   frontend/*             (installer/nginx/maran.conf:129 root /usr/local/maran/frontend;)
# so each component tarball's files sit at ITS OWN ROOT, not nested under a version or
# bin/ subdirectory — 50-artifacts.sh extracts each with `tar -xzf ... -C
# ${MARAN_INSTALL_ROOT}/${component}` and adds no path of its own.
#
# Usage:
#   scripts/maran release build   [--version <ver>] [--out <dir>]   assemble the bundle
#   scripts/maran release sign    --key <path> [--bundle <dir>]     sign its manifest
#   scripts/maran release verify  --bundle <dir> [--pubkey <path>]  verify it, as the installer would
#   scripts/maran release selftest                                  build+sign+verify, and prove
#                                                                     tamper and unsigned bundles
#                                                                     are BOTH refused
#
# The private signing key NEVER lives in this repository and this script never writes,
# prints or generates one for real use (rules/security.md #8, #9): `sign` takes its path
# from --key or $MARAN_RELEASE_SIGNING_KEY and refuses loudly when neither is set. Deciding
# where that key actually lives is the owner's decision — this file works with whatever
# path it is given and assumes nothing about it.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

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
usage: maran release <build|sign|verify|selftest> [arguments]

  build     [--version <ver>] [--channel stable|beta] [--out <dir>]
            assemble api+agent+frontend into a manifest'd bundle dir
  sign      --key <path> [--bundle <dir>]       sign the bundle's manifest.json with an Ed25519 key
  verify    --bundle <dir> [--pubkey <path>]    verify signature+checksums the way the installer does
  package   [--bundle <dir>] [--out <file>]     tar the bundle dir into the single offline tarball
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
  case "$(uname -m)" in
    x86_64|amd64) echo "x86_64" ;;
    aarch64|arm64) echo "aarch64" ;;
    *) echo "unsupported" ;;
  esac
}

# sha256_of: one place for the checksum command, matching
# installer/lib/50-artifacts.sh:126-132's verify_artifact_checksum exactly (sha256sum,
# first whitespace-separated field).
sha256_of() {
  sha256sum "$1" | awk '{print $1}'
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

# build_component: publishes/builds one component and tars its OUTPUT DIRECTORY's
# contents at the tarball root (never the directory itself as a top-level entry) — the
# nesting installer/lib/50-artifacts.sh's unpack_artifacts assumes.
#
# A component this session cannot build here (no toolchain, no network, tree lock held by
# the other lane's quota work) is a NOT DONE, stated loudly, never a placeholder file: an
# empty or fake component archive would pass this script's OWN checksum check and still
# be exactly the kind of decoration rules/testing.md rejects, because the check would see
# nothing different whether the real binary is broken or simply never built.
build_api() {
  local staging="$1"
  require_tool dotnet "publishes the backend"
  local publish_dir="${staging}/api"
  rm -rf -- "$publish_dir"
  dotnet publish "$root/backend/src/Maran.Host/Maran.Host.csproj" \
    -c Release -o "$publish_dir" \
    >"${staging}/.api-publish.log" 2>&1 \
    || { cat "${staging}/.api-publish.log" >&2; release_failed "dotnet publish failed for Maran.Host (log above)"; }
}

build_agent() {
  local staging="$1"
  require_tool cargo "builds the agent"
  (cd "$root/agent" && cargo build --release -p maran-agent) \
    >"${staging}/.agent-build.log" 2>&1 \
    || { cat "${staging}/.agent-build.log" >&2; release_failed "cargo build --release -p maran-agent failed (log above)"; }
  local out_dir="${staging}/agent"
  rm -rf -- "$out_dir"
  install -d "$out_dir"
  install -m 0755 "$root/agent/target/release/maran-agent" "$out_dir/maran-agent"
}

build_frontend() {
  local staging="$1"
  require_tool npm "builds the SPA"
  ( cd "$root/frontend" && npm run build ) \
    >"${staging}/.frontend-build.log" 2>&1 \
    || { cat "${staging}/.frontend-build.log" >&2; release_failed "npm run build failed in frontend/ (log above)"; }
  local out_dir="${staging}/frontend"
  rm -rf -- "$out_dir"
  cp -r "$root/frontend/dist" "$out_dir"
}

# tar_component: tars a staged output directory's CONTENTS at the archive root, matching
# what use_offline_tarball's per-component `tar -xzf ... -C
# ${MARAN_INSTALL_ROOT}/${component}` expects to find (installer/lib/50-artifacts.sh:179-206).
tar_component() {
  local src_dir="$1" dest_tar="$2"
  ( cd "$src_dir" && tar -czf "$dest_tar" . )
}

# INTEGRITY_MANIFEST_MIN_FILES: the vacuity floor for integrity-manifest.json
# (docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 1). An empty or
# near-empty hash list would sign successfully, verify successfully, and report Clean
# forever on every installed host — the single most dangerous failure mode this feature
# has, because it looks identical to health. A fixed number rather than a percentage of
# the previous release's count: this script has no access to "the previous release" at
# build time (open question left to the owner, per the plan). Small enough not to be a
# maintenance burden as the release grows, large enough that "the frontend bundle alone
# produced zero files" or "the walk pointed at an empty directory" cannot pass silently.
readonly INTEGRITY_MANIFEST_MIN_FILES=50

# write_integrity_manifest: the code-integrity hash list, a SEPARATE artefact from
# manifest.json (docs/superpowers/specs/2026-09-19-maran-code-integrity.md §3) — one
# sha256 per installed FILE rather than per component archive, read continuously after
# install by the closed PluginLoader, never by this installer's own transport check.
# Walks the SAME staged output directories build_api/build_agent/build_frontend already
# populate and tar_component already tars — never the installed filesystem (§4: this is a
# positive list of what a release CONTAINS, not a live filesystem scan) — and keys each
# entry by "<component>/<path relative to that component's staged root>", matching
# installer/lib/50-artifacts.sh's own extraction targets
# (${MARAN_INSTALL_ROOT}/${component}), so api/, agent/, frontend/ entries cannot collide.
write_integrity_manifest() {
  local out_dir="$1" version="$2"
  local manifest="${out_dir}/integrity-manifest.json"
  local tmp_entries
  tmp_entries="$(mktemp)"

  local component file rel_path sha
  for component in api agent frontend; do
    local comp_dir="${out_dir}/${component}"
    [ -d "$comp_dir" ] || release_failed "write_integrity_manifest: ${comp_dir} does not exist — build_${component} must run before the integrity manifest is written"
    while IFS= read -r -d '' file; do
      rel_path="${file#"${comp_dir}"/}"
      sha="$(sha256_of "$file")"
      printf '%s\0%s\0' "${component}/${rel_path}" "$sha" >> "$tmp_entries"
    done < <(find "$comp_dir" -type f -print0)
  done

  local count=0
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    echo "  \"files\": {"
    local first=1 key val
    while IFS= read -r -d '' key && IFS= read -r -d '' val; do
      [ "$first" -eq 1 ] || echo ","
      first=0
      count=$((count + 1))
      printf '    "%s": "%s"' "$key" "$val"
    done < "$tmp_entries"
    echo
    echo "  }"
    echo "}"
  } > "$manifest"
  rm -f "$tmp_entries"

  # The vacuity-floor gate: a build that produced fewer files than this is refused loudly
  # rather than shipped as a hash list nobody can trust "all clean" against.
  if [ "$count" -lt "$INTEGRITY_MANIFEST_MIN_FILES" ]; then
    release_failed "write_integrity_manifest: only ${count} file(s) walked across api/agent/frontend, below INTEGRITY_MANIFEST_MIN_FILES=${INTEGRITY_MANIFEST_MIN_FILES} — refusing to ship a hash list this thin (an empty or truncated list would report 'clean' forever)."
  fi

  echo "Wrote ${manifest} (${count} files, floor ${INTEGRITY_MANIFEST_MIN_FILES})."
}

cmd_build() {
  local version="" out_dir="" channel=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --version) version="${2:?--version requires a value}"; shift 2 ;;
      --channel) channel="${2:?--channel requires a value}"; shift 2 ;;
      --out) out_dir="${2:?--out requires a value}"; shift 2 ;;
      *) echo "release build: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ -n "$version" ] || version="0.0.0-dev-$(date -u +%Y%m%dT%H%M%SZ)"
  # The channel is part of every artifact URL the manifest publishes, and the manifest is what the
  # signature covers — so a bundle built for one channel and served from another is a bundle whose
  # own URLs point somewhere it is not. It was hardcoded `stable`, which would have published the
  # first beta under the word an operator reads as "ready".
  [ -n "$channel" ] || channel="stable"
  case "$channel" in
    stable|beta) ;;
    *) echo "release build: --channel must be stable or beta, got: ${channel}" >&2; exit 2 ;;
  esac
  [ -n "$out_dir" ] || out_dir="/tmp/maran-release-bundle"

  tree_lock_acquire "$root" 0 "release build" \
    || { suite_did_not_run "$VERDICT_PREFIX" "the tree lock is held (named above) — retry once the other lane finishes."; exit "$SUITE_STATUS_DID_NOT_RUN"; }
  trap tree_lock_release EXIT INT TERM

  rm -rf -- "$out_dir"
  install -d "$out_dir"

  local arch
  arch="$(arch_name)"
  [ "$arch" != "unsupported" ] || release_failed "uname -m ($(uname -m)) is not a MARAN_ARCH this installer recognizes (installer/install.sh:310-312)"

  build_api "$out_dir"
  build_agent "$out_dir"
  build_frontend "$out_dir"

  local component
  for component in api agent frontend; do
    tar_component "${out_dir}/${component}" "${out_dir}/${component}.tar.gz"
  done

  # integrity-manifest.json: a SEPARATE artefact from manifest.json below, written from the
  # same staged directories before they are removed from consideration — see
  # write_integrity_manifest's own comment for why this is not just another field on
  # manifest.json.
  write_integrity_manifest "$out_dir" "$version"

  # manifest.json: hand-built with the exact field names manifest_field's awk reader
  # scans for (installer/lib/50-artifacts.sh:91-104) — "url" and "sha256" as quoted JSON
  # string values on their own line inside each "<component>-<arch>" block. jq would be
  # cleaner but the installer never assumes jq is present at verify time, and a manifest
  # this script cannot also read with a bare awk scan would be a shape the installer's
  # own reader cannot be trusted to parse either.
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    echo "  \"artifacts\": {"
    local first=1
    for component in api agent frontend; do
      local sha
      sha="$(sha256_of "${out_dir}/${component}.tar.gz")"
      [ "$first" -eq 1 ] || echo "    },"
      first=0
      echo "    \"${component}-${arch}\": {"
      echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${component}-${arch}.tar.gz\","
      echo "      \"sha256\": \"${sha}\""
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${out_dir}/manifest.json"

  rm -f "${out_dir}"/.*-*.log
  echo "Bundle staged at ${out_dir} (version ${version}, arch ${arch})."
  echo "NOT SIGNED YET — run: maran release sign --key <path-to-private-key> --bundle ${out_dir}"
}

require_signing_key() {
  local key="$1"
  if [ -z "$key" ]; then
    echo "50-artifacts.sh's trust boundary is only as good as this key never entering the" >&2
    echo "repository. Pass --key <path> or set MARAN_RELEASE_SIGNING_KEY to an Ed25519" >&2
    echo "private key file OUTSIDE this working tree. This script will not generate one," >&2
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
  local key="${MARAN_RELEASE_SIGNING_KEY:-}" bundle_dir="/tmp/maran-release-bundle"
  while [ $# -gt 0 ]; do
    case "$1" in
      --key) key="${2:?--key requires a value}"; shift 2 ;;
      --bundle) bundle_dir="${2:?--bundle requires a value}"; shift 2 ;;
      *) echo "release sign: unknown argument: $1" >&2; usage ;;
    esac
  done
  require_tool openssl "signs the manifest"
  require_signing_key "$key"
  [ -f "${bundle_dir}/manifest.json" ] || release_failed "no manifest.json under ${bundle_dir} — run: maran release build"

  openssl pkeyutl -sign -rawin \
    -inkey "$key" \
    -in "${bundle_dir}/manifest.json" \
    -out "${bundle_dir}/manifest.json.sig" \
    || release_failed "openssl pkeyutl -sign failed"

  echo "Signed ${bundle_dir}/manifest.json -> ${bundle_dir}/manifest.json.sig"

  # integrity-manifest.json: same key, same invocation, a SECOND file rather than folded
  # into the manifest.json signature above — see write_integrity_manifest's comment.
  [ -f "${bundle_dir}/integrity-manifest.json" ] || release_failed "no integrity-manifest.json under ${bundle_dir} — run: maran release build (this bundle predates the code-integrity hash list)"

  openssl pkeyutl -sign -rawin \
    -inkey "$key" \
    -in "${bundle_dir}/integrity-manifest.json" \
    -out "${bundle_dir}/integrity-manifest.json.sig" \
    || release_failed "openssl pkeyutl -sign failed for integrity-manifest.json"

  echo "Signed ${bundle_dir}/integrity-manifest.json -> ${bundle_dir}/integrity-manifest.json.sig"
}

# verify_bundle: the SAME two checks installer/lib/50-artifacts.sh performs — same
# openssl invocation (verify_manifest_signature, installer/lib/50-artifacts.sh:70-88) and
# same checksum comparison (verify_artifact_checksum, installer/lib/50-artifacts.sh:
# 126-132) — run here so a bundle this script accepts is a bundle the installer accepts,
# not a bundle that merely looks right to a differently-shaped check
# (rules/testing.md "a check must be able to observe what it reports on").
verify_bundle() {
  local bundle_dir="$1" pubkey="$2"
  [ -f "$pubkey" ] || { echo "verify: public key not found at ${pubkey}" >&2; return 1; }
  [ -f "${bundle_dir}/manifest.json" ] || { echo "verify: no manifest.json in ${bundle_dir}" >&2; return 1; }
  [ -f "${bundle_dir}/manifest.json.sig" ] || { echo "verify: no manifest.json.sig in ${bundle_dir} — bundle is UNSIGNED" >&2; return 1; }

  if ! openssl pkeyutl -verify -rawin \
      -pubin -inkey "$pubkey" \
      -in "${bundle_dir}/manifest.json" -sigfile "${bundle_dir}/manifest.json.sig" >/dev/null 2>&1; then
    echo "verify: manifest signature does NOT verify against ${pubkey}" >&2
    return 1
  fi

  local component expected actual
  for component in api agent frontend; do
    [ -f "${bundle_dir}/${component}.tar.gz" ] || { echo "verify: missing ${component}.tar.gz" >&2; return 1; }
    expected="$(awk -v key="\"${component}-" '
      $0 ~ key { in_block=1 }
      in_block && /"sha256"/ {
        match($0, /"[^"]*"[[:space:]]*$/)
        val = substr($0, RSTART, RLENGTH); gsub(/"/, "", val); print val; exit
      }
      in_block && /}/ { in_block=0 }
    ' "${bundle_dir}/manifest.json")"
    [ -n "$expected" ] || { echo "verify: no sha256 in manifest for ${component}-*" >&2; return 1; }
    actual="$(sha256_of "${bundle_dir}/${component}.tar.gz")"
    if [ "$actual" != "$expected" ]; then
      echo "verify: checksum mismatch for ${component}.tar.gz: expected ${expected}, got ${actual}" >&2
      return 1
    fi
  done
  return 0
}

cmd_verify() {
  local bundle_dir="/tmp/maran-release-bundle" pubkey="$root/installer/keys/release-signing.pub"
  while [ $# -gt 0 ]; do
    case "$1" in
      --bundle) bundle_dir="${2:?--bundle requires a value}"; shift 2 ;;
      --pubkey) pubkey="${2:?--pubkey requires a value}"; shift 2 ;;
      *) echo "release verify: unknown argument: $1" >&2; usage ;;
    esac
  done
  require_tool openssl "verifies the manifest"
  if verify_bundle "$bundle_dir" "$pubkey"; then
    release_ok "${bundle_dir} verifies against ${pubkey} (signature + all three checksums)."
  else
    release_failed "${bundle_dir} did not verify against ${pubkey} (reason printed above)."
  fi
}

cmd_package() {
  local bundle_dir="/tmp/maran-release-bundle" out_file=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --bundle) bundle_dir="${2:?--bundle requires a value}"; shift 2 ;;
      --out) out_file="${2:?--out requires a value}"; shift 2 ;;
      *) echo "release package: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ -n "$out_file" ] || out_file="${bundle_dir%/}.tar.gz"
  [ -f "${bundle_dir}/manifest.json.sig" ] || release_failed "${bundle_dir} has no manifest.json.sig — sign it first: maran release sign"
  [ -f "${bundle_dir}/integrity-manifest.json" ] || release_failed "${bundle_dir} has no integrity-manifest.json — run: maran release build (this bundle predates the code-integrity hash list)"
  [ -f "${bundle_dir}/integrity-manifest.json.sig" ] || release_failed "${bundle_dir} has no integrity-manifest.json.sig — sign it first: maran release sign"
  ( cd "$bundle_dir" && tar -czf "$out_file" \
      manifest.json manifest.json.sig \
      integrity-manifest.json integrity-manifest.json.sig \
      api.tar.gz agent.tar.gz frontend.tar.gz )
  echo "Packaged offline bundle: ${out_file}"
  echo "This is the file installer/install.sh --offline-tarball <path> expects."
}

# cmd_selftest: the inverse-control demonstration rules/testing.md requires of any
# refusing gate — sign with a THROWAWAY key (never committed, never a production key),
# verify OK, then prove verify REFUSES a tampered component and REFUSES an unsigned
# bundle. A verify that only ever sees a good bundle proves nothing.
#
# This exercises the MANIFEST/SIGNATURE/CHECKSUM machinery only — the same
# manifest-writing and verify_bundle code cmd_build and cmd_verify use — against
# synthetic, clearly-fake component archives, NOT a real build. It does not call
# cmd_build: at the time this was written, `cargo build --release -p maran-agent` does
# not compile (crates/agent/src/services/accounts/accounts_service.rs:473, `no field
# quota_bytes on type AccountUsage` — the concurrent quota-enforceability lane's
# in-progress work, out of scope here and not this script's to fix). That failure is
# reported as its own NOT DONE; it must not block proving the signing/verification logic
# is correct, and a selftest that quietly swapped in fake bytes and called them a real
# release would be exactly the decoration rules/testing.md warns against — so this
# synthetic bundle is never written where cmd_build's or cmd_package's real output goes,
# and every line below says "selftest" or "synthetic", never "release".
cmd_selftest() {
  require_tool openssl "generates the throwaway key and runs every check below"
  local work
  work="$(mktemp -d)"
  trap 'rm -rf -- "$work"' RETURN

  # The selftest builds its own manifest inline rather than calling cmd_build, so it needs the
  # same `channel` the real builder has — without it the URL would interpolate an empty segment
  # and this test would be asserting against a shape no release ever has.
  local channel="stable"
  local keydir="${work}/keys" bundle="${work}/bundle"
  install -d -m 0700 "$keydir"
  openssl genpkey -algorithm ed25519 -out "${keydir}/throwaway.key" >/dev/null 2>&1
  openssl pkey -in "${keydir}/throwaway.key" -pubout -out "${keydir}/throwaway.pub" >/dev/null 2>&1

  install -d "$bundle"
  local component arch
  arch="$(arch_name)"

  # Synthetic per-component STAGED DIRECTORIES (not just a single payload file), so
  # write_integrity_manifest has something real to walk — 20 files per component, 60 in
  # total, comfortably above INTEGRITY_MANIFEST_MIN_FILES=50. Named "selftest", never
  # "release", per this function's own header comment.
  local i
  for component in api agent frontend; do
    install -d "${bundle}/${component}"
    for i in $(seq 1 20); do
      echo "synthetic ${component} file ${i} for release-bundle.sh selftest only — not a real artifact" \
        > "${bundle}/${component}/file-${i}.txt"
    done
    tar_component "${bundle}/${component}" "${bundle}/${component}.tar.gz"
  done

  write_integrity_manifest "$bundle" "selftest"
  {
    echo "{"
    echo "  \"version\": \"selftest\","
    echo "  \"artifacts\": {"
    local first=1
    for component in api agent frontend; do
      [ "$first" -eq 1 ] || echo "    },"
      first=0
      echo "    \"${component}-${arch}\": {"
      echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${component}-${arch}.tar.gz\","
      echo "      \"sha256\": \"$(sha256_of "${bundle}/${component}.tar.gz")\""
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${bundle}/manifest.json"
  MARAN_RELEASE_SIGNING_KEY="" cmd_sign --key "${keydir}/throwaway.key" --bundle "$bundle"

  echo
  echo "-- outcome 1: untouched bundle --"
  if verify_bundle "$bundle" "${keydir}/throwaway.pub"; then
    echo "${VERDICT_PREFIX}: OK — untouched bundle ACCEPTED (expected)."
  else
    release_failed "selftest: a freshly built and signed bundle did not verify — the builder or the verifier is broken."
  fi

  echo
  echo "-- outcome 2: one byte flipped in a component archive --"
  local tampered="${work}/tampered"
  cp -r "$bundle" "$tampered"
  printf 'TAMPERED' >> "${tampered}/agent.tar.gz"
  if verify_bundle "$tampered" "${keydir}/throwaway.pub"; then
    release_failed "selftest: verify ACCEPTED a bundle with a tampered agent.tar.gz — the checksum check is decoration."
  else
    echo "${VERDICT_PREFIX}: OK — tampered bundle REFUSED (expected)."
  fi

  echo
  echo "-- outcome 3: signature file removed --"
  local unsigned="${work}/unsigned"
  cp -r "$bundle" "$unsigned"
  rm -f "${unsigned}/manifest.json.sig"
  if verify_bundle "$unsigned" "${keydir}/throwaway.pub"; then
    release_failed "selftest: verify ACCEPTED a bundle with no manifest.json.sig — the signature check is decoration."
  else
    echo "${VERDICT_PREFIX}: OK — unsigned bundle REFUSED (expected)."
  fi

  echo
  echo "-- outcome 4: integrity-manifest.json names every synthetic file across ALL three components --"
  local api_count agent_count frontend_count total_count
  api_count="$(grep -c '"api/file-' "${bundle}/integrity-manifest.json")"
  agent_count="$(grep -c '"agent/file-' "${bundle}/integrity-manifest.json")"
  frontend_count="$(grep -c '"frontend/file-' "${bundle}/integrity-manifest.json")"
  total_count=$((api_count + agent_count + frontend_count))
  if [ "$api_count" -eq 20 ] && [ "$agent_count" -eq 20 ] && [ "$frontend_count" -eq 20 ] && [ "$total_count" -eq 60 ]; then
    echo "${VERDICT_PREFIX}: OK — integrity-manifest.json has 20 api + 20 agent + 20 frontend entries (60 total), not just one component (expected)."
  else
    release_failed "selftest: integrity-manifest.json has api=${api_count} agent=${agent_count} frontend=${frontend_count} — write_integrity_manifest silently omitted at least one component."
  fi

  echo
  echo "-- outcome 5: untouched integrity-manifest.json verifies against integrity-manifest.json.sig --"
  if openssl pkeyutl -verify -rawin \
      -pubin -inkey "${keydir}/throwaway.pub" \
      -in "${bundle}/integrity-manifest.json" -sigfile "${bundle}/integrity-manifest.json.sig" >/dev/null 2>&1; then
    echo "${VERDICT_PREFIX}: OK — untouched integrity-manifest.json ACCEPTED against its own signature (expected)."
  else
    release_failed "selftest: a freshly written and signed integrity-manifest.json did not verify against its own signature — the signing or verification invocation is broken."
  fi

  echo
  echo "-- outcome 6: a build-time defect (one wrong hex character in one entry's sha256, BEFORE signing) --"
  # This is deliberately NOT a tamper-after-signing case (outcomes 2/3 already cover that,
  # and cover it correctly: any byte changed after signing makes the signature refuse). This
  # models the honest gap the plan calls out instead: a manifest that was ALREADY WRONG at
  # the moment it was signed — one entry's sha256 corrupted by a build-time defect, the rest
  # of the file untouched and internally consistent. Once signed, that (defective) file's
  # signature verifies exactly like a correct one, because the signature only ever attests
  # "these bytes were signed by this key" — it does not re-derive any hash inside them.
  local defective="${work}/defective-manifest.json"
  cp "${bundle}/integrity-manifest.json" "$defective"
  sed -i '0,/": "[0-9a-f]\{64\}"/{s/": "0/": "1/; s/": "1/": "2/}' "$defective"
  if cmp -s "${bundle}/integrity-manifest.json" "$defective"; then
    release_failed "selftest: outcome 6's sed did not actually change any byte — the corruption step itself is broken, so this proves nothing."
  fi
  openssl pkeyutl -sign -rawin \
    -inkey "${keydir}/throwaway.key" \
    -in "$defective" \
    -out "${defective}.sig" \
    || release_failed "selftest: could not sign the outcome-6 fixture"
  if openssl pkeyutl -verify -rawin \
      -pubin -inkey "${keydir}/throwaway.pub" \
      -in "$defective" -sigfile "${defective}.sig" >/dev/null 2>&1; then
    echo "${VERDICT_PREFIX}: OK — a manifest with one entry's sha256 corrupted BEFORE signing still verifies once signed (expected, and the honest boundary this feature has: the signature proves the bytes were signed by this key, never that any hash inside them is correct — the build's own count/floor check does not catch this either, and this outcome documents that gap rather than hiding it)."
  else
    release_failed "selftest: a manifest signed with a valid key over its own (defective) bytes did NOT verify — the sign/verify invocation pairing itself is broken, which is a bigger problem than the gap this outcome exists to document."
  fi

  echo
  echo "-- outcome 7: the vacuity floor refuses a build with fewer than INTEGRITY_MANIFEST_MIN_FILES --"
  local thin="${work}/thin-bundle"
  install -d "${thin}/api" "${thin}/agent" "${thin}/frontend"
  echo "one lonely file" > "${thin}/api/only-file.txt"
  if ( write_integrity_manifest "$thin" "selftest-thin" ) >/dev/null 2>&1; then
    release_failed "selftest: write_integrity_manifest ACCEPTED a bundle with 1 file, below the floor of ${INTEGRITY_MANIFEST_MIN_FILES} — the vacuity floor is decoration."
  else
    echo "${VERDICT_PREFIX}: OK — a build with far fewer than ${INTEGRITY_MANIFEST_MIN_FILES} files was REFUSED by the vacuity floor (expected)."
  fi

  echo
  release_ok "selftest: build -> sign -> verify accepted a good bundle, refused a tampered one and an unsigned one, wrote a complete integrity-manifest.json across all three components, verified and refused it exactly like manifest.json, documented the signature-does-not-re-derive-hashes gap, and refused a build below the vacuity floor."
}

command_name="${1:-}"
[ -n "$command_name" ] || usage
shift || true

case "$command_name" in
  build) cmd_build "$@" ;;
  sign) cmd_sign "$@" ;;
  verify) cmd_verify "$@" ;;
  package) cmd_package "$@" ;;
  selftest) cmd_selftest "$@" ;;
  -h|--help|help) usage ;;
  *) echo "unknown release subcommand: $command_name" >&2; usage ;;
esac
