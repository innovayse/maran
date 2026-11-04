#!/usr/bin/env bash
# release-layout.sh: the on-disk release layout `maran update` and `maran rollback` manage.
#
# Sourced after common.sh, release-version.sh, release-verify.sh, release-unpack.sh; no `set` option.
# The layout (root:root, written only by the CLI, which runs as root):
#   <root>/releases/<name>/{api,agent,frontend}   a release, never written after install
#   <root>/current -> releases/<name>               the live release, swapped with mv -T
#   <root>/{api,agent,frontend} -> current/<c>      static links, created once, only if missing
#   /etc/maran/release.state                        CHANNEL=, VERSION=, PREVIOUS= lines
# A release <name> is a SemVer version or legacy-<UTC stamp> (an adopted pre-layout install). No
# path is built from a value that has not passed cli_version_is_release_name.
#
# Failure convention for every function here, value-printing ones included: the function prints its
# message to stderr and ends with cli_fail (exit 1). Called inside $( ) that ends only the substitution:
# the message is printed and the substitution's status is non-zero, so a caller MUST test it, either a
# plain assignment under errexit (v="$(cli_layout_state_read VERSION)") or `|| exit 1`; never
# `local v="$(...)"` and never `|| true`. Bash clears errexit inside a command substitution, so inside
# this lib every nested value read and every command whose failure matters carries its own explicit
# `|| exit 1` (the callee has already printed) or `|| cli_fail`; nothing relies on `set -e`. The one
# exception is cli_layout_check_consistent, which returns 1 so a caller can test it.
# Plain assignments, not read from the environment; a test reassigns them after sourcing. Other libs
# reuse MARAN_LAYOUT_ROOT, _BACKUP_DIR and _COMPONENTS.
MARAN_LAYOUT_ROOT="/usr/local/maran"
MARAN_LAYOUT_STATE_FILE="/etc/maran/release.state"
MARAN_LAYOUT_BACKUP_DIR="/var/backups/maran-update"
MARAN_LAYOUT_COMPONENTS=(api agent frontend)
MARAN_LAYOUT_KEEP_NEWEST=3

# cli_layout_is_release_dir NAME: succeeds when releases/NAME is a real directory, not a symlink.
cli_layout_is_release_dir() {
  [ -d "${MARAN_LAYOUT_ROOT}/releases/${1}" ] && [ ! -L "${MARAN_LAYOUT_ROOT}/releases/${1}" ]
}

# cli_layout_current_version: prints the release `current` points at, and NOTHING only when there is
# no `current` at all. Any other state (not a symlink, wrong target, dangling) is an error.
cli_layout_current_version() {
  local link="${MARAN_LAYOUT_ROOT}/current" target name
  if [ ! -L "$link" ]; then
    [ ! -e "$link" ] || cli_fail "${link} is not a symlink"
    return 0
  fi
  target="$(readlink -- "$link")"
  name="${target#releases/}"
  [ "$name" != "$target" ] && cli_version_is_release_name "$name" \
    || cli_fail "${link} points at an unexpected target"
  cli_layout_is_release_dir "$name" || cli_fail "the release ${name} that ${link} names is missing"
  printf '%s\n' "$name"
}

