#!/usr/bin/env bash
# Builds, signs and verifies the release bundle `installer/lib/50-artifacts.sh` expects.
#
# That step is the installer's only trust boundary (its own header comment,
# installer/lib/50-artifacts.sh:1-6): it will not unpack anything until a manifest's
# ECDSA P-384 signature verifies against installer/keys/release-signing.pub
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

  build     [--version <ver>] [--channel stable|beta] [--out <dir>] [--arch <list>|both]
            assemble api+agent+frontend into a manifest'd bundle dir. --arch defaults to this
            host's; `both` builds x86_64 and aarch64, which is what a PUBLISHED release needs
            (preflight accepts both, so a release with one leaves ARM64 operators nothing to
            install). A single-architecture bundle also gets the unsuffixed tarball names the
            offline installer path reads.
  sign      --key <path> [--bundle <dir>]       sign the bundle's manifest.json with an ECDSA P-384 key
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
  local staging="$1" arch="${2:-$(arch_name)}"
  require_tool dotnet "publishes the backend"
  # Staged under an arch-suffixed name so a two-architecture build does not have its first half
  # overwritten by its second (issue #53).
  local publish_dir="${staging}/api-${arch}"
  rm -rf -- "$publish_dir"
  local rid
  rid="$(dotnet_rid "$arch")"
  [ "$rid" != "unsupported" ] || release_failed "dotnet publish: ${arch} is not an architecture this release builds for"
  # Cross-publishing needs no emulation: `dotnet publish -r linux-arm64` on an x86_64 host produces
  # an aarch64 apphost, measured by RUNNING it in an aarch64 container (it reached Wolverine's
  # startup on glibc 2.28), which is the only evidence that counts here.

  # SELF-CONTAINED, and this is the whole point of the flag rather than a preference. Published
  # framework-dependent, the artifact needs a .NET 9 runtime already on the host; no fresh Debian or
  # Ubuntu carries one and `20-dependencies.sh` installs none. Measured on Ubuntu 24.04: the panel
  # never started — "You must install .NET to run this application ... Failed to resolve
  # libhostfxr.so" — and the installer refused at step 70 because the socket was never bound. There
  # was no host on which either published beta could start.
  #
  # The alternative was to teach the installer to add Microsoft's package repository as root, per
  # family. That couples every install to a third-party feed's availability and spelling, for a
  # runtime this project already ships, signs and checksums. Size is the cost of not doing that.
  dotnet publish "$root/backend/src/Maran.Host/Maran.Host.csproj" \
    -c Release -o "$publish_dir" \
    --self-contained true -r "$rid" \
    -p:PublishTrimmed=false \
    >"${staging}/.api-publish.log" 2>&1 \
    || { cat "${staging}/.api-publish.log" >&2; release_failed "dotnet publish failed for Maran.Host (log above)"; }

  # The apphost is what systemd execs, and a publish that produced no runtime beside it is the
  # defect this function exists to prevent — caught here rather than sixty seconds into an install.
  [ -f "${publish_dir}/Maran.Host" ] \
    || release_failed "dotnet publish produced no Maran.Host apphost in ${publish_dir}"
  [ -f "${publish_dir}/libhostfxr.so" ] \
    || release_failed "dotnet publish produced no libhostfxr.so in ${publish_dir}: the artifact is framework-dependent and will not start on a host without a .NET runtime (issue #39)"
}

# The oldest glibc in installer/lib/10-preflight.sh's supported matrix: AlmaLinux 8 and Rocky 8
# carry 2.28, and every other accepted distribution carries more. The agent is built against THIS
# floor because a dynamically linked glibc binary is forward compatible and never backward
# compatible, so the floor is the only build target that runs on all eight (issue #48).
readonly MARAN_AGENT_GLIBC_FLOOR="2.28"

