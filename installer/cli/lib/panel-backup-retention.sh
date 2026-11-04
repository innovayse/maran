#!/usr/bin/env bash
# panel-backup-retention.sh: `cli_pbretention_apply` — keeps the newest N panel archives in the
# output directory and removes the older ones (issue #129).
#
# This is `rm` run by root against a directory, driven by a number from a configuration file, so it
# is written as the list of ways it must be unable to go wrong (threat note section 3):
#
#   - it is called only after a backup succeeded, and refuses unless the new archive is itself
#     in the list it enumerates;
#   - a candidate is deleted only when its whole basename matches the archive name pattern, it is a
#     regular file and not a symlink, and it sits directly inside the output directory;
#   - the output directory is checked first, and a system directory or a typo is refused;
#   - the order comes from the UTC stamp in the name, so a `touch` cannot reorder the archives;
#   - removal is `rm -- <path>` as an argv array, never a shell string and never `find -delete`.
#
# Remote copies are never touched: retention is local only, so a compromised panel cannot erase the
# off-server copies through this verb.
#
# Every step that can fail returns non-zero explicitly: the caller runs this inside `||`, where
# `set -e` is switched off, so a failing command that is merely left to errexit would be ignored.

# The whole basename of a panel archive, anchored at both ends. Digit classes only: the UTC stamp
# and the PID. Anything else in the directory is not ours and is never examined further.
readonly MARAN_PBRETENTION_NAME_PATTERN='^maran-panel-backup-([0-9]{8}T[0-9]{6}Z)-([0-9]+)\.tar\.gpg$'
# Top-level trees of the system itself. An output directory inside one of them is not where an
# operator keeps panel archives, whatever the configuration says.
readonly MARAN_PBRETENTION_SYSTEM_TREES="bin boot dev etc home lib lib32 lib64 proc root run sbin sys usr"
# Two-component directories that hold other things; the pattern would keep them safe today, the
# guard is for the day it is loosened.
readonly MARAN_PBRETENTION_SHARED_DIRS="/var/lib /var/log /var/backups /var/cache /var/spool /var/tmp /var/mail"

# cli_pbretention_out_dir_ok: succeeds when $1 is a directory retention may delete from: at least two
# path components, not inside a system tree, not a shared two-component directory, and not the
# panel's own configuration, state or staging directory. Prints the reason on stderr otherwise.
#   $1 = the output directory (already an absolute path of the plain alphabet).
cli_pbretention_out_dir_ok() {
  local out="${1%/}" first rest
  first="${out#/}"
  first="${first%%/*}"
  rest="${out#/"$first"}"
  if [ -z "$rest" ]; then
    echo "maran: retention refuses a top-level directory as the output directory" >&2
    return 1
  fi
  case " ${MARAN_PBRETENTION_SYSTEM_TREES} " in
    *" ${first} "*)
      echo "maran: retention refuses an output directory inside a system tree" >&2
      return 1
      ;;
  esac
  case " ${MARAN_PBRETENTION_SHARED_DIRS} " in
    *" ${out} "*)
      echo "maran: retention refuses a shared system directory as the output directory" >&2
      return 1
      ;;
  esac
  case "$out" in
    "$MARAN_PANEL_ETC_DIR"|"$MARAN_PANEL_ETC_DIR"/*|"$MARAN_PANEL_STATE_DIR"|"$MARAN_PANEL_STATE_DIR"/*|"$MARAN_CLI_STAGING_BASE"|"$MARAN_CLI_STAGING_BASE"/*)
      echo "maran: retention refuses the panel's own directories as the output directory" >&2
      return 1
      ;;
  esac
  return 0
}

# cli_pbretention_list: prints the panel archives directly inside $1, newest first, one basename per
# line. Only an entry that is a regular file, is not a symlink and matches the name pattern in full
# is listed; the glob does not recurse and builds no path from outside $1. The stamp orders them
# (then the PID numerically), never the file's mtime.
#   $1 = the output directory.
cli_pbretention_list() {
  local out="${1%/}" entry name
  for entry in "$out"/*; do
    name="${entry##*/}"
    [[ "$name" =~ $MARAN_PBRETENTION_NAME_PATTERN ]] || continue
    [ -f "$entry" ] && [ ! -L "$entry" ] || continue
    printf '%s %s %s\n' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "$name"
  done | sort -r -k1,1 -k2,2nr | cut -d ' ' -f 3
}

# cli_pbretention_apply: removes every panel archive in the output directory beyond the newest KEEP.
# The new archive is never removed, and nothing is removed unless it is present, non-empty and
# listed. Prints how many it removed. Returns non-zero (removing nothing further) on any refusal or
# failed removal.
#   $1 = output directory, $2 = KEEP (1..365), $3 = path of the archive the backup just wrote.
cli_pbretention_apply() {
  local out="${1%/}" keep="$2" newest="$3" newest_name listing name rank=0 removed=0 entry
  [[ "$keep" =~ ^[1-9][0-9]{0,2}$ ]] && [ "$keep" -le 365 ] \
    || { echo "maran: retention needs a KEEP between 1 and 365" >&2; return 1; }
  [[ "$out" =~ ^/[A-Za-z0-9._/-]+$ ]] && [[ "$out" != *..* ]] \
    || { echo "maran: retention refuses an output directory that is not a plain absolute path" >&2; return 1; }
  cli_pbretention_out_dir_ok "$out" || return 1
  # The directory is root-only and a real directory before anything is deleted from it.
  ( cli_require_private_out_dir "$out" ) || return 1

  newest_name="${newest##*/}"
  [ "${newest%/*}" = "$out" ] || { echo "maran: the new archive is not directly inside the output directory" >&2; return 1; }
  [[ "$newest_name" =~ $MARAN_PBRETENTION_NAME_PATTERN ]] \
    || { echo "maran: the new archive does not carry the archive name" >&2; return 1; }
  [ -f "$newest" ] && [ ! -L "$newest" ] && [ -s "$newest" ] \
    || { echo "maran: the new archive is missing or empty; retention removed nothing" >&2; return 1; }

  listing="$(cli_pbretention_list "$out")" || return 1
  # The same enumeration that picks the victims must contain the archive that was just written;
  # otherwise this function is looking at something other than the directory the backup used.
  grep -qxF -- "$newest_name" <<< "$listing" \
    || { echo "maran: the new archive is not in the retention listing; retention removed nothing" >&2; return 1; }

  while IFS= read -r name; do
    rank=$((rank + 1))
    [ "$rank" -gt "$keep" ] || continue
    [ "$name" != "$newest_name" ] || continue
    entry="${out}/${name}"
    # Checked again at the moment of removal, on the entry itself.
    [ -f "$entry" ] && [ ! -L "$entry" ] || continue
    rm -- "$entry" || { echo "maran: could not remove an old archive" >&2; return 1; }
    removed=$((removed + 1))
  done <<< "$listing"
  echo "Retention: kept the newest ${keep}, removed ${removed} older panel archive(s)."
}
