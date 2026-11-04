#!/usr/bin/env bash
# `maran release rebuild`: rebuild the agent from a release tag and compare it with the SIGNED manifest
# (issue #154: "a documented script rebuilds the agent from the tag and matches its hash").
#
# SOURCED by scripts/lib/release-bundle.sh, which supplies the helpers used below: release_ok,
# release_failed, require_tool, sha256_of, arch_name, build_agent, tar_component, verify_release_file,
# VERDICT_PREFIX and the global `root` (this checkout), plus manifest_version and manifest_sha256 from
# release-package.sh and release_source_date_epoch_for from release-reproducible.sh, and
# suite_did_not_run / tree_lock_acquire from suite.sh and tree-lock.sh. It is a library, not a script:
# it has no `set` line and no dispatcher of its own.
#
# THE ORDER IS THE SECURITY PROPERTY (threat note, items 5 and 6; rules/security.md):
#
#   1. manifest.json.sig is verified against the pinned public key BEFORE any value is read from the
#      manifest. A hash file shipped beside the artifacts is worth what its signature is worth, because
#      whoever can replace an artifact can replace an unsigned hash.
#   2. The signed version must equal the tag without its leading `v`.
#   3. The tag is exported with `git archive` into a fresh directory. The working tree is never read, so
#      an uncommitted or untracked file cannot influence what is rebuilt.
#   4. The agent is built from that export (SOURCE_DATE_EPOCH = the tag commit's time) and archived the
#      way a release archives it.
#   5. The rebuilt archive's sha256 is compared with the SIGNED manifest's entry, and with the downloaded
#      archive when the bundle holds one.
#
# WHAT A MATCH CLAIMS: the `agent-<arch>` archive in the signed manifest equals the archive rebuilt here
# from the tag's clean export, with this host's tar and gzip and the builder image whose id is printed.
# WHAT IT DOES NOT CLAIM: that the api and frontend archives are reproducible (they are not rebuilt); or
# that the toolchain is fully pinned. The builder image pins its BASE image by digest, but the packages
# installed into it (gcc, binutils) are not pinned, and the host's tar and gzip affect the archive, so a
# mismatch can come from the toolchain and not from the source. That is why the tool versions and the
# image id are printed with every result.

# The builder seam. release_rebuild_main reaches the real build ONLY through this function, so the
# selftest can redefine it (in a subshell) with a stub and exercise every check around the build without
# docker. Arguments: <export dir> <staging dir> <arch>. Contract: leave the agent output at
# <staging>/agent-<arch>/maran-agent, exit non-zero (after printing why) on failure. The default holds the
# tree lock the way `maran release build` does, points build_agent's global `root` at the clean export
# for the duration (in a subshell, so the caller's `root` is untouched) and runs build_agent.
release_rebuild_build_agent() {
  local export_dir="$1" staging="$2" arch="$3"
  (
    tree_lock_acquire "$root" 0 "release rebuild" \
      || { suite_did_not_run "$VERDICT_PREFIX" "the tree lock is held (named above) — retry once the other lane finishes."; exit "$SUITE_STATUS_DID_NOT_RUN"; }
    trap tree_lock_release EXIT INT TERM
    # shellcheck disable=SC2030
    root="$export_dir"
    build_agent "$staging" "$arch"
  )
}

# release_rebuild_usage: prints the synopsis to stderr and exits 2, the status of a command line the
# tool could not act on.
release_rebuild_usage() {
  cat >&2 <<'USAGE'
usage: maran release rebuild --tag <tag> --bundle <dir> [--arch x86_64|aarch64|both] [--pubkey <path>]
                             [--repo <dir>] [--keep]

  --tag     the release tag to rebuild from, e.g. v1.0.0
  --bundle  directory holding manifest.json, manifest.json.sig and (optionally) the downloaded
            agent-<arch>.tar.gz files, e.g. a directory of the GitHub release's assets
  --arch    architecture to rebuild; default this host's; `both` rebuilds x86_64 and aarch64
            (the foreign one runs under emulation: slow)
  --pubkey  public key the manifest must verify against (default installer/keys/release-signing.pub)
  --repo    git repository to export the tag from (default: this checkout)
  --keep    keep the temporary export and staging directory and print its path
USAGE
  exit 2
}

