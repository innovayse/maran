#!/usr/bin/env bash
# Runs the installer's own database and SFTP steps inside a polygon image and
# asserts what they left behind, and holds the panel's public port to the single
# place that decides it. Executed at BUILD time by both
# docker/polygon/*.Dockerfile, so an installer that stops doing any of this
# stops both image builds and no polygon suite runs at all.
#
# Why it is here and not in each Dockerfile: the assertions are the same on both
# families (they are assertions about Maran, not about a distribution), and a
# Dockerfile RUN joins its continuation lines into one shell line, where a
# multi-branch negative test is unreadable and a stray `#` silently comments out
# the rest of the build step.
#
# What the caller must have done first, because it is distribution-specific and
# is image setup rather than assertion: installed the packages, initialised the
# data directory, started `mariadbd`, generated SSH host keys and created
# /run/sshd. This script starts nothing and installs nothing.
#
# The point of the whole file, restated because it is the reason plan 3 shipped a
# panel that could not create a site: the image RUNS THE INSTALLER'S FUNCTIONS.
# It never repeats their work. An image that performs the edit itself and then
# asserts the edit is present proves only that the image works.
set -euo pipefail

readonly INSTALLER_ROOT="/tmp/maran-installer"
readonly INSTALLER_LIB="${INSTALLER_ROOT}/lib"
readonly SSHD_CONFIG="/etc/ssh/sshd_config"

# The files this script READS or SOURCES rather than runs, and the `COPY` each one needs in
# the Dockerfile. They are listed in one place because a missing one is a failure, never a
# skip: a check that cannot see its subject passes silently, which is the way this
# repository has lost verdicts before.
readonly INSTALLER_ENTRY_POINT="${INSTALLER_ROOT}/install.sh"
readonly PANEL_VHOST="${INSTALLER_ROOT}/nginx/maran.conf"
readonly PANEL_ENV_EXAMPLE="${INSTALLER_ROOT}/panel.env.example"
readonly CONFIG_STEP="${INSTALLER_LIB}/60-config.sh"
readonly API_UNIT="${INSTALLER_ROOT}/systemd/maran-api.service"
# The tmpfiles snippet that builds the panel's socket directory, the step that renders and applies
# it, and the second unit that step installs beside the api's. The snippet is the panel's trust
# boundary written down: it is SOURCE for a check that RUNS it (see
# assert_the_panel_socket_directory_is_built_and_then_looked_at), not a file this script greps.
readonly API_TMPFILES="${INSTALLER_ROOT}/systemd/maran-api.tmpfiles.conf"
readonly AGENT_UNIT="${INSTALLER_ROOT}/systemd/maran-agent.service"
readonly SERVICES_STEP="${INSTALLER_LIB}/70-services.sh"
readonly PREFLIGHT_STEP="${INSTALLER_LIB}/10-preflight.sh"
readonly FIREWALL_STEP="${INSTALLER_LIB}/87-firewall.sh"
# The step whose validation gate is asserted below, and the step whose `panel` group and
# /var/log/maran it needs before it can run. Both are RUN, not read — see run_nginx_step.
readonly NGINX_STEP="${INSTALLER_LIB}/80-nginx.sh"
readonly USER_STEP="${INSTALLER_LIB}/40-user.sh"
# The step that prints the install's last message. RUN and not read: what it claims about the
# certificate paths and the panel's port is only observable by rendering the message and
# comparing it against the steps that decide those two facts — see
# assert_the_finish_message_tells_the_truth_about_this_install.
readonly FINISH_STEP="${INSTALLER_LIB}/90-finish.sh"

# The throwaway setup token that assertion plants in panel.env, and the two ports it renders the
# message at. The token is not a secret: it is written into a file this script creates and
# deletes, on a build host, and the only thing it is used for is to see that the step hands the
# operator something. Two ports, because one number cannot tell a derived value from a literal.
readonly FINISH_PROBE_TOKEN="polygon-throwaway-setup-token"
# The probe for assert_the_panel_vhost_can_never_log_a_query_string: the path it asks the panel
# for, the string it puts in that path's query, and the two files the panel vhost logs into. The
# secret is not a secret — it is a literal in this script, sent to 127.0.0.1 inside a build layer
# — and it stands in for the one that used to be there: the installer's one-time setup token.
#
# The path is /setup for one reason: it is the page the leak was measured on, so a reader of the
# failure message is looking at the request that mattered. Any path the SPA fallback serves would
# do; a path that 404s would not, because a 404 is written to the ERROR log with its full request
# line and the probe would then be reading nginx's error path.
readonly VHOST_LOG_PROBE_PATH="/setup"
readonly VHOST_LOG_PROBE_SECRET="polygon-throwaway-url-secret-8f3a1c"
# The two paths installer/nginx/maran.conf names. They are literals here on purpose, the same
# arrangement the directory modes follow: an assertion that reads its expectation out of the file
# it is checking agrees with that file by construction. The census below reads the directives out
# of the vhost, so a log that MOVED is caught there — by the behavioural half finding an empty
# file at these names, with the census having passed.
readonly VHOST_LOG_ACCESS_LOG="/var/log/maran/nginx-access.log"
readonly VHOST_LOG_ERROR_LOG="/var/log/maran/nginx-error.log"
readonly FINISH_PROBE_PORT_ONE="19101"
readonly FINISH_PROBE_PORT_TWO="19202"
# The step that renames a pre-rename installation's account, group and database role in place.
# RUN, not read: the assertion below drives its own rename functions against throwaway accounts
# and then looks at the uids, because "the rename preserves the uid" is the property every file
# the old account owned depends on and no grep over the step can see it.
readonly IDENTITY_STEP="${INSTALLER_LIB}/15-identity.sh"
# The two steps that create the directories today's privilege-escalation fixes moved, both
# RUN and not read: step 40 for the panel layout, the restore staging root and the bulk
# scratch, step 50 for the release staging directory. See the block above main().
readonly ARTIFACTS_STEP="${INSTALLER_LIB}/50-artifacts.sh"
# SOURCED IN A CHILD, never here: see run_uninstaller. It is in this list all the same, because
# a missing file must be a failure and not a silently skipped assertion.
readonly UNINSTALLER="${INSTALLER_ROOT}/uninstall.sh"
# Step 89 and the four things it needs to be able to run at all. The step is SOURCED and RUN:
# the assertions below call mask_packaged_vsftpd, install_vsftpd_package, ensure_ftps_group,
# ensure_ftps_directories, install_ftps_pam_service, install_ftps_unit, install_ftps_logrotate
# and report_ftps_is_installed_and_off — the step's own functions — and then look at what they
# left behind. The image therefore INSTALLS vsftpd from the step's own
# `vsftpd_packages_for_family`, so a package name that stops being right on a family stops that
# family's image build rather than a customer's install.
readonly FTPS_STEP="${INSTALLER_LIB}/89-ftps.sh"
# The package-manager adapter step 89 installs through. `install_vsftpd_package` refuses by name
# when `pkg_install` is undefined, so a missing COPY here is a named failure and not a skip.
readonly DEPENDENCIES_STEP="${INSTALLER_LIB}/20-dependencies.sh"
# The two payload files step 89 places, resolved by it against $SCRIPT_DIR. They are its
# payload and not its text: `install_ftps_unit` and `install_ftps_logrotate` abort by name when
# either is missing, which is what a forgotten COPY must look like.
readonly FTPS_UNIT_SOURCE="${INSTALLER_ROOT}/systemd/maran-ftps.service"
readonly FTPS_LOGROTATE_SOURCE="${INSTALLER_ROOT}/logrotate/maran-ftps"

# The PAM witness: a C program this script COMPILES and RUNS, and the binary it becomes. It is
# what makes the FTPS authorization observable here at all — a PAM stack is its control flow, and
# control flow is not a thing a grep over the file can see. See
# assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for.
readonly PAM_WITNESS_SOURCE="/tmp/maran-polygon/pam-witness.c"
readonly PAM_WITNESS_BINARY="/tmp/maran-polygon/pam-witness"

# The agent's own copies of the three FTPS names step 89 also spells, READ and never compiled.
# They are here because the installer and the agent are two halves of one arrangement — the
# installer creates the group and writes /etc/pam.d/<service>, the agent adds logins to that group
# and renders `pam_service_name` and every jail path under that root — and until this script read
# them there were three independent literals each asserted only against itself. Drift costs every
# FTPS login on every install, with every gate green. See
# assert_the_installer_and_the_agent_spell_the_ftps_names_the_same.
readonly AGENT_SOURCE_ROOT="/tmp/maran-agent"
readonly AGENT_DEBIAN_SERVICES="${AGENT_SOURCE_ROOT}/crates/distro/src/debian/debian_services.rs"
readonly AGENT_RHEL_SERVICES="${AGENT_SOURCE_ROOT}/crates/distro/src/rhel/rhel_services.rs"
readonly AGENT_PATHS="${AGENT_SOURCE_ROOT}/crates/agent-core/src/agent_paths.rs"
readonly AGENT_VSFTPD_TEMPLATE="${AGENT_SOURCE_ROOT}/crates/templates/templates/vsftpd/vsftpd.conf.j2"

# The fourth name the two halves both spell, and the one whose drift nothing reported: the
# transfer log path. The agent renders it into `vsftpd_log_file`; installer/logrotate/maran-ftps
# is the only thing that bounds the file it names. A drift leaves vsftpd writing a file no
# rotation policy rotates -- one line per login and per transfer, from every customer, forever,
# fastest on exactly the host where FTPS is used most -- with every gate in this repository green
# until a partition fills.
readonly AGENT_ENABLE_FTPS="${AGENT_SOURCE_ROOT}/crates/ops/src/ftps/enable_ftps.rs"

# A throwaway credential, used only to put this container's MariaDB into the
# broken states below and then take it back out. It is not a secret and it never
# leaves the build layer: the negative assertions need a server that really has a
# root password, and there is no way to assert that the installer refuses one
# without creating one.
readonly THROWAWAY_ROOT_PASSWORD="polygon-throwaway"

# The two throwaway logins the PAM witness authenticates as, and the two passwords it offers.
# Neither account outlives the assertion that creates them (both are `userdel`ed at the end of it)
# and neither password is a secret: the only way to observe that the FTPS stack refuses a
# non-member holding a CORRECT password is to give a non-member a password and know what it is.
# The member is in maran-ftps, the outsider is in no group of ours, and that difference is the
# entire subject of the assertion.
readonly FTPS_WITNESS_MEMBER="ftpswitnessin"
readonly FTPS_WITNESS_OUTSIDER="ftpswitnessout"
readonly FTPS_WITNESS_PASSWORD="polygon-throwaway-ftps"
readonly FTPS_WITNESS_WRONG_PASSWORD="polygon-throwaway-wrong"

# The service name of the deliberately broken stack this script plants as its own inverse control,
# and the file that carries it. It is the reviewer's mutant exactly: the shipped directives,
# untouched, with `auth sufficient pam_permit.so` and `account sufficient pam_permit.so` prepended.
# It exists so that the refusals above are shown to be caused by the shipped stack's content and
# not by a witness that refuses everything.
readonly FTPS_MUTANT_PAM_SERVICE="maran-ftps-polygon-mutant"

# The drop-in this script adds to prove that Include following works on THIS family's own
# sshd_config, and removes again. `zz-` so it is read last whatever else is in there.
readonly SSHD_TEST_DROP_IN="/etc/ssh/sshd_config.d/zz-maran-polygon-port.conf"

# Where docker/polygon/systemctl-stand-in.sh keeps the state of the units it has been asked
# about. Named here because the firewalld assertions below put this container into host states
# that no verb can reach — a unit that exists, a query that fails to answer, a disable that is
# refused — and then read back what the step did with them.
readonly UNIT_STATE_DIRECTORY="/run/polygon-units"

# fail: an assertion that did not hold, named, on stderr.
fail() {
  echo "assert-installer-steps.sh: $1" >&2
  exit 1
}

# require_installer_file: refuse, naming the exact Dockerfile line that is missing.
require_installer_file() {
  local path="$1" source_path="$2"
  [ -f "$path" ] && return 0
  fail "${path} is not in this image. The Dockerfile must carry it:

    COPY ${source_path} ${path}"
}

require_installer_file "$INSTALLER_ENTRY_POINT" installer/install.sh
require_installer_file "$PANEL_VHOST" installer/nginx/maran.conf
require_installer_file "$PANEL_ENV_EXAMPLE" installer/panel.env.example
require_installer_file "$CONFIG_STEP" installer/lib/60-config.sh
require_installer_file "$API_UNIT" installer/systemd/maran-api.service
require_installer_file "$API_TMPFILES" installer/systemd/maran-api.tmpfiles.conf
require_installer_file "$AGENT_UNIT" installer/systemd/maran-agent.service
require_installer_file "$SERVICES_STEP" installer/lib/70-services.sh
require_installer_file "$PREFLIGHT_STEP" installer/lib/10-preflight.sh
require_installer_file "$FIREWALL_STEP" installer/lib/87-firewall.sh
require_installer_file "$NGINX_STEP" installer/lib/80-nginx.sh
require_installer_file "$USER_STEP" installer/lib/40-user.sh
require_installer_file "$FINISH_STEP" installer/lib/90-finish.sh
require_installer_file "$IDENTITY_STEP" installer/lib/15-identity.sh
require_installer_file "$ARTIFACTS_STEP" installer/lib/50-artifacts.sh
require_installer_file "${INSTALLER_LIB}/85-mysql.sh" installer/lib/85-mysql.sh
require_installer_file "${INSTALLER_LIB}/86-sftp.sh" installer/lib/86-sftp.sh
require_installer_file "$UNINSTALLER" installer/uninstall.sh
require_installer_file "$FTPS_STEP" installer/lib/89-ftps.sh
require_installer_file "$DEPENDENCIES_STEP" installer/lib/20-dependencies.sh
require_installer_file "$FTPS_UNIT_SOURCE" installer/systemd/maran-ftps.service
require_installer_file "$FTPS_LOGROTATE_SOURCE" installer/logrotate/maran-ftps
require_installer_file "$PAM_WITNESS_SOURCE" docker/polygon/pam-witness.c
require_installer_file "$AGENT_DEBIAN_SERVICES" agent/crates/distro/src/debian/debian_services.rs
require_installer_file "$AGENT_RHEL_SERVICES" agent/crates/distro/src/rhel/rhel_services.rs
require_installer_file "$AGENT_PATHS" agent/crates/agent-core/src/agent_paths.rs
require_installer_file "$AGENT_VSFTPD_TEMPLATE" agent/crates/templates/templates/vsftpd/vsftpd.conf.j2
require_installer_file "$AGENT_ENABLE_FTPS" agent/crates/ops/src/ftps/enable_ftps.rs

# require_systemctl_stand_in: the polygon's systemctl, in place before a single assertion runs.
#
# Its own requirement rather than a line in the list above, because it is not a file this script
# reads: it is the only firewalld this image can have. `disable_firewalld` is the one part of the
# firewall step whose subject is a UNIT, and the four host states it must tell apart — no unit, a
# query that fails to answer, a working disable, a refused one — exist nowhere but in the state
# docker/polygon/systemctl-stand-in.sh records.
#
# It is checked rather than assumed because the check was already lost once. The stand-in was
# copied into the image AFTER this script had run, so the firewalld cases met the real systemctl,
# which reads unit files straight off the disk with no booted manager and answered
# `0 unit files listed.` for every one of them — the fixture that meant to say "the query broke"
# said "there is no firewalld here" instead. A missing fixture must name itself; the alternative
# is an assertion that fails, or worse passes, for a reason nobody can see from its message.
require_systemctl_stand_in() {
  local binary
  binary="$(command -v systemctl || true)"
  [ -n "$binary" ] && grep -q 'systemctl-stand-in' "$binary" && return 0
  fail "${binary:-systemctl} is not docker/polygon/systemctl-stand-in.sh. The firewalld assertions below put
this container into host states no verb can reach — a unit that exists, a query that fails to answer,
a disable that is refused — and only the stand-in records them. The Dockerfile must carry it BEFORE
the RUN that executes this script:

    COPY docker/polygon/systemctl-stand-in.sh /usr/bin/systemctl
    RUN chmod 755 /usr/bin/systemctl"
}

require_systemctl_stand_in

# shellcheck source=/dev/null
. "${INSTALLER_LIB}/85-mysql.sh"
# shellcheck source=/dev/null
. "${INSTALLER_LIB}/86-sftp.sh"
# The config step is SOURCED, not read: its two detection functions are the most
# lockout-relevant code in the installer, and this is the only place they meet a real
# sshd_config, a real sshd and a real distribution's bash. 10-preflight.sh is deliberately
# NOT sourced — it defines its own `fail`, which would replace the one above.
# shellcheck source=/dev/null
. "$CONFIG_STEP"
# The firewall step, sourced for the same reason: its include wiring is loaded by real
# `nft` below, and its port-flag splitting is the one line in it that fails silently.
# shellcheck source=/dev/null
. "$FIREWALL_STEP"
# The package-manager adapter and step 89, sourced together because the second calls the first.
# Step 89 is sourced rather than run whole: `step_ftps` ends at `systemctl daemon-reload`, which
# in this image reaches the stand-in and reloads nothing, so running the step end to end would
# report on a manager that is not here. Its parts are what the assertions drive, one at a time,
# in the order the step itself fixes — the mask before the package, which is the whole security
# property of this step.
# shellcheck source=/dev/null
. "$DEPENDENCIES_STEP"
# shellcheck source=/dev/null
. "$FTPS_STEP"

# sql: one statement, as root over the socket, with an optional password for the
# states where root has one. Double quotes around identifiers and literals so the
# statement survives the Dockerfile's single-quoted RUN string.
sql() {
  /usr/bin/mysql -u root "$@"
}


# installer_value: the raw remainder of one `KEY=` line, the way the installer reads its own
# files back — the LAST such line, because that is the one bash would have executed. Reading
# the first was a hole: a second `MARAN_PANEL_PORT=` further down moved the real value while
# this check went on agreeing with the one at the top.
installer_value() {
  local key="$1" file="$2"
  awk -v k="$key" 'index($0, k "=") == 1 { value = substr($0, length(k) + 2) } END { print value }' "$file"
}

# installer_value_count: how many times a file assigns a key at the start of a line.
installer_value_count() {
  local key="$1" file="$2"
  awk -v k="$key" 'index($0, k "=") == 1 { n++ } END { print n + 0 }' "$file"
}

# The service account's names, resolved ONCE out of install.sh — the one place that decides
# them — and never written down here. Step 40 creates the account, step 15 renames a pre-rename
# host onto it, the uninstaller removes it, and both units name it; a literal in this file would
# agree with none of them the first time one of them changed.
#
# Every one of them refuses an empty answer, and that is the vacuity guard that matters here: the
# values are read out of a shell assignment by a text match, so the way this goes wrong is that
# an assignment is renamed or reformatted and every check below then compares "" with "", passing
# loudest at the moment it stopped observing anything.
POLYGON_SERVICE_USER=""
POLYGON_SERVICE_GROUP=""
POLYGON_LEGACY_USER=""
POLYGON_LEGACY_GROUP=""
POLYGON_SERVICE_MARKER=""
resolve_service_identity() {
  local marker
  POLYGON_SERVICE_USER="$(installer_value MARAN_USER "$INSTALLER_ENTRY_POINT")"
  POLYGON_SERVICE_GROUP="$(installer_value MARAN_GROUP "$INSTALLER_ENTRY_POINT")"
  POLYGON_LEGACY_USER="$(installer_value MARAN_LEGACY_USER "$INSTALLER_ENTRY_POINT")"
  POLYGON_LEGACY_GROUP="$(installer_value MARAN_LEGACY_GROUP "$INSTALLER_ENTRY_POINT")"
  marker="$(installer_value MARAN_SERVICE_ACCOUNT_MARKER "$INSTALLER_ENTRY_POINT")"
  marker="${marker%\"}"
  POLYGON_SERVICE_MARKER="${marker#\"}"
  [ -n "$POLYGON_SERVICE_USER" ] && [ -n "$POLYGON_SERVICE_GROUP" ] \
    && [ -n "$POLYGON_LEGACY_USER" ] && [ -n "$POLYGON_LEGACY_GROUP" ] \
    && [ -n "$POLYGON_SERVICE_MARKER" ] \
    || fail "install.sh no longer assigns all five of MARAN_USER, MARAN_GROUP, MARAN_LEGACY_USER,
MARAN_LEGACY_GROUP and MARAN_SERVICE_ACCOUNT_MARKER at the start of a line (read:
'${POLYGON_SERVICE_USER}', '${POLYGON_SERVICE_GROUP}', '${POLYGON_LEGACY_USER}',
'${POLYGON_LEGACY_GROUP}', '${POLYGON_SERVICE_MARKER}'). Every assertion here that names the
account reads it from there, so an empty answer would compare nothing with nothing."
}

# service_identity_environment: the identity install.sh exports, for a child that runs a step
# file. The step files read these from the environment and abort by name when they are unset,
# which is what stops one of them expanding an empty string into `install -o` or `chown root:`.
service_identity_environment() {
  printf 'MARAN_USER=%s MARAN_GROUP=%s MARAN_LEGACY_USER=%s MARAN_LEGACY_GROUP=%s' \
    "$POLYGON_SERVICE_USER" "$POLYGON_SERVICE_GROUP" "$POLYGON_LEGACY_USER" "$POLYGON_LEGACY_GROUP"
}

# run_user_step: `$1` evaluated in a child with step 40 sourced and the identity in the
# environment, exactly the way install.sh gives it to that step.
run_user_step() {
  MARAN_USER="$POLYGON_SERVICE_USER" \
    MARAN_GROUP="$POLYGON_SERVICE_GROUP" \
    MARAN_SERVICE_ACCOUNT_MARKER="$POLYGON_SERVICE_MARKER" \
    MARAN_ADOPT_EXISTING_USER="${MARAN_ADOPT_EXISTING_USER:-}" \
    bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$USER_STEP" "$1"
}

# run_identity_step: the same, for step 15, with the two ends of the rename overridable so the
# assertion can drive it against throwaway accounts instead of against this image's real one.
run_identity_step() {
  MARAN_USER="${MARAN_IDENTITY_TO:-$POLYGON_SERVICE_USER}" \
    MARAN_GROUP="${MARAN_IDENTITY_TO:-$POLYGON_SERVICE_GROUP}" \
    MARAN_LEGACY_USER="${MARAN_IDENTITY_FROM:-$POLYGON_LEGACY_USER}" \
    MARAN_LEGACY_GROUP="${MARAN_IDENTITY_FROM:-$POLYGON_LEGACY_GROUP}" \
    MARAN_SERVICE_ACCOUNT_MARKER="$POLYGON_SERVICE_MARKER" \
    bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$IDENTITY_STEP" "$1"
}

# installer_root_trusted_log_names: the leaf names under /var/log/maran that a ROOT process
# opens for append, read out of install.sh's MARAN_ROOT_TRUSTED_LOG_NAMES rather than repeated
# here. One list, in the file that owns it: a fourth root-written log added there is checked by
# the ancestor walk without anyone remembering to extend a copy in this file.
#
# Refuses an empty answer. The list is read out of a shell assignment by a text match, so the
# way it goes wrong is that the assignment is renamed or reformatted and this returns nothing —
# which would turn every assertion driven by it into a loop over no items, passing loudest at
# the moment it stopped looking (rules/testing.md).
installer_root_trusted_log_names() {
  local names
  names="$(installer_value MARAN_ROOT_TRUSTED_LOG_NAMES "$INSTALLER_ENTRY_POINT")"
  names="${names%\"}"
  names="${names#\"}"
  [ -n "$names" ] \
    || fail "install.sh no longer assigns MARAN_ROOT_TRUSTED_LOG_NAMES at the start of a line, so the list of log files root appends to cannot be read and every check driven by it would silently check nothing."
  printf '%s\n' "$names"
}

# The identity is resolved HERE: after every reader above exists, and before the first assertion
# runs. Every name this script compares against comes out of install.sh at this line.
resolve_service_identity

# port_of_url: the port in a `scheme://host:port` value, or nothing.
port_of_url() {
  local url="$1" tail="${1##*:}"
  case "$url" in
    *:*) ;;
    *) return 1 ;;
  esac
  tail="${tail%%/*}"
  case "$tail" in
    ''|*[!0-9]*) return 1 ;;
  esac
  printf '%s' "$tail"
}

# run_installer_step: runs installer step code THE WAY install.sh runs it — as a plain command
# in a shell with `set -euo pipefail` and the step files sourced — and hands back its status.
#
# This exists because two findings in a row were the same defect wearing different clothes, and
# neither was a missing case: the test and production differed in bash's ERROR SEMANTICS.
# First an `exit` inside a process substitution, exercised here in an explicit subshell where
# `exit` is observable and in production through `< <(...)` where it is not. Then `set -e`,
# which bash SUSPENDS for a command in an `&&`/`||` list or an `if` condition — and that
# suspension reaches inside `( … )`. Measured on the identical call: `( seed_firewall_files ) ||
# fail` returned 0 with both files written, while the same function as a plain command returned
# 1 with nothing written, because a bare assignment from a command substitution is a plain
# command and errexit had killed the step before its own diagnostic could print.
#
# A CHILD PROCESS is what makes this honest. The child sets errexit itself, so no construct in
# this script can suspend it; the parent is free to use `if` and `||` to observe the result,
# which is what a test must do. Anything that changes bash's error handling is therefore on the
# test's side of the boundary, where it cannot flatter the code.
run_installer_step() {
  local snippet="$1"
  bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
. "$4"
eval "$5"' _ "${INSTALLER_LIB}/85-mysql.sh" "${INSTALLER_LIB}/86-sftp.sh" "$CONFIG_STEP" \
    "$FIREWALL_STEP" "$snippet"
}

# assert_panel_port_has_one_authority: the panel's public port is decided in exactly one
# place — `MARAN_PANEL_PORT` at the top of install.sh — and every reader derives from it.
# One reader cannot: the `listen` line of the nginx vhost, because a configuration file
# interpolates no shell variable. This assertion ties that literal back to the authority,
# and then checks that the other four readers still derive rather than repeat.
#
# It is here rather than left to review because the drift is invisible on the machine that
# matters. Change the authority alone and preflight guards a port nginx will not bind, the
# finish step prints a URL that refuses the connection, and the firewall opens a port with
# nothing behind it — while every file involved still looks internally consistent. Nothing
# fails until an operator meets it on a server they can no longer reach.
#
# The last check is R2's trap and the reason this function grew past the vhost: the panel
# port must never be the api's own listen port. Kestrel is on loopback behind nginx, and a
# firewall that opened 5080 under a default-drop policy would leave the panel reachable for
# exactly as long as nothing had dropped anything yet — then cut it off at the first rule
# change, with nobody able to log in and undo it. Two files could introduce that quietly, so
# both are read here.
assert_panel_port_has_one_authority() {
  local assignments authority
  assignments="$(installer_value_count MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")"
  [ "$assignments" -eq 1 ] \
    || fail "install.sh assigns MARAN_PANEL_PORT ${assignments} times; the whole point is that it is assigned once"

  authority="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")"
  case "$authority" in
    ''|*[!0-9]*)
      fail "install.sh no longer sets MARAN_PANEL_PORT to a plain number (read: '${authority}'),
so the one authority for the panel's public port is gone and this check cannot hold anything to it."
      ;;
  esac

  # Every listen directive, as a port: `listen 8443 ssl;` and `listen [::]:8443 ssl;` are
  # the same number written two ways, and the port is what follows the last colon.
  local listen_ports checked=0 port
  listen_ports="$(awk '$1 == "listen" { spec = $2; sub(/;$/, "", spec); n = split(spec, parts, ":"); print parts[n] }' \
    "$PANEL_VHOST")"
  if [ -z "$listen_ports" ]; then
    fail "no listen directive was found in ${PANEL_VHOST}; a check that reads nothing agrees with everything"
  fi

  while read -r port; do
    case "$port" in
      ''|*[!0-9]*)
        fail "a listen directive in ${PANEL_VHOST} does not end in a port this check can read: '${port}'"
        ;;
    esac
    [ "$port" = "$authority" ] \
      || fail "the vhost listens on ${port} while install.sh sets MARAN_PANEL_PORT=${authority}; they are one number"
    checked=$((checked + 1))
  done <<< "$listen_ports"

  # 60-config.sh must WRITE the panel port derived, never as a number of its own.
  grep -q 'Firewall__PanelPort=\${MARAN_PANEL_PORT}' "$CONFIG_STEP" \
    || fail "60-config.sh no longer writes Firewall__PanelPort from \${MARAN_PANEL_PORT}; the panel.env value has
stopped following the authority, and the firewall would open whatever number was pasted there instead."

  # Preflight must DERIVE the port it guards.
  grep -q 'MARAN_REQUIRED_PORTS="\${MARAN_PANEL_PORT' "$PREFLIGHT_STEP" \
    || fail "10-preflight.sh no longer derives MARAN_REQUIRED_PORTS from \${MARAN_PANEL_PORT}; it would refuse to
install over a port that is not the one nginx binds."

  # The documented example must agree with the authority, or an operator reading it is told
  # the wrong number about the machine in front of them.
  local documented
  documented="$(installer_value Firewall__PanelPort "$PANEL_ENV_EXAMPLE")"
  [ "$documented" = "$authority" ] \
    || fail "panel.env.example documents Firewall__PanelPort=${documented} while the authority is ${authority}"

  # R2's trap used to be checked here: that the api's own loopback port was never the same
  # number as the panel port, in panel.env.example and in what 60-config.sh generates. The api
  # no longer HAS a port — it listens on a unix socket — so that check now compares nothing, and
  # is replaced by the stronger proposition the transport change created: the api must have no
  # TCP listener for the firewall to confuse with nginx's, and the socket must be one path.
  local documented_url
  documented_url="$(installer_value ASPNETCORE_URLS "$PANEL_ENV_EXAMPLE")"
  case "$documented_url" in
    http://unix:/*) ;;
    *) fail "panel.env.example documents ASPNETCORE_URLS='${documented_url}', which is not a unix socket.
The api listening on a TCP port is the loopback trust-boundary flaw: every uid on the box can reach it, and
everything that reaches it arrives with the source address the panel trusts as its reverse proxy." ;;
  esac
  case "$documented_url" in
    *127.0.0.1*|*localhost*|*0.0.0.0*)
      fail "panel.env.example's ASPNETCORE_URLS still names a TCP address as well: '${documented_url}'.
Kestrel binds every url it is given, so one stray entry re-opens the port the socket exists to remove." ;;
  esac

  # 60-config.sh must GENERATE the same shape, from the one authority, not a literal of its own.
  grep -q 'ASPNETCORE_URLS=http://unix:\${MARAN_API_SOCKET_PATH}' "$CONFIG_STEP" \
    || fail "60-config.sh no longer writes ASPNETCORE_URLS as http://unix:\${MARAN_API_SOCKET_PATH}; the api's
listening socket has stopped following install.sh's authority, or has gone back to a TCP port."

  # One socket path, spelled once in install.sh and derived everywhere it is read.
  local socket_authority socket_dir
  socket_authority="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  case "$socket_authority" in
    /*) ;;
    *) fail "install.sh no longer sets MARAN_API_SOCKET_PATH to an absolute path (read: '${socket_authority}')" ;;
  esac
  [ "http://unix:${socket_authority}" = "$documented_url" ] \
    || fail "panel.env.example documents ASPNETCORE_URLS='${documented_url}' while install.sh sets
MARAN_API_SOCKET_PATH=${socket_authority}; an operator reading the example is told the wrong path."
  grep -q '__MARAN_API_SOCKET__' "$PANEL_VHOST" \
    || fail "the panel vhost no longer carries the __MARAN_API_SOCKET__ placeholder, so its upstream has stopped
following MARAN_API_SOCKET_PATH and can disagree with the socket the api actually binds."

  # The socket DIRECTORY — the boundary itself — used to be checked here, by grepping two lines of
  # the unit for an `ExecStartPre` chgrp and a `RuntimeDirectoryMode`. Those two greps passed while
  # the directory came out `2710 panel:panel` on both families and nginx could not open the socket
  # at all, because systemd re-applies a unit's User=/Group= to its RuntimeDirectory= on every
  # command invocation and undid the chgrp before ExecStart ran. A grep over a unit file is not
  # evidence about a directory, and a failure message that names a consequence the check cannot see
  # retires the question instead of asking it. The checks now live in
  # assert_the_panel_socket_directory_is_built_and_then_looked_at, which BUILDS the directory with
  # the installer's own code and this family's real systemd-tmpfiles and then stats it.
  socket_dir="${socket_authority%/*}"

  echo "All ${checked} listen directives, panel.env.example, 60-config.sh and 10-preflight.sh follow"
  echo "MARAN_PANEL_PORT=${authority}; the api listens on ${socket_authority} and on no TCP port at all."
  echo "The directory ${socket_dir} is checked by building it, further down, not by reading the unit."
}

# render_finish_message: runs step 90's `step_finish` from `$1` at panel port `$2` and prints
# everything it says, both streams together.
#
# fd 3 is the terminal the step writes the setup link to — install.sh opens it before it
# redirects logging, so the one-time token never reaches /var/log/maran/install.log. Here it is
# pointed at this function's own stdout, which is what makes the link readable by an assertion
# without changing the step's own choice about where it goes. Without it the step falls through
# to `> /dev/tty`, and a docker build has no controlling terminal: the step would die on a
# redirection rather than on anything it said.
#
# A CHILD, for the reason run_installer_step gives: the step sets `set -euo pipefail` itself, so
# nothing in this script's control flow can suspend it and flatter the step.
render_finish_message() {
  local step="$1" port="$2"
  MARAN_PANEL_PORT="$port" bash -c 'set -euo pipefail
exec 3>&1
. "$1"
step_finish' _ "$step" 2>&1
}

# message_names_path: whether `$1` names `$2` as a whole word.
#
# A substring match is what the first version of this did, and its own positive control caught
# it: a message naming `/etc/maran/tls/panel.crt.moved` CONTAINS `/etc/maran/tls/panel.crt`, so
# a certificate path that had grown a suffix passed a check written to notice exactly that. The
# message is therefore split on whitespace and one token must EQUAL the path — a sentence's
# trailing punctuation stripped, because the message ends sentences with paths in them.
message_names_path() {
  printf '%s\n' "$1" | tr -s ' \t\n' '\n' | sed 's/[.,;:]$//' | grep -qxF -- "$2"
}

# finish_message_embeds_the_token_in_something: prints every place in the message `$1` where the
# setup token `$2` appears as PART of a longer word, and returns zero when there is at least one.
#
# WHY IT IS A CENSUS AND NOT A SEARCH FOR `?token=`. What must never happen is that the one-time
# token — permission to become this server's administrator — reaches the operator inside a URL:
# nginx writes the request line of every request it serves into the panel's own access log, and a
# browser writes the address bar into a history database that nothing on this host can reach.
# `?token=` is only today's spelling of that mistake. `#token=` in a fragment, a token appended to
# a path segment, a `curl` line built around it, a second link added beside the first — each is
# the same defect and none matches a grep for the first one. So every occurrence of the token in
# the message is enumerated and each must stand ALONE as its own whitespace-delimited word, which
# a URL-embedded token cannot do. A future line that hands the token over some other way is
# reported by this check with the offending word quoted, rather than being invisible to it.
#
# Trailing sentence punctuation is stripped exactly as message_names_path strips it, and for the
# same reason: the handover line ends with the token and a message may put a full stop after it.
finish_message_embeds_the_token_in_something() {
  local message="$1" token="$2" word found=""
  while read -r word; do
    [ -n "$word" ] || continue
    case "$word" in
      *"$token"*) [ "$word" = "$token" ] || found="${found} '${word}'" ;;
    esac
  done < <(printf '%s\n' "$message" | tr -s ' \t\n' '\n' | sed 's/[.,;:]$//')
  [ -n "$found" ] || return 1
  echo "the setup token appears inside${found}, not on its own"
  return 0
}

# finish_message_disagrees_with_the_install: the check itself, as a predicate.
#
# It prints what is wrong and returns non-zero rather than calling `fail`, so that the
# assertion below can run it against a DELIBERATELY WRONG step and require it to refuse. A gate
# that has only ever been shown something correct is a gate nobody has watched work.
finish_message_disagrees_with_the_install() {
  local message="$1" cert="$2" key="$3" port="$4" stale_port="$5"

  case "$message" in
    *"Maran is installed"*) ;;
    *) echo "the step printed no installation message at all"; return 0 ;;
  esac
  if ! message_names_path "$message" "$cert"; then
    echo "the message does not name ${cert}, which is where step 80 puts the certificate"
    return 0
  fi
  if ! message_names_path "$message" "$key"; then
    echo "the message does not name ${key}, which is where step 80 puts the private key"
    return 0
  fi
  case "$message" in
    *":${port}/"*) ;;
    *) echo "the message names no url on port ${port}, the port this run was given"; return 0 ;;
  esac
  case "$message" in
    *":${stale_port}/"*) echo "the message still names port ${stale_port} after the port changed"; return 0 ;;
  esac

  return 1
}

# assert_the_finish_message_tells_the_truth_about_this_install: the last thing an operator
# reads must describe the machine the install actually left behind.
#
# Three sentences in step 90 are claims about other steps' decisions, and every one of them can
# become false with nobody editing the message: it tells the operator to put their own
# certificate and key at two absolute paths, and it prints two urls and a `curl` line carrying
# the panel's port. The paths are literals in step 90 and variables in step 80; the port has one
# authority in install.sh. Change either side and the install still succeeds, every other gate
# stays green, and the operator is sent to a path nothing serves or an address that refuses the
# connection — on the one screen they must reach.
#
# So this asserts the AGREEMENT and never the wording. Nothing here reads a sentence, a heading
# or an order: a message rewritten in different words passes, a message that has drifted from
# what steps 80 and install.sh do does not. Wording is what a reviewer can see; agreement is
# what nobody can.
#
# WHAT THIS CANNOT SEE: whether the paths and the port are the ones the RUNNING system uses.
# It ties step 90 to step 80's own variables and to install.sh's authority, which is where both
# facts are decided — the served vhost's `listen` line is held to that same authority by
# assert_panel_port_has_one_authority, and the certificate is written by the step whose
# variables are read here.
assert_the_finish_message_tells_the_truth_about_this_install() {
  local cert key message complaint mutant
  local panel_env="/etc/maran/panel.env"

  # The two paths as STEP 80 spells them, taken by running that step's own code rather than by
  # grepping it: `${MARAN_TLS_DIR}` is expanded, so this reads the value the step really uses.
  cert="$(run_nginx_step 'printf "%s" "$MARAN_CERT_PATH"')"
  key="$(run_nginx_step 'printf "%s" "$MARAN_KEY_PATH"')"
  # The vacuity guard, on the axis that can go blind: every check below is a substring match,
  # and an empty needle is found in every haystack. This is what stops a renamed variable in
  # step 80 turning this whole assertion into four comparisons of "" with "".
  case "$cert" in /*) ;; *) fail "step 80 no longer sets MARAN_CERT_PATH to an absolute path (read: '${cert}');
every check in this assertion would match anything." ;; esac
  case "$key" in /*) ;; *) fail "step 80 no longer sets MARAN_KEY_PATH to an absolute path (read: '${key}');
every check in this assertion would match anything." ;; esac
  [ "$cert" != "$key" ] || fail "step 80 gives the certificate and the key the same path: '${cert}'"

  # The step reads the setup token back out of panel.env. This assertion writes that exact path,
  # so it refuses rather than destroy a real installation's file — the same discipline the
  # firewall assertions above follow.
  [ -e "$panel_env" ] \
    && fail "${panel_env} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  install -d -m 0750 /etc/maran
  printf 'Setup__Token=%s\n' "$FINISH_PROBE_TOKEN" > "$panel_env"

  # Twice, at two ports, and that is the whole port check: a message carrying a literal agrees
  # with one of the two runs and contradicts the other, whichever number was pasted into it.
  message="$(render_finish_message "$FINISH_STEP" "$FINISH_PROBE_PORT_ONE")"
  if complaint="$(finish_message_disagrees_with_the_install \
    "$message" "$cert" "$key" "$FINISH_PROBE_PORT_ONE" "$FINISH_PROBE_PORT_TWO")"; then
    rm -f "$panel_env"
    fail "installer/lib/90-finish.sh: ${complaint}.
The install's last message is the operator's only instruction, and it has drifted from the steps that
decide what it describes. Full message:
${message}"
  fi

  message="$(render_finish_message "$FINISH_STEP" "$FINISH_PROBE_PORT_TWO")"
  if complaint="$(finish_message_disagrees_with_the_install \
    "$message" "$cert" "$key" "$FINISH_PROBE_PORT_TWO" "$FINISH_PROBE_PORT_ONE")"; then
    rm -f "$panel_env"
    fail "installer/lib/90-finish.sh: ${complaint}.
The message must derive the panel's port from MARAN_PANEL_PORT; a number written into it is right until
the day the port changes and wrong for every install afterwards. Full message:
${message}"
  fi

  # The token reaches the operator, and this is the inverse control for every refusal above: a
  # step that printed nothing at all would satisfy each of them by having no wrong path in it.
  case "$message" in
    *"$FINISH_PROBE_TOKEN"*) ;;
    *) rm -f "$panel_env"
       fail "step 90 printed no setup token. An install that ends without it leaves nobody able to create
the first administrator. Full message:
${message}" ;;
  esac

  # And it reaches the operator ON ITS OWN, never inside a URL. rules/security.md item 8: a secret
  # never goes in a log or a URL, and a token in a URL is a token in a log — the panel's own nginx
  # logs the request line of everything it serves. This used to be a link reading
  # `/setup?token=<token>`, and opening it wrote a LIVE administrator token into
  # /var/log/maran/nginx-access.log, which every uid in the service group can read
  # (docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md).
  if complaint="$(finish_message_embeds_the_token_in_something "$message" "$FINISH_PROBE_TOKEN")"; then
    rm -f "$panel_env"
    fail "installer/lib/90-finish.sh: ${complaint}.
The one-time setup token must be handed over as its own word, not built into a URL or any other
composite: the panel's nginx logs the request line of every request it serves, and a browser records
the address bar in a history database nothing on this host can reach. Full message:
${message}"
  fi

  # Its positive control, and it is the mutation that was actually shipped rather than an invented
  # one: put the token back into the query string of the printed link. A check that has only ever
  # been shown a correct message is a check nobody has watched work.
  mutant="/tmp/maran-finish-step-token-in-url-mutant.sh"
  sed 's#^\(  local setup_url=".*\)"$#\1?token=${token}"#' "$FINISH_STEP" > "$mutant"
  cmp -s "$mutant" "$FINISH_STEP" \
    && { rm -f "$panel_env" "$mutant"
         fail "the mutation of ${FINISH_STEP} changed nothing, so the control for the token-in-a-URL check
measures nothing. It rewrites the line assigning setup_url; find that line and re-spell the sed
above, rather than deleting this control." ; }
  message="$(render_finish_message "$mutant" "$FINISH_PROBE_PORT_ONE")"
  finish_message_embeds_the_token_in_something "$message" "$FINISH_PROBE_TOKEN" >/dev/null \
    || { rm -f "$panel_env" "$mutant"
         fail "a step 90 printing the token inside the setup URL passed the check written to refuse exactly
that, so the check cannot see the leak it exists for. Full message:
${message}"; }
  rm -f "$mutant"

  # Back to the message the shipped step prints, because the cert control below renders its own
  # mutant and the two must not be confused: the variable is reused.
  message="$(render_finish_message "$FINISH_STEP" "$FINISH_PROBE_PORT_TWO")"

  # The positive control: the same check, shown the drift it exists to catch. A copy of step 90
  # with the certificate path moved by one character must be REFUSED — otherwise the four
  # matches above are agreeing with everything and this assertion is decoration.
  mutant="/tmp/maran-finish-step-mutant.sh"
  sed "s#${cert}#${cert}.moved#g" "$FINISH_STEP" > "$mutant"
  cmp -s "$mutant" "$FINISH_STEP" \
    && { rm -f "$panel_env" "$mutant"; fail "the mutation of ${FINISH_STEP} changed nothing, so the control below measures nothing"; }
  message="$(render_finish_message "$mutant" "$FINISH_PROBE_PORT_ONE")"
  finish_message_disagrees_with_the_install \
    "$message" "$cert" "$key" "$FINISH_PROBE_PORT_ONE" "$FINISH_PROBE_PORT_TWO" >/dev/null \
    || { rm -f "$panel_env" "$mutant"
         fail "a step 90 naming ${cert}.moved instead of ${cert} passed this check, so the check cannot see
the drift it exists for. Full message:
${message}"; }

  rm -f "$panel_env" "$mutant"
  echo "The install's last message names ${cert} and ${key} as step 80 spells them and derives the panel's"
  echo "port from MARAN_PANEL_PORT; a copy of the step naming a moved certificate path was refused."
  echo "It hands the one-time setup token over as a word of its own and inside nothing — every occurrence"
  echo "of it in the message was enumerated — and a copy of the step that put it back into the setup"
  echo "link's query string was refused, which is the leak that check exists for."
}

# assert_generated_keys_are_documented: every key 60-config.sh writes into panel.env has an
# entry in panel.env.example, and the three the firewall depends on are present by name.
#
# The general half is rules/security.md §7 made mechanical — "every variable the product reads
# has an entry in an .env.example" — and it exists because of a mutation this script did not
# catch: 60-config.sh writing the OLD singular `Firewall__SshPort=` passed every check here
# while the panel bound nothing and the firewall would have opened no SSH port at all. One
# guard for one key would have closed that one mutation; binding the two files closes the
# class, which is the same question asked of every other key at once.
#
# One direction only, deliberately: panel.env.example documents keys the installer does NOT
# generate (the Acme block is edited by the operator), and requiring those to be written would
# be a false alarm rather than a finding.
assert_generated_keys_are_documented() {
  local written documented key missing="" count

  # WHAT THIS CAN SEE: `echo "KEY=..."` lines, which is how every key is written today. A key
  # written with `printf`, or through a variable holding its name, is invisible here and would
  # reach panel.env unguarded — so add keys in the same spelling as their neighbours, or teach
  # this extractor the new one. The count tripwire below catches the wholesale case, and the
  # three names checked at the end are fail-closed whatever the spelling.
  #
  # Only the body of `write_config`, which is the function that writes panel.env. The step
  # also has `write_agent_env`, which writes a DIFFERENT file (agent.env, documented in
  # installer/agent.env.example) — scanning the whole step file reported its
  # MARAN_AGENT_ALLOW_UID as an undocumented panel.env key, which is a false alarm and was
  # caught by running this script inside the image rather than by reading it.
  written="$(awk -v fn="write_config() {" -F'"' '
    index($0, fn) == 1 { inside = 1; next }
    inside && /^}/ { inside = 0 }
    inside && /^[[:space:]]*echo "[A-Za-z_][A-Za-z0-9_]*=/ { split($2, kv, "="); print kv[1] }
  ' "$CONFIG_STEP" | sort -u)"
  count="$(printf '%s\n' "$written" | grep -c . || true)"
  [ "${count:-0}" -ge 8 ] \
    || fail "only ${count} generated keys were found in ${CONFIG_STEP}; this check has stopped reading the file
it is supposed to be checking, and a check that reads nothing agrees with everything."

  documented="$(grep -oE '^[A-Za-z_][A-Za-z0-9_]*=' "$PANEL_ENV_EXAMPLE" | tr -d '=' | sort -u || true)"

  while read -r key; do
    [ -n "$key" ] || continue
    printf '%s\n' "$documented" | grep -qx "$key" || missing="${missing} ${key}"
  done <<< "$written"
  [ -z "$missing" ] \
    || fail "60-config.sh writes${missing} into panel.env, and panel.env.example documents no such key.
Either the installer is generating something nothing reads, or a key was renamed in one file and not the
other — which is how a panel comes up bound to nothing (rules/security.md, configuration is documented)."

  local expected
  for expected in Firewall__SshPorts Firewall__PanelPort Firewall__SeedWhitelistCidr; do
    printf '%s\n' "$written" | grep -qx "$expected" \
      || fail "60-config.sh no longer writes ${expected}. The firewall binds it on startup: without it the
panel opens no SSH port, or no panel port, or bans the operator who installed it."
    printf '%s\n' "$documented" | grep -qx "$expected" \
      || fail "panel.env.example no longer documents ${expected}, which the installer writes"
  done

  echo "All ${count} keys 60-config.sh generates are documented in panel.env.example."
}

# assert_firewall_renders_through_the_agent: the installer seeds its ruleset by RUNNING the
# agent, and writes no nftables syntax of its own.
#
# One template source or two is the whole question. The agent renders the same templates at
# every later apply, so a copy of that text in shell would be a second source, and the first
# divergence between them is a firewall that changes shape the moment an administrator
# touches a rule. Asserted rather than trusted because the shortcut is so easy to take: a
# heredoc in a step file looks like the simplest thing in the world.
#
# The tokens below are the load-bearing syntax of a ruleset — a policy, a hook, a port
# match, the loopback exemption. A step that hand-rolled one would need them; a step that
# renders through the agent has no use for any of them.
assert_firewall_renders_through_the_agent() {
  # CODE, not prose, and that applies to the POSITIVE checks below as much as to the negative
  # ones. The step's doc comments name every token this function looks for — both subcommands
  # at :19, the plural flag the agent refuses, the policy the seed renders — so a check reading
  # the whole file is satisfied by the comment that warns about the defect. Measured twice: the
  # negative checks were corrected for it first, and the three positive ones below still passed
  # against a copy of 87-firewall.sh with EVERY code line stripped and only its comments left.
  # A check that cannot fail for the reason it names is worse than no check, because it is
  # counted.
  #
  # Full-line comments are dropped; a token on a code line, even after a trailing `#`, still
  # counts.
  local code
  code="$(grep -v '^[[:space:]]*#' "$FIREWALL_STEP" || true)"

  # The subcommand name, ending where it ends. A plain substring match was satisfied by
  # `render-firewall-bans-typo`, which is a subcommand the agent refuses — measured, that
  # mutation walked straight through this check.
  printf '%s\n' "$code" | grep -qE 'render-firewall-bans([^A-Za-z0-9_-]|$)' \
    || fail "87-firewall.sh no longer invokes 'render-firewall-bans'; the bans table must be seeded before
any include names it, or the next boot loads no firewall at all."
  printf '%s\n' "$code" | grep -qE 'render-firewall-ruleset([^A-Za-z0-9_-]|$)' \
    || fail "87-firewall.sh no longer invokes 'render-firewall-ruleset'"

  printf '%s\n' "$code" | grep -q -- '--ssh-port' \
    || fail "87-firewall.sh no longer passes --ssh-port to the agent"

  printf '%s\n' "$code" | grep -q -- '--ssh-ports' \
    && fail "87-firewall.sh passes --ssh-ports, which the agent refuses outright: the flag is --ssh-port,
singular and repeatable, once per port sshd listens on."

  local token
  for token in 'policy drop' 'hook input' 'dport' 'iif "lo"'; do
    if printf '%s\n' "$code" | grep -qF -- "$token"; then
      fail "87-firewall.sh contains nftables ruleset syntax of its own (found: ${token}). The seed must be
rendered by the agent so that one set of templates produces both the seed and every later apply."
    fi
  done

  echo "87-firewall.sh seeds both files through the agent and writes no ruleset text of its own."
}

# assert_firewall_seeding_composes: what the STEP does with a Firewall__SshPorts value —
# which argv reaches the agent, whether the files are written, and whether a bad list stops
# the install.
#
# It asserts on the caller, and that is the correction of a test that could not fail. The
# previous version ran `ssh_port_flags` in an explicit subshell, where its `exit` was
# observable, and never ran the function that consumes it. Meanwhile the real caller read it
# through a process substitution, where the same `exit` killed only the subshell: a list of
# `22,abc,2222` produced `--ssh-port 22 --panel-port 8443`, wrote the file, and returned 0.
# The assertion passed while the production path silently closed two live SSH ports.
#
# The agent is a stand-in here — the image has no agent binary at build time — and it is the
# right stand-in for this question: what is under test is the argv the installer BUILDS, not
# the ruleset the agent renders, which is golden-tested in its own crate. It records what it
# was called with and prints a table that satisfies the step's own checks.
assert_firewall_seeding_composes() {
  local agent="/usr/local/maran/agent/maran-agent"
  local panel_env="/etc/maran/panel.env"
  local argv_log="/tmp/maran-fake-agent-argv"

  # This assertion writes files at the paths the step really uses. On a host that already has
  # them it would destroy the real thing, so it refuses rather than assuming it is in a
  # throwaway image.
  local existing
  for existing in "$agent" "$panel_env" /etc/maran/firewall.nft /etc/maran/firewall-bans.nft; do
    [ -e "$existing" ] \
      && fail "${existing} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  done

  install -d -m 0755 "$(dirname "$agent")"
  cat > "$agent" <<'FAKE'
#!/usr/bin/env bash
printf '%s\n' "$*" >> /tmp/maran-fake-agent-argv
table=maran
priority=0
policy=accept
case "$1" in
  render-firewall-bans) table=maran_bans; priority=-5 ;;
esac
[ -n "${MARAN_FAKE_AGENT_BROKEN:-}" ] && policy=drpo
printf 'table inet %s {\n' "$table"
printf '    chain input {\n'
printf '        type filter hook input priority %s; policy %s;\n' "$priority" "$policy"
printf '    }\n}\n'
FAKE
  chmod 0755 "$agent"
  install -d -m 0750 /etc/maran

  # The good case: three ports, one flag each, in order, and both files written.
  : > "$argv_log"
  printf 'Firewall__SshPorts=22,2200,2222\nFirewall__PanelPort=8443\n' > "$panel_env"
  local step_output="/tmp/maran-step-output"
  run_installer_step 'seed_firewall_files' >"$step_output" 2>&1 \
    || fail "seed_firewall_files refused an ordinary three-port host:
$(cat "$step_output")"

  local rendered_argv expected_argv
  rendered_argv="$(grep '^render-firewall-ruleset' "$argv_log" || true)"
  expected_argv="render-firewall-ruleset --ssh-port 22 --ssh-port 2200"
  expected_argv="${expected_argv} --ssh-port 2222 --panel-port 8443"
  [ "$rendered_argv" = "$expected_argv" ] \
    || fail "the step invoked the agent as:
  ${rendered_argv}
and it must have been:
  ${expected_argv}
A ruleset seeded from a shorter list opens the ports it names and drops the rest, under a policy with no
remote recovery."
  grep -q '^render-firewall-bans$' "$argv_log" \
    || fail "the step did not render the bans table; an include naming a missing file aborts the whole load"
  [ -s /etc/maran/firewall.nft ] && [ -s /etc/maran/firewall-bans.nft ] \
    || fail "the step did not write both rendered files"

  # The bad cases, asserted on the STEP: it must abort, and it must not have handed the agent
  # a truncated list on the way.
  local bad
  for bad in "22,abc,2222" "22,,2200" "22," ",22" "22,70000" ""; do
    : > "$argv_log"
    rm -f /etc/maran/firewall.nft
    printf 'Firewall__SshPorts=%s\nFirewall__PanelPort=8443\n' "$bad" > "$panel_env"
    if run_installer_step 'seed_firewall_files' >"$step_output" 2>&1; then
      fail "seed_firewall_files ACCEPTED Firewall__SshPorts='${bad}' and invoked the agent as:
  $(grep '^render-firewall-ruleset' "$argv_log" || echo '(nothing)')
Every port that list failed to name is a port the seeded policy drops, and one of them is how the
operator is connected to the machine."
    fi
    [ ! -e /etc/maran/firewall.nft ] \
      || fail "seed_firewall_files refused Firewall__SshPorts='${bad}' but wrote the ruleset anyway"
    grep -q '^render-firewall-ruleset' "$argv_log" \
      && fail "seed_firewall_files refused Firewall__SshPorts='${bad}' only AFTER invoking the agent with
  $(grep '^render-firewall-ruleset' "$argv_log")"
  done

  # And a rendered file that does not parse must not become the file a boot depends on. The
  # agent is trusted to render a ruleset, not assumed to: an unparseable file left at the live
  # path with the include already wired is nftables.service FAILED at the next boot even though
  # the install aborted.
  : > "$argv_log"
  rm -f /etc/maran/firewall.nft /etc/maran/firewall-bans.nft
  printf 'Firewall__SshPorts=22\nFirewall__PanelPort=8443\n' > "$panel_env"
  if run_installer_step 'MARAN_FAKE_AGENT_BROKEN=1 seed_firewall_files' >"$step_output" 2>&1; then
    fail "seed_firewall_files accepted a rendered ruleset that does not parse"
  fi
  # The TEXT, not only the status. This refusal and the two below have each survived a mutant
  # once by refusing for the wrong reason, which is exactly where the message earns its keep.
  grep -q 'does not parse' "$step_output" \
    || fail "seed_firewall_files refused an unparseable rendering, but not for that reason:
$(cat "$step_output")"
  [ ! -e /etc/maran/firewall-bans.nft ] && [ ! -e /etc/maran/firewall.nft ] \
    || fail "seed_firewall_files refused an unparseable rendering but left the file at the live path,
where the next boot would read it"

  rm -f "$agent" "$panel_env" "$argv_log" /etc/maran/firewall.nft /etc/maran/firewall-bans.nft
  echo "The step builds one --ssh-port per port, refuses a list or a rendering it cannot read, and stops"
  echo "before anything reaches the live path."
}

# firewalld_state: put this container's stand-in systemctl into one of the four host states the
# installer's firewalld handling has to tell apart, and only those.
#
# The state directory is wiped first, so each case starts from "this host has no firewalld" and
# adds exactly what its name says. `firewalld` and `firewalld.service` are written BOTH ways on
# purpose: a real systemd treats them as one unit, the stand-in keeps one state file per literal
# name (its own header says so), and `disable_firewalld` asks `list-unit-files` with the suffix
# and `is-enabled`/`disable` without it. Writing one spelling would make a case pass because the
# fixture was half-applied rather than because the code was right.
firewalld_state() {
  local wanted="$1"
  rm -rf "$UNIT_STATE_DIRECTORY"
  mkdir -p "$UNIT_STATE_DIRECTORY"
  case "$wanted" in
    absent) ;;
    query-broken)
      printf 'Connection reset by peer\n' > "${UNIT_STATE_DIRECTORY}/firewalld.service.query-broken"
      ;;
    present | disable-refused)
      : > "${UNIT_STATE_DIRECTORY}/firewalld.service.installed"
      printf 'enabled\n' > "${UNIT_STATE_DIRECTORY}/firewalld.service.enabled"
      printf 'enabled\n' > "${UNIT_STATE_DIRECTORY}/firewalld.enabled"
      printf 'active\n' > "${UNIT_STATE_DIRECTORY}/firewalld"
      if [ "$wanted" = disable-refused ]; then
        printf 'Unit firewalld.service is masked.\n' > "${UNIT_STATE_DIRECTORY}/firewalld.refuse-disable"
      fi
      ;;
    *) fail "firewalld_state: '${wanted}' is not one of the four states this assertion models" ;;
  esac
}

# firewalld_disable_was_attempted: 0 when the stand-in was actually asked to disable firewalld.
#
# Asked of the STATE the verb writes, not of a log: `disable` records the unit's enablement, and
# a unit nothing disabled has no such file. That is what separates "the step decided there was
# nothing to disable" from "the step tried and the host refused" — two outcomes an earlier
# version printed the same sentence for.
firewalld_disable_was_attempted() {
  [ -e "${UNIT_STATE_DIRECTORY}/firewalld.enabled" ]
}

# assert_firewalld_handling_tells_its_three_answers_apart: `disable_firewalld` against all four
# host states, because until this existed it was exercised by NOTHING.
#
# Neither polygon image installs firewalld and the stand-in had no `list-unit-files` arm at all,
# so every build took its catch-all `*) exit 0` — an empty answer, which the step read as "no
# firewalld here". Every other path through the function, including both of the ones that leave
# a host with firewalld still in charge of the ruleset, was unreachable from this repository.
# That is why a firewalld failure reported from a real host could not be reproduced: there was
# no coverage of it to reproduce it with.
#
# The two states that matter are the middle two, and they are the ones a mutation discriminates:
# restoring the historical `2>/dev/null || true` on the query makes the BROKEN case say "No
# firewalld unit on this host" at rc 0, and restoring `|| true` on the disable makes the REFUSED
# case finish at rc 0 with firewalld still enabled. Measured, both. The ABSENT and PRESENT cases
# behave identically under that mutation, which is exactly why an assertion resting on them
# alone would have been a green light for the defect.
assert_firewalld_handling_tells_its_three_answers_apart() {
  local output status=0

  # 1. No firewalld, and systemctl said so. The one answer that may proceed quietly — and it
  #    must proceed without touching a unit it has just been told is not there.
  firewalld_state absent
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "disable_firewalld failed on a host that simply has no firewalld (exit ${status}):
${output}"
  case "$output" in
    *"No firewalld unit on this host"*) ;;
    *) fail "disable_firewalld said nothing about a host with no firewalld. Every path through it must
reach the install log, or an operator whose firewall rules stopped applying cannot find out why:
${output}" ;;
  esac
  if firewalld_disable_was_attempted; then
    fail "disable_firewalld reported no firewalld unit and then disabled one anyway"
  fi

  # 2. The query BROKE. Not the same answer as 'this host has no firewalld', and the difference
  #    is a host that finishes the install with firewalld still rewriting the ruleset under a
  #    panel that reports its own rules as live.
  status=0
  firewalld_state query-broken
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  case "$output" in
    *"No firewalld unit on this host"*)
      fail "disable_firewalld reported 'No firewalld unit on this host' for a query that FAILED. That is a
statement of fact the code did not establish, and the diagnosis that contradicts it was discarded:
${output}" ;;
  esac
  case "$output" in
    *"could not be answered here"*) ;;
    *) fail "disable_firewalld did not report that the firewalld query failed:
${output}" ;;
  esac
  case "$output" in
    *"Connection reset by peer"*) ;;
    *) fail "disable_firewalld reported a failed query without saying what systemctl said. The diagnosis is
the only thing that tells an operator whether this host has firewalld:
${output}" ;;
  esac
  firewalld_disable_was_attempted \
    || fail "disable_firewalld could not find out whether firewalld is here and then skipped the disable.
'I could not find out' is a reason to act and check, not a reason to do nothing."

  # 3. firewalld is here and the disable works: announced, and gone.
  status=0
  firewalld_state present
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "disable_firewalld failed against a firewalld it disabled successfully (exit ${status}):
${output}"
  case "$output" in
    *"Disabling firewalld"*) ;;
    *) fail "disable_firewalld disabled firewalld without saying so. An operator whose firewalld rules just
stopped applying must be able to find out why from the install log:
${output}" ;;
  esac
  [ "$(systemctl is-enabled firewalld 2>/dev/null || true)" = disabled ] \
    || fail "disable_firewalld returned success but firewalld is still enabled"

  # 4. firewalld is here and the disable is REFUSED. The install must stop: firewalld flushes
  #    and rewrites the ruleset on its own reloads, so finishing here means shipping a host
  #    whose panel reports rules that something else is about to erase.
  status=0
  firewalld_state disable-refused
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  [ "$status" -ne 0 ] \
    || fail "disable_firewalld ACCEPTED a host whose 'systemctl disable --now firewalld' was refused, and
firewalld is still enabled and active. The install would finish reporting 'Firewall active' while
firewalld erases the panel's table at its next reload:
${output}"
  case "$output" in
    *"firewalld is still there"*) ;;
    *) fail "disable_firewalld refused the host whose disable failed, but not for that reason:
${output}" ;;
  esac
  case "$output" in
    *"Unit firewalld.service is masked."*) ;;
    *) fail "disable_firewalld refused without repeating what systemctl said, so the operator cannot see
why the disable failed:
${output}" ;;
  esac

  rm -rf "$UNIT_STATE_DIRECTORY"
  echo "disable_firewalld tells an absent firewalld, an unanswerable query, a working disable and a"
  echo "refused one apart, and says which of the four it met."
}

# assert_firewall_keeps_firewalld_until_its_own_table_is_loaded: the ORDER of step_firewall,
# asserted by the state an aborted install leaves the host in.
#
# The step used to disable and stop firewalld before a single ruleset byte existed, and every
# line after that can still abort: a render that fails or prints nothing or prints a non-table,
# a rendering nft rejects, an unusable Firewall__SshPorts, a damaged marker pair in the include
# target, a candidate that does not parse, a failing `systemctl enable --now`, the closing kernel
# gate. Any one of them left a RHEL host with its working firewall stopped, nothing in its
# place, and the log's last word on the subject a present-tense promise that Maran was now
# managing the firewall.
#
# So the assertion is about the aborted run, not the successful one: this container cannot reach
# the end of the step (the closing gate asks the kernel for `table inet maran` and nothing loads
# it here), and the aborted run is the case that matters anyway. The agent is a stand-in that
# exits non-zero — the shape of a real half-unpacked or wrong-architecture binary — so the abort
# happens at the first render, which is the earliest point after which the old order had already
# taken the firewall away.
#
# Mutation-confirmed: with `disable_firewalld` moved back in front of `seed_firewall_files`, this
# same run leaves `is-enabled` at `disabled` and `is-active` at `inactive`.
assert_firewall_keeps_firewalld_until_its_own_table_is_loaded() {
  local agent="/usr/local/maran/agent/maran-agent"
  local panel_env="/etc/maran/panel.env"
  local existing
  for existing in "$agent" "$panel_env" /etc/maran/firewall.nft /etc/maran/firewall-bans.nft; do
    [ -e "$existing" ] \
      && fail "${existing} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  done

  install -d -m 0755 "$(dirname "$agent")"
  printf '#!/bin/sh\nexit 9\n' > "$agent"
  chmod 0755 "$agent"
  install -d -m 0750 /etc/maran
  printf 'Firewall__SshPorts=22\nFirewall__PanelPort=8443\n' > "$panel_env"
  firewalld_state present

  local output status=0
  # `pkg_install` lives in 20-dependencies.sh, which this image does not carry and must not run:
  # the packages are already installed and an assertion has no business invoking a package
  # manager. Everything else in the step is the real thing.
  output="$(run_installer_step 'pkg_install() { :; }
step_firewall' 2>&1)" || status=$?

  local enabled active
  enabled="$(systemctl is-enabled firewalld 2>/dev/null || true)"
  active="$(systemctl is-active firewalld 2>/dev/null || true)"
  rm -f "$agent" "$panel_env"
  rm -rf "$UNIT_STATE_DIRECTORY"

  [ "$status" -ne 0 ] \
    || fail "step_firewall reported success with an agent binary that exits 9. Nothing was rendered, so
there is no ruleset for the include block to name:
${output}"
  case "$output" in
    *"was not touched"*) ;;
    *) fail "step_firewall aborted, but not at the render it was made to abort at. This assertion is then
measuring the wrong moment:
${output}" ;;
  esac
  [ "$enabled" = enabled ] && [ "$active" = active ] \
    || fail "step_firewall aborted and left firewalld disabled (is-enabled=${enabled}, is-active=${active}).
The host's working firewall is gone, nothing replaced it, and the install failed — which is strictly
worse than the two firewalls overlapping for the length of three lines. firewalld must be taken away
only after the kernel has confirmed the table that replaces it:
${output}"

  echo "An aborted step_firewall leaves firewalld exactly as it found it: enabled, active, and in charge."
}

# assert_firewall_marker_records_only_our_own_enabling: the marker that decides whether the
# UNINSTALLER may take a firewall away, on both of its branches.
#
# Both branches, because the interesting half is the one that must NOT happen: a host whose
# firewall was enabled before Maran arrived must keep it after Maran leaves. Neither branch had
# any coverage — the polygon's systemctl stand-in answers `is-enabled` from its catch-all, so
# every run took the same path and nothing would have noticed the other one break.
assert_firewall_marker_records_only_our_own_enabling() {
  local marker="/etc/maran/firewall-service-enabled-by-maran"
  [ -e "$marker" ] \
    && fail "${marker} already exists; this assertion creates and deletes exactly that path"
  install -d -m 0750 /etc/maran

  # A unit that is NOT enabled: we are about to enable it, so the uninstaller may disable it.
  run_installer_step 'systemctl() { [ "$1" = "is-enabled" ] && return 1; return 0; }
record_firewall_service_enablement' \
    || fail "record_firewall_service_enablement failed for a service that was not enabled"
  [ -f "$marker" ] \
    || fail "no marker was written for a firewall service this installer had to enable. The uninstaller
would then leave nftables enabled on a host that had no firewall before Maran."
  rm -f "$marker"

  # A unit that is ALREADY enabled: not ours, and the uninstaller must not take it away.
  run_installer_step 'systemctl() { return 0; }
record_firewall_service_enablement' \
    || fail "record_firewall_service_enablement failed for a service that was already enabled"
  if [ ! -e "$marker" ]; then
    echo "The service marker is written when this installer enables the firewall, and not when it did not."
    return 0
  fi

  rm -f "$marker"
  fail "a marker was written for a service that was ALREADY enabled. The uninstaller would then disable a
firewall this installer never turned on."
}

# assert_firewall_include_wiring: the include block, wired by the installer's own function,
# with real files behind the includes and real nft reading them.
#
# Written this way because of what this repository keeps learning: a check that greps for an
# include line proves the line is spelled right and nothing else. So the payload files exist
# at the paths the block names, nft is made to resolve them, and the failure the whole step
# is ordered around — an include whose target is missing — is produced on purpose and the
# installer's own guard is required to refuse it.
#
# What it deliberately does NOT do is load the ruleset into the kernel. This script runs at
# image BUILD time, where there is no CAP_NET_ADMIN: measured, `nft -c -f` on a perfectly
# valid file fails there with "cache initialization failed", while a missing include still
# fails with "File not found" because the parser reaches it first. So the include RESOLUTION
# is checkable here and the kernel load is not; the load is the agent's own privileged
# polygon suite's job, and a skip disguised as a pass would be worse than either.
#
# The payloads are written here rather than rendered by the agent — the image has no agent
# binary at build time — so what this proves is the WIRING: one block after two runs, bans
# included before rules, every include resolving, and a missing one refused.
assert_firewall_include_wiring() {
  local scratch target wire_output existing
  # It writes and deletes /etc/maran/firewall*.nft, so like its sibling it refuses to run on a
  # host that already has them rather than destroying a real installation's files.
  for existing in /etc/maran/firewall.nft /etc/maran/firewall-bans.nft; do
    [ -e "$existing" ] \
      && fail "${existing} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  done
  wire_output="/tmp/maran-wire-output"
  scratch="$(mktemp -d)"
  target="${scratch}/nftables.conf"
  printf '#!/usr/sbin/nft -f\nflush ruleset\n' > "$target"

  # At the paths the block names, which are the step's own constants — not rewritten copies,
  # so the assertion exercises the real ones.
  install -d -m 0750 /etc/maran
  cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
  cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD

  run_installer_step "wire_firewall_includes \"$target\"" >/dev/null \
    || fail "wire_firewall_includes refused an ordinary target with both files present"
  run_installer_step "wire_firewall_includes \"$target\"" >/dev/null \
    || fail "wire_firewall_includes refused on a second run over its own block"

  local blocks
  blocks="$(grep -c '^# BEGIN Maran firewall' "$target" || true)"
  [ "$blocks" -eq 1 ] \
    || fail "the include target carries ${blocks} Maran blocks after two runs, not 1"

  local bans_line rules_line
  bans_line="$(grep -n 'firewall-bans.nft' "$target" | cut -d: -f1)"
  rules_line="$(grep -n '"/etc/maran/firewall.nft"' "$target" | cut -d: -f1)"
  [ -n "$bans_line" ] && [ -n "$rules_line" ] \
    || fail "the include block does not name both rendered files"
  [ "$bans_line" -lt "$rules_line" ] \
    || fail "the bans file is included AFTER the rules file. File order is load order, and the rules table's
chain hooks at a priority that assumes the bans table already exists."

  # Every include resolves: nft reads the target, follows both includes, and gets as far as
  # the kernel — which is where the missing capability, and nothing about the files, stops it.
  local output
  output="$(/usr/sbin/nft -c -f "$target" 2>&1 || true)"
  case "$output" in
    *"File not found"*)
      fail "nft could not resolve an include in the target the installer wired: ${output}"
      ;;
  esac

  # And the failure the ordering exists to prevent, produced rather than described: with one
  # rendered file missing, the whole load aborts — which is why 87-firewall.sh seeds both
  # files before either is included, and why its own guard must refuse such a target.
  rm -f /etc/maran/firewall-bans.nft
  output="$(/usr/sbin/nft -c -f "$target" 2>&1 || true)"
  case "$output" in
    *"File not found"*) ;;
    *) fail "nft accepted an include target whose file is missing (${output}). The ordering in 87-firewall.sh
is built on the opposite being true, so either nft changed or this check is looking at the wrong thing." ;;
  esac

  # Every half-seeded shape, because one of them is not enough and that was measured rather
  # than assumed. A target whose FIRST include is missing makes nft stop at the parser and
  # print only "File not found"; a target whose first include RESOLVES and declares a table
  # makes it print the missing file AND the capability error together — but only where the
  # target does not open with `flush ruleset`, which is exactly the shape RHEL ships and
  # Debian does not. So both orders are tried against both families' shapes: four cases, and
  # the one that matters is the one where a check looking for the capability error first
  # would wave a half-seeded host through.
  # An unmatched marker pair, with an operator's own rules below it. A `sed '/BEGIN/,/END/d'`
  # deletes from the opening marker to the END OF FILE when the closing one is gone — measured,
  # that removed a hand-written `table inet mine` from a real target. The step must refuse and
  # leave every byte of it alone: an installer that eats rules it never wrote has done more
  # damage than one that fails.
  # Both payloads back in place first, so the ONLY thing wrong with the target below is the
  # marker pair. Without this the refusal came from the missing include instead and the
  # assertion passed against a version with no marker checking at all — measured.
  cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
  cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD

  local damaged="${scratch}/damaged.conf" operator_table
  operator_table='table inet mine {
    chain input {
        type filter hook input priority 10; policy accept;
    }
}'
  {
    printf '# a target with an operator table below a half-deleted Maran block\n'
    printf '%s\n' "$MARAN_FIREWALL_BEGIN_MARKER"
    printf 'include "/etc/maran/firewall-bans.nft"\n'
    printf '%s\n' "$operator_table"
  } > "$damaged"
  local before
  before="$(cat "$damaged")"
  if run_installer_step "wire_firewall_includes \"$damaged\"" >"$wire_output" 2>&1; then
    fail "wire_firewall_includes accepted a target whose Maran markers are not a matched pair. Deleting from
the opening marker to the end of the file takes an operator's own rules with it."
  fi
  grep -q 'not a matched pair' "$wire_output" \
    || fail "wire_firewall_includes refused the damaged target, but not for the marker reason:
$(cat "$wire_output")"
  [ "$(cat "$damaged")" = "$before" ] \
    || fail "wire_firewall_includes MODIFIED a target whose markers are not a matched pair:
$(diff <(printf '%s\n' "$before") "$damaged" || true)"
  grep -q '^table inet mine {' "$damaged" \
    || fail "the operator's own table was destroyed by the marker handling"

  # A payload that does not parse, behind an include, on the target shape where nft ALSO
  # complains about the kernel. This is the case a substring match got wrong: it classified
  # "cannot check here" and moved a broken ruleset into the live target, and the boot after
  # that has no firewall at all.
  cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
  cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy drpo;
    }
}
PAYLOAD
  local broken_target="${scratch}/broken.conf"
  printf '# a target that ships nothing but comments\n' > "$broken_target"
  if run_installer_step "wire_firewall_includes \"$broken_target\"" >"$wire_output" 2>&1; then
    fail "wire_firewall_includes accepted a target whose included ruleset does not parse. On this shape nft
reports the syntax error and a capability error together, and treating the pair as 'cannot check here'
puts a file the next boot cannot load into the live path."
  fi
  grep -q 'does not parse' "$wire_output" \
    || fail "wire_firewall_includes refused the broken ruleset, but not for that reason:
$(cat "$wire_output")"
  grep -q '^# BEGIN Maran firewall' "$broken_target" \
    && fail "wire_firewall_includes refused the broken ruleset but wired the target anyway"

  local fresh="${scratch}/fresh.conf" head missing
  for head in "flush" "comments"; do
    for missing in "bans" "rules"; do
      rm -f /etc/maran/firewall-bans.nft /etc/maran/firewall.nft
      if [ "$missing" != "bans" ]; then
        cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
      fi
      if [ "$missing" != "rules" ]; then
        cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD
      fi

      if [ "$head" = "flush" ]; then
        printf '#!/usr/sbin/nft -f\nflush ruleset\n' > "$fresh"
      else
        printf '# a target that ships nothing but comments\n' > "$fresh"
      fi

      if run_installer_step "wire_firewall_includes \"$fresh\"" >"$wire_output" 2>&1; then
        fail "wire_firewall_includes accepted a target whose ${missing} file does not exist, against a
${head}-shaped include target. A half-seeded host wired that way boots with nftables.service FAILED and no
firewall at all — which is the failure the step's seed-both-first ordering exists to prevent."
      fi
      grep -q 'names a file that does not exist' "$wire_output" \
        || fail "wire_firewall_includes refused the ${missing}-missing ${head}-shaped target, but not because
of the missing file:
$(cat "$wire_output")"
    done
  done

  rm -f /etc/maran/firewall.nft /etc/maran/firewall-bans.nft
  rm -f "$wire_output"
  rm -rf "$scratch"
  echo "The include block is wired once, bans before rules, every include resolves, and a missing one is refused."
}

# run_uninstaller: runs uninstaller code the way the uninstaller runs it — in a shell with
# `set -euo pipefail` and installer/uninstall.sh sourced — and hands back its status.
#
# A CHILD, for a reason beyond the errexit one run_installer_step gives: uninstall.sh defines
# its own `main`, and sourcing it into THIS script would replace the `main` at the bottom of
# this file. The build step would then run an uninstall instead of a suite. uninstall.sh runs
# its `main` only when it is EXECUTED and not when it is sourced, which is what makes sourcing
# it in a child safe and what makes this assertion possible at all.
run_uninstaller() {
  local snippet="$1"
  bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$UNINSTALLER" "$snippet"
}

# firewall_host_state: describe what an uninstall left behind, in one line, for a failure to
# quote back. Deliberately reads the host rather than remembering what was set up.
firewall_host_state() {
  local target="$1"
  printf 'wired=%s ruleset=%s bans=%s' \
    "$(grep -qE '^[[:space:]]*include[[:space:]]+"?/etc/maran/' "$target" 2>/dev/null \
        && echo yes || echo no)" \
    "$([ -e /etc/maran/firewall.nft ] && echo present || echo gone)" \
    "$([ -e /etc/maran/firewall-bans.nft ] && echo present || echo gone)"
}

# nft_reports_a_missing_include: 0 when real nft, reading this file, cannot find something it
# includes — the witness state this whole assertion exists to make unreachable.
#
# `File not found` and not a status, because a status is unusable here: this script runs at
# image BUILD time with no CAP_NET_ADMIN, so `nft -c -f` fails on a perfectly good file with a
# capability error. The parser reaches an include before the kernel is asked, so the missing
# include is reported at this privilege level and the load's other failure is not confused
# with it.
nft_reports_a_missing_include() {
  case "$(/usr/sbin/nft -c -f "$1" 2>&1 || true)" in
    *"File not found"*) return 0 ;;
  esac
  return 1
}

# nft_residue: everything real nft complains about in <file> EXCEPT this container's missing
# CAP_NET_ADMIN, one complaint per line. Empty means the next boot would load this file.
#
# The same filter remove_firewall applies to its own candidate, deliberately: the assertion then
# judges the host by the standard the uninstaller judged it by, so neither can be right about a
# file the other calls broken. A missing include is caught more precisely by the function above;
# this is the general form, and it is what notices the removal that leaves an operator's table
# unterminated — a state with no missing include in it at all.
nft_residue() {
  /usr/sbin/nft -c -f "$1" 2>&1 | grep 'Error:' | grep -v 'Operation not permitted' || true
}

# assert_uninstaller_never_leaves_a_dangling_include: the uninstaller's firewall half, driven
# against real files, a real include target and real nft — because nothing in this repository
# drove it at all.
#
# Nothing: no image ran it, no harness sourced it, and its hand-copied marker state machine
# could be replaced by the `sed '/BEGIN/,/END/d'` that once destroyed an operator's own
# `table inet mine` while `maran structure`, `bash -n` and both image builds stayed green. The
# installer's copy of that state machine is mutation-proved by assert_firewall_include_wiring;
# this is the other copy.
#
# What it asserts is ONE property, in six host states: an uninstall never leaves this machine
# with an include naming a file it deleted. That state is not "no Maran firewall" — `nft -f` on
# a missing include is `Error: File not found`, rc 1, and the ENTIRE load aborts, so the
# operator's own tables in the same file do not load either. nftables.service is FAILED at the
# next boot and the host has no firewall whatsoever.
#
# The six states are the six ways this script has actually reached it, each one reproduced
# with the real uninstaller before it was fixed:
#
#   1. the ordinary wired host          — the positive control, and it must really DELETE:
#                                         without it, an uninstaller that kept everything would
#                                         pass all five cases below.
#   2. markers that are not a pair      — the unwiring refuses, so the files are still included.
#   3. include lines with no markers     — an operator who followed 87-firewall.sh's own advice
#                                         to "remove both markers and everything between them".
#   4. an include with a trailing comment — nft reads the path between the quotes; a reader that
#                                         stripped one quote off each END of the line derived a
#                                         path matching nothing, and deleted the file.
#   5. an include reached only through another file — nft follows includes, so the question
#                                         "does anything still include this" must follow them too.
#   6. a removal nft would reject       — the OTHER refusal in remove_firewall, and the one
#                                         nothing here reached. remove_firewall carries two
#                                         hand-copied duplicates of 87-firewall.sh: the marker
#                                         state machine, which case 2 and m5 cover, and the
#                                         `nft -c -f` residue check on the candidate. With that
#                                         second one deleted whole, this suite finished at rc 0
#                                         — measured. A branch that guards an operator's file
#                                         and cannot be observed to guard it is not guarded.
#
# Both deleting functions run, in the order main() runs them, because the defect this replaces
# lived in the gap between them: remove_firewall decided to keep the files and said so, and
# remove_config_and_state deleted the whole directory four calls later at exit status 0.
assert_uninstaller_never_leaves_a_dangling_include() {
  local target backup_config backup_target extra_directory
  target="$(nftables_include_target)"
  extra_directory="/etc/maran-polygon-operator"

  # This drives the REAL deleters at the REAL paths, so it saves what it is about to destroy
  # and puts it back — including on the way out of a failure, the way the sshd assertion does.
  backup_config="$(mktemp -d)"
  cp -a /etc/maran "${backup_config}/maran"
  backup_target="$(mktemp)"
  if [ -e "$target" ]; then
    cp -a "$target" "$backup_target"
  else
    rm -f "$backup_target"
  fi

  local outcome=""
  local case_name state residue
  for case_name in wired damaged-markers no-markers trailing-comment indirect nft-rejects; do
    rm -rf /etc/maran "$extra_directory"
    install -d -m 0750 /etc/maran
    cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
    cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD
    # panel.env holds the encryption key and must go in every one of these cases; it is here so
    # that "kept the directory" can never be confused with "kept everything in it".
    printf 'Firewall__SshPorts=22\n' > /etc/maran/panel.env

    printf '#!/usr/sbin/nft -f\nflush ruleset\n' > "$target"
    case "$case_name" in
      wired | damaged-markers)
        {
          printf '%s\n' "$MARAN_FIREWALL_BEGIN_MARKER"
          printf 'include "/etc/maran/firewall-bans.nft"\n'
          printf 'include "/etc/maran/firewall.nft"\n'
          [ "$case_name" = wired ] && printf '%s\n' "$MARAN_FIREWALL_END_MARKER"
          # An operator's own table, BELOW the block. On the damaged host it is what a
          # `sed '/BEGIN/,/END/d'` takes with it: a range whose end marker is missing deletes
          # to the END OF FILE, and that is not a hypothetical — it removed a real
          # `table inet mine` from a real target. The installer's copy of the state machine
          # that replaced it is mutation-proved by assert_firewall_include_wiring; this line
          # is what proves the uninstaller's copy, which nothing exercised at all.
          printf 'table inet mine {\n    chain input {\n'
          printf '        type filter hook input priority 10; policy accept;\n    }\n}\n'
        } >> "$target"
        ;;
      no-markers)
        printf 'include "/etc/maran/firewall-bans.nft"\ninclude "/etc/maran/firewall.nft"\n' >> "$target"
        ;;
      trailing-comment)
        printf 'include "/etc/maran/firewall-bans.nft"\n' >> "$target"
        printf 'include "/etc/maran/firewall.nft" # the panel rules\n' >> "$target"
        ;;
      indirect)
        install -d -m 0755 "$extra_directory"
        printf 'include "/etc/maran/firewall-bans.nft"\ninclude "/etc/maran/firewall.nft"\n' \
          > "${extra_directory}/maran.nft"
        printf 'include "%s/maran.nft"\n' "$extra_directory" >> "$target"
        ;;
      nft-rejects)
        # A MATCHED marker pair, so the state machine accepts and hands on a candidate — and
        # an opening marker the operator pasted INSIDE their own table, so the block the
        # markers delimit carries that table's two closing braces away with it. The file is
        # valid nft now and the candidate is not, which is the single thing the `nft -c -f`
        # on the candidate exists to notice.
        #
        # A syntax error rather than a rule the kernel refuses, because this script runs at
        # image build time with no CAP_NET_ADMIN: every rule here fails with `Operation not
        # permitted`, which remove_firewall filters out precisely so that an uninstall inside
        # a container still gets a clean removal. The parser runs before the kernel is asked,
        # so a syntax error is the one complaint that survives that filter and is therefore
        # the only way to reach this branch from here — measured both ways.
        {
          printf 'table inet mine {\n    chain input {\n'
          printf '        type filter hook input priority 10; policy accept;\n'
          printf '%s\n' "$MARAN_FIREWALL_BEGIN_MARKER"
          printf '    }\n}\n'
          printf 'include "/etc/maran/firewall-bans.nft"\n'
          printf 'include "/etc/maran/firewall.nft"\n'
          printf '%s\n' "$MARAN_FIREWALL_END_MARKER"
        } >> "$target"
        ;;
    esac

    local output status=0
    output="$(run_uninstaller 'remove_firewall
remove_maran_config_directory' 2>&1)" || status=$?
    state="$(firewall_host_state "$target")"

    if [ "$status" -ne 0 ]; then
      outcome="the uninstaller failed on the '${case_name}' host (exit ${status}):
${output}"
      break
    fi
    if [ -e /etc/maran/panel.env ]; then
      outcome="the uninstaller left /etc/maran/panel.env behind on the '${case_name}' host. It holds the
encryption key for every secret in the panel's database and must never survive on a host the panel has
been removed from."
      break
    fi

    # The property that covers all six, ahead of any case's own: whatever this uninstall decided,
    # the file the next boot reads must still load. Checked for every host and not only for the
    # ones that keep something, because the ordinary wired host is rewritten too — and a rewrite
    # that produces a target nft rejects costs the operator every rule in it, ours and theirs.
    #
    # Two messages off one condition, because the two ways to fail it want different words. A
    # missing include is the deletion defect and names the file that went; anything else is a
    # target this script broke by rewriting it, which has no missing file in it at all. Deciding
    # between them AFTER the residue is in hand keeps one gate over all six cases and still puts
    # the specific diagnosis in front of the reader.
    residue="$(nft_residue "$target")"
    if [ -n "$residue" ]; then
      if nft_reports_a_missing_include "$target"; then
        outcome="the uninstaller left the '${case_name}' host with an include naming a file it deleted
(${state}). nft answers:
${residue}

The next boot loads NOTHING from ${target} — not our tables and not the operator's — so
nftables.service is FAILED and the host has no firewall at all. The uninstaller said:
${output}"
      else
        outcome="the uninstaller left the '${case_name}' host with a ${target} that nft rejects:
${residue}

Nothing is missing from this host — the file itself no longer parses, so the uninstaller broke it by
rewriting it. The next boot loads nothing from it, ours or the operator's. The uninstaller said:
${output}"
      fi
      break
    fi

    # The two hosts whose target the uninstaller must not rewrite at all, for two different
    # reasons — a marker pair it cannot trust, and a removal nft would reject. Both are checked
    # by the same evidence: the operator's own table, which only an uninstaller that rewrote the
    # file anyway can lose.
    case "$case_name" in
      damaged-markers | nft-rejects)
        if ! grep -q '^table inet mine {' "$target"; then
          outcome="the uninstaller destroyed an operator's own 'table inet mine' on the '${case_name}' host.
Deleting from the opening marker to the end of the file, or replacing the target with a candidate nft
rejects, takes rules this installer never wrote; an uninstaller that eats them has done more damage than
one that leaves its own block behind and says so. ${target} is now:
$(cat "$target")"
          break
        fi
        ;;
    esac

    if [ "$case_name" = nft-rejects ]; then
      # The refusal has to name ITS reason. Both refusals in remove_firewall leave the block
      # wired, so "the files are still here" cannot tell them apart — and an operator whose
      # markers are fine is sent to look for a damaged pair by the wrong message.
      case "$output" in
        *"that nft rejects"*) ;;
        *)
          outcome="the uninstaller kept the block on a host where removing it would leave a ${target} that
nft rejects, but did not say that is why. The other refusal blames the markers, which are a matched pair
here, so the operator is sent to look for a fault that is not there:
${output}" ;;
      esac
      [ -z "$outcome" ] || break
    fi

    if [ "$case_name" = wired ]; then
      # The positive control. Without it every check below is satisfied by an uninstaller that
      # deletes nothing at all, which is the shape of vacuous pass this plan keeps finding.
      case "$state" in
        "wired=no ruleset=gone bans=gone") ;;
        *)
          outcome="the uninstaller left an ordinary wired host at '${state}'. It must remove its own include
block and then its own two files, or an uninstall leaves the panel's firewall enforcing on a machine
the panel is gone from:
${output}"
          break
          ;;
      esac
      if [ -e /etc/maran ]; then
        outcome="the uninstaller kept /etc/maran on a host where nothing includes anything in it:
${output}"
        break
      fi
      continue
    fi

    # The five states where something still includes the rendered files. The host they leave has
    # already been checked to load; what is left to ask is whether the operator was told why a
    # file survived, since a file kept in silence is a file nobody removes.
    case "$output" in
      *"still includes"* | *"still names them"*) ;;
      *)
        outcome="the uninstaller kept files on the '${case_name}' host without telling the operator which
lines are keeping them there. A file kept in silence is a file nobody removes:
${output}"
        break
        ;;
    esac
  done

  # Put the host back before anything is reported, so a failure here does not also break every
  # assertion after it.
  rm -rf /etc/maran "$extra_directory"
  cp -a "${backup_config}/maran" /etc/maran
  rm -rf "$backup_config"
  if [ -e "$backup_target" ]; then
    cp -a "$backup_target" "$target"
  else
    rm -f "$target"
  fi
  rm -f "$backup_target"

  [ -z "$outcome" ] || fail "$outcome"
  [ -d /etc/maran/nginx/sites ] \
    || fail "this assertion did not put /etc/maran back the way it found it"

  # And main's ORDER, because that is precisely where the defect lived: remove_firewall decided
  # to keep the two rendered files and said so, and remove_config_and_state deleted the directory
  # holding them four calls later. The loop above drives those two in main's order — but it
  # drives them BY NAME, so a main that reordered them would leave all six cases green.
  #
  # Read out of `declare -f`, which prints the function bash actually parsed, rather than off the
  # file: a doc comment naming either function cannot satisfy this the way a grep over the source
  # would, and that shape of check has already been found here once (the three raw-file greps in
  # assert_firewall_renders_through_the_agent, satisfied by a comment).
  # `declare -f` re-prints each call with a trailing `;`, and the anchors carry it: without them
  # `remove_firewall` would also match the `remove_firewall_rendered_files` line inside it, which
  # is a different function and a different question.
  local main_body firewall_at config_at
  main_body="$(run_uninstaller 'declare -f main')"
  firewall_at="$(printf '%s\n' "$main_body" | grep -nE '^[[:space:]]*remove_firewall;?[[:space:]]*$' \
    | head -1 | cut -d: -f1 || true)"
  config_at="$(printf '%s\n' "$main_body" | grep -nE '^[[:space:]]*remove_config_and_state;?[[:space:]]*$' \
    | head -1 | cut -d: -f1 || true)"
  [ -n "$firewall_at" ] && [ -n "$config_at" ] \
    || fail "uninstall.sh's main no longer calls both remove_firewall and remove_config_and_state as plain
commands. Everything above drives those two by name, so this suite would keep passing while the
uninstaller did something else entirely. main is:
${main_body}"
  [ "$firewall_at" -lt "$config_at" ] \
    || fail "uninstall.sh's main runs remove_config_and_state BEFORE remove_firewall. /etc/maran holds both
rendered files and the marker saying whether this installer enabled nftables, so reading that marker after
the directory is gone makes every uninstall decide 'we did not enable it' — and the firewall unwiring then
asks about files that have already been deleted. main is:
${main_body}"

  echo "The uninstaller removes its own block and files from a wired host, and on five hosts that still"
  echo "include them keeps every named file and says which lines are keeping it. Every one of the six"
  echo "is left with an ${target} real nft still loads."
}

# sshd_effective_ports: the ports of the sockets sshd will actually open, one per line.
#
# The oracle for the assertion below, and the reason it is an oracle rather than a second
# opinion: `sshd -T` prints the effective configuration AFTER processing Include, so it knows
# about drop-in files, this family's own layout and its own defaults.
#
# It reads `listenaddress`, NOT `port`, and the difference is not cosmetic. `port` is the Port
# OPTION, which sshd prints always and defaults to 22 whether or not a socket uses it;
# `listenaddress` is the socket list. Measured on OpenSSH 9.6p1 and 9.9p1, which agree:
#
#   ListenAddress 0.0.0.0:2300, no Port directive  ->  port 22            listenaddress 0.0.0.0:2300
#   Port 2244 + ListenAddress 0.0.0.0:2200 + [::]:2222
#                                                  ->  port 2244         listenaddress 0.0.0.0:2200
#                                                                        listenaddress [::]:2222
#
# In the first, sshd serves 2300 and nothing else; an oracle reading `port` would demand 22 and
# FAIL the build over a detector that answered 2300 correctly. In the second, sshd serves 2200
# and 2222 and never 2244. Reading `port` inverted both. It stays as the fallback for the case
# where a version prints no listenaddress at all, where it is the only answer available.
sshd_effective_ports() {
  local dump ports
  dump="$("$(sshd_binary)" -T -f "$SSHD_CONFIG" 2>/dev/null || true)"
  if [ -z "$dump" ]; then
    # Some versions refuse `-T` outright while a `Match` block is present, and this image has
    # one by the time this runs — 86-sftp.sh appends it. `-C` names a connection to evaluate
    # the Match blocks against, which makes the dump well defined; `Port` and `ListenAddress`
    # are global keywords, so which connection is named cannot change the answer. (Measured:
    # both families' sshd accept the plain form, so this path has never been needed here.)
    dump="$("$(sshd_binary)" -T -C user=root,host=localhost,addr=127.0.0.1 -f "$SSHD_CONFIG" 2>/dev/null || true)"
  fi

  # `0.0.0.0:2300` and `[::]:2222` are the two shapes sshd prints; the port follows the last
  # colon in both. Parsed here rather than through the installer's own helper on purpose — an
  # oracle that shares code with the thing it checks is not an oracle.
  ports="$(printf '%s\n' "$dump" | awk '
      $1 == "listenaddress" {
        spec = $2
        n = split(spec, parts, ":")
        if (parts[n] ~ /^[0-9]+$/) { print parts[n] }
      }' | sort -n -u)"
  if [ -z "$ports" ]; then
    ports="$(printf '%s\n' "$dump" | awk '$1 == "port" { print $2 }' | sort -n -u)"
  fi
  printf '%s\n' "$ports"
}

# assert_detection_covers_sshd: every port sshd will listen on is in detect_ssh_ports' answer.
#
# A SUPERSET is allowed and a subset is not, deliberately. Do not tighten this to an equality.
# The detector CAN name a port sshd does not serve — `Port 2244` beside two `ListenAddress`
# ports makes it answer 2200,2222,2244 where sshd opens 2200 and 2222 — and that is the whole
# of the cost: one extra `accept` in a default-drop ruleset, attack surface rather than a
# lockout, on a port number the host's own administrator wrote into sshd_config. The other
# direction costs the operator their server. Equality is not merely stricter here, it is
# unachievable: the installer parses a file, the oracle asks a daemon, and the two disagree
# in exactly this harmless direction by design.
assert_detection_covers_sshd() {
  local context="$1" detected expected port missing=""
  expected="$(sshd_effective_ports)"
  [ -n "$expected" ] \
    || fail "sshd -T named no port at all for ${context}; the oracle this assertion depends on is not working"

  detected="$(detect_ssh_ports "$SSHD_CONFIG")"
  case "$detected" in
    ''|*[!0-9,]*) fail "detect_ssh_ports answered '${detected}' for ${context}, which is not a port list" ;;
  esac

  for port in $expected; do
    case ",${detected}," in
      *",${port},"*) ;;
      *) missing="${missing} ${port}" ;;
    esac
  done
  [ -z "$missing" ] \
    || fail "for ${context}, sshd listens on${missing} but detect_ssh_ports answered '${detected}'.
The firewall would close a port this host's sshd is serving, and the operator would lose the server."

  echo "  ${context}: sshd names $(echo $expected | tr '\n' ' '), detect_ssh_ports answers ${detected}."
}

# assert_ssh_port_detection_follows_includes: the installer's own port detection, against
# this family's real sshd_config, its real sshd and a real drop-in.
#
# The middle case is the whole point. Ubuntu and Debian ship
# `Include /etc/ssh/sshd_config.d/*.conf` as the first line of sshd_config, and a modern port
# override is a file in there — so a parser that reads only the main file answers 22 for a
# host whose sshd is on 2222, and the firewall then locks the operator out of half the
# platforms we support on their default configuration. A fixture cannot prove this: whether
# the Include is there at all is a fact about the distribution, which is why it is asserted
# on the image. On a family that ships no Include, sshd -T reports no 2222 either and the
# assertion still holds — the oracle adjusts itself.
assert_ssh_port_detection_follows_includes() {
  assert_detection_covers_sshd "this family's stock sshd_config"

  mkdir -p "$(dirname "$SSHD_TEST_DROP_IN")"
  printf 'Port 2222\n' > "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "a drop-in adding Port 2222"

  # The same file with CRLF line endings, because sshd accepts one and serves the port while a
  # parser that leaves the carriage return on the value reads no port at all and the firewall
  # closes it. There was a guard for this in the installer and nothing that exercised it, which
  # is the shape this repository keeps finding: a fixture that names a mechanism is not one
  # that runs it.
  printf 'Port 2222\r\n' > "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "a CRLF drop-in adding Port 2222"

  # A host whose port comes ONLY from ListenAddress, with no Port directive anywhere. sshd
  # serves 2300 alone while still printing `port 22`, so this is the shape that tells a correct
  # oracle from one reading the wrong field — it is here because an earlier version of this
  # function failed the build on the detector's correct answer.
  printf 'ListenAddress 0.0.0.0:2300\n' > "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "a drop-in with only ListenAddress 0.0.0.0:2300"

  rm -f "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "the stock sshd_config again, drop-in removed"
}

# assert_whitelist_seed_walks_this_login_session: the /proc ancestor walk and its session
# bound, on the image, with SSH_CLIENT absent from the process that runs the detection.
#
# It exists because every other case here SETS SSH_CLIENT, so the walk — the whole mechanism
# that makes the seed work under `sudo bash install.sh`, and the session check that keeps a
# tmux server's stale address out of the whitelist — had no coverage at all. That is this
# repository's own lesson applied to the fix for the finding that taught it: naming a mechanism
# in a comment is not exercising it.
assert_whitelist_seed_walks_this_login_session() {
  command -v setsid >/dev/null 2>&1 \
    || fail "setsid (util-linux) is not in this image, so the cross-session refusal cannot be exercised.
It is the control that proves the session check does the refusing rather than the test's own shape."

  local probe recovered refused
  probe="$(mktemp)"
  cat > "$probe" <<PROBE
. "${CONFIG_STEP}"
detect_seed_whitelist_cidr
PROBE

  # The shape sudo leaves behind: an ancestor in this login session holds the address, the
  # process running the detection does not. The trailing command keeps the holder alive —
  # without one bash exec-replaces it, the walk finds nothing, and the test passes for the
  # wrong reason. That false negative happened once already while this was being written.
  recovered="$(SSH_CLIENT="198.51.100.9 40000 22" \
    bash -c "env -u SSH_CLIENT bash '${probe}' 2>/dev/null; echo -n ''")"
  [ "$recovered" = "198.51.100.9/32" ] \
    || fail "with SSH_CLIENT only in an ancestor of the same login session, detect_seed_whitelist_cidr
answered '${recovered}' instead of 198.51.100.9/32. Under 'sudo bash install.sh' — the installer's own
documented usage — the whitelist would be seeded with nothing and the operator could ban themselves."

  # The same chain with one variable changed: the detection runs in its own session, as it does
  # inside a tmux or screen pane whose server is an ancestor carrying somebody else's address.
  refused="$(SSH_CLIENT="198.51.100.9 40000 22" \
    bash -c "setsid --wait env -u SSH_CLIENT bash '${probe}' 2>/dev/null; echo -n ''")"
  [ -z "$refused" ] \
    || fail "an ancestor OUTSIDE this login session seeded '${refused}'. That is a tmux or screen server's
address, which may be another operator on another machine from days ago, and it would be whitelisted here."

  rm -f "$probe"
  echo "The client-address walk recovers from an ancestor in this login session and refuses one outside it."
}

# assert_whitelist_seed_takes_only_addresses: the other lockout-relevant function, on the
# family's own bash.
#
# The three refusals are not pedantry. A malformed row reaches the panel as a whitelist
# entry that can never match a packet, so the operator reads their own address back out of
# the panel, believes they are exempt from the automatic bans, and is not.
assert_whitelist_seed_takes_only_addresses() {
  local seeded
  seeded="$(SSH_CLIENT="203.0.113.7 54321 22" detect_seed_whitelist_cidr 2>/dev/null)"
  [ "$seeded" = "203.0.113.7/32" ] \
    || fail "detect_seed_whitelist_cidr made '${seeded}' out of an ordinary IPv4 SSH_CLIENT"

  seeded="$(SSH_CLIENT="2001:db8::7 54321 22" detect_seed_whitelist_cidr 2>/dev/null)"
  [ "$seeded" = "2001:db8::7/128" ] \
    || fail "detect_seed_whitelist_cidr made '${seeded}' out of an IPv6 SSH_CLIENT"

  local malformed
  for malformed in "999.999.999.999" "1.2.3.4:5" "::::::::::" "01.2.3.4" "$(printf '203.0.113.7\nInjected=1')"; do
    seeded="$(SSH_CLIENT="${malformed} 1 22" detect_seed_whitelist_cidr 2>/dev/null)"
    [ -z "$seeded" ] \
      || fail "detect_seed_whitelist_cidr seeded '${seeded}' from a client address that is not an address"
  done

  echo "detect_seed_whitelist_cidr seeds real addresses and refuses the rest."
}

# assert_mysql_gate_accepts_socket_auth: the installer's gate, against the server
# as the family's own package leaves it. This is the positive case, and the one
# that would silently stop being run if verify_mysql_socket_auth were deleted —
# the function call below would then be "command not found" and the build fails.
assert_mysql_gate_accepts_socket_auth() {
  [ -x /usr/bin/mysql ] || fail "/usr/bin/mysql is missing; it is the path the agent execs"
  run_installer_step 'verify_mysql_socket_auth' || fail "verify_mysql_socket_auth refused this image's MariaDB"
}

# assert_gate_refuses_with: runs the gate expecting it to refuse, and expecting it
# to say the RIGHT thing while refusing.
#
# The message is asserted, not just the exit status, and that is not politeness
# about wording. The gate asks two questions — can the agent connect, and was it
# the socket that let it in — and either one refuses a server with a root
# password, so an assertion that only reads the exit status is passed by a gate
# with one of them deleted. Measured: deleting the connection check survives an
# exit-status-only assertion. Refusing with the wrong diagnosis is also a real
# defect on its own, since it sends an operator to fix a service that is running
# perfectly well.
assert_gate_refuses_with() {
  local situation="$1" expected="$2" output
  if output="$(run_installer_step 'verify_mysql_socket_auth' 2>&1)"; then
    fail "verify_mysql_socket_auth accepted ${situation}"
  fi
  case "$output" in
    *"$expected"*) ;;
    *) fail "verify_mysql_socket_auth refused ${situation} but never said '${expected}'" ;;
  esac
  echo "verify_mysql_socket_auth refused ${situation}, saying the right thing."
}

# assert_mysql_gate_refuses_passwordless_root: root that answers to anyone local
# with no credential at all is not a working install, it is a server where every
# local user owns every customer database. The gate must say no, and must say why.
assert_mysql_gate_refuses_passwordless_root() {
  sql -e 'ALTER USER "root"@"localhost" IDENTIFIED BY "";'
  assert_gate_refuses_with "a root@localhost with an EMPTY password" \
    "it has no password at all"
}

# assert_mysql_gate_refuses_password_root: the realistic break — an operator who
# set a root password by hand. The gate must refuse rather than prompt for it,
# store it, or invent a second privileged account to get around it, and it must
# tell the operator the connection failed rather than blaming a missing password.
assert_mysql_gate_refuses_password_root() {
  sql -e "ALTER USER \"root\"@\"localhost\" IDENTIFIED BY \"${THROWAWAY_ROOT_PASSWORD}\";"
  assert_gate_refuses_with "a password-authenticated root@localhost" \
    "cannot connect to MariaDB as root@localhost over the unix socket"
}

# restore_socket_auth: puts root back the way the package had it, so the image
# ships a server the polygon suites can actually use.
restore_socket_auth() {
  sql "--password=${THROWAWAY_ROOT_PASSWORD}" \
    -e 'ALTER USER "root"@"localhost" IDENTIFIED VIA unix_socket;'
  sql -e "FLUSH PRIVILEGES;"
}

# assert_sftp_prerequisites: the group, the jail base directory and exactly one
# Match block — after running the installer's function TWICE, because a re-run
# that duplicates the block is the failure this is here to catch.
assert_sftp_prerequisites() {
  run_installer_step 'install_sftp_prerequisites' || fail "install_sftp_prerequisites failed on its first run"
  run_installer_step 'install_sftp_prerequisites' || fail "install_sftp_prerequisites failed on its second run"

  getent group maran-sftp >/dev/null \
    || fail "the maran-sftp group was not created (DistroAdapter::sftp_group)"

  [ -d /var/lib/maran-sftp ] \
    || fail "the SFTP jail base directory /var/lib/maran-sftp was not created"

  local ownership_and_mode
  ownership_and_mode="$(stat -c '%U:%G:%a' /var/lib/maran-sftp)"
  [ "$ownership_and_mode" = "root:root:700" ] \
    || fail "/var/lib/maran-sftp is ${ownership_and_mode}, not root:root:700"

  # The directory's OWN mode was all this used to ask, and that is precisely how the defect
  # got past it: the base was root:root 0755 and correct, while the panel-owned
  # /var/lib/maran above it made sshd refuse every chroot. OpenSSH walks the whole path, so
  # the assertion has to walk it too.
  assert_ancestors_are_root_only /var/lib/maran-sftp

  local blocks
  # `|| true` because `grep -c` exits 1 when it counts NOTHING, and `set -e`
  # would then kill this script inside the command substitution — before the
  # named `fail` below is ever reached. Measured: with 86-sftp.sh's
  # install_sshd_match_block stubbed out, this script exited 1 and printed no
  # diagnosis at all, so the build failed without saying which installer step
  # had stopped working. An exit status is not evidence of which check fired.
  blocks="$(grep -c '^Match Group maran-sftp$' "$SSHD_CONFIG" || true)"
  [ "$blocks" -eq 1 ] \
    || fail "sshd_config carries ${blocks} 'Match Group maran-sftp' blocks after two runs, not 1"

  local directive
  for directive in \
    '^    ChrootDirectory %h$' \
    '^    ForceCommand internal-sftp$' \
    '^    AllowTcpForwarding no$' \
    '^    X11Forwarding no$'; do
    grep -q "$directive" "$SSHD_CONFIG" \
      || fail "the Match block is missing a directive matching: ${directive}"
  done

  sshd -t || fail "sshd rejects the config the installer produced"
}

# assert_sftp_validates_before_replacing: a broken sshd_config must survive the
# installer untouched rather than being replaced by a broken one with our block
# appended. Proved by breaking it on purpose, because "it validates first" is a
# claim about a failure path, and a failure path nothing exercises is a comment.
#
# An operator locked out of a server by an installer has lost the server, which
# is why this is asserted here and not left to a reading of the code.
assert_sftp_validates_before_replacing() {
  local pristine
  pristine="$(mktemp)"
  cp "$SSHD_CONFIG" "$pristine"
  printf 'ThisIsNotAnSshdDirective yes\n' >> "$SSHD_CONFIG"

  if run_installer_step 'install_sshd_match_block' >/dev/null 2>&1; then
    cp "$pristine" "$SSHD_CONFIG"
    rm -f "$pristine"
    fail "install_sshd_match_block replaced a config that 'sshd -t' rejects"
  fi
  grep -q '^ThisIsNotAnSshdDirective yes$' "$SSHD_CONFIG" \
    || fail "install_sshd_match_block modified sshd_config despite failing validation"

  cp "$pristine" "$SSHD_CONFIG"
  rm -f "$pristine"
  sshd -t || fail "the pristine sshd_config was not restored"
  echo "install_sshd_match_block left an invalid sshd_config untouched, as it must."
}

# run_nginx_step: runs step 80's code THE WAY install.sh runs it — a plain command in a child
# shell with `set -euo pipefail`, the step file sourced, and the values install.sh decides
# exported into the environment first — and hands back its status and its output.
#
# A CHILD PROCESS, for the reason run_installer_step gives at length: `exit` and `set -e` mean
# different things inside this script's `if` than they do in the installer. Step 80 aborts a
# refused install with `exit 1`, which is observable across a process boundary and is invisible
# inside a `( … )` used as an `if` condition.
#
# 80-nginx.sh alone rather than added to run_installer_step's list: the panel port and the api
# socket are environment step 80 needs and the other four steps do not, and step 80's top-level
# `readonly` declarations have no business in their children.
#
# The port and the socket come from install.sh, never from literals here, for the same reason
# assert_panel_port_has_one_authority exists: a check that carries its own copy of a value stops
# following the one place that decides it.
run_nginx_step() {
  local snippet="$1" path_prefix="${2:-}"
  local search_path="$PATH"
  # An optional directory placed AHEAD of the step's PATH. One case needs to interfere with a
  # tool the step runs rather than with the step itself — see
  # nginx_shim_that_interrupts_the_validation — and passing it here keeps every other case
  # driving the step through exactly the same environment install.sh gives it.
  [ -z "$path_prefix" ] || search_path="${path_prefix}:${PATH}"
  MARAN_PANEL_PORT="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")" \
    MARAN_API_SOCKET_PATH="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")" \
    MARAN_GROUP="$POLYGON_SERVICE_GROUP" \
    LIB_DIR="$INSTALLER_LIB" \
    PATH="$search_path" \
    bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$NGINX_STEP" "$snippet"
}

# nginx_shim_that_interrupts_the_validation: writes, into the directory `$1`, an `nginx` that
# kills the shell which ran it with signal `$2` and then does what the real one would have done.
#
# It is how this script produces the interrupt an operator actually produces — Ctrl-C at the
# terminal, an SSH session that drops, an OOM kill of `sudo bash install.sh` — reduced to the
# one instant that matters: after the candidate has been renamed onto the served path and
# before the step has decided anything about it. `$PPID` inside the shim is the step's own
# shell, because `nginx -t` runs as its direct child inside the step's `if`.
#
# `kill` FIRST and then the real binary, deliberately: bash defers a trapped signal until the
# foreground command finishes, so the validation really runs, really prints its verdict, and the
# step's shell meets the signal at the point in the window where a real interrupt would arrive.
# With KILL there is nothing to defer and the shell dies at once, which is the other case below.
#
# IT FIRES ONCE, like the one Ctrl-C an operator presses, and that is load-bearing rather than
# tidy. Step 80's restoration asks `nginx -t` whether the tree loads with the file it is about to
# put back — that is the check this suite's newest case exists for — and a shim that killed on
# every `-t` would kill that verification too: measured, the step then died of the SHIM's signal
# in the middle of its handler, so the exit-status check below would have read 143 off the shim
# rather than off the step's own re-raise, and the mutant that deletes the re-raise would have
# stayed green.
#
# The real binary's absolute path is resolved HERE and written into the shim, because the shim
# runs with its own directory first on PATH and a plain `nginx` inside it would call itself.
nginx_shim_that_interrupts_the_validation() {
  local directory="$1" signal="$2" real
  real="$(command -v nginx)"
  cat > "${directory}/nginx" <<EOF
#!/bin/sh
if [ "\$1" = -t ] && [ ! -e "${directory}/fired" ]; then
  : > "${directory}/fired"
  kill -${signal} "\$PPID"
fi
exec ${real} "\$@"
EOF
  chmod 755 "${directory}/nginx"
}

# nginx_service_records: which of the polygon systemctl's records for the nginx unit exist, as a
# readable list, or nothing at all when the service was left alone.
#
# All in one reader, because "the service was never touched" is one question and asking it several
# different ways in several places is several chances to ask it weakly. Three things are what a
# container can observe: `enable` writes the enablement file, `reload` writes the reload file, and
# `start`/`restart`/`stop` write the ActiveState one. The reload one is the one that used to be
# missing, and it is the one that matters most here: a reload is the single event by which a
# configuration file reaches a RUNNING server, and it changes neither of the other two.
#
# BOTH SPELLINGS OF THE UNIT, and that is a fix rather than thoroughness. systemd treats `nginx`
# and `nginx.service` as one unit; the polygon's systemctl deliberately does not — it keeps one
# state file per literal name, because normalising them would rename the files the agent's own
# monitor suite writes (docker/polygon/systemctl-stand-in.sh says so at length). So a gate reading
# only the short name is blind to a step that reloads `nginx.service`, and measured: with the
# step's own reload line moved inside the swap window and spelled `nginx.service`, this assertion
# passed green on both families while a vhost `nginx -t` had never seen reached the running server.
# Both spellings work identically on a real host and installer/lib/70-services.sh already uses the
# suffixed form for the panel's own units, so the next edit to step 80 could pick either.
nginx_service_records() {
  local records="" unit
  for unit in nginx nginx.service; do
    [ ! -e "${UNIT_STATE_DIRECTORY}/${unit}.enabled" ] || records="${records} enabled"
    [ ! -e "${UNIT_STATE_DIRECTORY}/${unit}.reloaded" ] || records="${records} reloaded"
    [ ! -e "${UNIT_STATE_DIRECTORY}/${unit}" ] || records="${records} started-or-restarted"
  done
  printf '%s' "${records# }"
}

# forget_nginx_service_records: remove every record nginx_service_records reads, both spellings.
#
# Beside that reader rather than spelled out at each case, and for the same reason it reads both
# names: a case that cleared only the short spelling would leave a `nginx.service` marker written
# by the case before it, and the next case's "the service was never touched" would go red for
# somebody else's reason — the mirror of the blindness this pair was written to fix.
forget_nginx_service_records() {
  local unit
  for unit in nginx nginx.service; do
    rm -f "${UNIT_STATE_DIRECTORY}/${unit}.enabled" \
      "${UNIT_STATE_DIRECTORY}/${unit}.reloaded" \
      "${UNIT_STATE_DIRECTORY}/${unit}"
  done
}

# prepare_host_for_the_nginx_step: the state a real server is in by the time step 80 runs, taken
# from the installer's own step 40 rather than invented here.
#
# Step 80 needs exactly two things from that step: the service group, because it installs the
# panel's private key root:<that group>, and /var/log/maran, because the vhost names it in access_log
# and error_log and `nginx -t` opens both. The group is created by step 40's own function. The
# directory is made here with step 40's own owner, group and mode instead of by calling
# create_directory_layout, and that is stated rather than hidden: that function also re-modes
# /run/maran to 0750 root:<service group>, which this image sets 0755 on purpose for the php-pool suites,
# and step 80 never reads it.
#
# `-o root` and not the service account, matching step 40 since the log-directory split: the two files the
# vhost names here are opened by the ROOT nginx master, so the directory holding them is root's
# and the panel writes under /var/log/maran/panel instead
# (docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md). Preparing the
# host the OLD way would leave `nginx -t` passing here against an ownership no install produces.
prepare_host_for_the_nginx_step() {
  run_user_step 'create_service_user >/dev/null
install -d -o root -g "$MARAN_GROUP" -m 0750 /var/log/maran'
}

# nginx_reads_configuration_file: whether nginx's own dump of the configuration it loads names
# `$1`.
#
# `nginx -T` prints one `# configuration file <path>:` line per file it actually parsed, so this
# asks NGINX which files it reads instead of asking a glob written here whether it thinks it
# matches. That is the exact question the defect below turned on, and nothing this script could
# compute about include patterns answers it as well as the binary that follows them.
#
# The dump is read into a variable and matched with `case` rather than piped into `grep -q`, and
# the mechanism is written down with the measurement that produced it, because this check was
# already once fixed with an explanation that did not survive being tested.
#
# `grep -q` exits at its first match and closes the read end of the pipe. `nginx` is usually still
# writing — the matched line is `# configuration file <path>:`, and every byte of that file and of
# every file included after it comes AFTER it — so nginx's next `write` takes SIGPIPE and exits
# 141. Under this script's `set -o pipefail` the pipeline's status is that death rather than grep's
# answer, and the function reports "nginx does not read this file" about a file nginx has just been
# seen reading.
#
# Measured, both polygon images, `PIPESTATUS` captured in the same run as the failure:
#
#   alma9, 300 runs of the piped form:   26 wrong answers, every one of them `nginx=141 grep=0`
#                                        — grep FOUND the line and pipefail overrode it
#   dump 14 946 bytes, of which 6 988 remained to be written after the matched line
#   ubuntu24, 300 runs:                  0 wrong answers in that run, 12/200 and 23/200 in others
#                                        — it is a race, and its rate is not stable
#   both families, padding added under conf.d so 300 KB follows the match: 200/200 wrong, and the
#   form used below: 0/200
#
# What this does NOT depend on is the dump exceeding the 64 KiB pipe buffer: a write to a pipe
# whose reader has closed fails whether or not the writer would have blocked. That was the reason
# offered when this was first fixed, and it is wrong — it predicts 0 failures at these images'
# 15-17 KiB dumps, and the measurement above is 26 in 300. Recorded so that the next person to see
# this function flake is not sent to look at buffer sizes.
nginx_reads_configuration_file() {
  local dump
  dump="$(nginx -T 2>/dev/null || true)"
  case "$dump" in
    *"# configuration file $1:"*) return 0 ;;
  esac
  return 1
}

# nginx_gate_outcome: drives step 80 through the five cases below and prints what went wrong, or
# prints nothing at all. It is separate from the assertion so that every way out of it passes
# through the one restore of the shipped template and the host in the caller.
#
# `$1` is the path step 80 serves the vhost from, obtained from the step's own nginx_conf_dest.
# stamp_as_maran_vhost: rewrite `$1` so that step 80's vhost_is_ours accounts for it — the marker
# `$2` on line 1, carrying the SHA-256 of every byte after it, replacing whatever line 1 held.
#
# Cases below need BOTH answers from that predicate on demand, and neither can be produced by hand:
# a file this installer wrote is one whose digest matches, and there is no way to write one without
# computing the digest. The marker prefix is read out of the STEP's own constant rather than spelled
# again here, for the same reason the two suffixes are — a check carrying its own copy of a value
# stops following the one place that decides it.
stamp_as_maran_vhost() {
  local file="$1" prefix="$2" body digest
  body="$(mktemp)"
  tail -n +2 -- "$file" > "$body"
  digest="$(sha256sum "$body" | cut -d' ' -f1)"
  { printf '%s%s\n' "$prefix" "$digest"; cat "$body"; } > "$file"
  rm -f "$body"
}

# swap_recorder: writes, into the directory `$1`, a `sync` and an `nginx` that each append what they
# were asked to do to `$1/log`, in order, and then do what the real ones would have done.
#
# It is how this script observes a durability the filesystem gives no way to read back. `sync FILE`
# and `sync DIRECTORY` are coreutils' fsync of exactly those objects — traced on alma9, each issues
# one `fsync(3)` — so "the served vhost and its directory were fsynced" is answerable by recording
# which operands the step passed. Nothing else in this suite can see the difference between a step
# that flushes the file it installs and one that flushes only the copy beside it, which is how a
# header comment claiming the whole of rules/rust.md's protocol survived three rounds over a swap
# that was a plain `install` and `mv`.
#
# `nginx` IS RECORDED TOO, and it is what makes the recording mean anything. A check that merely
# asked whether `/etc/nginx/conf.d` appears among the operands passes on a step that never syncs it
# around the swap at all, because the rollback copy's own write and its later removal sync that same
# directory twice more: measured, deleting the directory sync from the swap left that check GREEN.
# The property is not "this directory was synced at some point", it is "the staged file and then its
# directory were flushed, and only then was the vhost validated" — an ordering, which needs the
# validation in the same log to be visible at all.
#
# The real binaries' absolute paths are resolved HERE and written in, because the shims run with
# their own directory first on PATH and a plain `sync` or `nginx` inside one would call itself.
swap_recorder() {
  local directory="$1" real_sync real_nginx
  real_sync="$(command -v sync)"
  real_nginx="$(command -v nginx)"
  cat > "${directory}/sync" <<EOF
#!/bin/sh
for operand in "\$@"; do
  printf '%s\n' "\$operand" >> "${directory}/log"
done
exec ${real_sync} "\$@"
EOF
  cat > "${directory}/nginx" <<EOF
#!/bin/sh
printf 'nginx %s\n' "\$*" >> "${directory}/log"
exec ${real_nginx} "\$@"
EOF
  chmod 755 "${directory}/sync" "${directory}/nginx"
}

nginx_gate_outcome() {
  local dest="$1"
  # A directive nginx has never had, appended at the END of the template so that breaking it does
  # not depend on the vhost's internal shape, and distinctive enough to be searched for by name
  # across the whole configuration tree afterwards.
  local broken="this_directive_is_not_nginxs_and_never_was"
  # The three files the polygon's systemctl writes for the nginx unit. They are named here and
  # read through nginx_service_records below; the reload one is the newest and the reason this
  # function was rewritten — the suite used to read the enablement file alone and call it "the
  # service never touched".
  local enabled_marker="${UNIT_STATE_DIRECTORY}/nginx.enabled"
  local reload_marker="${UNIT_STATE_DIRECTORY}/nginx.reloaded"
  local state_marker="${UNIT_STATE_DIRECTORY}/nginx"
  # The two names the step swaps the vhost through, taken from the STEP's own constants rather
  # than written again here: a check carrying its own copy of a value stops following the one
  # place that decides it, which is the same reason assert_panel_port_has_one_authority exists.
  local candidate_suffix previous_suffix adopted_suffix foreign_suffix marker_prefix
  candidate_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_CANDIDATE_SUFFIX"')"
  previous_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_PREVIOUS_SUFFIX"')"
  adopted_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_ADOPTED_SUFFIX"')"
  foreign_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_FOREIGN_SUFFIX"')"
  marker_prefix="$(run_nginx_step 'printf %s "$MARAN_VHOST_MARKER_PREFIX"')"
  local output rendered good shim records status

  # 1. THE POSITIVE CONTROL, and it is not optional. A gate that refuses everything satisfies
  #    both negative cases below and fails only here; without this half the suite could not tell
  #    a working gate from one that had stopped accepting the product's own configuration.
  forget_nginx_service_records
  if ! output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    printf '%s\n' "step 80 refused the panel vhost this repository SHIPS, so the installer cannot finish on
this family at all:
${output}"
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    printf '%s\n' "step 80 reported success and left an nginx configuration that does not load:
$(nginx -t 2>&1)"
    return
  fi
  # The proposition the whole defect was about: the file that is now served is a file nginx
  # actually opens. Staged as `maran.conf.staging`, it was not — and `nginx -t` passed anyway.
  if ! nginx_reads_configuration_file "$dest"; then
    printf '%s\n' "step 80 reported success, but nginx's own dump of the configuration it loads does not name
${dest}. The installed vhost is a file nginx never opens, which is the defect itself: the thing
validated and the thing served are two different files."
    return
  fi
  # And the other direction: what is served is byte-for-byte what was rendered, so nothing
  # between the render and the served path can rewrite the vhost after it was validated.
  rendered="$(mktemp)"
  if ! run_nginx_step "render_vhost '${rendered}'" >/dev/null 2>&1; then
    rm -f "$rendered"
    printf '%s\n' "render_vhost failed on the shipped template outside step_nginx, so this check cannot compare
what step 80 served with what it rendered."
    return
  fi
  if ! cmp -s "$rendered" "$dest"; then
    rm -f "$rendered"
    printf '%s\n' "the file step 80 left at ${dest} is not byte-for-byte what render_vhost produces from the
shipped template, so what 'nginx -t' accepted and what nginx serves are two different files."
    return
  fi
  rm -f "$rendered"
  if [ ! -e "$enabled_marker" ]; then
    printf '%s\n' "step 80 finished without enabling nginx. The negative cases below read the ABSENCE of
${enabled_marker} as 'the install stopped before it touched the service', and a step that never
enables anything at all would make every one of them pass for a reason that is not the one they
name."
    return
  fi
  if [ ! -e "$reload_marker" ]; then
    printf '%s\n' "step 80 finished without reloading nginx: the new vhost is on disk and the running server was
never told about it. The negative cases below read the ABSENCE of ${reload_marker} as 'the install
stopped before it reached the service', which is the check that catches a reload INSIDE the swap
window — the one way a configuration 'nginx -t' has not seen reaches a running server. A step that
never reloads anything would retire that question instead of asking it."
    return
  fi
  # The third record the absence checks read is the ActiveState one, written by `start`, `restart`
  # and `stop`. A successful step_nginx never reaches those verbs in this image — its
  # `systemctl reload nginx` succeeds here, so the `|| systemctl restart nginx` fallback does not
  # run — so the file is proved WRITABLE here rather than left as an absence nothing could have
  # produced. That fallback is the dangerous half of the step's reload line on a real host: a
  # restart against a vhost `nginx -t` has not seen stops an nginx that then does not come back.
  rm -f "$state_marker"
  systemctl restart nginx
  if [ ! -e "$state_marker" ]; then
    printf '%s\n' "the polygon's systemctl did not record a restart of nginx at ${state_marker}, so every check
below that reads its absence cannot fail for the reason it names. Fix the stand-in, not the check."
    return
  fi
  rm -f "$state_marker"

  # The vhost now in place is the one a re-install has to roll back to.
  good="$(mktemp)"
  cp -p "$dest" "$good"

  # 1a. THE DURABILITY REACHES THE FILE THAT IS SERVED, not only the copy beside it.
  #
  #     rules/rust.md "Config writes: render → swap → validate", step 3: fsync the temporary file
  #     AND its containing directory, "so a crash cannot leave a rename pointing at unflushed
  #     bytes". Step 80 spent three rounds giving that treatment to <dest>.previous alone while the
  #     served vhost went in through a bare `install` and `mv`, under a header comment claiming the
  #     whole protocol and a doc comment citing the rule by line number. The failure is not exotic:
  #     a SUCCESSFUL install, then a power cut inside the ext4 writeback window, leaves the
  #     directory entry for the panel vhost pointing at unflushed data — a zero-length or
  #     null-padded file — and nginx will not START. This same nginx serves every customer site on
  #     the host through maran-sites.conf, so it is the whole box at the next boot, with the
  #     rollback copy already deleted because the install had succeeded.
  #
  #     Observed through a recording `sync`, because a filesystem gives no way to read back whether
  #     an fsync happened. What is required is the pair: the staged file before the rename, and the
  #     directory the rename commits in.
  local recorder log log_one_line wanted
  recorder="$(mktemp -d)"
  swap_recorder "$recorder"
  forget_nginx_service_records
  if ! output="$(run_nginx_step 'step_nginx' "$recorder" 2>&1)"; then
    rm -rf "$recorder"
    rm -f "$good"
    printf '%s\n' "step 80 failed while its durability was being observed, so this case proves nothing:
${output}"
    return
  fi
  log="$(cat "${recorder}/log" 2>/dev/null || true)"
  rm -rf "$recorder"
  # The three events of the swap, adjacent and in this order: flush the staged file, flush the
  # directory the rename commits in, then validate. Adjacency is what stops each line being
  # satisfied by some other part of the step — the rollback copy's own write and its removal both
  # sync this same directory, and the whole point of the case is to tell those apart from the swap.
  # Joined onto one line first, and that is a correction rather than a style: `grep -F` given a
  # multi-line pattern treats each line as a SEPARATE pattern and matches any one of them, so the
  # sequence check written that way passed on both fsync mutants — measured. `|` appears in no path
  # and in no nginx argument this step passes, so it is safe as the joiner.
  log_one_line="$(printf '%s|' "$log" | tr '\n' '|')"
  wanted="${dest}${candidate_suffix}|$(dirname "$dest")|nginx -t|"
  if [ "${log_one_line#*"$wanted"}" = "$log_one_line" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 did not flush the vhost it installs the way rules/rust.md 'Config writes: render →
swap → validate' step 3 requires: fsync the staged file AND its containing directory, 'so a crash
cannot leave a rename pointing at unflushed bytes'. Expected these three, adjacent and in order:
${wanted}
What the step actually did, in order (every fsync operand and every nginx invocation, '|'-joined):
${log_one_line:-(nothing)}
A successful install followed by a power cut inside the writeback window then leaves the directory
entry for the panel vhost pointing at unflushed data — a zero-length or null-padded file — and
nginx will not START. This nginx serves every customer site on the host through maran-sites.conf,
so it is the whole box at the next boot, and the rollback copy is already gone because the install
succeeded. Three rounds of this step gave that treatment to the rollback copy alone, under a header
comment claiming the whole protocol."
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the durability observation changed what step 80 serves; the cases below can no longer start
from the vhost case 1 installed."
    return
  fi

  # 1b0. AN OPERATOR'S OWN BACKUP UNDER THE STEP'S OWN NAME, AND AN INSTALL THAT SUCCEEDS.
  #
  #      `maran.conf.previous` is the name a human picks for a backup, and the step's own comment
  #      has conceded that since round 2. Round 3's answer was to treat whatever sits there as its
  #      working state: it copied the served vhost OVER the operator's file and then deleted it when
  #      the install succeeded. The operator's backup was gone, with no message naming it.
  #
  #      The step now decides that by PROVENANCE — vhost_is_ours, the marker its own render_vhost
  #      stamps and the digest of the bytes after it — rather than by anything about the state of
  #      the host. A file it cannot account for is moved aside and never deleted, by this step or by
  #      uninstall.sh. This case pins the predicate from the side case 4 cannot: case 4 fails if the
  #      predicate is always FALSE (a real leftover stops being preferred), and this one fails if it
  #      is always TRUE (a stranger's file is consumed and deleted).
  local backup_copy
  cp -p "$good" "${dest}${previous_suffix}"
  printf '\n# an operator backup, taken by hand before touching anything\n' >> "${dest}${previous_suffix}"
  backup_copy="$(mktemp)"
  cp -p "${dest}${previous_suffix}" "$backup_copy"
  forget_nginx_service_records
  if ! output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good" "$backup_copy"
    printf '%s\n' "step 80 refused to finish an install because a file it did not write was sitting at
${dest}${previous_suffix}. Somebody else's file under one of this step's working names is a thing to
move aside and name, not a reason to stop an install:
${output}"
    return
  fi
  if ! grep -qF "${foreign_suffix}" <<<"$output"; then
    rm -f "$good" "$backup_copy"
    printf '%s\n' "step 80 met a file at ${dest}${previous_suffix} that it did not write and never told the
operator where it went:
${output}"
    return
  fi
  local survivor found_backup=0
  while IFS= read -r survivor; do
    if cmp -s "$backup_copy" "$survivor"; then
      found_backup=1
    fi
  done < <(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)
  if [ "$found_backup" -ne 1 ]; then
    rm -f "$good" "$backup_copy"
    printf '%s\n' "step 80 finished an install and destroyed an operator's own backup at
${dest}${previous_suffix}. Nothing on this host still holds those bytes:
$(ls -1 "$(dirname "$dest")" | grep -F "$(basename "$dest")" || true)
The step decides what is its own working state by PROVENANCE — the marker its render stamps — and a
file it cannot account for is moved aside, never consumed and never removed. Treating whatever sits
under that name as scratch is how a re-install eats the backup an operator took before running it.
Its output:
${output}"
    return
  fi
  rm -f "$backup_copy"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the operator-backup case changed what step 80 serves; the cases below can no longer start
from the vhost case 1 installed."
    return
  fi

  # 1b1. A DIRECTORY UNDER ONE OF THE STEP'S OWN WORKING NAMES, AND AN INSTALL THAT SUCCEEDS.
  #
  #      Measured on alma9 against round 3: the rollback copy's `mv` moved the staged file INTO the
  #      directory and succeeded, so the step recorded a rollback copy that is not a file; the swap
  #      and the validation then passed; and the `rm -f` at the end failed on a directory and killed
  #      the shell under `set -e` — AFTER the guard was disarmed, so nothing printed the step's name.
  #      The operator got exit 1 from an install that had worked, a vhost on disk the running server
  #      was never told about (neither `systemctl enable` nor `reload` runs after that point), and a
  #      stray copy of the panel vhost left inside the directory.
  mkdir -p "${dest}${previous_suffix}"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -ne 0 ]; then
    rm -rf "${dest}${previous_suffix}"
    rm -f "$good"
    printf '%s\n' "step 80 exited ${status} because a DIRECTORY was sitting at ${dest}${previous_suffix}. Its output:
${output}
Everything the step exists to do had already worked. A working name occupied by something that is
not a regular file is a thing to move aside and name, not a way to end an install non-zero after it
has succeeded — and an install that dies there never reaches 'systemctl enable' or 'reload', so the
vhost is on disk and the running server has not been told."
    return
  fi
  records="$(nginx_service_records)"
  case "$records" in
    *reload*) ;;
    *)
      rm -rf "${dest}${previous_suffix}"
      rm -f "$good"
      printf '%s\n' "step 80 reported success with a directory at ${dest}${previous_suffix} but never reloaded
nginx — the polygon's systemctl recorded: ${records:-nothing}. The new vhost is on disk and the
running server was never told about it."
      return
      ;;
  esac
  rm -rf "${dest}${previous_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -exec rm -rf {} +
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the directory case changed what step 80 serves; the cases below can no longer start from the
vhost case 1 installed."
    return
  fi

  # 1b. AN INTERRUPT WITH THE SHIPPED TEMPLATE, and the ONLY case that can see the second half of
  #     the interrupt guarantee: that the step DIES of the signal rather than resuming.
  #
  #     Every other interrupt case here uses a broken template, so the step fails for that reason
  #     too and a swallowed signal is invisible. Measured with `kill -s "$signal" "$BASHPID"`
  #     replaced by `return "$status"`: the guard restored, the step carried on past its own
  #     handler, enabled and reloaded nginx and printed "panel reachable on port 8443" — with no
  #     panel vhost anywhere on the machine, because the guard had just removed it. Exit 0, a
  #     success line, and nothing served.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" TERM
  status=0
  output="$(run_nginx_step 'step_nginx' "$shim" 2>&1)" || status=$?
  rm -rf "$shim"
  # The premise, asserted rather than assumed, and the reason is measured: the shim fires ONCE, and
  # step 80 runs an `nginx -t` before the swap whenever a leftover rollback copy is on the host. In
  # that state the single fire lands there, the step dies before it has written anything, and every
  # check below is satisfied by a run that never armed the guard — the status is still 143 and the
  # served file is still byte-for-byte the one that was there, because nothing touched it. The
  # sentence the step prints on that path names it exactly.
  case "$output" in
    *"was not written to at all"*)
      rm -f "$good"
      printf '%s\n' "this case cannot test what it names: step 80 stopped BEFORE it wrote anything to ${dest}, so
the interrupt landed outside the swap window and the guard was never armed. The status and byte
checks below would pass on a step with no guard at all. Its output:
${output}"
      return
      ;;
  esac
  if [ "$status" -ne 143 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was sent SIGTERM in the middle of its own validation and exited ${status}, not 143.
An installer that answers a signal with anything but dying of it has decided to carry on: at 0 it
went on to enable and reload nginx and to report the panel reachable, with whatever the guard had
just taken off the served path. 128 + SIGTERM is the only status that says the step stopped because
it was told to. Its output:
${output}"
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted with the SHIPPED template and ${dest} is no longer the vhost that was
there before it ran. The guard restored the wrong thing, or nothing."
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted during its validation and touched the nginx service anyway — the
polygon's systemctl recorded: ${records}. Reached at all, that line means the step ran past its own
interrupt handler."
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "an interrupted install with the shipped template left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)"
    return
  fi

  # 1c. A FAILURE BEFORE THE SWAP, WITH A ROLLBACK COPY ALREADY ON THE HOST — the guard must not
  #     restore a served path nothing has written to.
  #
  #     The guard is armed before the candidate is written, deliberately: the window between the
  #     rename and the arm is the hole it exists to close. So there is a stretch in which it is
  #     armed and the served file is still untouched, and a restoration that fired there would
  #     REPLACE a vhost this step never wrote. Measured before the fix, with a leftover rollback
  #     copy present and an `install` that fails the way a full /etc fails: the served path took
  #     the leftover copy and the step printed "is as it was before this step ran".
  #
  #     The leftover has to be one the step will hold aside as its first choice on a rollback
  #     rather than overwrite — which means the tree must not load AND the file must be one the
  #     step can account for — so a third party's broken file goes into conf.d for the length of
  #     this case and comes out again at the end of it, and the leftover is re-stamped after it is
  #     altered so that it stays a vhost this installer wrote. That is also the honest shape of the
  #     bug: an operator re-running the installer on a host something else has already broken.
  local intruder="/etc/nginx/conf.d/zz-not-maran.conf"
  printf 'a_directive_belonging_to_nobody on;\n' > "$intruder"
  cp -p "$good" "${dest}${previous_suffix}"
  printf '\n# leftover-rollback-copy-from-an-earlier-run\n' >> "${dest}${previous_suffix}"
  stamp_as_maran_vhost "${dest}${previous_suffix}" "$marker_prefix"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step "install_validated_vhost '/nonexistent/render' '${dest}'" 2>&1)" || status=$?
  rm -f "$intruder"
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "${dest}${previous_suffix}"
    printf '%s\n' "step 80's install_validated_vhost reported success for a render that does not exist:
${output}"
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good" "${dest}${previous_suffix}"
    printf '%s\n' "step 80 failed BEFORE it wrote anything to ${dest} — the render it was given does not exist —
and the served vhost changed anyway. Its output:
${output}
The interrupt guard restored a path the step had never written to, so a leftover rollback copy from
an earlier run replaced a vhost that was serving the panel, under the words 'as it was'."
    return
  fi
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after a step-80 failure before the swap, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi

  printf '\n%s on;\n' "$broken" >> "$PANEL_VHOST"

  # 2. A BROKEN VHOST OVER A GOOD ONE. Refused, rolled back byte-for-byte, service untouched.
  forget_nginx_service_records
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "step 80 INSTALLED a panel vhost that nginx cannot load, over a working one. Its own output:
${output}
This is the staging-name defect: the validation parsed a tree the candidate was not in."
    return
  fi
  case "$output" in
    *"$broken"*) ;;
    *)
      rm -f "$good"
      printf '%s\n' "step 80 refused the broken vhost, but never named the directive nginx choked on
(${broken}), so it refused for some other reason and this case proves nothing:
${output}"
      return
      ;;
  esac
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost but did not put ${dest} back byte-for-byte. An operator is
left with a served vhost that no run of this installer ever validated."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost and left an nginx that no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost and touched the nginx service anyway — the polygon's systemctl
recorded: ${records}. A refusal that still reaches the service is the failure this gate exists to
prevent, and a RELOAD is the sharp end of it: it is how a vhost 'nginx -t' has not seen reaches a
running server, and the restart the step falls back to when a reload fails stops an nginx that
cannot come back up."
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "the refused vhost is still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)
A rendered candidate or a rollback copy left in the tree is a file the next include pattern
change turns into a live configuration nobody validated."
    return
  fi

  # 2b/2c. A REFUSED RE-INSTALL ON A HOST SOMETHING ELSE HAS ALREADY BROKEN — the state in which
  #        the round-3 step DELETED the working panel vhost it found and told the operator the
  #        fault lay elsewhere.
  #
  #        This is one of the commonest reasons to re-run an installer: a third party's file under
  #        conf.d does not parse, so `nginx -t` fails for a reason that has nothing to do with the
  #        panel vhost. Round 3 chose which rollback copy to take by asking that question — "does
  #        the tree load" — instead of "is this file one I wrote". With any `<dest>.previous` on
  #        the host it therefore took NO copy of the served vhost at all, overwrote it, had its own
  #        render refused, declined the leftover because the tree still did not load, and left the
  #        served path ABSENT. Measured on alma9: the working panel vhost that was serving before
  #        the step ran existed nowhere on the machine afterwards, under the words "the
  #        configuration at fault is not one this step wrote".
  #
  #        Case 1c builds this host and stops one character short of the defect: it fails the run
  #        before the swap, so the guard's `swapped` is 0 and the restoration returns without ever
  #        reaching the arm that breaks. These two reach it, with a render that IS swapped in and
  #        IS refused, once for each kind of leftover — one the step cannot account for (2b) and
  #        one it can (2c) — because round 3 loses the served vhost on both.
  #
  #        What is asserted is the entry state: whatever else the step does, ${dest} afterwards is
  #        byte-for-byte the vhost that was serving when it started. That is the whole contract of
  #        a rollback, and it is the one thing a step may not trade away for a tidier message.
  local backup
  for backup in stranger ours; do
    printf 'a_directive_belonging_to_nobody on;\n' > "$intruder"
    cp -p "$good" "${dest}${previous_suffix}"
    printf '\n# an operator backup, or what a killed run left\n' >> "${dest}${previous_suffix}"
    # `stranger` leaves the appended line unaccounted for, so vhost_is_ours is FALSE — an
    # operator's own `maran.conf.previous`. `ours` re-stamps it, so the predicate is TRUE and the
    # step holds it aside as its first choice on a rollback. Round 3 asked neither question and
    # destroyed the served vhost in both.
    if [ "$backup" = ours ]; then
      stamp_as_maran_vhost "${dest}${previous_suffix}" "$marker_prefix"
    fi
    forget_nginx_service_records
    status=0
    output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
    rm -f "$intruder"
    if [ "$status" -eq 0 ]; then
      rm -f "$good"
      printf '%s\n' "step 80 INSTALLED a panel vhost nginx cannot load, on a host whose configuration was
already broken by a file that is not the panel's (leftover: ${backup}):
${output}"
      return
    fi
    # The premise: a run that failed before the swap never reaches the arm this case is about,
    # which is exactly how case 1c passes today while the defect is live.
    case "$output" in
      *"was not written to at all"*)
        rm -f "$good"
        printf '%s\n' "this case cannot test what it names (leftover: ${backup}): step 80 stopped BEFORE it wrote
anything to ${dest}, so the restoration returned without reaching the arm that chooses a rollback
copy. Its output:
${output}"
        return
        ;;
    esac
    if [ ! -e "$dest" ]; then
      rm -f "$good"
      printf '%s\n' "step 80 re-ran on a host that some OTHER file under conf.d had already broken (leftover:
${backup}), refused its own render — and left ${dest} with no file at all. Its output:
${output}
The panel vhost that was serving before the step ran is now nowhere on this machine. The step
decided which rollback copy to take by asking whether 'nginx -t' passes on the host, which is a
question about the whole tree and not about ${dest}, so it never copied the served file before
overwriting it. 'Is this file mine' and 'does the tree load' are two different questions."
      return
    fi
    if ! cmp -s "$good" "$dest"; then
      rm -f "$good"
      printf '%s\n' "step 80 re-ran on a host that some OTHER file under conf.d had already broken (leftover:
${backup}), refused its own render, and left ${dest} holding something that is not the vhost it
found there. Its output:
${output}"
      return
    fi
    if ! nginx -t >/dev/null 2>&1; then
      rm -f "$good"
      printf '%s\n' "with the intruding file removed again (leftover: ${backup}), nginx still does not load after
step 80's refusal:
$(nginx -t 2>&1)"
      return
    fi
    records="$(nginx_service_records)"
    if [ -n "$records" ]; then
      rm -f "$good"
      printf '%s\n' "step 80 refused on an already-broken host (leftover: ${backup}) and touched the nginx service
anyway — the polygon's systemctl recorded: ${records}."
      return
    fi
    if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
      rm -f "$good"
      printf '%s\n' "after a refusal on an already-broken host (leftover: ${backup}), the vhost nginx rejected is
still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
      return
    fi
    rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
    find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  done

  # 3. AN INTERRUPT INSIDE THE SWAP WINDOW — the candidate renamed onto the served path, the
  #    validation running, and the step's shell killed. Nothing RETURNS, so a rollback written
  #    only on the paths that return does not happen at all.
  #
  #    This is the price of validating after the swap, and it has to be paid rather than argued
  #    away. Measured on both families before the step grew its trap: exit 143, the
  #    never-validated vhost left at the served path permanently, `nginx -t` then [emerg], and
  #    nginx refusing to start — which, because this same nginx serves every customer site
  #    through maran-sites.conf, is every site on the box at the next restart or reboot, not
  #    just the panel. The old staging-name version left `maran.conf.staging`, a file nginx
  #    never opens, so this was a failure the fix INTRODUCED and only a guard removes.
  #
  #    The case is here rather than at the end because it must leave the host exactly as it
  #    found it — that is what it asserts — so the two cases below still start from the served
  #    path holding the vhost case 1 installed.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" TERM
  status=0
  output="$(run_nginx_step 'step_nginx' "$shim" 2>&1)" || status=$?
  rm -rf "$shim"
  if [ "$status" -eq 0 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was killed in the middle of its own validation and still reported SUCCESS:
${output}
Whatever it left on the served path, no run of this installer ever validated it."
    return
  fi
  # The same premise case 1b carries, and for the same measured reason: the shim fires once, and a
  # step that stops before the swap satisfies every check below without ever arming the guard.
  case "$output" in
    *"was not written to at all"*)
      rm -f "$good"
      printf '%s\n' "this case cannot test what it names: step 80 stopped BEFORE it wrote anything to ${dest}, so
the interrupt landed outside the swap window and the guard was never armed. Every check below would
pass on a step with no guard at all. Its output:
${output}"
      return
      ;;
  esac
  if [ "$status" -ne 143 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was sent SIGTERM in the middle of its validation and exited ${status} rather than 143.
The guard restored the served path but did not die of the signal it was sent, so the status the
installer hands its parent no longer says what happened — and the step's own handler is one line
away from resuming an install that was interrupted. Its output:
${output}"
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted (SIGTERM) between the swap and the end of its validation, and ${dest}
is no longer the vhost that was there before it ran — it holds ${broken} $(grep -cF "$broken" "$dest" || true)
time(s). Nothing on this host will put it back: the panel and every customer site this nginx serves
go down at the next restart or reboot. A rollback that runs only on the paths that RETURN cannot
see this one, which is why ops::safe_write guards its swap with a Drop rather than an \`if\` at each
error path (agent/crates/ops/src/safe_write/rollback_guard.rs)."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 was interrupted during its validation, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after an interrupted install, the vhost nothing validated is still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "an interrupted install left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)
The restoration takes them with it precisely so that the next run does not have to guess which of
two files on the served path a human should trust."
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted during its validation and touched the nginx service anyway — the
polygon's systemctl recorded: ${records}. An interrupted install must leave the running server
exactly where it found it."
    return
  fi

  # 4. AN INSTALL KILLED BY THE ONE SIGNAL NO TRAP CAN CATCH, AND THE RUN THAT COMES AFTER IT.
  #
  #    SIGKILL — `kill -9`, an OOM kill, a container stop that runs out of grace — leaves the
  #    never-validated candidate at the served path and the last vhost this installer validated
  #    at <dest>.previous. Nothing in step 80 can prevent that half. What it can do, and what
  #    this case is about, is the NEXT run: it must treat that leftover copy as the good one.
  #
  #    The step used to delete both working files at the top of every run and then back up
  #    whatever was on the served path. After a killed install that is exactly backwards: the
  #    only validated copy on the machine was deleted, the file that replaced it was the one
  #    nothing had ever validated, and a refusal then "rolled back" to it. The host was left
  #    serving a vhost nginx cannot load, by a step whose whole promise is the opposite.
  #    One artefact of this case outlives it and is left deliberately: the killed run cannot
  #    remove the vhost it had rendered into /tmp, so a 0600 root-owned copy of it stays in the
  #    image. That is what a SIGKILLed install leaves on a real host too, and removing it here
  #    would mean this script tidying up after a process it deliberately killed.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" KILL
  if output="$(run_nginx_step 'step_nginx' "$shim" 2>&1)"; then
    rm -rf "$shim"
    rm -f "$good"
    printf '%s\n' "step 80 was killed with SIGKILL in the middle of its validation and still reported SUCCESS:
${output}"
    return
  fi
  rm -rf "$shim"
  # The premise, asserted rather than assumed: if SIGKILL did not leave the host in the state
  # this case is about, everything below it would pass while testing nothing.
  if cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "this case cannot test what it names: after SIGKILL inside the validation, ${dest} still holds
the previously validated vhost, so no run of step 80 ever meets the leftover state below. Either the
kill missed the window or the step no longer swaps before it validates."
    return
  fi
  if ! cmp -s "$good" "${dest}${previous_suffix}"; then
    rm -f "$good"
    printf '%s\n' "this case cannot test what it names: after SIGKILL, ${dest}${previous_suffix} is not the vhost
that was validated before it. The next run has no good copy to prefer, so the check below would pass
for the wrong reason."
    return
  fi
  # And now the run that comes after it, with the template still unloadable, so that the run has
  # to roll back and the file it rolls back TO is the whole question.
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  case "$output" in
    *"$broken"*) ;;
    *)
      rm -f "$good"
      printf '%s\n' "the run after a killed install refused, but never named the directive nginx choked on
(${broken}), so it refused for some other reason and this case proves nothing:
${output}"
      return
      ;;
  esac
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install rolled back to the WRONG file: ${dest} now holds ${broken}
$(grep -cF "$broken" "$dest" || true) time(s) instead of the last vhost this installer validated. The
run deleted ${dest}${previous_suffix} — the only validated copy on the machine — took its backup from
the never-validated file the killed run had left on the served path, and restored that. An operator
who interrupts an install and re-runs it is left serving a configuration nginx refuses."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after the run that followed a killed install, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install refused and touched the nginx service anyway — the polygon's
systemctl recorded: ${records}."
    return
  fi

  # 4b. A ROLLBACK COPY THAT DOES NOT PARSE — the file the restoration is about to serve, checked.
  #
  #     Case 4 establishes that the run after a killed install prefers `<dest>.previous` to the
  #     never-validated file on the served path. This is the other half of that sentence: the step
  #     did not write that copy, did not watch it being written, and cannot assume it is whole. A
  #     `cp` interrupted by a hard reset leaves a truncated file — the ordinary ext4 outcome — and
  #     the previous version of this step adopted it, moved it onto the served path, and printed
  #     "is as it was before this step ran". Measured on both families: `nginx -t` afterwards was
  #     `[emerg] unexpected end of file`, and nginx would not start. That is the whole box at the
  #     next reboot, by way of the code written to prevent exactly that.
  #
  #     The state is produced rather than described: SIGKILL inside the validation, which leaves
  #     the candidate served and the validated vhost at `.previous`, and then that copy truncated
  #     to the first 3000 of its bytes.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" KILL
  run_nginx_step 'step_nginx' "$shim" >/dev/null 2>&1 || true
  rm -rf "$shim"
  if [ ! -e "${dest}${previous_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "this case cannot test what it names: after SIGKILL inside the validation there is no
${dest}${previous_suffix} for it to truncate, so the step never meets a rollback copy it did not write."
    return
  fi
  local fragment
  fragment="$(mktemp)"
  head -c 3000 "${dest}${previous_suffix}" > "$fragment"
  cp -p "$fragment" "${dest}${previous_suffix}"
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "$fragment"
    printf '%s\n' "the run after a killed install INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  if [ -e "$dest" ] && cmp -s "$fragment" "$dest"; then
    rm -f "$good" "$fragment"
    printf '%s\n' "step 80 served a rollback copy it never read. ${dest} now holds the truncated
${dest}${previous_suffix} — 3000 bytes of a vhost that does not parse — and the step said so in these
words:
${output}
nginx will not START with that file, and this nginx serves every customer site on the host through
maran-sites.conf, so the whole box goes at the next reboot. A restoration that installs an
unvalidated file is worse than none: the operator has been told the machine is as they left it."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good" "$fragment"
    printf '%s\n' "after a refusal whose rollback copy does not parse, nginx no longer loads:
$(nginx -t 2>&1)
The step's job there is to leave a host that BOOTS: the panel unreachable is a loss, an nginx that
will not start is an outage of every site on the machine."
    return
  fi
  # The step must SAY which file it declined and why. It refuses the fragment twice over now: the
  # marker its own render_vhost stamps does not match a truncated file, so it is parked without ever
  # reaching the served path at all, and the never-validated candidate it finds on the served path
  # is then tried and refused by `nginx -t`. Either sentence is a naming of the file at fault; a
  # silent decline is not.
  case "$output" in
    *"does not parse"* | *"not a panel vhost this installer wrote"*) ;;
    *)
      rm -f "$good" "$fragment"
      printf '%s\n' "step 80 declined to serve a rollback copy it could not account for — correctly — but never
said so. Its output was:
${output}
The operator is left with no panel vhost and no sentence naming the file that caused it."
      return
      ;;
  esac
  # The declined copy is still ON THE MACHINE. A step that made the host boot by DELETING the only
  # other copy of a panel vhost is the round-3 defect this suite's newest case is about, and a
  # truncated file is still evidence an operator may want.
  if [ -z "$(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)" ]; then
    rm -f "$good" "$fragment"
    printf '%s\n' "step 80 declined the rollback copy it could not account for and then made it disappear: there
is no ${dest}${foreign_suffix}* on the host. A file the step did not write is not a file the step may
delete, and the operator has been left with neither a panel vhost nor the copy that caused it."
    return
  fi
  case "$output" in
    *"already broken before this step ran"* | *"was already failing before"*)
      rm -f "$good" "$fragment"
      printf '%s\n' "step 80 told the operator their host's nginx was already broken, about a file the step
itself installed:
${output}"
      return
      ;;
  esac
  rm -f "$fragment"
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4c. A LEFTOVER HELD ASIDE AND NEVER HANDED BACK — the state this round's own fix creates.
  #
  #     Preferring a validated leftover to the entry bytes means the two cannot share one name, so
  #     the step moves the leftover to `<dest>.adopted` and writes its own copy of the served file
  #     to `<dest>.previous`. That introduces a window of its own: a run killed between those two
  #     renames leaves the last validated panel vhost under a name the NEXT run has never had to
  #     think about. Treated as this step's own scratch it would be deleted on sight — which is
  #     round 2's defect (the only validated copy on the machine, removed by the run that came to
  #     help) reappearing under a new name. The next run promotes it back instead.
  #
  #     This case exists because the fix for the finding above is the kind of change that leaves a
  #     smaller version of the same bug behind, which is what the three rounds before this one did.
  cp -p "$good" "${dest}${adopted_suffix}"
  printf '\n%s on;\n' "$broken" >> "$dest"
  forget_nginx_service_records
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "the run after an install killed while it held a leftover aside INSTALLED a vhost nginx cannot
load:
${output}"
    return
  fi
  if [ ! -e "$dest" ] || ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "an install was killed between holding the last validated panel vhost aside at
${dest}${adopted_suffix} and handing it back, and the run that followed did not pick it up: ${dest}
$([ -e "$dest" ] && echo 'holds something else' || echo 'does not exist'). Its output:
${output}
A file the step moved aside is not the step's scratch to delete on the next run — that is round 2's
defect, which removed the only validated copy on the machine, wearing the name this round's own fix
introduced."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after the run that followed an install killed mid-hand-aside, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "that run left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)"
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after that run, the vhost nginx rejected is still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
    return
  fi

  # 4d. A SYMLINK UNDER ONE OF THIS STEP'S WORKING NAMES, pointing at a stamped panel vhost
  #     OUTSIDE /etc/nginx. The step must not follow it, must not serve it, and must not delete
  #     the entry copy on the strength of having accounted for it.
  #
  #     `[ -f ]` is not "is a regular file": it FOLLOWS the link and is true of any symlink
  #     resolving to one. vhost_is_ours tested exactly that for four rounds while its doc comment
  #     said regular file, so a link at `<dest>.previous` was accounted for, adopted, renamed onto
  #     the served path, and the entry copy of the vhost that was actually serving was deleted
  #     afterwards. Measured on both families before the fix, with the served path ending as
  #     `maran.conf -> /root/hidden.conf` and the step announcing that it held "the last panel
  #     vhost an install of this product validated".
  #
  #     Three things are asserted, because the harm is three-fold. The served path is never a
  #     symlink — delete the target, or boot with its filesystem unmounted, and nginx does not
  #     START, which is every customer site on this host through maran-sites.conf. The `nginx -t`
  #     proof stays a statement about bytes rather than about a name: with a link on the served
  #     path, what nginx loaded was the target at one instant and anything that can write the
  #     target afterwards changes what is served, which is the "the file validated and the file
  #     served are two different files" state case 1 exists to forbid. And the entry bytes survive.
  #
  #     The link's target is checked too: this step is not entitled to consume, rewrite or remove
  #     a file an operator put outside the directory the installer owns.
  local outside="/root/a-panel-vhost-outside-etc-nginx.conf"
  cp -p "$good" "$outside"
  # The premise, asserted rather than assumed: the target IS a file the step accounts for. If it
  # were not, the case below would pass because the marker did not match and would say nothing at
  # all about symlinks.
  if ! run_nginx_step "vhost_is_ours '${outside}'"; then
    rm -f "$good" "$outside"
    printf '%s\n' "this case cannot test what it names: ${outside} is a copy of the vhost step 80 itself
rendered and vhost_is_ours does not account for it, so a symlink pointing at it would be declined
for the wrong reason."
    return
  fi
  printf '\n%s on;\n' "$broken" >> "$dest"
  ln -s "$outside" "${dest}${previous_suffix}"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "$outside"
    printf '%s\n' "with a symlink at ${dest}${previous_suffix}, step 80 INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  if [ -L "$dest" ]; then
    local target
    target="$(readlink -f "$dest" 2>/dev/null || true)"
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 followed a symlink it found under one of its own working names and made the SERVED
path a link out of the directory it owns: ${dest} -> ${target}. Its output:
${output}
Delete that target, or boot with its filesystem unmounted, and nginx does not START — this nginx
serves every customer site on the host through maran-sites.conf. And the 'nginx -t' the step ran is
no longer a statement about the bytes it serves: it is a statement about whatever the link resolved
to at one instant, which anything that can write that path may change afterwards. That is the
'the file validated and the file served are two different files' state this suite's first case
exists to forbid, reached through a predicate that says 'regular file' and tests '[ -f ]'."
    return
  fi
  if [ ! -e "$outside" ] || ! cmp -s "$good" "$outside"; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 met a symlink under one of its working names and then consumed, rewrote or removed
what it pointed AT: ${outside} $([ -e "$outside" ] && echo 'has changed' || echo 'is gone'). A file
outside the directory this step owns is not this step's to touch, whatever name inside the directory
happens to point at it."
    return
  fi
  if [ ! -e "${dest}${previous_suffix}" ] || ! grep -qF "$broken" "${dest}${previous_suffix}"; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 met a symlink at ${dest}${previous_suffix}, declined to serve it — correctly — and
still destroyed the bytes that were on the served path when it started: they are not at
${dest}${previous_suffix}. Its output:
${output}
The entry copy is taken before anything is touched precisely so that no predicate about a LEFTOVER
can cost the operator the vhost that was serving."
    return
  fi
  if [ -z "$(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)" ]; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 declined the symlink at ${dest}${previous_suffix} but did not park it: there is no
${dest}${foreign_suffix}* on the host. A name this step only borrows, occupied by something it
cannot account for, is moved aside and named to the operator — never deleted, never followed."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good" "$outside"
    printf '%s\n' "after step 80 met a symlink under one of its working names, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 refused with a symlink under one of its working names and touched the nginx service
anyway — the polygon's systemctl recorded: ${records}."
    return
  fi
  rm -f "$outside"
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4f. A DANGLING SYMLINK UNDER ONE OF THIS STEP'S WORKING NAMES — the intersection of case 1b
  #     and case 4d, which is exactly where five rounds of this suite had no coverage.
  #
  #     Case 1b names the harm — an install that finished and destroyed an operator's own backup at
  #     `<dest>.previous` — and asserts it for a REGULAR FILE. Case 4d asserts the symlink handling,
  #     and its link has a target that EXISTS. Each reads like coverage of this name; between them
  #     they left the one state where the file is a symlink AND its target is gone, and the code
  #     failed there for five rounds while both cases passed. Two green cases whose union looks
  #     total are worth less than either of them looks: the predicate that decides which of them a
  #     run takes — `[ -e ]` — is precisely the one that answers differently for a dangling link.
  #
  #     `[ -e ]` RESOLVES the link, so it is false for a dangling one and the whole leftover branch
  #     is skipped: vhost_is_ours is never asked, park_foreign_file is never reached, nothing is
  #     printed, and the unconditional entry copy then renames onto that path — `rename(2)` replaces
  #     the SYMLINK, not its absent target. Measured before the fix on both families: no `.foreign`
  #     anywhere, the link gone, and the step exiting 0 with the ordinary success line.
  #
  #     The state is the ordinary one, not a contrivance: `maran.conf.previous` is the name a human
  #     picks for a backup, and a backup pointer into a volume that is later unmounted, or at a file
  #     later deleted, is a dangling link. Nothing about it is visible to the operator afterwards,
  #     which is why the install must say the name it moved it to.
  local nowhere="/mnt/a-volume-that-is-not-mounted/panel-backup.conf"
  [ ! -e "$nowhere" ] || rm -f "$nowhere"
  ln -s "$nowhere" "${dest}${previous_suffix}"
  # The premise, asserted rather than assumed: a link that RESOLVES is case 4d's state and would
  # test nothing new here.
  if [ -e "${dest}${previous_suffix}" ] || [ ! -L "${dest}${previous_suffix}" ]; then
    rm -f "$good" "${dest}${previous_suffix}"
    printf '%s\n' "this case cannot test what it names: ${dest}${previous_suffix} is not a DANGLING symlink, so it
is case 4d's state over again and says nothing about the gate that decides whether case 4d's
handling is reached at all."
    return
  fi
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  local parked_link
  parked_link="$(find "$(dirname "$dest")" -maxdepth 1 \
    -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null | head -n 1)"
  if [ -z "$parked_link" ] || [ ! -L "$parked_link" ]; then
    rm -f "$good"
    rm -f "${dest}${previous_suffix}"
    printf '%s\n' "step 80 met a DANGLING symlink at ${dest}${previous_suffix} and did not park it: there is no
symlink under ${dest}${foreign_suffix}* on this host. It exited ${status} saying:
${output}
The link is gone — the entry copy was renamed straight onto it, and rename(2) replaces the link
itself. That is case 1b's harm — an install that finished and destroyed an operator's own backup at
<dest>.previous — reached through the one input case 1b and case 4d do not have between them: 1b
uses a regular file, 4d uses a link whose target exists, and the test [ -e ] resolves the link, so a
dangling one skips the branch that both of those cases exercise."
    return
  fi
  if [ "$(readlink "$parked_link")" != "$nowhere" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 parked the dangling symlink at ${dest}${previous_suffix} but not as it found it: it now
points at $(readlink "$parked_link") rather than at ${nowhere}. A link an operator made is moved,
whole, or it is not preserved at all."
    return
  fi
  case "$output" in
    *"$parked_link"*) : ;;
    *)
      rm -f "$good"
      printf '%s\n' "step 80 moved an operator's dangling symlink to ${parked_link} and never said so:
${output}
A file moved aside under a name the operator chose is only 'left alone' if they are told where it
went; the pointer is the whole of what a dangling link is worth."
      return
      ;;
  esac
  # The render is refused here, because the template has carried `$broken` since case 2 and this
  # case runs among the others that need it. That is not a weaker test of the same thing: the
  # destroying rename is write_rollback_copy's, which happens BEFORE the swap and therefore on the
  # refusing path too. What the refusal adds is the second half — the entry bytes come back with an
  # operator's link parked beside them rather than consumed on the way.
  if [ "$status" -eq 0 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 INSTALLED a vhost nginx cannot load while a dangling symlink sat at
${dest}${previous_suffix}:
${output}"
    return
  fi
  if [ -L "$dest" ] || ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 met a dangling symlink at ${dest}${previous_suffix}, refused its own render — correctly
— and did not put the served path back: ${dest} is $([ -L "$dest" ] && echo 'a symlink' || echo 'not
the vhost that was there'). The entry copy is taken before anything is touched precisely so that no
predicate about a LEFTOVER can cost the operator the vhost that was serving, and a leftover that is
a dangling link is still a leftover."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 met a dangling symlink under one of its working names, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4g. A DANGLING SYMLINK ON THE SERVED PATH ITSELF, which is the same defect one name over.
  #
  #     `<dest>` is not one of the three working names — it is the name this step exists to write —
  #     and the gate that decides whether the entry copy is taken asked `[ -e ]` about it too. So a
  #     link there was consumed exactly as a link at `<dest>.previous` was: `install` follows it and
  #     copies the TARGET's bytes into the rollback copy, the swap's `mv -f` then replaces the LINK,
  #     and a DANGLING one is not even looked at, because `[ -e ]` resolved it to nothing. Measured
  #     before the fix on both families: no `.foreign` anywhere, `maran.conf` a regular file, and
  #     the operator's pointer gone with no line naming it.
  #
  #     What the step does now is what claim_working_name does for `<dest>.candidate` and
  #     vhost_is_ours does for `<dest>.previous`: park the link, name it, and write a regular file.
  #     No entry copy is taken — there is nothing of this step's to copy — so what is asserted here
  #     is the link's survival and the operator being told, not a restoration.
  rm -f "$dest"
  ln -s "$nowhere" "$dest"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  parked_link="$(find "$(dirname "$dest")" -maxdepth 1 \
    -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null | head -n 1)"
  if [ -z "$parked_link" ] || [ ! -L "$parked_link" ] || [ "$(readlink "$parked_link")" != "$nowhere" ]; then
    rm -f "$good"
    rm -f "$dest"
    printf '%s\n' "step 80 found a dangling symlink on the SERVED path and destroyed it: there is no symlink to
${nowhere} under ${dest}${foreign_suffix}* on this host. It exited ${status} saying:
${output}
The served path is this step's to write, not this step's to consume: an operator's link there is
moved aside and named, like anything else under a name this step needs and did not put there."
    return
  fi
  if [ -L "$dest" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 left a symlink on the served path at ${dest}. Nothing this step writes is ever a link
out of the directory it owns — case 4d's whole argument."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 met a dangling symlink on the served path, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  rm -f "$dest"
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4e. A MARKER-ONLY FILE — a stamp that agrees with itself and attests nothing.
  #
  #     `tail -n +2` on a file consisting of the marker line ALONE yields nothing, and the SHA-256
  #     of nothing is a perfectly well-formed digest. So a 72-byte residue carrying it satisfied
  #     every question vhost_is_ours asked: the predicate agreed with itself and was still wrong.
  #     Measured on both families before the fix — the file was adopted, `nginx -t` passed with it
  #     (one comment line is valid nginx), it was left SERVED, the entry copy was deleted, and the
  #     operator was told ${dest} held "the last panel vhost an install of this product validated"
  #     while it held no `server` block and no `listen` at all: the panel unreachable, under a
  #     sentence asserting the opposite.
  #
  #     The state is not exotic in origin — a crash truncates on block boundaries — but the defect
  #     is in the predicate, so it is produced directly. The lower bound the step now applies is
  #     the vhost's own shape, the `listen` line, which no marker-only residue can have.
  printf '\n%s on;\n' "$broken" >> "$dest"
  printf '%s%s\n' "$marker_prefix" "$(printf '' | sha256sum | cut -d' ' -f1)" \
    > "${dest}${previous_suffix}"
  local hollow
  hollow="$(mktemp)"
  cp -p "${dest}${previous_suffix}" "$hollow"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "$hollow"
    printf '%s\n' "with a marker-only rollback copy present, step 80 INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  if [ -e "$dest" ] && cmp -s "$hollow" "$dest"; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 served a file that is nothing but this step's own provenance marker. ${dest} now
holds $(wc -c < "$dest") bytes with no 'server' block and no 'listen' line, and the step said:
${output}
'nginx -t' passes with it, because one comment under conf.d is valid nginx — which is exactly why
the digest cannot be the whole question. The SHA-256 of an empty body is a real digest, so the stamp
agreed with itself; the panel is unreachable and the vhost that was serving has been deleted on the
strength of it."
    return
  fi
  if [ -e "$dest" ] && ! grep -q -e '^[[:space:]]*listen[[:space:]]' "$dest"; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 left ${dest} holding a file with no 'listen' line at all:
$(cat "$dest")
Whatever else is true of a panel vhost, it listens on the panel's port; a served file that does not
is a panel nobody can reach, reported as an install that rolled back cleanly."
    return
  fi
  if [ ! -e "${dest}${previous_suffix}" ] || ! grep -qF "$broken" "${dest}${previous_suffix}"; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 declined the marker-only rollback copy — correctly — and still destroyed the bytes
that were on the served path when it started: they are not at ${dest}${previous_suffix}. Its output:
${output}"
    return
  fi
  if [ -z "$(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)" ]; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 declined the marker-only rollback copy and then made it disappear: there is no
${dest}${foreign_suffix}* on the host. A file the step cannot account for is moved aside, not
deleted — it is evidence, and it is not this step's to remove."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good" "$hollow"
    printf '%s\n' "after step 80 met a marker-only rollback copy, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 refused with a marker-only rollback copy present and touched the nginx service anyway
— the polygon's systemctl recorded: ${records}."
    return
  fi
  rm -f "$hollow"
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 5. A BROKEN VHOST ON A HOST THAT HAS NONE — the FIRST install, where there is nothing to roll
  #    back to and the step must leave the served path empty rather than occupied.
  rm -f "$dest"
  forget_nginx_service_records
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "on a host with no panel vhost at all, step 80 INSTALLED one that nginx cannot load:
${output}"
    return
  fi
  if [ -e "$dest" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost on a first install and left it at ${dest} anyway:
$(cat "$dest")"
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 refused a first install, nginx no longer loads at all:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 refused a first install and touched the nginx service anyway — the polygon's systemctl
recorded: ${records}."
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after a refused first install, the broken vhost is still under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
    return
  fi
  rm -f "$good"

}

# assert_the_panel_certificate_is_never_written_through_a_symlink: step 80 generates a self-signed
# certificate only when there is none, and "none" must be a question about the PATH, not about what
# a link at that path resolves to.
#
# `[ -f ]` follows the link. So the gate that exists to leave an operator's own certificate alone
# answered about the TARGET, and for a link whose target has gone it answered "there is nothing
# here" — after which `openssl req -keyout/-out` writes THROUGH the link and creates this
# installer's throwaway key and certificate at whatever path the link names. The state is this
# function's own documented case: an operator who swapped in a real certificate, which for a
# renewing ACME client means symlinks at exactly these two names, and a renewal that moved the
# archive or a lineage that was removed leaves them dangling. Measured on both families before the
# fix: a fresh private key written into /etc/letsencrypt/live/panel/, silently, by an installer
# whose whole doctrine elsewhere is that it never writes through a link out of the directory it
# owns — here with key material, which is the one file where a wrong write cannot be undone by
# writing again.
#
# It is the same defect as the vhost gates', asked in the direction of ABSENCE rather than of type,
# and it is in this file because the previous rounds each fixed one instance of it and left another.
#
# It runs AFTER assert_nginx_vhost_is_validated_before_it_is_served, which is not cosmetic: the
# function under test creates its TLS directory `-g` the service group, so without that group the
# prepare step makes, the certificate this one puts back cannot be written and the vhost assertion
# would fail on a missing `ssl_certificate` for a reason of this assertion's making.
assert_the_panel_certificate_is_never_written_through_a_symlink() {
  local cert key lineage outcome=""

  cert="$(run_nginx_step 'printf %s "$MARAN_CERT_PATH"')"
  key="$(run_nginx_step 'printf %s "$MARAN_KEY_PATH"')"
  lineage="$(mktemp -d)"
  rm -f "$cert" "$key"
  mkdir -p "$(dirname "$cert")"
  ln -s "${lineage}/fullchain.pem" "$cert"
  ln -s "${lineage}/privkey.pem" "$key"

  run_nginx_step 'generate_self_signed_cert' >/dev/null 2>&1 || true

  if [ -e "${lineage}/privkey.pem" ] || [ -e "${lineage}/fullchain.pem" ]; then
    outcome="step 80 wrote a certificate THROUGH an operator's symlink: ${lineage} now holds
$(ls -1 "$lineage"). A dangling link at ${key} is not 'no certificate here' — the test [ -f ] follows the
link and answers about the target, while the thing that gate protects is the path. What was
written is a private key, into a directory an ACME client owns."
  elif [ ! -L "$cert" ] || [ ! -L "$key" ]; then
    outcome="step 80 replaced an operator's certificate symlink at ${cert} or ${key} with a file of
its own. A link under either name says a certificate of the operator's is in place; this step
neither follows it nor takes the name."
  fi

  # The links go, and a real certificate goes back: the assertion after this one installs the panel
  # vhost and runs `nginx -t`, which refuses a configuration whose `ssl_certificate` file is not
  # there. Restoring it through the step's own function rather than by hand keeps this assertion
  # from becoming a second authority on where that certificate lives.
  rm -f "$cert" "$key"
  rm -rf "$lineage"
  run_nginx_step 'generate_self_signed_cert' >/dev/null 2>&1 || true
  [ -z "$outcome" ] || fail "$outcome"
  { [ -f "$cert" ] && [ -f "$key" ]; } \
    || fail "this assertion did not put a panel certificate back at ${cert} / ${key}; the vhost
assertion after it would fail on a missing ssl_certificate for a reason of this one's making."

  echo "Step 80 leaves a certificate symlink alone and never writes a key through one, including when the"
  echo "link dangles — the state a renewing ACME client leaves after a lineage is removed."
}

# assert_the_vhost_swap_reports_a_failed_rename: step 80's move_into_place must return the status of
# the RENAME, not of the flush that follows it.
#
# Every swap of the served path and every restoration goes through that one helper. It has SEVEN
# call sites, not four, and they were counted rather than remembered: three test the return value
# with `|| return 1` (try_restore_from's forward and reverse renames, and restore_panel_vhost's
# last arm), and FOUR are bare — in write_rollback_copy, where it is the last command so its status
# IS the function's; at the promotion of `<dest>.adopted` back to `<dest>.previous`; at the
# adoption that moves `<dest>.previous` aside; and at the swap itself. A bare call is not a
# swallowed one: `set -e` aborts the shell on it, which at the first three is before the guard is
# armed and the served path is untouched, and at the swap runs the EXIT trap with
# MARAN_VHOST_GUARD_SWAPPED still 0, so the restoration correctly does nothing. The
# count is written out because it is what a reader will use to decide whether a NEW call site needs
# a guard, and the answer depends on which of those two shapes it is in.
#
# It used to end on `sync "$(dirname "$destination")"` and so returned THAT: the target directory
# always exists, the flush always succeeds, and all three guards were therefore decorative — a
# failed `mv` reported success.
# Measured before the fix, with the destination on a read-only filesystem: "RESULT: move_into_place
# returned 0 DESPITE mv failing", source still in place, destination never created.
#
# What that costs is not hypothetical at the one site where it is load-bearing. In try_restore_from
# a forward rename that silently did nothing is followed by the REVERSE rename, which then moves the
# refused candidate — sitting on the served path — onto `<dest>.previous`, the name holding the only
# copy of the entry bytes. `restore_panel_vhost`'s protective `[ ! -e "$previous" ]` is defeated by
# a name existing rather than by the bytes being right, and the served vhost is gone.
#
# THE CALL IS MADE IN A CONDITION, exactly as those three call sites make it, and that is the whole
# subtlety of the check: `set -e` is suppressed inside a function invoked in an `if` or a `||` list,
# so the failing `mv` does NOT abort and the return value is all there is. Called bare, the same
# mutant dies of `set -e` and the assertion would pass while the defect was live.
#
# The failure is produced without a mount or a privilege a build has no way to get: `mv` of a
# DIRECTORY onto an existing regular FILE is refused for root as for anybody ("cannot overwrite
# non-directory with directory"), while the parent directory the helper flushes afterwards is
# present and healthy — so a helper returning the flush's status returns 0 here and one returning
# the rename's returns non-zero. Directory-onto-directory would not do: `mv` moves the source
# INSIDE an existing directory and succeeds, which is why this assertion also checks that the
# rename it expected to fail did fail.
assert_the_vhost_swap_reports_a_failed_rename() {
  local scratch source destination answer

  scratch="$(mktemp -d)"
  source="${scratch}/source"
  destination="${scratch}/destination"
  mkdir -p "$source"
  : > "$destination"

  answer="$(run_nginx_step "if move_into_place '${source}' '${destination}' 2>/dev/null; then
printf SWALLOWED; else printf PROPAGATED; fi")"
  if [ ! -d "$source" ]; then
    rm -rf "$scratch"
    fail "this assertion cannot test what it names: the rename it expected to fail SUCCEEDED, so
move_into_place was never asked to report a failure."
  fi
  [ "$answer" = PROPAGATED ] \
    || fail "move_into_place returned success for a rename that failed (answer: ${answer:-none}). Its exit
status is the trailing directory flush's, and that flush cannot fail: the directory always exists.
So the three '|| return 1' guards on this helper test a value that is always 0 — including the one
in try_restore_from, where a forward rename that silently does nothing is followed by the reverse
one moving the REFUSED candidate onto the name holding the only copy of the entry bytes."

  rm -rf "$scratch"
  echo "move_into_place reports a failed rename as a failure, called the way its guarded call sites call it."
}

# assert_nginx_vhost_is_validated_before_it_is_served: step 80 must refuse to finish an install
# with a panel vhost nginx cannot load, and must finish one with a vhost it can.
#
# The defect this is written against, so that it cannot come back quietly: step 80 used to write
# the render to `maran.conf.staging` and then run `nginx -t`. Both families include
# `conf.d/*.conf` and nothing else, which `.staging` does not match — so the test parsed a tree
# the candidate was not in, printed "test is successful", and the step renamed a file nothing had
# ever read over the served path. A vhost with a syntax error installed clean and took the panel
# down at the next reload, with the successful `nginx -t` in the install log as evidence that it
# was fine. Measured on both polygon images before the fix.
#
# Both halves are asserted, because from the outside a gate that refuses everything and a gate
# that refuses nothing look the same, and only the pair tells them apart. That is also why the
# refusal cases require step 80 to NAME the directive nginx rejected: a refusal for some other
# reason — a missing binary, an absent group — would otherwise be read as the gate working.
#
# The third proposition, added after the fix for the defect above introduced a failure the defect
# did not have: an install KILLED inside the swap window leaves the served path exactly as it
# was. The step validates after the rename, which is the correct order and the one
# ops::safe_write uses, and the price of that order is a window in which a file no one has
# validated is the file nginx would read. A rollback written only on the paths that return does
# not close it; a trap does. Case 3 is the only check here that can tell the two apart.
#
# Case 4 is its other half, and it is about the one signal no trap catches. SIGKILL leaves the
# candidate on the served path and the last validated vhost under `.previous`; the step cannot
# prevent that, so what is asserted instead is that the NEXT run prefers that leftover copy to
# the file on the served path. The step used to delete it and back up the broken file in its
# place, which turned an interrupted install into a permanently broken one at the next refusal.
#
# The fourth: on every path that refuses, the nginx unit is neither enabled, reloaded nor
# restarted. This used to be read off the enablement file alone while the conclusion claimed the
# service was never touched — so a `systemctl reload nginx` inserted straight after the rename,
# and the step's own reload line moved inside the window, both passed green. The polygon's
# systemctl records a reload now, and nginx_service_records reads all three records under BOTH
# spellings of the unit: with the short name alone, the same mutants spelled `nginx.service` were
# green again, which is the same defect wearing a suffix.
#
# The fifth, and the reason this file grew three cases in its third round: the restoration is only
# worth having if what it restores has been checked. Case 4b truncates `<dest>.previous` — the
# ordinary outcome of a hard reset during the copy — and requires the step to refuse to serve it,
# to say so, and to leave a host on which nginx still starts. Case 1b interrupts the step with the
# SHIPPED template and requires exit 143, which is the only case here that can tell a guard that
# re-raises from one that restores and then carries on to report the panel reachable. Case 1c fails
# the step BEFORE the swap and requires the served path not to change at all, because a guard armed
# before the write must not put a rollback copy onto a file nothing has written to.
#
# It runs LAST in main: it is the only assertion here that writes into /etc/nginx and creates a
# system account, and putting it after the uninstaller cases keeps it out of the state they save
# and restore.
assert_nginx_vhost_is_validated_before_it_is_served() {
  local dest entry_copy="" template_backup outcome

  prepare_host_for_the_nginx_step

  dest="$(run_nginx_step 'nginx_conf_dest')"
  case "$dest" in
    /etc/nginx/*.conf) ;;
    *)
      fail "80-nginx.sh's nginx_conf_dest answered '${dest}'. Every case below drives the step by that
answer, and a destination that is not a .conf under nginx's own tree is a vhost nginx never reads."
      ;;
  esac

  # The host is left exactly as it was found. The image reaches here with no panel vhost, and the
  # two negative cases need to start from that state deliberately rather than by luck.
  if [ -e "$dest" ]; then
    entry_copy="$(mktemp)"
    cp -p "$dest" "$entry_copy"
  fi
  rm -f "$dest"

  template_backup="$(mktemp)"
  cp -p "$PANEL_VHOST" "$template_backup"

  outcome="$(nginx_gate_outcome "$dest")"

  # Put both back before anything is reported, so a failure here does not also break every check
  # that reads the shipped template afterwards.
  cp -p "$template_backup" "$PANEL_VHOST"
  rm -f "$template_backup"
  rm -f "$dest"
  if [ -n "$entry_copy" ]; then
    cp -p "$entry_copy" "$dest"
    rm -f "$entry_copy"
  fi

  [ -z "$outcome" ] || fail "$outcome"
  grep -q '__MARAN_API_SOCKET__' "$PANEL_VHOST" \
    || fail "this assertion did not put the shipped vhost template back the way it found it"
  nginx -t >/dev/null 2>&1 \
    || fail "this assertion did not put ${dest} back the way it found it; nginx no longer loads:
$(nginx -t 2>&1)"

  echo "Step 80 installs the shipped panel vhost, nginx confirms it reads the file that was installed,"
  echo "and a vhost nginx cannot load is refused — on a first install, over a working one, and when the"
  echo "step is killed in the middle of its own validation — with the previous configuration restored"
  echo "byte-for-byte, the step dying of the signal it was sent rather than resuming, and the nginx unit"
  echo "neither enabled, reloaded nor restarted, all three read from the polygon's systemctl under both"
  echo "spellings of the unit. A failure before the swap leaves the served path untouched. After a"
  echo "SIGKILL, which no trap can catch, the run that follows rolls back to the copy that was validated"
  echo "rather than to the one that was not — and refuses to serve that copy at all when it does not"
  echo "parse, leaving a host on which nginx still starts and saying which file it declined to install."
  echo "The vhost it installs is fsynced, and so is its directory, before the validation that commits"
  echo "the install — observed through a recording sync and nginx, in order. Whose file is whose is"
  echo "decided by the marker the step's own render stamps and not by the state of the host, so a"
  echo "re-install on a host some OTHER conf.d file has already broken still leaves the panel vhost it"
  echo "found byte-for-byte where it was, an operator's own backup under the step's working name"
  echo "survives an install that succeeds, and a directory under one of those names is moved aside"
  echo "rather than ending a successful install non-zero. That marker answers for BYTES and not for a"
  echo "name: a symlink under one of those names is parked rather than followed, so the served path is"
  echo "never a link out of the directory this step owns and what the link pointed at is left alone;"
  echo "and a file that is nothing but the marker — whose empty body has a perfectly valid digest — is"
  echo "parked too, rather than served as a panel vhost with no 'listen' line in it. A link whose target"
  echo "is GONE is parked on the same terms, at the working name and on the served path alike: a test"
  echo "that resolves the link answers about a target that is not there, so a dangling one used to be"
  echo "renamed over and lost without a word."
}

# THE PANEL VHOST'S ACCESS LOG, AND THE SECRET IT USED TO CARRY.
#
# The defect, measured rather than reasoned about
# (docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md): the vhost named
# `access_log <path>` with no format, so nginx used its built-in `combined`, which logs
# `"$request"` — the raw request line, query string included. The installer printed the operator a
# link reading `/setup?token=<48 hex characters>`, and that token is permission to become this
# server's administrator until the first one exists. Opening the link therefore wrote a live
# credential into /var/log/maran/nginx-access.log: a file inside a directory every uid in the
# panel's service group may read, which nothing rotated. rules/security.md item 8 forbids exactly
# that, by name, and two earlier passes over this branch recorded that it did not happen.
#
# WHY A CENSUS OVER `access_log` DIRECTIVES RATHER THAN A CHECK ON THE ONE THAT LEAKED. There is
# one access_log directive in the vhost today. A check written against that one directive is a
# check that stops being about anything the moment a `location` is given a log of its own — and a
# location with its own log is exactly how a per-endpoint log gets added, which is how this leak
# would come back. So every directive in the file is enumerated and each must either be `off` or
# name a format this file declares, and every format the file declares is enumerated and none may
# carry a variable that contains a query string. A directive added tomorrow is named by this
# assertion or it satisfies it; there is no third outcome, and neither of them is silence.
#
# AND WHY IT IS ALSO BEHAVIOURAL. The census is a fact about text, and what matters is a fact
# about nginx: rules/testing.md, a check must observe what it reports on. The variable list could
# be right and the claim still false — `$uri` is query-free and is REWRITTEN by `try_files`, so a
# format built on it logs `/index.html` for every SPA route and a reader would conclude the log
# names the page that was asked for when it does not. That was measured here, not assumed, and it
# is why the vhost maps the path out of `$request_uri` instead. So this assertion serves two real
# requests through this family's real nginx, with this family's real vhost, and reads the file
# nginx wrote.
#
# THE CONTROL RUNS FIRST AND IN THE OTHER DIRECTION. Before the shipped vhost is measured, the
# same two requests are served through a copy of it with the format name removed from the
# access_log line — which is, byte for byte, what shipped before this fix — and the token MUST
# appear. Without that half, "the token is not in the log" is satisfied by a probe that cannot see
# a token at all, by an nginx that never started, or by a request that never arrived. And in the
# other direction the shipped run requires the request to STILL BE THERE: a log that stopped
# recording /setup would pass a check for the token's absence while destroying the record an
# operator investigating an intrusion needs. Neither half is worth having without the other.
#
# WHAT THIS CANNOT SEE, stated rather than left to be discovered. nginx's ERROR log has no format
# and cannot be given one: for any request that errors, nginx writes `request: "<the raw request
# line>"` into it, query string and all. Nothing in this file, and nothing in the vhost, can close
# that — which is why the setup token no longer travels in a URL at all
# (installer/lib/90-finish.sh, held by the census in
# assert_the_finish_message_tells_the_truth_about_this_install) and why the access-log format is
# the second line of defence rather than the fix. This assertion also says nothing about a
# customer's site vhosts, which the agent renders from its own templates.
assert_the_panel_vhost_can_never_log_a_query_string() {
  local dest entry_copy="" served_copy outcome root_backup="" document_root port

  # The census first: it reads one file, touches no service, and reports the cheapest failure
  # before nginx is asked for anything.
  outcome="$(panel_vhost_log_census)"
  [ -z "$outcome" ] || fail "$outcome"

  prepare_host_for_the_nginx_step
  dest="$(run_nginx_step 'nginx_conf_dest')"

  # The host is left exactly as it was found, the same discipline
  # assert_nginx_vhost_is_validated_before_it_is_served follows: this writes the served path.
  if [ -e "$dest" ]; then
    entry_copy="$(mktemp)"
    cp -p "$dest" "$entry_copy"
  fi
  rm -f "$dest"

  # The installer's own step puts the vhost there — rendered by its renderer, validated by its
  # gate — because a vhost this script wrote itself would be a second authority and the census
  # above would be checking a file nginx never serves.
  run_nginx_step 'step_nginx' >/dev/null 2>&1 \
    || fail "step 80 refused to install the shipped panel vhost, so nothing below could be served. Run
assert_nginx_vhost_is_validated_before_it_is_served for the named reason."
  forget_nginx_service_records

  served_copy="$(mktemp)"
  cp -p "$dest" "$served_copy"

  # The SPA's document root, read out of the vhost rather than written here: a root this script
  # spelled itself would answer 404 for /setup the day the vhost moved, and a 404 is logged by the
  # ERROR log with the full request line — the probe would then measure the wrong file and read as
  # a leak the access log does not have.
  document_root="$(sed 's/^[[:space:]]*#.*//' "$PANEL_VHOST" \
    | awk '$1 == "root" { sub(/;$/, "", $2); print $2; exit }')"
  case "$document_root" in
    /*) ;;
    *) fail "no absolute 'root' directive was found in ${PANEL_VHOST} (read: '${document_root}'). The probe
below serves /setup out of that directory; without it every request 404s and this assertion would be
measuring nginx's error path instead of its access log." ;;
  esac
  if [ -e "${document_root}/index.html" ]; then
    root_backup="$(mktemp)"
    cp -p "${document_root}/index.html" "$root_backup"
  fi
  install -d -m 0755 "$document_root"
  printf '<!doctype html><title>polygon</title>\n' > "${document_root}/index.html"

  port="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")"

  outcome="$(panel_vhost_log_outcome "$dest" "$served_copy" "$port")"

  # Everything back before anything is reported, so a failure here does not also break the
  # assertions that follow.
  nginx -s quit >/dev/null 2>&1 || true
  cp -p "$served_copy" "$dest"
  rm -f "$served_copy"
  rm -f "${document_root}/index.html"
  if [ -n "$root_backup" ]; then
    cp -p "$root_backup" "${document_root}/index.html"
    rm -f "$root_backup"
  fi
  rm -f "$VHOST_LOG_ACCESS_LOG" "$VHOST_LOG_ERROR_LOG"
  rm -f "$dest"
  if [ -n "$entry_copy" ]; then
    cp -p "$entry_copy" "$dest"
    rm -f "$entry_copy"
  fi
  forget_nginx_service_records

  [ -z "$outcome" ] || fail "$outcome"
  nginx -t >/dev/null 2>&1 \
    || fail "this assertion did not put ${dest} back the way it found it; nginx no longer loads:
$(nginx -t 2>&1)"

  echo "The panel vhost declares a log format of its own and every access_log directive in it was"
  echo "enumerated: each names one of this file's own query-free formats, and no format the file declares"
  echo "carries a variable that holds a query string. Served through this family's real nginx, a request"
  echo "for /setup carrying a secret in its query string is logged WITHOUT the secret and WITH the path,"
  echo "and the same two requests through a copy of the vhost with the format name removed — what shipped"
  echo "before this fix — put the secret in the log, so the probe can see the leak it reports absent."
}

# panel_vhost_log_census: prints what is wrong with the vhost's logging directives, or nothing.
#
# A predicate rather than a series of `fail` calls, so that the propositions stay readable and so
# that the vacuity guards sit beside the checks they protect. Whole-line comments are stripped
# first and the file is then read as one line: every directive in nginx's configuration language
# ends in `;` and may be wrapped across as many lines as it likes, which the shipped log_format is.
panel_vhost_log_census() {
  local block name variable directive format
  local declared="" formats_seen=0 logs_seen=0
  local flattened
  flattened="$(sed 's/^[[:space:]]*#.*//' "$PANEL_VHOST" | tr '\n' ' ')"

  # Every format the file declares, and the variables in it. The deny list is the set of nginx
  # variables that contain a query string: `$request` is the raw request line, `$request_uri` the
  # original target, and `$args`/`$query_string`/`$is_args` the query itself. `$request_method` and
  # `$request_time` are not in it and must not be — which is why the comparison below is against
  # whole variable names and not a substring match, the mistake that would ban the method too.
  while IFS= read -r block; do
    [ -n "$block" ] || continue
    formats_seen=$((formats_seen + 1))
    name="$(printf '%s' "$block" | awk '{print $2}')"
    case "$name" in
      [A-Za-z_]*) ;;
      *) echo "a log_format in ${PANEL_VHOST} declares no usable name (read: '${name}'). Every access_log
directive below is checked against the names declared here, and an unnamed format makes that
comparison meaningless."
         return 0 ;;
    esac
    while IFS= read -r variable; do
      case "$variable" in
        '$request'|'$request_uri'|'$args'|'$query_string'|'$is_args')
          echo "log_format ${name} in ${PANEL_VHOST} logs ${variable}, which carries the request's query
string. A secret an operator is handed in a URL — a one-time setup token, a reset link, a signed
download — then reaches /var/log/maran/nginx-access.log, which outlives the install and is readable by
every uid in the panel's service group (rules/security.md item 8: never in logs, never in URLs)."
          return 0 ;;
      esac
    done < <(printf '%s\n' "$block" | grep -oE '\$[A-Za-z_][A-Za-z0-9_]*' || true)
    declared="${declared} ${name}"
  done < <(printf '%s\n' "$flattened" | grep -oE 'log_format[[:space:]]+[^;]*;' || true)

  # Every access_log directive, against those names. A directive with a path and NO format is the
  # defect itself: nginx falls back to the built-in `combined`, which logs `"$request"`.
  while IFS= read -r directive; do
    [ -n "$directive" ] || continue
    logs_seen=$((logs_seen + 1))
    format="$(printf '%s' "$directive" | sed 's/;[[:space:]]*$//' | awk '{print $3}')"
    case "$format" in
      "")
        case "$(printf '%s' "$directive" | sed 's/;[[:space:]]*$//' | awk '{print $2}')" in
          off) continue ;;
        esac
        echo "${PANEL_VHOST} has '${directive}' — an access log with no format named, which is nginx's
built-in 'combined', which logs \"\$request\" and therefore the query string. Name one of this file's own
query-free formats, or 'off'. This is the exact shape that wrote the installer's one-time setup token
into /var/log/maran/nginx-access.log (rules/security.md item 8)."
        return 0 ;;
    esac
    printf '%s\n' $declared | grep -qxF -- "$format" \
      || { echo "${PANEL_VHOST} has '${directive}', naming a log format '${format}' that this file does not
declare. Either it is a typo, in which case nginx refuses the whole configuration, or the format lives
somewhere this assertion cannot read and cannot check for a query string."
           return 0; }
  done < <(printf '%s\n' "$flattened" | grep -oE 'access_log[[:space:]]+[^;]*;' || true)

  # The two vacuity guards, and they are the point of writing this as a census. A file with no
  # access_log directive at all, or with none of its own formats, satisfies both loops above by
  # having nothing in them — and a check that agrees with everything is the failure rules/testing.md
  # names, not a pass.
  [ "$logs_seen" -gt 0 ] \
    && { [ "$formats_seen" -gt 0 ] || { echo "${PANEL_VHOST} names ${logs_seen} access_log directive(s) and declares no log_format of its
own, so the loop that checks formats for query-string variables checked nothing."; return 0; }; }
  [ "$logs_seen" -gt 0 ] \
    || { echo "no access_log directive was found anywhere in ${PANEL_VHOST}, so this census agreed with an
empty list. Either the panel vhost no longer logs — which is a decision, not an accident, and belongs in
this assertion — or the directive is spelled in a way this reader cannot see."
         return 0; }
  # Nothing wrong. This predicate always returns zero and says what it found on stdout, because a
  # non-zero status from inside a command substitution is what `set -e` acts on: the caller would
  # exit with no message at all, which is the one failure mode a gate must never have.
  return 0
}

# panel_vhost_log_outcome: serves two real requests twice — once through a vhost with the format
# name stripped from its access_log line, once through the shipped one — and prints what went
# wrong, or nothing.
#
# THE TWO REQUESTS. The first carries a secret in its query string, the way the installer's old
# setup link did. The second carries none and is otherwise identical, and it is what makes the
# grep for the secret a discriminating one: it must be logged too, and its line must NOT match,
# so a probe that matched anything would be caught by the very run that is supposed to be clean.
panel_vhost_log_outcome() {
  local dest="$1" shipped="$2" port="$3" logged

  # The leaking vhost first: the shipped file with the format name taken off the access_log line,
  # which is character-for-character what this repository served before this fix.
  sed -E 's#^([[:space:]]*access_log[[:space:]]+[^ ;]+)[[:space:]]+[A-Za-z_][A-Za-z0-9_]*;#\1;#' \
    "$shipped" > "$dest"
  cmp -s "$dest" "$shipped" \
    && { printf '%s' "the control vhost is identical to the shipped one, so the run below cannot tell a logged
secret from an unlogged one. The access_log line no longer has the shape this sed strips."; return 0; }
  logged="$(panel_vhost_request_pair "$port")" || { printf '%s' "$logged"; return 0; }
  case "$logged" in
    *"$VHOST_LOG_PROBE_SECRET"*) ;;
    *) printf '%s' "served through a vhost whose access_log names NO format — nginx's built-in 'combined' —
the query string '${VHOST_LOG_PROBE_SECRET}' did not appear in ${VHOST_LOG_ACCESS_LOG}. That is the leak
this assertion exists to keep closed, and this control is how it knows it can see it: without the secret
appearing here, the shipped run below proves only that nothing was logged at all. What the log holds:
${logged}"
       return 0 ;;
  esac

  # And now the shipped one.
  cp -p "$shipped" "$dest"
  logged="$(panel_vhost_request_pair "$port")" || { printf '%s' "$logged"; return 0; }
  case "$logged" in
    *"$VHOST_LOG_PROBE_SECRET"*)
      printf '%s' "the shipped panel vhost logged the query string '${VHOST_LOG_PROBE_SECRET}' into
${VHOST_LOG_ACCESS_LOG}. A secret handed to an operator in a URL therefore reaches a file that outlives
the install and is readable by every uid in the panel's service group (rules/security.md item 8). What
the log holds:
${logged}"
      return 0 ;;
  esac
  # The other direction, and the reason the absence above means anything: the request is still on
  # the record. A vhost that had simply stopped logging would satisfy the check above, and it would
  # cost the operator investigating an intrusion the one file that says the setup page was reached.
  case "$logged" in
    *"$VHOST_LOG_PROBE_PATH"*) ;;
    *) printf '%s' "the shipped panel vhost logged no line naming '${VHOST_LOG_PROBE_PATH}' at all. The secret
is absent from ${VHOST_LOG_ACCESS_LOG} because nothing was recorded, not because the format omits it —
and an access log that does not say the setup page was reached is a record an operator investigating an
intrusion no longer has. What the log holds:
${logged}"
       return 0 ;;
  esac
  # Twice, because two requests were made: a format that logged only one of them would still name
  # the path. This is what tells "the query string was dropped" apart from "the request carrying a
  # query string was dropped".
  case "$(printf '%s\n' "$logged" | grep -cF -- "$VHOST_LOG_PROBE_PATH")" in
    2) ;;
    *) printf '%s' "the shipped panel vhost logged $(printf '%s\n' "$logged" | grep -cF -- "$VHOST_LOG_PROBE_PATH") line(s) naming
'${VHOST_LOG_PROBE_PATH}' and two requests were made for it — one with a query string and one without.
A log that keeps the request without the query string and drops the request that had one hides exactly
the requests worth investigating. What the log holds:
${logged}"
       return 0 ;;
  esac
  # Nothing wrong; zero for the reason panel_vhost_log_census gives.
  return 0
}

# panel_vhost_request_pair: (re)starts nginx on the vhost now on the served path, makes the two
# probe requests, and prints the access log. Non-zero, with the reason on stdout, when the server
# or the requests did not happen — which must never be read as "the secret was not logged".
panel_vhost_request_pair() {
  local port="$1" status

  : > "$VHOST_LOG_ACCESS_LOG"
  : > "$VHOST_LOG_ERROR_LOG"
  if nginx -s quit >/dev/null 2>&1; then
    # nginx does not exit synchronously on `quit`; it finishes its connections first, and starting
    # a second master while the first still holds the port fails on `bind()`. Waited for by the pid
    # file it removes on the way out, rather than by a sleep long enough to look safe.
    #
    # `-s` and not `-e`: these images reach here with an EMPTY /run/nginx.pid left behind by the
    # build's own `nginx -t`, so a test for the file's existence waits the whole bound out on every
    # probe and then starts nginx anyway. Measured in the image, as five seconds per request pair.
    local waited=0
    while [ -s /run/nginx.pid ] && [ "$waited" -lt 50 ]; do
      waited=$((waited + 1))
      sleep 0.1
    done
  fi
  status=0
  nginx || status=$?
  [ "$status" -eq 0 ] \
    || { printf '%s' "nginx refused to start on the vhost under test (exit ${status}), so no request was served and
nothing below observed anything:
$(nginx -t 2>&1)"; return 1; }

  # -k because the certificate is the self-signed one step 80 generates; the probe is about the log
  # line, not about trust. --fail is deliberately NOT passed: a non-2xx answer is still a logged
  # request and the checks above are about the log, so a refusal here must be visible as a missing
  # line rather than as a dead probe.
  curl -sk -o /dev/null "https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH}?token=${VHOST_LOG_PROBE_SECRET}" \
    || { printf '%s' "curl could not reach https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH} on this container, so the
secret was never sent and its absence from the log means nothing."; return 1; }
  curl -sk -o /dev/null "https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH}" \
    || { printf '%s' "curl reached the panel with a query string and not without one, which leaves this assertion
no control request. https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH}"; return 1; }

  # WAIT for the two lines rather than reading once. curl returns when the RESPONSE is complete;
  # nginx writes its access-log entry after finishing the request, and with the write buffered the
  # line is regularly not there yet. Measured 2026-09-23: commit b2d6a7a passed this assertion on
  # dev and failed it on main — the same tree, the same image, a different machine's timing — and
  # the failure read "the shipped panel vhost logged no line naming '/setup' at all", which is the
  # sentence this assertion prints when the leak it hunts is absent. A flaky assertion that fails
  # with the words of a real finding is worse than no assertion: it teaches its reader to disbelieve
  # it.
  #
  # Bounded, and the bound is a failure rather than a fall-through: if the lines never arrive, the
  # caller still reads an empty log and says so, exactly as before. The same shape as the pid-file
  # wait above — wait for the observation, never sleep a length that looks safe.
  vhost_log_waited=0
  while [ "$(grep -cF -- "$VHOST_LOG_PROBE_PATH" "$VHOST_LOG_ACCESS_LOG" 2>/dev/null || echo 0)" -lt 2 ] \
        && [ "$vhost_log_waited" -lt 50 ]; do
    vhost_log_waited=$((vhost_log_waited + 1))
    sleep 0.1
  done

  cat "$VHOST_LOG_ACCESS_LOG"
  return 0
}

# web_server_group_for_this_family: the group install.sh's own detect_web_server_identity decides.
#
# EXTRACTED from install.sh and run, rather than copied here. install.sh ends in `main "$@"` and
# cannot be sourced, and a family-to-group table written in this file would be a second authority —
# exactly the thing assert_panel_port_has_one_authority exists to prevent. MARAN_OS_FAMILY is set
# by the Dockerfile RUN that executes this script, which is what makes the answer this family's.
web_server_group_for_this_family() {
  bash -c 'set -euo pipefail
eval "$(sed -n "/^detect_web_server_identity()/,/^}/p" "$1")"
detect_web_server_identity
printf "%s" "$MARAN_WEB_SERVER_GROUP"' _ "$INSTALLER_ENTRY_POINT"
}

# run_services_step: runs step 70's code the way install.sh runs it — a child shell with
# `set -euo pipefail`, the step file sourced, and the values install.sh decides exported first.
#
# A CHILD, for the reason run_installer_step gives at length: `exit` and `set -e` mean different
# things inside this script's `if` than they do in the installer, and step 70 aborts with `exit 1`.
run_services_step() {
  local socket_path="$1" web_group="$2" snippet="$3"
  MARAN_API_SOCKET_PATH="$socket_path" \
    MARAN_WEB_SERVER_GROUP="$web_group" \
    MARAN_USER="$POLYGON_SERVICE_USER" \
    MARAN_GROUP="$POLYGON_SERVICE_GROUP" \
    LIB_DIR="$INSTALLER_LIB" \
    bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$SERVICES_STEP" "$snippet"
}

# assert_the_panel_socket_directory_is_built_and_then_looked_at: the panel's trust boundary,
# BUILT in this image by the installer's own code and then observed — not grepped for.
#
# WHY IT IS SHAPED THIS WAY. The two checks this replaced grepped the api unit for an
# `ExecStartPre=+/usr/bin/chgrp` line and for `RuntimeDirectoryMode=2710`, and their failure
# messages said nginx would not be able to open the panel's socket. Both greps passed while the
# directory came out owned by the service account and its own group on both families and nginx
# could not open the socket at
# all: systemd re-applies a unit's User=/Group= to its RuntimeDirectory= on every command
# invocation of that unit, so the chgrp was undone before ExecStart ran. A check that reports on
# something it never looked at is worse than no check, because it retires the question.
#
# WHAT THIS ONE SEES, and it is a runtime fact rather than a text one: it runs step 70's real
# `install_units` (so the snippet is rendered by the installer's renderer, from install.sh's own
# socket path and this family's own web server group), its real `build_api_socket_directory` (so
# the directory is made by THIS family's systemd-tmpfiles, against this image's real service and
# web-server groups), and its real `assert_api_socket_directory`. Then it stats the directory
# itself, so the verdict does not rest on the installer agreeing with itself. It also breaks the
# directory by hand and checks that the installer's postcondition REFUSES, and that a second
# `--create` puts it back.
#
# WHAT THIS CANNOT SEE, stated plainly because the previous version of this check did not.
# This image never boots systemd — /usr/bin/systemctl here is docker/polygon/systemctl-stand-in.sh
# — so nothing starts maran-api.service, and NOTHING HERE OBSERVES the web server's uid reaching
# the socket or a customer's uid being refused. Two things stand in for that: the text assertion
# below that the unit declares no RuntimeDirectory= at all, so there is no exec directory for
# systemd to re-apply ownership to; and a measurement on booted systemd on both families
# (255 and 252), recorded in docs/superpowers/notes/2026-09-03-panel-socket-threat-note.md §3.1.
# The gap is printed in this function's own output, so a reader of a green build is told what it
# did not ask.
assert_the_panel_socket_directory_is_built_and_then_looked_at() {
  local socket_path socket_dir web_group observed expected payload

  socket_path="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  socket_dir="${socket_path%/*}"
  web_group="$(web_server_group_for_this_family)"
  [ -n "$web_group" ] \
    || fail "install.sh's detect_web_server_identity names no web server group for MARAN_OS_FAMILY=${MARAN_OS_FAMILY:-unset};
the socket directory would be group-owned by nothing and nginx could not reach the panel."
  getent group "$web_group" >/dev/null \
    || fail "install.sh names '${web_group}' as this family's web server group, and no such group exists in this
image. Either the name is wrong for ${MARAN_OS_FAMILY:-this family}, or the Dockerfile stopped installing nginx."

  # TEXT, and only what text can know: the unit must not take the directory back. This is the
  # regression guard for the defect above — RuntimeDirectory= is the one thing that makes systemd
  # re-apply User=/Group= over the directory on every command invocation.
  if grep -q '^RuntimeDirectory=' "$API_UNIT"; then
    fail "maran-api.service declares RuntimeDirectory= again. systemd re-applies the unit's User=/Group= to a
RuntimeDirectory= on EVERY command invocation, which is what silently undid the group this directory needs;
the directory is built by ${API_TMPFILES} instead, and the unit must leave it alone.
(This is a text check. What the directory comes out as is checked below, by building it.)"
  fi
  if ! grep -q "^ReadWritePaths=.*__MARAN_API_SOCKET_DIR__" "$API_UNIT"; then
    fail "maran-api.service's ReadWritePaths= no longer names __MARAN_API_SOCKET_DIR__. ProtectSystem=strict
mounts the whole filesystem read-only, RuntimeDirectory= used to make its own exception, and without one the
panel cannot create its socket at all.
(This is a text check over ${API_UNIT}.)"
  fi
  if ! grep -q '^ExecStopPost=-/usr/bin/rm -f __MARAN_API_SOCKET__$' "$API_UNIT"; then
    fail "maran-api.service no longer removes its socket in ExecStopPost. systemd does not delete the directory
on stop any more, and the server refuses to bind over an existing socket rather than reusing it — so a killed
panel would never start again.
(This is a text check over ${API_UNIT}.)"
  fi

  # The snippet is one directive, and this checks the shape of it rather than the whole line: what
  # it renders to is applied for real below.
  payload="$(grep -v '^[[:space:]]*#' "$API_TMPFILES" | grep -v '^[[:space:]]*$' || true)"
  [ "$(printf '%s\n' "$payload" | wc -l)" -eq 1 ] \
    || fail "${API_TMPFILES} no longer carries exactly one directive; systemd-tmpfiles would build something
this check has not read. It carries:
${payload}"
  case "$payload" in
    "d __MARAN_API_SOCKET_DIR__ 2710 __MARAN_USER__ __MARAN_WEB_GROUP__ -") ;;
    *) fail "${API_TMPFILES}'s directive is '${payload}'. It must be
'd __MARAN_API_SOCKET_DIR__ 2710 __MARAN_USER__ __MARAN_WEB_GROUP__ -': the type d so an existing directory is
CORRECTED and not only created, 2710 so no other uid can traverse to the socket and so the socket inherits
the group, __MARAN_USER__ because the unit's User= must be able to bind there, and the placeholders so the
path, the account and the group keep following install.sh." ;;
  esac

  # RUNTIME, from here down. Step 70's own functions, this family's own systemd-tmpfiles.
  run_services_step "$socket_path" "$web_group" 'install_units; build_api_socket_directory; assert_api_socket_directory' \
    || fail "step 70 could not build ${socket_dir} on this family. That directory is the panel's trust boundary
and the api unit does not start without it."

  # The polygon's own eyes, not the installer agreeing with itself.
  expected="2710 ${POLYGON_SERVICE_USER} ${web_group}"
  observed="$(stat -c '%a %U %G' "$socket_dir" 2>/dev/null || true)"
  [ "$observed" = "$expected" ] \
    || fail "${socket_dir} was built as '${observed:-absent}' and must be '${expected}'. At 2710 no other uid on
the machine can resolve a path inside it, so a customer's cron entry or PHP script cannot connect(2) to the
socket; the group is what lets nginx traverse it at all, and the setgid bit is what hands the socket that
group when the panel — which holds no capabilities — creates it."

  # The postcondition is not vacuous: break the directory and watch step 70 refuse.
  chgrp "$POLYGON_SERVICE_GROUP" "$socket_dir"
  if run_services_step "$socket_path" "$web_group" 'assert_api_socket_directory' >/dev/null 2>&1; then
    fail "step 70's assert_api_socket_directory accepted ${socket_dir} group-owned by ${POLYGON_SERVICE_GROUP}, which is the exact
state in which nginx cannot open the panel's socket and every API call answers 502. The postcondition that is
supposed to catch that on a real server does not catch it."
  fi

  # And a re-run puts it right, which is what makes re-running the installer a repair.
  run_services_step "$socket_path" "$web_group" 'build_api_socket_directory'
  observed="$(stat -c '%a %U %G' "$socket_dir" 2>/dev/null || true)"
  [ "$observed" = "$expected" ] \
    || fail "systemd-tmpfiles --create did not put ${socket_dir} back to '${expected}' after it was changed by
hand (read: '${observed:-absent}'). A boundary that cannot be repaired by re-running the installer is one an
operator will widen with chmod instead."

  # Leave the image as it was found: this is the only assertion that installs a unit.
  rm -f /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service
  rm -f /etc/tmpfiles.d/maran-api.conf
  rm -rf "$socket_dir"

  echo "Step 70 built ${socket_dir} as ${expected} with this family's real systemd-tmpfiles, refused it when its"
  echo "group was changed by hand, and repaired it on a re-run."
  echo "UNOBSERVED HERE: this image boots no systemd, so nothing above started maran-api.service or watched"
  echo "${web_group}'s uid connect to the socket while a customer's uid was refused. That was measured on booted"
  echo "systemd on both families and is recorded in the panel socket threat note; what stands in for it here is"
  echo "the text assertion that the unit declares no RuntimeDirectory= over the same directory."
}

# assert_the_service_account_name_has_one_authority: install.sh decides the name of the account
# the panel runs as, and every other file derives it. This fails the image build when one of them
# has grown a copy that disagrees.
#
# It exists for the reason assert_panel_port_has_one_authority exists, with a worse failure: the
# port disagreeing costs a 502, whereas the NAME disagreeing means step 40 creates one account,
# step 15 renames onto another, the units name a third and the uninstaller deletes a fourth. Two
# of those are silent on a fresh host and appear only on an upgrade.
#
# What it checks, and each one is a way this has already been done wrong somewhere:
#   * install.sh assigns each of the five names exactly once — a second assignment further down
#     is what `installer_value` reads, and the first is then a comment with syntax;
#   * the step files do NOT assign them at all: they read them from the environment, so a
#     `MARAN_USER=` inside a step is a second authority that would win inside that step only;
#   * the uninstaller, which is its own entry point and sources nothing, spells the same values.
assert_the_service_account_name_has_one_authority() {
  local key count step assignment uninstall_user uninstall_legacy uninstall_marker

  for key in MARAN_USER MARAN_GROUP MARAN_LEGACY_USER MARAN_LEGACY_GROUP MARAN_SERVICE_ACCOUNT_MARKER; do
    count="$(installer_value_count "$key" "$INSTALLER_ENTRY_POINT")"
    [ "$count" -eq 1 ] \
      || fail "install.sh assigns ${key} ${count} times at the start of a line. One is the authority; a
second one further down is what every reader here resolves, and the first becomes a comment with syntax."
  done

  for step in "$USER_STEP" "$IDENTITY_STEP" "$SERVICES_STEP" "$CONFIG_STEP" "$NGINX_STEP"; do
    for key in MARAN_USER MARAN_GROUP; do
      assignment="$(installer_value_count "$key" "$step")"
      [ "$assignment" -eq 0 ] \
        || fail "$(basename "$step") assigns ${key} itself. The name of the account is install.sh's to
decide and a step's to READ: a step that assigns it wins inside itself and disagrees with every other
step, which on an upgrade means one account created, another renamed and a third named by the units."
    done
  done

  uninstall_user="$(installer_value MARAN_USER "$UNINSTALLER")"
  uninstall_legacy="$(installer_value MARAN_LEGACY_USER "$UNINSTALLER")"
  uninstall_marker="$(installer_value MARAN_SERVICE_ACCOUNT_MARKER "$UNINSTALLER")"
  uninstall_marker="${uninstall_marker%\"}"
  uninstall_marker="${uninstall_marker#\"}"
  [ "$uninstall_user" = "$POLYGON_SERVICE_USER" ] \
    || fail "uninstall.sh removes the account '${uninstall_user}' while install.sh creates
'${POLYGON_SERVICE_USER}'. The uninstaller is its own entry point and sources nothing from the installer,
which is why these two are compared here instead of hoped about."
  [ "$uninstall_legacy" = "$POLYGON_LEGACY_USER" ] \
    || fail "uninstall.sh knows the pre-rename account as '${uninstall_legacy}' while install.sh names it
'${POLYGON_LEGACY_USER}'. An uninstall on a host that was never upgraded would leave its account behind."
  [ "$uninstall_marker" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "uninstall.sh looks for the marker '${uninstall_marker}' and install.sh stamps
'${POLYGON_SERVICE_MARKER}'. The uninstaller would refuse to remove this product's own account, or —
worse, if it ever loosened — remove one it did not create."

  echo "The service account is named once, in install.sh: '${POLYGON_SERVICE_USER}' (group"
  echo "'${POLYGON_SERVICE_GROUP}', pre-rename '${POLYGON_LEGACY_USER}'), and no step file, and not"
  echo "the uninstaller, carries a second spelling of it."
}

# assert_a_foreign_account_at_the_service_name_is_refused: step 40 must REFUSE an account of its
# own name that it did not create, and must ACCEPT the one it did.
#
# The defect this is the gate for: create_service_user's predecessor was idempotent by `id -u`
# alone, so an account of that name that belonged to somebody else was adopted in silence and
# handed /var/lib/maran, read on panel.env and on the TLS key, User= on the api unit and an
# admitted uid on the root agent's socket. A rarer name makes that less likely and not
# impossible, so the mechanism is what has to hold.
#
# Three legs, and the second is why this is evidence rather than decoration: a guard mutated to
# refuse everything passes any test that only ever feeds it the bad case.
#   1. the marker replaced with somebody else's comment  -> the step must refuse, by that name;
#   2. the marker put back                               -> the step must accept;
#   3. the foreign comment again, with MARAN_ADOPT_EXISTING_USER=1 -> the step must adopt it
#      deliberately, which is the operator's documented way past the refusal.
#
# The image's real account is used, and its GECOS is put back at the end and READ BACK: a
# usermod that silently did not take would leave leg 2 measuring the same state as leg 1.
assert_a_foreign_account_at_the_service_name_is_refused() {
  local original restored output

  id -u "$POLYGON_SERVICE_USER" >/dev/null 2>&1 \
    || fail "this image has no '${POLYGON_SERVICE_USER}' account for the adoption gate to be tested
against. It is created by step 40's own create_service_user earlier in this script."
  original="$(getent passwd "$POLYGON_SERVICE_USER" | cut -d: -f5)"
  [ "$original" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "the '${POLYGON_SERVICE_USER}' account in this image carries the comment '${original}' and not
install.sh's marker '${POLYGON_SERVICE_MARKER}'. Step 40 stamps that marker when it creates the account, so
either it stopped stamping it or something else made this account."

  usermod -c "Some other product's service account" "$POLYGON_SERVICE_USER"
  [ "$(getent passwd "$POLYGON_SERVICE_USER" | cut -d: -f5)" = "Some other product's service account" ] \
    || fail "the adoption control could not replace the marker on '${POLYGON_SERVICE_USER}'. The plant did
not land, so the refusal below would have been measured against an account carrying our own marker."

  if output="$(run_user_step 'create_service_user' 2>&1)"; then
    usermod -c "$POLYGON_SERVICE_MARKER" "$POLYGON_SERVICE_USER"
    fail "step 40's create_service_user ACCEPTED an account named '${POLYGON_SERVICE_USER}' that this
installer did not create. That is the defect the rename was made for: the install then hands an unrelated
local principal /var/lib/maran, read on panel.env and on the panel's TLS key, User= on maran-api.service
and an admitted uid on the root agent's socket. It said:
${output}"
  fi
  case "$output" in
    *"already exists on this host and this"*) ;;
    *) usermod -c "$POLYGON_SERVICE_MARKER" "$POLYGON_SERVICE_USER"
       fail "step 40 refused the foreign account but not by name; an operator reading this cannot tell it
from any other abort. It said:
${output}" ;;
  esac

  # Leg 3, while the foreign comment is still in place: the documented way past the refusal.
  MARAN_ADOPT_EXISTING_USER=1 run_user_step 'create_service_user' >/dev/null \
    || fail "MARAN_ADOPT_EXISTING_USER=1 did not let step 40 adopt the existing '${POLYGON_SERVICE_USER}'
account. The refusal then has no way past it, and an operator whose host legitimately holds that account
cannot install this product at all."

  # Leg 2, the inverse control. Adoption at leg 3 stamps the marker, so this also proves the
  # deliberate adoption left the account recognisable to the next run.
  usermod -c "$POLYGON_SERVICE_MARKER" "$POLYGON_SERVICE_USER"
  restored="$(getent passwd "$POLYGON_SERVICE_USER" | cut -d: -f5)"
  [ "$restored" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "the adoption control could not put the marker back on '${POLYGON_SERVICE_USER}' (it reads
'${restored}'). The image is left in the planted state, and every assertion after this one runs against it."
  run_user_step 'create_service_user' >/dev/null \
    || fail "step 40's create_service_user REFUSED the account it created itself, the one carrying its own
marker. Re-running the installer over its own previous run is what idempotence means here, and it is now
broken: an upgrade of an installed host would abort at step 40."

  echo "Step 40 refused an account named '${POLYGON_SERVICE_USER}' carrying somebody else's comment, adopted"
  echo "it when MARAN_ADOPT_EXISTING_USER=1 said so, and accepted the one carrying its own marker."
}

# assert_the_legacy_service_account_is_migrated_in_place: step 15's rename keeps the uid and the
# gid, which is the property every file the pre-rename account owned depends on.
#
# WHY THIS IS THE PROPERTY. /etc/maran, panel.env, the TLS key, /var/lib/maran with the
# DataProtection keys in it, /var/log/maran and its panel subdirectory, /run/maran and the api's
# socket directory are all owned by that uid on an installed host. A migration that created a new
# account and chowned a list of paths would have to get the list right and would break whatever
# it missed; a rename cannot miss anything, because nothing about the files changes at all.
#
# Driven against THROWAWAY accounts rather than this image's real one: the real account owns
# every directory the assertions around this one measure, and taking it apart to rebuild it would
# make this assertion's failure mode "the next twelve assertions fail for a reason of my making".
# The names are passed to the step the same way install.sh passes them.
#
# UNOBSERVED HERE: this image never runs install.sh end to end, so the ORDER of the whole
# migration — stop the api, rename, rewrite, and step 70's restart at the far end — is not
# exercised. What is exercised is each thing the step does to the host, one at a time.
assert_the_legacy_service_account_is_migrated_in_place() {
  local from="maranpolygonfrom" to="maranpolygonto"
  local uid_before gid_before uid_after gid_after owner planted config found

  userdel "$from" >/dev/null 2>&1 || true
  userdel "$to" >/dev/null 2>&1 || true
  groupdel "$from" >/dev/null 2>&1 || true
  groupdel "$to" >/dev/null 2>&1 || true
  rm -rf -- /tmp/polygon-legacy-state
  useradd --system --no-create-home --shell /usr/sbin/nologin --user-group "$from"
  uid_before="$(id -u "$from")"
  gid_before="$(id -g "$from")"
  install -d -o "$from" -g "$from" -m 0750 /tmp/polygon-legacy-state
  : > /tmp/polygon-legacy-state/keys
  chown "${from}:${from}" /tmp/polygon-legacy-state/keys

  MARAN_IDENTITY_FROM="$from" MARAN_IDENTITY_TO="$to" \
    run_identity_step 'rename_the_group; rename_the_user' >/dev/null \
    || { userdel "$from" >/dev/null 2>&1 || true
         fail "step 15's rename of '${from}' to '${to}' failed inside the polygon. A pre-rename host cannot
be upgraded at all, and the panel is left stopped by the step that stops it first."; }

  id -u "$to" >/dev/null 2>&1 \
    || fail "step 15 reported success and there is no '${to}' account. The migration is the only thing that
renames a pre-rename host onto the name every other step now uses."
  uid_after="$(id -u "$to")"
  gid_after="$(id -g "$to")"
  [ "$uid_after" = "$uid_before" ] && [ "$gid_after" = "$gid_before" ] \
    || fail "step 15 changed the uid/gid while renaming: ${uid_before}/${gid_before} became
${uid_after}/${gid_after}. EVERY file the pre-rename account owned — /var/lib/maran and the DataProtection
keys in it, /etc/maran/panel.env, the panel's TLS private key, /var/log/maran/panel — is owned by that
NUMBER, so the panel would come up unable to read its own configuration and unable to write its own state."
  owner="$(stat -c '%U:%G' /tmp/polygon-legacy-state)"
  [ "$owner" = "${to}:${to}" ] \
    || fail "a directory owned by the pre-rename account reads '${owner}' after the migration and must read
'${to}:${to}'. Files follow a rename because the uid does not change; if they do not, this migration is a
chown of a list of paths and the list is not in it."
  [ "$(getent passwd "$to" | cut -d: -f5)" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "step 15 renamed the account without stamping install.sh's marker on it, so step 40 — which
runs 25 steps later in the same install — would then refuse the very account this step just produced, and
every upgrade of a pre-rename host would abort halfway through the migration it just performed."

  userdel "$to" >/dev/null 2>&1 || true
  groupdel "$to" >/dev/null 2>&1 || true
  rm -rf -- /tmp/polygon-legacy-state

  # The predicate that decides whether a `panel` on this host is OURS at all, both ways round. A
  # foreign account of that name on a host with no Maran state must NOT be migrated, and the
  # account that owns /var/lib/maran must be.
  useradd --system --no-create-home --shell /usr/sbin/nologin --user-group "$from"
  if MARAN_IDENTITY_FROM="$from" MARAN_IDENTITY_TO="$to" \
      run_identity_step 'host_is_a_legacy_installation' >/dev/null 2>&1; then
    userdel "$from" >/dev/null 2>&1 || true
    groupdel "$from" >/dev/null 2>&1 || true
    fail "step 15 treats an account that owns nothing of this product's as a pre-rename installation of it.
It would then rename somebody else's account — and on a host that merely happens to have one, an install
would take it over instead of refusing it, which is the whole defect this change exists to close."
  fi
  userdel "$from" >/dev/null 2>&1 || true
  groupdel "$from" >/dev/null 2>&1 || true

  planted="$(stat -c '%U' /var/lib/maran)"
  [ "$planted" = "$POLYGON_SERVICE_USER" ] \
    || fail "/var/lib/maran is owned by '${planted}' and not by '${POLYGON_SERVICE_USER}', so the inverse
control below would be measuring the same 'no' as the case above and would prove nothing."
  MARAN_IDENTITY_FROM="$POLYGON_SERVICE_USER" MARAN_IDENTITY_TO="$to" \
    run_identity_step 'host_is_a_legacy_installation' >/dev/null 2>&1 \
    || fail "step 15's predicate answers NO for the account that owns /var/lib/maran. A pre-rename host
would then be left alone by the migration and taken over by step 40 instead, which is the state where the
api runs as one account and every file belongs to another."

  # The one name a rename does not follow: the role in the panel's own configuration file.
  config="/etc/maran/panel.env"
  rm -f -- "$config"
  install -d -o root -g root -m 0750 /etc/maran
  printf 'Database__Host=/var/run/postgresql\nDatabase__Username=%s\nSecurity__EncryptionKey=x\n' \
    "$POLYGON_LEGACY_USER" > "$config"
  run_identity_step 'rewrite_the_configured_role' >/dev/null \
    || { rm -f -- "$config"; fail "step 15 could not rewrite Database__Username in ${config}."; }
  grep -q "^Database__Username=${POLYGON_SERVICE_USER}\$" "$config" \
    || { rm -f -- "$config"
         fail "step 15 left ${config} naming the pre-rename role. PostgreSQL authenticates the panel by
matching its OS user name to the role name (peer auth), so the api would come up and fail every connection
with an authentication error against a role that no longer exists."; }
  found="$(stat -c '%U:%G:%a' "$config")"
  [ "$found" = "root:${POLYGON_SERVICE_GROUP}:640" ] \
    || { rm -f -- "$config"
         fail "step 15 rewrote ${config} and left it ${found} instead of
root:${POLYGON_SERVICE_GROUP}:640. That file holds the encryption key and the setup token; a rewrite that
widens it is a worse defect than the name it was fixing."; }
  rm -f -- "$config"

  echo "Step 15 renamed an account and its group in place with the uid and gid unchanged, files following,"
  echo "and install.sh's marker stamped; refused a same-named account that owns nothing of ours; recognised"
  echo "the one that owns /var/lib/maran; and rewrote Database__Username without widening panel.env."
  echo "UNOBSERVED HERE: this image runs no install end to end, so the migration's ORDER — the api stopped"
  echo "first and restarted by step 70 last — is not exercised, and neither is the PostgreSQL role rename,"
  echo "which needs a server holding this product's own database."
}

# --- The on-disk boundary the two privilege-escalation fixes moved ------------------------
#
# Everything from here to main() is about ONE question: is each directory the installer
# creates the thing it is documented to be — a real directory, owned by the uid that is
# supposed to own it, with exactly the mode that keeps every other uid out?
#
# It exists because two panel->root escalations were found and fixed in one day, and both
# were escapes of exactly that property:
#
#   * release staging lived inside a `panel`-owned directory, so the api's uid could swap
#     agent.tar.gz between the checksum verification and the extraction, and the extracted
#     file is what maran-agent.service runs as root (installer/lib/50-artifacts.sh, and
#     docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md);
#   * the bulk scratch lived inside `panel`-owned /var/lib/maran, so the api's uid owned an
#     ancestor of root's staging area, could rename a level aside and leave a symlink at
#     that name, and root would then write a customer's plaintext database dump into a
#     panel-readable file — or truncate a root-owned one
#     (docs/superpowers/notes/2026-09-05-backups-threat-note.md section 1).
#
# Both fixes are correct and both were, until this section, guarded by nothing but the
# comments explaining them. A prose guarantee is the thing this repository has already been
# taught to distrust: it does not fail when it stops being true.
#
# Why the expected owners and modes are written out here as literals, when the Dockerfile
# comment beside `create_directory_layout` says a second copy of a mode is a copy that
# stops matching. Those are two different jobs. The Dockerfile calls the installer so the
# polygon's directories are made the way a real install makes them — there, a literal would
# be a copy of a value with no opinion of its own. Here the literals ARE the opinion: a
# check that reads its expectation out of the code it is checking agrees with that code by
# construction and can never fail, which is precisely how a mode gets loosened without
# anything going red. This table is a second, independent statement of the boundary, in the
# same spirit as check-structure.sh comparing ReadWritePaths= against agent_paths.rs.

# assert_the_directory_layout_is_what_it_claims: runs the installer's OWN step 40 and then
# looks at every directory it made — is it a real directory rather than a symlink to one,
# who owns it, what is its exact mode.
#
# The symlink question is asked separately from the directory question on purpose. `[ -d ]`
# follows symlinks, so a symlink pointing at a directory answers yes to it; that is the
# shape the scratch attack took, and a check that only asked `[ -d ]` would have watched it
# happen. `stat` without `-L` reports the link's own mode (0777 on Linux, always) and the
# link's owner, so neither of those would have caught it either.
#
# /run/maran's mode is put back afterwards. This image sets it 0755 on purpose for the
# php-pool suites and step 40 sets 0750; the Dockerfile re-applies 0755 after its own call
# to this function, and this restores it so the assertion cannot depend on which of the two
# ran last.
assert_the_directory_layout_is_what_it_claims() {
  local run_maran_mode_before=''
  if [ -d /run/maran ]; then
    run_maran_mode_before="$(stat -c '%a' /run/maran)"
  fi

  run_user_step 'create_service_user >/dev/null
create_directory_layout' \
    || fail "step 40's create_directory_layout failed inside the polygon"

  # path:expected uid:expected gid:expected mode, one per line. The service account is
  # resolved at check time rather than written as a number, because a system user's uid is
  # whatever useradd picked on this host.
  local panel_uid panel_gid
  panel_uid="$(id -u "$POLYGON_SERVICE_USER")"
  panel_gid="$(id -g "$POLYGON_SERVICE_USER")"

  # `/var/log/maran/sites` (AgentPaths::SITE_LOG_ROOT) is in this list and is not one more
  # directory: it is the containment for a CRITICAL escalation. Every site's nginx logs used
  # to be /home/<account>/logs/<domain>.access.log, inside a tree the CUSTOMER owns, and the
  # root nginx master opens every access_log target O_WRONLY|O_APPEND|O_CREAT with no
  # O_NOFOLLOW — so a customer replaced one with a symbolic link and the next reload, which
  # any tenant's site action triggers because one nginx serves them all, had ROOT create and
  # append to the target. Proved on this image against /etc/ld.so.preload
  # (docs/superpowers/notes/2026-09-09-site-logs-threat-note.md).
  #
  # The fix is the path, and the path's whole defence is the ownership and mode of every
  # ancestor: /var/log/maran root:<service group> 0750, /var/log/maran/sites root:root 0750. Group
  # ROOT and not the service group like its parent — the panel process has no business reading a
  # customer's access log off the disk, it asks the agent, so the panel uid must match only
  # `other`, which is `---` at 0750. If the installer ever creates this directory with the
  # parent's group, or 0755, or under a uid that is not root, the escalation returns and no
  # test in this repository outside this file would notice: the agent's own suites create
  # the tree themselves rather than reading what the installer left.
  local entry checked=0
  for entry in \
    "/usr/local/maran:0:0:755" \
    "/etc/maran:0:${panel_gid}:750" \
    "/var/lib/maran:${panel_uid}:${panel_gid}:750" \
    "/var/log/maran:0:${panel_gid}:750" \
    "/var/log/maran/panel:${panel_uid}:${panel_gid}:750" \
    "/var/log/maran/sites:0:0:750" \
    "/var/backups/maran:0:0:700" \
    "/home/.maran-restore:0:0:711" \
    "/var/lib/maran-scratch:0:0:700" \
    "/var/lib/maran-sftp:0:0:700" \
    "/run/maran:0:${panel_gid}:750"; do
    assert_directory_is "${entry%%:*}" "${entry#*:}"
    checked=$((checked + 1))
  done
  # The vacuity guard, and it is on the axis that can actually go blind here. Everything this
  # loop asserts comes from ONE list, the loop body runs zero times for an empty one, and a
  # `for` over nothing exits 0 — so a path deleted from the list (a bad merge, a rebase
  # resolving a conflict by taking the shorter side) removes a check with no output changing
  # anywhere. A count is not decoration on that axis: it is the only thing that observes the
  # list's SIZE, which is the property the assertions themselves cannot see. Raise it with the
  # list.
  [ "$checked" -eq 11 ] \
    || fail "the directory layout list checked ${checked} paths, not the 11 step 40 creates. A path has been dropped from the list, and a check that is not in the list is not a check that failed — it is one that no longer exists."

  # The scratch is a SIBLING of /var/lib/maran, never a child, and the reason is the whole
  # fix: the owner of a directory can rename an entry inside it aside and leave a symlink
  # at that name without ever having permission to enter it, so a root-only directory
  # underneath a panel-owned one is not root-only. Asserted as a statement about the PATH
  # because that is what the fix changed, and a mode check on the scratch cannot see it —
  # the scratch was root:root 0700 while it was exploitable.
  case "$(cd /var/lib/maran-scratch && pwd -P)" in
    /var/lib/maran/*) fail "/var/lib/maran-scratch resolves underneath /var/lib/maran, whose owner is the panel uid — the ancestor that made this directory reachable is back" ;;
  esac
  # And the ancestor it does have must be root's. /var/lib is root:root 0755 on both
  # families; if it ever were not, no mode on the scratch itself would save it.
  assert_ancestors_are_root_only /var/lib/maran-scratch

  # The SFTP jail base, for the same reason and with a second consequence the scratch does
  # not have. Every path component of a `ChrootDirectory` must be root-owned or OpenSSH
  # refuses the login, so a panel-owned ancestor here does not merely make the jails
  # reachable — it makes SFTP not work at all, silently, with the reason visible only in
  # the daemon's log. Measured on both families at the old base: `Accepted password ...`
  # followed by `bad ownership or modes for chroot directory component "/var/lib/maran/"`.
  case "$(cd /var/lib/maran-sftp && pwd -P)" in
    /var/lib/maran/*) fail "/var/lib/maran-sftp resolves underneath /var/lib/maran, whose owner is the panel uid — sshd refuses a chroot with a non-root path component, so every SFTP login on this host is broken" ;;
  esac
  assert_ancestors_are_root_only /var/lib/maran-sftp

  # The log directory, the fourth instance of this class and the only one not fixed by a
  # relocation. Three leaf names inside /var/log/maran are opened for APPEND by root:
  # install.sh's own install.log, and the panel vhost's nginx-access.log and nginx-error.log,
  # which the root nginx MASTER creates and reopens on every start, reload and SIGUSR1. While
  # step 40 made this directory panel:panel, the panel uid could unlink any of those names and
  # leave a symbolic link — measured getting root's `tee -a` to append into a root-owned 0600
  # file, and getting nginx to append an attacker-chosen request line into another
  # (docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md).
  #
  # Asserted on the LEAVES and not on the directory, because the ancestor walk is the check
  # that can see this defect: the leaf's own mode says nothing about who may replace it, and
  # nginx recreates these files itself, as root, so nothing else stands between the plant and
  # the open. The names come from install.sh, one place, so a fourth root-written log added
  # there is covered here without anyone extending a second list.
  local trusted_log_leaf
  for trusted_log_leaf in $(installer_root_trusted_log_names); do
    assert_ancestors_are_root_only "/var/log/maran/${trusted_log_leaf}"
  done
  # And the panel's own subdirectory is NOT an ancestor of any of them — the split's whole
  # point. If a future change moved a root-written log inside it, the walk above would fail,
  # but this states the shape directly so the diagnosis names the design and not just a uid.
  case "$(cd /var/log/maran/panel && pwd -P)" in
    /var/log/maran/panel) ;;
    *) fail "/var/log/maran/panel does not resolve to itself — the panel's writable log directory is a link somewhere else, and root writes in its parent" ;;
  esac

  # The site log root's ANCESTRY, which is a different question from its own uid:gid:mode
  # asserted in the list above and is the question the F-1 fix actually turns on. Root writes
  # into this tree — the nginx master opens every site's access_log and error_log under it, as
  # root, with no O_NOFOLLOW — and who may replace a directory is decided by its PARENT, never
  # by the directory's own mode. The old location was root-writable too; what made it
  # exploitable was that /home/<account> belongs to the customer. So a mode check on this
  # directory cannot see the defect that moved it, and this walk is what can.
  assert_ancestors_are_root_only /var/log/maran/sites
  # And it must be a real directory at that name, not a link: `install -d` exits 0 on a symlink
  # to an existing directory and applies the ownership and the mode to the link's TARGET, which
  # would have root open every customer's site log wherever the link points. assert_directory_is
  # rejects a link above; this states the resolution directly so the diagnosis names the shape.
  case "$(cd /var/log/maran/sites && pwd -P)" in
    /var/log/maran/sites) ;;
    *) fail "/var/log/maran/sites does not resolve to itself — the root-owned site log root is a link somewhere else, and the root nginx master creates and appends to every site's access_log and error_log through it" ;;
  esac

  if [ -n "$run_maran_mode_before" ]; then
    chmod "$run_maran_mode_before" /run/maran
  fi

  echo "Step 40's directory layout verified: eleven directories, each a real directory with its own owner and mode, and every root-written log leaf under a root-only ancestry."
  echo "UNOBSERVED HERE: only the two directories the INSTALLER creates are asserted above — /var/log/maran and /var/log/maran/sites. The per-account level, /var/log/maran/sites/<account> root:root 0750, is created by the agent as root at site-create time (SiteHost::create_site_log_directory), never by the installer, because the installer does not know the accounts and an account created later must get one anyway. It is asserted where it is made: agent/crates/agent/tests/sites_on_a_real_host.rs, every_directory_the_site_log_tree_is_made_of_belongs_to_root. Nothing here can see it, and a reader must not take the eleven rows above as covering the whole tree."
  echo "UNOBSERVED HERE: the ancestry above is the state step 40 leaves behind, not a guarantee about later. Neither of the two root writers that made this directory a defect runs in this image — install.sh's own \`tee -a\` and the root nginx master's access_log/error_log opens — and the nginx one could not be gated even where it does run, because nginx re-creates those files itself on every start, reload and SIGUSR1, after every check the installer performs. The ownership asserted above is the whole defence, not a check in front of the write."
}

# assert_directory_is: one path against `uid:gid:mode`, with the three questions asked
# separately so the diagnosis names which one failed.
assert_directory_is() {
  local path="$1" expected="$2" found

  [ -e "$path" ] || [ -L "$path" ] \
    || fail "${path} was not created by the installer step that is supposed to create it"
  if [ -L "$path" ]; then
    fail "${path} is a SYMBOLIC LINK, not a directory. Its owner and its mode are the link's, never the target's, and root writing through it writes wherever it points."
  fi
  [ -d "$path" ] || fail "${path} exists but is not a directory"

  found="$(stat -c '%u:%g:%a' "$path")"
  [ "$found" = "$expected" ] \
    || fail "${path} is uid:gid:mode ${found}, not ${expected} ($(stat -c '%U:%G' "$path") by name)"
}

# assert_ancestors_are_root_only: every directory from `$1` up to / is owned by uid 0 and
# is not writable by group or other.
#
# This is the check the scratch defect needed and nothing had. A directory's own mode says
# who may act on its CONTENTS; who may replace the directory itself is decided by its
# parent, and by that parent's parent. Both of today's fixes are relocations — the same
# mode, moved to a path whose ancestors are root's — so this is the assertion that actually
# observes what changed.
assert_ancestors_are_root_only() {
  local path="$1" current="$1" owner mode
  while [ "$current" != "/" ]; do
    current="$(dirname "$current")"
    owner="$(stat -c '%u' "$current")"
    # The symbolic form, not the octal one: the octal is three digits on one host and four
    # on another (a setuid or sticky bit adds a leading digit and shifts every position),
    # so a pattern written against three characters silently stops matching the field it
    # was aimed at. `drwxr-xr-x` puts group-write at index 5 and other-write at index 8 on
    # every host there is.
    mode="$(stat -c '%A' "$current")"
    [ "$owner" -eq 0 ] \
      || fail "${current}, an ancestor of ${path}, is owned by uid ${owner} and not by root. The owner of a directory can rename an entry inside it aside and leave a symlink in its place, so ${path} is not root-only however it is moded."
    if [ "${mode:5:1}" = "w" ]; then
      fail "${current}, an ancestor of ${path}, is ${mode} — GROUP-writable. Any member of that group can replace ${path} with an entry of their own."
    fi
    if [ "${mode:8:1}" = "w" ]; then
      fail "${current}, an ancestor of ${path}, is ${mode} — WORLD-writable. Any uid can replace ${path} with an entry of its own."
    fi
  done
}

# assert_the_scratch_gate_refuses_a_directory_root_does_not_own: the inverse control for
# step 40's `assert_root_only_directory`.
#
# The positive assertion above passes on a correct host and would pass just as happily if
# that gate had been deleted — `install -d` had already made the directory right. So the
# gate is fed, one at a time, each of the three states it exists to refuse, and must abort
# on every one of them; then it is handed the real directory and must ACCEPT it, because a
# gate mutated to refuse everything passes every test that only ever gives it broken input
# (rules/testing.md).
#
# The gate is called directly rather than through create_directory_layout, and that is a
# limitation worth naming rather than hiding: create_directory_layout does `rm -rf` and
# then `install -d` BEFORE it calls the gate, so on a real host root always repairs the
# planted state and the gate never sees it. What the gate defends against is the window
# between those two calls, and against a host where root's own `install` did not do what
# root asked. Neither is reachable by planting a file. What is reachable is the gate's own
# behaviour, and that is what is measured here.
assert_the_scratch_gate_refuses_a_directory_root_does_not_own() {
  local scratch="/var/lib/maran-scratch" victim="/tmp/polygon-scratch-victim"

  rm -rf -- "$scratch" "$victim"
  install -d -o root -g root -m 0700 "$victim"
  ln -s "$victim" "$scratch"
  assert_scratch_gate_refuses "a symbolic link to a root-owned 0700 directory" 'not a real directory'

  rm -f -- "$scratch"
  install -d -o "$POLYGON_SERVICE_USER" -g "$POLYGON_SERVICE_GROUP" -m 0700 "$scratch"
  assert_scratch_gate_refuses "a directory owned by the panel uid" 'must be owned by root'

  rm -rf -- "$scratch"
  install -d -o root -g root -m 0750 "$scratch"
  assert_scratch_gate_refuses "a root-owned directory the panel GROUP can enter" 'must be owned by root'

  rm -rf -- "$scratch"
  install -d -o root -g root -m 0777 "$scratch"
  assert_scratch_gate_refuses "a world-writable root-owned directory" 'must be owned by root'

  # The inverse control: the state a correct install leaves behind must be ACCEPTED.
  rm -rf -- "$scratch" "$victim"
  install -d -o root -g root -m 0700 "$scratch"
  run_user_step 'assert_root_only_directory "'"$scratch"'"' \
    || fail "step 40's assert_root_only_directory REFUSED a real root:root 0700 directory. A gate that refuses everything proves nothing about the four refusals above."

  echo "Step 40's assert_root_only_directory refused a symlink, a panel-owned directory, a group-readable one and a world-writable one, and accepted the real thing."
}

# assert_scratch_gate_refuses: hands the planted /var/lib/maran-scratch to the installer's
# own gate in a child process — `exit 1` inside it is only observable across a process
# boundary, for the reason run_installer_step states at length — and requires both a
# non-zero status and a diagnosis containing `$2`.
#
# The message is checked and not only the status, because a gate that aborts for the wrong
# reason (a typo'd `stat`, an unbound variable under `set -u`) is indistinguishable from a
# gate that caught the attack if all you look at is the exit code.
assert_scratch_gate_refuses() {
  local what="$1" expected_message="$2" output status=0
  output="$(run_user_step 'assert_root_only_directory /var/lib/maran-scratch' 2>&1)" || status=$?

  [ "$status" -ne 0 ] \
    || fail "step 40's assert_root_only_directory ACCEPTED ${what} at /var/lib/maran-scratch. Root stages plaintext customer database dumps there."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "step 40's assert_root_only_directory refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_ancestor_walk_refuses_a_panel_owned_ancestor: the inverse control for
# `assert_ancestors_are_root_only` itself.
#
# The two positive calls above pass on a correct host, and they would pass exactly as
# happily if the walk's body were `return 0` — /var/lib is root:root 0755 on both families,
# so on a healthy image the loop never has anything to refuse. That is the shape this
# assertion exists to break: the walk is the ONLY check in this file that observes what the
# three relocations actually changed, and a check that has never once said no is a check
# nobody has seen work.
#
# So it is handed, in a child process because `fail` exits, the exact state the SFTP defect
# was — a root:root 0700 directory whose PARENT is owned by the panel uid, the shape the
# directory's own mode cannot see — and then a group-writable ancestor and a world-writable
# one. Each must be refused with a diagnosis naming what it found. Then the real
# /var/lib/maran-sftp, whose ancestors are root's, must be ACCEPTED, because a walk mutated
# to refuse everything passes every test that only ever feeds it broken input.
assert_the_ancestor_walk_refuses_a_panel_owned_ancestor() {
  local root="/tmp/polygon-ancestor-control"

  rm -rf -- "$root"
  install -d -o root -g root -m 0755 "$root"

  install -d -o "$POLYGON_SERVICE_USER" -g "$POLYGON_SERVICE_GROUP" -m 0750 "$root/panel-owned"
  install -d -o root -g root -m 0700 "$root/panel-owned/child"
  assert_ancestor_walk_refuses "$root/panel-owned/child" \
    "a root:root 0700 directory whose parent is owned by the panel uid" \
    'is owned by uid'

  install -d -o root -g root -m 0775 "$root/group-writable"
  install -d -o root -g root -m 0700 "$root/group-writable/child"
  assert_ancestor_walk_refuses "$root/group-writable/child" \
    "a root-owned directory under a GROUP-writable ancestor" \
    'GROUP-writable'

  # 0757 and not 0777: the walk checks group-write BEFORE other-write, so a 0777 ancestor
  # is refused with the GROUP diagnosis and this case would never reach the other-write
  # branch at all. Measured — the first version of this control used 0777 and the build
  # said so, which is the control doing its job on its own author. 0757 is world-writable
  # and not group-writable, so it exercises the branch it names.
  install -d -o root -g root -m 0757 "$root/world-writable"
  install -d -o root -g root -m 0700 "$root/world-writable/child"
  assert_ancestor_walk_refuses "$root/world-writable/child" \
    "a root-owned directory under a WORLD-writable ancestor" \
    'WORLD-writable'

  rm -rf -- "$root"

  # The inverse control: the real jail base, after the relocation, must be accepted.
  ( assert_ancestors_are_root_only /var/lib/maran-sftp ) \
    || fail "assert_ancestors_are_root_only REFUSED /var/lib/maran-sftp, whose ancestors are /var/lib and /. A walk that refuses everything proves nothing about the three refusals above."

  echo "The ancestor walk refused a panel-owned, a group-writable and a world-writable ancestor — the SFTP defect's own shape — and accepted the relocated jail base."
}

# assert_ancestor_walk_refuses: runs the walk against a planted path in a SUBSHELL, because
# `fail` exits the process, and requires both a non-zero status and a diagnosis containing
# `$3`.
#
# The message and not only the status, for the reason assert_scratch_gate_refuses gives: a
# walk that aborts for the wrong reason — an unbound variable under `set -u`, a `stat` that
# could not read the path — is indistinguishable from one that caught the attack if all you
# look at is an exit code.
assert_ancestor_walk_refuses() {
  local path="$1" what="$2" expected_message="$3" output status=0

  output="$( assert_ancestors_are_root_only "$path" 2>&1 )" || status=$?

  [ "$status" -ne 0 ] \
    || fail "assert_ancestors_are_root_only ACCEPTED ${what}. That is the exact shape of the SFTP chroot defect, and this walk is the only check in this file that can see it."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "assert_ancestors_are_root_only refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_log_directory_gate_sees_the_defect_it_was_written_for: the inverse control for the
# log-leaf ancestor assertions in assert_the_directory_layout_is_what_it_claims.
#
# Those assertions pass on a correct host and would pass just as happily if the walk could not
# see this defect at all, so the defect is PUT BACK — /var/log/maran is chowned to the panel uid,
# which is exactly what installer/lib/40-user.sh:29 used to do — and every root-written leaf must
# then be refused, by name, with the ownership diagnosis. It is restored afterwards and the same
# leaves must be ACCEPTED, because a walk that refuses everything proves nothing.
#
# The plant is on the REAL path and not on a stand-in tree: the generic walk control below covers
# the walk's behaviour on synthetic ancestors, and what is unproven without this is that the walk
# is pointed at the paths the defect actually lives on. A check aimed one directory to the side
# would pass that control and see nothing here.
#
# The directory's contents are untouched: `chown` on the directory alone, both ways.
#
# The plant is VERIFIED TO HAVE LANDED before anything is scored, and the restore afterwards.
# A `chown` that silently did not take — the directory already gone, a stand-in `chown` earlier
# on PATH, a read-only layer — leaves the walk looking at a correct host, and every leaf is then
# refused by nothing and this function reports the control as held while it measured a healthy
# tree. Three mutations in this repository were scored BLIND that way in one day, so the plant is
# read back with `stat` and disagreement is a failure, not a warning.
assert_the_log_directory_gate_sees_the_defect_it_was_written_for() {
  local leaf checked=0 panel_uid planted restored

  panel_uid="$(id -u "$POLYGON_SERVICE_USER")"
  chown "${POLYGON_SERVICE_USER}:${POLYGON_SERVICE_GROUP}" /var/log/maran
  planted="$(stat -c '%u' /var/log/maran)"
  [ "$planted" = "$panel_uid" ] \
    || fail "the log-leaf control tried to put the defect back by chowning /var/log/maran to the panel uid (${panel_uid}) and stat still reports uid ${planted}. The plant did not land, so every refusal below would have been measured against a HEALTHY directory and this control would report itself held while checking nothing."

  for leaf in $(installer_root_trusted_log_names); do
    assert_ancestor_walk_refuses "/var/log/maran/${leaf}" \
      "/var/log/maran/${leaf} under a PANEL-OWNED /var/log/maran — the defect exactly as step 40 used to create it" \
      'is owned by uid'
    checked=$((checked + 1))
  done
  chown "root:${POLYGON_SERVICE_GROUP}" /var/log/maran
  restored="$(stat -c '%u' /var/log/maran)"
  [ "$restored" = "0" ] \
    || fail "the log-leaf control could not restore /var/log/maran to root (stat reports uid ${restored}). The image is being left carrying the defect this control plants, and the acceptance pass below would be measured against it."

  [ "$checked" -eq 3 ] \
    || fail "the log-leaf control planted the defect and then checked ${checked} leaves, not the three root writes install.sh, and nginx's master, actually make. A control that checks nothing passes."

  for leaf in $(installer_root_trusted_log_names); do
    ( assert_ancestors_are_root_only "/var/log/maran/${leaf}" ) \
      || fail "assert_ancestors_are_root_only REFUSED /var/log/maran/${leaf} on a correctly installed host, whose ancestors are /var/log/maran (root:<service group> 0750), /var/log and /. The refusals above prove nothing if the walk refuses the real thing too."
  done

  echo "The log-leaf ancestor check was shown the original defect — /var/log/maran owned by the panel — refused all three root-written leaves by name, and accepted them again once the directory was root's. The plant and the restore were both read back with stat, so a chown that did not take fails here instead of scoring a healthy directory as a caught defect."
  # What this control does NOT observe, in its own output rather than only in a comment above it,
  # because a check that reads like coverage it does not have is the defect this file exists for.
  echo "UNOBSERVED HERE: the nginx half of this defect cannot be gated by anything, here or in the installer. The root nginx MASTER creates and re-opens nginx-access.log and nginx-error.log itself, at the fixed paths installer/nginx/maran.conf names, on every start, every reload and every SIGUSR1 — always AFTER install.sh's harden_log_directory and after step 40's assertion have finished. So what is proved above is a statement about the directory's ownership at install time, which is the ENTIRE defence; there is no moment at which a check could stand between a planted symlink and nginx's open. Nothing in this image, and nothing in this repository, watches those two opens. A reviewer wanting that evidence must take it from a booted host: nginx running, its master's /proc/<pid>/fd entries resolving to regular files inside /var/log/maran, and \`ls -l\` on the two names showing root-owned regular files rather than links."
}

# assert_the_site_log_root_check_can_see_each_property_break: the inverse control for the
# `/var/log/maran/sites:0:0:750` row of the directory layout list.
#
# That row passes on a correctly installed image and would pass exactly as happily if
# `assert_directory_is` had gone blind on any one of the three properties it claims to check
# — a `stat` format string losing a field, an expectation string that no longer matches what
# the installer writes, a comparison that became a prefix test. The row is the ONLY thing in
# CI that observes the containment the F-1 escalation fix depends on, so "it is green" has to
# mean something stronger than "nobody has broken it yet".
#
# So each of the three properties is broken ON THE REAL PATH, one at a time, and the check
# must refuse with a diagnosis naming the triple it found:
#
#   owner  the panel uid instead of root. The owner of a directory may rename any entry inside
#          it aside and leave a symbolic link at that name, so a panel-owned site log root
#          gives the panel uid the root nginx master's O_APPEND back — the whole escalation,
#          one level up from where it used to live.
#   group  root:<service group>, the parent's group, which is the realistic regression: somebody makes
#          this directory look like /var/log/maran. At 0750 that hands the panel uid read on
#          every hosting customer's access log, which the panel is deliberately not allowed to
#          take off the disk — it asks the agent, which streams it.
#   mode   0755. Defence in depth rather than the front line, since /var/log/maran above it is
#          0750 root:<service group> and `other` cannot traverse it today — but this directory's mode is
#          what stops a customer walking into a NEIGHBOUR's log directory if that parent ever
#          widens, and a check that cannot see the mode would not report it when it does.
#
# Then the directory is put back and the same check must ACCEPT it, because a check mutated to
# refuse everything passes every test that only ever hands it broken input (rules/testing.md).
#
# Every plant and the restore are read back with `stat` before anything is scored. A `chown`
# or `chmod` that silently did not take leaves the check looking at a healthy directory, and
# this function would then report itself held while measuring nothing — the exact shape three
# mutations in this repository were scored blind by in one day.
assert_the_site_log_root_check_can_see_each_property_break() {
  local sites="/var/log/maran/sites" panel_uid panel_gid found refusals=0

  panel_uid="$(id -u "$POLYGON_SERVICE_USER")"
  panel_gid="$(id -g "$POLYGON_SERVICE_USER")"

  chown "$panel_uid":root "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "${panel_uid}:0:750" ] \
    || fail "the site log root control tried to break the OWNER by chowning ${sites} to uid ${panel_uid} and stat reports ${found}. The plant did not land, so the refusal below would have been measured against a healthy directory."
  assert_directory_check_refuses "$sites" "0:0:750" \
    "${sites} owned by the panel uid instead of root — the owner of a directory can rename an entry inside it aside and leave a symlink, which is the escalation this path exists to contain" \
    "is uid:gid:mode ${found}, not 0:0:750"
  refusals=$((refusals + 1))

  chown root:"$panel_gid" "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "0:${panel_gid}:750" ] \
    || fail "the site log root control tried to break the GROUP by chowning ${sites} to gid ${panel_gid} and stat reports ${found}. The plant did not land."
  assert_directory_check_refuses "$sites" "0:0:750" \
    "${sites} carrying the panel group like its parent, which at 0750 gives the panel uid read on every customer's access log" \
    "is uid:gid:mode ${found}, not 0:0:750"
  refusals=$((refusals + 1))

  chown root:root "$sites"
  chmod 0755 "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "0:0:755" ] \
    || fail "the site log root control tried to break the MODE by chmodding ${sites} to 0755 and stat reports ${found}. The plant did not land."
  assert_directory_check_refuses "$sites" "0:0:750" \
    "${sites} at 0755, world-traversable — the bit that stops a customer walking into a neighbour's log directory if the tree above it ever widens" \
    "is uid:gid:mode ${found}, not 0:0:750"
  refusals=$((refusals + 1))

  chown root:root "$sites"
  chmod 0750 "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "0:0:750" ] \
    || fail "the site log root control could not restore ${sites} to root:root 0750 (stat reports ${found}). The image would ship carrying the defect this control plants, and every site the polygon suites create would write its logs into it."

  [ "$refusals" -eq 3 ] \
    || fail "the site log root control scored ${refusals} refusals, not the three properties — owner, group and mode — it exists to prove are observable. A control that checks fewer than it says passes."

  # The inverse control: the state a correct install leaves behind must be ACCEPTED.
  ( assert_directory_is "$sites" "0:0:750" ) \
    || fail "assert_directory_is REFUSED the real ${sites} at root:root 0750, which is what installer/lib/40-user.sh creates. A check that refuses everything proves nothing about the three refusals above."

  echo "The site log root's row was shown each of its three properties broken in turn on the real path — owner to the panel uid, group to the panel group, mode to 0755 — refused each by name with the triple it found, and accepted root:root 0750 again. Every plant and the restore were read back with stat, so a chown that did not take fails here instead of being scored as a caught defect."
  echo "UNOBSERVED HERE: this proves the CHECK can see a break, on the path the installer writes, at install time. It is not a statement about later. The root nginx master creates and re-opens every site's access_log and error_log under this directory itself, on every start, reload and SIGUSR1, always after this script has finished — so the ownership and mode asserted here are the ENTIRE defence, not a gate standing in front of the open. What the agent does one level down, at /var/log/maran/sites/<account>, is asserted only in agent/crates/agent/tests/sites_on_a_real_host.rs."
}

# assert_directory_check_refuses: hands a planted path to `assert_directory_is` in a SUBSHELL,
# because `fail` exits the process, and requires both a non-zero status and a diagnosis
# containing `$4`.
#
# The message and not only the status, for the reason assert_scratch_gate_refuses gives at
# length: a check that aborts for the wrong reason — an unbound variable under `set -u`, a
# `stat` that could not read the path — is indistinguishable from one that caught the defect
# if all you look at is an exit code. Here the expected message carries the TRIPLE the plant
# produced, so a check that noticed some other difference and reported some other value does
# not score as a catch either.
assert_directory_check_refuses() {
  local path="$1" expected="$2" what="$3" expected_message="$4" output status=0

  output="$( assert_directory_is "$path" "$expected" 2>&1 )" || status=$?

  [ "$status" -ne 0 ] \
    || fail "assert_directory_is ACCEPTED ${what}. That row is the only thing in CI that observes the containment the site-log escalation fix depends on."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "assert_directory_is refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_release_staging_directory_is_root_only: step 50's staging directory, the one
# an artifact is downloaded into, checksummed in and extracted from.
#
# It is the highest-value directory in the whole install: the file that comes out of it is
# /usr/local/maran/agent/maran-agent, which maran-agent.service starts as root. While it
# lived under /var/lib/maran the api's uid owned it, and because every archive used to be
# checksummed before ANY of them was extracted, the api could replace agent.tar.gz in that
# window and install a root backdoor.
#
# Step 50 is not RUN here — it downloads over HTTPS from the release server and this build
# has no network trust to spend on that — so `prepare_staging_dir` is called on its own.
# That is the whole of the directory half of the fix, and it is the half a polygon can see.
assert_the_release_staging_directory_is_root_only() {
  # The path comes from the step's own constant, and is then held against the literal
  # below. Two different jobs again: asking the step where it stages means a move of the
  # directory is diagnosed as a MOVE — "step 50 now stages in X" — instead of arriving as
  # "the directory nobody created is missing", which is what a hardcoded path alone says
  # and is the least useful sentence a security check can produce. The literal is what
  # makes it a check rather than a description: without it the ancestor rule below would
  # be the only thing standing between the staging directory and a quiet relocation to
  # somewhere that happens to satisfy it.
  local staging
  staging="$(run_staging_step 'printf "%s" "$MARAN_ARTIFACT_TMP"')" \
    || fail "step 50 does not define MARAN_ARTIFACT_TMP; this check cannot see where release artifacts are staged"
  [ "$staging" = "/var/lib/maran-artifact-staging" ] \
    || fail "step 50 stages release artifacts in ${staging}, not /var/lib/maran-artifact-staging. That directory is where an archive is checksummed and extracted, and the file that comes out of it is the binary maran-agent.service runs as root — a move of it is a security change, not a tidy-up."

  run_staging_step 'prepare_staging_dir' \
    || fail "step 50's prepare_staging_dir failed inside the polygon"

  assert_directory_is "$staging" "0:0:700"
  assert_ancestors_are_root_only "$staging"

  # It must not be under /var/lib/maran, for the reason the scratch must not be.
  case "$(cd "$staging" && pwd -P)" in
    /var/lib/maran/*) fail "${staging} resolves underneath /var/lib/maran, which the panel uid owns — root would be extracting a root-run binary out of a directory the api can rearrange" ;;
  esac

  # And no unit may hand it to an unprivileged process. This is a text assertion about the
  # units and says so: nothing here boots systemd.
  local unit
  for unit in "$AGENT_UNIT" "$API_UNIT"; do
    if grep '^ReadWritePaths=' "$unit" | grep -q 'maran-artifact-staging'; then
      fail "$(basename "$unit") lists /var/lib/maran-artifact-staging in ReadWritePaths=. The release staging directory is the installer's alone; a running service that can write it can replace the agent binary before it is extracted."
    fi
  done

  echo "Step 50's prepare_staging_dir built /var/lib/maran-artifact-staging root:root 0700 under root-only ancestors, and no unit makes it writable."
}

# run_staging_step: step 50's code in a child, the way install.sh runs it.
#
# SCRIPT_DIR is exported because 50-artifacts.sh resolves the release signing key against
# it in a top-level `readonly`, and under `set -u` an unset variable would kill the source
# before a single function was defined — a failure that looks nothing like the one this
# check is about. It points at the installer tree this image carries; nothing below reads
# the key, and no check here depends on it existing.
run_staging_step() {
  local snippet="$1" path_prefix="${2:-}"
  local search_path="$PATH"
  if [ -n "$path_prefix" ]; then
    search_path="${path_prefix}:${PATH}"
  fi
  SCRIPT_DIR="$INSTALLER_ROOT" PATH="$search_path" bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$ARTIFACTS_STEP" "$snippet"
}

# assert_the_staging_gate_refuses_what_install_left_wrong: the inverse control for the gate
# inside prepare_staging_dir.
#
# It cannot be driven by planting a directory, and the reason is worth writing down because
# it is the same reason the scratch gate needed its own entry point. prepare_staging_dir
# does `rm -rf` and then `install -d -o root -g root -m 0700` before it looks at anything,
# so root repairs whatever was planted and the gate is handed a correct directory every
# time. Feeding it a violation means the CREATION, not the plant, has to go wrong — which
# is exactly the case the gate is written for: `install -d` exits 0 on a path it did not
# make the way it was asked.
#
# So an `install` stand-in goes first on the step's PATH, using the same mechanism
# run_nginx_step already uses to put a hostile binary in front of a step. The stand-in is
# a fixture, not a mutation of the installer: 50-artifacts.sh is byte-for-byte the shipped
# file in every run below, and the thing being measured is whether its gate looks at the
# result of a command instead of trusting it.
#
# Two states, because they fail different lines: a directory with the wrong mode, and a
# symlink — which `install -d` follows and `[ -d ]` would call a directory.
assert_the_staging_gate_refuses_what_install_left_wrong() {
  local shim_dir="/tmp/polygon-staging-shim" victim="/tmp/polygon-staging-victim"
  rm -rf -- "$shim_dir" "$victim"
  install -d -m 0755 "$shim_dir"

  # A stand-in that ignores -o/-g/-m and leaves the directory world-writable.
  cat > "${shim_dir}/install" <<'SHIM'
#!/usr/bin/env bash
# Polygon fixture: an `install` that creates the directory it was asked for and then
# ignores the ownership and mode it was asked for. It stands in for the host on which
# root's own tools did not do what root asked, which is the only state the staging gate
# can ever meet on a real machine.
set -euo pipefail
: > /tmp/polygon-staging-shim.ran
target="${@: -1}"
/usr/bin/install -d -m 0777 "$target"
SHIM
  chmod 755 "${shim_dir}/install"
  assert_staging_gate_refuses "$shim_dir" "a world-writable staging directory" 'must be owned by root with mode 0700'

  # A stand-in that leaves a symbolic link where the directory should be.
  install -d -o root -g root -m 0700 "$victim"
  cat > "${shim_dir}/install" <<'SHIM'
#!/usr/bin/env bash
# Polygon fixture: an `install` that leaves a symbolic link at the staging path instead
# of a directory. `[ -d ]` alone answers yes to this, which is why the gate asks `[ -L ]`
# first; this is the fixture that makes that ordering matter.
set -euo pipefail
: > /tmp/polygon-staging-shim.ran
target="${@: -1}"
ln -s /tmp/polygon-staging-victim "$target"
SHIM
  chmod 755 "${shim_dir}/install"
  assert_staging_gate_refuses "$shim_dir" "a symbolic link at the staging path" 'is not a real directory'

  rm -rf -- "$shim_dir" "$victim" /tmp/polygon-staging-shim.ran
  # The inverse control: with the real `install` back on PATH the step must SUCCEED.
  run_staging_step 'prepare_staging_dir' \
    || fail "prepare_staging_dir refused the directory its own real install(1) had just built. A gate that refuses everything proves nothing about the two refusals above."
  assert_directory_is /var/lib/maran-artifact-staging "0:0:700"

  echo "Step 50's staging gate refused a world-writable directory and a symbolic link, and accepted the one the real install builds."
}

# assert_staging_gate_refuses: runs prepare_staging_dir with `$1` first on PATH and
# requires a non-zero status and a diagnosis containing `$2`.
#
# The stand-in is checked to have actually been reached. A PATH prefix that does not take
# effect — a stand-in that is not executable, a step that calls install by absolute path —
# turns this into a run of the ordinary code that passes, and a negative test that passes
# for the wrong reason is the exact failure mode this file's header is about.
assert_staging_gate_refuses() {
  local shim_dir="$1" what="$2" expected_message="$3" output status=0

  rm -rf -- /var/lib/maran-artifact-staging /tmp/polygon-staging-shim.ran
  output="$(run_staging_step 'prepare_staging_dir' "$shim_dir" 2>&1)" || status=$?

  # The vacuity guard, on the axis that can go blind: whether the stand-in was reached at
  # all. A PATH prefix that does not take effect turns this into an ordinary, passing run
  # of the step, and the negative test then reports a refusal that never happened.
  [ -f /tmp/polygon-staging-shim.ran ] \
    || fail "the install(1) stand-in in ${shim_dir} was never executed, so prepare_staging_dir met no violation at all and the refusal below would have been about nothing"

  [ "$status" -ne 0 ] \
    || fail "step 50's prepare_staging_dir ACCEPTED ${what}. The archive extracted from that directory becomes /usr/local/maran/agent/maran-agent, which systemd runs as root."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "prepare_staging_dir refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_agent_unit_declares_its_writable_roots: the systemd half, and the limits of
# asking a text file about a sandbox.
#
# Two properties, both of which a wrong answer would make invisible on a running host:
#
#   1. Every writable root the agent uses is named in ReadWritePaths=. This is checked in
#      full against agent_paths.rs by `maran structure` (check 20); what is added here is
#      the four paths today's two fixes touch, stated by name, so a build of this image
#      fails if one of them is dropped even where the agent's constant went with it.
#   2. EnvironmentFile= appears BEFORE Environment=PATH=. systemd applies these in file
#      order, so that ordering is what stops a PATH= line added to /etc/maran/agent.env
#      from winning against the deliberately empty PATH. Reverse the two lines and the
#      unit still parses, still starts and still looks right — the protection is the
#      ORDER, and nothing else in this repository observes it.
assert_the_agent_unit_declares_its_writable_roots() {
  local writable_set entry root covered
  writable_set="$(grep '^ReadWritePaths=' "$AGENT_UNIT" | head -1 | sed 's/^ReadWritePaths=//')"
  [ -n "$writable_set" ] \
    || fail "$(basename "$AGENT_UNIT") has no ReadWritePaths= line — this check cannot observe the unit's writable set"

  # The roots the agent actually writes, named by hand so that dropping one from the unit fails
  # this build even where the agent's own constant went with it.
  #
  # `/var/lib/maran` is deliberately NOT in this list, and the deletion is the point rather than a
  # tidy-up: nothing under agent/crates/*/src builds a path there, the directory belongs to the
  # unprivileged panel uid (owned by the service account and its group, 0750), and an entry granting the root daemon write access
  # to a directory the panel owns is the exact shape the scratch and the SFTP jail were RELOCATED
  # out of after measured escalations. It was removed from the unit, and the sentence this check
  # used to print about it — "the agent writes there" — was false.
  #
  # `/var/log/maran/sites` and `/var/lib/maran-sftp` are here because they were missing: the two
  # newest roots were absent from the list whose whole purpose is to notice an absent root. The
  # site log root is the most load-bearing of them, since the root nginx master opens every site's
  # access_log and error_log through it.
  local checked=0
  for root in \
    /home \
    /home/.maran-restore \
    /var/lib/maran-sftp \
    /var/lib/maran-scratch \
    /var/backups/maran \
    /var/log/maran/sites \
    /run/maran; do
    covered=0
    for entry in $writable_set; do
      entry="${entry#-}"
      case "$root" in
        "$entry"|"$entry"/*) covered=1 ;;
      esac
    done
    [ "$covered" -eq 1 ] \
      || fail "$(basename "$AGENT_UNIT") ReadWritePaths= does not cover ${root}, which the agent writes. Under this sandbox it would get EROFS at runtime on a real server, with a clean install behind it. If the agent has genuinely stopped writing there, remove the root from THIS list in the same change and say why — a grant nothing writes is a permanent one nobody revisits."
    checked=$((checked + 1))
  done
  # The vacuity guard, on the axis that can go blind: this loop is one list, a `for` over an empty
  # list exits 0, and a root deleted from it removes a check with no output changing anywhere.
  # Raise it with the list.
  [ "$checked" -eq 7 ] \
    || fail "the writable-root list checked ${checked} roots, not the 7 the agent writes. A root has been dropped from the list, and a check that is not in the list is not a check that failed — it is one that no longer exists."

  local environment_file_line path_line
  environment_file_line="$(grep -n '^EnvironmentFile=' "$AGENT_UNIT" | head -1 | cut -d: -f1)"
  path_line="$(grep -n '^Environment=PATH=' "$AGENT_UNIT" | head -1 | cut -d: -f1)"
  [ -n "$environment_file_line" ] \
    || fail "$(basename "$AGENT_UNIT") has no EnvironmentFile= line; the agent would start with no MARAN_AGENT_ALLOW_UID and deny the API every request"
  [ -n "$path_line" ] \
    || fail "$(basename "$AGENT_UNIT") has no 'Environment=PATH=' line; the daemon would inherit systemd's compiled-in PATH, which begins with the directories this product installs into"
  [ "$environment_file_line" -lt "$path_line" ] \
    || fail "$(basename "$AGENT_UNIT") sets Environment=PATH= on line ${path_line}, BEFORE EnvironmentFile= on line ${environment_file_line}. systemd applies these in file order, so in this order a PATH= line written into /etc/maran/agent.env overrides the empty PATH the unit intends."

  echo "maran-agent.service names all seven writable roots the agent uses, and loads EnvironmentFile= before it empties PATH."
  echo "UNOBSERVED HERE: this image boots no systemd. Nothing above ran ExecStartPre=, mounted a"
  echo "ReadWritePaths= sandbox, or applied UMask=0027 — those are claims about a booted host, and the"
  echo "two lines above are claims about a text file. What would settle them is one boot per family:"
  echo "'systemd-analyze verify maran-agent.service', then 'systemctl show -p ReadWritePaths -p UMask"
  echo "maran-agent.service' read back from the running manager, a file created by the daemon and"
  echo "stat'd for 0640, a write attempted outside the set and seen to fail with EROFS, and a scratch"
  echo "directory planted before a restart and seen to be gone after it."
}


# run_preflight_step: runs step 10's code THE WAY install.sh runs it — a plain command in a child
# shell with `set -euo pipefail`, the step file sourced, and MARAN_PANEL_PORT exported first
# because the step's `readonly MARAN_REQUIRED_PORTS="${MARAN_PANEL_PORT:?…}"` refuses to be sourced
# without it — and hands back its status and its output.
#
# The port comes from install.sh, never from a literal here, for the reason
# assert_panel_port_has_one_authority exists.
#
# The optional PATH prefix is how the free-space check is driven: `check_backup_space` reads the
# host with `df`, and a container's `df` reports whatever the build host happens to have free, so
# the only way to see BOTH of that function's branches is to control its answer. Same mechanism
# run_nginx_step uses for a hostile nginx and the staging assertion uses for `install(1)` — the
# STEP is the real one, unmodified; the tool it calls is the stand-in.
run_preflight_step() {
  local snippet="$1" path_prefix="${2:-}"
  local search_path="$PATH"
  [ -z "$path_prefix" ] || search_path="${path_prefix}:${PATH}"
  MARAN_PANEL_PORT="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")" \
    PATH="$search_path" \
    bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$PREFLIGHT_STEP" "$snippet"
}

# df_stand_in: writes, into directory `$1`, a `df` that reports `$2` MiB available and records that
# it was reached in `$1/df.ran`.
#
# The marker is not decoration. A PATH prefix that failed to take effect would leave the REAL `df`
# answering, and on a build host with little free space the "warns" case below would then pass
# without the stand-in having run at all — a refusal that never happened, scored as one. The same
# trap the step 50 staging assertion names.
#
# It prints the header line and one data line because that is what `df -P` guarantees and what the
# step's `awk 'NR==2 { print $4 }'` reads; anything else would be this script inventing a format the
# installer does not parse.
df_stand_in() {
  local directory="$1" available_mb="$2"
  cat > "${directory}/df" <<STAND_IN
#!/usr/bin/env bash
touch "${directory}/df.ran"
echo "Filesystem 1M-blocks Used Available Capacity Mounted-on"
echo "polygon-stand-in 102400 102400 ${available_mb} 100% /"
STAND_IN
  chmod 0755 "${directory}/df"
  rm -f "${directory}/df.ran"
}

# assert_preflight_warns_about_backup_space_without_refusing: step 10's backup free-space check,
# in all three of the states it can be in, driven through the installer's own function.
#
# Three properties, and the third is the one that would be a defect rather than a missing feature:
#
#   1. Below the floor it WARNS and names the number it measured. A check that says "not enough
#      space" without the figure is not actionable, and the plan asks for the number by name.
#   2. Above the floor it accepts. This is the inverse control rules/testing.md requires of every
#      refusing gate: a check mutated to warn unconditionally passes every test that only ever
#      hands it a starved filesystem.
#   3. It NEVER fails the install. `_PREFLIGHT_FAILED` is read back out of the child after the warn
#      case, because a warning that quietly sets that flag is a refusal wearing a warning's words,
#      and an operator who intends to mount a backup volume after installing would be locked out of
#      their own server by it.
#
# It also runs the function against the REAL `df` on the real host, with no stand-in, to prove the
# thing parses this family's actual `df -Pm` output — the stand-in's format is this script's belief
# about `df`, and a belief is not the tool.
assert_preflight_warns_about_backup_space_without_refusing() {
  local shim_dir output
  shim_dir="$(mktemp -d)"

  # 1. Starved: one mebibyte free, far under the step's floor.
  df_stand_in "$shim_dir" 1
  output="$(run_preflight_step 'check_backup_space; echo "PREFLIGHT_FAILED=${_PREFLIGHT_FAILED}"' "$shim_dir" 2>&1)" \
    || fail "check_backup_space exited non-zero on a starved filesystem; it is a warning and must never abort the step. It said: ${output}"
  [ -f "${shim_dir}/df.ran" ] \
    || fail "the df stand-in in ${shim_dir} was never executed, so check_backup_space measured the real host and the warning below would be about nothing"
  case "$output" in
    *"PREFLIGHT WARN"*) ;;
    *) fail "check_backup_space did not warn on a filesystem with 1 MiB free. It said: ${output}" ;;
  esac
  case "$output" in
    *"1 MiB free"*) ;;
    *) fail "check_backup_space warned without naming the number it measured, which is the one thing the warning is for. It said: ${output}" ;;
  esac
  case "$output" in
    *"/var/backups/maran"*) ;;
    *) fail "check_backup_space warned without naming the backup root, so an operator cannot tell which filesystem to enlarge. It said: ${output}" ;;
  esac
  case "$output" in
    *"PREFLIGHT_FAILED=0"*) ;;
    *) fail "check_backup_space set the preflight failure flag. It is documented as a warning: an operator who plans to mount a backup volume after installing would be refused an install by this. It said: ${output}" ;;
  esac

  # 2. Ample: comfortably over the floor. The gate must ACCEPT.
  df_stand_in "$shim_dir" 999999
  output="$(run_preflight_step 'check_backup_space' "$shim_dir" 2>&1)" \
    || fail "check_backup_space exited non-zero on a filesystem with plenty of space: ${output}"
  [ -f "${shim_dir}/df.ran" ] \
    || fail "the df stand-in in ${shim_dir} was never executed on the ample case"
  case "$output" in
    *"PREFLIGHT WARN"*) fail "check_backup_space warned about a filesystem with 999999 MiB free, so it warns whatever it is handed and the warning above proves nothing. It said: ${output}" ;;
    *"PREFLIGHT OK"*) ;;
    *) fail "check_backup_space said neither OK nor WARN on an ample filesystem: ${output}" ;;
  esac

  # 3. The real tool on the real host: whatever this build host has free, the function must parse
  #    this family's own `df -Pm` and produce one of its two verdicts rather than an empty number.
  output="$(run_preflight_step 'check_backup_space' 2>&1)" \
    || fail "check_backup_space exited non-zero against this family's real df: ${output}"
  case "$output" in
    *"PREFLIGHT OK"*|*"PREFLIGHT WARN"*) ;;
    *) fail "check_backup_space produced neither verdict against the real df on this family, so its awk does not read this df's output: ${output}" ;;
  esac
  case "$output" in
    *": 0 MiB free"*|*"only 0 MiB free"*)
      fail "check_backup_space read 0 MiB off this family's real df, which means its column arithmetic is wrong here rather than that the disk is full: ${output}" ;;
  esac

  rm -rf "$shim_dir"
  echo "Step 10's backup free-space check warns with the number, accepts an ample filesystem, never fails the install, and parses this family's real df."
  echo "UNOBSERVED HERE: three of the four cases above were answered by a df stand-in, not by a"
  echo "filesystem. What this image cannot show is the check on a host whose backup volume is"
  echo "mounted at /var/backups AFTER the install: step 10 runs before step 40 creates the"
  echo "directory, so it measures the nearest existing ancestor and its number is then about the"
  echo "wrong filesystem — labelled in the message, never corrected. What would settle it is one"
  echo "boot per family: a real volume mounted there, an install run, and the warning read back."
}

# The account directory and the two files planted under the backup root. Named here because the
# assertion below and its inverse control must plant and look for exactly the same things, and a
# check that hunts for a name nothing planted is the shape that reports a protection as held.
readonly PLANTED_BACKUP_ACCOUNT="/var/backups/maran/polykeep"
readonly PLANTED_BACKUP_ARTIFACT="${PLANTED_BACKUP_ACCOUNT}/00000000-0000-0000-0000-0000000000ff.tar.gz"
readonly PLANTED_BACKUP_SIDECAR="${PLANTED_BACKUP_ACCOUNT}/00000000-0000-0000-0000-0000000000ff.json"
readonly PLANTED_BACKUP_BYTES="polygon planted artifact — a customer's only copy"

# plant_a_backup_artifact: an account directory and two files under the REAL backup root, in the
# layout ops::backup writes (<root>/<account>/<id>.tar.gz plus its sidecar) and at the modes it
# writes them at. Real files at the real path, because the whole question is what a root process
# running `rm -rf` by name does to them.
plant_a_backup_artifact() {
  install -d -o root -g root -m 0700 /var/backups/maran
  install -d -o root -g root -m 0700 "$PLANTED_BACKUP_ACCOUNT"
  printf '%s\n' "$PLANTED_BACKUP_BYTES" > "$PLANTED_BACKUP_ARTIFACT"
  printf '{"id":"00000000-0000-0000-0000-0000000000ff"}\n' > "$PLANTED_BACKUP_SIDECAR"
  chmod 0600 "$PLANTED_BACKUP_ARTIFACT" "$PLANTED_BACKUP_SIDECAR"
  [ -f "$PLANTED_BACKUP_ARTIFACT" ] && [ -f "$PLANTED_BACKUP_SIDECAR" ] \
    || fail "the planted backup artifact was not written, so everything below would be measuring an empty directory"
}

# backup_root_survives: runs the uninstaller at <path> the way main() runs it — the deleters, in
# main()'s order — and answers whether the planted artifact is still there afterwards, byte for
# byte. Prints a diagnosis and returns 1 when it is not.
#
# A function that RETURNS rather than one that calls fail, because it is used twice and in opposite
# directions: once on the real uninstaller, which must survive it, and once on a copy carrying the
# one line this whole promise is about, which must not.
#
# `remove_var_lib` and `remove_logs` are the two deleters that run `rm -rf` on a named directory,
# which is where a fifth sibling would be added by whoever added it. `note_backups_kept` runs after
# them, in main()'s order, so its count is a count taken after the deleting is done.
backup_root_survives() {
  local uninstaller="$1" output status=0
  output="$(bash -c 'set -euo pipefail
. "$1"
remove_var_lib
remove_logs
note_backups_kept' _ "$uninstaller" 2>&1)" || status="$?"

  if [ "$status" -ne 0 ]; then
    printf 'the uninstaller exited %s while deleting; it said: %s\n' "$status" "$output"
    return 1
  fi
  if [ ! -d "$PLANTED_BACKUP_ACCOUNT" ]; then
    printf 'the uninstaller DELETED %s. That directory is a customer'"'"'s only copy of their files and their databases, and an uninstall is not a decommission. It said: %s\n' \
      "$PLANTED_BACKUP_ACCOUNT" "$output"
    return 1
  fi
  if [ ! -f "$PLANTED_BACKUP_ARTIFACT" ]; then
    printf 'the uninstaller DELETED the artifact %s while leaving its directory. It said: %s\n' \
      "$PLANTED_BACKUP_ARTIFACT" "$output"
    return 1
  fi
  if ! printf '%s\n' "$PLANTED_BACKUP_BYTES" | cmp -s - "$PLANTED_BACKUP_ARTIFACT"; then
    printf 'the artifact at %s survived the uninstall with different bytes in it\n' "$PLANTED_BACKUP_ARTIFACT"
    return 1
  fi
  printf '%s' "$output"
  return 0
}

# assert_the_uninstaller_keeps_the_backup_root: the uninstaller leaves /var/backups/maran alone,
# says so naming the path and the count, and this assertion can tell the difference.
#
# The plan's words: an uninstaller that removes a customer's only copy of their data "is the worst
# defect this product could ship, and it would be one line". Today the line is absent by omission —
# `remove_var_lib` deletes three siblings of the backup root by name, and a fourth `rm -rf` beside
# them would read like its neighbours and nothing in this repository would have gone red for it.
#
# So this is proved in both directions, which for a promise about ABSENCE is not optional: every
# positive assertion here passes just as happily against an uninstaller that never had the promise,
# because the deletion is what has to be caught and there is none to catch. The inverse control puts
# that one line into a COPY of the uninstaller — verified landed with `cmp` and a grep count, since
# a mutation that silently failed to apply is how three mutations in this repository were scored
# blind — and requires the check above to refuse it, naming the path it lost.
#
# UNOBSERVED HERE: this is the uninstaller's shell functions against a planted file, not an
# uninstall of a real install. No systemd, no maran-api, no agent, no operator at a terminal, and
# `confirm` reaches no tty in a build layer so `remove_logs` takes its keep-the-data branch. What a
# booted host would have to show instead is one real install, one real backup taken through the
# panel, `bash uninstall.sh --yes`, and the artifact still on the disk afterwards with the
# transcript naming it.
assert_the_uninstaller_keeps_the_backup_root() {
  local mutant output diagnosis planted_lines restored

  plant_a_backup_artifact

  output="$(backup_root_survives "$UNINSTALLER")" \
    || fail "the real uninstaller did not leave the backup root alone: ${output}"
  case "$output" in
    *"/var/backups/maran"*) ;;
    *) fail "the uninstaller kept the backup root but never named it. An operator finishing a decommission has to be told where the data it refused to delete is: ${output}" ;;
  esac
  case "$output" in
    *"2 file(s)"*) ;;
    *) fail "the uninstaller did not report the two planted artifacts as a count; a report that says 'backups were kept' without saying how many is not something an operator can act on: ${output}" ;;
  esac

  # The inverse control: the one line, in a copy, verified to have landed before it is scored.
  mutant="$(mktemp)"
  sed 's|^  rm -rf /var/lib/maran$|  rm -rf /var/lib/maran\n  rm -rf /var/backups/maran|' \
    "$UNINSTALLER" > "$mutant"
  planted_lines="$(grep -c '^  rm -rf /var/backups/maran$' "$mutant" || true)"
  [ "$planted_lines" -eq 1 ] \
    || fail "the inverse control planted ${planted_lines} deletions of the backup root instead of exactly one — the anchor line in uninstall.sh has changed, and this control would have scored a mutation that never applied"
  if cmp -s "$UNINSTALLER" "$mutant"; then
    fail "the inverse control's copy of the uninstaller is identical to the original, so the deletion below never applied and the refusal it asks for would be about nothing"
  fi

  if diagnosis="$(backup_root_survives "$mutant")"; then
    rm -f "$mutant"
    fail "an uninstaller carrying 'rm -rf /var/backups/maran' PASSED this assertion, so the assertion cannot see the deletion it exists for and its green verdict above means nothing"
  fi
  case "$diagnosis" in
    *"$PLANTED_BACKUP_ACCOUNT"*) ;;
    *) fail "the inverse control was refused, but the diagnosis does not name what was lost: ${diagnosis}" ;;
  esac
  rm -f "$mutant"

  # Everything the mutant and the real deleters removed, put back by the installer's own steps
  # rather than by literals here: the backup root and the panel layout from step 40, the SFTP jail
  # base from step 86 (remove_var_lib deletes it, and the suites that follow this build need it).
  run_user_step 'create_directory_layout' >/dev/null \
    || fail "step 40's create_directory_layout could not restore the layout this assertion's deleters removed"
  run_installer_step 'install_sftp_prerequisites' >/dev/null \
    || fail "step 86's install_sftp_prerequisites could not restore the SFTP jail base this assertion's deleters removed"
  for restored in /var/backups/maran /var/lib/maran /var/lib/maran-scratch /var/lib/maran-sftp; do
    [ -d "$restored" ] \
      || fail "${restored} is missing after this assertion restored the layout; the image would ship without it and every suite that needs it would fail for the wrong reason"
  done
  rm -rf "$PLANTED_BACKUP_ACCOUNT"

  echo "The uninstaller keeps /var/backups/maran, names it and counts what is in it — and an uninstaller that deletes it is refused here."
  echo "UNOBSERVED HERE: this is the uninstaller's shell functions against a planted file, not an"
  echo "uninstall of a real install. No systemd, no maran-api, no agent, and no operator at a"
  echo "terminal — confirm() reaches no tty in a build layer, so remove_logs took its keep-the-data"
  echo "branch and the delete-the-logs branch is not exercised here at all. What would settle it is"
  echo "one real install per family, one real backup taken through the panel, bash uninstall.sh"
  echo "--yes, and the artifact still on the disk afterwards with the transcript naming it."
}
# ---------------------------------------------------------------------------------------------
# The uninstall census. See assert_the_uninstaller_removes_every_drop_in_the_installer_creates.
# ---------------------------------------------------------------------------------------------

# The directories this census is about, and the reason it is these six and not everything the
# installer touches.
#
# Every one of them is a directory the DISTRIBUTION owns, into which a product drops a file that
# some daemon then reads on a schedule or at boot: logrotate reads /etc/logrotate.d nightly from
# cron, systemd reads /etc/systemd/system and /etc/tmpfiles.d at boot, libpam reads /etc/pam.d per
# authentication, nginx reads /etc/nginx/conf.d on reload, sudo reads /etc/sudoers.d per command.
# So a file left behind here is not untidiness — it is a live effect on a host that no longer runs
# this product, and the effect is silent, which is why the defect that prompted this census
# (`/etc/logrotate.d/maran-panel`, installed by step 80 and removed by nothing) survived review:
# the policy carries `missingok`, so logrotate would have gone on reading it and saying nothing for
# years.
#
# What this scope deliberately CANNOT see, because widening it would force a wrong uninstaller:
#
# - /etc/maran, /usr/local/maran, /var/lib/maran*, /run/maran*: directories that are wholly ours.
#   The uninstaller removes them as trees, so a per-file census over them would enumerate files
#   with no removal line of their own and demand lines that must not exist.
# - /var/log/maran, /var/backups/maran, /home/<account>, the PostgreSQL database: the operator's
#   and the customer's data. The uninstaller keeps the backup root on purpose and prompts before
#   the rest, which is the behaviour assert_the_uninstaller_keeps_the_backup_root exists to
#   protect. A census that demanded removal here would be a census arguing for data loss.
# - /etc/ssh/sshd_config and the family's nftables configuration: files the installer EDITS rather
#   than creates. What must go from those is our BLOCK, not the file, and that is a different
#   property with its own checks (remove_sftp_sshd_block, and
#   assert_uninstaller_never_leaves_a_dangling_include for the firewall include).
readonly UNINSTALL_CENSUS_DROP_IN_DIRECTORIES="/etc/logrotate.d /etc/pam.d /etc/tmpfiles.d /etc/systemd/system /etc/nginx/conf.d /etc/sudoers.d"

# The register: drop-in paths the installer NAMES and the uninstaller must NOT remove, each with
# the reason it is kept.
#
# This is the half that keeps the census honest. "Everything the installer installs is removed" is
# a property this product must not have — it already keeps a backup root deliberately — so the
# property asserted is the weaker and truthful one: every path the census finds is EITHER removed
# by its exact literal OR named here with a reason. One line per entry, `path|reason`.
#
# It carries a staleness guard for the same reason `maran structure`'s exemption lists gained one
# after three of them were found naming files that no longer exist: an entry for a path the
# installer no longer names is a hole the next file of that name falls into, and it reads exactly
# like a considered decision. So every entry below must still be a path the census FINDS, and must
# not also be removed — a path that is registered as kept and deleted anyway means one of the two
# statements is a lie and a reader cannot tell which.
readonly UNINSTALL_CENSUS_KEPT_REGISTER="/etc/pam.d/vsftpd|the distribution's own PAM stack for the packaged vsftpd service. installer/lib/89-ftps.sh names it only to say it never edits it — this panel's daemon authenticates against /etc/pam.d/maran-ftps, a service of our own — so removing it would break a stock vsftpd this product never configured and may not have installed.
/etc/systemd/system/multi-user.target.wants|systemd's own enablement directory, which this installer reads and never creates. Step 89 looks in it to prove maran-ftps.service has no entry there; an rm at this path would un-enable every unit on the host."

# expand_installer_literals: one installer file's text with its own literal shell constants
# substituted, so a path written as ${MARAN_UNIT_DIR}/maran-api.service is readable as a path.
#
# Without this the census is blind to exactly the files that matter most. Step 70 writes the two
# systemd units and the tmpfiles snippet as ${MARAN_UNIT_DIR}/maran-api.service,
# ${MARAN_UNIT_DIR}/maran-agent.service and ${MARAN_TMPFILES_DIR}/${MARAN_API_TMPFILES_NAME} —
# not one of the three is a single literal, so a literal-only census would have enumerated ten
# drop-ins instead of thirteen and would have reported a clean tree while being unable to see the
# panel's own units at all. That is the census-reads-nothing failure mode, and it is why the
# expander carries its own planted control below.
#
# Only assignments whose value is a plain literal in the SAME file are resolved: no command
# substitution, no nested variable, no whitespace, no `|` (which would break the sed script this
# builds). A path a step COMPUTES — from the OS family, a loop variable, a `basename` — is
# therefore invisible to the census, and that is stated in its output.
expand_installer_literals() {
  local file="$1" script="" assignment name value
  while IFS= read -r assignment; do
    name="${assignment%%=*}"
    name="${name##* }"
    value="${assignment#*=}"
    value="${value%\"}"
    value="${value#\"}"
    case "$value" in
      ''|*'|'*) continue ;;
    esac
    script="${script}s|\${${name}}|${value}|g;s|\$${name}\\b|${value}|g;"
  done < <( { grep -oE '^[[:space:]]*(readonly[[:space:]]+)?[A-Z][A-Z0-9_]*="[^"$`]*"' "$file" || true; } )
  # An empty sed script is `sed ''`, which is a valid no-op, so a file declaring no constants
  # still comes out as itself rather than killing the run.
  sed "$script" "$file"
}

# installer_drop_in_paths: every path under the six drop-in directories named anywhere in the
# given installer files, one per line, de-duplicated.
#
# Comments included, deliberately, the same choice the FTPS log-path census makes: a comment
# naming a path is what the next person edits from, and a path that appears only in prose is
# either a file we place (and must remove) or somebody else's (and must be registered). Both are
# answers the census wants; neither is noise.
#
# The trailing `sed` strips sentence punctuation, because these paths are named inside English
# prose and `... never edits /etc/pam.d/vsftpd.` would otherwise enter the census one byte longer
# than the path it is about. The cost is the inability to see a drift to a path that genuinely
# ends in punctuation, which no installer here writes.
installer_drop_in_paths() {
  local directory file
  for file in "$@"; do
    local expanded
    expanded="$(expand_installer_literals "$file")"
    for directory in $UNINSTALL_CENSUS_DROP_IN_DIRECTORIES; do
      printf '%s\n' "$expanded" \
        | { grep -oE "${directory}/[A-Za-z0-9_.@-]+" || true; } \
        | sed 's/[.,;:)]\+$//'
    done
  done | sort -u | { grep -E '^/' || true; }
}

# uninstaller_removal_targets: every absolute path the uninstaller hands to `rm`, one per line.
#
# Line continuations are joined first — `rm -f a \` + newline + `b` is one command and `b` is one
# of its targets — and only lines whose FIRST word is `rm` are read. That second rule is not
# cosmetic: installer/uninstall.sh prints an operator runbook containing the words
# `rm -f /etc/systemd/system/<that unit>`, and a census that read an echo as a deletion would
# accept a path the uninstaller merely talks about.
uninstaller_removal_targets() {
  sed -e ':join' -e '/\\$/{N;s/\\\n//;bjoin' -e '}' "$UNINSTALLER" \
    | { grep -oE '^[[:space:]]*rm[[:space:]]+-[rf]+[[:space:]]+[^#]*' || true; } \
    | tr ' \t' '\n\n' \
    | { grep -E '^"?/' || true; } \
    | tr -d '"' \
    | sort -u
}

# assert_the_uninstaller_removes_every_drop_in_the_installer_creates: the census this repository
# owed, and the check that would have caught today's defect the day it was written.
#
# The defect: a fix added a THIRD rotation policy, installed by step 80. installer/uninstall.sh
# removed the other two by exact literal name and not the new one — three installed, two removed —
# and it was found by a person reading the file. Nothing in this repository compared the two lists;
# the only uninstall assertions covered a dangling firewall include and the backup root.
#
# Why a CENSUS and not "the three policies are removed". A check naming the three files agrees with
# the tree the day it is written and is silent on the fourth, which is the same defect one release
# later. So both sides are DERIVED: the installed side from the step list installer/install.sh
# itself runs, the removed side from the uninstaller's own `rm` lines. A drop-in added tomorrow by
# a step nobody here has read is caught BY NAME.
#
# What is asserted is not "everything installed is removed" — that property would force a wrong
# uninstaller, since this one keeps a backup root on purpose and prompts before customer data.
# It is: **every drop-in the installer names is either removed by its exact literal, or named in
# UNINSTALL_CENSUS_KEPT_REGISTER with a reason** — and the register cannot go stale, because every
# entry in it must still be a path the census finds.
#
# It enforces the CONVENTION and not merely the outcome. The path must appear as a whole
# `rm` ARGUMENT: `rm -f /etc/logrotate.d/maran-*` would not satisfy it, and that is deliberate. A
# glob in this uninstaller is dangerous — a peer proved the literal is not a prefix match by
# planting `/etc/logrotate.d/maran-panel-backup` and watching it survive, and a glob would have
# eaten an operator's own copy. A lane that wants a glob has to change this check and argue for it.
assert_the_uninstaller_removes_every_drop_in_the_installer_creates() {
  local censused=0 planted steps step_count step_file installer_files
  local installed removed path entry registered_path reason

  # 1. The step list, from the installer's own table of contents rather than from a list here. A
  # list written here would be the same "two named copies" arrangement this census replaces.
  steps="$( { grep -oE '^[[:space:]]*run_step[[:space:]]+[0-9]+-[A-Za-z0-9-]+\.sh' "$INSTALLER_ENTRY_POINT" || true; } | awk '{ print $2 }' | sort -u)"
  step_count="$(printf '%s' "$steps" | grep -c . || true)"
  # The vacuity guard on the axis that can go blind. `run_step` renamed, or the loop replaced by
  # something else, empties this list and every comparison below becomes a comparison over nothing.
  [ "$step_count" -ge 10 ] \
    || fail "only ${step_count} step files could be read out of ${INSTALLER_ENTRY_POINT}'s run_step lines, and this installer has more than ten steps. The census below would have enumerated the drop-ins of a handful of files and reported on all of them (rules/testing.md)"
  censused=$((censused + 1))

  # Its positive control: the extractor must see a step it has never seen, in a copy. Without
  # this, a `run_step` spelling that no longer matches returns a SHORTER list and the guard above
  # is the only thing between that and a census that quietly stopped reading a step.
  planted="/tmp/polygon-uninstall-census-steps.sh"
  { cat "$INSTALLER_ENTRY_POINT"; echo '  run_step 99-polygon-planted.sh step_polygon_planted'; } > "$planted"
  printf '%s\n' "$( { grep -oE '^[[:space:]]*run_step[[:space:]]+[0-9]+-[A-Za-z0-9-]+\.sh' "$planted" || true; } | awk '{ print $2 }')" \
    | grep -Fxq -- '99-polygon-planted.sh' \
    || fail "the step-list extractor read ${step_count} steps out of install.sh but does not read '99-polygon-planted.sh' out of a copy carrying exactly that run_step line — it is not reading the file's content, and the census's coverage below means nothing."
  rm -f -- "$planted"
  censused=$((censused + 1))

  # 2. Every one of those steps must be IN this image. A step the Dockerfile does not carry is a
  # step whose drop-ins this census cannot see, and a census with an invisible member is the
  # failure this whole block is written against — so it is a named build failure, never a skip.
  installer_files="$INSTALLER_ENTRY_POINT"
  for step_file in $steps; do
    require_installer_file "${INSTALLER_LIB}/${step_file}" "installer/lib/${step_file}"
    installer_files="${installer_files} ${INSTALLER_LIB}/${step_file}"
  done
  censused=$((censused + 1))

  # 3. The two controls on the extractor itself, before anything is compared. The first is the
  # ordinary planted one; the second is on the EXPANDER, and it is the one that matters here,
  # because without variable expansion this census silently loses the panel's own two systemd
  # units and its tmpfiles snippet while still printing a clean verdict.
  planted="/tmp/polygon-uninstall-census-step.sh"
  { cat "$FTPS_STEP"; echo '# polygon control: /etc/logrotate.d/maran-polygon-planted'; } > "$planted"
  installer_drop_in_paths "$planted" | grep -Fxq -- '/etc/logrotate.d/maran-polygon-planted' \
    || fail "the drop-in extractor does not read '/etc/logrotate.d/maran-polygon-planted' out of a copy of 89-ftps.sh carrying exactly that line — it is not reading the installer's content, and every agreement reported below is an agreement with nothing (rules/testing.md)."
  if installer_drop_in_paths "$FTPS_STEP" | grep -Fxq -- '/etc/logrotate.d/maran-polygon-planted'; then
    fail "the drop-in extractor reports '/etc/logrotate.d/maran-polygon-planted' for the UNMODIFIED 89-ftps.sh, which names it nowhere — it is answering from something other than the file, so its planted control above proves nothing."
  fi
  rm -f -- "$planted"
  censused=$((censused + 1))

  planted="/tmp/polygon-uninstall-census-expand.sh"
  {
    echo 'readonly ZZ_POLYGON_PLANTED_DIR="/etc/logrotate.d"'
    echo 'install -D -m 0644 x "${ZZ_POLYGON_PLANTED_DIR}/maran-polygon-expanded"'
  } > "$planted"
  installer_drop_in_paths "$planted" | grep -Fxq -- '/etc/logrotate.d/maran-polygon-expanded' \
    || fail "the drop-in extractor cannot resolve a destination written as \${VARIABLE}/name, so it is blind to every file installer/lib/70-services.sh places — both systemd units and the tmpfiles snippet are written that way. A census that cannot see the panel's own units would report this tree clean whatever the uninstaller did (rules/testing.md)."
  rm -f -- "$planted"
  censused=$((censused + 1))

  # 4. The two lists.
  installed="$(installer_drop_in_paths $installer_files)"
  removed="$(uninstaller_removal_targets)"

  # The two vacuity guards, one per list. Either list going empty makes every claim below trivially
  # true: an empty installed list has nothing to demand, and an empty removed list would fail
  # everything, which is the direction that at least announces itself — both are failures to
  # observe and neither is a verdict.
  local installed_count removed_count
  installed_count="$(printf '%s' "$installed" | grep -c . || true)"
  removed_count="$(printf '%s' "$removed" | grep -c . || true)"
  [ "$installed_count" -gt 0 ] \
    || fail "the census found NO path under ${UNINSTALL_CENSUS_DROP_IN_DIRECTORIES} in any of the ${step_count} installer steps, and this installer places a rotation policy, two systemd units, a tmpfiles snippet, a PAM stack and two nginx files. The installed list is empty, so 'every installed drop-in is removed' held over nothing (rules/testing.md)"
  [ "$removed_count" -gt 0 ] \
    || fail "no absolute path could be read out of any 'rm' line in ${UNINSTALLER}, so the removed list is empty and every path below would be reported as an omission. The extractor has stopped reading the uninstaller, which is a failure to observe and not a finding about the uninstaller (rules/testing.md)"
  censused=$((censused + 2))

  # 5. The register's staleness guard, and its contradiction guard.
  local register_count=0
  while IFS= read -r entry; do
    [ -n "$entry" ] || continue
    register_path="${entry%%|*}"
    reason="${entry#*|}"
    register_count=$((register_count + 1))
    [ -n "$reason" ] && [ "$reason" != "$register_path" ] \
      || fail "UNINSTALL_CENSUS_KEPT_REGISTER names ${register_path} with no reason. An exemption without a reason is one nobody can review or retire (rules/security.md)"
    printf '%s\n' "$installed" | grep -Fxq -- "$register_path" \
      || fail "UNINSTALL_CENSUS_KEPT_REGISTER exempts ${register_path} from removal and no installer step names that path any more — an exemption for a file the installer does not place is a hole the next file of that name falls into, and it reads exactly like a decision somebody made on purpose (rules/security.md)"
    if printf '%s\n' "$removed" | grep -Fxq -- "$register_path"; then
      fail "UNINSTALL_CENSUS_KEPT_REGISTER says ${register_path} is kept and installer/uninstall.sh removes it by that exact literal. One of the two is wrong and a reader cannot tell which: either the register entry is stale, or the uninstaller is deleting a file this repository has written down a reason to keep."
    fi
    censused=$((censused + 1))
  done < <(printf '%s\n' "$UNINSTALL_CENSUS_KEPT_REGISTER")
  # The register's own vacuity guard. An empty register would make the staleness loop above examine
  # nothing while reading like a clean list — the exact shape `maran structure`'s exemption guard
  # was given a control for.
  [ "$register_count" -ge 2 ] \
    || fail "UNINSTALL_CENSUS_KEPT_REGISTER read as ${register_count} entries, and it is written with two. The staleness guard examined no exemption at all, which is a failure to observe and not a clean register (rules/testing.md)"
  censused=$((censused + 1))

  # 6. The census proper. Removed by its exact literal, or registered as kept with a reason.
  #
  # `grep -Fxq` and not a substring test, and that is the trap a sibling check in this file fell
  # into: '/etc/logrotate.d/maran-panel' is a substring of '/etc/logrotate.d/maran-panel-backup',
  # so a substring match would accept an uninstaller that removes the wrong file and would also
  # accept a deliberately corrupted path. Whole line, whole word, nothing else.
  local omissions=0
  while IFS= read -r path; do
    [ -n "$path" ] || continue
    if printf '%s\n' "$removed" | grep -Fxq -- "$path"; then
      continue
    fi
    if printf '%s\n' "$UNINSTALL_CENSUS_KEPT_REGISTER" | grep -q "^${path}|"; then
      continue
    fi
    omissions=$((omissions + 1))
    echo "assert-installer-steps.sh: the installer names ${path} and installer/uninstall.sh neither removes it by that exact literal nor names it in UNINSTALL_CENSUS_KEPT_REGISTER with a reason for keeping it." >&2
  done < <(printf '%s\n' "$installed")
  [ "$omissions" -eq 0 ] \
    || fail "${omissions} drop-in file(s) the installer places are left on the host by installer/uninstall.sh, named above. Every one of these directories is read by a daemon on a schedule or at boot, so a leftover goes on having an effect on a host that no longer runs this product — silently, because a logrotate policy carries missingok and a tmpfiles snippet just recreates its directory. Either add 'rm -f <path>' to the uninstaller, or add the path to UNINSTALL_CENSUS_KEPT_REGISTER with the reason it must stay."
  censused=$((censused + 1))

  echo "The uninstall census: ${installed_count} drop-in path(s) named across install.sh and its ${step_count} steps, under ${UNINSTALL_CENSUS_DROP_IN_DIRECTORIES}, each of them either removed by installer/uninstall.sh as a whole rm ARGUMENT — ${removed_count} such paths read out of its own rm lines — or named in UNINSTALL_CENSUS_KEPT_REGISTER with a reason, with every register entry proved to still be a path the installer places. ${censused} checks made."
  printf '%s\n' "$installed" | sed 's/^/  installed: /'
  echo "UNOBSERVED HERE: this reads the installer's TEXT, not a host. A destination a step COMPUTES rather than declares — from the OS family, a loop, a basename — is invisible to it, and so is any drop-in outside the six directories above: /etc/maran, /usr/local/maran, /var/lib/maran*, /var/log/maran and /var/backups/maran are removed as trees or kept on purpose, and /etc/ssh/sshd_config and the family's nftables file are EDITED rather than created, so what must go from those is our block and not the file."
  echo "UNOBSERVED HERE: it asserts a removal LITERAL exists, not that the removal RUNS. An rm inside a branch that never fires, after an early exit, or in a function main() stopped calling passes this check unchanged — assert_the_uninstaller_keeps_the_backup_root is the one assertion here that executes the uninstaller's deleters, and it does so for a single path. Nor does it judge ORDER: a policy removed after the prompt that may have deleted the directory it rotates satisfies this."
}

# ---------------------------------------------------------------------------------------------
# Step 89 — FTPS: a daemon that is installed and does not listen.
#
# Everything below RUNS installer/lib/89-ftps.sh's own functions and then looks at what they
# left behind. Nothing here repeats the step's work: the package comes from the step's own
# `vsftpd_packages_for_family`, the group from `ensure_ftps_group`, the jail base from
# `ensure_ftps_directories`, the PAM stack from `install_ftps_pam_service`, and the unit and the
# rotation policy from `install_ftps_unit` and `install_ftps_logrotate`. An image that performed
# any of it itself and then asserted it would be asserting about the image.
#
# WHAT IS DELIBERATELY NOT ASSERTED HERE, and it is the most important sentence in this block:
# the package's own ENABLEMENT bookkeeping. "The package did not enable the packaged unit" looks
# like the obvious thing to check and it is the wrong control, because it is a statement about
# `deb-systemd-helper` on one family rather than about whether the daemon can start.
#
# The measurements, all taken 2026-09-09 in throwaway containers with `docker run --rm`, because
# this repository has had two contradictory readings of it written down:
#
#   ubuntu:24.04 and debian:trixie, /etc/systemd/system/vsftpd.service already a link to
#   /dev/null when `apt-get install vsftpd` runs — NO .wants entry is created, and
#   /var/lib/systemd/deb-systemd-helper-enabled holds no vsftpd.service.dsh-also record. The
#   helper checks for exactly that link and skips the enable.
#
#   The same two images with no mask — /etc/systemd/system/multi-user.target.wants/vsftpd.service
#   IS created, with the dsh-also record beside it.
#
# So on the Debian family the mask happens to suppress the enablement too. That is a side effect
# of one helper's behaviour, it differs from what the comment block in installer/lib/89-ftps.sh
# records, and NOTHING here is allowed to depend on it:
# a check asserting the absence of that symlink would be asserting on a package script, and would
# go red the day a distribution changes its helper without the daemon becoming any more startable.
#
# The control is the MASK — /etc/systemd/system/vsftpd.service as a link to /dev/null, shadowing
# the unit file the package ships, which is what makes the unit unstartable — plus the positive
# control that there IS a shipped unit under the shadow. The .wants state is PRINTED, on both
# branches, so the next reader sees what this host actually did instead of re-deriving it from a
# check that was never about it.

# ftps_unit_shipped_by_the_distribution: the path of the vsftpd unit the PACKAGE ships, or the
# empty string. Both families' locations are tried because the answer is a distribution fact:
# /lib/systemd/system on Debian, /usr/lib/systemd/system on RHEL, and the two are the same
# directory on a merged-usr Debian.
ftps_unit_shipped_by_the_distribution() {
  local candidate
  for candidate in /lib/systemd/system/vsftpd.service /usr/lib/systemd/system/vsftpd.service; do
    if [ -f "$candidate" ]; then
      printf '%s' "$candidate"
      return 0
    fi
  done
  printf ''
}

# ftps_wants_entries_for: every .wants entry naming a unit, one per line, or nothing.
# Used by the mask assertion to REPORT the Debian family's bookkeeping and by the
# switched-off assertion to REFUSE one naming maran-ftps.service, which are two different
# questions about the same kind of symlink.
ftps_wants_entries_for() {
  find /etc/systemd/system -name "$1" -path '*.wants/*' 2>/dev/null || true
}

# ftps_pam_directives: a PAM file with its comments and its blank lines removed.
#
# It exists because the file's own comments argue about the modules it does NOT use, so a search
# over the raw text finds the explanation and calls it a defect. Stripping the comments is what
# makes the aggregate-stack probe a question about the stack rather than about the prose.
ftps_pam_directives() {
  sed -e 's/#.*$//' -e '/^[[:space:]]*$/d' "$1"
}

# assert_file_is: one regular file against `uid:gid:mode`, the three questions asked separately
# so the diagnosis names which one failed, and a symbolic link at the name refused outright.
#
# The companion of assert_directory_is above, and it exists for the same reason: `install -D`
# exits 0 onto a symlink and applies the ownership and the mode to the link's TARGET. Two of the
# three files it is used on decide who may authenticate (the PAM stack) and what root executes
# (the unit), so a link at either name is a file somebody else chose.
assert_file_is() {
  local path="$1" expected="$2" found

  [ -e "$path" ] || [ -L "$path" ] \
    || fail "${path} was not created by the installer step that is supposed to create it"
  if [ -L "$path" ]; then
    fail "${path} is a SYMBOLIC LINK, not a regular file. Its owner and its mode are the link's, never the target's, and root reading or executing through it reads wherever it points."
  fi
  [ -f "$path" ] || fail "${path} exists but is not a regular file"

  found="$(stat -c '%u:%g:%a' "$path")"
  [ "$found" = "$expected" ] \
    || fail "${path} is uid:gid:mode ${found}, not ${expected} ($(stat -c '%U:%G' "$path") by name)"
}

# assert_the_packaged_daemon_was_never_startable: the mask, and then the package, in that order.
#
# This is the one assertion in the block whose subject is a WINDOW rather than a state, so it
# runs the two functions itself instead of reading a host somebody else prepared: the package is
# required to be absent when it starts, the mask is made, the package is installed through the
# step's own list, and the mask is asked about again afterwards. Between those two calls is the
# whole security property of step 89 — on the Debian family `apt-get install vsftpd` starts the
# distribution's daemon from its postinst, which is port 21 accepting local system logins with
# the password in the clear, on a machine that is by definition reachable from the internet.
#
# Fails if `mask_packaged_vsftpd` stops running or stops producing the link, if the package name
# in `vsftpd_packages_for_family` stops being right on this family, or if anything in the install
# removes the mask again.
assert_the_packaged_daemon_was_never_startable() {
  if [ -e /usr/sbin/vsftpd ]; then
    fail "/usr/sbin/vsftpd is already on this image before the FTPS assertions ran. This assertion is about the WINDOW between masking the packaged unit and installing the package, and a package installed by something else has closed that window before it could be observed. Remove the install from the Dockerfile: the image gets vsftpd from install_vsftpd_package and from nothing else."
  fi

  mask_packaged_vsftpd

  local mask_path="/etc/systemd/system/vsftpd.service"
  [ -L "$mask_path" ] \
    || fail "${mask_path} is not a symbolic link after mask_packaged_vsftpd ran. A mask IS that link; nothing else is."
  [ "$(readlink -- "$mask_path")" = "/dev/null" ] \
    || fail "${mask_path} points at $(readlink -- "$mask_path"), not at /dev/null — the packaged unit is not masked and its postinst could start it"

  install_vsftpd_package

  [ -x /usr/sbin/vsftpd ] \
    || fail "install_vsftpd_package ran, but /usr/sbin/vsftpd is not on this host. Either vsftpd_packages_for_family names a package this family does not have, or the package stopped putting the daemon at the path installer/systemd/maran-ftps.service execs."

  # The mask must have SURVIVED the package. dpkg and rpm both write into
  # /etc/systemd/system, and a postinst that replaced the link would have reopened exactly the
  # window this ordering exists to close.
  [ -L "$mask_path" ] && [ "$(readlink -- "$mask_path")" = "/dev/null" ] \
    || fail "${mask_path} is no longer a link to /dev/null after the package was installed — the package's own scripts took the mask away"

  # The positive control on the axis that could silently go blind: a mask shadows a unit FILE,
  # so if the distribution shipped none, this whole assertion would be green while masking
  # nothing. It is the shadowing that makes the unit unstartable, and there has to be something
  # under the shadow for the sentence to mean anything.
  local packaged_unit
  packaged_unit="$(ftps_unit_shipped_by_the_distribution)"
  [ -n "$packaged_unit" ] \
    || fail "the vsftpd package on this family ships no unit file under /lib/systemd/system or /usr/lib/systemd/system, so ${mask_path} shadows nothing and this assertion has been measuring an empty statement. Find out what the package ships now and mask that."
  echo "Masked: ${mask_path} -> /dev/null, shadowing ${packaged_unit}."

  # REPORTED, never asserted — see the block comment. On the Debian family this entry EXISTS,
  # mask or no mask, and a check written the other way round would fail this build for a reason
  # that has nothing to do with whether the daemon can start.
  local packaged_wants
  packaged_wants="$(ftps_wants_entries_for vsftpd.service)"
  if [ -n "$packaged_wants" ]; then
    echo "NOT A DEFECT, and not asserted on: the package's own enablement bookkeeping left ${packaged_wants}."
    echo "  It names a unit whose file is shadowed by /dev/null, which systemd ignores. The mask above is the control; this symlink is neither the control nor a defect."
  else
    echo "NOT THE CONTROL either way: this family's package left no .wants entry for vsftpd.service. On the Debian family that is deb-systemd-helper declining to enable a unit it finds masked (measured 2026-09-09), which is a fact about a package script and not about whether the daemon can start."
  fi

  if command -v ss >/dev/null 2>&1; then
    echo "Listening sockets on port 21 after the package landed:"
    ss -lnt 2>/dev/null | awk 'NR == 1 || $4 ~ /:21$/' || true
  else
    echo "UNOBSERVED HERE: iproute2 is not in this image, so not even the empty port-21 listing below could be taken."
  fi
  echo "UNOBSERVED HERE: whether the postinst would have started the daemon. No systemd boots during an image build, and Docker's own policy-rc.d denies the start outright, so the port-21 listing above is empty whether the mask exists or not — it is decoration and is printed as such. What is observed here is the artefact: the link, and the unit it shadows. The live fact belongs on a real host."
}

# assert_the_vsftpd_package_is_refused_while_its_unit_is_startable: the inverse control for the
# ordering above.
#
# The positive assertion passes on a correct host and would pass just as happily if
# `install_vsftpd_package`'s own guard had been deleted — the mask was already there. So the
# guard is handed the one host it exists to refuse, a host whose packaged unit is startable, and
# must abort by name; and then the real host again, which it must ACCEPT, because a gate mutated
# to refuse everything passes every test that only ever gives it broken input (rules/testing.md).
#
# The second half is the other refusal in the same function, and it is a refusal to DESTROY:
# a real file at the mask path is an operator's own unit or override, and `mask_packaged_vsftpd`
# must refuse the install rather than delete it to make room for a link.
assert_the_vsftpd_package_is_refused_while_its_unit_is_startable() {
  local mask_path="/etc/systemd/system/vsftpd.service" status output

  rm -f -- "$mask_path"
  status=0
  output="$( ( install_vsftpd_package ) 2>&1 )" || status=$?
  [ "$status" -ne 0 ] \
    || fail "install_vsftpd_package accepted a host whose packaged vsftpd.service is not masked. On the Debian family that is the postinst starting port 21 with local logins and no TLS."
  case "$output" in
    *"is NOT masked"*) ;;
    *) fail "install_vsftpd_package refused, but not for the reason it should have:
${output}" ;;
  esac

  # The refusal to destroy. A real file, not a link, at the same path.
  install -d -o root -g root -m 0755 /etc/systemd/system
  printf '# an operator unit, not ours\n' > "$mask_path"
  status=0
  output="$( ( mask_packaged_vsftpd ) 2>&1 )" || status=$?
  [ "$status" -ne 0 ] \
    || fail "mask_packaged_vsftpd accepted a REAL ${mask_path}. Whatever it did to that file, the file was somebody else's unit."
  case "$output" in
    *"is a real file, not a mask"*) ;;
    *) fail "mask_packaged_vsftpd refused a real unit file, but not for the reason it should have:
${output}" ;;
  esac
  [ -f "$mask_path" ] && [ ! -L "$mask_path" ] \
    || fail "mask_packaged_vsftpd DELETED an operator's own ${mask_path} instead of refusing. The refusal message is worth nothing if the file is gone by the time it is printed."
  grep -q 'an operator unit, not ours' "$mask_path" \
    || fail "the operator's own ${mask_path} was rewritten by mask_packaged_vsftpd"
  rm -f -- "$mask_path"

  # And the acceptance, so this function cannot pass against a guard that refuses everything.
  mask_packaged_vsftpd
  install_vsftpd_package
  [ -x /usr/sbin/vsftpd ] \
    || fail "after the mask was restored, install_vsftpd_package left no /usr/sbin/vsftpd — the gate now refuses a host it must accept"
  echo "install_vsftpd_package refuses an unmasked host by name, mask_packaged_vsftpd refuses an operator's own unit without deleting it, and both accept the real host."
}

# assert_ftps_group_is_created: the group exists and is a SYSTEM group.
#
# Membership of it is the entire authorization to authenticate through /etc/pam.d/maran-ftps,
# so the gid range is not cosmetic: a group in the login range is one `useradd` can hand out as
# a new account's primary group by accident. Fails if ensure_ftps_group stops running or loses
# its --system flag.
assert_ftps_group_is_created() {
  ensure_ftps_group
  getent group maran-ftps >/dev/null \
    || fail "89-ftps.sh did not create the maran-ftps group, which is the entire authorization to use FTPS"
  local gid
  gid="$(getent group maran-ftps | cut -d: -f3)"
  [ "$gid" -lt 1000 ] \
    || fail "maran-ftps is not a system group (gid ${gid}); groupadd --system is what keeps it out of the range a new login can be given by accident"

  # Idempotence, which an installer needs and which this function claims: a second call must
  # converge rather than fail on its own previous work, and must not make a second group.
  ensure_ftps_group
  local group_count
  group_count="$(getent group | awk -F: '$1 == "maran-ftps"' | wc -l)"
  [ "$group_count" -eq 1 ] \
    || fail "after two calls to ensure_ftps_group there are ${group_count} maran-ftps groups"
  echo "The maran-ftps group exists, is a system group (gid ${gid}), and a second call to ensure_ftps_group changes nothing."
}

# assert_the_ftps_artifacts_are_what_they_claim: the five things step 89 leaves on disk, each
# as one uid:gid:mode triple, with a symbolic link at any of the names refused outright.
#
# The jail base's row reads 711 and not 700, and the change of that one digit is worth a
# sentence, because this row PASSED throughout the whole life of the defect it was meant to
# cover. The base was created 0700, this row required 0700, the two agreed, and every FTPS
# login on every real install failed with `500 OOPS: cannot change directory` — vsftpd chdirs
# into the login's home AFTER dropping to the account's uid, and 0700 is exactly the mode that
# stops it. A row that reads the intended value back cannot see an intended value that is
# wrong. It is kept because it observes the OTHER half — that nothing widened the base to
# 0755 — and the half it cannot see is observed live, as an unprivileged uid, by
# assert_the_jail_base_is_traversable_by_an_unprivileged_uid below.
#
# One list and one loop, exactly like step 40's directory layout, and carrying the same vacuity
# guard for the same reason: a `for` over an empty list exits 0, so a path dropped from the list
# by a bad merge removes a check with no output changing anywhere. The count is the only thing
# that observes the list's SIZE. Raise it with the list.
assert_the_ftps_artifacts_are_what_they_claim() {
  local SCRIPT_DIR="$INSTALLER_ROOT"

  ensure_ftps_directories
  install_ftps_pam_service
  install_ftps_unit
  install_ftps_logrotate

  local entry checked=0
  for entry in \
    "d:/var/lib/maran-ftps:0:0:711" \
    "d:/etc/maran/vsftpd:0:0:755" \
    "f:/etc/pam.d/maran-ftps:0:0:644" \
    "f:/etc/systemd/system/maran-ftps.service:0:0:644" \
    "f:/etc/logrotate.d/maran-ftps:0:0:644"; do
    local kind="${entry%%:*}" rest="${entry#*:}"
    case "$kind" in
      d) assert_directory_is "${rest%%:*}" "${rest#*:}" ;;
      f) assert_file_is "${rest%%:*}" "${rest#*:}" ;;
    esac
    checked=$((checked + 1))
  done
  [ "$checked" -eq 5 ] \
    || fail "the FTPS artifact list checked ${checked} paths, not the 5 step 89 creates. A path has been dropped from the list, and a check that is not in the list is not a check that failed — it is one that no longer exists."

  echo "Step 89's five artifacts verified: two directories and three files, each with its own owner and mode, none of them a link."
}

# assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root: where the jail base IS, which
# is a different question from its own mode and is the question that has already cost this
# repository a working feature.
#
# Step 40 creates /var/lib/maran owned by the service account, 0750, and the owner of a directory can rename a
# level aside and leave an entry of its own at the name every customer's jail hangs under — so a
# root-only directory underneath a panel-owned one is not root-only however it is moded. While
# the SFTP base was /var/lib/maran/sftp, sshd refused every login on every real install with
# `bad ownership or modes for chroot directory component "/var/lib/maran/"`. vsftpd's rule is not
# the same rule and is not weaker: it refuses to run at all with a chroot root the logged-in user
# can write to. A mode check on the base cannot see this defect; the path and the ancestor walk
# can.
assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root() {
  ensure_ftps_directories

  # The step's OWN constant, not the literal, and this is the correction that makes the
  # assertion able to fail. Written against the literal /var/lib/maran-ftps it was BLIND to the
  # one mutation it exists to catch: moving MARAN_FTPS_JAIL_ROOT to /var/lib/maran/ftps left the
  # literal path uncreated, `cd` into it failed inside a command substitution — which `set -e`
  # does not see — the `case` matched an empty string, and the ancestor walk walked the parents
  # of a path that was not there and found them all root's. Measured as a SURVIVED mutant before
  # this line existed. The literal is asserted separately, in the artifact list above, where a
  # moved path fails as "was not created"; here the question is about the path the step actually
  # uses, so the step is what names it.
  local base="$MARAN_FTPS_JAIL_ROOT"
  [ -d "$base" ] \
    || fail "${base}, the jail base 89-ftps.sh names in MARAN_FTPS_JAIL_ROOT, is not a directory after ensure_ftps_directories ran"
  local resolved
  resolved="$(cd "$base" && pwd -P)" \
    || fail "${base} could not be resolved; every check below it would have been asked about nothing"
  case "$resolved" in
    /var/lib/maran/*) fail "the FTPS jail base resolves to ${resolved}, underneath /var/lib/maran, whose owner is the panel uid — an unprivileged uid can then replace a component of every account's chroot path, and vsftpd refuses a writable chroot root outright" ;;
  esac
  assert_ancestors_are_root_only "$resolved"
  echo "The FTPS jail base the step names (${base}) resolves to ${resolved}, a sibling of the panel state root and not a child of it, and every ancestor of it belongs to root and is writable by nobody else."
}

# assert_the_jail_base_is_traversable_by_an_unprivileged_uid: the property a mode row cannot
# see, asked the way the daemon asks it.
#
# It drives the step's OWN gate, `assert_jail_base_is_traversable_and_not_listable`, rather
# than re-implementing the question here — a polygon assertion that re-derives the answer is
# scoring its own copy of the logic and not the shipped one. What this function adds is the
# pair of controls that gate owes, because a refusing gate that has only ever been handed
# correct input is indistinguishable from one mutated to accept everything:
#
#   POSITIVE  at 0711 the gate must ACCEPT (the inverse control: it accepts something).
#   INVERSE A at 0700 it must REFUSE, naming the traversal — the shape of the live defect.
#   INVERSE B at 0755 it must REFUSE, naming the listing — the over-correction, which would
#             publish one directory name per hosting account to every local uid.
#
# Each call runs in a subshell because the gate exits on refusal, which is what it should do
# inside the installer and is not what a harness wants. UNOBSERVED HERE: an actual FTPS login.
# No daemon boots in an image build. What IS observed is the same syscall the login's chdir
# makes, performed by the same class of uid, which is the axis the defect lived on.
assert_the_jail_base_is_traversable_by_an_unprivileged_uid() {
  ensure_ftps_directories

  local mode outcome checked=0
  for mode in 0711 0700 0755; do
    chmod "$mode" /var/lib/maran-ftps
    outcome="$( (assert_jail_base_is_traversable_and_not_listable) 2>&1 )" && outcome="ACCEPTED
${outcome}" || outcome="REFUSED
${outcome}"
    case "$mode" in
      0711)
        case "$outcome" in
          ACCEPTED*) : ;;
          *) fail "the step's jail-base gate REFUSED the correct mode 0711. A gate that refuses everything passes every test that only hands it broken input, and this is the control that says it is not one. It said: $(echo "$outcome" | tail -n +2)" ;;
        esac
        ;;
      0700)
        case "$outcome" in
          REFUSED*"cannot traverse"*) : ;;
          *) fail "the step's jail-base gate did not refuse mode 0700 by naming the traversal. 0700 is the mode under which EVERY FTPS login fails with '500 OOPS: cannot change directory', and it is the mode this gate exists for. It said: ${outcome}" ;;
        esac
        ;;
      0755)
        case "$outcome" in
          REFUSED*"can LIST"*) : ;;
          *) fail "the step's jail-base gate did not refuse mode 0755 by naming the listing. The entries under this base are hosting account names; at 0755 every local uid can read them. It said: ${outcome}" ;;
        esac
        ;;
    esac
    checked=$((checked + 1))
  done
  [ "$checked" -eq 3 ] \
    || fail "the jail-base traversal controls ran ${checked} of the 3 modes. A control that is not in the loop is not a control that failed — it is one that no longer exists."

  # And the axis the three controls above CANNOT see, which is the axis the live defect lived
  # on: the mode the step ITSELF chooses. The controls hand the gate modes this function sets,
  # so they say the gate works and say nothing about what `ensure_ftps_directories` produces.
  # Measured as a SURVIVOR before this line existed: a mutant that made the step create 0700
  # again AND moved every mode literal in this file to agree with it — which is precisely the
  # shape of the shipped defect, an intended value that was wrong with every assertion
  # agreeing — left all three assertions green. This line asks the gate about the step's own
  # output and states no mode at all, so there is no literal for such a mutant to move.
  ensure_ftps_directories
  assert_jail_base_is_traversable_and_not_listable
  echo "The jail base is traversable and not listable by an unprivileged uid, asked as that uid: the step's own gate accepts 0711, refuses 0700 by naming the traversal, refuses 0755 by naming the listing, and accepts what ensure_ftps_directories actually creates."
  echo "UNOBSERVED HERE: an actual FTPS login — no daemon boots in an image build. The syscall the login's chdir makes is what was performed, by the same class of uid."
}

# assert_ftps_pam_stack_requires_group_membership: the CONTENT of the file that decides who may
# authenticate. What that content does is a separate assertion, and the one below this one.
#
# The name of this function used to be the whole defect. It says "requires", and it observes
# presence: every check in it is a search for a line, and a PAM stack is its control flow, not its
# lines. Prepending `auth sufficient pam_permit.so` to the shipped stack leaves both `required
# pam_succeed_if` lines byte-identical, passes every check here, and turns the daemon into one that
# accepts every account on the host with no password at all. So this function keeps the two
# properties a content check really can hold — the group test is present in both phases, and the
# host's aggregate stack is absent — and it no longer claims the property it cannot see;
# assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for asks the library instead. The second is not tidiness — password-auth and common-auth pull in whatever
# authentication policy an operator has configured (pam_sss, a directory service), and including
# them would make this daemon's answer depend on configuration nobody wrote for it.
assert_ftps_pam_stack_requires_group_membership() {
  local SCRIPT_DIR="$INSTALLER_ROOT"
  install_ftps_pam_service

  local phase found=0
  for phase in auth account; do
    grep -Eq "^${phase}[[:space:]]+required[[:space:]]+pam_succeed_if\.so user ingroup maran-ftps" \
      /etc/pam.d/maran-ftps \
      || fail "/etc/pam.d/maran-ftps has no ${phase}-phase pam_succeed_if test for maran-ftps membership. Without the auth line every local shadow entry can authenticate to FTPS; without the account line the gate is passed once and never re-asked, and the account phase is the one that answers whether this user may use this service."
    found=$((found + 1))
  done
  [ "$found" -eq 2 ] \
    || fail "the PAM phase list checked ${found} phases, not the 2 this stack must carry the group test in"

  # The DIRECTIVES, never the whole file: the shipped stack argues in a comment why it uses
  # pam_unix.so "and not password-auth/common-auth", so a grep over the raw text matches the
  # explanation and refuses a correct file. Measured, as a red run of this very assertion.
  if ftps_pam_directives /etc/pam.d/maran-ftps | grep -Eq 'password-auth|common-auth|system-auth'; then
    fail "/etc/pam.d/maran-ftps pulls in the host's aggregate stack ($(ftps_pam_directives /etc/pam.d/maran-ftps | grep -E 'password-auth|common-auth|system-auth')). An FTPS login is a local shadow entry and nothing else; including the aggregate makes this daemon's answer depend on an operator's directory service, pam_faillock and whatever else is in there."
  fi

  # The positive control on the grep itself. Both patterns above are searches, and a search that
  # cannot match — a renamed file, a `grep` reading the wrong path — reports the same green as
  # one that matched. So the probe is pointed at a planted copy carrying exactly what it hunts
  # for and must FIND it, and then at a copy with the line removed and must NOT.
  local planted="/tmp/polygon-pam-probe"
  render_ftps_pam_service > "$planted"
  grep -Eq '^auth[[:space:]]+required[[:space:]]+pam_succeed_if\.so user ingroup maran-ftps' "$planted" \
    || fail "the probe cannot find the group test in a freshly rendered stack — it has been searching for something render_ftps_pam_service does not write, and its green above meant nothing"
  grep -v 'pam_succeed_if' "$planted" > "${planted}.stripped"
  if grep -Eq '^auth[[:space:]]+required[[:space:]]+pam_succeed_if\.so user ingroup maran-ftps' "${planted}.stripped"; then
    fail "the probe matched a stack with every pam_succeed_if line removed — it cannot tell the presence of the group test from its absence"
  fi
  # And the same for the aggregate-stack probe, which is a REFUSING check and therefore the one
  # that would silently stop refusing: an aggregate is planted as a real directive and the probe
  # must see it. Without this the comment-stripping above could go wrong in the other direction
  # and strip the whole file, and the check would pass on every host forever.
  printf '@include common-auth\n' >> "${planted}.stripped"
  ftps_pam_directives "${planted}.stripped" | grep -Eq 'password-auth|common-auth|system-auth' \
    || fail "the aggregate-stack probe cannot see an @include common-auth planted as a real directive — it has stopped being able to refuse the thing it exists to refuse"
  rm -f -- "$planted" "${planted}.stripped"

  echo "The PAM stack CONTAINS the maran-ftps membership test in both the auth and the account phase, and pulls in no aggregate stack; the probe was shown to find that line when it is there and to miss it when it is not."
  echo "UNOBSERVED HERE: whether that content makes the stack REQUIRE membership. Presence is not control flow — a 'sufficient' module in front of these lines leaves every one of them byte-identical and admits everybody, and this assertion cannot tell the two files apart. What the stack ACTUALLY answers is measured next, through the real libpam, by assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for."
}

# compile_pam_witness: build docker/polygon/pam-witness.c against THIS family's own libpam.
#
# Compiled here rather than shipped as a binary for the reason the Dockerfiles compile the agent
# in each image: the two families ship different glibc and different PAM releases, and a binary
# built anywhere else is a binary that either refuses to start or links a library nobody runs.
#
# A missing compiler or a missing header is a named failure and never a skipped assertion: an
# authorization check that quietly did not run is the exact shape rules/testing.md forbids.
compile_pam_witness() {
  local compiler
  compiler="$(command -v cc || command -v gcc || true)"
  [ -n "$compiler" ] \
    || fail "no C compiler in this image, so the PAM witness cannot be built and the FTPS stack's actual answer cannot be observed. The Dockerfile must install one before this script runs."
  [ -f /usr/include/security/pam_appl.h ] \
    || fail "the PAM development headers are not in this image (/usr/include/security/pam_appl.h), so the PAM witness cannot be built. The Dockerfile must install them before this script runs: libpam0g-dev on the Debian family, pam-devel on the RHEL family."
  install -d -o root -g root -m 0755 "$(dirname -- "$PAM_WITNESS_BINARY")"
  "$compiler" -O0 -Wall -Wextra -Werror -o "$PAM_WITNESS_BINARY" "$PAM_WITNESS_SOURCE" -lpam \
    || fail "docker/polygon/pam-witness.c did not compile in this image. Nothing below it has measured anything, which is why this is a failure and not a warning."
  [ -x "$PAM_WITNESS_BINARY" ] \
    || fail "${PAM_WITNESS_BINARY} is not executable after the compiler reported success"
}

# pam_witness_says: what the named PAM service answers for one login and one password.
#
# One word plus the two codes, so a caller compares an ANSWER and a reader sees why. `BROKEN` is
# deliberately a third answer and not folded into `REFUSED`: pam_start failing is a harness fault,
# and reading it as a refusal is how a check that can no longer observe anything reports the
# strictest possible result and passes.
pam_witness_says() {
  local service="$1" user="$2" password="$3" status=0 output
  output="$("$PAM_WITNESS_BINARY" "$service" "$user" "$password" 2>&1)" || status=$?
  output="$(printf '%s' "$output" | tr '\n' ' ')"
  case "$status" in
    0) printf 'ACCEPTED %s' "$output" ;;
    1) printf 'REFUSED %s' "$output" ;;
    *) printf 'BROKEN exit %s: %s' "$status" "$output" ;;
  esac
}

# ftps_witness_cases: how many logins the assertion below actually put to the library.
#
# A module list PAM cannot load, a service file at a name nothing reads, a witness that exits
# before its first transaction — every one of those produces an assertion that runs no case and
# prints its success line. The count is the axis that goes blind, so the count is what is guarded.
ftps_witness_cases=0

# assert_pam_answer: one login through one service, against the answer it must get.
assert_pam_answer() {
  local expected="$1" service="$2" user="$3" password="$4" why="$5" answer
  answer="$(pam_witness_says "$service" "$user" "$password")"
  ftps_witness_cases=$((ftps_witness_cases + 1))
  case "$answer" in
    "${expected} "*) return 0 ;;
  esac
  fail "PAM service ${service} answered ${answer} for ${user}, and it must be ${expected}. ${why}"
}

# assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for: what the real
# pam_authenticate does with the stack step 89 installed, asked through the real libpam.
#
# This is the assertion the one above it cannot be. `assert_ftps_pam_stack_requires_group_membership`
# reads the file, and a PAM stack is not its lines — it is their control flow. Measured on
# 2026-09-09 on both families: prepend `auth sufficient pam_permit.so` to the shipped stack, leave
# both `required pam_succeed_if` lines byte-identical, and every content check stays green while an
# account in no group, typing the WRONG password, is authenticated. That is a total authentication
# bypass under a green gate, and only the library can see it.
#
# The four cases against the shipped stack are the four answers the arrangement is made of:
#
#   member,  correct password  -> ACCEPTED   the daemon has to let its own customers in
#   outsider, correct password -> REFUSED    this is what the group gate IS. The outsider holds a
#                                            valid shadow entry; membership is the whole difference
#   outsider, wrong password   -> REFUSED    the case that catches the bypass mutant
#   member,   wrong password   -> REFUSED    pam_unix is really consulted; the group test is an
#                                            authorization gate and never a substitute for a password
#
# The fifth case is the inverse control, and it is the reviewer's mutant itself: the same
# directives with `sufficient pam_permit.so` prepended, planted under a service name of its own,
# where the outsider with a wrong password MUST be accepted. Without it the three refusals above
# would also be produced by a witness that can no longer authenticate anybody — a broken libpam, a
# throwaway account whose password never got set — and a gate that refuses everything passes every
# test that only ever hands it something it must refuse (rules/testing.md).
assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for() {
  local SCRIPT_DIR="$INSTALLER_ROOT"
  ensure_ftps_group
  install_ftps_pam_service
  compile_pam_witness

  # Made here and removed at the end: these accounts exist for five PAM transactions and must not
  # be in the image any suite later runs against. `-M` so no home directory is created and `-N` so
  # neither gets a group of its own, which keeps the ONLY group difference between them the one
  # under test.
  userdel -f "$FTPS_WITNESS_MEMBER" >/dev/null 2>&1 || true
  userdel -f "$FTPS_WITNESS_OUTSIDER" >/dev/null 2>&1 || true
  useradd -M -N "$FTPS_WITNESS_MEMBER" \
    || fail "could not create the throwaway FTPS member account ${FTPS_WITNESS_MEMBER}"
  useradd -M -N "$FTPS_WITNESS_OUTSIDER" \
    || fail "could not create the throwaway FTPS outsider account ${FTPS_WITNESS_OUTSIDER}"
  usermod -aG "$MARAN_FTPS_GROUP" "$FTPS_WITNESS_MEMBER" \
    || fail "could not add ${FTPS_WITNESS_MEMBER} to ${MARAN_FTPS_GROUP}"
  printf '%s:%s\n' "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_PASSWORD" | chpasswd \
    || fail "could not set the throwaway password on ${FTPS_WITNESS_MEMBER}"
  printf '%s:%s\n' "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_PASSWORD" | chpasswd \
    || fail "could not set the throwaway password on ${FTPS_WITNESS_OUTSIDER}"

  # The premise of every case below, checked rather than assumed: the two accounts differ by that
  # membership and by nothing else the stack asks about. A refusal for the outsider means nothing
  # if the outsider was never given a password, and an acceptance for the member means nothing if
  # the member is not actually in the group.
  id -nG "$FTPS_WITNESS_MEMBER" | tr ' ' '\n' | grep -qx "$MARAN_FTPS_GROUP" \
    || fail "${FTPS_WITNESS_MEMBER} is not in ${MARAN_FTPS_GROUP} after usermod -aG, so the accepted case below would prove nothing about membership"
  if id -nG "$FTPS_WITNESS_OUTSIDER" | tr ' ' '\n' | grep -qx "$MARAN_FTPS_GROUP"; then
    fail "${FTPS_WITNESS_OUTSIDER} IS in ${MARAN_FTPS_GROUP}, so the refusals below would be refusals of a member and would say nothing about the group gate"
  fi

  # The inverse control's stack: the shipped directives, untouched, with a `sufficient` short
  # circuit in front of them. This is the file the content check above cannot tell from the real
  # one.
  local mutant_path="/etc/pam.d/${FTPS_MUTANT_PAM_SERVICE}"
  {
    printf 'auth sufficient pam_permit.so\n'
    printf 'account sufficient pam_permit.so\n'
    cat "$MARAN_FTPS_PAM_SERVICE"
  } > "$mutant_path"
  chmod 0644 "$mutant_path"

  local service
  service="$(basename -- "$MARAN_FTPS_PAM_SERVICE")"

  assert_pam_answer ACCEPTED "$service" "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_PASSWORD" \
    "A member of ${MARAN_FTPS_GROUP} holding the right password is the login this daemon exists to serve; a stack that refuses it refuses every customer."
  assert_pam_answer REFUSED "$service" "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_PASSWORD" \
    "This is the whole reason the group gate exists: an account that holds a VALID local password and is not in ${MARAN_FTPS_GROUP} must not reach this daemon. Every other login on the host — root, the panel uid, every system account — is refused by the same line."
  assert_pam_answer REFUSED "$service" "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_WRONG_PASSWORD" \
    "A non-member with a wrong password being accepted is the shape of a total authentication bypass: it is what a 'sufficient' module in front of the required ones produces, with every line of this file still present and every content check still green."
  assert_pam_answer REFUSED "$service" "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_WRONG_PASSWORD" \
    "Membership authorizes; it does not authenticate. If a member gets in with the wrong password, pam_unix.so is no longer being consulted and the group test has become the only thing between the network and the account."
  assert_pam_answer ACCEPTED "$FTPS_MUTANT_PAM_SERVICE" "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_WRONG_PASSWORD" \
    "This is the inverse control. A stack with 'sufficient pam_permit.so' in front of the shipped directives MUST let a non-member in with a wrong password; a witness that refuses even that one is refusing everything, and the three refusals above were measuring nothing."

  [ "$ftps_witness_cases" -eq 5 ] \
    || fail "the PAM witness put ${ftps_witness_cases} logins to the library, not the 5 this assertion is made of. A case that is not run is not a case that passed — it is one that no longer exists."

  rm -f -- "$mutant_path" "$PAM_WITNESS_BINARY"
  userdel -f "$FTPS_WITNESS_MEMBER" >/dev/null 2>&1 || true
  userdel -f "$FTPS_WITNESS_OUTSIDER" >/dev/null 2>&1 || true

  # The mail spool, deleted by name, and it is not tidiness. The RHEL family's `useradd` creates
  # /var/spool/mail/<login> even under `-M`, the Debian family's does not, and `userdel -f` leaves
  # it behind on both. Measured: the alma9 image shipped for a while with two 0-byte spool files
  # owned by the raw uids 1000 and 1001 — the numbers these two accounts held — after the accounts
  # themselves were long gone. A later suite's account that lands on a recycled uid then finds a
  # file it OWNS at a path it never created, which is exactly the kind of state a polygon image is
  # supposed not to carry between suites. `userdel -r` is not the fix: these accounts are made with
  # `-M`, so `-r` would spend its time complaining about a home directory that was never there.
  local spool
  for spool in "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_OUTSIDER"; do
    rm -f -- "/var/spool/mail/${spool}" "/var/mail/${spool}"
  done

  local leftover
  for leftover in "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_OUTSIDER"; do
    if getent passwd "$leftover" >/dev/null; then
      fail "the throwaway account ${leftover} outlived the assertion that made it and would be in this image, where every later suite would meet it"
    fi
    # The guard is widened to the axis that actually went blind. `getent passwd` answered
    # correctly all along while two files named after these accounts sat in the image, so a
    # deletion check that only asks the passwd database is one that agrees with the defect.
    if [ -e "/var/spool/mail/${leftover}" ] || [ -e "/var/mail/${leftover}" ]; then
      fail "the throwaway account ${leftover} is gone from /etc/passwd but its mail spool is still in this image, owned by a uid that is now free to be handed to a later suite's account"
    fi
  done

  echo "The real pam_authenticate/pam_acct_mgmt, against the stack step 89 installed at ${MARAN_FTPS_PAM_SERVICE}, in 5 transactions: a ${MARAN_FTPS_GROUP} member with the right password is ACCEPTED; a non-member with the RIGHT password is REFUSED; a non-member with a wrong password is REFUSED; a member with a wrong password is REFUSED; and the same directives with 'sufficient pam_permit.so' prepended ACCEPT the non-member with the wrong password, which is what proves the four answers above were the stack's and not the witness's."
  echo "UNOBSERVED HERE: everything the DAEMON does around those two calls — the TLS handshake, the chroot into the jail, the data channel, and vsftpd's own pam_service_name reaching this file. No daemon boots in an image build; ftps_on_a_real_host.rs is where the login itself is proven."
}

# agent_string_after: the first string literal following a marker in a Rust source, read with the
# file flattened to one line.
#
# Flattened on purpose: a declaration rustfmt wrapped across lines is invisible to a line-oriented
# regex, and a constant that silently retires from a drift check is worse than one that was never
# in it. The same defect, and the same fix, as the `ReadWritePaths=` comparison in
# scripts/lib/check-structure.sh.
agent_string_after() {
  local marker="$1" file="$2"
  # `|| true` and not a bare grep: this script runs under `set -o pipefail`, so a grep that
  # matches nothing would make the ASSIGNMENT in the caller fail and kill the script with no
  # message at all. Measured, on the mutant that renames the function this hunts for: the run
  # exited 1 having printed nothing, which is the one outcome worse than a false green — a check
  # that failed for a reason nobody can read. An empty answer is returned instead, and every
  # caller turns it into a named failure.
  tr '\n' ' ' < "$file" | { grep -o "${marker}[^\"]*\"[^\"]*\"" || true; } | head -1 \
    | sed 's/.*"\(.*\)"$/\1/'
}

# assert_the_installer_and_the_agent_spell_the_ftps_names_the_same: the seam between the two halves
# of FTPS, which until this existed was three literals each asserted only against itself.
#
# The arrangement needs both halves to agree on three names. The installer creates the group and
# writes /etc/pam.d/<service>; the agent adds each login to that group, renders `pam_service_name`
# into the vsftpd configuration and builds every jail under the same root. If either side's spelling
# moves, the installer creates group A, the agent adds logins to group B, the PAM stack tests group
# A — and EVERY FTPS login on EVERY install fails while every gate in this repository stays green.
# Nothing observed that, and installer/lib/89-ftps.sh claimed that these images did.
#
# What it compares: the agent's SOURCE TEXT against the installer's own constants, read from the
# step this script has already sourced. What it therefore cannot see is stated in its output — a
# value the agent computes rather than declares, and anything the compiler would do to these
# literals. The agent's own unit tests pin them from the other side
# (agent/crates/distro/src/tests/*/\*_adapter_tests.rs, agent/crates/agent-core/src/tests/agent_paths_tests.rs),
# which is what makes this a comparison of two independent statements rather than of one restated.
assert_the_installer_and_the_agent_spell_the_ftps_names_the_same() {
  local compared=0 value planted

  # The positive control first, and on the axis that can go blind: the extractor. If it returned
  # the empty string for every input — a marker that no longer matches, a moved file — every
  # comparison below would be `"" = ""` against a value read the same way, or would fail for a
  # reason that reads like drift. So it is pointed at a copy carrying a value that is deliberately
  # NOT the shipped one, and it must report that value back.
  #
  # The sentinel is derived from the file itself and NOT from the installer's constant, which was
  # the first version of this control and was wrong: with the installer's own spelling mutated, the
  # planted `sed` matched nothing and the control failed — a red run, but one that blamed the
  # extractor for the drift the check exists to name. A control must fail for its own reason only.
  planted="/tmp/polygon-agent-name-probe.rs"
  value="$(agent_string_after 'fn ftps_group()' "$AGENT_DEBIAN_SERVICES")"
  [ -n "$value" ] \
    || fail "no string literal at all could be read out of ftps_group() in ${AGENT_DEBIAN_SERVICES}. The extractor is not reading this file, so every agreement reported below would be an agreement between two empty strings (rules/testing.md)."
  sed "s/\"${value}\"/\"polygon-planted-name\"/" "$AGENT_DEBIAN_SERVICES" > "$planted"
  [ "$(agent_string_after 'fn ftps_group()' "$planted")" = "polygon-planted-name" ] \
    || fail "the Rust literal extractor read '${value}' out of debian_services.rs but does not read 'polygon-planted-name' out of a copy carrying exactly that instead — it is not reading the file's content, and its agreements mean nothing."
  rm -f -- "$planted"

  # 1 and 2: the group, on each family's adapter. Both, and not one of them, because the two
  # families answer it independently and a drift on either is a family whose logins are added to a
  # group the PAM stack does not test.
  local services_file
  for services_file in "$AGENT_DEBIAN_SERVICES" "$AGENT_RHEL_SERVICES"; do
    value="$(agent_string_after 'fn ftps_group()' "$services_file")"
    [ -n "$value" ] \
      || fail "no string literal could be read out of ftps_group() in ${services_file} — this comparison had nothing to compare, which is a failure to observe and not an agreement (rules/testing.md)"
    [ "$value" = "$MARAN_FTPS_GROUP" ] \
      || fail "the FTPS group has drifted: installer/lib/89-ftps.sh creates '${MARAN_FTPS_GROUP}' and ${services_file} adds logins to '${value}'. Every FTPS login on this family would be refused by a PAM stack testing a group nobody is in."
    compared=$((compared + 1))
  done

  # 3: the jail root. The agent builds /<root>/<account> and bind-mounts the home beneath it; the
  # installer creates the root, with the mode that decides whether the daemon can traverse it.
  value="$(agent_string_after 'FTPS_JAIL_ROOT' "$AGENT_PATHS")"
  [ -n "$value" ] \
    || fail "no string literal could be read for FTPS_JAIL_ROOT in ${AGENT_PATHS} — this comparison had nothing to compare (rules/testing.md)"
  [ "$value" = "$MARAN_FTPS_JAIL_ROOT" ] \
    || fail "the FTPS jail root has drifted: installer/lib/89-ftps.sh creates '${MARAN_FTPS_JAIL_ROOT}' and AgentPaths::FTPS_JAIL_ROOT is '${value}'. The agent would mount every account's jail under a directory the installer never made, root-owned and traversable."
  compared=$((compared + 1))

  # 4: the PAM service. `pam_service_name` in the rendered vsftpd configuration is a NAME that
  # libpam resolves under /etc/pam.d; the installer writes the file. They must be the same name.
  # `|| true` for the same pipefail reason as the extractor above: a template that has lost the
  # line must produce the named failure below, never a script that dies without printing one.
  value="$( { grep -E '^pam_service_name=' "$AGENT_VSFTPD_TEMPLATE" || true; } | tail -1 | cut -d= -f2-)"
  [ -n "$value" ] \
    || fail "no pam_service_name= line in ${AGENT_VSFTPD_TEMPLATE} — the daemon's configuration no longer names a PAM service where this check can read it, so nothing compares it against the file installer/lib/89-ftps.sh writes (rules/testing.md)"
  case "$value" in
    *'{'*) fail "pam_service_name in ${AGENT_VSFTPD_TEMPLATE} is rendered from a template expression ('${value}'), so its value is not readable here. Whatever decides it must be compared against installer/lib/89-ftps.sh's MARAN_FTPS_PAM_SERVICE somewhere that can see it — this check cannot, and must not pretend to." ;;
  esac
  [ "$value" = "$(basename -- "$MARAN_FTPS_PAM_SERVICE")" ] \
    || fail "the FTPS PAM service name has drifted: the agent renders pam_service_name=${value} and installer/lib/89-ftps.sh writes ${MARAN_FTPS_PAM_SERVICE}. vsftpd would resolve /etc/pam.d/${value}, which is the distribution's file or no file at all, and the group gate would not be in the path of a single login."
  compared=$((compared + 1))

  # 5: and the directory that name is resolved under. A service NAME is only half of a path.
  [ "$(dirname -- "$MARAN_FTPS_PAM_SERVICE")" = "/etc/pam.d" ] \
    || fail "installer/lib/89-ftps.sh writes its PAM stack to ${MARAN_FTPS_PAM_SERVICE}, outside /etc/pam.d, where libpam resolves a pam_service_name. The stack would not be read by anything."
  compared=$((compared + 1))

  # 6: the transfer log path, read from the agent first. Its own planted control, on its own
  # marker: `agent_string_after` is only as good as the marker it is given, and a marker that has
  # stopped matching returns the empty string, which every comparison below would then be
  # comparing against. The sentinel is derived from the file's own value for the same reason the
  # first control is -- a control must fail for its own reason only.
  local expected_log
  expected_log="$(agent_string_after 'FTPS_LOG_PATH' "$AGENT_ENABLE_FTPS")"
  [ -n "$expected_log" ] \
    || fail "no string literal could be read for FTPS_LOG_PATH in ${AGENT_ENABLE_FTPS} — the path the agent renders into vsftpd_log_file is not where this check can read it, so nothing below compared anything (rules/testing.md)"
  case "$expected_log" in
    /*.log) ;;
    *) fail "FTPS_LOG_PATH in ${AGENT_ENABLE_FTPS} reads '${expected_log}', which is not an absolute path ending in .log. Either the constant has become something this extractor cannot read, or the daemon is being pointed somewhere unexpected; either way the comparisons below would be comparing the wrong string." ;;
  esac
  planted="/tmp/polygon-agent-log-probe.rs"
  sed "s|\"${expected_log}\"|\"/var/log/polygon-planted/probe.log\"|" "$AGENT_ENABLE_FTPS" > "$planted"
  [ "$(agent_string_after 'FTPS_LOG_PATH' "$planted")" = "/var/log/polygon-planted/probe.log" ] \
    || fail "the Rust literal extractor read '${expected_log}' out of enable_ftps.rs but does not read '/var/log/polygon-planted/probe.log' out of a copy carrying exactly that instead — it is not reading this file's content, and its agreements mean nothing."
  rm -f -- "$planted"

  # 7: the rotation policy rotates THAT file. One stanza, and its header is the agent's path.
  #
  # The stanza count is part of the comparison and not pedantry: a second stanza would make
  # `head -1` pick whichever came first, so a policy that had grown a second block could agree
  # here while rotating something else. `|| true` for the pipefail reason the extractor documents.
  local stanza_lines stanza_count stanza_path
  stanza_lines="$( { grep -E '^[[:space:]]*/[^[:space:]]+[[:space:]]*\{[[:space:]]*$' "$FTPS_LOGROTATE_SOURCE" || true; } )"
  stanza_count="$(printf '%s' "$stanza_lines" | grep -c . || true)"
  [ "$stanza_count" -eq 1 ] \
    || fail "installer/logrotate/maran-ftps has ${stanza_count} rotation stanzas, not the 1 this comparison is written for. With none, nothing rotates the FTPS transfer log at all; with more, the file this check reads is not necessarily the one that rotates it (rules/testing.md)."
  stanza_path="$(printf '%s\n' "$stanza_lines" | sed 's/[[:space:]]*{[[:space:]]*$//' | tr -d '[:space:]')"
  [ "$stanza_path" = "$expected_log" ] \
    || fail "the FTPS transfer log path has drifted: the agent renders vsftpd_log_file=${expected_log} and installer/logrotate/maran-ftps rotates ${stanza_path}. vsftpd would write one line per login and per transfer, from every customer, into a file nothing bounds — fastest on exactly the host where FTPS is used most — until the operator's partition fills and takes the panel and every site on it down. Nothing else reports this."
  compared=$((compared + 1))

  # 8, 9 and 10: the census. Not "the two copies agree" but "every copy agrees", derived from the
  # files rather than listed here, so a THIRD site added tomorrow is caught by name instead of by
  # luck. Every log path under /var/log written anywhere in the shipped rotation policy, in the
  # installer step that installs it, or in the agent file that declares the constant -- comments
  # included, because a comment that names the wrong path is what the next person edits from.
  local census_file census_here mention
  for census_file in "$FTPS_LOGROTATE_SOURCE" "$FTPS_STEP" "$AGENT_ENABLE_FTPS"; do
    census_here=0
    while IFS= read -r mention; do
      [ -n "$mention" ] || continue
      census_here=$((census_here + 1))
      [ "$mention" = "$expected_log" ] \
        || fail "${census_file} names the FTPS transfer log as '${mention}', and the agent renders '${expected_log}'. Two spellings of the one file this product writes for FTPS: whichever of them the rotation policy does not name grows without bound."
    done < <( { grep -oE '/var/log/[A-Za-z0-9_./-]*\.log' "$census_file" || true; } )
    # The vacuity guard, per file, on the axis that can go blind: a file that names the path
    # nowhere agrees with everything. Each of these three names it at least once today, and a
    # file that has stopped naming it has either lost the line or spelled it in a way this
    # census cannot see -- both are failures to observe (rules/testing.md).
    [ "$census_here" -gt 0 ] \
      || fail "no log path under /var/log appears anywhere in ${census_file}, so its agreement with the agent's '${expected_log}' was an agreement with nothing (rules/testing.md)"
    compared=$((compared + 1))
  done

  # 11: the FIFTH name, and the one nothing read until this block existed: the path of the
  # vsftpd.conf this panel's daemon is started against. It is spelled three times, independently —
  # the agent DECLARES it (`AgentPaths::VSFTPD_CONFIG_PATH`) and both writes and reads it back
  # there; installer/systemd/maran-ftps.service hands it to vsftpd on its ExecStart line; and
  # installer/lib/89-ftps.sh creates the DIRECTORY it lands in. Before this, the only thing in the
  # repository that mentioned all three together was a comment, and
  # docker/polygon/assert-installer-steps.sh's own artifact table restated the directory literal
  # against itself, which is not a comparison.
  #
  # Why the drift is silent, which is why it is worth a check rather than a comment. If the AGENT's
  # path moves and the unit's does not, the agent renders a config the daemon never reads and the
  # daemon starts against whatever is still at the old path — on any host where FTPS was enabled
  # once, that is a PREVIOUS render, so the panel reports the settings it just wrote while the
  # running daemon serves the old ones. The old ones may have `force_local_logins_ssl=NO`.
  # Every unit test on both sides still passes: each half is consistent with itself.
  local expected_config config_dir
  expected_config="$(agent_string_after 'VSFTPD_CONFIG_PATH' "$AGENT_PATHS")"
  [ -n "$expected_config" ] \
    || fail "no string literal could be read for VSFTPD_CONFIG_PATH in ${AGENT_PATHS} — the path the agent writes the daemon's configuration to is not where this check can read it, so nothing below compared anything (rules/testing.md)"
  case "$expected_config" in
    /*.conf) ;;
    *) fail "VSFTPD_CONFIG_PATH in ${AGENT_PATHS} reads '${expected_config}', which is not an absolute path ending in .conf. Either the constant has become something this extractor cannot read, or the daemon is being pointed somewhere unexpected; either way the comparisons below would be comparing the wrong string." ;;
  esac
  # Its own planted control, on its own marker, for the reason the two above carry one: an
  # extractor that has stopped matching returns the empty string, and the guard above would then
  # be the only thing between that and four agreements between nothing.
  planted="/tmp/polygon-agent-config-probe.rs"
  sed "s|\"${expected_config}\"|\"/etc/polygon-planted/probe.conf\"|" "$AGENT_PATHS" > "$planted"
  [ "$(agent_string_after 'VSFTPD_CONFIG_PATH' "$planted")" = "/etc/polygon-planted/probe.conf" ] \
    || fail "the Rust literal extractor read '${expected_config}' out of agent_paths.rs but does not read '/etc/polygon-planted/probe.conf' out of a copy carrying exactly that instead — it is not reading this file's content, and its agreements mean nothing."
  rm -f -- "$planted"
  config_dir="$(dirname -- "$expected_config")"
  [ "$config_dir" = "$MARAN_FTPS_CONFIG_DIR" ] \
    || fail "the FTPS configuration directory has drifted: the agent writes '${expected_config}' and installer/lib/89-ftps.sh creates '${MARAN_FTPS_CONFIG_DIR}'. The agent's safe_write would be renaming into a directory the installer never made, so enabling FTPS fails on every install — or, worse, succeeds into a directory somebody else owns."
  compared=$((compared + 1))

  # 12: the unit hands the daemon THAT file. Matched as a whole ARGUMENT and never as a substring:
  # a substring test passes a deliberately corrupted path ('/etc/maran/vsftpd/vsftpd.conf.bak'
  # contains the shipped spelling), which is precisely the trap that let a corrupted path through a
  # sibling check in this file. The ExecStart line is split on whitespace and one of its words must
  # equal the agent's constant exactly.
  local exec_line word exec_names_config=0
  exec_line="$( { grep -E '^ExecStart=' "$FTPS_UNIT_SOURCE" || true; } | tail -1)"
  [ -n "$exec_line" ] \
    || fail "no ExecStart= line in ${FTPS_UNIT_SOURCE} — the unit this installer installs starts nothing, and nothing below compared the configuration path it would have handed the daemon (rules/testing.md)"
  for word in ${exec_line#ExecStart=}; do
    [ "$word" = "$expected_config" ] && exec_names_config=1
  done
  [ "$exec_names_config" -eq 1 ] \
    || fail "the FTPS configuration path has drifted: the agent writes '${expected_config}' and installer/systemd/maran-ftps.service execs '${exec_line}'. The daemon would be started against a file the agent does not write — on a host where FTPS has been enabled before, a PREVIOUS render — so the panel would report the configuration it just saved while vsftpd served the old one, forced TLS included."
  compared=$((compared + 1))

  # 13, 14 and 15: the same census shape as the log path, on this path's own prefix. Every mention
  # of anything under the agent's configuration directory, in the installer step that creates it,
  # in the unit that reads it and in the agent file that declares it — comments included, because a
  # comment naming the wrong path is what the next person edits from. A fourth site added tomorrow
  # is caught by name rather than by luck. The prefix is DERIVED from the agent's constant and not
  # written here, so a wholesale move of the directory empties the census in the other two files
  # and trips the vacuity guard by name instead of quietly matching nothing.
  for census_file in "$FTPS_STEP" "$FTPS_UNIT_SOURCE" "$AGENT_PATHS"; do
    census_here=0
    while IFS= read -r mention; do
      [ -n "$mention" ] || continue
      census_here=$((census_here + 1))
      case "$mention" in
        "$expected_config"|"$config_dir") ;;
        *) fail "${census_file} names '${mention}' under the agent's FTPS configuration directory, and the agent declares '${expected_config}'. Two spellings of the one file this daemon is started against: whichever of them the daemon reads is not necessarily the one the agent writes." ;;
      esac
    # The trailing `sed` strips sentence punctuation, and it is not cosmetic: two of these three
    # files name the path inside English prose, so `... does not write /etc/maran/vsftpd/vsftpd.conf.`
    # yields a mention one byte longer than the constant and the census fails on a full stop. What it
    # costs is the ability to see a drift to a path that genuinely ENDS in punctuation, which the
    # dirname and ExecStart comparisons above would refuse anyway.
    done < <( { grep -oE "${config_dir}[A-Za-z0-9_./-]*" "$census_file" || true; } | sed 's/[.,;:)]\+$//' )
    [ "$census_here" -gt 0 ] \
      || fail "nothing under '${config_dir}' appears anywhere in ${census_file}, so its agreement with the agent's '${expected_config}' was an agreement with nothing (rules/testing.md)"
    compared=$((compared + 1))
  done

  [ "$compared" -eq 14 ] \
    || fail "the name-agreement check made ${compared} comparisons, not the 14 it is made of. A comparison that is not made is not one that agreed."

  echo "The installer and the agent spell the five FTPS names identically, compared and not assumed: the group '${MARAN_FTPS_GROUP}' against ftps_group() on BOTH family adapters, the jail root '${MARAN_FTPS_JAIL_ROOT}' against AgentPaths::FTPS_JAIL_ROOT, the PAM service '$(basename -- "$MARAN_FTPS_PAM_SERVICE")' against the pam_service_name the agent renders — under /etc/pam.d, where libpam resolves it — and the transfer log '${expected_log}', against the one stanza installer/logrotate/maran-ftps rotates and against EVERY mention of a log path in that file, in installer/lib/89-ftps.sh and in the agent file that declares it — and the daemon's configuration path '${expected_config}', against the directory installer/lib/89-ftps.sh creates, against the whole ExecStart ARGUMENT installer/systemd/maran-ftps.service hands vsftpd, and against EVERY mention of that directory in those two files and in agent_paths.rs."
  echo "UNOBSERVED HERE: this reads the agent's SOURCE TEXT, not the compiled agent. A name a future adapter computes instead of declaring, or a copy of any of these strings outside the files read here, is invisible to it; the agent's own unit tests pin these literals from the other side. For the log path specifically, two more things it cannot see: a path both halves spell identically and that the daemon still cannot write (a missing directory, a mode, an SELinux label) — ftps_on_a_real_host.rs is where the daemon's own answer is observed — and WHAT logrotate does with the file, since this compares the name in the stanza header and judges no directive inside it."
}

# assert_a_planted_symlink_at_the_jail_base_is_replaced_not_followed: the inverse control for
# ensure_ftps_directories.
#
# `install -d` alone exits 0 on a symbolic link to an existing directory and applies the
# ownership and the mode to the link's TARGET — so a step that only ever met a clean host would
# pass every check while every account's jail hung under a path somebody else chose. Each half
# below asserts on an axis that can actually go red: the link is gone, what stands at the name is
# a real root-owned 0711 directory, and the link's target was never written into — following the
# link would have chmod'd or populated /tmp/planted, so its emptiness and its unchanged mode are
# the observation that nothing went through it.
assert_a_planted_symlink_at_the_jail_base_is_replaced_not_followed() {
  rm -rf -- /var/lib/maran-ftps /tmp/planted
  install -d -o root -g root -m 0755 /tmp/planted
  ln -s /tmp/planted /var/lib/maran-ftps

  ensure_ftps_directories

  if [ -L /var/lib/maran-ftps ]; then
    fail "ensure_ftps_directories left the planted symlink at /var/lib/maran-ftps in place — every account jail would be created wherever it points"
  fi
  [ -d /var/lib/maran-ftps ] \
    || fail "/var/lib/maran-ftps is not a real directory after the planted link was replaced"
  [ "$(stat -c '%U:%G %a' /var/lib/maran-ftps)" = "root:root 711" ] \
    || fail "the replaced jail base is $(stat -c '%U:%G %a' /var/lib/maran-ftps), not root:root 711"
  [ -z "$(ls -A /tmp/planted)" ] \
    || fail "ensure_ftps_directories wrote through the symlink: /tmp/planted now holds $(ls -A /tmp/planted)"
  [ "$(stat -c '%a' /tmp/planted)" = "755" ] \
    || fail "ensure_ftps_directories chmod'd through the symlink: /tmp/planted is now mode $(stat -c '%a' /tmp/planted), not the 755 it was planted with"
  rm -rf -- /tmp/planted
  echo "A symbolic link planted at the jail base is replaced by a real root:root 0711 directory, and nothing was written or chmod'd through it."
}

# assert_the_ftps_step_says_it_is_off_and_can_see_when_it_is_not: report_ftps_is_installed_and_off
# run against the real host, and then against the two hosts it exists to refuse.
#
# The step's own report is a gate, and a gate that has only ever seen a good host is one that
# passes even when it refuses nothing. Its two answerable questions are both symlink facts — the
# mask, and any .wants entry naming maran-ftps.service — so both can be broken here and both must
# produce a named refusal. Its third line, the port, is decoration in an image build and the
# report says so itself; that is quoted rather than counted.
assert_the_ftps_step_says_it_is_off_and_can_see_when_it_is_not() {
  local status output wants_directory="/etc/systemd/system/multi-user.target.wants"

  report_ftps_is_installed_and_off \
    || fail "report_ftps_is_installed_and_off refused the host step 89 itself had just built"

  # Enabled: a .wants entry naming our unit is what "somebody turned FTPS on" looks like on
  # disk, and this step promises to leave it off.
  install -d -o root -g root -m 0755 "$wants_directory"
  ln -sfn /etc/systemd/system/maran-ftps.service "${wants_directory}/maran-ftps.service"
  status=0
  output="$( ( report_ftps_is_installed_and_off ) 2>&1 )" || status=$?
  rm -f -- "${wants_directory}/maran-ftps.service"
  [ "$status" -ne 0 ] \
    || fail "report_ftps_is_installed_and_off accepted a host where maran-ftps.service is ENABLED. FTPS ships switched off; enabling it is an administrator action taken in the panel, with its firewall consequences shown first."
  case "$output" in
    *"is ENABLED"*) ;;
    *) fail "report_ftps_is_installed_and_off refused an enabled unit, but not for the reason it should have:
${output}" ;;
  esac

  # Unmasked: the other half, and the more dangerous one.
  local mask_path="/etc/systemd/system/vsftpd.service"
  rm -f -- "$mask_path"
  status=0
  output="$( ( report_ftps_is_installed_and_off ) 2>&1 )" || status=$?
  [ "$status" -ne 0 ] \
    || fail "report_ftps_is_installed_and_off accepted a host whose packaged vsftpd.service is not masked"
  case "$output" in
    *"is not masked"*) ;;
    *) fail "report_ftps_is_installed_and_off refused an unmasked host, but not for the reason it should have:
${output}" ;;
  esac
  mask_packaged_vsftpd

  report_ftps_is_installed_and_off \
    || fail "after both plants were removed, report_ftps_is_installed_and_off refuses a host it must accept"
  echo "The step's own off-report accepts the host it built, and refuses by name both an enabled maran-ftps.service and an unmasked packaged unit."
}

# ---------------------------------------------------------------------------------------------
# The process-signalling census. See
# assert_every_supported_family_installs_the_process_signalling_tool.
# ---------------------------------------------------------------------------------------------

# The uid the behavioural half hands `pkill`, and the reason it is this one.
#
# It must match NOTHING: this runs inside a build layer where the assertions above have started a
# database and an sshd, and a cull that matched a real uid here would kill them. 4294967294 is
# (uint32)-2 — the `nobody` uid on no supported family, allocated to no account by any of them, and
# the value `pkill` itself refuses to treat as a wildcard. What is asserted is therefore the
# "nothing matched" answer, which is the answer `ops::logins::end_account_sessions` treats as a
# SUCCESS (its `NOTHING_MATCHED: i32 = 1`), and the flags it is asked for are that operation's own
# argv in that operation's own order.
readonly SIGNALLING_PROBE_UID="4294967294"

# dependency_step_family_arms: the `case "$MARAN_OS_FAMILY"` arm labels of one function in
# installer/lib/20-dependencies.sh, one per line, with the `*)` catch-all left out.
#
# The file is a PARAMETER with the shipped step as its default, so the planted control below can
# point the same reader at a copy. `$DEPENDENCIES_STEP` is readonly, which is right — the census
# must not be able to move the step out from under itself — and a command-prefix assignment to a
# readonly name is a shell error, not an override.
#
# This is the enumerator the census is built on, and the reason the check is a census rather than
# "both families name a package". A list of families written HERE would agree with the installer
# the day it was typed and be silent on the third family, which is the same defect one release
# later — so the families are read out of the installer's own arms, and a family added there
# tomorrow is caught BY NAME by the comparison below.
#
# It reads the arms between the function's opening line and the first line that is a bare `}` in
# column one, which is how every function in that file ends, and it accepts only the lowercase
# labels the file uses. A label spelled some other way — a glob, two labels joined by `|`, a
# variable — is invisible to it, and the assertion says so in its output.
dependency_step_family_arms() {
  local function_name="$1" file="${2:-$DEPENDENCIES_STEP}"
  awk -v fn="${function_name}() {" '
    index($0, fn) == 1 { inside = 1; next }
    inside && $0 == "}" { inside = 0 }
    inside { print }
  ' "$file" \
    | { grep -oE '^[[:space:]]+[a-z][a-z0-9_]*\)' || true; } \
    | sed 's/[[:space:])]//g' \
    | sort -u
}

# assert_every_supported_family_installs_the_process_signalling_tool: the census this branch owed
# after the suspension work gained a dependency on `pkill` and named it nowhere.
#
# WHAT BREAKS WITHOUT IT. Suspending a hosting account locks its logins and then ends the sessions
# already open — `ops::logins::end_account_sessions` spawns `DistroAdapter::pkill_binary()` with
# `--signal KILL --count --uid <uid>` — because a lock that a connected SFTP or FTPS client never
# notices is a suspension in name only. With the program absent the cull is
# `LoginsError::SpawnFailed` and the whole suspension fails, so the panel shows an account it could
# not suspend while the customer's transfers keep running. Measured 2026-09-12: `/usr/bin/pkill`
# does not exist in the pinned AlmaLinux 9 base image, no package this installer or these images
# install pulls `procps-ng` in, and installer/lib/20-dependencies.sh named neither spelling.
#
# WHY A CENSUS AND NOT "THE TWO FAMILIES BOTH NAME IT". The two families spell the package
# differently — `procps` on the Debian family, `procps-ng` on the RHEL family — so the obvious
# check is two named copies compared to each other, which is exactly the arrangement that goes
# quiet when a THIRD family arrives: a new `case` arm in the installer with no signalling package
# would satisfy a check written about debian and rhel. So the family list is DERIVED from the
# installer's own arms — from `pkg_install`, the one function every package install in this product
# goes through — and every family it finds must have an arm in `signalling_packages_for_family`
# naming a non-empty package, with the reverse direction checked too so a stale arm for a family
# the installer can no longer install for is named rather than believed.
#
# AND IT IS BEHAVIOURAL AS WELL AS TEXTUAL, because the text cannot see two things that have both
# already happened in this repository: a package that installs and puts the program somewhere else
# (the `passwd` gap), and a program whose FLAGS differ between the two families' versions — the
# Debian family ships procps 4.x and the RHEL family procps-ng 3.3.17, and the cull's argv is
# `--signal KILL --count --uid`. So the census ends by installing through the installer's own
# function and running that exact argv against a uid that matches nothing.
# assert_the_postgresql_conf_resolver_finds_a_real_debian_layout: the resolver in 30-postgresql.sh
# against the directory shape Debian and Ubuntu actually create.
#
# WHY THIS EXISTS. The resolver searched `-maxdepth 2` while the file lives three levels down at
# /etc/postgresql/<major>/<cluster>/postgresql.conf, so on Ubuntu 24.04 the packages installed, the
# cluster was created, and the installer then refused with "could not locate postgresql.conf" —
# pointing at a file that was exactly where the distribution puts it. It reached a real server
# before anybody saw it, because this image installs MariaDB and sshd and NOT PostgreSQL: the step
# is copied in and never executed, so nothing had ever run this resolver against a real layout.
#
# Installing PostgreSQL here to close that would cost minutes on every image build for one
# function. Instead the layout is built in a temp directory and the resolver is pointed at it,
# which is the same question asked cheaply. What this does NOT cover, stated rather than implied:
# everything else in step 30 — the rewrite, the restart, and the no-TCP assertion — still runs
# nowhere.
assert_the_postgresql_conf_resolver_finds_a_real_debian_layout() {
  local root conf hba
  root="$(mktemp -d)"
  mkdir -p "${root}/etc/postgresql/16/main"
  : >"${root}/etc/postgresql/16/main/postgresql.conf"
  : >"${root}/etc/postgresql/16/main/pg_hba.conf"

  conf="$(find "${root}/etc/postgresql" -maxdepth 3 -name postgresql.conf 2>/dev/null | sort -V | tail -1)"
  hba="$(find "${root}/etc/postgresql" -maxdepth 3 -name pg_hba.conf 2>/dev/null | sort -V | tail -1)"

  [ -n "$conf" ] || fail "the postgresql.conf resolver found nothing in a layout Debian creates:
${root}/etc/postgresql/16/main/postgresql.conf exists and the search missed it. This is the depth
bug that reached a real Ubuntu 24.04 server."
  [ -n "$hba" ] || fail "the pg_hba.conf resolver found nothing in a layout Debian creates."

  # The inverse control: the depth the resolver USED to have must fail on the same tree, or this
  # assertion would pass just as happily against the bug it was written for.
  if find "${root}/etc/postgresql" -maxdepth 2 -name postgresql.conf 2>/dev/null | grep -q .; then
    fail "maxdepth 2 found postgresql.conf in this layout, so this assertion cannot tell the fixed
resolver from the broken one and proves nothing."
  fi

  rm -rf -- "$root"
  echo "assert-installer-steps.sh: the postgresql.conf resolver finds a real Debian layout, and the
  depth it replaced does not."
}

assert_every_supported_family_installs_the_process_signalling_tool() {
  local censused=0 planted family declared value
  local reference_arms signalling_arms reference_count signalling_count

  # 1. The declared path, from the agent rather than from a literal here: `ops` never writes the
  # path, it asks the adapter (rules/architecture.md), so the adapter is where the installer's
  # obligation is written down. Both families are read, because each answers independently.
  declared=""
  for value in "$AGENT_DEBIAN_SERVICES" "$AGENT_RHEL_SERVICES"; do
    local path_here
    path_here="$(agent_string_after 'fn pkill_binary()' "$value")"
    [ -n "$path_here" ] \
      || fail "no string literal could be read out of pkill_binary() in ${value} — the path the suspension cull execs is not where this check can read it, so nothing below compared anything (rules/testing.md)"
    case "$path_here" in
      /*) ;;
      *) fail "pkill_binary() in ${value} reads '${path_here}', which is not an absolute path. The agent's process-execution allow-list is absolute paths only (rules/rust.md), so either the extractor is reading the wrong literal or the adapter has stopped declaring one." ;;
    esac
    if [ -n "$declared" ] && [ "$declared" != "$path_here" ]; then
      fail "the two adapters declare different paths for the suspension cull's signalling tool: '${declared}' and '${path_here}' in ${value}. The installer's assert_agent_tooling checks one path, so one family would be installing a package and proving nothing about it."
    fi
    declared="$path_here"
    censused=$((censused + 1))
  done

  # Its planted control, on the axis that can go blind: an extractor whose marker has stopped
  # matching returns the empty string, and the guard above would then be the only thing between
  # that and a comparison between nothing. The sentinel is derived from the file's own value, so
  # this control fails for its own reason only.
  planted="/tmp/polygon-signalling-path-probe.rs"
  sed "s|\"${declared}\"|\"/usr/bin/polygon-planted-signaller\"|" "$AGENT_DEBIAN_SERVICES" > "$planted"
  [ "$(agent_string_after 'fn pkill_binary()' "$planted")" = "/usr/bin/polygon-planted-signaller" ] \
    || fail "the Rust literal extractor read '${declared}' out of debian_services.rs but does not read '/usr/bin/polygon-planted-signaller' out of a copy carrying exactly that instead — it is not reading the file's content, and its agreements mean nothing."
  rm -f -- "$planted"
  censused=$((censused + 1))

  # 2. The two arm lists, both read out of the installer.
  reference_arms="$(dependency_step_family_arms pkg_install)"
  signalling_arms="$(dependency_step_family_arms signalling_packages_for_family)"
  reference_count="$(printf '%s' "$reference_arms" | grep -c . || true)"
  signalling_count="$(printf '%s' "$signalling_arms" | grep -c . || true)"

  # The vacuity guard, on the axis that can actually go blind: the enumerator. An arm list that
  # came back empty — a renamed function, an arm spelled in a way the reader cannot see — makes
  # every comparison below a comparison over nothing, and this product supports two families.
  [ "$reference_count" -ge 2 ] \
    || fail "only ${reference_count} family arm(s) could be read out of pkg_install in ${DEPENDENCIES_STEP}, and this installer supports two families. The census below would have enumerated nothing and reported that every family names a signalling package (rules/testing.md)"
  [ "$signalling_count" -ge 2 ] \
    || fail "only ${signalling_count} family arm(s) could be read out of signalling_packages_for_family in ${DEPENDENCIES_STEP}. Either the function has gone, or its arms are spelled in a way this reader cannot see — both are failures to observe, and neither is a clean census (rules/testing.md)"
  censused=$((censused + 2))

  # Its positive control: the enumerator must SEE a family it has never seen. Without this, a
  # `case` statement the reader has stopped parsing returns a shorter list and the guard above is
  # the only thing between that and a census that quietly stopped reading a family.
  planted="/tmp/polygon-signalling-family-probe.sh"
  awk '
    index($0, "pkg_install() {") == 1 { print; print "    polygonplanted)"; print "      true"; print "      ;;"; next }
    { print }
  ' "$DEPENDENCIES_STEP" > "$planted"
  dependency_step_family_arms pkg_install "$planted" | grep -Fxq -- 'polygonplanted' \
    || fail "the family enumerator reads ${reference_count} arms out of pkg_install but does not read 'polygonplanted' out of a copy carrying exactly that arm — it is not reading the file's content, and the coverage this census claims below means nothing (rules/testing.md)."
  if dependency_step_family_arms pkg_install | grep -Fxq -- 'polygonplanted'; then
    fail "the family enumerator reports 'polygonplanted' for the UNMODIFIED ${DEPENDENCIES_STEP}, which names it nowhere — it is answering from something other than the file, so its planted control proves nothing."
  fi
  rm -f -- "$planted"
  censused=$((censused + 2))

  # 3. The census proper, both directions.
  while IFS= read -r family; do
    [ -n "$family" ] || continue
    printf '%s\n' "$signalling_arms" | grep -Fxq -- "$family" \
      || fail "installer/lib/20-dependencies.sh can install packages for the '${family}' family and signalling_packages_for_family has no arm for it, so a host of that family gets no package supplying ${declared}. Suspending an account there ends at LoginsError::SpawnFailed: the panel records a suspension it could not carry out while the customer's open SFTP and FTPS sessions keep transferring (docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md)."
    censused=$((censused + 1))
  done < <(printf '%s\n' "$reference_arms")

  while IFS= read -r family; do
    [ -n "$family" ] || continue
    printf '%s\n' "$reference_arms" | grep -Fxq -- "$family" \
      || fail "signalling_packages_for_family names a package for the '${family}' family and pkg_install cannot install for it. One of the two statements is wrong and a reader cannot tell which: either the arm is stale — a hole the next family of that name falls into, reading exactly like a decision — or the package manager adapter lost an arm this installer still needs."
    censused=$((censused + 1))
  done < <(printf '%s\n' "$signalling_arms")

  # 4. The installer must PROVE the path on the host it installed it on. A package manager
  # reporting success and a path existing are different facts, and the agent execs the path — the
  # sentence assert_agent_tooling itself is written under. Matched as a whole word so a
  # deliberately corrupted path ('/usr/bin/pkill.bak' contains the shipped spelling) cannot pass.
  local tooling_words word tooling_names_path=0
  tooling_words="$(awk '
    index($0, "assert_agent_tooling() {") == 1 { inside = 1 }
    inside { print }
    inside && $0 == "}" { inside = 0 }
  ' "$DEPENDENCIES_STEP")"
  [ -n "$tooling_words" ] \
    || fail "assert_agent_tooling could not be read out of ${DEPENDENCIES_STEP} at all, so the check below — that the installer proves ${declared} exists on the host — was made against an empty string (rules/testing.md)"
  for word in $tooling_words; do
    # The list is a `for` loop's word list continued across lines, so the last path on a line
    # carries the `;` that ends the list. Stripped here and nowhere else: the comparison stays a
    # whole-word one, which is what keeps '/usr/bin/pkill.bak' from satisfying it.
    word="${word%;}"
    [ "$word" = "$declared" ] && tooling_names_path=1
  done
  [ "$tooling_names_path" -eq 1 ] \
    || fail "installer/lib/20-dependencies.sh installs a signalling package but assert_agent_tooling does not name ${declared}, so nothing proves the package put the program where the agent execs it. That is the shape of the defect that made every suspension on the RHEL family fail: the package was named, the path was not checked, and the failure arrived at exec time in billing."
  censused=$((censused + 1))

  # 5. And now the host. Installed through the installer's own function — never a package name
  # written here, which would be a second authority — and then the operation's own argv.
  # shellcheck disable=SC2046
  pkg_install $(signalling_packages_for_family) >/dev/null \
    || fail "pkg_install $(signalling_packages_for_family) failed on this family. The package name signalling_packages_for_family produces is not one this family's package manager can resolve."
  [ -x "$declared" ] \
    || fail "signalling_packages_for_family's package installed and ${declared} is not executable on this host. The agent execs that exact path, so every suspension on this family would end at LoginsError::SpawnFailed."
  censused=$((censused + 1))

  # The argv the cull sends, against a uid that matches nothing. Status 1 with a count of 0 is
  # `NOTHING_MATCHED`, which that operation treats as a success — an idle account is the state a
  # cull is trying to reach. A status of 2 or 3 would be `pkill` rejecting the flags, which is the
  # per-family risk the text cannot see: the two families ship different major versions of this
  # tool.
  local probe_status=0 probe_output
  probe_output="$("$declared" --signal KILL --count --uid "$SIGNALLING_PROBE_UID" 2>&1)" || probe_status=$?
  [ "$probe_status" -eq 1 ] \
    || fail "${declared} --signal KILL --count --uid ${SIGNALLING_PROBE_UID} exited ${probe_status} on this family, and ops::logins::end_account_sessions reads anything but 0 and 1 as LoginsError::SessionCullFailed. A status of 2 or 3 is this tool refusing the flags, which differ between the versions the two families ship. What it printed:
${probe_output}"
  [ "$(printf '%s' "$probe_output" | tr -d '[:space:]')" = "0" ] \
    || fail "${declared} --count reported '${probe_output}' for a uid no process on this host runs as, and ops::logins::end_account_sessions parses that number as the count of sessions it ended. A non-zero answer here means either the flag no longer counts or the uid matched something, and in both cases the figure the panel shows an operator after a suspension is not a count of anything."
  censused=$((censused + 1))

  echo "The process-signalling census: ${reference_count} family arm(s) read out of pkg_install in installer/lib/20-dependencies.sh, each of them with an arm in signalling_packages_for_family naming a package, and no arm there for a family pkg_install cannot install for; both distro adapters declare ${declared} for the cull, assert_agent_tooling names that path as a whole word, and on this family the installer's own package list installs it and it answers the cull's own argv with 'nothing matched'. ${censused} checks made."
  printf '%s\n' "$reference_arms" | sed 's/^/  family: /'
  echo "UNOBSERVED HERE: no suspension runs in this image and no session is culled — this proves the PROGRAM is installed, accepts the operation's flags and can say 'nothing matched', not that a real SFTP or FTPS session dies. That is what the #[ignore]d polygon suites do (agent/crates/agent/tests/sftp_on_a_real_host.rs, ftps_on_a_real_host.rs), and they are the reason this check exists: without the package they fail with a message about a cull rather than about a missing program."
  echo "UNOBSERVED HERE: the family list is read from the TEXT of one function's case arms. A family arm spelled as a glob, as two labels joined by '|', or through a variable is invisible to the enumerator, and so is a package installed by any path other than pkg_install — 85-mysql.sh, 87-firewall.sh, 88-cron.sh and 89-ftps.sh each name their own family packages, and this census says nothing about those."
}

main() {
  # First: it reads files and touches no service, so it reports the cheapest failure before
  # anything slower has a chance to fail for its own reasons.
  assert_panel_port_has_one_authority
  assert_the_service_account_name_has_one_authority
  assert_generated_keys_are_documented
  # Before the firewall assertions: they write /etc/maran/panel.env at its real path and refuse
  # to run when it is already there, and so does this one. It cleans up after itself.
  assert_the_finish_message_tells_the_truth_about_this_install
  assert_whitelist_seed_takes_only_addresses
  assert_whitelist_seed_walks_this_login_session
  assert_firewall_renders_through_the_agent
  assert_firewall_seeding_composes
  assert_firewall_marker_records_only_our_own_enabling
  assert_firewall_include_wiring
  assert_firewalld_handling_tells_its_three_answers_apart
  assert_firewall_keeps_firewalld_until_its_own_table_is_loaded
  assert_uninstaller_never_leaves_a_dangling_include

  assert_mysql_gate_accepts_socket_auth
  assert_mysql_gate_refuses_passwordless_root
  assert_mysql_gate_refuses_password_root
  restore_socket_auth
  assert_mysql_gate_accepts_socket_auth

  assert_sftp_prerequisites
  assert_sftp_validates_before_replacing
  # After the SFTP assertions: they leave sshd_config in its final, valid state, and `sshd -T`
  # answers for the configuration that will actually be in the image.
  assert_ssh_port_detection_follows_includes

  # Last, and the comment on the function says why: it is the only assertion here that writes into
  # /etc/nginx and creates a system account.
  assert_the_vhost_swap_reports_a_failed_rename
  assert_nginx_vhost_is_validated_before_it_is_served
  # After the vhost assertion, not before it: generate_self_signed_cert does
  # `install -d -o root -g` the service group on the TLS directory, and that group is created by that
  # assertion's prepare step. Run first, this one restores no certificate — measured, as a red
  # build on its own restoration check rather than as a green assertion that had tested nothing.
  assert_the_panel_certificate_is_never_written_through_a_symlink
  # After both of those, and it is an ordering rather than a preference: this one installs the
  # vhost through step 80 itself and then STARTS nginx on it, so it must not be holding the port
  # or the served path while the two assertions above drive that same step. It puts the served
  # path, the document root and both log files back, and stops the server it started.
  assert_the_panel_vhost_can_never_log_a_query_string
  # After it, because it needs the `panel` group that assertion's prepare step creates, and because
  # it is the other assertion that installs files outside /tmp.
  assert_the_panel_socket_directory_is_built_and_then_looked_at

  # Last of all, and deliberately: these RUN step 40's create_directory_layout, which
  # re-modes /run/maran and re-owns /var/lib/maran to the values a real install leaves.
  # Every assertion above has finished with those directories by the time they do, and the
  # Dockerfile's own call to the same function follows this script.
  assert_the_directory_layout_is_what_it_claims
  # After the layout, because both need the service account step 40 creates, and because the
  # first of them plants a foreign comment on it and puts it back.
  assert_a_foreign_account_at_the_service_name_is_refused
  assert_the_legacy_service_account_is_migrated_in_place
  assert_the_scratch_gate_refuses_a_directory_root_does_not_own
  assert_the_ancestor_walk_refuses_a_panel_owned_ancestor
  assert_the_log_directory_gate_sees_the_defect_it_was_written_for
  # After the log-leaf control and not before it: that control chowns /var/log/maran itself and
  # restores it, and this one plants and restores the CHILD. Run in the other order they would
  # still not overlap, but the two plants would be in flight at once for no reason.
  assert_the_site_log_root_check_can_see_each_property_break
  assert_the_release_staging_directory_is_root_only
  assert_the_staging_gate_refuses_what_install_left_wrong
  assert_the_agent_unit_declares_its_writable_roots
  assert_preflight_warns_about_backup_space_without_refusing

  # Step 89, and the ORDER inside this group is the step's own order, because the step's whole
  # security property is an ordering: the mask before the package. The first assertion is the
  # only one that may run while /usr/sbin/vsftpd is absent — it refuses a host where something
  # else installed the package — so nothing may be moved in front of it.
  assert_the_packaged_daemon_was_never_startable
  assert_the_vsftpd_package_is_refused_while_its_unit_is_startable
  assert_ftps_group_is_created
  assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root
  assert_the_ftps_artifacts_are_what_they_claim
  assert_the_jail_base_is_traversable_by_an_unprivileged_uid
  assert_ftps_pam_stack_requires_group_membership
  # Straight after the content check, and it is the reason that one no longer claims "requires":
  # this is the assertion that asks the library.
  assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for
  # Neither PAM check can see it, and it costs every FTPS login when it breaks: the two halves of
  # this feature agreeing on the three names they both spell.
  assert_the_installer_and_the_agent_spell_the_ftps_names_the_same
  assert_a_planted_symlink_at_the_jail_base_is_replaced_not_followed
  # After the artifacts assertion, which is what puts maran-ftps.service on disk: the report
  # below plants a .wants entry pointing at it and must refuse.
  assert_the_ftps_step_says_it_is_off_and_can_see_when_it_is_not

  # Last of everything: it runs the uninstaller's real deleters at their real paths, which takes
  # /var/lib/maran, the scratch and the SFTP jail base with them. It puts all three back through
  # the installer's own steps and checks they came back, but nothing above it should have to
  # depend on that restoration having worked.
  # Before it, and textual where that one is behavioural: the census over what the installer drops
  # into the distribution's own configuration directories and what the uninstaller takes back out.
  assert_the_uninstaller_removes_every_drop_in_the_installer_creates
  # The process-signalling census: a text comparison plus one install and one probe, so it sits
  # beside the other census rather than among the assertions that start daemons.
  assert_every_supported_family_installs_the_process_signalling_tool
  assert_the_postgresql_conf_resolver_finds_a_real_debian_layout
  assert_the_uninstaller_keeps_the_backup_root
  echo "Installer steps 40, 50, 60, 70, 80, 85, 86, 87 and 89, the panel port's single authority, and"
  echo "the on-disk boundary the two privilege-escalation fixes moved, verified inside the polygon."
}

# Both Dockerfiles run this script with NO arguments, which is the only mode that counts: every
# assertion, in the order main() fixes. Named arguments run only those functions, and that is a
# DEBUGGING affordance, never a gate — a subset scored as if it were the suite is the shape
# rules/testing.md names as the one that manufactures confidence. A run with arguments says so on
# stdout so its output can never be mistaken for a build's.
if [ "$#" -gt 0 ]; then
  echo "assert-installer-steps.sh: SUBSET RUN of $* — this is not the polygon build gate."
  for requested in "$@"; do
    "$requested"
  done
else
  main
fi
