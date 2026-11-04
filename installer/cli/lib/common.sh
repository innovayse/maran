#!/usr/bin/env bash
# common.sh: the constants and helpers every verb of the `maran` break-glass command shares.
#
# Sourced by /usr/local/sbin/maran AFTER its root check and before any verb runs; never executed
# on its own. It carries no `set` option of its own — the wrapper has already set -euo pipefail.
#
# The one rule that shapes everything here: the wrapper is root, the panel is not, and root must
# never touch the panel's data directly. Every question about panel state is therefore put to the
# panel's own binary (`Maran.Host --cli <verb>`) run as the service account, with the same
# environment the service itself starts with. Root contributes only what only root can read:
# systemd facts, journals, the firewall ruleset.

# The wrapper is root and runs openssl, curl, tar, gpg and the PostgreSQL clients on files that
# decide what root installs or restores. Settings those tools read from the environment can point
# them at another trust store, another configuration, another library or another database, and the
# environment is whatever the caller (sudo, a login shell) handed over. They are removed once, here,
# before any verb runs, so no verb has to remember to. The PG* family is found by prefix because
# libpq grows new variables; the service account reaches the database through its own peer
# authentication, and the connection details come from panel.env, never from the caller.
unset OPENSSL_CONF SSL_CERT_FILE SSL_CERT_DIR CURL_CA_BUNDLE CURL_HOME TAR_OPTIONS LD_PRELOAD LD_LIBRARY_PATH
while IFS= read -r _maran_scrubbed_name; do
  unset "$_maran_scrubbed_name"
done < <(compgen -v PG)
unset _maran_scrubbed_name

readonly MARAN_CLI_BINARY="/usr/local/maran/api/Maran.Host"
readonly MARAN_CLI_ENV_FILE="/etc/maran/panel.env"
readonly MARAN_CLI_SERVICE_USER="maran"
# The systemd units the verbs start, stop and restart: named here once, and every verb uses these.
readonly MARAN_CLI_API_UNIT="maran-api.service"
readonly MARAN_CLI_AGENT_UNIT="maran-agent.service"
readonly MARAN_CLI_BACKUP_TIMER_UNIT="maran-panel-backup.timer"
# Where every verb that builds something on disk stages it: root-owned 0700, outside every account's
# home and outside the service account's own /var/lib/maran.
readonly MARAN_CLI_STAGING_BASE="/var/lib/maran-cli-staging"

# cli_fail: prints one message on stderr and ends the command with exit 1.
cli_fail() {
  echo "maran: $*" >&2
  exit 1
}

# cli_audit_log: writes one fixed-shape line to the authentication log. It takes only verbs and
# outcomes — never an argument the operator typed and never a secret — and passes them as separate
# argv words, so no operator text is ever interpreted by anything.
cli_audit_log() {
  local verb="$1" outcome="$2" operator="${SUDO_USER:-root}"
  # SUDO_USER is set by sudo and is advisory; anything outside the account-name alphabet is logged
  # as a placeholder rather than passed on.
  [[ "$operator" =~ ^[A-Za-z0-9._-]{1,64}$ ]] || operator="unknown"
  logger -t maran-cli -p authpriv.notice -- "verb=${verb} outcome=${outcome} operator=${operator}" \
    2>/dev/null || true
}

