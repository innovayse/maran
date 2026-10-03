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
# The shared helpers the step files call but no step defines. Sourced rather than read: a step that
# calls `host_display_name` and cannot find it exits 127 with no assertion of this script ever
# running, which is how it was found (installer/lib/00-common.sh carries the full account).
readonly COMMON_LIB="${INSTALLER_LIB}/00-common.sh"
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

# Where docker/polygon/stand-ins/systemctl-stand-in.sh keeps the state of the units it has been asked
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
require_installer_file "$COMMON_LIB" installer/lib/00-common.sh
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
require_installer_file "$PAM_WITNESS_SOURCE" docker/polygon/asserts/pam-witness.c
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
# docker/polygon/stand-ins/systemctl-stand-in.sh records.
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
  fail "${binary:-systemctl} is not docker/polygon/stand-ins/systemctl-stand-in.sh. The firewalld assertions below put
this container into host states no verb can reach — a unit that exists, a query that fails to answer,
a disable that is refused — and only the stand-in records them. The Dockerfile must carry it BEFORE
the RUN that executes this script:

    COPY docker/polygon/stand-ins/systemctl-stand-in.sh /usr/bin/systemctl
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
# The shared helpers first, so a step sourced below finds what it calls. Safe to source anywhere:
# 00-common.sh defines no `fail` and holds no readonly state, which is exactly why it is a file of
# its own rather than part of 10-preflight.sh (which this script deliberately does not source).
# shellcheck source=/dev/null
. "$COMMON_LIB"
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
. "$2"
eval "$3"' _ "$COMMON_LIB" "$USER_STEP" "$1"
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
. "$2"
eval "$3"' _ "$COMMON_LIB" "$IDENTITY_STEP" "$1"
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
  # The shared helpers are sourced INSIDE the child, not merely in this script. A child sees nothing
  # the parent sourced, so a step that calls `host_display_name` would exit 127 here with no
  # assertion having run — which is exactly how this was found (installer/lib/00-common.sh).
  # The dependency adapter is sourced too, because the steps run here INSTALL things now:
  # `install_sftp_prerequisites` brings its own openssh-server rather than assuming one, and that
  # call reaches `pkg_install`, which lives in 20-dependencies.sh. Without it the step dies with
  # `pkg_install: command not found` before any assertion of this script runs.
  bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
. "$4"
. "$5"
. "$6"
eval "$7"' _ "$COMMON_LIB" "$DEPENDENCIES_STEP" "${INSTALLER_LIB}/85-mysql.sh" \
    "${INSTALLER_LIB}/86-sftp.sh" "$CONFIG_STEP" "$FIREWALL_STEP" "$snippet"
}
# The assertions themselves, one file per subject, sourced in the order they were written in.
# They define functions and constants only; main() below decides what runs and when.
readonly ASSERT_PARTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/parts"
for __part in \
  "${ASSERT_PARTS_DIR}/config-and-finish.sh" \
  "${ASSERT_PARTS_DIR}/firewall.sh" \
  "${ASSERT_PARTS_DIR}/sshd-and-whitelist.sh" \
  "${ASSERT_PARTS_DIR}/mysql.sh" \
  "${ASSERT_PARTS_DIR}/sftp.sh" \
  "${ASSERT_PARTS_DIR}/nginx.sh" \
  "${ASSERT_PARTS_DIR}/service-account.sh" \
  "${ASSERT_PARTS_DIR}/directory-layout.sh" \
  "${ASSERT_PARTS_DIR}/units-and-preflight.sh" \
  "${ASSERT_PARTS_DIR}/uninstaller.sh" \
  "${ASSERT_PARTS_DIR}/ftps.sh" \
  "${ASSERT_PARTS_DIR}/artifacts-and-packages.sh"; do
  [ -r "$__part" ] || fail "assert-installer-steps.sh: ${__part} is not in this image; the Dockerfile must COPY docker/polygon/asserts/parts/"
  # shellcheck source=/dev/null
  . "$__part"
done
unset __part


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
  assert_the_manifest_reader_reads_every_field_not_only_the_last
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
