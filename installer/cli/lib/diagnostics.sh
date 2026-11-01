#!/usr/bin/env bash
# diagnostics.sh: `maran collect-diagnostics` — a redacted support bundle the operator can send.
#
# The bundle is built from an ALLOW-LIST: only the sources named in cli_diag_collect go in. It never
# reads /home, /var/lib/maran*, backups, TLS or signing keys, database dumps, or the VALUES of
# panel.env (names only). Anything it does include passes three barriers before it may leave:
#
#   1. every text is streamed through the panel's own redactor (`Maran.Host --cli redact`), which
#      knows this host's live configuration secrets as well as the general secret patterns;
#   2. the finished archive is extracted into a fresh private directory and scanned for every live
#      secret value this host's configuration holds, and for PEM key blocks;
#   3. a hit on either deletes the archive and fails the command. It never "warns and keeps".
#
# Redaction is best-effort by nature, so the command ends by telling the operator to read the
# bundle before sending it.

readonly MARAN_DIAG_DEFAULT_OUT="/root"
readonly MARAN_DIAG_JOURNAL_LINES=20000
readonly MARAN_DIAG_LOG_TAIL_LINES=5000
readonly MARAN_DIAG_CERT_PATH="/etc/maran/tls/panel.crt"

# cli_diag_add: runs one fixed command and writes its output, redacted, to a file in the staging
# directory. The command's own failure is recorded in the file and tolerated (a stopped service
# should still yield a bundle); a failure of the REDACTOR is not, and fails the pipeline. The raw
# output only ever exists in the pipe, never on disk.
#   $1 = file name inside the bundle, the rest = the command and its arguments.
cli_diag_add() {
  local name="$1"
  shift
  { "$@" 2>&1 || echo "[command exited non-zero]"; } \
    | cli_dotnet redact > "${CLI_DIAG_STAGE}/${name}" \
    || cli_fail "the redactor failed on ${name}; no bundle was produced"
}

# cli_diag_add_file: like cli_diag_add for a regular, non-symlink file, optionally its tail only.
#   $1 = file name inside the bundle, $2 = source path, $3 = "all" or a line count.
cli_diag_add_file() {
  local name="$1" source="$2" lines="$3"
  [ -f "$source" ] && [ ! -L "$source" ] || return 0
  if [ "$lines" = "all" ]; then
    cli_diag_add "$name" cat -- "$source"
  else
    cli_diag_add "$name" tail -n "$lines" -- "$source"
  fi
}

# cli_diag_env_names: prints the NAMES of the settings in panel.env and never a value. The sed
# prints only the captured name of an assignment line; every other line is dropped.
cli_diag_env_names() {
  sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' -- "$MARAN_CLI_ENV_FILE"
}

# cli_diag_cert_facts: the panel certificate's subject and validity window, never the key.
cli_diag_cert_facts() {
  [ -f "$MARAN_DIAG_CERT_PATH" ] || { echo "no certificate at the expected path"; return 0; }
  openssl x509 -noout -subject -issuer -dates -in "$MARAN_DIAG_CERT_PATH"
}

