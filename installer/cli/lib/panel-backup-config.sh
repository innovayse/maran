#!/usr/bin/env bash
# panel-backup-config.sh: the strict reader of /etc/maran/panel-backup.conf, the file that steers
# the scheduled panel backup (issue #129).
#
# The file is read by root and names a passphrase file, an output directory, credential files and an
# endpoint, so whoever can write it steers a root process. Three rules follow from that, and every
# function below exists to hold one of them:
#
#   1. The file is never sourced, evaluated or handed to a shell. It is read line by line as DATA.
#   2. It is refused as a whole, before one byte is parsed, unless root alone could have written it.
#   3. Every line is blank, a comment, or KEY=VALUE with KEY on a fixed list and VALUE matching a
#      positive pattern for that key. An unknown key, a duplicate, a malformed line or a forbidden
#      character is a refusal that names the first problem, never a skip.
#
# The secret values (passphrase, access key id, secret key) are never IN this file; it holds the
# paths of 0600 files that do, so the config can be shown to a reviewer without leaking anything.
# docs/superpowers/notes/2026-11-04-panel-backup-schedule-threat-note.md section 2 carries the
# argument.
#
# The validated values are left in shell variables named MARAN_PBCONF_<KEY>, one per key; a key the
# file leaves out holds its default (or the empty string for the optional S3 keys).

readonly MARAN_PBCONF_FILE="/etc/maran/panel-backup.conf"
# The only keys the file may set, in the order the threat note lists them.
readonly MARAN_PBCONF_KEYS="PASSPHRASE_FILE OUT_DIR KEEP SCHEDULE S3_ENDPOINT S3_BUCKET S3_REGION S3_PREFIX S3_ACCESS_KEY_ID_FILE S3_SECRET_FILE"
# A file larger than this is not a configuration of ten short keys; refused before it is read.
readonly MARAN_PBCONF_MAX_BYTES=4096
readonly MARAN_PBCONF_DEFAULT_OUT_DIR="/var/backups/maran-panel"
readonly MARAN_PBCONF_DEFAULT_KEEP="14"
readonly MARAN_PBCONF_DEFAULT_SCHEDULE="*-*-* 02:30:00"

