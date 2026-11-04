#!/usr/bin/env bash
# Opt-in deterministic mode for scripts/lib/release-bundle.sh (issue #154), sourced by it.
#
# Deterministic mode is on exactly when SOURCE_DATE_EPOCH is set. Unset, every function here is
# bypassed and a release builds byte-for-byte as it did before this file existed, so no existing
# selftest or published bundle changes meaning.
#
# What it makes reproducible, and what it does not (docs/superpowers/notes/
# 2026-11-04-release-pipeline-threat-note.md, item 6): the component TARBALL (same staged
# directory, same bytes) and the agent's build inputs (a fixed clock and no build-directory path
# embedded in the binary). The agent binary's own determinism is a property of cargo and the
# builder image and is measured by `maran release rebuild`, not asserted here. The API publish
# and the SPA bundle are NOT claimed reproducible.
#
# Needs from the caller (release-bundle.sh): release_failed, tar_component, require_tool.

# release_check_source_date_epoch: refuses a SOURCE_DATE_EPOCH value that is not plain digits.
# An empty value is refused too: "set but empty" would otherwise read as "unset" and silently
# build a non-deterministic bundle that the caller believes is deterministic.
release_check_source_date_epoch() {
  case "${1-}" in
    ''|*[!0-9]*) release_failed "SOURCE_DATE_EPOCH must be digits only (seconds since the epoch), got '${1-}'" ;;
  esac
}

# release_source_date_epoch_for: <git-ref> <repo-root>. Prints the committer time of the ref's
# commit, the value a tag's release is built with, so a rebuild from the tag derives the same
# clock the release used. Refuses an unknown ref or a non-numeric answer.
release_source_date_epoch_for() {
  local ref="${1:?release_source_date_epoch_for: a git ref is required}"
  local repo="${2:?release_source_date_epoch_for: a repository root is required}"
  local epoch
  epoch="$(git -C "$repo" log -1 --format=%ct "$ref" 2>/dev/null)" \
    || release_failed "cannot read the commit time of '${ref}' in ${repo}"
  release_check_source_date_epoch "$epoch"
  printf '%s\n' "$epoch"
}

# release_tar_deterministic: <src_dir> <dest_tar>. Writes the CONTENTS of src_dir at the archive
# root, as tar_component does, but from an explicit byte-sorted file list with every
# machine-dependent field pinned: mtime to SOURCE_DATE_EPOCH, owner and group to numeric 0,
# group/other write bits cleared (so a umask of 002 on the builder cannot change a mode), the GNU
# format (which carries no atime/ctime extended headers) and `gzip -n` (no name or mtime in the
# gzip header). Directory order on disk and file mtimes therefore cannot reach the output.
release_tar_deterministic() {
  local src_dir="$1" dest_tar="$2"
  release_check_source_date_epoch "${SOURCE_DATE_EPOCH-}"
  tar --version 2>/dev/null | grep -q 'GNU tar' \
    || release_failed "deterministic mode needs GNU tar (--sort-free file list, --mtime, --owner, --mode); this tar is not GNU tar"
  local dest_dir dest_abs
  dest_dir="$(dirname "$dest_tar")"
  [ -d "$dest_dir" ] || release_failed "deterministic archive destination directory ${dest_dir} does not exist"
  dest_dir="$(cd "$dest_dir" && pwd)" || release_failed "cannot enter ${dest_dir}"
  dest_abs="${dest_dir}/$(basename "$dest_tar")"
  ( cd "$src_dir" \
      && find . -print0 | LC_ALL=C sort -z \
      | tar --null --no-recursion --files-from=- --format=gnu \
            --mtime="@${SOURCE_DATE_EPOCH}" --owner=0 --group=0 --numeric-owner --mode=go-w -cf - \
      | gzip -n > "$dest_abs" ) \
    || release_failed "could not write the deterministic archive ${dest_tar}"
}

# release_reproducible_docker_env: <array-name>. Fills the named array with the `-e` arguments
# build_agent adds to its docker run in deterministic mode, and leaves it empty otherwise: the
# fixed clock, and a prefix remap for every directory cargo can embed. The remapped prefixes are
# /src (the mounted tree), /work/.cargo (CARGO_HOME, so registry sources), /work/target
# (CARGO_TARGET_DIR) and /opt/rust (the toolchain's own home). They are disjoint on purpose, so
# the result does not depend on which of two overlapping remaps rustc prefers.
release_reproducible_docker_env() {
  local -n out_args="$1"
  out_args=()
  [ "${SOURCE_DATE_EPOCH+x}" = "x" ] || return 0
  release_check_source_date_epoch "$SOURCE_DATE_EPOCH"
  out_args=(
    -e "SOURCE_DATE_EPOCH=${SOURCE_DATE_EPOCH}"
    -e "RUSTFLAGS=--remap-path-prefix=/src=/build/src --remap-path-prefix=/work/.cargo=/build/cargo --remap-path-prefix=/work/target=/build/target --remap-path-prefix=/opt/rust=/build/toolchain"
  )
}

