#!/usr/bin/env bash
# Maran native production installer entry point.
#
# Usage: sudo bash install.sh [--offline-tarball <path>] [--channel stable|beta]
#
# Run from an unpacked, verified installer package — never piped straight from the network
# into a shell. Piping is not merely discouraged: it cannot work here, because the steps in
# lib/, the systemd units, the nginx template and the release signing key are all resolved
# relative to this file, and a pipe has no such directory.
#
# This script is deliberately thin: it detects the OS/arch, verifies it is one of
# Maran's supported targets, sets up logging, then sources and runs the numbered
# steps under lib/ in order. All privileged, distro-specific and feature-specific logic
# lives in those step files, not here. Every step is idempotent (checks current state
# before acting) so the whole script is safe to re-run after an interrupted install —
# re-running simply resumes at whatever is not yet done.
set -euo pipefail

# Resolve the real directory of this script, following symlinks. There is no pipe fallback:
# see the usage note above — everything the installer needs lives beside this file.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-.}")" >/dev/null 2>&1 && pwd -P)"
LIB_DIR="${SCRIPT_DIR}/lib"
# Exported because step files resolve their own sibling assets (systemd/, nginx/, keys/)
# relative to the installer package, not to whatever directory the operator ran it from.
export SCRIPT_DIR LIB_DIR

MARAN_LOG_DIR="/var/log/maran"
MARAN_LOG_FILE="${MARAN_LOG_DIR}/install.log"
# The leaf names inside MARAN_LOG_DIR that a ROOT process opens for append: this script's own
# install log, and the two the root nginx MASTER opens for the panel vhost
# (installer/nginx/maran.conf). They are listed in one place because they are the reason the
# directory must be root-owned, and because harden_log_directory below has to neutralise a
# symbolic link planted at any of them on a server installed before that was true.
# Exported so installer/lib/40-user.sh re-states the same three names rather than inventing
# its own list.
MARAN_ROOT_TRUSTED_LOG_NAMES="install.log nginx-access.log nginx-error.log"
export MARAN_LOG_DIR MARAN_ROOT_TRUSTED_LOG_NAMES

# --- The panel's system account ----------------------------------------------------
# The one place the name of the unprivileged principal the API runs as is decided, in the same
# spirit as the port and the socket path below: step 40 creates it, step 60 and step 80 give it
# the group of /etc/maran, panel.env, the TLS key and the socket directory, step 70 writes it
# into both units, step 30 names the PostgreSQL role after it (peer authentication matches the
# role name to the OS user name), and step 15 renames a pre-rename installation onto it.
#
# It is `maran` and no longer `panel`. `panel` was the one name this product put on a host that
# did not say whose it is, and — worse — step 40's idempotence was `id -u`, so a host that
# already had an unrelated account of that name had it silently adopted and handed /etc/maran,
# /var/lib/maran, the TLS key, `User=` on the api unit and an admitted uid on the root daemon's
# socket. See docs/superpowers/notes/2026-09-09-service-account-rename-threat-note.md for the
# argument, for why the user and the group share one name, and for why `maran-panel` was
# rejected (`ps` truncates it, and a hyphen is not a bare SQL identifier for the peer-auth role).
#
# The steps read these from the environment and abort by name when they are unset, so a step
# driven on its own — the polygon images do exactly that — fails loudly instead of expanding an
# empty string into a `chown root:` or an `install -o`.
MARAN_USER=maran
MARAN_GROUP=maran
# The account this product created before the rename, and the name step 15 migrates FROM. It is
# never created and never adopted; it exists in this file only so that one place decides both
# ends of the migration.
MARAN_LEGACY_USER=panel
MARAN_LEGACY_GROUP=panel
# The GECOS field step 40 stamps on the account it creates, and the only evidence this installer
# has that an account of its own name is its own. It is what makes adoption deliberate: an
# account carrying it is a previous run of ours and is adopted silently (which is what keeps the
# installer idempotent), an account without it stops the install. It is root-writable and is
# therefore provenance against ACCIDENT, never authentication — see the threat note.
MARAN_SERVICE_ACCOUNT_MARKER="Maran panel service account"
export MARAN_USER MARAN_GROUP MARAN_LEGACY_USER MARAN_LEGACY_GROUP MARAN_SERVICE_ACCOUNT_MARKER