# cli_pbconfig_require_private_path: refuses unless $1 is a regular, non-symlink file owned by
# root:root with mode 0600 or 0400, and (when $3 is "parent") its directory is a plain directory
# owned by root that neither group nor others can write. The same shape of check the passphrase
# file already gets, applied to the configuration and to the credential files it names.
#   $1 = path, $2 = what to call it in a message, $3 = "parent" to check the directory too.
cli_pbconfig_require_private_path() {
  local path="$1" label="$2" scope="${3:-}" facts parent
  [[ "$path" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$path" != *..* ]] \
    || cli_fail "${label} must be an absolute path of letters, digits, '.', '_', '-' and '/'"
  [ -f "$path" ] && [ ! -L "$path" ] || cli_fail "${label} is not a regular file"
  facts="$(stat -c '%u %g %a' -- "$path")"
  [[ "$facts" =~ ^0\ 0\ [46]00$ ]] || cli_fail "${label} must be owned by root:root with mode 0600 or 0400"
  if [ "$scope" = "parent" ]; then
    parent="$(dirname -- "$path")"
    [ -d "$parent" ] && [ ! -L "$parent" ] || cli_fail "the directory of ${label} is not a plain directory"
    facts="$(stat -c '%u %a' -- "$parent")"
    # The last two permission digits are group and other; any write bit (2, 3, 6, 7) refuses.
    [[ "$facts" =~ ^0\ [0-9]*[0145][0145]$ ]] \
      || cli_fail "the directory of ${label} must be owned by root and not writable by group or others"
  fi
}

# cli_pbconfig_schedule_valid: succeeds only for a schedule of the shapes systemd.time(7) defines
# for OnCalendar that this job accepts: hourly, daily, weekly or monthly; or an optional weekday
# list or range, an optional date, and a time of day. It is a positive match, so a value carrying a
# newline, a control character or anything else systemd could read as another directive cannot pass
# (rules/security.md item 4). It checks the SHAPE, not the ranges: `enable` also asks systemd.
#   $1 = the candidate value.
cli_pbconfig_schedule_valid() {
  local value="$1" day datepart hour minute pattern
  [ "${#value}" -le 64 ] || return 1
  [[ "$value" =~ ^(hourly|daily|weekly|monthly)$ ]] && return 0
  day='(Mon|Tue|Wed|Thu|Fri|Sat|Sun)'
  datepart='(\*|[0-9]{4})-(\*|[0-9]{1,2})-(\*|[0-9]{1,2})'
  hour='(\*|[0-9]{1,2}(,[0-9]{1,2})*)'
  minute='[0-9]{1,2}(,[0-9]{1,2})*'
  pattern="^(${day}(\.\.${day}|(,${day})*) )?(${datepart} )?${hour}:${minute}(:[0-9]{1,2})?\$"
  [[ "$value" =~ $pattern ]]
}

# cli_pbconfig_value_valid: succeeds when $2 is acceptable for the allow-listed key $1. An empty
# value is decided by the caller, not here. Every key has a positive pattern; none is a search for
# forbidden characters.
#   $1 = key, $2 = value.
cli_pbconfig_value_valid() {
  local key="$1" value="$2"
  case "$key" in
    PASSPHRASE_FILE|OUT_DIR|S3_ACCESS_KEY_ID_FILE|S3_SECRET_FILE)
      [[ "$value" =~ ^/[A-Za-z0-9._/-]{1,200}$ ]] && [[ "$value" != *..* ]]
      ;;
    KEEP)
      [[ "$value" =~ ^[1-9][0-9]{0,2}$ ]] && [ "$value" -le 365 ]
      ;;
    SCHEDULE)
      cli_pbconfig_schedule_valid "$value"
      ;;
    S3_ENDPOINT)
      # https only, host and optional port only: no userinfo, path, query or fragment.
      [[ "$value" =~ ^https://[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?(:[0-9]{1,5})?$ ]] && [[ "$value" != *..* ]]
      ;;
    S3_BUCKET)
      [[ "$value" =~ ^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$ ]] && [[ "$value" != *..* ]]
      ;;
    S3_REGION)
      [[ "$value" =~ ^[a-z0-9][a-z0-9-]{1,30}$ ]]
      ;;
    S3_PREFIX)
      # Components start with a non-dot, so `.` and `..` can never be one.
      [[ "$value" =~ ^[A-Za-z0-9_-][A-Za-z0-9._-]{0,63}(/[A-Za-z0-9_-][A-Za-z0-9._-]{0,63}){0,7}$ ]]
      ;;
    *) return 1 ;;
  esac
}

# cli_pbconfig_reset: puts every variable back to its default, so a second load never inherits the
# first one's values.
cli_pbconfig_reset() {
  MARAN_PBCONF_PASSPHRASE_FILE=""
  MARAN_PBCONF_OUT_DIR="$MARAN_PBCONF_DEFAULT_OUT_DIR"
  MARAN_PBCONF_KEEP="$MARAN_PBCONF_DEFAULT_KEEP"
  MARAN_PBCONF_SCHEDULE="$MARAN_PBCONF_DEFAULT_SCHEDULE"
  MARAN_PBCONF_S3_ENDPOINT=""
  MARAN_PBCONF_S3_BUCKET=""
  MARAN_PBCONF_S3_REGION=""
  MARAN_PBCONF_S3_PREFIX=""
  MARAN_PBCONF_S3_ACCESS_KEY_ID_FILE=""
  MARAN_PBCONF_S3_SECRET_FILE=""
}

# cli_pbconfig_parse: reads the file named by $1 line by line and assigns each valid KEY=VALUE to
# MARAN_PBCONF_<KEY>. Refuses at the first line that is not blank, a comment, or a valid assignment.
# The key reaching `printf -v` has already matched the fixed list, so no operator text becomes a
# variable name.
#   $1 = the configuration file (already checked by the caller).
cli_pbconfig_parse() {
  local file="$1" line number=0 key value seen=" "
  while IFS= read -r line || [ -n "$line" ]; do
    number=$((number + 1))
    # CR (a CRLF file), tab, DEL and every other control character: refused on every line, comments
    # included, so a comment cannot carry what a value may not.
    if [[ "$line" =~ [[:cntrl:]] ]]; then
      cli_fail "${file} line ${number} contains a control character"
    fi
    if [ -z "$line" ] || [[ "$line" =~ ^\ +$ ]] || [[ "$line" == \#* ]]; then
      continue
    fi
    [[ "$line" =~ ^([A-Z0-9_]+)=(.*)$ ]] || cli_fail "${file} line ${number} is not KEY=VALUE"
    key="${BASH_REMATCH[1]}"
    value="${BASH_REMATCH[2]}"
    case " ${MARAN_PBCONF_KEYS} " in
      *" ${key} "*) ;;
      *) cli_fail "${file} line ${number} sets an unknown key" ;;
    esac
    case "$seen" in
      *" ${key} "*) cli_fail "${file} sets ${key} more than once" ;;
    esac
    seen="${seen}${key} "
    case "$value" in
      *\"*|*\'*|*\$*|*\`*|*\\*) cli_fail "the value of ${key} contains a quote, '\$', a backtick or a backslash" ;;
    esac
    if [ -z "$value" ]; then
      # Only the optional S3 keys may be left empty; the others always have a value.
      case "$key" in
        S3_*) ;;
        *) cli_fail "${key} needs a value" ;;
      esac
    elif ! cli_pbconfig_value_valid "$key" "$value"; then
      cli_fail "the value of ${key} is not acceptable"
    fi
    printf -v "MARAN_PBCONF_${key}" '%s' "$value"
  done < "$file"
}

