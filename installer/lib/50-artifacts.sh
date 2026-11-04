#!/usr/bin/env bash
# Step 50: obtain the release (maran-api, maran-agent, frontend bundle), verify it and place it in
# the release layout. This file only sequences the steps; everything that decides whether bytes are
# trusted or where they go lives in the `maran` command's libraries (installer/cli/lib/), which the
# installer package carries and this step sources, so the first install and `maran update` apply
# one implementation of each rule and cannot drift apart:
#   release-version.sh  version / release-name validation and ordering
#   release-verify.sh   fetch the manifest, verify its ECDSA P-384 signature (SHA-384 digest) with
#                       the package's public key, fetch artifacts, check each sha256, offline bundles
#   release-unpack.sh   vet an archive's members, extract it safely
#   release-layout.sh   releases/, `current`, static links, release.state, legacy adoption
# An artifact that cannot be verified aborts the install (cli_fail exits 1): this is the installer's
# only trust boundary against a compromised mirror or a man-in-the-middle, so it is never bypassed
# and never made a warning. Also supports a fully offline install from a local tarball.
#
# Release layout this step produces (shared with `maran update` and `maran rollback`):
#   /usr/local/maran/releases/<version>/{api,agent,frontend}   root:root, never written afterwards
#   /usr/local/maran/current -> releases/<version>             swapped atomically (symlink + mv -T)
#   /usr/local/maran/{api,agent,frontend} -> current/<c>       static, so no unit or vhost path changes
#   /etc/maran/release.state                                   CHANNEL, VERSION, PREVIOUS
# A host installed before this layout (real directories at /usr/local/maran/{api,agent,frontend})
# is adopted on the way: its directories move into releases/<name> (cli_layout_adopt_legacy).
#
# Re-running the installer (every case ends with `current`, the static links and release.state agreeing):
#   - a host with no layout (fresh install): the release is unpacked, `current` and the links are made.
#   - the SAME version as the one `current` points at, or as PREVIOUS: cli_layout_install_release refuses
#     `replace` for both, so the tree already on disk is KEPT, but only after it passes
#     release_tree_problem (the api and agent binaries, frontend/index.html, three real component
#     directories, and no entry that is not root's, is group/world writable or is a link). A tree that
#     fails is REPAIRED, never silently accepted and never left for a service to trip over: the freshly
#     verified archives are unpacked into releases/<version>.partial, published under the temporary
#     release name <version>-repair, `current` is switched to it when it named the damaged tree, the
#     damaged directory is replaced by a hard-linked copy of the repaired one, `current` is switched
#     back, and the temporary name is removed. `current` names an existing release at every instant, so
#     there is no moment at which a restart finds it dangling.
#   - a DIFFERENT, newer version: unpacked beside the others; the release `current` named becomes
#     PREVIOUS (the rollback target). An abandoned directory of the same name left by a failed update is
#     replaced; one that is `current` or PREVIOUS is never replaced (see above).
#   - a LOWER version than the one installed is refused: a replayed older signed bundle must not turn a
#     re-run of the installer into a downgrade. `maran rollback` is the verb that goes backwards.
#   - a pre-layout host: its real directories are adopted into releases/legacy-<stamp> first, and that
#     name becomes PREVIOUS.
# CHANNEL: online it is the requested channel (the fetch refuses a manifest signed for another one);
# when no --channel was given and release.state already records one, that recorded channel is the
# requested one (resolve_channel), so a re-run never moves a beta host to stable by omission;
# offline it is the channel the signed manifest declares, since an operator who typed no --channel must
# not have a beta bundle recorded as stable.
# An offline tarball must sit in a directory that is owned by root and not group/world writable (the
# CLI copies it once and trusts only the copy); /tmp does not qualify, move the file to /root.
#
# The libraries are sourced here, not in install.sh, because they hold definitions only: common.sh
# and the others execute nothing at source time except common.sh's scrub of the environment
# variables that could redirect openssl, curl, tar or the dynamic loader (OPENSSL_CONF, CURL_HOME,
# TAR_OPTIONS, LD_PRELOAD, PG* and the like), which is wanted for every command this installer runs
# as root from here on.
set -euo pipefail