# --- The panel's public port -------------------------------------------------------
# The one place this number is decided. nginx listens on it, preflight refuses to install
# when something else already holds it, and the finish step prints it in the URL handed to
# the operator. Anything added later that needs the number derives it from here rather
# than repeating it: a port written as a literal in four files is a port that is wrong in
# three of them the first time an operator changes it.
#
# Set here, before main() runs, and therefore before run_step sources anything under lib/ —
# so a step file may derive from it at source time (10-preflight.sh does) as well as inside
# a function.
#
# The one site that cannot read it is the nginx vhost's own `listen` line: a configuration
# file interpolates no shell variable. That literal is tied back to this one by an assertion
# in docker/polygon/assert-installer-steps.sh, which fails the polygon image build when the
# two disagree — a failing check in place of a hope.
MARAN_PANEL_PORT=8443
export MARAN_PANEL_PORT

# --- The panel's listening socket --------------------------------------------------
# The one place this path is decided, and the panel's trust boundary. The api binds it instead
# of a loopback TCP port so that WHICH LOCAL PROCESS connected is a kernel fact rather than a
# guess: a port on 127.0.0.1 is reachable by every uid on the box, and everything that reaches
# it arrives with the source address the panel trusts as its reverse proxy.
#
# Read by 60-config.sh (into ASPNETCORE_URLS), by 80-nginx.sh (into the vhost's upstream) and by
# 70-services.sh, which substitutes both this path and its directory half into the api unit and
# into the tmpfiles snippet that builds that directory. Nothing spells either one a second time;
# the polygon's assert-installer-steps.sh builds the directory from this value and checks it.
MARAN_API_SOCKET_PATH=/run/maran-api/api.sock
export MARAN_API_SOCKET_PATH

# --- The panel's public port -------------------------------------------------------
# The one place this number is decided. nginx listens on it, preflight refuses to install
# when something else already holds it, and the finish step prints it in the URL handed to
# the operator. Anything added later that needs the number derives it from here rather
# than repeating it: a port written as a literal in four files is a port that is wrong in
# three of them the first time an operator changes it.
#
# Set here, before main() runs, and therefore before run_step sources anything under lib/ —
# so a step file may derive from it at source time (10-preflight.sh does) as well as inside
# a function.
#
# The one site that cannot read it is the nginx vhost's own `listen` line: a configuration
# file interpolates no shell variable. That literal is tied back to this one by an assertion
# in docker/polygon/assert-installer-steps.sh, which fails the polygon image build when the
# two disagree — a failing check in place of a hope.
MARAN_PANEL_PORT=8443
export MARAN_PANEL_PORT

# --- The panel's listening socket --------------------------------------------------
# The one place this path is decided, and the panel's trust boundary. The api binds it instead
# of a loopback TCP port so that WHICH LOCAL PROCESS connected is a kernel fact rather than a
# guess: a port on 127.0.0.1 is reachable by every uid on the box, and everything that reaches
# it arrives with the source address the panel trusts as its reverse proxy.
#
# Read by 60-config.sh (into ASPNETCORE_URLS), by 80-nginx.sh (into the vhost's upstream) and by
# 70-services.sh, which substitutes both this path and its directory half into the api unit and
# into the tmpfiles snippet that builds that directory. Nothing spells either one a second time;
# the polygon's assert-installer-steps.sh builds the directory from this value and checks it.
MARAN_API_SOCKET_PATH=/run/maran-api/api.sock
export MARAN_API_SOCKET_PATH

# --- CLI arguments -----------------------------------------------------------------
# Parsed once here and exported so any step file can read them without re-parsing argv.
MARAN_CHANNEL="stable"
MARAN_OFFLINE_TARBALL=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --channel)
      MARAN_CHANNEL="${2:?--channel requires a value}"
      shift 2
      ;;
    --offline-tarball)
      MARAN_OFFLINE_TARBALL="${2:?--offline-tarball requires a path}"
      shift 2
      ;;
    *)
      echo "install.sh: unknown argument: $1" >&2
      exit 2
      ;;
  esac
done
export MARAN_CHANNEL MARAN_OFFLINE_TARBALL