# release_rebuild_arches: <--arch value>. Prints the architectures to rebuild, one per line; returns 1
# for a value this release does not build for. An empty value means this host's architecture.
release_rebuild_arches() {
  local arch
  if [ "${1:-}" = "both" ]; then
    printf '%s\n' x86_64 aarch64
    return 0
  fi
  arch="$(arch_name "${1:-}")"
  [ "$arch" != "unsupported" ] || return 1
  printf '%s\n' "$arch"
}

# release_rebuild_require_signed_manifest: <public key> <bundle dir>. Step 1. Refuses a bundle that
# lacks either file and a manifest whose signature does not verify. It reads NOTHING from the manifest:
# the first read happens only after this returns, which is what makes every later value trustworthy.
release_rebuild_require_signed_manifest() {
  local pubkey="$1" bundle="$2"
  [ -f "$pubkey" ] || release_failed "public key not found at ${pubkey}"
  [ -f "${bundle}/manifest.json" ] || release_failed "no manifest.json in ${bundle}"
  [ -f "${bundle}/manifest.json.sig" ] || release_failed "no manifest.json.sig in ${bundle}: an unsigned manifest is not evidence of anything"
  verify_release_file "$pubkey" "${bundle}/manifest.json" "${bundle}/manifest.json.sig" \
    || release_failed "the signature of manifest.json does not verify against ${pubkey}: no value was read from the manifest and nothing was rebuilt"
}

# release_rebuild_require_version: <manifest> <tag>. Step 2, run only on a verified manifest. The signed
# version must equal the tag without a leading `v`; a manifest signed for another version is a valid
# signature over the wrong release, which a hash comparison alone would not notice.
release_rebuild_require_version() {
  local manifest="$1" tag="$2" version
  version="$(manifest_version "$manifest")" \
    || release_failed "manifest.json carries no usable \"version\" field"
  [ "$version" = "${tag#v}" ] \
    || release_failed "manifest.json is signed for version '${version}' but the tag ${tag} means '${tag#v}': this bundle is not that release"
}

# release_rebuild_resolve_tag: <repo> <tag>. Prints the commit the tag names. Returns 1 when the tag is
# not a tag of that repository; `refs/tags/` is spelled out so a branch or a bare hash of the same
# spelling cannot stand in for it.
release_rebuild_resolve_tag() {
  git -C "$1" rev-parse --verify --quiet "refs/tags/${2}^{commit}"
}

# release_rebuild_check_reachability: <repo> <tag> <commit>. A SOFT check, printed and never a refusal:
# the release workflow refuses a tag that is not on main, but a maintainer may rebuild offline with a
# stale or absent origin/main, and refusing there would make the rebuild unusable exactly when it is
# wanted. The reproducibility verdict does not depend on it; the hash comparison does the work.
release_rebuild_check_reachability() {
  local repo="$1" tag="$2" commit="$3"
  if ! git -C "$repo" rev-parse --verify --quiet refs/remotes/origin/main >/dev/null; then
    echo "note: refs/remotes/origin/main does not exist in ${repo}; whether ${tag} is on main was not checked." >&2
    return 0
  fi
  git -C "$repo" merge-base --is-ancestor "$commit" refs/remotes/origin/main \
    || echo "WARNING: ${tag} (${commit}) is not reachable from origin/main. The release workflow would have refused it; a match below is still a match, but ask why this tag is off main." >&2
  return 0
}

# release_rebuild_export: <repo> <commit> <dest>. Step 3, a clean export of one commit. `git archive`
# reads the object database, not the working tree, so nothing uncommitted, untracked or ignored can be
# in the result.
release_rebuild_export() {
  local repo="$1" commit="$2" dest="$3"
  install -d "$dest"
  git -C "$repo" archive --format=tar "$commit" | tar -x -C "$dest"
}