# The libraries ship in the installer package next to this step's installer (SCRIPT_DIR, exported
# by install.sh), the same directory 66-cli.sh installs them from.
: "${SCRIPT_DIR:?must be set by install.sh}"
for MARAN_ARTIFACTS_LIB in common release-version release-verify release-unpack release-layout; do
  [ -f "${SCRIPT_DIR}/cli/lib/${MARAN_ARTIFACTS_LIB}.sh" ] \
    || { echo "50-artifacts.sh: ${SCRIPT_DIR}/cli/lib/${MARAN_ARTIFACTS_LIB}.sh" \
           "is missing from the installer package." >&2
         exit 1; }
  # shellcheck disable=SC1090
  . "${SCRIPT_DIR}/cli/lib/${MARAN_ARTIFACTS_LIB}.sh"
done
unset MARAN_ARTIFACTS_LIB

# MARAN_PACKAGE_RELEASE_KEY: the ECDSA P-384 public key that signs release manifests, as shipped in
# the installer package and handed to the verifier as an argument (never fetched at install time:
# an attacker who could serve the installer AND a fake manifest would otherwise control both halves
# of the trust check). Resolved against the package, NOT the install root: /usr/local/maran is
# populated by THIS step, so a key looked up there could never exist on a first install. 66-cli.sh
# installs the same file for `maran update`, which is how both verify against one key.
readonly MARAN_PACKAGE_RELEASE_KEY="${SCRIPT_DIR}/keys/release-signing.pub"

# MARAN_ARTIFACT_TMP: staging for the manifest and archives between verification and extraction.
# It is deliberately NOT under /var/lib/maran: that directory is created maran:maran 0750 by
# 40-user.sh and is in maran-api.service's ReadWritePaths=, so the unprivileged uid the api
# runs as owns it and can place any entry inside it, a symlink or a directory of its own at
# the staging name. Root would then download into a directory the api controls, and because
# every archive is checksummed before ANY of them is extracted, the api could swap
# agent.tar.gz in that window; the extracted file becomes /usr/local/maran/agent/maran-agent,
# which maran-agent.service runs as root. Staging lives directly under /var/lib instead
# (root:root 0755), where no unprivileged uid can create an entry at all.
readonly MARAN_ARTIFACT_TMP="/var/lib/maran-artifact-staging"

# prepare_staging_dir: creates the staging directory as a fresh, root-owned, root-only
# directory, and refuses to continue if it is anything else. `mkdir -p` was not enough on its
# own: it exits 0 on an existing path, following a symlink and checking neither owner nor
# mode, so it silently accepts a directory an attacker put there first. The leading `rm -rf`
# removes a stale directory or a planted symlink (removing a symlink unlinks the link, never
# its target), `install -d` then creates a real directory, and the gate below is what makes
# this a refusal rather than a hope: a staging path that is a symlink, is not a directory, is
# not owned by uid 0, or is group/other-accessible aborts the install instead of being used.
prepare_staging_dir() {
  local owner mode
  rm -rf -- "$MARAN_ARTIFACT_TMP"
  install -d -o root -g root -m 0700 "$MARAN_ARTIFACT_TMP"
  if [ -L "$MARAN_ARTIFACT_TMP" ] || [ ! -d "$MARAN_ARTIFACT_TMP" ]; then
    echo "50-artifacts.sh: staging path ${MARAN_ARTIFACT_TMP} is not a real directory. Aborting." >&2
    exit 1
  fi
  owner="$(stat -c '%u' "$MARAN_ARTIFACT_TMP")"
  mode="$(stat -c '%a' "$MARAN_ARTIFACT_TMP")"
  if [ "$owner" -ne 0 ] || [ "$mode" != "700" ]; then
    echo "50-artifacts.sh: staging directory ${MARAN_ARTIFACT_TMP} must be owned by root with mode 0700" >&2
    echo "  (found owner uid ${owner}, mode ${mode}). Refusing to stage release artifacts in a" >&2
    echo "  directory another uid can write to. Aborting." >&2
    exit 1
  fi
}

