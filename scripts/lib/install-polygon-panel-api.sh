#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# The helpers the panel-recovery stage (install-polygon-recovery.sh) uses to talk to a panel
# running in the install polygon's container, and to the container around it. Nothing here asserts
# anything: the stage decides what a status or a file means, because a helper that also judged would
# hide which assertion failed.
#
# Every function reads the harness's global `container` at CALL time, not at definition time. The
# stage swaps that variable from the server that is about to die to the freshly installed one, and
# the same helpers then talk to whichever server is current.
#
# Credentials in this file are throwaway values the harness made up for a container that is deleted
# at the end of the run; they travel on `docker exec` argv exactly as the lockout-recovery stage's do.

# REC_CODE and REC_BODY: the HTTP status and body of the last rec_call. REC_TOKEN: the access token
# of the last sign-in, empty when the sign-in stopped for a second factor.
REC_CODE=""
REC_BODY=""
REC_TOKEN=""
# REC_TOTP_WINDOW: the 30-second window of the last one-time code the harness used. The panel
# refuses a code from a window at or before the last one it accepted (replay protection), so a
# second sign-in has to wait for a window the first did not use.
REC_TOTP_WINDOW=0
REC_TOTP_CODE=""

# rec_json_field: the first string value of the field named $2 in the JSON text $1, or nothing.
#
# A grep and not a JSON parser on purpose: the harness reads three or four well-known fields from
# bodies the panel itself serializes compactly, and `|| true` is what keeps a missing field an empty
# answer instead of a pipefail death of the whole harness.
rec_json_field() {
  { printf '%s' "$1" | grep -o "\"$2\":\"[^\"]*\"" || true; } | head -1 | cut -d'"' -f4
}

# rec_call: one JSON request to the panel through nginx, in the current container. Sets REC_CODE to
# the HTTP status ("000" when curl could not connect) and REC_BODY to the response body.
#   $1 = method, $2 = path (with any query), $3 = JSON body or empty, $4 = bearer token or empty.
#
# The body is KEPT, unlike panel_post's, because this stage reads ids and tokens out of responses.
rec_call() {
  local method="$1" path="$2" body="${3:-}" token="${4:-}" out
  local -a args=(-sk --max-time 30 -X "$method" -H 'Content-Type: application/json' -w $'\n%{http_code}')
  [ -z "$token" ] || args+=(-H "Authorization: Bearer ${token}")
  [ -z "$body" ] || args+=(--data "$body")
  out="$(docker exec "$container" curl "${args[@]}" "https://127.0.0.1:8443${path}" 2>/dev/null \
    | tr -d '\r')" || out=$'\n000'
  REC_CODE="${out##*$'\n'}"
  REC_BODY="${out%$'\n'*}"
  [ -n "$REC_CODE" ] || REC_CODE="000"
}

# rec_login: signs in by password. Sets REC_CODE/REC_BODY and REC_TOKEN (empty when the panel asks
# for a second factor instead of issuing a session).
#
# The username is repeated in the query string because the sign-in rate limiter partitions on it
# (5 attempts per address per 300 seconds): without it every sign-in of this stage would share the
# partition the lockout stage already spent.
#   $1 = username, $2 = password.
rec_login() {
  rec_call POST "/api/v1/auth/login?username=$1" "{\"username\":\"$1\",\"password\":\"$2\"}"
  REC_TOKEN="$(rec_json_field "$REC_BODY" accessToken)"
}

# rec_second_factor: finishes a sign-in that stopped for a code, using the one-time code of the
# secret $3 for the current window. Sets REC_CODE/REC_BODY/REC_TOKEN.
#   $1 = username, $2 = password, $3 = base32 secret.
rec_second_factor() {
  rec_totp_fresh "$3"
  rec_call POST "/api/v1/auth/two-factor?username=$1" \
    "{\"username\":\"$1\",\"password\":\"$2\",\"code\":\"${REC_TOTP_CODE}\"}"
  REC_TOKEN="$(rec_json_field "$REC_BODY" accessToken)"
}