# release_rebuild_build_tarball: <export dir> <epoch> <arch> <staging> <out tarball>. Step 4, in a
# subshell so SOURCE_DATE_EPOCH reaches the builder and the archiver and goes no further. Every call that
# can fail is followed by an explicit `|| exit`: this runs under the caller's `||`, where bash disables
# errexit, and a failed build must not fall through to archiving an old or missing output.
release_rebuild_build_tarball() {
  local export_dir="$1" epoch="$2" arch="$3" staging="$4" out_tar="$5"
  install -d "$staging" || return 1
  (
    export SOURCE_DATE_EPOCH="$epoch"
    release_rebuild_build_agent "$export_dir" "$staging" "$arch" || exit "$?"
    [ -f "${staging}/agent-${arch}/maran-agent" ] \
      || release_failed "the ${arch} build left no ${staging}/agent-${arch}/maran-agent"
    tar_component "${staging}/agent-${arch}" "$out_tar" || exit "$?"
  )
}

# release_rebuild_explain_difference: <rebuilt tarball> <other tarball> <label of other>. Prints, to
# stderr, WHERE two archives differ: the first differing lines of their member listings (name, size,
# mode, owner, time), or, when the listings agree, the first differing byte of the decompressed stream,
# or, when that agrees too, that only the gzip framing differs.
release_rebuild_explain_difference() {
  local rebuilt="$1" other="$2" label="$3" first offset
  first="$(diff <(tar -tvzf "$rebuilt" 2>&1) <(tar -tvzf "$other" 2>&1) | grep -m2 '^[<>]')" || true
  if [ -n "$first" ]; then
    echo "  first differing archive members ('<' rebuilt, '>' ${label}):" >&2
    printf '    %s\n' "$first" >&2
    return 0
  fi
  offset="$(cmp <(gzip -dc "$rebuilt" 2>/dev/null) <(gzip -dc "$other" 2>/dev/null) 2>&1)" || true
  if [ -n "$offset" ]; then
    echo "  member listings agree; decompressed content differs (${offset})" >&2
  else
    echo "  member listings and decompressed content agree; only the gzip framing differs" >&2
  fi
}

# release_rebuild_compare: <tag> <arch> <rebuilt tarball> <signed manifest sha256> <downloaded tarball or
# empty>. Step 5 for one architecture. Prints the verdict line on a match; on any mismatch prints every
# hash involved and where the archives differ, and returns 1. The caller decides when to stop, so a
# `--arch both` run reports both architectures before it fails.
release_rebuild_compare() {
  local tag="$1" arch="$2" rebuilt="$3" signed="$4" downloaded="$5" rebuilt_sha downloaded_sha ok=0
  rebuilt_sha="$(sha256_of "$rebuilt")"
  if [ "$rebuilt_sha" != "$signed" ]; then
    ok=1
    echo "MISMATCH agent ${arch} from ${tag}:" >&2
    echo "  rebuilt sha256:         ${rebuilt_sha}" >&2
    echo "  signed manifest sha256: ${signed}" >&2
  fi
  if [ -n "$downloaded" ]; then
    downloaded_sha="$(sha256_of "$downloaded")"
    if [ "$downloaded_sha" != "$rebuilt_sha" ]; then
      ok=1
      echo "MISMATCH downloaded agent-${arch}.tar.gz:" >&2
      echo "  rebuilt sha256:         ${rebuilt_sha}" >&2
      echo "  downloaded sha256:      ${downloaded_sha}" >&2
      release_rebuild_explain_difference "$rebuilt" "$downloaded" "downloaded"
      release_rebuild_compare_binary "$rebuilt" "$downloaded"
    fi
  else
    echo "note: no downloaded agent-${arch}.tar.gz in the bundle; compared with the signed manifest only." >&2
  fi
  [ "$ok" -eq 0 ] || return 1
  echo "  ok: agent ${arch} from ${tag} rebuilt identical to the signed manifest (sha256 ${rebuilt_sha})"
}