# cli_diag_collect: stages every allow-listed source. $1 = "logs" or "nologs".
cli_diag_collect() {
  local logs="$1" file base

  {
    echo "Maran diagnostics bundle"
    echo "created (UTC): $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    echo "logs included: ${logs}"
    echo "Contents are listed in this directory; each text was redacted, but read before sending."
  } | cli_dotnet redact > "${CLI_DIAG_STAGE}/manifest.txt" \
    || cli_fail "the redactor failed on manifest.txt; no bundle was produced"

  cli_diag_add status.txt cli_status
  cli_diag_add_file os-release.txt /etc/os-release all
  cli_diag_add units.txt systemctl cat maran-api.service maran-agent.service maran-ftps.service
  cli_diag_add unit-states.txt systemctl list-units --all --no-pager 'maran-*' postgresql.service nginx.service
  cli_diag_add_file nginx-maran.conf /etc/nginx/conf.d/maran.conf all
  cli_diag_add_file nginx-maran-sites.conf /etc/nginx/conf.d/maran-sites.conf all
  cli_diag_add panel-env-names.txt cli_diag_env_names
  cli_diag_add certificate.txt cli_diag_cert_facts
  cli_diag_add firewall-ruleset.txt nft list ruleset

  if [ "$logs" = "logs" ]; then
    cli_diag_add journal.txt journalctl --no-pager --since '7 days ago' \
      -n "$MARAN_DIAG_JOURNAL_LINES" -u maran-api.service -u maran-agent.service -u nginx.service
    # Only the product's own top-level logs; /var/log/maran/sites holds customers' logs and is not
    # on the list.
    for file in /var/log/maran/*.log /var/log/maran/*.log.1; do
      base="$(basename -- "$file")"
      cli_diag_add_file "log-${base}" "$file" "$MARAN_DIAG_LOG_TAIL_LINES"
    done
  fi
}

# cli_diag_scan: extracts the finished archive into a fresh private directory and looks for what
# redaction must have removed. Returns 0 only when nothing was found. Reports the NAME of the
# setting that leaked, never its value.
#   $1 = archive path.
cli_diag_scan() {
  local archive="$1" scan name value piece found=0
  scan="$(mktemp -d "${MARAN_CLI_STAGING_BASE}/scan.XXXXXX")"
  # Recorded for the EXIT trap: a scan directory holds the extracted bundle, and a kill mid-scan
  # must not leave it behind.
  CLI_DIAG_SCAN="$scan"
  if ! tar -xzf "$archive" -C "$scan"; then
    rm -rf -- "$scan"
    echo "maran: could not read the bundle back for scanning" >&2
    return 1
  fi

  if grep -rqE -- '-----BEGIN' "$scan"; then
    echo "maran: a PEM key or certificate block survived redaction" >&2
    found=1
  fi

  # Every setting whose name says it is a secret, compared by VALUE. The value reaches grep through
  # a process substitution, not argv, so it is never visible in a process list. Values under eight
  # characters are skipped: they would match ordinary text and prove nothing.
  while IFS= read -r name; do
    # Case-insensitive: the names are Security__EncryptionKey and Setup__Token today, but an
    # upper-case SOME_API_KEY must not slip past a pattern that only knew the camel-case spelling.
    # The name must END in the word, as the redactor's own rule does (SecretTextRedactor): a
    # substring match made PasswordReset__PanelUrl a "secret", found its URL in the logs and refused
    # every bundle on every install (#68's real-install run).
    [[ "${name,,}" =~ (key|token|password|secret|connectionstring|credential)$ ]] || continue
    value="$(
      set -a
      # shellcheck disable=SC1090
      . "$MARAN_CLI_ENV_FILE"
      set +a
      printf '%s' "${!name-}"
    )"
    # One pattern per line of the value: a multi-line value fed to `grep -f` whole would contribute
    # an empty pattern for a blank line, which matches everything.
    while IFS= read -r piece; do
      [ "${#piece}" -ge 8 ] || continue
      if grep -rqF -f <(printf '%s\n' "$piece") -- "$scan"; then
        echo "maran: the value of ${name} survived redaction" >&2
        found=1
        break
      fi
    done <<< "$value"
  done < <(cli_diag_env_names)

  rm -rf -- "$scan"
  CLI_DIAG_SCAN=""
  [ "$found" -eq 0 ]
}

# cli_diagnostics: the verb. Arguments: [--out DIR] [--no-logs].
cli_diagnostics() {
  local out="$MARAN_DIAG_DEFAULT_OUT" logs="logs" arg
  while [ "$#" -gt 0 ]; do
    arg="$1"
    case "$arg" in
      --no-logs) logs="nologs" ;;
      --out)
        [ "$#" -ge 2 ] || cli_fail "--out needs a directory"
        out="$2"
        shift
        ;;
      *) cli_fail "unknown option. Usage: maran collect-diagnostics [--out DIR] [--no-logs]" ;;
    esac
    shift
  done

  cli_require_private_out_dir "$out"
  cli_prepare_staging_base
  CLI_DIAG_STAGE="$(mktemp -d "${MARAN_CLI_STAGING_BASE}/stage.XXXXXX")"
  # Globals, not locals: the EXIT trap runs after this function has returned.
  CLI_DIAG_TARGET=""
  CLI_DIAG_CREATED=0
  CLI_DIAG_OK=0
  CLI_DIAG_SCAN=""
  # Leaves nothing behind on any exit: the staged (redacted) texts and any half-finished scan
  # directory always, the archive unless it was completed and passed every barrier. Signals are
  # turned into exits so the EXIT trap below runs for them too.
  trap 'exit 130' INT
  trap 'exit 143' TERM HUP
  trap 'rm -rf -- "$CLI_DIAG_STAGE" ${CLI_DIAG_SCAN:+"$CLI_DIAG_SCAN"}; if [ "$CLI_DIAG_CREATED" -eq 1 ] && [ "$CLI_DIAG_OK" -ne 1 ]; then rm -f -- "$CLI_DIAG_TARGET"; fi' EXIT

  cli_diag_collect "$logs"

  local target
  target="${out%/}/maran-diagnostics-$(date -u '+%Y%m%dT%H%M%SZ')-$$.tar.gz"
  # noclobber makes the redirection an O_EXCL create: an existing file or a planted symlink at the
  # target is refused instead of followed. umask 077 gives it mode 0600 from the first byte. The
  # descriptor is kept and tar writes to it, so the file is opened exactly once and never re-opened
  # by name.
  # The open happens inside a group so its 2>/dev/null applies to the open alone. A bare
  # `exec 9> ... 2>/dev/null` made the stderr redirection permanent, and every later refusal
  # of this command was printed to /dev/null (#68's real-install run).
  set -C
  if ! { exec 9> "$target"; } 2>/dev/null; then
    set +C
    cli_fail "refusing to write ${target}: it already exists or cannot be created"
  fi
  set +C
  CLI_DIAG_TARGET="$target"
  CLI_DIAG_CREATED=1
  tar --owner=0 --group=0 --numeric-owner -czf - -C "$CLI_DIAG_STAGE" . >&9 \
    || cli_fail "could not build the archive"
  exec 9>&-

  if ! cli_diag_scan "$target"; then
    cli_fail "the bundle failed the secret scan and was deleted; nothing was written"
  fi
  CLI_DIAG_OK=1
  chmod 0600 -- "$target"

  echo "Diagnostics bundle: ${target}"
  echo
  echo "Review it before sending: extract it and read it. Redaction removes the secrets this"
  echo "host is known to hold and common secret patterns, but it cannot know everything — IP"
  echo "addresses, host names and customer domain names are NOT removed. Send it only to"
  echo "people you trust with that information."
}
