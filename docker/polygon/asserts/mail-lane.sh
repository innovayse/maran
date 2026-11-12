#!/usr/bin/env bash
# The mail lane: the assertions that need a RUNNING mail stack, run where one exists.
#
# Why it is a lane of its own. docker/polygon/asserts/parts/mail-local-submit.sh and
# mail-port25-lockdown.sh drive the real Postfix, Rspamd and nftables, and the suite images' build-time
# run of assert-installer-steps.sh boots no mail daemon, so there they print "UNOBSERVED HERE (GAP)" and
# check nothing. This script is the other half: it runs INSIDE an ordinary suite image
# (maran-polygon-ubuntu24 or maran-polygon-alma9) at run time, installs the stack through the
# installer's OWN mail step, has the agent enable and start it, and runs exactly those two assertions with
# MARAN_MAIL_LOCAL_SUBMIT_REQUIRED=yes and MARAN_MAIL_PORT25_REQUIRED=yes, so the gap is a failure here.
# Every other lane keeps printing the gap; no suite image changed.
#
# Run it with the repository mounted, privileged (nftables, su, a real mail queue):
#   docker run --rm --init --privileged -v "$PWD:/maran" maran-polygon-ubuntu24 \
#     bash /maran/docker/polygon/asserts/mail-lane.sh
#
# What it takes from the mounted tree is staged to the paths assert-installer-steps.sh reads, because
# the build removes /tmp/maran-agent and /tmp/maran-polygon after the build-time run and the installer
# copies in the image are those of the build. Staging from the mounted tree makes the lane judge THIS
# tree's installer and assertions and not the image's older copy.
#
# What renders the mail configuration. This script writes none of it: Postfix's main.cf and master.cf
# (cleanup_local, self_clients.cidr), Rspamd's local-submission rule and maps, and the nftables table
# `inet maran` are rendered by the agent's own operations, driven by the ignored real-host suite
# agent/crates/agent/tests/mail_server_on_a_real_host.rs, which this script runs FIRST and which leaves the
# stack running. The daemons are started by the polygon `systemctl` stand-in (its "Real mail daemons"
# section, switched on by that suite), because that is when the agent asks for them and enable_mail's wait
# for the ports needs them. The assertions then judge exactly what the agent rendered. If the suite fails,
# or the rendering is missing afterwards, the lane fails by name: a lane that judged the distribution's
# defaults would pass or fail for a reason it cannot state.
set -Eeuo pipefail

repo="${MARAN_REPO_MOUNT:-/maran}"
installer_root="/tmp/maran-installer"

lane_fail() {
  echo "mail-lane.sh: $*" >&2
  echo "MAIL LANE VERDICT: FAILED"
  exit 1
}

[ "$(id -u)" = 0 ] || lane_fail "must run as root."
[ -r "$repo/scripts/lib/polygon.sh" ] || lane_fail "$repo is not the repository (scripts/lib/polygon.sh missing)."
# shellcheck source=/dev/null
. "$repo/scripts/lib/polygon.sh"
[ -r "$repo/installer/lib/89-mail.sh" ] || lane_fail "$repo is not the repository (installer/lib/89-mail.sh missing); mount it with -v \"\$PWD:/maran\"."
grep -q 'systemctl-stand-in' "$(command -v systemctl)" \
  || lane_fail "systemctl is not the polygon stand-in; this lane runs in a maran-polygon suite image."

# Stage the tree the assertion script reads. The installer tree is mirrored whole; the agent's sources
# and the PAM witness are the few files the script's own requirement list names.
rm -rf "$installer_root" /tmp/maran-agent /tmp/maran-polygon
mkdir -p "$installer_root" /tmp/maran-agent /tmp/maran-polygon
cp -a "$repo/installer/." "$installer_root/"
cp "$repo/docker/polygon/asserts/assert-installer-steps.sh" "$installer_root/assert-installer-steps.sh"
cp -a "$repo/docker/polygon/asserts/parts" "$installer_root/parts"
cp "$repo/docker/polygon/asserts/pam-witness.c" /tmp/maran-polygon/pam-witness.c
for source in \
  crates/distro/src/debian/debian_services.rs \
  crates/distro/src/rhel/rhel_services.rs \
  crates/distro/src/suse/suse_services.rs \
  crates/distro/src/suse/suse_dns_services.rs \
  crates/distro/src/suse/suse_account_programs.rs \
  crates/agent-core/src/agent_paths.rs \
  crates/templates/templates/vsftpd/vsftpd.conf.j2 \
  crates/ops/src/ftps/enable_ftps.rs; do
  install -D "$repo/agent/$source" "/tmp/maran-agent/$source"