# require_root: the installer performs privileged operations (package install, service
# management, user creation) from step 20 onward; refuse early rather than fail deep
# inside a step with a half-finished state.
require_root() {
  if [ "$(id -u)" -ne 0 ]; then
    echo "install.sh: must be run as root (try: sudo bash install.sh)" >&2
    exit 1
  fi
}

# MARAN_LOG_DIR_WARNINGS: anything harden_log_directory has to say. It runs BEFORE stdout is
# redirected into the log file, so a message printed there would reach the terminal and never the
# install log — and a "somebody had planted a symlink at your install log" line is precisely the
# one an operator needs to find again tomorrow. The messages are collected here and replayed by
# setup_logging once the redirection is in place.
MARAN_LOG_DIR_WARNINGS=""

# harden_log_directory: make /var/log/maran a directory only root can create entries in, and
# neutralise anything already planted at a name root opens for append.
#
# Why this exists (docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md,
# section "/var/log/maran"): this directory used to be created panel:panel 0750 by step 40, while
# root appended to `install.log` here and the root nginx MASTER opened `nginx-access.log` and
# `nginx-error.log` here. The panel uid owning the directory can unlink either name and leave a
# symbolic link in its place without ever having permission to enter the target. Both follows were
# measured under real root with the distribution's own nginx: root's `tee -a` appended into a
# root-owned 0600 file, and nginx appended a line whose request target the attacker chose.
# `fs.protected_symlinks` does not help — it engages only in a world-writable STICKY directory, and
# this one is 0750.
#
# So the directory is root's, and the panel writes in a subdirectory of its own that step 40
# creates (/var/log/maran/panel). This function is in install.sh rather than in a step file
# because it must run before the FIRST root write, which is this script's own logging — earlier
# than step 40 and earlier than every gate.
#
# The group is deliberately left alone here: on a fresh install the service group does not exist
# yet (step 40 creates it) and on an upgrade it is already the right one — an upgrade from before
# the rename included, because step 15 renames the group in place and a rename keeps the gid, so
# this directory's group is still the right gid under its new name. Step 40 sets it.
#
# Nothing is deleted. An operator's logs are their record of every install this server has had,
# and a planted symlink is evidence of an attempted escalation; both are kept, the link under a
# name no root process opens.
harden_log_directory() {
  local name path stamp aside owner
  stamp="$(date -u '+%Y%m%dT%H%M%SZ')"

  # A symbolic link AT the directory itself. /var/log is root:root 0755 on both families, so no
  # unprivileged uid can put one here — but `mkdir -p` and `chmod` would both follow it, and a
  # refusal costs nothing next to reaching through somebody else's link as root.
  if [ -L "$MARAN_LOG_DIR" ]; then
    echo "install.sh: ${MARAN_LOG_DIR} is a symbolic link, not a directory. Refusing to log" >&2
    echo "  through it. Move it aside and re-run the installer." >&2
    exit 1
  fi
  mkdir -p "$MARAN_LOG_DIR"
  # An upgrade inherits the directory this defect created: owned by the service account. Take it back.
  owner="$(stat -c '%u' "$MARAN_LOG_DIR")"
  if [ "$owner" -ne 0 ]; then
    chown root "$MARAN_LOG_DIR"
    MARAN_LOG_DIR_WARNINGS="${MARAN_LOG_DIR_WARNINGS}NOTE: ${MARAN_LOG_DIR} was owned by uid ${owner}, not root, and has been taken back by this upgrade. Root appends its install log and nginx opens the panel vhost's logs in this directory, so a uid that owns it can redirect either write into any file root can append to.
"
  fi
  chmod 750 "$MARAN_LOG_DIR"

  # Whatever the previous owner may have left at the three names root opens.
  for name in $MARAN_ROOT_TRUSTED_LOG_NAMES; do
    path="${MARAN_LOG_DIR}/${name}"
    if [ -L "$path" ]; then
      aside="${path}.planted-symlink.${stamp}"
      mv -f -- "$path" "$aside"
      MARAN_LOG_DIR_WARNINGS="${MARAN_LOG_DIR_WARNINGS}SECURITY: ${path} was a SYMBOLIC LINK to $(readlink "$aside"), not a log file. A root process appends to that name, so this server may already have had root writes redirected into that target. The link has been moved to ${aside} — it is NOT deleted, so you can see where it pointed — and nothing root opens carries its name any more. Inspect the target before trusting this host.
"
      continue
    fi
    # A real file the previous owner could still rewrite. Root's install log is a record of what
    # was done to this server; leave the content, take the inode.
    if [ -e "$path" ]; then
      owner="$(stat -c '%u' "$path")"
      if [ "$owner" -ne 0 ]; then
        chown root "$path"
        chmod g-w,o-w "$path"
        MARAN_LOG_DIR_WARNINGS="${MARAN_LOG_DIR_WARNINGS}NOTE: ${path} was owned by uid ${owner}; ownership taken by root. Its contents are unchanged and may have been written by that uid.
"
      fi
    fi
  done
}