# release_rebuild_binary_sha: <tarball>. Prints the sha256 of the maran-agent member, or nothing when the
# archive has no such member. Secondary evidence only: the verdict is against the signed archive hash.
release_rebuild_binary_sha() {
  tar -tzf "$1" ./maran-agent >/dev/null 2>&1 || return 0
  tar -xzOf "$1" ./maran-agent | sha256sum | awk '{print $1}'
}

# release_rebuild_compare_binary: <rebuilt tarball> <other tarball>. Reports whether the extracted
# maran-agent binaries match although the archives do not, which separates "the compiler produced a
# different binary" from "only the archive framing (tar, gzip, metadata) differs". It changes no verdict.
release_rebuild_compare_binary() {
  local mine theirs
  mine="$(release_rebuild_binary_sha "$1")"
  theirs="$(release_rebuild_binary_sha "$2")"
  if [ -z "$mine" ] || [ -z "$theirs" ]; then
    echo "  the maran-agent binaries could not be compared: an archive has no ./maran-agent member (secondary information)" >&2
  elif [ "$mine" = "$theirs" ]; then
    echo "  the extracted maran-agent binaries MATCH (sha256 ${mine}): only the archive differs (secondary information)" >&2
  else
    echo "  the extracted maran-agent binaries DIFFER (rebuilt ${mine}, other ${theirs}) (secondary information)" >&2
  fi
}

# release_rebuild_print_environment: <arch>. Prints what besides the source decides the bytes: the host's
# tar and gzip versions and the id of the builder image build_agent tags for the architecture. It names
# the image by the tag build_agent uses; a host without docker (a stubbed run) prints that instead.
release_rebuild_print_environment() {
  echo "  tar:  $(tar --version | head -1)"
  echo "  gzip: $(gzip --version 2>&1 | head -1)"
  if command -v docker >/dev/null 2>&1; then
    echo "  builder image maran-agent-builder:alma8-${1}: $(docker image inspect --format '{{.Id}}' "maran-agent-builder:alma8-${1}" 2>/dev/null || echo 'not present')"
  else
    echo "  builder image: docker is not on PATH, so no image id"
  fi
}

# release_rebuild_signed_sha: <manifest> <arch>. Prints the signed manifest's agent sha256 for the
# architecture; refuses (exit, via release_failed) when it is absent or not 64 lower-case hex digits.
release_rebuild_signed_sha() {
  local sha
  sha="$(manifest_sha256 "$1" agent "$2")"
  case "$sha" in
    ""|*[!0-9a-f]*) release_failed "the signed manifest has no valid sha256 for agent-${2}" ;;
  esac
  [ "${#sha}" -eq 64 ] || release_failed "the signed manifest's sha256 for agent-${2} is not 64 hex characters"
  printf '%s' "$sha"
}

