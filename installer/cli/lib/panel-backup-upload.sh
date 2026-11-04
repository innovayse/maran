#!/usr/bin/env bash
# panel-backup-upload.sh: sends one finished, already-encrypted panel archive to S3-compatible
# storage (issue #129). Sourced by /usr/local/sbin/maran with the other libraries; never executed.
#
# This is the one outbound call the scheduled backup makes, so it is deliberately small and takes
# only a path: the object it uploads is the `.tar.gpg` that gpg has already encrypted, named by the
# archive's own basename, so it cannot be aimed at any other file or object. The argument for the
# call is docs/superpowers/notes/2026-11-04-panel-backup-schedule-threat-note.md section 4.
#
# What it reads: the variables the panel-backup.conf reader has already validated, all prefixed
# MARAN_PBCONF_ (S3_ENDPOINT, S3_BUCKET, S3_REGION, S3_PREFIX, S3_ACCESS_KEY_ID_FILE,
# S3_SECRET_FILE). They are validated again here at the point of use, because a value that reaches
# a URL or a credential file is checked where it is used and not only where it was read.
#
# What it must never do, and how each is held:
#   - put a credential on argv, in the environment or in a log: the two secrets are read from their
#     root-only files into shell variables (not exported), written with the `printf` builtin into a
#     curl config file (-K) created exclusive and 0600 inside the stage directory, and curl is run
#     with its own output discarded. The journal line carries a verb, a fixed outcome word and at
#     most a three-digit HTTP status;
#   - follow a redirect, speak plain HTTP, or skip certificate verification: --proto =https,
#     --tlsv1.2, --max-redirs 0, never -L, never -k;
#   - read the invoking user's ~/.curlrc: -q is curl's first argument, because that file can switch
#     verification off and root's is not ours to trust;
#   - report success on a 2xx alone: a signed HEAD must report a length equal to the local size.

# How many times a transport failure or a 5xx is tried, and the pause between tries. Fixed, not read
# from the environment or the config: a retry policy an operator typo can turn into a hammer is a
# worse thing than one that cannot be tuned.
readonly MARAN_PBUPLOAD_ATTEMPTS=3
readonly MARAN_PBUPLOAD_BACKOFF_SECONDS=10
# curl's own limits: a hung endpoint must end the run rather than hold the backup lock forever.
readonly MARAN_PBUPLOAD_CONNECT_TIMEOUT=30
readonly MARAN_PBUPLOAD_MAX_TIME=7200
# The exact shape of an archive this library will send; the same pattern retention deletes by.
readonly MARAN_PBUPLOAD_NAME_PATTERN='^maran-panel-backup-[0-9]{8}T[0-9]{6}Z-[0-9]+\.tar\.gpg$'

# cli_pbupload_outcome: the single journal line for one upload, and a fixed message on stderr. It
# takes only fixed words from this file (and a status that was matched to three digits), never text
# from curl, the endpoint or the operator.
#   $1 = outcome word, $2 = message for stderr.
cli_pbupload_outcome() {
  cli_audit_log panel-backup-upload "$1"
  echo "maran: panel backup upload: $2" >&2
}