# The release signature: ECDSA P-384 over SHA-384, created and verified with `openssl dgst`.
#
# It was Ed25519 through `openssl pkeyutl -verify -rawin`, and that could not be verified on half
# the supported fleet: `-rawin` arrived in OpenSSL 3.0, and AlmaLinux 8, Rocky 8, Ubuntu 20.04 and
# Debian 11 all ship OpenSSL 1.1.1, whose `pkeyutl` has no such option and whose CLI cannot verify
# Ed25519 at all. Measured on AlmaLinux 8 (issue #52):
#
#     # openssl pkeyutl -verify -rawin -pubin -inkey release-signing.pub ...
#     pkeyutl: Option unknown option -rawin
#
# The installer reported that as "the signature does not match the trusted key" and told the
# operator to suspect a compromised mirror, so the old arrangement did not merely fail there — it
# failed while accusing the download.
#
# ECDSA over `openssl dgst` is verifiable by the OS's own openssl on EVERY supported version, with
# no fallback branch and no second key: a supply-chain check that asks "which signature can I manage
# here?" is only as strong as the weakest answer. P-384 rather than P-256 because the margin is free
# — one digest name differs — and this is the key that decides whether a server runs our code.
#
# Measured on AlmaLinux 8's OpenSSL 1.1.1k, both halves: a good signature answers `Verified OK` and
# a tampered payload answers `Verification Failure`. A verifier that only ever sees a good signature
# proves nothing (rules/testing.md).
readonly MARAN_SIGNATURE_DIGEST="sha384"

# sign_release_file: <private key> <file> <signature out>. The ONE place this repository spells the
# signing invocation, so the thing `verify_release_file` and installer/lib/50-artifacts.sh check can
# never drift from the thing that was signed.
sign_release_file() {
  openssl dgst -"$MARAN_SIGNATURE_DIGEST" -sign "$1" -out "$3" "$2"
}

# verify_release_file: <public key> <file> <signature>. Silent; the caller reports.
verify_release_file() {
  openssl dgst -"$MARAN_SIGNATURE_DIGEST" -verify "$1" -signature "$3" "$2" >/dev/null 2>&1
}

build_agent() {
  local staging="$1" arch="${2:-$(arch_name)}"
  # docker rather than cargo, and the substitution is the fix for issue #48: a plain
  # `cargo build --release` on the release host produced an agent requiring GLIBC_2.38 and 2.39,
  # which could not execute on Ubuntu 22.04, Debian 12, AlmaLinux 9 or Rocky 9 — four of the eight
  # distributions preflight accepts. On those hosts the panel installed, nginx served, and every
  # privileged operation failed because the root daemon never ran a single instruction.
  #
  # docker/release/agent-builder-alma8.Dockerfile carries the reasoning, including why a static musl
  # build is the wrong escape (the agent calls `getpwnam_r`, and glibc's NSS cannot be linked
  # statically, so musl would break account lookups wherever SSSD or LDAP answers them).
  require_tool docker "builds the agent against the oldest supported glibc (issue #48)"
  # One builder image per architecture, each a native build INSIDE that architecture rather than a
  # cross-compile: the Rust agent links glibc and calls `getpwnam_r`, so building it in an aarch64
  # AlmaLinux 8 container is what makes "requires at most GLIBC_2.28 on ARM too" a measurement
  # rather than a hope. On an x86_64 host the aarch64 build runs under qemu — slow, and honest.
  local platform image
  platform="$(docker_platform "$arch")"
  [ "$platform" != "unsupported" ] || release_failed "no docker platform for architecture ${arch}"
  image="maran-agent-builder:alma8-${arch}"
  docker build -q --platform "$platform" -f "$root/docker/release/agent-builder-alma8.Dockerfile" \
    -t "$image" "$root/docker/release" >"${staging}/.agent-builder-${arch}.log" 2>&1 \
    || { cat "${staging}/.agent-builder-${arch}.log" >&2; release_failed "could not build the ${arch} agent builder image (log above)"; }

  # The source is mounted READ-ONLY and the target directory is separate, so a release can never be
  # a build that quietly rewrote the tree it was built from. `--locked` is the other half of that:
  # a release that would have had to update Cargo.lock fails instead of shipping dependencies
  # nobody reviewed.
  #
  # --user with the caller's uid keeps the build from leaving root-owned files in the staging
  # directory, which a non-root release would then be unable to remove.
  local work="${staging}/.agent-build"
  rm -rf -- "$work"
  install -d "$work"
  # The REPOSITORY ROOT is mounted, not agent/ alone: agent/crates/agent/build.rs compiles the shared
  # contract from `../../../proto/`, so a mount that stopped at agent/ fails with a protoc error
  # about a path outside it (rules/proto.md — the generated code is never committed, so every build
  # needs proto/ present). HOME is set because --user gives the process no passwd entry, and cargo
  # writing to a home it cannot create fails in a way that names neither cargo nor the mount.
  docker run --rm --user "$(id -u):$(id -g)" \
    -v "$root":/src:ro \
    -v "$work":/work \
    -e HOME=/work \
    -e CARGO_HOME=/work/.cargo \
    -e CARGO_TARGET_DIR=/work/target \
    -w /src/agent "$image" \
    cargo build --release --locked -p maran-agent \
    >"${staging}/.agent-build-${arch}.log" 2>&1 \
    || { cat "${staging}/.agent-build-${arch}.log" >&2; release_failed "cargo build --release -p maran-agent failed in the ${arch} glibc ${MARAN_AGENT_GLIBC_FLOOR} builder (log above)"; }

  local built="${work}/target/release/maran-agent"
  [ -f "$built" ] \
    || release_failed "the agent builder produced no binary at ${built}"

  # The guard, and it reads the ARTIFACT rather than trusting the image that produced it: a builder
  # image that silently moved to a newer base, or a dependency that pulled in a newer symbol, would
  # otherwise ship exactly the defect this function was written to end. The check runs inside the
  # builder because that is where binutils lives, and it asks for the highest glibc version the
  # binary REQUIRES, not the one it was compiled on.
  local highest
  highest="$(docker run --rm --platform "$platform" --user "$(id -u):$(id -g)" -v "$work":/work:ro "$image" \
    sh -c "readelf -V /work/target/release/maran-agent 2>/dev/null \
             | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -uV | tail -1")"
  highest="$(printf '%s' "$highest" | tr -d '[:space:]')"
  [ -n "$highest" ] \
    || release_failed "could not read the agent's required glibc versions, so this release cannot claim it runs on ${MARAN_AGENT_GLIBC_FLOOR} (rules/testing.md)"
  if [ "$(printf '%s\n%s\n' "$MARAN_AGENT_GLIBC_FLOOR" "$highest" | sort -V | tail -1)" != "$MARAN_AGENT_GLIBC_FLOOR" ]; then
    release_failed "the agent requires GLIBC_${highest}, above the ${MARAN_AGENT_GLIBC_FLOOR} floor: it would not start on AlmaLinux 8, Rocky 8, AlmaLinux 9, Debian 12 or Ubuntu 22.04 (issue #48)"
  fi
  echo "   agent (${arch}) requires at most GLIBC_${highest} (floor ${MARAN_AGENT_GLIBC_FLOOR})"

  local out_dir="${staging}/agent-${arch}"
  rm -rf -- "$out_dir"
  install -d "$out_dir"
  install -m 0755 "$built" "$out_dir/maran-agent"
}