# setup_logging: make the log directory root's before anything else writes to it, then
# duplicate all stdout/stderr into the log file (via `tee`) while still showing output
# on the terminal. Uses `tee -a` so a re-run after an interrupted install appends
# rather than truncates — the log is a full history of every attempt.
#
# The `tee -a` below is the FIRST root write on this server, which is why the directory it
# writes into is hardened on the line above and not in a step file.
setup_logging() {
  harden_log_directory
  # Keep the original stdout on file descriptor 3 BEFORE redirecting. Anything a step
  # must show the operator without persisting it — the one-time setup token, which is
  # enough on its own to create the first administrator — is written to fd 3, so it
  # reaches the terminal and never the log file.
  exec 3>&1
  # exec through a process substitution so both stdout and stderr are captured,
  # timestamps are added by `ts`-less awk (no extra dependency), and the terminal
  # still sees everything live.
  exec > >(awk '{ print strftime("[%Y-%m-%d %H:%M:%S]"), $0; fflush() }' | tee -a "$MARAN_LOG_FILE") 2>&1
  # Replayed here rather than printed where they were produced, so they reach the log file too.
  if [ -n "$MARAN_LOG_DIR_WARNINGS" ]; then
    printf '%s' "$MARAN_LOG_DIR_WARNINGS" >&2
  fi
}

# detect_os: identifies distro family (debian|rhel), distro id and version from
# /etc/os-release. Exported for lib/10-preflight.sh and the package-manager adapter
# in lib/20-dependencies.sh. Unsupported values are rejected by 10-preflight.sh, not
# here — this function only detects, it never judges.
detect_os() {
  if [ ! -r /etc/os-release ]; then
    echo "install.sh: cannot read /etc/os-release; unsupported system" >&2
    exit 1
  fi
  # shellcheck disable=SC1091
  . /etc/os-release
  MARAN_OS_ID="${ID:-unknown}"
  MARAN_OS_VERSION_ID="${VERSION_ID:-unknown}"
  MARAN_OS_ID_LIKE="${ID_LIKE:-}"

  case "$MARAN_OS_ID" in
    ubuntu|debian)
      MARAN_OS_FAMILY="debian"
      ;;
    almalinux|rocky)
      MARAN_OS_FAMILY="rhel"
      ;;
    *)
      # Fall back to ID_LIKE for derivative distros that still ship compatible package
      # managers; preflight still validates the concrete (id, version) pair against the
      # supported matrix, so a permissive fallback here cannot admit an untested target.
      case "$MARAN_OS_ID_LIKE" in
        *debian*) MARAN_OS_FAMILY="debian" ;;
        *rhel*|*fedora*) MARAN_OS_FAMILY="rhel" ;;
        *) MARAN_OS_FAMILY="unknown" ;;
      esac
      ;;
  esac
  export MARAN_OS_ID MARAN_OS_VERSION_ID MARAN_OS_FAMILY
}