# cli_pbconfig_check_s3: the S3 keys are all or nothing. Any S3 key without an endpoint, or an
# endpoint missing a part of what it needs, is refused, so a half-written configuration never
# silently means "no upload".
cli_pbconfig_check_s3() {
  if [ -z "$MARAN_PBCONF_S3_ENDPOINT" ]; then
    if [ -n "${MARAN_PBCONF_S3_BUCKET}${MARAN_PBCONF_S3_REGION}${MARAN_PBCONF_S3_PREFIX}${MARAN_PBCONF_S3_ACCESS_KEY_ID_FILE}${MARAN_PBCONF_S3_SECRET_FILE}" ]; then
      cli_fail "S3 keys are set but S3_ENDPOINT is not; set it or remove them"
    fi
    return 0
  fi
  [ -n "$MARAN_PBCONF_S3_BUCKET" ] || cli_fail "S3_ENDPOINT is set but S3_BUCKET is not"
  [ -n "$MARAN_PBCONF_S3_REGION" ] || cli_fail "S3_ENDPOINT is set but S3_REGION is not"
  [ -n "$MARAN_PBCONF_S3_ACCESS_KEY_ID_FILE" ] || cli_fail "S3_ENDPOINT is set but S3_ACCESS_KEY_ID_FILE is not"
  [ -n "$MARAN_PBCONF_S3_SECRET_FILE" ] || cli_fail "S3_ENDPOINT is set but S3_SECRET_FILE is not"
  cli_pbconfig_require_private_path "$MARAN_PBCONF_S3_ACCESS_KEY_ID_FILE" "S3_ACCESS_KEY_ID_FILE"
  cli_pbconfig_require_private_path "$MARAN_PBCONF_S3_SECRET_FILE" "S3_SECRET_FILE"
}

# cli_pbconfig_load: validates the configuration file and leaves its values in MARAN_PBCONF_*.
# Refuses (cli_fail, naming the first problem, never a value) when the file is missing or insecure,
# a line is not acceptable, a required key is absent, or a file the config names fails the checks
# the passphrase file gets. Called by the scheduled job and by `enable`, so a configuration the job
# would refuse is refused at enable time too. It must be called directly, not inside `$(...)`,
# because the refusal ends the command.
#   $1 = optional path of the configuration (the installed one when omitted).
cli_pbconfig_load() {
  local file="${1:-$MARAN_PBCONF_FILE}" size plain
  cli_pbconfig_reset
  [ -e "$file" ] || cli_fail "${file} does not exist; write it first"
  cli_pbconfig_require_private_path "$file" "$file" parent
  size="$(wc -c < "$file")"
  [ "$size" -le "$MARAN_PBCONF_MAX_BYTES" ] || cli_fail "${file} is larger than ${MARAN_PBCONF_MAX_BYTES} bytes"
  # bash's `read` drops a NUL byte without a word, so the NULs are counted out of band: a file that
  # shrinks when they are removed is refused.
  plain="$(tr -d '\0' < "$file" | wc -c)"
  [ "$plain" = "$size" ] || cli_fail "${file} contains a NUL byte"

  cli_pbconfig_parse "$file"
  [ -n "$MARAN_PBCONF_PASSPHRASE_FILE" ] || cli_fail "PASSPHRASE_FILE is not set in ${file}"
  cli_panel_require_passphrase_file "$MARAN_PBCONF_PASSPHRASE_FILE"
  cli_pbconfig_check_s3
}