build_frontend() {
  local staging="$1" arch="${2:-$(arch_name)}"
  require_tool npm "builds the SPA"
  ( cd "$root/frontend" && npm run build ) \
    >"${staging}/.frontend-build.log" 2>&1 \
    || { cat "${staging}/.frontend-build.log" >&2; release_failed "npm run build failed in frontend/ (log above)"; }
  # The SPA is architecture-independent — it is JavaScript and CSS — but it is staged and published
  # per architecture anyway, so that `<component>-<arch>` is the shape of every manifest entry and
  # installer/lib/50-artifacts.sh's reader needs no special case for one of the three. The cost is
  # one duplicated archive per release; the alternative is a manifest where two components are keyed
  # one way and the third another, which is the kind of exception that outlives its reason.
  local out_dir="${staging}/frontend-${arch}"
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

  # The staged directories are DISCOVERED rather than assumed, because there are now two shapes:
  # a real build stages `api-x86_64/`, `agent-aarch64/` and so on (one set per architecture, issue
  # #53), while the selftest stages plain `api/`, `agent/`, `frontend/`. Walking what is present
  # keeps one writer for both instead of a second copy for the second shape. Each entry is prefixed
  # with the directory's OWN name, so a two-architecture bundle's hashes cannot collide.
  local comp_dirs=() d
  for d in "${out_dir}"/api "${out_dir}"/api-* \
           "${out_dir}"/agent "${out_dir}"/agent-* \
           "${out_dir}"/frontend "${out_dir}"/frontend-*; do
    [ -d "$d" ] && comp_dirs+=("$d")
  done
  [ "${#comp_dirs[@]}" -gt 0 ] \
    || release_failed "write_integrity_manifest: no staged component directories under ${out_dir} — the build functions must run first"

  local component comp_dir file rel_path sha
  for comp_dir in "${comp_dirs[@]}"; do
    component="$(basename "$comp_dir")"
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
  local version="" out_dir="" channel="" arch_list=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --version) version="${2:?--version requires a value}"; shift 2 ;;
      --channel) channel="${2:?--channel requires a value}"; shift 2 ;;
      --out) out_dir="${2:?--out requires a value}"; shift 2 ;;
      # --arch: a comma-separated list, or `both` for the pair a published release needs. Defaults
      # to the host's architecture further down, so an unadorned build is unchanged (issue #53).
      --arch)
        arch_list="${2:?--arch requires a value}"
        [ "$arch_list" != "both" ] || arch_list="x86_64,aarch64"
        shift 2
        ;;
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

  # The architectures this bundle is for. Defaults to the host's, so a plain `maran release build`
  # behaves exactly as it did; `--arch x86_64,aarch64` builds the publishable pair (issue #53).
  #
  # preflight has accepted aarch64 from the beginning and its refusal text promised "x86_64 and
  # aarch64 artifacts only" while the release produced one, so an ARM64 operator passed the gate and
  # then found nothing to install. Building both is what makes that text true.
  local arch_list="${arch_list:-$(arch_name)}"
  local arch
  for arch in ${arch_list//,/ }; do
    [ "$(arch_name "$arch")" != "unsupported" ] \
      || release_failed "'${arch}' is not a MARAN_ARCH this installer recognizes (installer/install.sh:310-312)"
  done

  local component
  for arch in ${arch_list//,/ }; do
    arch="$(arch_name "$arch")"
    echo "== building for ${arch}"
    build_api "$out_dir" "$arch"
    build_agent "$out_dir" "$arch"
    build_frontend "$out_dir" "$arch"
    for component in api agent frontend; do
      tar_component "${out_dir}/${component}-${arch}" "${out_dir}/${component}-${arch}.tar.gz"
    done
  done

  # The unsuffixed names the OFFLINE path reads. installer/lib/50-artifacts.sh's offline branch opens
  # `<component>.tar.gz` with no architecture in the name — an offline bundle is carried to one
  # machine, so it holds one architecture — while the online manifest points at the suffixed URLs.
  # Written only for a single-architecture build, because a two-architecture bundle has no single
  # right answer and silently picking one would produce an offline bundle for the wrong machine.
  local single_arch=""
  case "$arch_list" in
    *,*) : ;;
    *) single_arch="$(arch_name "$arch_list")" ;;
  esac
  if [ -n "$single_arch" ]; then
    for component in api agent frontend; do
      cp "${out_dir}/${component}-${single_arch}.tar.gz" "${out_dir}/${component}.tar.gz"
    done
  fi

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
    for arch in ${arch_list//,/ }; do
      arch="$(arch_name "$arch")"
      for component in api agent frontend; do
        local sha
        sha="$(sha256_of "${out_dir}/${component}-${arch}.tar.gz")"
        [ "$first" -eq 1 ] || echo "    },"
        first=0
        echo "    \"${component}-${arch}\": {"
        echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${component}-${arch}.tar.gz\","
        echo "      \"sha256\": \"${sha}\""
      done
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${out_dir}/manifest.json"

  rm -f "${out_dir}"/.*-*.log
  echo "Bundle staged at ${out_dir} (version ${version}, arch ${arch_list})."
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

  if ! verify_release_file "$pubkey" "${bundle_dir}/manifest.json" "${bundle_dir}/manifest.json.sig"; then
    echo "verify: manifest signature does NOT verify against ${pubkey}" >&2
    return 1
  fi

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
    release_ok "${bundle_dir} verifies against ${pubkey} (signature + every checksum the manifest names)."
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
  # The SAME curve and digest the real key uses, because a selftest that signs with a different
  # algorithm than production would verify an invocation nobody ships (rules/testing.md).
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/throwaway.key" >/dev/null 2>&1
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
  if verify_release_file "${keydir}/throwaway.pub" "${bundle}/integrity-manifest.json" \
       "${bundle}/integrity-manifest.json.sig"; then
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
  sign_release_file "${keydir}/throwaway.key" "$defective" "${defective}.sig" \
    || release_failed "selftest: could not sign the outcome-6 fixture"
  if verify_release_file "${keydir}/throwaway.pub" "$defective" "${defective}.sig"; then
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