# cli_layout_state_read KEY: prints CHANNEL, VERSION or PREVIOUS from release.state, or nothing when
# the file is absent. Parsed line by line, never sourced; refused unless a root-owned plain file nobody
# else can write; a duplicate key, unknown line or invalid value is an error, not a guess.
cli_layout_state_read() {
  local want="$1" file="$MARAN_LAYOUT_STATE_FILE" line key value found="" seen="" mode
  [[ "$want" =~ ^(CHANNEL|VERSION|PREVIOUS)$ ]] || cli_fail "unknown release state key"
  [ -e "$file" ] || [ -L "$file" ] || return 0
  [ -f "$file" ] && [ ! -L "$file" ] || cli_fail "${file} is not a plain file"
  mode="$(stat -c '%u %a' -- "$file")"
  [ "${mode%% *}" = "0" ] && [[ "${mode: -2}" =~ ^[0145]{2}$ ]] \
    || cli_fail "${file} must be owned by root and not writable by group or others"
  while IFS= read -r line || [ -n "$line" ]; do
    [[ "$line" =~ ^(CHANNEL|VERSION|PREVIOUS)=(.*)$ ]] || cli_fail "${file} holds an unrecognised line"
    key="${BASH_REMATCH[1]}"
    value="${BASH_REMATCH[2]}"
    [[ " ${seen} " != *" ${key} "* ]] || cli_fail "${file} repeats ${key}"
    seen+=" ${key}"
    case "$key" in
      CHANNEL) [[ "$value" =~ ^(stable|beta)$ ]] || cli_fail "${file} holds an invalid CHANNEL" ;;
      VERSION) cli_version_is_release_name "$value" || cli_fail "${file} holds an invalid VERSION" ;;
      PREVIOUS) [ -z "$value" ] || cli_version_is_release_name "$value" \
        || cli_fail "${file} holds an invalid PREVIOUS" ;;
    esac
    [ "$key" != "$want" ] || found="$value"
  done < "$file"
  printf '%s\n' "$found"
}

