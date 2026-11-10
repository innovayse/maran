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
set -Eeuo pipefail

# A `set -e` death is otherwise ANONYMOUS: bash prints nothing, the Dockerfile RUN just ends with a
# non-zero status, and the reader is left bisecting a build that takes minutes per attempt. This
# trap names the file, line, function chain and command of every failure that `set -e` is about to
# turn into an exit. ERR is not raised for a command in an `if`/`while` condition, a `!` pipeline,
# or any non-final member of an `&&`/`||` list, which is exactly the set `set -e` also ignores, so it
# is silent for the refusals an assertion deliberately probes. `-E` carries it into functions and
# command substitutions. `fail` exits through `exit 1`, which raises no ERR, so its own message
# stays the only line a deliberate failure prints.
trap 'echo "assert-installer-steps.sh: died at ${BASH_SOURCE[0]}:${LINENO} in ${FUNCNAME[*]:-main}: ${BASH_COMMAND}" >&2' ERR

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
# The tmpfiles snippet that builds the PHP pool socket directory. Applied by the step 40 directory
# layout check so /run/maran-php comes from the installer's own artefact (issue #101).
readonly PHP_TMPFILES="${INSTALLER_ROOT}/systemd/maran-php.tmpfiles.conf"
# The tmpfiles snippet that builds /run/maran-host, the root-only mount points the agent unit binds
# the host's /tmp, /var/tmp and /dev/shm onto (issue #139). Applied by the directory layout check.
readonly HOST_TMPFILES="${INSTALLER_ROOT}/systemd/maran-host.tmpfiles.conf"
readonly AGENT_UNIT="${INSTALLER_ROOT}/systemd/maran-agent.service"
# The package-install template step 70 renders and installs beside the two units above (issue #93).
# RENDERED and INSTALLED by the step's own functions, never read for an expectation: see
# parts/php-install-unit.sh. Step 70's install_units reads it, so a missing COPY stops the build
# at the step and not only at the assertion.
readonly PHP_INSTALL_UNIT_SOURCE="${INSTALLER_ROOT}/systemd/maran-php-install@.service"
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
# The same kind of literal, sent as the Referer of the SECOND probe request (issue #465): the
# address of a page whose query string holds a reset or invitation token, which a browser names on
# every request that page makes. A different string from the one above so that the two leaks are
# told apart in the failure message.
readonly VHOST_LOG_PROBE_REFERER_SECRET="polygon-throwaway-referer-secret-5d27be"
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
# Step 89's DNS half. SOURCED, and its mask and ordering functions are RUN by parts/dns.sh against
# a host with no BIND package; the names it spells are COMPARED with the agent's source text.
readonly DNS_STEP="${INSTALLER_LIB}/89-dns.sh"
# Step 66, the break-glass `maran` command, and the sources it installs. The step is RUN (its own
# step_cli, in a child) and the sources are READ: parts/cli-recovery.sh looks at what the step left
# on the disk and at the wrapper's text. SOURCE paths are the installer package's layout, which the
# Dockerfile reproduces under INSTALLER_ROOT; the INSTALLED paths are literals in the part, because
# an assertion that reads its expectation out of the file it checks agrees with it by construction.
readonly CLI_STEP="${INSTALLER_LIB}/66-cli.sh"
readonly CLI_WRAPPER_SOURCE="${INSTALLER_ROOT}/cli/maran"
readonly CLI_LIB_SOURCE_DIR="${INSTALLER_ROOT}/cli/lib"
# Step 67, the scheduled panel backup's installer step, and the three files it installs or reads
# from the package: the service, the timer and the documented example configuration. The step is
# RUN (its own step_panel_backup, in a child) and the units are READ for their effective lines; see
# parts/panel-backup-units.sh. Resolved by the step against $SCRIPT_DIR and $LIB_DIR, so a missing
# COPY stops the step by name and not only this script.
readonly PANEL_BACKUP_STEP="${INSTALLER_LIB}/67-panel-backup.sh"
readonly PANEL_BACKUP_SERVICE_SOURCE="${INSTALLER_ROOT}/systemd/maran-panel-backup.service"
readonly PANEL_BACKUP_TIMER_SOURCE="${INSTALLER_ROOT}/systemd/maran-panel-backup.timer"
readonly PANEL_BACKUP_EXAMPLE_SOURCE="${INSTALLER_ROOT}/panel-backup.conf.example"
# Step 68, the opt-in unattended OS security updates (issue #163). RUN, not read: parts/os-security-updates-step.sh
# drives its own step_os_updates twice over, flag off and flag on, and looks at what it left. The step
# installs a package through step 20's pkg_install (DEPENDENCIES_STEP, below) and writes one file under
# /etc, so a missing COPY stops the build at the assertion by name.
readonly OS_UPDATES_STEP="${INSTALLER_LIB}/68-os-updates.sh"
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
# The SUSE adapter splits what the other two keep in one services file: the DNS names live in
# suse_dns_services.rs and the account-program paths (pkill among them) in suse_account_programs.rs.
# Each is the file a comparison below reads for that subject.
readonly AGENT_SUSE_SERVICES="${AGENT_SOURCE_ROOT}/crates/distro/src/suse/suse_services.rs"
readonly AGENT_SUSE_DNS_SERVICES="${AGENT_SOURCE_ROOT}/crates/distro/src/suse/suse_dns_services.rs"
readonly AGENT_SUSE_ACCOUNT_PROGRAMS="${AGENT_SOURCE_ROOT}/crates/distro/src/suse/suse_account_programs.rs"
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
require_installer_file "$PHP_TMPFILES" installer/systemd/maran-php.tmpfiles.conf
require_installer_file "$HOST_TMPFILES" installer/systemd/maran-host.tmpfiles.conf
require_installer_file "$AGENT_UNIT" installer/systemd/maran-agent.service
require_installer_file "$PHP_INSTALL_UNIT_SOURCE" installer/systemd/maran-php-install@.service
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
require_installer_file "$DNS_STEP" installer/lib/89-dns.sh
require_installer_file "${INSTALLER_ROOT}/systemd/maran-dns.service" installer/systemd/maran-dns.service
require_installer_file "$CLI_STEP" installer/lib/66-cli.sh
require_installer_file "$CLI_WRAPPER_SOURCE" installer/cli/maran
for __cli_lib in common.sh status.sh reset.sh panel-certificate.sh diagnostics.sh panel-archive.sh panel-backup-config.sh \
  panel-backup-retention.sh panel-backup-upload.sh panel-backup.sh panel-backup-schedule.sh panel-restore.sh \
  release-version.sh release-verify.sh release-unpack.sh release-layout.sh panel-dump.sh update-dumps.sh \
  update-health.sh update-unattended.sh update.sh update-schedule.sh rollback.sh; do
  require_installer_file "${CLI_LIB_SOURCE_DIR}/${__cli_lib}" "installer/cli/lib/${__cli_lib}"