# cli_pbupload_read_secret_file: prints the first line of the root-only file $1 after checking it
# the way the passphrase file is checked (regular, not a symlink, owned by root, mode 0600 or
# 0400) and that the line is made only of the characters an access key id or secret key use. The
# alphabet is also what makes the value safe inside the double-quoted curl config line it is written
# into: no quote, no backslash, no whitespace, no control character, so it cannot end the string or
# start another directive. Returns non-zero, printing nothing, on any problem.
#   $1 = path.
cli_pbupload_read_secret_file() {
  local file="$1" line=""
  [[ "$file" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$file" != *..* ]] || return 1
  [ -f "$file" ] && [ ! -L "$file" ] || return 1
  [ "$(stat -c '%u' -- "$file")" = "0" ] || return 1
  [[ "$(stat -c '%a' -- "$file")" =~ ^[46]00$ ]] || return 1
  # A last line with no newline makes read return 1 but still fills the variable.
  IFS= read -r line < "$file" || [ -n "$line" ] || return 1
  [[ "$line" =~ ^[A-Za-z0-9/+=_.-]{1,512}$ ]] || return 1
  printf '%s' "$line"
}

# cli_pbupload_object_url: prints ENDPOINT/BUCKET/PREFIX/NAME from the validated parts, path-style,
# or returns non-zero if any part has a shape the product never produces. Nothing from outside
# these variables and the archive's basename enters the URL.
#   $1 = archive basename.
cli_pbupload_object_url() {
  local name="$1" endpoint="${MARAN_PBCONF_S3_ENDPOINT:-}" bucket="${MARAN_PBCONF_S3_BUCKET:-}"
  local prefix="${MARAN_PBCONF_S3_PREFIX:-}"
  [[ "$endpoint" =~ ^https://[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?(:[0-9]{1,5})?(/[A-Za-z0-9._/-]*)?$ ]] \
    && [[ "$endpoint" != *..* ]] || return 1
  [[ "$bucket" =~ ^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$ ]] && [[ "$bucket" != *..* ]] || return 1
  [[ "$prefix" =~ ^([A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*)?$ ]] && [[ "$prefix" != *..* ]] || return 1
  [[ "$name" =~ $MARAN_PBUPLOAD_NAME_PATTERN ]] || return 1
  endpoint="${endpoint%/}"
  if [ -n "$prefix" ]; then
    printf '%s/%s/%s/%s' "$endpoint" "$bucket" "$prefix" "$name"
  else
    printf '%s/%s/%s' "$endpoint" "$bucket" "$name"
  fi
}

# cli_pbupload_write_config: writes the curl config file $1 holding the credentials. It is created
# exclusive (noclobber, so a planted file or symlink is refused, not followed) under umask 077, in a
# subshell so neither setting leaks, and the secrets reach it through the `printf` builtin only —
# never through a command's argv. The caller removes it.
#   $1 = config path, $2 = access key id, $3 = secret key.
cli_pbupload_write_config() {
  (
    umask 077
    set -C
    printf 'user = "%s:%s"\n' "$2" "$3" > "$1"
  ) 2>/dev/null
}

# cli_pbupload_curl: one curl run against the object. The status code (or 000) lands in
# CLI_PBUPLOAD_STATUS and curl's exit status in CLI_PBUPLOAD_RC; curl's stdout is only the
# --write-out status, its body goes to /dev/null and its stderr is dropped, because an S3 error
# document or a curl diagnostic can echo the key id, a request id or the signed URL.
#   $1 = config path, $2 = region, $3 = URL, the rest = extra curl arguments (method and body).
cli_pbupload_curl() {
  local config="$1" region="$2" url="$3"
  shift 3
  local status
  CLI_PBUPLOAD_RC=0
  status="$(curl -q --proto '=https' --tlsv1.2 --max-redirs 0 --fail --silent --show-error \
    --connect-timeout "$MARAN_PBUPLOAD_CONNECT_TIMEOUT" --max-time "$MARAN_PBUPLOAD_MAX_TIME" \
    --aws-sigv4 "aws:amz:${region}:s3" --config "$config" \
    --header 'x-amz-content-sha256: UNSIGNED-PAYLOAD' \
    --output /dev/null --write-out '%{http_code}' "$@" -- "$url" 2>/dev/null)" || CLI_PBUPLOAD_RC=$?
  [[ "$status" =~ ^[0-9]{3}$ ]] || status="000"
  CLI_PBUPLOAD_STATUS="$status"
}

# cli_pbupload_retryable: succeeds when the last curl run failed in a way another try can fix: no
# HTTP answer at all after a transport-level curl exit (resolve, connect, timeout, send, receive,
# partial transfer), or a 5xx. A 4xx is the server's considered answer to this request and is never
# retried; a certificate, TLS or protocol refusal is configuration and is not retried either.
cli_pbupload_retryable() {
  case "$CLI_PBUPLOAD_STATUS" in
    5??) return 0 ;;
    000)
      case "$CLI_PBUPLOAD_RC" in
        5|6|7|18|28|52|55|56) return 0 ;;
      esac
      ;;
  esac
  return 1
}

# cli_pbupload_remote_length: prints the Content-Length of the stored object from the header file
# $1 that a signed HEAD wrote, or returns non-zero if there is no such header. The last one wins,
# the name is matched case-insensitively and a trailing carriage return is dropped.
cli_pbupload_remote_length() {
  awk 'tolower($1) == "content-length:" { v = $2; sub(/\r$/, "", v) } END { if (v != "") print v }' \
    "$1" | grep -E '^[0-9]+$'
}

# cli_pbupload_attempt: one full try — PUT, then a signed HEAD whose length must equal the local
# size. Sets CLI_PBUPLOAD_VERDICT to uploaded, retry, rejected or mismatch; the caller decides what
# a retry costs.
#   $1 = work dir, $2 = region, $3 = URL, $4 = archive, $5 = local size.
cli_pbupload_attempt() {
  local work="$1" region="$2" url="$3" archive="$4" size="$5" headers remote
  headers="${work}/head.txt"
  cli_pbupload_curl "${work}/curl.cfg" "$region" "$url" --upload-file "$archive"
  if [ "$CLI_PBUPLOAD_RC" -ne 0 ] || [[ ! "$CLI_PBUPLOAD_STATUS" =~ ^2 ]]; then
    if cli_pbupload_retryable; then CLI_PBUPLOAD_VERDICT="retry"; else CLI_PBUPLOAD_VERDICT="rejected"; fi
    return 0
  fi
  rm -f -- "$headers"
  cli_pbupload_curl "${work}/curl.cfg" "$region" "$url" --head --dump-header "$headers"
  if [ "$CLI_PBUPLOAD_RC" -ne 0 ] || [[ ! "$CLI_PBUPLOAD_STATUS" =~ ^2 ]]; then
    if cli_pbupload_retryable; then CLI_PBUPLOAD_VERDICT="retry"; else CLI_PBUPLOAD_VERDICT="rejected"; fi
    return 0
  fi
  remote="$(cli_pbupload_remote_length "$headers")" || remote=""
  if [ "$remote" = "$size" ]; then CLI_PBUPLOAD_VERDICT="uploaded"; else CLI_PBUPLOAD_VERDICT="mismatch"; fi
}

# cli_pbupload_run: the body of cli_pbupload_send, split out so the caller can remove the work
# directory on every path that returns. Never calls cli_fail: a failure here must still write its
# one journal line, and `exit` would skip it. Returns 0 only for a verified upload.
#   $1 = archive, $2 = work dir.
cli_pbupload_run() {
  local archive="$1" work="$2" name size url region key_id secret attempt=1
  name="${archive##*/}"
  region="${MARAN_PBCONF_S3_REGION:-}"
  [[ "$region" =~ ^[a-z0-9][a-z0-9-]{1,31}$ ]] \
    || { cli_pbupload_outcome refused "the region is not valid"; return 1; }
  url="$(cli_pbupload_object_url "$name")" \
    || { cli_pbupload_outcome refused "the endpoint, bucket, prefix or archive name is not valid"; return 1; }
  key_id="$(cli_pbupload_read_secret_file "${MARAN_PBCONF_S3_ACCESS_KEY_ID_FILE:-}")" \
    || { cli_pbupload_outcome refused "the access key id file is missing or not a root-only 0600 file"; return 1; }
  secret="$(cli_pbupload_read_secret_file "${MARAN_PBCONF_S3_SECRET_FILE:-}")" \
    || { cli_pbupload_outcome refused "the secret key file is missing or not a root-only 0600 file"; return 1; }
  size="$(stat -c '%s' -- "$archive")" || size=""
  [[ "$size" =~ ^[0-9]+$ ]] && [ "$size" -gt 0 ] \
    || { cli_pbupload_outcome refused "the archive is empty"; return 1; }

  cli_pbupload_write_config "${work}/curl.cfg" "$key_id" "$secret" \
    || { cli_pbupload_outcome refused "could not create the credentials file"; return 1; }
  unset key_id secret

  while :; do
    cli_pbupload_attempt "$work" "$region" "$url" "$archive" "$size"
    case "$CLI_PBUPLOAD_VERDICT" in
      uploaded)
        cli_pbupload_outcome uploaded "the archive was stored and its size was confirmed"
        return 0
        ;;
      mismatch)
        cli_pbupload_outcome size-mismatch "the stored object's size differs from the local archive"
        return 1
        ;;
      rejected)
        cli_pbupload_outcome "rejected-${CLI_PBUPLOAD_STATUS}" "the storage refused the request; the local archive is kept"
        return 1
        ;;
      retry)
        if [ "$attempt" -ge "$MARAN_PBUPLOAD_ATTEMPTS" ]; then
          cli_pbupload_outcome "failed-${CLI_PBUPLOAD_STATUS}" "the storage could not be reached after ${attempt} attempts; the local archive is kept"
          return 1
        fi
        attempt=$((attempt + 1))
        sleep "$MARAN_PBUPLOAD_BACKOFF_SECONDS"
        ;;
    esac
  done
}