# detect_web_server_identity: the unix user and group nginx runs as on this family.
#
# The one place these two names are decided, for the same reason MARAN_PANEL_PORT is: two steps
# need them and they must not disagree. 60-config.sh resolves the USER to a uid and hands it to
# the panel as the only caller allowed on its listening socket; 70-services.sh renders the GROUP
# into the api unit, so that the socket's directory is traversable by nginx and by no other user
# on the machine. Written apart even though both families spell them the same word, because what
# a directory is group-owned by is a GROUP: naming the user there would be right by coincidence
# and wrong the first time a distribution changed one of them. The agent's distro adapter makes
# the same separation for the same reason.
#
# Detection only — an unsupported family is rejected by 10-preflight.sh, not here. An empty value
# reaching a step is refused there with `:?` rather than defaulted, because a defaulted web server
# group is a socket the wrong processes can open.
detect_web_server_identity() {
  case "$MARAN_OS_FAMILY" in
    debian)
      MARAN_WEB_SERVER_USER="www-data"
      MARAN_WEB_SERVER_GROUP="www-data"
      ;;
    rhel)
      MARAN_WEB_SERVER_USER="nginx"
      MARAN_WEB_SERVER_GROUP="nginx"
      ;;
    *)
      MARAN_WEB_SERVER_USER=""
      MARAN_WEB_SERVER_GROUP=""
      ;;
  esac
  export MARAN_WEB_SERVER_USER MARAN_WEB_SERVER_GROUP
}

# detect_arch: normalizes `uname -m` to Maran's two supported artifact arches.
# Anything else is rejected by preflight with an explicit message.
detect_arch() {
  case "$(uname -m)" in
    x86_64|amd64) MARAN_ARCH="x86_64" ;;
    aarch64|arm64) MARAN_ARCH="aarch64" ;;
    *) MARAN_ARCH="unsupported" ;;
  esac
  export MARAN_ARCH
}

# run_step: sources one numbered file from lib/ and calls the function it defines
# (file `NN-name.sh` defines function `step_name`). Centralizing this makes the step
# list in main() a readable table of contents and keeps sourcing/error handling in
# one place.
run_step() {
  local file="$1" fn="$2"
  echo "==> ${file}"
  # shellcheck disable=SC1090
  . "${LIB_DIR}/${file}"
  "$fn"
}

main() {
  require_root
  setup_logging
  echo "Maran installer starting: $(date -u '+%Y-%m-%dT%H:%M:%SZ')"

  detect_os
  detect_web_server_identity
  detect_arch
  echo "Detected: ${MARAN_OS_ID} ${MARAN_OS_VERSION_ID} (${MARAN_OS_FAMILY} family), arch ${MARAN_ARCH}"

  run_step 10-preflight.sh    step_preflight
  # Before anything that names the service account, and before the packages: on a host installed
  # before the rename this renames the account, its group and the PostgreSQL role in place, so
  # every step after it sees exactly the state a fresh install produces. It does nothing at all
  # on a fresh host. It stops maran-api.service, and the panel is down from here to step 70 —
  # the step says so on the terminal before it does it.
  run_step 15-identity.sh     step_identity
  run_step 20-dependencies.sh step_dependencies
  run_step 30-postgresql.sh   step_postgresql
  run_step 40-user.sh         step_user
  run_step 50-artifacts.sh    step_artifacts
  run_step 60-config.sh       step_config
  run_step 70-services.sh     step_services
  run_step 80-nginx.sh        step_nginx
  # After 80, because it validates and reloads the tree 80 has just made complete, and before
  # the feature steps, because every one of them can end in a site operation that reloads
  # nginx — and until this step has run, a reload re-opens log files inside customers' homes.
  run_step 81-site-logs.sh    step_site_logs
  # Customer-facing services, after the panel itself is standing: MariaDB for
  # customer databases (the panel's own PostgreSQL is step 30 and is untouched),
  # then the host-level pieces a chrooted SFTP login needs, then the firewall, then
  # the cron daemon that runs the scheduled jobs the panel writes.
  #
  # The firewall comes after nginx and SFTP, not before: it seeds a policy-drop
  # ruleset, and the ports it opens are the ones those steps established.
  run_step 85-mysql.sh        step_mysql
  run_step 86-sftp.sh         step_sftp
  run_step 87-firewall.sh     step_firewall
  run_step 88-cron.sh         step_cron
  # FTPS, installed and switched OFF. It comes after the firewall on purpose: the step
  # opens no port and enables no unit, so there is nothing here for the firewall to have
  # to know about — an administrator turns FTPS on later, and the ports are opened then,
  # with their consequences shown first. Its very first action is to MASK the
  # distribution's own vsftpd.service, because on the Debian family the package's postinst
  # otherwise leaves port 21 listening with local logins in the clear.
  run_step 89-ftps.sh         step_ftps
  run_step 90-finish.sh       step_finish

  echo "Maran installer finished: $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
}

main "$@"
