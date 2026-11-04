#!/usr/bin/env bash
# update-dumps.sh: the ONE home of the pre-update database dump contract (issue #128). `maran update`
# writes a dump before it changes anything; `maran rollback --restore-database` and the
# interrupted-update message look one up. Neither spells a dump name, a dump path or the
# directory's rules itself: they call the functions below.
#
# THE NAME GRAMMAR (this is the only place it is written down):
#
#   pre-<FROM>-to-<TO>-<TIMESTAMP>.dump
#
#   FROM, TO   release names: a strict SemVer version or a legacy-<UTC timestamp> name, exactly what
#              cli_version_is_release_name (release-version.sh) accepts. FROM is the release being
#              left, TO the release being installed. On a legacy host FROM is the adopted release
#              name legacy-<ts>, the same string recorded as PREVIOUS, never 0.0.0 or unknown.
#   TIMESTAMP  the UTC moment the dump was named, `%Y%m%dT%H%M%SZ` (fixed width, so the newest is the
#              greatest string).
#
# A pre-release identifier may itself contain "-to-", so a name can split into FROM and TO in more
# than one way; a name is accepted when ANY split yields two valid release names, and a lookup by TO
# accepts it only when the split ends in exactly that TO. The newest dump is chosen by the
# TIMESTAMP IN THE NAME and never by mtime, which a copy, a restore from backup or a touch rewrites.
# Only a regular, non-symlink file owned by root whose whole name fits the grammar is ever listed,
# returned or removed.
#
# The directory is $MARAN_LAYOUT_BACKUP_DIR (release-layout.sh, /var/backups/maran-update by
# default): root:root 0700, created on first use by cli_dumps_dir, and refused when it is a symlink,
# not owned by root, or reachable by anyone else.

# The directory used when release-layout.sh has not been sourced (a selftest, a partial source).
readonly MARAN_DUMPS_DEFAULT_DIR="/var/backups/maran-update"
# The shape of the TIMESTAMP part and of the file extension; the release names are checked by calling
# cli_version_is_release_name, never by a pattern here.
readonly MARAN_DUMPS_TAIL_RE='^pre-(.+)-([0-9]{8}T[0-9]{6}Z)\.dump$'

# cli_dumps_path: prints the directory's path, nothing is created or checked.
cli_dumps_path() {
  printf '%s' "${MARAN_LAYOUT_BACKUP_DIR:-$MARAN_DUMPS_DEFAULT_DIR}"
}

# cli_dumps_require_private: refuses (cli_fail) unless the dump directory exists, is not a symlink,
# is owned by root:root and has mode 0700. A dump holds every row of the panel database, so a
# directory anyone else can enter or swap is refused before a byte is written or read.
cli_dumps_require_private() {
  local dir
  dir="$(cli_dumps_path)"
  if [ -L "$dir" ] || [ ! -d "$dir" ] || [ "$(stat -c '%u %g %a' -- "$dir")" != "0 0 700" ]; then
    cli_fail "${dir} is not a root-owned 0700 directory; refusing to use it for database dumps"
  fi
}

# cli_dumps_dir: prints the dump directory, creating it root:root 0700 on first use and refusing
# (cli_fail) anything cli_dumps_require_private refuses. Use it where a dump is about to be written.
cli_dumps_dir() {
  local dir
  dir="$(cli_dumps_path)"
  if [ ! -e "$dir" ] && [ ! -L "$dir" ]; then
    install -d -m 0700 -o root -g root -- "$dir"
  fi
  cli_dumps_require_private
  printf '%s' "$dir"
}

# cli_dumps_name: prints the file NAME (not a path) of a dump for the update FROM -> TO, stamped with
# the current UTC time. Refuses (cli_fail) a FROM or TO that is not a release name.
#   $1 = FROM, $2 = TO.
cli_dumps_name() {
  local from="${1:-}" to="${2:-}"
  cli_version_is_release_name "$from" || cli_fail "refusing to name a dump after the invalid release '${from}'"
  cli_version_is_release_name "$to" || cli_fail "refusing to name a dump after the invalid release '${to}'"
  printf 'pre-%s-to-%s-%s.dump' "$from" "$to" "$(date -u '+%Y%m%dT%H%M%SZ')"
}