# cli_pbupload_send: uploads the finished archive $1 and returns 0 only when the storage accepted it
# AND reported the same length. Any other result returns 1 after writing one journal line; the local
# archive is never touched.
#
# Must be called while the verb's stage directory is open (CLI_PANEL_STAGE, from
# cli_panel_open_stage): the credentials file lives in a directory under it, and that stage's EXIT
# trap is what removes it on a signal or an abort, exactly as it removes the plaintext. This
# function also removes its own directory on every normal return, so the file lives for one upload.
#   $1 = path of a maran-panel-backup-*.tar.gpg file.
cli_pbupload_send() {
  local archive="${1:-}" work rc=0
  [ -n "${CLI_PANEL_STAGE:-}" ] && [ -d "$CLI_PANEL_STAGE" ] && [ ! -L "$CLI_PANEL_STAGE" ] \
    || { cli_pbupload_outcome refused "no open stage directory to hold the credentials file"; return 1; }
  [ -f "$archive" ] && [ ! -L "$archive" ] && [[ "${archive##*/}" =~ $MARAN_PBUPLOAD_NAME_PATTERN ]] \
    || { cli_pbupload_outcome refused "the argument is not a panel backup archive"; return 1; }
  work="$(mktemp -d "${CLI_PANEL_STAGE}/upload.XXXXXX")" \
    || { cli_pbupload_outcome refused "could not create the working directory"; return 1; }
  cli_pbupload_run "$archive" "$work" || rc=$?
  rm -rf -- "$work"
  return "$rc"
}