# stage_release: fills the staging directory with a verified manifest and the three archives,
# either from the release host (the channel's signed manifest, whose signed channel field must
# equal the requested one) or from the operator's offline bundle (which carries no URL to disagree
# with). Both paths end in the same state: manifest.json, manifest.json.sig and <component>.tar.gz
# whose signature and sha256 have been checked against the package key. cli_layout_install_release
# checks each sha256 again immediately before extracting it.
#
# The offline verifier is run in a subshell with errexit set INSIDE it and switched off around it
# (a plain `|| ...` would disable errexit for everything the library does), so its own messages are
# kept and, when it refused the tarball's directory, the operator is told what to do about it.
stage_release() {
  local out status
  if [ -n "${MARAN_OFFLINE_TARBALL:-}" ]; then
    set +e
    out="$( ( set -e; cli_release_verify_offline "$MARAN_OFFLINE_TARBALL" "$MARAN_ARTIFACT_TMP" \
      "$MARAN_PACKAGE_RELEASE_KEY" ) 2>&1 )"
    status=$?
    set -e
    [ -z "$out" ] || printf '%s\n' "$out" >&2
    if [ "$status" -ne 0 ]; then
      case "$out" in
        *"holding the offline tarball"*)
          echo "install.sh: an offline tarball is only read from a directory that root owns and nobody else can" >&2
          echo "  write. A directory such as /tmp, a user's home or a shared download folder does not qualify." >&2
          echo "  Move the file to a root-owned directory, for example: mv ${MARAN_OFFLINE_TARBALL} /root/" >&2
          echo "  and run the installer again with --offline-tarball /root/$(basename -- "$MARAN_OFFLINE_TARBALL")." >&2
          ;;
      esac
      exit "$status"
    fi
    echo "Offline bundle verified."
  else
    cli_release_fetch_manifest "$MARAN_CHANNEL" "$MARAN_ARTIFACT_TMP" "$MARAN_PACKAGE_RELEASE_KEY"
    echo "Release manifest signature verified."
    cli_release_fetch_artifacts "${MARAN_ARTIFACT_TMP}/manifest.json" "$MARAN_ARTIFACT_TMP"
    echo "Artifacts downloaded and verified."
  fi
}

# resolve_channel: settles MARAN_CHANNEL before anything is fetched. An explicit --channel
# (MARAN_CHANNEL_GIVEN=1, set only by install.sh's flag) always wins. Without it, a host whose
# release.state records a channel keeps that channel; a fresh host keeps install.sh's default.
resolve_channel() {
  local recorded
  [ "${MARAN_CHANNEL_GIVEN:-0}" != "1" ] || return 0
  recorded="$(cli_layout_state_read CHANNEL)" || exit 1
  [ -z "$recorded" ] || MARAN_CHANNEL="$recorded"
}

# staged_channel: prints the channel to record in release.state. Online it is the requested channel
# (the fetch already refused any other). Offline the signed manifest's own channel wins; an old bundle
# with no channel field records the requested one.
staged_channel() {
  local declared
  declared="$(cli_release_manifest_field "${MARAN_ARTIFACT_TMP}/manifest.json" channel)" || exit 1
  if [ -z "$declared" ] || [ "$declared" = "$MARAN_CHANNEL" ]; then
    printf '%s\n' "${declared:-$MARAN_CHANNEL}"
  elif [ -n "${MARAN_OFFLINE_TARBALL:-}" ]; then
    echo "Offline bundle is for channel ${declared}; recording that channel." >&2
    printf '%s\n' "$declared"
  else
    cli_fail "asked for channel ${MARAN_CHANNEL} but the signed manifest is for ${declared}"
  fi
}