# cli_dumps_name_fits: returns 0 when NAME fits the grammar in the header and, when TO is given,
# when the split ends in exactly that TO. Tries every "-to-" as the split, because a pre-release
# identifier may contain one.
#   $1 = file name, $2 = TO or empty.
cli_dumps_name_fits() {
  local name="$1" to="${2:-}" head="" tail
  [[ "$name" =~ $MARAN_DUMPS_TAIL_RE ]] || return 1
  tail="${BASH_REMATCH[1]}"
  while [[ "$tail" == *-to-* ]]; do
    head+="${tail%%-to-*}"
    tail="${tail#*-to-}"
    if cli_version_is_release_name "$head" && cli_version_is_release_name "$tail" \
       && { [ -z "$to" ] || [ "$tail" = "$to" ]; }; then
      return 0
    fi
    head+="-to-"
  done
  return 1
}

# cli_dumps_list: prints "<timestamp> <name>" for every qualifying dump in the (already verified)
# directory, newest first by the timestamp in the name, ties broken by name. Qualifying means a
# regular, non-symlink, root-owned file whose whole name fits the grammar (and ends in TO when given).
#   $1 = directory, $2 = TO or empty.
cli_dumps_list() {
  local dir="$1" to="${2:-}" file name stamp
  local LC_ALL=C
  for file in "$dir"/pre-*.dump; do
    [ -f "$file" ] && [ ! -L "$file" ] || continue
    name="${file##*/}"
    cli_dumps_name_fits "$name" "$to" || continue
    [ "$(stat -c '%u' -- "$file")" = "0" ] || continue
    stamp="${name%.dump}"
    printf '%s %s\n' "${stamp##*-}" "$name"
  done | sort -r
}

# cli_dumps_newest_to: prints the path of the newest dump taken for the update TO the release TO
# (any FROM), or NOTHING when there is none. Always returns 0: callers test the OUTPUT, so "no dump"
# and "no usable directory" (reported on stderr) are the same empty answer, never a path to trust.
#   $1 = TO.
cli_dumps_newest_to() {
  local to="${1:-}" dir line
  cli_version_is_release_name "$to" || { echo "maran: '${to}' is not a release name; no dump looked up" >&2; return 0; }
  dir="$(cli_dumps_path)"
  [ -d "$dir" ] && [ ! -L "$dir" ] || return 0
  if [ "$(stat -c '%u %g %a' -- "$dir")" != "0 0 700" ]; then
    echo "maran: ${dir} is not a root-owned 0700 directory; its dumps are not trusted" >&2
    return 0
  fi
  line="$(cli_dumps_list "$dir" "$to")"
  line="${line%%$'\n'*}"
  [ -z "$line" ] || printf '%s/%s' "$dir" "${line#* }"
  return 0
}

# cli_dumps_prune: keeps the KEEP newest dumps by the timestamp in the name and removes the rest,
# `rm -f --` of full, validated names only (nothing else in the directory is touched). A missing
# directory has nothing to prune; one that is not private is refused (cli_fail).
#   $1 = KEEP, a non-negative integer.
cli_dumps_prune() {
  local keep="${1:-}" dir line
  [[ "$keep" =~ ^[0-9]{1,6}$ ]] || cli_fail "the number of dumps to keep must be a non-negative integer"
  dir="$(cli_dumps_path)"
  [ -e "$dir" ] || [ -L "$dir" ] || return 0
  cli_dumps_require_private
  while read -r line; do
    [ -n "$line" ] || continue
    rm -f -- "${dir}/${line#* }"
  done < <(cli_dumps_list "$dir" "" | tail -n "+$((10#$keep + 1))")
}