# cli_require_trusted_env_file: refuses unless the live panel.env is a regular, non-symlink file
# owned by root with mode 0640 or stricter. Reading it means executing it as shell, so the file must
# be one that nobody but root can have written: a group- or world-writable panel.env would turn the
# service account (or anyone) into root through the next verb that reads it.
cli_require_trusted_env_file() {
  local file="$MARAN_CLI_ENV_FILE" owner mode
  [ -f "$file" ] && [ ! -L "$file" ] || cli_fail "${file} is not a regular file; refusing to read it"
  owner="$(stat -c '%u' -- "$file")"
  mode="$(stat -c '%a' -- "$file")"
  [ "$owner" = "0" ] || cli_fail "${file} must be owned by root; refusing to read it"
  # Any permission bit outside 0640 (a write bit for group or others, any other-read, a setuid,
  # setgid or sticky bit) refuses.
  [[ "$mode" =~ ^[0-7]{3,4}$ ]] && [ $(( 8#$mode & ~8#640 )) -eq 0 ] \
    || cli_fail "${file} must have mode 0640 or stricter (it is ${mode}); refusing to read it"
}

# cli_run_as_service: runs the program $1 with the arguments that follow as the service account,
# with the panel's environment, from the program's own directory, and returns its exit status. The
# ONE place that reads panel.env for a child: every verb that runs the panel's binary
# (cli_dotnet, the restore's migration, the update's migration) calls this.
#
# The environment is read the way systemd reads it and the way 65-schema.sh reads it, inside a
# subshell so the secrets never reach the wrapper's own environment or any other command it runs,
# and only after cli_require_trusted_env_file, because reading is executing. The working directory
# is the program's directory because the content root comes from it, and the service account can
# enter it wherever the operator happens to be standing. Standard input and output pass straight
# through. It refuses (through cli_fail) when the environment file is not trusted; a caller that
# must keep control then runs it inside a command substitution.
#   $1 = the absolute path of the program, the rest = its arguments.
cli_run_as_service() {
  local binary="${1:-}"
  [[ "$binary" == /* ]] || cli_fail "cli_run_as_service needs an absolute program path"
  shift
  cli_require_trusted_env_file
  (
    set -a
    # A failed read must end the subshell: callers run this where errexit is off, and carrying on
    # would start the program with a partial environment.
    # shellcheck disable=SC1090
    . "$MARAN_CLI_ENV_FILE" || exit 1
    set +a
    cd "$(dirname -- "$binary")" || exit 1
    exec runuser -u "$MARAN_CLI_SERVICE_USER" -- "$binary" "$@"
  )
}

# cli_dotnet: runs the panel's own binary in CLI mode as the service account, with the panel's
# environment, and returns its exit status. Standard input and output pass straight through, so a
# caller never has to capture what the binary prints, which matters for the one verb that prints a
# credential. The environment and the working directory are cli_run_as_service's business.
cli_dotnet() {
  [ -x "$MARAN_CLI_BINARY" ] || cli_fail "${MARAN_CLI_BINARY} is missing; is Maran installed?"
  [ -r "$MARAN_CLI_ENV_FILE" ] || cli_fail "${MARAN_CLI_ENV_FILE} is missing; is Maran installed?"
  cli_run_as_service "$MARAN_CLI_BINARY" --cli "$@"
}

# cli_require_private_out_dir: refuses unless $1 is an absolute path of plain characters with no
# `..` component, naming a plain (non-symlink) directory owned by root that neither group nor
# others can write. A verb writes something sensitive there, so the directory is checked, not
# interpreted, and nobody but root can swap a file into it.
cli_require_private_out_dir() {
  local out="$1" mode owner
  [[ "$out" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$out" != *..* ]] \
    || cli_fail "--out must be an absolute path of letters, digits, '.', '_', '-' and '/'"
  [ -d "$out" ] && [ ! -L "$out" ] || cli_fail "--out is not a plain directory"
  mode="$(stat -c '%a' -- "$out")"
  owner="$(stat -c '%u' -- "$out")"
  [ "$owner" = "0" ] || cli_fail "--out must be owned by root"
  # The last two permission digits are group and other; any write bit (2, 3, 6, 7) refuses.
  [[ "${mode: -2}" =~ ^[0145]{2}$ ]] || cli_fail "--out must not be writable by group or others"
}

# cli_prepare_staging_base: creates the staging base if absent and then CHECKS what is there:
# `install -d` accepts an existing path, so a symlink or a loosened mode left by anybody is refused
# rather than staged into. The same gate 50-artifacts.sh applies to its own staging directory.
cli_prepare_staging_base() {
  install -d -m 0700 -o root -g root "$MARAN_CLI_STAGING_BASE"
  if [ -L "$MARAN_CLI_STAGING_BASE" ] || [ ! -d "$MARAN_CLI_STAGING_BASE" ] \
     || [ "$(stat -c '%u %a' -- "$MARAN_CLI_STAGING_BASE")" != "0 700" ]; then
    cli_fail "${MARAN_CLI_STAGING_BASE} is not a root-owned 0700 directory; refusing to stage there"
  fi
}

# cli_panel_env_value: prints the value the live panel.env gives the setting named $1, read the way
# systemd reads the file, inside a subshell so no other setting reaches this shell. Only for the
# installed, root-owned file — an archive's copy is never sourced (see panel-restore.sh). Reading is
# executing, so the file is checked first (cli_require_trusted_env_file). It is meant for a command
# substitution, where errexit may be off: it returns non-zero, after printing why on stderr, when the
# file is missing, untrusted or fails to source, and the caller must test that status.
cli_panel_env_value() {
  local name="$1"
  [ -r "$MARAN_CLI_ENV_FILE" ] || { echo "maran: ${MARAN_CLI_ENV_FILE} is missing; is Maran installed?" >&2; return 1; }
  # A subshell: cli_require_trusted_env_file ends the shell it runs in when it refuses.
  (cli_require_trusted_env_file) || return 1
  (
    set -a
    # shellcheck disable=SC1090
    . "$MARAN_CLI_ENV_FILE" || exit 1
    set +a
    printf '%s' "${!name-}"
  )
}
