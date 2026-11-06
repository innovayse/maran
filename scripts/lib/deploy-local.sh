#!/usr/bin/env bash
# `maran deploy-local` (issue #410): build, sign with a TEST key, package and install on a development
# host over ssh. SOURCED by scripts/lib/release-bundle.sh, which supplies $root, cmd_build, cmd_sign,
# cmd_package, sha256_of and the suite.sh verdict helpers. Sourced files do not set shell options;
# release-bundle.sh has already set `-euo pipefail`. The threat note this file must keep true:
# docs/superpowers/notes/2026-11-06-deploy-local-threat-note.md.
#
# This file parses and validates; the build/ship stages live in deploy-local-ship.sh, which it sources.
# The validators it calls (deploy_valid_host, deploy_refuse_production_key, ...) are in
# deploy-local-guard.sh. State is held in DL_* globals shared with the ship file.

# shellcheck disable=SC1091
. "$root/scripts/lib/deploy-local-ship.sh"

DEPLOY_VERDICT="DEPLOY-LOCAL VERDICT"

# deploy_ok MSG...: prints the OK verdict on stdout. The caller returns normally afterwards.
deploy_ok() {
  echo "${DEPLOY_VERDICT}: OK - $*"
}

# deploy_failed MSG...: prints the FAILED verdict on stdout and exits 1. Used for a finding about the
# request or the run itself, never for a missing tool or an unreachable host.
deploy_failed() {
  echo "${DEPLOY_VERDICT}: FAILED - $*"
  exit 1
}

# deploy_did_not_run MSG...: prints the DID NOT RUN verdict on stdout and exits with the status
# suite.sh reserves for "nothing was measured" (2). Used for a missing tool, an unreachable host or a
# sudo that wants a password: facts about the machine, not findings about the code.
deploy_did_not_run() {
  suite_did_not_run "$DEPLOY_VERDICT" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}

# deploy_usage: prints the grammar on stderr and exits 2.
deploy_usage() {
  cat >&2 <<'USAGE'
usage: maran deploy-local [[user@]host] [--host H] [--user U] [--port N] [--identity PATH] [--key PATH]
                          [--config PATH] [--arch x86_64|aarch64] [--version V] [--channel stable|beta]
                          [--bundle DIR] [--ask-sudo-password] [--keep-remote] [--dry-run]
       maran deploy-local selftest

  Builds the tree, signs it with a TEST key (never the production key), packages it, copies the bundle and
  a copy of the installer carrying the TEST public key to the host, and runs the offline installer there
  under sudo. The host then trusts the TEST key, also for `maran update`: use it for development hosts only.
  ssh and scp run with StrictHostKeyChecking=yes (an unknown host key is refused) and BatchMode=yes;
  --ask-sudo-password drops BatchMode only so that sudo can prompt, and keeps PasswordAuthentication=no
  and KbdInteractiveAuthentication=no. The host stage directory is removed on EXIT, INT, TERM and HUP
  (best effort) unless --keep-remote.
  Precedence: command line, then the config file (default ~/.maran-dev/deploy-local.conf), then defaults.
USAGE
  exit 2
}

# dl_reset_state: clears every DL_* global so one process can run the verb more than once (the selftest).
dl_reset_state() {
  DL_HOST="" DL_USER="" DL_PORT="" DL_IDENT="" DL_KEY="" DL_CONFIG="" DL_ARCH="" DL_VERSION=""
  DL_CHANNEL="" DL_BUNDLE="" DL_POS="" DL_ASK_SUDO=0 DL_KEEP=0 DL_DRY=0 DL_CONFIG_EXPLICIT=0
  DL_TMP="" DL_STAGE="" DL_PUB_SHA="" DL_REPO_PUB="$root/installer/keys/release-signing.pub"
  DL_SSH=() DL_SSH_TTY=() DL_SCP=()
}

# dl_parse ARGS...: fills the DL_* globals from the command line. An option missing its value, an
# unknown option or a second positional is a FAILED verdict (nothing has been read or run yet).
dl_parse() {
  while [ $# -gt 0 ]; do
    case "$1" in
      --host|--user|--port|--identity|--key|--config|--arch|--version|--channel|--bundle)
        [ $# -ge 2 ] || deploy_failed "$1 requires a value" ;;
    esac
    case "$1" in
      --host) DL_HOST="$2"; shift 2 ;;
      --user) DL_USER="$2"; shift 2 ;;
      --port) DL_PORT="$2"; shift 2 ;;
      --identity) DL_IDENT="$2"; shift 2 ;;
      --key) DL_KEY="$2"; shift 2 ;;
      --config) DL_CONFIG="$2"; DL_CONFIG_EXPLICIT=1; shift 2 ;;
      --arch) DL_ARCH="$2"; shift 2 ;;
      --version) DL_VERSION="$2"; shift 2 ;;
      --channel) DL_CHANNEL="$2"; shift 2 ;;
      --bundle) DL_BUNDLE="$2"; shift 2 ;;
      --ask-sudo-password) DL_ASK_SUDO=1; shift ;;
      --keep-remote) DL_KEEP=1; shift ;;
      --dry-run) DL_DRY=1; shift ;;
      -h|--help) deploy_usage ;;
      -*) deploy_failed "unknown option: $1" ;;
      *) [ -z "$DL_POS" ] || deploy_failed "more than one destination given: $DL_POS and $1"
         DL_POS="$1"; shift ;;
    esac
  done
  # A positional [user@]host fills only what an explicit --host/--user left empty.
  if [ -n "$DL_POS" ]; then
    case "$DL_POS" in
      *@*) [ -n "$DL_USER" ] || DL_USER="${DL_POS%%@*}"
           [ -n "$DL_HOST" ] || DL_HOST="${DL_POS#*@}" ;;
      *) [ -n "$DL_HOST" ] || DL_HOST="$DL_POS" ;;
    esac
  fi
}