# rec_totp_script: the program that turns a base32 secret into an RFC 6238 code (HMAC-SHA1, 30
# seconds, six digits), printed to standard output. It runs INSIDE the container because that is
# where `openssl` is guaranteed (the installer installs it) and the host's is not.
#   arguments of the printed script: $1 = secret, $2 = window offset (default 0), $3 = a fixed unix
#   time for a self-test (default: now).
#
# Kept as printed text so it can be fed to `bash -s` locally against the RFC's own test vector
# (secret GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ at time 59 yields ...287082): a code generator whose
# output was never compared with the standard would make a wrong sign-in look like a decryption
# failure.
rec_totp_script() {
  cat <<'TOTP'
set -eu
secret="$1"; offset="${2:-0}"; now="${3:-$(date +%s)}"
alphabet=ABCDEFGHIJKLMNOPQRSTUVWXYZ234567
bits=""
for ((i = 0; i < ${#secret}; i++)); do
  ch="${secret:i:1}"
  [ "$ch" != "=" ] || break
  head="${alphabet%%"$ch"*}"
  [ "$head" != "$alphabet" ] || { echo "not a base32 character" >&2; exit 1; }
  value=${#head}
  for ((b = 4; b >= 0; b--)); do bits+=$(( (value >> b) & 1 )); done
done
key=""
for ((i = 0; i + 8 <= ${#bits}; i += 8)); do
  key+="$(printf '%02x' "$((2#${bits:i:8}))")"
done
counter=$(( now / 30 + offset ))
message=""
for ((s = 56; s >= 0; s -= 8)); do message+="$(printf '\\x%02x' $(( (counter >> s) & 255 )))"; done
mac="$(printf '%b' "$message" | openssl dgst -sha1 -mac HMAC -macopt "hexkey:${key}" -r | cut -d' ' -f1)"
start=$(( 16#${mac:39:1} ))
chunk=$(( 16#${mac:$((start * 2)):8} & 0x7fffffff ))
printf '%06d\n' $(( chunk % 1000000 ))
TOTP
}

# rec_totp_code: the code for secret $1 at window offset $2 (0 = now, -1 = the window before), read
# from the current container's clock.
rec_totp_code() {
  rec_totp_script | docker exec -i "$container" bash -s -- "$1" "${2:-0}" | tr -d '\r'
}

# rec_totp_fresh: sets REC_TOTP_CODE to a code from a window the harness has not used yet, waiting
# for the next one when it must. Not called in a command substitution: the window it records has to
# survive the call.
#   $1 = base32 secret.
rec_totp_fresh() {
  while [ $(( $(date +%s) / 30 )) -le "$REC_TOTP_WINDOW" ]; do sleep 1; done
  REC_TOTP_CODE="$(rec_totp_code "$1" 0)"
  REC_TOTP_WINDOW=$(( $(date +%s) / 30 ))
}

# rec_totp_wrong: a six-digit code that is neither this window's nor the previous one's, so a
# sign-in with it can only be refused for being wrong. The panel accepts one window back, which is
# why a fixed "000000" would be a coin the size of one in 500,000 and not a control.
#   $1 = base32 secret. Sets REC_TOTP_CODE.
rec_totp_wrong() {
  local current previous candidate
  current="$(rec_totp_code "$1" 0)"
  previous="$(rec_totp_code "$1" -1)"
  for candidate in 000000 111111 222222; do
    if [ "$candidate" != "$current" ] && [ "$candidate" != "$previous" ]; then
      REC_TOTP_CODE="$candidate"
      return 0
    fi
  done
}

# rec_put_file: writes standard input to a root-owned file in a container, mode $2 from the file's
# first byte (umask 077, then the explicit chmod for a file that already existed).
#   $1 = container, $2 = mode, $3 = absolute path.
rec_put_file() {
  docker exec -i "$1" bash -c 'umask 077; cat > "$1"; chown root:root "$1"; chmod "$2" "$1"' _ "$3" "$2"
}

# rec_env_hash: the sha256 of the value the current container's /etc/maran/panel.env gives the
# setting $1, so two servers' secrets can be compared without a value ever being printed. An absent
# or empty setting hashes to the hash of nothing, which the callers refuse as "no key to compare".
rec_env_hash() {
  docker exec "$container" sed -n "s/^$1=//p" /etc/maran/panel.env | tr -d '\r' | sha256sum | cut -d' ' -f1
}

# REC_EMPTY_HASH: sha256 of the empty string, the answer rec_env_hash gives when the setting is
# missing. Spelled out so a "different from the old server" check cannot pass on two empty values.
REC_EMPTY_HASH="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"

# rec_psql: one single-value query against the panel database, run as the postgres OS user over the
# unix socket (peer authentication) inside the current container. Prints the value.
#   $1 = SQL.
rec_psql() {
  docker exec -w /tmp "$container" runuser -u postgres -- psql -d maran -tA -c "$1" 2>/dev/null \
    | tr -d '\r' || true
}

# rec_archives: the names of the panel archives that are REGULAR files directly inside the
# directory $1, sorted, one per line. The same name pattern retention deletes by, written out here
# a second time on purpose: the stage must not read its expectation out of the code it checks.
rec_archives() {
  { docker exec "$container" find "$1" -maxdepth 1 -type f -printf '%f\n' 2>/dev/null | tr -d '\r' \
      | grep -E '^maran-panel-backup-[0-9]{8}T[0-9]{6}Z-[0-9]+\.tar\.gpg$' || true; } | sort
}

# rec_lines: how many non-empty lines the text $1 holds.
rec_lines() {
  { printf '%s\n' "$1" | grep -c . || true; }
}

# rec_journal_failures: how many scheduled-backup failure lines the maran-cli journal tag holds in
# the current container. The scheduled verb writes exactly one per failed run, so the difference
# across a run is the assertion.
rec_journal_failures() {
  docker exec "$container" journalctl --sync >/dev/null 2>&1 || true
  { docker exec "$container" journalctl -t maran-cli --no-pager -o cat 2>/dev/null | tr -d '\r' \
      | grep -c 'verb=panel-backup-scheduled outcome=failed' || true; }
}

# rec_journal_matches: how many lines of the maran-cli journal tag in the current container contain
# the fixed text $1.
rec_journal_matches() {
  docker exec "$container" journalctl --sync >/dev/null 2>&1 || true
  { docker exec "$container" journalctl -t maran-cli --no-pager -o cat 2>/dev/null | tr -d '\r' \
      | grep -cF -- "$1" || true; }
}

# rec_leak_count: how many of the harness's secrets (the passphrase and the S3 credentials, when
# set) appear in the text $1. A refusal or a success that printed one is a defect in the product.
rec_leak_count() {
  local secret hits=0
  for secret in "${REC_PASSPHRASE:-}" "${REC_S3_SECRET:-}" "${REC_S3_KEY_ID:-}"; do
    [ -n "$secret" ] || continue
    case "$1" in *"$secret"*) hits=$((hits + 1)) ;; esac
  done
  echo "$hits"
}

# rec_refused: "refused" for a non-zero status and "accepted" for zero, so a check reads as words.
rec_refused() {
  if [ "$1" -ne 0 ]; then echo refused; else echo accepted; fi
}