done

# The family and release the installer's steps key on, read the way the preflight reads them.
# shellcheck source=/dev/null
. /etc/os-release
export MARAN_OS_ID="$ID" MARAN_OS_VERSION_ID="$VERSION_ID"
case "$ID" in
  ubuntu|debian) export MARAN_OS_FAMILY=debian DEBIAN_FRONTEND=noninteractive ;;
  almalinux|rocky|ol|rhel) export MARAN_OS_FAMILY=rhel ;;
  *) lane_fail "OS '$ID' has no mail lane (the SUSE family is not offered mail)." ;;
esac

# The stack, installed by the installer's own step so that a package list that stops being right on this
# family stops this lane. The step masks every packaged unit first and leaves them stopped and masked.
# shellcheck source=/dev/null
. "$installer_root/lib/00-common.sh"
# shellcheck source=/dev/null
. "$installer_root/lib/20-dependencies.sh"
# shellcheck source=/dev/null
. "$installer_root/lib/89-mail.sh"
rm -rf "$MARAN_MAIL_CONFIG_DIR"
[ "$MARAN_OS_FAMILY" != debian ] || apt-get update >/dev/null
# A fresh container reads every unit it has no record of as active and enabled, the way a real host's
# installed units are, and the step refuses a host whose mail daemons already run (they would be an
# operator's). This lane stands for the host with the daemons installed and idle, so it records them stopped
# and disabled first.
# Rspamd is in no RHEL-family base repository and the installer adds no package source (rules/security.md
# item 10), so a host of that family has it only because the operator supplied the vendor's repository. This
# lane is that operator: it supplies it, as a test fixture, before the step looks for the package.
if [ "$MARAN_OS_FAMILY" = rhel ] && ! dnf -q list --available rspamd >/dev/null 2>&1; then
  major="${VERSION_ID%%.*}"
  curl -fsSL -o /etc/yum.repos.d/rspamd.repo "https://rspamd.com/rpm-stable/centos-${major}/rspamd.repo" \
    || lane_fail "the vendor repository file for Rspamd could not be fetched."
  # The vendor key carries a SHA-1 self-signature, which this family's default crypto policy refuses to
  # import, so the fixture installs from the repository without checking its packages' signatures. That is
  # confined to this throwaway container and to the repository file written here.
  sed -i -e 's/^gpgcheck=.*/gpgcheck=0/' -e 's/^repo_gpgcheck=.*/repo_gpgcheck=0/' /etc/yum.repos.d/rspamd.repo
  dnf -q list --available rspamd >/dev/null 2>&1 \
    || lane_fail "the vendor repository does not carry rspamd for ${ID} ${VERSION_ID}."
fi
for mail_unit in $(mail_units); do
  systemctl stop "${mail_unit}.service"
  systemctl disable "${mail_unit}.service"
done
step_mail

# The package's own post-install step sets postdrop's owner and mode (root, group postdrop, setgid), and
# the agent refuses to bind local submission on a host where it is not so. An image layer whose package was
# unpacked without that step has the file as the archive carried it, so the lane puts it as the package does.
postdrop_binary="$(command -v postdrop || true)"
[ -n "$postdrop_binary" ] || postdrop_binary=/usr/sbin/postdrop
chown root:postdrop "$postdrop_binary"
chmod 2755 "$postdrop_binary"

# Redis-protocol client, nftables and a syslog daemon: the assertions read the hourly counter and the
# table with the first two, and Postfix logs through syslog into the mail log they read its verdicts from
# (/var/log/mail.log or /var/log/maillog), which an image without an init system has no daemon for.
case "$MARAN_OS_FAMILY" in
  debian) pkg_install redis-tools nftables rsyslog util-linux ;;
  rhel)   pkg_install nftables rsyslog ;;