# dl_load_config: reads the config file through the strict reader (never sources it) and fills only the
# values the command line left empty. The default file is read only if it exists; an explicit --config
# that does not exist is a FAILED verdict. A path inside the repository or a refused file is FAILED.
dl_load_config() {
  [ -n "$DL_CONFIG" ] || DL_CONFIG="$HOME/.maran-dev/deploy-local.conf"
  if [ ! -e "$DL_CONFIG" ] && [ ! -L "$DL_CONFIG" ]; then
    [ "$DL_CONFIG_EXPLICIT" -eq 0 ] || deploy_failed "config file not found: $DL_CONFIG"
    return 0
  fi
  deploy_require_outside_repo "$DL_CONFIG" || deploy_failed "refusing a config file inside the repository"
  local lines k v
  lines="$(deploy_read_config "$DL_CONFIG")" || deploy_failed "config file refused (reason printed above)"
  while IFS='=' read -r k v; do
    case "$k" in
      host) [ -n "$DL_HOST" ] || DL_HOST="$v" ;;
      user) [ -n "$DL_USER" ] || DL_USER="$v" ;;
      port) [ -n "$DL_PORT" ] || DL_PORT="$v" ;;
      identity) [ -n "$DL_IDENT" ] || DL_IDENT="$v" ;;
      key) [ -n "$DL_KEY" ] || DL_KEY="$v" ;;
      arch) [ -n "$DL_ARCH" ] || DL_ARCH="$v" ;;
    esac
  done <<< "$lines"
}

# dl_validate: applies the defaults (port 22, the TEST key path, channel stable, user $USER only when
# nothing else named one), then checks every value that will reach a command or a path. FAILED when a
# value is invalid, no host was given, or --key/--identity/--config points inside the repository.
dl_validate() {
  [ -n "$DL_PORT" ] || DL_PORT=22
  [ -n "$DL_KEY" ] || DL_KEY="$HOME/.maran-dev/test-signing.key"
  [ -n "$DL_CHANNEL" ] || DL_CHANNEL="stable"
  [ -n "$DL_USER" ] || DL_USER="${USER:-}"
  [ -n "$DL_HOST" ] || deploy_failed "no host given: pass [user@]host or --host H, or set host= in ${DL_CONFIG} (there is no default host)"
  [ -n "$DL_USER" ] || deploy_failed "no user given: pass user@host or --user U, or set user= in ${DL_CONFIG}"
  deploy_valid_host "$DL_HOST" || deploy_failed "invalid host"
  deploy_valid_user "$DL_USER" || deploy_failed "invalid user"
  deploy_valid_port "$DL_PORT" || deploy_failed "invalid port"
  deploy_require_outside_repo "$DL_KEY" || deploy_failed "refusing a key path inside the repository"
  if [ -n "$DL_IDENT" ]; then
    deploy_require_outside_repo "$DL_IDENT" || deploy_failed "refusing an identity path inside the repository"
    deploy_refuse_production_path "$DL_IDENT" "ssh identity" || deploy_failed "refusing this ssh identity (reason printed above)"
  fi
  case "$DL_ARCH" in ""|x86_64|aarch64) ;; *) deploy_failed "--arch must be x86_64 or aarch64, got: $DL_ARCH" ;; esac
  case "$DL_CHANNEL" in stable|beta) ;; *) deploy_failed "--channel must be stable or beta, got: $DL_CHANNEL" ;; esac
  if [ -n "$DL_VERSION" ]; then
    [[ "$DL_VERSION" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*$ ]] || deploy_failed "invalid --version (letters, digits and . _ + - only)"
  fi
  [ -z "$DL_BUNDLE" ] || [ -d "$DL_BUNDLE" ] || deploy_failed "--bundle is not a directory: $DL_BUNDLE"
}

# dl_require_tool TOOL: DID NOT RUN (2) when TOOL is not on PATH, under this verb's own verdict prefix.
dl_require_tool() {
  command -v "$1" >/dev/null 2>&1 || deploy_did_not_run "$1 is not on PATH (needed to deploy to a host). source scripts/dev first, and see: maran check"
}

# deploy_local_main ARGS...: the verb. Stages in order: parse, config, validate, tools, production-key
# refusal, then (dry run) the printed plan or (real run) arch detection, build, sign, package, installer
# copy, ship. `selftest` as the first argument runs deploy_local_selftest instead.
deploy_local_main() {
  if [ "${1:-}" = "selftest" ]; then
    deploy_local_selftest
    return
  fi
  dl_reset_state
  dl_parse "$@"
  dl_load_config
  dl_validate
  local tool
  for tool in ssh scp openssl tar; do dl_require_tool "$tool"; done
  deploy_refuse_production_key "$DL_KEY" "$DL_REPO_PUB" \
    || deploy_failed "refusing this signing key (reason printed above)"
  DL_PUB_SHA="$(sha256_of "$DL_REPO_PUB")"
  dl_ssh_arrays
  if [ "$DL_DRY" -eq 1 ]; then
    dl_dry_run
    deploy_ok "dry run, nothing was executed"
    return
  fi
  trap dl_cleanup EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM
  trap 'exit 129' HUP
  dl_detect_arch
  dl_probe_sudo
  DL_TMP="$(mktemp -d)"
  dl_build_sign_package
  dl_ship
  dl_check_repo_pub
  echo "WARNING: this host now trusts the TEST key (also for maran update): a development host from now on."
  echo "UNOBSERVED HERE: the host's service health after the install; only the installer's exit status was read - run maran status on the host."
  deploy_ok "installer finished on ${DL_USER}@${DL_HOST} (${DL_ARCH})"
}