# release_rebuild_main: the whole command, `maran release rebuild ...`. Runs the five steps in the order
# the header gives. Exits 0 only when every requested architecture rebuilt identical to the signed manifest; 1 on any
# refusal or mismatch (via release_failed); 2 on a bad command line or a held tree lock.
release_rebuild_main() {
  local tag="" bundle="" arch_opt="" pubkey="" repo="" keep=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --tag) tag="${2:?--tag requires a value}"; shift 2 ;;
      --bundle) bundle="${2:?--bundle requires a value}"; shift 2 ;;
      --arch) arch_opt="${2:?--arch requires a value}"; shift 2 ;;
      --pubkey) pubkey="${2:?--pubkey requires a value}"; shift 2 ;;
      --repo) repo="${2:?--repo requires a value}"; shift 2 ;;
      --keep) keep=1; shift ;;
      *) echo "release rebuild: unknown argument: $1" >&2; release_rebuild_usage ;;
    esac
  done
  [ -n "$tag" ] && [ -n "$bundle" ] || release_rebuild_usage
  # shellcheck disable=SC2031
  : "${pubkey:=${root}/installer/keys/release-signing.pub}" "${repo:=${root}}"
  case "$tag" in
    -*|*[!A-Za-z0-9._+-]*) release_failed "'${tag}' is not a tag name this release uses" ;;
  esac
  local arches
  arches="$(release_rebuild_arches "$arch_opt")" \
    || { echo "release rebuild: --arch must be x86_64, aarch64 or both, got: ${arch_opt}" >&2; exit 2; }
  require_tool git "exports the tag"
  require_tool openssl "verifies the manifest signature"
  declare -F release_source_date_epoch_for >/dev/null \
    || release_failed "release_source_date_epoch_for is not defined: scripts/lib/release-reproducible.sh was not sourced"

  # 1 and 2: nothing below runs on a manifest that has not verified.
  release_rebuild_require_signed_manifest "$pubkey" "$bundle"
  release_rebuild_require_version "${bundle}/manifest.json" "$tag"

  # 3: the tag, exported clean.
  local commit
  commit="$(release_rebuild_resolve_tag "$repo" "$tag")" \
    || release_failed "tag ${tag} does not exist in ${repo}: fetch tags and retry (git fetch --tags)"
  release_rebuild_check_reachability "$repo" "$tag" "$commit"
  local work epoch
  work="$(mktemp -d)"
  if [ "$keep" -eq 1 ]; then
    echo "kept: ${work}"
  else
    # shellcheck disable=SC2064
    trap "rm -rf -- '${work}'" EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
  fi
  release_rebuild_export "$repo" "$commit" "${work}/export" \
    || release_failed "git archive of ${tag} (${commit}) failed"
  epoch="$(release_source_date_epoch_for "$commit" "$repo")" \
    || release_failed "could not read the commit time of ${tag}"
  case "$epoch" in
    ""|*[!0-9]*) release_failed "release_source_date_epoch_for printed '${epoch}', not a unix time" ;;
  esac
  echo "rebuilding the agent from ${tag} (${commit}), SOURCE_DATE_EPOCH=${epoch}, clean export in ${work}/export"

  # Every architecture's signed hash is validated BEFORE any build starts, so a bad entry for the second
  # architecture does not cost an hour of building the first.
  local arch signed downloaded rc summary="" failed=""
  for arch in $arches; do
    release_rebuild_signed_sha "${bundle}/manifest.json" "$arch" >/dev/null
  done

  # 4 and 5, per architecture.
  for arch in $arches; do
    signed="$(release_rebuild_signed_sha "${bundle}/manifest.json" "$arch")" || exit "$?"
    downloaded=""
    [ ! -f "${bundle}/agent-${arch}.tar.gz" ] || downloaded="${bundle}/agent-${arch}.tar.gz"
    rc=0
    release_rebuild_build_tarball "${work}/export" "$epoch" "$arch" "${work}/staging-${arch}" \
      "${work}/agent-${arch}.tar.gz" || rc=$?
    [ "$rc" -eq 0 ] || exit "$rc"
    release_rebuild_print_environment "$arch"
    if release_rebuild_compare "$tag" "$arch" "${work}/agent-${arch}.tar.gz" "$signed" "$downloaded"; then
      summary="${summary:+${summary}, }${arch} sha256 ${signed}"
    else
      failed="${failed} ${arch}"
    fi
  done
  [ -z "$failed" ] \
    || release_failed "the agent rebuilt from ${tag} does not match for:${failed}. Both hashes are above; the manifest's signature is valid, so this is a build that differs from the signed release (source, toolchain or archive tooling), not a forged manifest."
  release_ok "agent from ${tag} rebuilt identical to the signed manifest (${summary})"
  echo "CLAIMED: the archive(s) above equal the one(s) the signed manifest names, rebuilt from ${tag}'s clean export with the tool versions and builder image id printed above."
  echo "NOT CLAIMED: the api and frontend archives were not rebuilt and are not reproducible; the builder image pins only its base image by digest (installed packages such as gcc and binutils are not pinned), and host tar and gzip versions affect the archive."
}