# release_tree_problem VERSION: prints why releases/VERSION cannot be started from, and prints nothing
# when it can. It asks for what the units execute (api/Maran.Host and agent/maran-agent as regular,
# executable, non-link files; frontend/index.html as a regular file), for the three component
# directories to be real directories, and for a tree only root can change: any entry that is a link,
# not owned by root:root or writable by group or others is a problem. Always returns 0.
release_tree_problem() {
  local base="${MARAN_LAYOUT_ROOT}/releases/${1}" entry found
  for entry in api agent frontend; do
    [ -d "${base}/${entry}" ] && [ ! -L "${base}/${entry}" ] || { echo "${entry}/ is not a real directory"; return 0; }
  done
  for entry in api/Maran.Host agent/maran-agent; do
    [ -f "${base}/${entry}" ] && [ ! -L "${base}/${entry}" ] && [ -x "${base}/${entry}" ] \
      || { echo "no executable ${entry}"; return 0; }
  done
  [ -f "${base}/frontend/index.html" ] && [ ! -L "${base}/frontend/index.html" ] \
    || { echo "no frontend/index.html"; return 0; }
  found="$(find -P "$base" \( -type l -o ! -user root -o ! -group root -o -perm /022 \) -print -quit)" \
    || { echo "the tree cannot be inspected"; return 0; }
  [ -z "$found" ] || echo "${found} is a link, is not owned by root or can be written by others"
}

# require_release_entries VERSION: refuses (cli_fail) when release_tree_problem finds anything. Run on
# the tree that will be live, whether this run unpacked it, repaired it or kept the one installed.
require_release_entries() {
  local problem
  problem="$(release_tree_problem "$1")"
  [ -z "$problem" ] || cli_fail "release ${1} is not usable: ${problem}"
}

# repair_release VERSION STAGEDIR: replaces the tree of a release that is `current` or PREVIOUS (which
# cli_layout_install_release refuses to touch) with one unpacked from the staged, verified archives.
# The manifest's version must be VERSION and each sha256 is re-checked immediately before unpacking,
# as cli_layout_install_release does. Sequence, with `current` naming an existing release throughout:
# unpack into <VERSION>.partial, publish it as <VERSION>-repair, point `current` at that name if it
# named the damaged tree, replace releases/<VERSION> by a hard-linked copy of the repaired tree, point
# `current` back, remove the temporary name.
repair_release() {
  local version="$1" stage="$2" releases="${MARAN_LAYOUT_ROOT:?}/releases" manifest arch c current
  local partial="${releases}/${1:?}.partial" pivot="${1:?}-repair"
  cli_version_is_semver "$pivot" || cli_fail "cannot name a temporary release for ${version}"
  manifest="${stage}/manifest.json"
  [ "$(cli_release_manifest_field "$manifest" version)" = "$version" ] \
    || cli_fail "the manifest in ${stage} is not for release ${version}"
  arch="$(cli_release_arch)"
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    cli_release_check_sha256 "${stage}/${c}.tar.gz" "$(cli_release_manifest_field "$manifest" "${c}-${arch}.sha256")"
  done
  current="$(cli_layout_current_version)" || exit 1
  rm -rf --one-file-system -- "$partial" "${releases}/${pivot}"
  mkdir -m 0755 -- "$partial" || cli_fail "cannot create ${partial}"
  cli_layout_unpack_release "$stage" "$partial" \
    || { rm -rf --one-file-system -- "$partial"; cli_fail "release ${version} could not be unpacked safely"; }
  sync
  mv -T -- "$partial" "${releases}/${pivot}" || cli_fail "cannot publish ${releases}/${pivot}"
  if [ "$current" = "$version" ]; then cli_layout_point_current "$pivot"; fi
  rm -rf --one-file-system -- "${releases}/${version}"
  cp -a --link -- "${releases}/${pivot}" "$partial" || cli_fail "cannot rebuild ${releases}/${version}"
  mv -T -- "$partial" "${releases}/${version}" || cli_fail "cannot publish ${releases}/${version}"
  if [ "$current" = "$version" ]; then cli_layout_point_current "$version"; fi
  rm -rf --one-file-system -- "${releases}/${pivot}"
  sync -- "$releases"
}