# cli_layout_state_write CHANNEL VERSION PREVIOUS: replaces release.state (root:root 0644) after
# validating every value (PREVIOUS may be empty): temporary file beside it, sync, rename, sync of the
# directory, so a power loss leaves the old state or the new one, never a rename of unflushed bytes.
cli_layout_state_write() {
  local channel="$1" version="$2" previous="$3" file="$MARAN_LAYOUT_STATE_FILE" tmp
  [[ "$channel" =~ ^(stable|beta)$ ]] || cli_fail "refusing to record channel '${channel}'"
  cli_version_is_release_name "$version" || cli_fail "refusing to record version '${version}'"
  [ -z "$previous" ] || cli_version_is_release_name "$previous" || cli_fail "refusing to record previous '${previous}'"
  tmp="$(mktemp -- "${file%/*}/.release.state.XXXXXX")" || cli_fail "cannot create a state file"
  if ! { printf 'CHANNEL=%s\nVERSION=%s\nPREVIOUS=%s\n' "$channel" "$version" "$previous" > "$tmp" \
         && chown root:root -- "$tmp" && chmod 0644 -- "$tmp" && sync -- "$tmp" \
         && mv -T -- "$tmp" "$file"; }; then
    rm -f -- "$tmp"
    cli_fail "cannot write ${file}"
  fi
  # The rename succeeded, so the new state IS written; a failed directory sync only means it may not
  # survive a power loss. That is a warning, never "cannot write".
  sync -- "${file%/*}" || echo "maran: warning: ${file} is written but its directory could not be synced" >&2
}

# cli_layout_previous_version: prints PREVIOUS, or nothing when none; fails when it is not on disk.
cli_layout_previous_version() {
  local previous
  previous="$(cli_layout_state_read PREVIOUS)" || exit 1
  [ -n "$previous" ] || return 0
  cli_layout_is_release_dir "$previous" || cli_fail "the previous release ${previous} is no longer on disk"
  printf '%s\n' "$previous"
}

# cli_layout_link_static: creates each missing <root>/<c> -> current/<c>; a real directory is an error.
cli_layout_link_static() {
  local c path
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    path="${MARAN_LAYOUT_ROOT}/${c}"
    if [ -L "$path" ]; then continue; fi
    [ ! -e "$path" ] || cli_fail "${path} is a real directory; adopt the legacy layout first"
    ln -s -- "current/${c}" "$path" || cli_fail "cannot create the link ${path}"
  done
}

# cli_layout_point_current NAME: points `current` at releases/NAME atomically (ln -sfn to a temporary
# name, then mv -T over `current`) and syncs the directory.
cli_layout_point_current() {
  local name="$1" root="$MARAN_LAYOUT_ROOT" tmp="${MARAN_LAYOUT_ROOT}/.current.new"
  cli_version_is_release_name "$name" || cli_fail "refusing to switch to '${name}'"
  cli_layout_is_release_dir "$name" || cli_fail "release ${name} is not on disk"
  ln -sfn -- "releases/${name}" "$tmp" || cli_fail "cannot create the temporary link"
  mv -T -- "$tmp" "${root}/current" || { rm -f -- "$tmp"; cli_fail "cannot swap ${root}/current"; }
  sync -- "$root" || echo "maran: warning: ${root}/current is switched but ${root} could not be synced" >&2
}

# cli_layout_switch VERSION: points `current` at releases/VERSION, then creates any missing static link.
cli_layout_switch() {
  cli_layout_point_current "$1"
  cli_layout_link_static
}

# cli_layout_adopt_legacy: moves a pre-layout install's real api, agent and frontend directories into
# releases/<name> and links them back through `current`. <name> is VERSION from release.state, else
# legacy-<UTC stamp>. Prints the name, and nothing when there was nothing to adopt. Every move is a
# same-filesystem rename (another filesystem is refused), so running processes keep their inodes.
#
# Kill-window recovery: the order is create releases/<name>, point `current` at it, then per
# component rename it in and link it back. A kill leaves `current` set and some components still real
# (at most one missing between its rename and link); the next run takes <name> from `current` and
# RESUMES, only if <name> is the recorded VERSION or legacy-* and no real component also exists
# inside the release (two copies; nothing here chooses). Otherwise it refuses.
cli_layout_adopt_legacy() {
  local root="$MARAN_LAYOUT_ROOT" releases="${MARAN_LAYOUT_ROOT}/releases" name c dev dest current state
  local real=() missing=()
  current="$(cli_layout_current_version)" || exit 1
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    [ ! -L "${root}/${c}" ] || continue
    if [ -d "${root}/${c}" ]; then real+=("$c"); else
      [ ! -e "${root}/${c}" ] || cli_fail "${root}/${c} is neither a link nor a directory"
      missing+=("$c")
    fi
  done
  if [ "${#real[@]}" -eq 0 ] && { [ -z "$current" ] || [ "${#missing[@]}" -eq 0 ]; }; then return 0; fi
  state="$(cli_layout_state_read VERSION)" || exit 1
  [ ! -L "$releases" ] || cli_fail "${releases} is a symlink"
  # Every refusal below comes BEFORE the first change (the releases directory, the release directory,
  # `current`, any move), so a refusal leaves the tree exactly as it was found.
  if [ -d "$releases" ]; then dev="$(stat -c '%d' -- "$releases")" || exit 1
  else dev="$(stat -c '%d' -- "$root")" || exit 1; fi
  for c in ${real[@]+"${real[@]}"}; do
    [ "$(stat -c '%d' -- "${root}/${c}")" = "$dev" ] || cli_fail "${root}/${c} is on another filesystem"
  done
  if [ -n "$current" ]; then
    name="$current"
    [ "$name" = "$state" ] || cli_version_is_legacy "$name" \
      || cli_fail "${root}/current names ${name} but a component is a real directory; refusing to guess"
    for c in ${real[@]+"${real[@]}"}; do
      [ ! -e "${releases}/${name}/${c}" ] || cli_fail "${c} exists both in ${root} and in ${releases}/${name}"
    done
  else
    name="${state:-legacy-$(date -u +%Y%m%dT%H%M%SZ)}"
    [ ! -e "${releases}/${name}" ] && [ ! -L "${releases}/${name}" ] \
      || cli_fail "${releases}/${name} already exists; refusing to adopt over it"
  fi
  dest="${releases}/${name}"
  # Each component is either a real directory about to be moved, or already inside the release.
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    [[ " ${real[*]-} " == *" ${c} "* ]] || { [ -n "$current" ] && [ -d "${dest}/${c}" ]; } \
      || cli_fail "${dest}/${c} is missing"
  done
  if [ -z "$current" ]; then
    install -d -m 0755 -o root -g root -- "$releases" || cli_fail "cannot create ${releases}"
    mkdir -m 0755 -- "$dest" || cli_fail "cannot create ${dest}"
    cli_layout_point_current "$name"
  fi
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    if [ ! -L "${root}/${c}" ] && [ -d "${root}/${c}" ]; then
      mv -T -- "${root}/${c}" "${dest}/${c}" || cli_fail "cannot move ${root}/${c} into ${dest}; run the command again"
    fi
    [ -d "${dest}/${c}" ] || cli_fail "${dest}/${c} is missing"
    [ -L "${root}/${c}" ] || ln -s -- "current/${c}" "${root}/${c}" || cli_fail "cannot link ${root}/${c}"
  done
  printf '%s\n' "$name"
}

# cli_layout_unpack_release STAGEDIR PARTIAL: unpacks each <component>.tar.gz into PARTIAL/<component>
# (cli_unpack_into) and requires the entry files. Returns 1 on refusal.
cli_layout_unpack_release() {
  local stage="$1" partial="$2" c
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    mkdir -m 0755 -- "${partial}/${c}" || return 1
    cli_unpack_into "${stage}/${c}.tar.gz" "${partial}/${c}" || return 1
  done
  for c in api/Maran.Host agent/maran-agent; do
    [ -f "${partial}/${c}" ] && [ ! -L "${partial}/${c}" ] && [ -x "${partial}/${c}" ] \
      || { echo "maran: the release has no executable ${c}" >&2; return 1; }
  done
  [ -f "${partial}/frontend/index.html" ] && [ ! -L "${partial}/frontend/index.html" ] \
    || { echo "maran: the release has no frontend/index.html" >&2; return 1; }
}

# cli_layout_install_release VERSION STAGEDIR [replace]: installs the three archives of STAGEDIR (root
# owned, nobody else can write, holding the verified manifest.json) as releases/VERSION. BEFORE
# unpacking, each archive's sha256 is re-checked against the manifest and its version must be VERSION.
# Unpacks into VERSION.partial, then renames, so a final name is always complete. An existing
# releases/VERSION is refused unless `replace`, and then only when it is neither `current` nor PREVIOUS
# (an abandoned release of a failed update is simply replaced).
cli_layout_install_release() {
  local version="$1" stage="$2" mode="${3:-}" releases="${MARAN_LAYOUT_ROOT}/releases"
  local final partial c arch manifest st current previous expected
  cli_version_is_semver "$version" || cli_fail "refusing to install release '${version}'"
  [ -z "$mode" ] || [ "$mode" = "replace" ] || cli_fail "unknown install mode"
  [ -d "$stage" ] && [ ! -L "$stage" ] || cli_fail "${stage} is not a plain directory"
  st="$(stat -c '%u %a' -- "$stage")"
  [ "${st%% *}" = "0" ] && [[ "${st: -2}" =~ ^[0145]{2}$ ]] \
    || cli_fail "${stage} must be owned by root and not writable by group or others"
  manifest="${stage}/manifest.json"
  [ -f "$manifest" ] && [ ! -L "$manifest" ] || cli_fail "${manifest} is missing"
  [ "$(cli_release_manifest_field "$manifest" version)" = "$version" ] \
    || cli_fail "the manifest in ${stage} is not for release ${version}"
  arch="$(cli_release_arch)" || exit 1
  for c in "${MARAN_LAYOUT_COMPONENTS[@]}"; do
    [ -f "${stage}/${c}.tar.gz" ] && [ ! -L "${stage}/${c}.tar.gz" ] || cli_fail "${c}.tar.gz is missing"
    expected="$(cli_release_manifest_field "$manifest" "${c}-${arch}.sha256")" || exit 1
    cli_release_check_sha256 "${stage}/${c}.tar.gz" "$expected"
  done
  [ ! -L "$releases" ] || cli_fail "${releases} is a symlink"
  install -d -m 0755 -o root -g root -- "$releases"
  final="${releases}/${version}"
  partial="${final}.partial"
  if [ -e "$final" ] || [ -L "$final" ]; then
    [ "$mode" = "replace" ] || cli_fail "${final} already exists; refusing to overwrite it"
    cli_layout_is_release_dir "$version" || cli_fail "${final} is not a plain directory"
    current="$(cli_layout_current_version)" || exit 1
    previous="$(cli_layout_state_read PREVIOUS)" || exit 1
    [ "$version" != "$current" ] && [ "$version" != "$previous" ] \
      || cli_fail "refusing to replace release ${version}: it is the current or the previous release"
  fi
  rm -rf --one-file-system -- "$partial"
  mkdir -m 0755 -- "$partial" || cli_fail "cannot create ${partial}"
  cli_layout_unpack_release "$stage" "$partial" \
    || { rm -rf --one-file-system -- "$partial"; cli_fail "release ${version} could not be unpacked safely"; }
  sync
  [ ! -e "$final" ] || rm -rf --one-file-system -- "$final"
  mv -T -- "$partial" "$final" || { rm -rf --one-file-system -- "$partial"; cli_fail "cannot publish ${final}"; }
  sync -- "$releases"
}

# cli_layout_prune: keeps the 3 newest releases by SemVer precedence plus `current` and PREVIOUS and
# removes every other valid-named release directory under releases/ (legacy-* count as oldest).
cli_layout_prune() {
  local releases="${MARAN_LAYOUT_ROOT}/releases" current previous path name sorted=() keep=" " i n m tmp
  current="$(cli_layout_current_version)" || exit 1
  [ -n "$current" ] || cli_fail "refusing to prune without a current release"
  previous="$(cli_layout_state_read PREVIOUS)" || exit 1
  keep+="${current} ${previous} "
  for path in "$releases"/*; do
    name="${path##*/}"
    [ -d "$path" ] && [ ! -L "$path" ] && cli_version_is_semver "$name" || continue
    sorted+=("$name")
  done
  for ((i = 1; i < ${#sorted[@]}; i++)); do
    tmp="${sorted[i]}"
    for ((m = i - 1; m >= 0; m--)); do
      cli_version_gt "$tmp" "${sorted[m]}" || break
      sorted[m + 1]="${sorted[m]}"
    done
    sorted[m + 1]="$tmp"
  done
  for ((n = 0; n < ${#sorted[@]} && n < MARAN_LAYOUT_KEEP_NEWEST; n++)); do keep+="${sorted[n]} "; done
  for path in "$releases"/*; do
    name="${path##*/}"
    [ -d "$path" ] && [ ! -L "$path" ] && cli_version_is_release_name "$name" || continue
    [[ "$keep" != *" ${name} "* ]] || continue
    rm -rf --one-file-system -- "${releases}/${name}"
  done
}

# cli_layout_remove_partials: removes stale releases/<version>.partial directories a killed run left.
cli_layout_remove_partials() {
  local path name
  for path in "${MARAN_LAYOUT_ROOT}/releases"/*.partial; do
    name="${path##*/}"
    name="${name%.partial}"
    [ -d "$path" ] && [ ! -L "$path" ] && cli_version_is_semver "$name" || continue
    rm -rf --one-file-system -- "$path"
  done
}

# cli_layout_check_consistent: removes stale .partial directories, then FAILS CLOSED: returns 0 only
# when `current` and release.state VERSION agree (both absent included), or `current` is absent and the
# state is legacy-*, or the state is absent and `current` is legacy-* (an adoption in progress). Anything
# else returns 1 after printing both versions, the dump directory and the rollback command to stderr
# (an unresolvable `current` or unreadable state returns 1 after the reader's own message). No repair.
cli_layout_check_consistent() {
  local current state
  cli_layout_remove_partials
  current="$(cli_layout_current_version)" || return 1
  state="$(cli_layout_state_read VERSION)" || return 1
  if [ "$current" = "$state" ]; then return 0; fi
  if [ -z "$current" ] && cli_version_is_legacy "$state"; then return 0; fi
  if [ -z "$state" ] && cli_version_is_legacy "$current"; then return 0; fi
  printf 'maran: an earlier update was interrupted: current is %s but release.state says %s.\n%s\n%s\n' \
    "'${current:-none}'" "'${state:-none}'" "maran: the pre-update dumps are in ${MARAN_LAYOUT_BACKUP_DIR}." \
    "maran: run 'maran rollback' to return to the recorded release." >&2
  return 1
}