esac
mail_log=/var/log/mail.log
[ "$MARAN_OS_FAMILY" = debian ] || mail_log=/var/log/maillog
touch "$mail_log"
# The mail facility goes to the file the assertions read, by a configuration of this lane's own: a
# distribution's default one differs (RHEL's reads the journal and loads no socket module, Ubuntu's drops to
# an unprivileged user that cannot write the root-owned file), and the logging socket is the one thing
# Postfix's daemons write to without a service manager. It runs as root, and the claim is checked below,
# not assumed.
lane_syslog_conf=/tmp/maran-lane-rsyslog.conf
printf '%s\n' 'module(load="imuxsock")' "mail.* -${mail_log}" > "$lane_syslog_conf"
rsyslogd -f "$lane_syslog_conf" -i /tmp/maran-lane-rsyslog.pid || lane_fail "rsyslogd did not start; Postfix would have nowhere to log."
for _ in $(seq 1 20); do [ -S /dev/log ] && break; sleep 0.5; done
[ -S /dev/log ] || lane_fail "rsyslogd left no /dev/log; Postfix would have nowhere to log."
logger -p mail.info "maran-lane-probe-$$"
for _ in $(seq 1 20); do grep -q "maran-lane-probe-$$" "$mail_log" && break; sleep 0.5; done
grep -q "maran-lane-probe-$$" "$mail_log" || lane_fail "a mail.info line sent to syslog did not reach ${mail_log}; the assertions read their verdicts there."

# The polygon's systemctl is the image's copy from build time; the mounted tree's copy is the one that
# knows how to drive the mail daemons, so it is the one used, like the installer and the assertions above.
systemctl_path="$(command -v systemctl)"
install -m 0755 "$repo/docker/polygon/stand-ins/systemctl-stand-in.sh" "$systemctl_path"

# The agent enables mail, binds accounts and loads the firewall table, against the real daemons. The suite
# is the setup; it must pass, and "no tests ran" is a failure and never a pass. Which suite this lane owns
# and how many tests it must pass come from POLYGON_LANE_OWNED_SUITES, the same declaration that makes
# `maran polygon verify` stop requiring the suite in the `polygon` job's logs.
mapfile -t lane_suites < <(polygon_lane_suites polygon-mail)
[ "${#lane_suites[@]}" -eq 1 ] || lane_fail "POLYGON_LANE_OWNED_SUITES declares ${#lane_suites[@]} suites for lane polygon-mail; this lane runs exactly one."
owned_suite="${lane_suites[0]}"
suite_log="$(mktemp)"
suite_status=0
(
  cd "$repo/agent"
  export CARGO_TARGET_DIR=/tmp/maran-agent-target
  cargo test --test "$owned_suite" -- --ignored --test-threads=1
) 2>&1 | tee "$suite_log" || suite_status=$?
[ "$suite_status" -eq 0 ] || lane_fail "the $owned_suite suite failed (status ${suite_status}); the stack the assertions need was not enabled through the agent."
lane_problems="$(polygon_lane_verdict "$owned_suite" "$suite_log")" \
  || lane_fail "the $owned_suite suite is not proved by this lane:$lane_problems"

# What the assertions stand on, refused by name when the suite did not leave it.
for rendered in /etc/postfix/main.cf /etc/postfix/master.cf; do
  [ -r "$rendered" ] || lane_fail "$rendered is missing."
done
grep -q 'cleanup_local' /etc/postfix/master.cf \
  || lane_fail "/etc/postfix/master.cf carries no cleanup_local service: the agent did not render the local-submission configuration."
nft list table inet maran >/dev/null 2>&1 \
  || lane_fail "nftables table 'inet maran' is not loaded: the agent did not render the firewall."
postfix status >/dev/null 2>&1 || lane_fail "postfix is not running after the suite."
rspamadm configtest >/dev/null 2>&1 || lane_fail "rspamd does not have a valid configuration after the suite."

# The key-value server's socket, from the installer's own table of which server this release runs.
read -r key_value_unit _ key_value_user <<<"$(mail_key_value_service)"
MARAN_LOCAL_SUBMIT_REDIS_SOCKET="/run/${key_value_user}/${key_value_unit}.sock"
[ -S "$MARAN_LOCAL_SUBMIT_REDIS_SOCKET" ] || lane_fail "the key-value socket $MARAN_LOCAL_SUBMIT_REDIS_SOCKET is not there after the suite."
export MARAN_LOCAL_SUBMIT_REDIS_SOCKET

export MARAN_MAIL_LOCAL_SUBMIT_REQUIRED=yes MARAN_MAIL_PORT25_REQUIRED=yes
# A named subset, announced as such by the script it runs: this lane is not the build gate, and its
# verdict line says which assertions it judged.
bash "$installer_root/assert-installer-steps.sh" \
  assert_local_submission_is_bound_to_the_account \
  assert_port_25_is_closed_to_customers

echo "MAIL LANE VERDICT: OK (agent-enabled mail stack, local submission binding and port 25 lockdown, gaps are failures)"