done
unset __cli_lib
# The release-signing public key step 66 installs for the CLI and step 50 verifies the first
# install against: a package without it makes both steps fail by name, so a missing COPY must be a
# named failure here and not only inside them.
require_installer_file "${INSTALLER_ROOT}/keys/release-signing.pub" installer/keys/release-signing.pub
require_installer_file "$PANEL_BACKUP_STEP" installer/lib/67-panel-backup.sh
require_installer_file "$PANEL_BACKUP_SERVICE_SOURCE" installer/systemd/maran-panel-backup.service
require_installer_file "$OS_UPDATES_STEP" installer/lib/68-os-updates.sh
require_installer_file "$PANEL_BACKUP_TIMER_SOURCE" installer/systemd/maran-panel-backup.timer
require_installer_file "$PANEL_BACKUP_EXAMPLE_SOURCE" installer/panel-backup.conf.example
require_installer_file "$DEPENDENCIES_STEP" installer/lib/20-dependencies.sh
require_installer_file "$FTPS_UNIT_SOURCE" installer/systemd/maran-ftps.service
require_installer_file "$FTPS_LOGROTATE_SOURCE" installer/logrotate/maran-ftps
require_installer_file "$PAM_WITNESS_SOURCE" docker/polygon/asserts/pam-witness.c
require_installer_file "$AGENT_DEBIAN_SERVICES" agent/crates/distro/src/debian/debian_services.rs
require_installer_file "$AGENT_RHEL_SERVICES" agent/crates/distro/src/rhel/rhel_services.rs
require_installer_file "$AGENT_SUSE_SERVICES" agent/crates/distro/src/suse/suse_services.rs
require_installer_file "$AGENT_SUSE_DNS_SERVICES" agent/crates/distro/src/suse/suse_dns_services.rs
require_installer_file "$AGENT_SUSE_ACCOUNT_PROGRAMS" agent/crates/distro/src/suse/suse_account_programs.rs
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
# shellcheck source=/dev/null
. "$DNS_STEP"

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
  "${ASSERT_PARTS_DIR}/php-install-unit.sh" \
  "${ASSERT_PARTS_DIR}/panel-backup-units.sh" \
  "${ASSERT_PARTS_DIR}/update-request-units.sh" \
  "${ASSERT_PARTS_DIR}/update-security-units.sh" \
  "${ASSERT_PARTS_DIR}/os-security-updates.sh" \
  "${ASSERT_PARTS_DIR}/os-security-updates-step.sh" \
  "${ASSERT_PARTS_DIR}/uninstaller.sh" \
  "${ASSERT_PARTS_DIR}/ftps.sh" \
  "${ASSERT_PARTS_DIR}/dns.sh" \
  "${ASSERT_PARTS_DIR}/mail-uninstall.sh" \
  "${ASSERT_PARTS_DIR}/artifacts-and-packages.sh" \
  "${ASSERT_PARTS_DIR}/cli-recovery.sh" \
  "${ASSERT_PARTS_DIR}/cli-update.sh" \
  "${ASSERT_PARTS_DIR}/release-layout.sh" \
  "${ASSERT_PARTS_DIR}/log-retention.sh" \
  "${ASSERT_PARTS_DIR}/nginx-log-rotation.sh" \
  "${ASSERT_PARTS_DIR}/suse-family.sh"; do
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
  assert_the_panel_host_name_is_recorded_from_the_name_the_vhost_serves
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
  # The SUSE family's two firewall obligations (decision 220: the agent's nftables is the one owner).
  # After the assertion above, which leaves /etc/maran/panel.env absent as these refuse to find it.
  if [ "${MARAN_OS_FAMILY:-}" = suse ]; then
    assert_suse_retires_firewalld_only_when_ssh_is_in_the_loaded_ruleset
    assert_suse_masks_firewalld_when_the_unit_exists_and_says_what_it_did
  fi
  assert_uninstaller_never_leaves_a_dangling_include

  assert_mysql_gate_accepts_socket_auth
  assert_mysql_gate_refuses_passwordless_root
  assert_mysql_gate_refuses_password_root
  restore_socket_auth
  assert_mysql_gate_accepts_socket_auth

  assert_sftp_prerequisites
  assert_sftp_validates_before_replacing
  assert_sshd_confines_every_jailed_login
  # After the SFTP assertions: they leave sshd_config in its final, valid state, and `sshd -T`
  # answers for the configuration that will actually be in the image.
  assert_ssh_port_detection_follows_includes

  # Last, and the comment on the function says why: it is the only assertion here that writes into
  # /etc/nginx and creates a system account.
  assert_the_vhost_swap_reports_a_failed_rename
  # The SUSE family's vhost directory must be one nginx reads, asked of nginx itself, before the
  # assertion that drives step 80 end to end writes into it.
  if [ "${MARAN_OS_FAMILY:-}" = suse ]; then
    assert_suse_nginx_reads_the_directory_the_vhost_is_written_to
  fi
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
  # After it, for the same reason: it installs the vhost through step 80 and starts nginx on it, and puts
  # the served path and both log files back. The frame location of a packaged module's screen (issue #185).
  assert_the_module_frame_location_has_its_own_headers_and_api_keeps_deny
  # The file manager's upload and download location renders its body limit from panel.env (issue #169).
  assert_the_file_transfer_location_carries_the_configured_body_limit
  assert_the_account_move_locations_carry_the_package_limits
  # After it, because it needs the `panel` group that assertion's prepare step creates, and because
  # it is the other assertion that installs files outside /tmp.
  assert_the_panel_socket_directory_is_built_and_then_looked_at
  # After it, because both install and remove the same unit files through step 70's install_units,
  # and the socket assertion above leaves the unit directory clean for them.
  assert_the_php_install_unit_is_the_one_fixed_oneshot_the_design_allows
  assert_step_70_installs_the_php_install_unit_root_owned_and_never_enabled
  # The scheduled panel backup's units (issue #129), after the two units above for the same reason
  # they follow each other: every one of them installs into /etc/systemd/system and removes only its
  # own files, so none is holding a file another is about to look for. These three put the image
  # back as they found it, which includes /var/backups/maran-panel and the staging directory.
  assert_the_panel_backup_service_and_timer_are_what_the_design_allows
  assert_step_67_installs_the_panel_backup_units_with_exact_modes_and_enables_nothing
  assert_the_panel_backup_service_cannot_be_started_by_a_non_root_user
  # The units through which the panel's "Update now" starts a root update (issue #382): text checkers shown
  # to refuse before they are trusted. They read the package's files and install nothing, so they take no
  # part in the ordering above.
  assert_the_update_request_units_are_what_the_design_allows
  # The unattended security-patch update's units and step 69 (issue #383). After the panel backup assertions for
  # the same reason they follow each other: each installs into /etc/systemd/system and removes only its own
  # files. The second one runs step 66 (the break-glass command) to drive the real `maran update-schedule
  # disable`, and removes it again.
  assert_the_update_security_service_and_timer_are_what_the_design_allows
  assert_step_69_installs_the_security_update_units_and_keeps_the_operators_choice
  # Step 68, the opt-in unattended OS security updates (issue #163): the refusing half (the flag off writes
  # nothing) and then its inverse control (the flag on writes the exact file and enables the distribution's
  # timers). After the panel backup assertions, which leave /etc/systemd/system as they found it, because the
  # first of these takes a listing of that directory and asserts the step added no name to it. It installs the
  # distribution's unattended-update package, so it needs the package index this script's RUN refreshed.
  assert_the_os_security_updates_step_changes_nothing_without_the_flag
  assert_the_os_security_updates_step_writes_the_exact_configuration_when_asked
  assert_the_os_updates_step_refuses_a_codename_or_a_hold_pattern_that_could_inject_a_line

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
  assert_a_reinstall_keeps_a_killed_restores_rollback_dumps
  assert_the_ancestor_walk_refuses_a_panel_owned_ancestor
  assert_the_log_directory_gate_sees_the_defect_it_was_written_for
  # After the log-leaf control and not before it: that control chowns /var/log/maran itself and
  # restores it, and this one plants and restores the CHILD. Run in the other order they would
  # still not overlap, but the two plants would be in flight at once for no reason.
  assert_the_site_log_root_check_can_see_each_property_break
  assert_the_release_staging_directory_is_root_only
  assert_the_staging_gate_refuses_what_install_left_wrong
  # After both staging assertions, which leave step 50's staging directory as they found it, and
  # before the agent unit ones: this RUNS step 50 and step 66 on a synthetic signed bundle and then
  # puts /usr/local/maran, release.state and the key back. It needs the service account step 40
  # creates above (issue #128).
  assert_a_fresh_install_leaves_the_release_layout_the_design_requires
  assert_the_agent_unit_declares_its_writable_roots
  assert_the_agent_unit_never_makes_usr_writable
  assert_the_agent_unit_keeps_its_measured_hardening
  assert_the_agent_unit_never_hides_other_users_processes
  assert_the_agent_unit_renders_through_the_installer
  assert_preflight_warns_about_backup_space_without_refusing

  # Step 89, and the ORDER inside this group is the step's own order, because the step's whole
  # security property is an ordering: the mask before the package. The first assertion is the
  # only one that may run while /usr/sbin/vsftpd is absent — it refuses a host where something
  # else installed the package — so nothing may be moved in front of it.
  #
  # The FTPS assertions below need a vsftpd package. The SUSE family has none (decision 222), so there
  # the whole block is replaced by the one assertion that FTPS is skipped in plain words and leaves
  # nothing behind, and by the text comparison of the names the installer and the agent both spell,
  # which reads source and needs no package. `ftps_is_offered_on_family` is the installer's own gate,
  # so this image cannot drift from the step on which families have the daemon.
  if ftps_is_offered_on_family; then
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
  else
    assert_ftps_is_skipped_in_plain_words_and_touches_nothing
    assert_the_installer_and_the_agent_spell_the_ftps_names_the_same
    echo "UNOBSERVED HERE: the FTPS packaged-daemon, jail, PAM stack, PAM witness, symlink and off-report assertions did not run on this family, because it offers no FTPS (decision 222). Only the skip and the agreement of the names were checked."
  fi

  # Step 89's DNS half: the ordering that keeps the distribution's resolver from ever running, the
  # masks on every unit name the package can start it under, and the names the installer and the
  # agent both spell. After the FTPS group, which is what leaves the image in the state these expect.
  assert_the_dns_package_is_refused_while_a_packaged_unit_is_startable
  assert_the_dns_mask_covers_every_packaged_unit
  assert_the_installer_and_the_agent_spell_the_dns_names_the_same
  assert_the_dns_writable_directories_are_made_with_the_owner_and_mode_the_daemon_needs

  # The mail uninstall half of step 89 (issue #177): remove_mail stops and disables only the key-value
  # unit 89-mail.sh recorded, and leaves an operator's own redis or valkey unit running. It runs the
  # uninstaller's remove_mail against the systemctl stand-in and removes only /etc/maran/mail, which
  # it refuses to start without, so it does not depend on the order of anything above it.
  assert_remove_mail_stops_only_the_recorded_key_value_unit
  # The SUSE family does not offer mail until it is measured: the step must skip in plain words and
  # leave nothing behind. After the uninstall half above, which plants and removes its own records.
  if [ "${MARAN_OS_FAMILY:-}" = suse ]; then
    assert_mail_is_skipped_in_plain_words_and_touches_nothing
  fi

  # Last of everything: it runs the uninstaller's real deleters at their real paths, which takes
  # /var/lib/maran, the scratch and the SFTP jail base with them. It puts all three back through
  # the installer's own steps and checks they came back, but nothing above it should have to
  # depend on that restoration having worked.
  # Before it, and textual where that one is behavioural: the census over what the installer drops
  # into the distribution's own configuration directories and what the uninstaller takes back out.
  # Step 41's rotation policy, installed and parsed by the real logrotate (issue #131). Before the
  # census, which reads the same step as text.
  assert_the_install_log_rotation_policy_is_installed_and_parsed
  # Rotation reloads nginx and never signals USR1 (issue #490); text plus the real logrotate.
  assert_nginx_log_rotation_reloads_and_never_signals_usr1
  assert_the_uninstaller_removes_every_drop_in_the_installer_creates
  # The process-signalling census: a text comparison plus one install and one probe, so it sits
  # beside the other census rather than among the assertions that start daemons.
  assert_every_supported_family_installs_the_process_signalling_tool
  assert_the_postgresql_conf_resolver_finds_a_real_debian_layout
  assert_the_manifest_reader_reads_every_field_not_only_the_last
  assert_the_uninstaller_keeps_the_backup_root
  # The break-glass command, after the backup-root assertion and last of the installer-text ones:
  # both of these install into /usr/local, and the second RUNS the uninstaller's remove_binaries,
  # which takes /usr/local/maran with it (the layout is put back inside the assertion).
  assert_the_break_glass_command_installs_root_only_and_refuses_what_it_should
  # The scheduled-backup verbs refuse before they read, parse a hostile configuration as data and
  # run only as root (issue #129). Before the uninstaller ones: it installs the wrapper and step 67's
  # units, and removes them again, and the two below install and remove them for their own reasons.
  assert_the_scheduled_panel_backup_verbs_refuse_what_they_should
  # The restore refuses an archive taken by a newer release than the installed one (issue #129).
  assert_the_panel_restore_refuses_an_archive_from_a_newer_release
  # The update and rollback verbs (issue #128) refuse before they read, hostile values reach no
  # command, and a refused run changes nothing. Right after the scheduled-backup verbs, which it
  # reuses the helpers of and which leave the image as they found it.
  assert_the_update_and_rollback_verbs_refuse_what_they_should
  assert_the_uninstaller_removes_the_break_glass_command
  assert_the_uninstaller_removes_the_release_layout_and_the_update_state
  assert_the_uninstaller_removes_the_panel_backup_schedule_and_keeps_the_archives
  if [ "${MARAN_OS_FAMILY:-}" = suse ]; then
    echo "UNOBSERVED HERE: SELinux. This image loads no policy and the container shares the host kernel, so whether the daemons run confined, or are denied a path the installer made, is unseen; the install polygon reads no audit log either."
  fi
  echo "Installer steps 40, 41, 50, 60, 66, 67, 68, 70, 80, 85, 86, 87 and 89, the panel port's single authority, and"
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