# release_reproducible_selftest: proves the archive half of deterministic mode. The same scratch
# tree is archived twice with SOURCE_DATE_EPOCH set; between the runs the tree is rebuilt with
# its files created in the opposite order, so inode and directory order change, and every mtime
# is touched to a different value. The archives must be byte-identical. The inverse control runs
# the unchanged tar_component on the same two trees: those archives are expected to differ; if
# they happen not to, that is reported as not a witness rather than claimed as a catch.
release_reproducible_selftest() {
  local scratch first second i
  scratch="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf -- '$scratch'" RETURN
  first="${scratch}/a"; second="${scratch}/b"
  local names=(alpha.txt beta.txt gamma.txt sub/delta.txt sub/epsilon.txt) n
  install -d "${first}/sub" "${second}/sub"
  for n in "${names[@]}"; do printf 'content of %s\n' "$n" > "${first}/${n}"; done
  for ((i = ${#names[@]} - 1; i >= 0; i--)); do
    printf 'content of %s\n' "${names[i]}" > "${second}/${names[i]}"
  done
  chmod 0755 "${first}/sub" "${second}/sub"
  touch -d '2001-01-01 00:00:00' "${first}"/*.txt "${first}"/sub/*.txt
  touch -d '2030-06-15 12:34:56' "${second}"/*.txt "${second}"/sub/*.txt

  SOURCE_DATE_EPOCH=1700000000 tar_component "$first" "${scratch}/a.tar.gz"
  SOURCE_DATE_EPOCH=1700000000 tar_component "$second" "${scratch}/b.tar.gz"
  cmp -s "${scratch}/a.tar.gz" "${scratch}/b.tar.gz" \
    || release_failed "reproducible selftest: the same tree archived twice with SOURCE_DATE_EPOCH differs"
  # Positive control on the axis that can go blind: the archive must hold the planted files, or
  # "identical" would be two identical empty archives.
  [ "$(tar -tzf "${scratch}/a.tar.gz" | grep -c 'txt$')" -eq "${#names[@]}" ] \
    || release_failed "reproducible selftest: the deterministic archive does not list all ${#names[@]} planted files"
  echo "   ok: two builds of one tree, different mtimes and creation order, are byte-identical"

  # The epoch must reach the bytes: a different clock is a different archive.
  SOURCE_DATE_EPOCH=1700000001 tar_component "$first" "${scratch}/c.tar.gz"
  ! cmp -s "${scratch}/a.tar.gz" "${scratch}/c.tar.gz" \
    || release_failed "reproducible selftest: changing SOURCE_DATE_EPOCH did not change the archive, so the clock is not what is pinned"
  echo "   ok: a different SOURCE_DATE_EPOCH yields a different archive"

  # Inverse controls: a non-numeric epoch is refused, and with no epoch the old path is used.
  local refusal
  refusal="$( ( SOURCE_DATE_EPOCH='12ab' tar_component "$first" "${scratch}/d.tar.gz" ) 2>&1 )" \
    && release_failed "reproducible selftest: a non-numeric SOURCE_DATE_EPOCH was accepted"
  case "$refusal" in
    *"must be digits only"*) echo "   ok: a non-numeric SOURCE_DATE_EPOCH is refused with its own message" ;;
    *) release_failed "reproducible selftest: refused, but not for the epoch (got: ${refusal})" ;;
  esac
  # The docker environment: empty when the mode is off, the clock plus the remaps when it is on.
  local env_off env_on
  ( unset SOURCE_DATE_EPOCH; release_reproducible_docker_env env_off; [ "${#env_off[@]}" -eq 0 ] ) \
    || release_failed "reproducible selftest: docker env is not empty with SOURCE_DATE_EPOCH unset"
  SOURCE_DATE_EPOCH=1700000000 release_reproducible_docker_env env_on
  [ "${#env_on[@]}" -eq 4 ] && [[ "${env_on[*]}" == *"SOURCE_DATE_EPOCH=1700000000"* && "${env_on[*]}" == *"--remap-path-prefix=/src="* ]] \
    || release_failed "reproducible selftest: docker env with SOURCE_DATE_EPOCH set is not the clock plus the path remaps"
  echo "   ok: the docker environment is empty when unset, 4 arguments (clock, remaps) when set"
  ( unset SOURCE_DATE_EPOCH; tar_component "$first" "${scratch}/e.tar.gz"; tar_component "$second" "${scratch}/f.tar.gz" )
  if cmp -s "${scratch}/e.tar.gz" "${scratch}/f.tar.gz"; then
    echo "   not a witness: without SOURCE_DATE_EPOCH the two trees happened to archive identically"
  else
    echo "   ok: without SOURCE_DATE_EPOCH the same two trees differ (the control the mode removes)"
  fi
  release_ok "reproducible selftest passed — UNOBSERVED HERE: the agent binary's own determinism is not run (needs docker)"
}