# previous_release VERSION CANDIDATE...: prints the first candidate that is a valid release name, is
# not VERSION and still exists under releases/, and nothing when none qualifies: the release
# `maran rollback` would return to after VERSION is installed.
previous_release() {
  local version="$1" candidate
  shift
  for candidate in "$@"; do
    if cli_version_is_release_name "$candidate" && [ "$candidate" != "$version" ] \
        && [ -d "${MARAN_LAYOUT_ROOT}/releases/${candidate}" ]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
}

# step_artifacts: stages and verifies the release, unpacks it into releases/<version> (never into
# the live tree), adopts a pre-layout install if there is one, points `current` and the static
# links at the new release, and records release.state. Nothing downloaded is executed here, and the
# manifest's version is validated by cli_version_is_semver before it becomes a path.
step_artifacts() {
  local version channel adopted installed current prior prior_previous previous state_dir problem
  resolve_channel
  echo "Fetching and verifying release artifacts (channel: ${MARAN_CHANNEL})..."
  state_dir="$(dirname -- "$MARAN_LAYOUT_STATE_FILE")"
  [ -d "$state_dir" ] && [ ! -L "$state_dir" ] || cli_fail "${state_dir} is not a real directory"
  install -d -o root -g root -m 0755 "$MARAN_LAYOUT_ROOT"
  prepare_staging_dir
  stage_release

  version="$(cli_release_manifest_field "${MARAN_ARTIFACT_TMP}/manifest.json" version)" || exit 1
  cli_version_is_semver "$version" || cli_fail "the signed manifest carries no valid release version"
  channel="$(staged_channel)" || exit 1
  current="$(cli_layout_current_version)" || exit 1
  prior="$(cli_layout_state_read VERSION)" || exit 1
  prior_previous="$(cli_layout_state_read PREVIOUS)" || exit 1

  installed="${current:-$prior}"
  if [ -n "$installed" ] && cli_version_gt "$installed" "$version"; then
    cli_fail "release ${installed} is installed and is newer than ${version}; the installer does not downgrade" \
      "(maran rollback goes back to the previous release)"
  fi

  if [ -e "${MARAN_LAYOUT_ROOT}/releases/${version}" ] \
      && { [ "$version" = "$current" ] || [ "$version" = "$prior_previous" ]; }; then
    problem="$(release_tree_problem "$version")"
    if [ -z "$problem" ]; then
      echo "Release ${version} is already installed and its tree is intact; keeping it."
    else
      echo "Release ${version} is installed but unusable (${problem}); repairing it from the verified archives." >&2
      repair_release "$version" "$MARAN_ARTIFACT_TMP"
    fi
  else
    cli_layout_install_release "$version" "$MARAN_ARTIFACT_TMP" replace
  fi
  require_release_entries "$version"

  adopted="$(cli_layout_adopt_legacy)" || exit 1
  previous="$(previous_release "$version" "$adopted" "$installed" "$prior" "$prior_previous")"
  cli_layout_switch "$version"
  cli_layout_link_static
  cli_layout_state_write "$channel" "$version" "$previous"

  rm -rf --one-file-system -- "${MARAN_ARTIFACT_TMP:?}" "${MARAN_LAYOUT_ROOT:?}/releases/${version:?}-repair"
  echo "Release ${version} (channel ${channel}) installed under ${MARAN_LAYOUT_ROOT}/releases;" \
    "${MARAN_LAYOUT_ROOT}/current points at it."
}
