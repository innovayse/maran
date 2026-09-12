#!/usr/bin/env bash
# Step 89: put FTPS on the host — installed, configurable, and NOT LISTENING.
#
# The promise this step makes, and the only one it makes: when it finishes, the
# vsftpd binary is on the machine, the group that authorizes an FTPS login exists,
# the PAM stack that decides who may authenticate exists, the chroot base exists,
# and Maran's own unit is installed, DISABLED and STOPPED. No socket has been
# bound at any point during the step, and none will be until an administrator
# turns FTPS on. `rules/security.md` §10 forbids a new listening port arriving by
# surprise; the spec sanctions the daemon, so what this step owes is that the
# daemon arrives switched off.
#
# WHY THE MASK COMES FIRST, which is the whole ordering of `step_ftps`:
# on the Debian family `apt-get install vsftpd` leaves
# /etc/systemd/system/multi-user.target.wants/vsftpd.service in place and the
# daemon running under the distribution's own configuration — `listen_ipv6=YES`,
# `local_enable=YES`, no TLS required — which is port 21 accepting local system
# logins with the password in the clear. Measured on 2026-09-08 on Ubuntu 24.04
# and Debian 13; the RHEL family does not enable it. An installer that installed
# the package and then disabled the unit would still have opened that port for the
# length of the install, on a machine that is by definition reachable from the
# internet, because the operator just reached it. `systemctl mask` replaces the unit
# with a symbolic link to /dev/null in /etc/systemd/system, which shadows the
# distribution's /lib/systemd/system/vsftpd.service: systemd refuses to start a masked
# unit, so the postinst's own start cannot bind anything. The mask stays: this panel
# never runs the packaged unit, it runs maran-ftps.service against its own
# configuration file.
#
# WHAT THE MASK DOES TO THE PACKAGE'S ENABLEMENT, and this paragraph is a correction:
# an earlier version of it stated that the postinst creates
# /etc/systemd/system/multi-user.target.wants/vsftpd.service "mask or no mask", on the
# strength of one measurement. Two later measurements disagreed with it, so the four
# cells of mask x image were run a third time on 2026-09-09 in throwaway
# `docker run --rm` containers, and the first reading was wrong:
#
#   ubuntu:24.04  (vsftpd 3.0.5-0ubuntu3.1)  mask first -> NO .wants entry, no dsh-also
#   ubuntu:24.04                             no mask    -> .wants entry AND dsh-also
#   debian:trixie (vsftpd 3.0.5-0.2)         mask first -> NO .wants entry, no dsh-also
#   debian:trixie                            no mask    -> .wants entry AND dsh-also
#
# The mechanism, re-driven by hand with the postinst's own two calls: `deb-systemd-helper
# enable` does not do the enabling itself where systemctl exists — it delegates, and
# systemd refuses a masked unit:
#   Failed to preset unit, unit /etc/systemd/system/vsftpd.service is masked.
#   deb-systemd-helper: error: systemctl preset failed on vsftpd.service
# The helper then exits before it writes either the link or its state file, and the
# postinst swallows that with `|| true`. Its `deb-systemd-helper unmask` does not remove
# our mask either: it unmasks only masks it recorded itself under
# /var/lib/systemd/deb-systemd-helper-masked, and returns early — "Not unmasking ...
# because the state file ... does not exist" — so the link is still /dev/null afterwards.
#
# CAVEAT, and it is the reason this text does not simply invert the old claim: the absent
# .wants entry rests on systemctl BEING PRESENT. The helper's other branch, taken where it
# is not, creates the links itself. So the absence of that symlink is a CONSEQUENCE on the
# hosts we support and never the control. "The daemon does not start" rests on the MASK, on
# every path and on both branches, and a check written the other way round would be
# asserting on an artifact whose presence depends on which branch the helper took.
#
# UNOBSERVED HERE, and stated rather than papered over: whether the postinst would have
# started the daemon. A container has no booted systemd and Docker's own policy-rc.d
# denies the start outright ("policy-rc.d denied execution of start"), so the start was
# refused for a reason that has nothing to do with this step. What was observed is the
# artifact — the mask link, and the package's own enablement — and the live fact belongs
# on a real host.
#
# The ordering is not left to a reader's care — `install_vsftpd_package` REFUSES to
# run until the mask is observably in place, so moving the mask after the install
# (or deleting it) fails the step by name instead of quietly reopening the window.
#
# What this step deliberately does NOT do:
#
# - It does not enable or start maran-ftps.service, and it opens no port. Turning
#   FTPS on is an explicit administrator action with its firewall consequences
#   shown first. The firewall step is not touched here.
#
# - It does not write /etc/maran/vsftpd/vsftpd.conf. The configuration is rendered
#   by the agent through `ops::safe_write` (render -> atomic rename -> validate with
#   the real vsftpd -> reload, roll back on refusal). The validation follows the
#   rename because vsftpd is asked about the file at the path it will read, which a
#   temporary file is not; the cost of that order is a kill window in which
#   unvalidated content is live (rules/rust.md "Config writes: render -> swap ->
#   validate"). This step creates only the directory it lands in, because a config
#   written by two owners has no owner.
#
# - It does not create a per-account jail, its `home` mount point, or the bind-mount
#   unit that fills it. Those are account-lifetime resources belonging to the agent,
#   which derives every path from a validated `AccountName`. The installer runs once,
#   before any account exists; it lays the ground the agent then builds on.
#
# - It does not edit /etc/pam.d/vsftpd. Both families' stock file includes
#   pam_shells.so and every login this panel creates has a nologin shell, so the
#   stock stack refuses all of them — and editing a file the distribution owns to
#   work around that would also change what the packaged daemon accepts.
#
# Every action is idempotent, because installers get re-run: the group is created
# only if absent, `install -d` restates owner and mode rather than inheriting them,
# and the two files this step owns outright are rewritten whole rather than appended
# to, so a re-run converges on one current version however many times it happens.
# Rewritten and never appended to is a rule and not a preference wherever a vsftpd
# configuration is concerned, and the reason is worth stating once: measured on
# 2026-09-09 on ubuntu:24.04 (3.0.5-0ubuntu3.1) and almalinux:9 (3.0.5-8.el9), when a key
# appears twice in one file the LAST occurrence wins — an appended
# `force_local_logins_ssl=NO` after a rendered `=YES` turns forced TLS off, and an
# appended `listen_port` moves the listener. So an append is not an inert duplicate that
# a later render tidies away; it silently replaces the rendered decision. This step
# appends to nothing (`install -D`, `install -d`, a staged render plus `mv -f`, and
# `ln -sfn` — there is no `>>` in this file), and it does not write
# /etc/maran/vsftpd/vsftpd.conf at all: that file is the agent's, written whole through
# `ops::safe_write`. The one thing that legitimately overrides a rendered key is
# `-o<key>=<value>` on the command line in maran-ftps.service, which vsftpd applies after
# reading the file — the same last-wins rule, not an exception to it.
#
# Threat note (written before this file existed, as rules/security.md requires):
# docs/superpowers/notes/2026-09-09-ftps-threat-note.md. The second-reviewer
# requirement it records is OUTSTANDING, and this branch must not merge to `main`
# until a human has read it.
set -euo pipefail

# The system group whose membership is the ENTIRE authorization to use FTPS, matching
# `DistroAdapter::ftps_group()`. It is one string in two places by necessity — the
# agent adds logins to it, the installer creates it.
#
# The two spellings are COMPARED, and where that happens is worth naming because the
# sentence here used to claim a check that did not exist: nothing read both sides, and
# a drift would have had the installer create group A while the agent added logins to
# group B and this step's PAM stack tested group A — every FTPS login on every install
# refused, with every gate in the repository green. The comparison now lives in
# `assert_the_installer_and_the_agent_spell_the_ftps_names_the_same`
# (docker/polygon/assert-installer-steps.sh), which runs at polygon image BUILD time on
# both families: it sources this file and reads the literal out of `ftps_group()` in each
# family's services module. What it cannot see is a value the agent computes instead of
# declaring; the agent's own unit tests pin these literals from the other side.
readonly MARAN_FTPS_GROUP="maran-ftps"

# The base directory holding one root-owned jail per account, matching
# `AgentPaths::FTPS_JAIL_ROOT`. root:root 0711.
#
# 0711 AND NOT 0700, AND NOT THE 0700 THE SFTP BASE CARRIES. The two bases are siblings
# by design and their modes differ because the two daemons walk their chroot path at
# different privilege. `sshd` walks `ChrootDirectory` as ROOT before it drops, which is
# why installer/lib/40-user.sh can give /var/lib/maran-sftp 0700 and does. vsftpd
# `chdir()`s into the login's passwd home AFTER dropping to the account's uid, so every
# component of the jail path must carry the execute bit for that uid or nothing beneath
# it is reachable. Measured on 2026-09-09 in a throwaway ubuntu:24.04 container against
# this step's own PAM stack, one login, only this mode changed between the runs:
#   0700 -> 500 OOPS: cannot change directory:/var/lib/maran-ftps/alice
#   0711 -> 230 Login successful. / 226 Directory send OK.
# At 0700 EVERY FTPS login on every real install fails, and under the forced TLS this
# product ships the customer does not even see that message: vsftpd writes the 500 in the
# clear on a channel the client is reading as TLS, so the connection simply breaks.
# The same 0711 for the same reason is already in this installer at /home/.maran-restore
# (installer/lib/40-user.sh), where the account's own archiver extracts below a directory
# it must traverse and must not be able to enumerate.
#
# WHAT 0711 GRANTS, AND TO WHOM — stated because widening a mode on a privileged surface
# is a change to that surface (rules/security.md). 0711 is execute-without-read:
# TRAVERSABLE, NOT LISTABLE. Any local uid on the box can walk THROUGH this directory to
# a name it already knows; no local uid can read it, so it cannot learn the names, and
# the names are the hosting accounts. Measured as uid `nobody` on ubuntu:24.04 and
# almalinux:9 alike: at 0711 `ls -A /var/lib/maran-ftps` is refused with "Permission
# denied" while `stat /var/lib/maran-ftps/<account>` succeeds; at 0755 both succeed.
#
# So the security of the arrangement rests on the per-account jail and not on this base,
# and that is where it already rested — the base's 0700 was never load-bearing, because
# it made the daemon fail rather than making anything safe. What holds beneath it:
#   /var/lib/maran-ftps/<account>        root:root 0755 — the chroot root. World-readable
#     on purpose: the login must traverse and list it and MUST NOT be able to write it
#     (vsftpd refuses to run at all with a writable chroot root, `500 OOPS: vsftpd:
#     refusing to run with writable root inside chroot()`). It contains exactly one entry.
#   /var/lib/maran-ftps/<account>/home   the account's real home, BIND-MOUNTED here. Its
#     mode is the home's own, `<account>:<web server group> 0750`, and that mode is the
#     containment: no other uid may enter it.
# The net grant is therefore that a local uid who already knows an account name reaches a
# second path to that account's home — a home whose own 0750 refuses it, exactly as the
# first path does. /home is root:root 0755 on both families, so that uid could already
# walk to /home/<account> and be refused there. This base does not widen what any local
# uid can READ; it makes the daemon able to reach the jail at all.
#
# A SIBLING of /var/lib/maran, never a child of it, and the reason is a defect this
# repository has already paid for once. Step 40 creates /var/lib/maran as
# `maran:maran 0750` — owned by the unprivileged account the api runs as — and an
# unprivileged uid that owns an ancestor of a chroot can
# rename a level aside and leave an entry of its own at the name every customer's
# jail hangs under. While the SFTP base was /var/lib/maran/sftp, sshd refused every
# chroot with `bad ownership or modes for chroot directory component
# "/var/lib/maran/"` — every SFTP login on every real install. vsftpd's rule is not
# the same rule but it is not weaker: it refuses to run at all with a chroot root the
# logged-in user can write to (`500 OOPS: vsftpd: refusing to run with writable root
# inside chroot()`). /var/lib itself is root:root 0755 on every supported family, so
# no unprivileged uid can place an entry at this name.
#
# Compared against `AgentPaths::FTPS_JAIL_ROOT` by the same polygon assertion that
# compares the group above, for the same reason: the agent mounts every account's jail
# under this root and the installer is what creates it.
readonly MARAN_FTPS_JAIL_ROOT="/var/lib/maran-ftps"

# The agent's own configuration directory for this daemon, root:root 0755. Not a
# distro fact and therefore not an adapter method: it lives in `AgentPaths` beside
# /etc/maran/nginx/sites and /etc/maran/certificates. The two families' shipped
# config paths (/etc/vsftpd.conf against /etc/vsftpd/vsftpd.conf) reach nothing here,
# because nothing this panel runs reads them.
readonly MARAN_FTPS_CONFIG_DIR="/etc/maran/vsftpd"

# The parent of the directory above. Created by step 60 as `root:<service group> 0750` on a
# real install; named here only so that this step, run on its own (the polygon images
# do exactly that), does not have to invent a mode for a directory somebody else owns.
readonly MARAN_FTPS_CONFIG_PARENT="/etc/maran"

# The PAM service name vsftpd is pointed at by `pam_service_name`, and the file that
# implements it. The whole file is ours; the stock /etc/pam.d/vsftpd is never edited.
#
# Compared, by the same polygon assertion as the two above, against the
# `pam_service_name` the agent renders into its vsftpd configuration — and against
# /etc/pam.d, which is where libpam resolves such a name. A service name is only half a
# path, and either half drifting leaves the group gate out of the path of every login.
readonly MARAN_FTPS_PAM_SERVICE="/etc/pam.d/maran-ftps"

# Maran's own unit, and where units live. Named MARAN_FTPS_UNIT_DIR and not
# MARAN_UNIT_DIR: installer/lib/70-services.sh already declares that name `readonly`
# and run_step sources every step into the SAME shell, so re-declaring it here would
# abort the install with "readonly variable" on a line that looks like a constant.
readonly MARAN_FTPS_UNIT_NAME="maran-ftps.service"
readonly MARAN_FTPS_UNIT_DIR="/etc/systemd/system"

# The distribution's own unit, which this step masks and never runs.
readonly MARAN_FTPS_PACKAGED_UNIT="vsftpd.service"
readonly MARAN_FTPS_PACKAGED_UNIT_PATH="/etc/systemd/system/vsftpd.service"

# Where the rotation policy for the daemon's transfer log lands. Without it
# `vsftpd_log_file=/var/log/maran/ftps.log` grows unbounded: this installer configures
# no rotation for that file anywhere else, and both families ship logrotate in their
# base set.
#
# That path is the FOURTH name the two halves of FTPS both spell, and like the three
# above it is now COMPARED rather than restated in hope. The agent declares it
# (`FTPS_LOG_PATH` in agent/crates/ops/src/ftps/enable_ftps.rs) and renders it into
# `vsftpd_log_file`; installer/logrotate/maran-ftps names it in the stanza that bounds
# it. `assert_the_installer_and_the_agent_spell_the_ftps_names_the_same` reads the
# agent's constant and requires the rotation stanza to name exactly it -- and then
# requires the same of EVERY log path under /var/log written anywhere in the rotation
# policy, in this file (comments included: a comment naming the wrong path is what the
# next person edits from) and in the agent file that declares it, so a fourth site
# added later is caught by name rather than by luck. This paragraph is inside that
# census: the one spelling above is the one the agent declares, and a drift in it fails
# the assertion exactly as a drift in the stanza would. A drift is silent by construction:
# vsftpd would keep writing, no unit would fail, no gate would move, and the file would
# grow one line per login and per transfer until the operator's partition filled.
readonly MARAN_FTPS_LOGROTATE_DEST="/etc/logrotate.d/maran-ftps"

# vsftpd_packages_for_family: the package, per family.
#
# The ONE place a package name appears, mirroring `mysql_packages_for_family` in
# 85-mysql.sh, so a package rename stops the polygon image build rather than a
# customer's install. Both families spell it the same word today; they are written
# apart anyway, because a list that is right by coincidence is wrong the first time a
# distribution changes one of them.
#
# Public on purpose: the polygon images install the package from THIS list.
vsftpd_packages_for_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "vsftpd" ;;
    rhel)   echo "vsftpd" ;;
    *)
      echo "89-ftps.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

# packaged_vsftpd_is_masked: 0 when /etc/systemd/system/vsftpd.service is a symbolic
# link to /dev/null, which is what a mask IS on every systemd version this product
# supports. Asked of the filesystem and not of systemd, so the answer is the same on a
# booted host and inside an image build where no init runs.
packaged_vsftpd_is_masked() {
  [ "$(readlink -- "$MARAN_FTPS_PACKAGED_UNIT_PATH" 2>/dev/null || true)" = "/dev/null" ]
}

# mask_packaged_vsftpd: make the distribution's unit unstartable BEFORE its package
# exists on the machine.
#
# `systemctl mask` first, because on a real host it is the supported way and it also
# tells systemd. It is then VERIFIED and, if the link is not there, made directly:
# `systemctl` is not a working client during an image build (the polygon installs a
# stand-in that starts nothing), and a mask that silently did nothing is precisely the
# window this function exists to close. The link is the mask; nothing else is.
#
# A REAL file at that path is refused rather than deleted. `systemctl mask` itself
# refuses in that case ("File /etc/systemd/system/vsftpd.service already exists"), and
# the file is an operator's own unit or override: destroying it to make room for a
# /dev/null link would be this installer silently taking over a service somebody else
# configured on their machine.
mask_packaged_vsftpd() {
  if [ -e "$MARAN_FTPS_PACKAGED_UNIT_PATH" ] && [ ! -L "$MARAN_FTPS_PACKAGED_UNIT_PATH" ]; then
    echo "89-ftps.sh: ${MARAN_FTPS_PACKAGED_UNIT_PATH} is a real file, not a mask." >&2
    echo "  Something on this host — an operator, or another product — has installed its own" >&2
    echo "  ${MARAN_FTPS_PACKAGED_UNIT}. Maran will not delete it, and it will not install FTPS" >&2
    echo "  while the packaged daemon can still be started with the distribution's own" >&2
    echo "  configuration (port 21, local logins, no TLS required)." >&2
    echo "  Move that unit aside and re-run the installer." >&2
    exit 1
  fi

  systemctl mask "$MARAN_FTPS_PACKAGED_UNIT" >/dev/null 2>&1 || true

  if ! packaged_vsftpd_is_masked; then
    install -d -o root -g root -m 0755 "$MARAN_FTPS_UNIT_DIR"
    ln -sfn /dev/null "$MARAN_FTPS_PACKAGED_UNIT_PATH"
  fi

  # The assertion, in the step itself and not only in a polygon check: if this is not
  # true the next line of this step opens port 21 on this machine.
  if ! packaged_vsftpd_is_masked; then
    echo "89-ftps.sh: could not mask ${MARAN_FTPS_PACKAGED_UNIT}; refusing to install the" >&2
    echo "  package, whose postinst would start it on the Debian family. Nothing was installed." >&2
    exit 1
  fi
}

# install_vsftpd_package: the package, through the family's own manager — and never
# before the mask.
#
# The first thing it does is re-ask whether the packaged unit is masked, and abort if
# it is not. That is the ordering constraint made mechanical: deleting the
# `mask_packaged_vsftpd` call from `step_ftps`, or moving it below this one, fails
# HERE, by name, on the machine being installed — rather than being caught later by a
# reviewer, or not at all. A comment saying "call the mask first" is not a control.
install_vsftpd_package() {
  if ! packaged_vsftpd_is_masked; then
    echo "89-ftps.sh: ${MARAN_FTPS_PACKAGED_UNIT} is NOT masked, so installing the vsftpd" >&2
    echo "  package would leave the distribution's own daemon startable — and on the Debian" >&2
    echo "  family its postinst starts it: port 21, local logins, no TLS." >&2
    echo "  mask_packaged_vsftpd must run FIRST." >&2
    echo "  Nothing has been installed." >&2
    exit 1
  fi

  if ! command -v pkg_install >/dev/null 2>&1; then
    echo "89-ftps.sh: pkg_install is not defined. It lives in installer/lib/20-dependencies.sh," >&2
    echo "  which install.sh sources before this step; source it too if you are driving this" >&2
    echo "  step on its own." >&2
    exit 1
  fi

  # shellcheck disable=SC2046
  pkg_install $(vsftpd_packages_for_family)
}

# ensure_ftps_group: creates the group if it is not already there.
#
# A system group (no login, low gid range): it names a capability, never a person.
# Membership of it is the entire authorization to authenticate through the PAM stack
# below, which is why the group is created by the installer and not by the agent — it
# must exist before any login can be put in it, and its absence must not be something
# an account operation quietly repairs.
ensure_ftps_group() {
  if getent group "$MARAN_FTPS_GROUP" >/dev/null 2>&1; then
    return 0
  fi
  groupadd --system "$MARAN_FTPS_GROUP"
}

# ensure_ftps_directories: the jail base and the agent's config directory, each with
# its owner and mode restated rather than inherited from the installer's umask, which
# an operator's shell profile can have changed.
#
# `install -d` alone would exit 0 on a path somebody else had already put there and
# would follow a symbolic link, so whatever occupies the jail base is removed first
# unless it is already a real root-owned directory — the same conditional replacement
# step 40 and step 86 use. The exception is not a weakening: only root can create a
# root-owned directory in /var/lib, and it is what stops a re-run from recursing
# through the live bind mounts of customer homes that hang beneath this path.
#
# The config directory's PARENT (/etc/maran) is step 60's, created there as
# root:<service group> 0750. It is only created here when it is missing, which on a real
# install it never is — this step runs 29 steps after 60 — and which is the case when this
# file is driven on its own. It is created root:root 0750 in that case rather than
# guessing at a service group that does not exist yet, and an EXISTING directory is
# left exactly as its owner made it.
ensure_ftps_directories() {
  if [ -L "$MARAN_FTPS_JAIL_ROOT" ] \
    || { [ -e "$MARAN_FTPS_JAIL_ROOT" ] && [ ! -d "$MARAN_FTPS_JAIL_ROOT" ]; } \
    || { [ -d "$MARAN_FTPS_JAIL_ROOT" ] && [ "$(stat -c '%u' "$MARAN_FTPS_JAIL_ROOT")" -ne 0 ]; }; then
    rm -rf -- "$MARAN_FTPS_JAIL_ROOT"
  fi
  install -d -o root -g root -m 0711 "$MARAN_FTPS_JAIL_ROOT"

  if [ ! -d "$MARAN_FTPS_CONFIG_PARENT" ]; then
    install -d -o root -g root -m 0750 "$MARAN_FTPS_CONFIG_PARENT"
  fi
  install -d -o root -g root -m 0755 "$MARAN_FTPS_CONFIG_DIR"
}

# assert_jail_base_is_traversable_and_not_listable: ask an unprivileged uid the two
# questions the jail base exists to answer, instead of asking `stat` what mode it has.
#
# This check is here because a mode assertion would NOT have caught the defect that put it
# here. The base was created 0700, an assertion required 0700, both agreed, and every FTPS
# login on every real install failed — because vsftpd `chdir()`s into the login's home
# AFTER dropping to the account's uid, and 0700 is exactly the mode that stops it. A check
# that reads the intended value back cannot see an intended value that is wrong. What can
# see it is the operation itself, performed at the privilege the daemon performs it at.
#
# So, as uid `nobody` — an unprivileged uid every supported family ships, standing in for
# an FTPS login's uid, which does not exist yet when the installer runs:
#
#   1. TRAVERSE: `stat` a probe directory one level down must SUCCEED. This is the exact
#      path walk the daemon's chdir performs. It fails at 0700 and at any mode without the
#      other-execute bit, which is the defect, live.
#   2. LIST: reading the base itself must be REFUSED. This is the other half of 0711 and
#      the reason the mode is not simply widened to 0755: the entries are named after
#      hosting accounts, and a listable base publishes one directory name per customer to
#      every local uid on the machine.
#
# Both directions therefore fail loudly: 0700 fails the first question, 0755 fails the
# second, and only execute-without-read passes both. The probe directory is root:root 0700
# so that answering question 1 proves traversal of the BASE and nothing about the probe,
# and it is removed on every path out of this function so that nothing is left in a
# directory whose entries the agent reads as account names.
assert_jail_base_is_traversable_and_not_listable() {
  local probe="${MARAN_FTPS_JAIL_ROOT}/.maran-traversal-probe"
  local uid gid

  if ! command -v setpriv >/dev/null 2>&1; then
    echo "UNOBSERVED HERE: setpriv is not on this host, so the jail base's traversability" \
         "was NOT tested as an unprivileged uid — only its mode was set. setpriv is part of" \
         "util-linux and is present on every supported family, so this line means the host" \
         "is not one of them. The property this check exists for is unverified here."
    return 0
  fi

  uid="$(getent passwd nobody | cut -d: -f3)"
  gid="$(getent passwd nobody | cut -d: -f4)"
  if [ -z "$uid" ] || [ -z "$gid" ]; then
    echo "UNOBSERVED HERE: this host has no 'nobody' account, so the jail base's" \
         "traversability was NOT tested as an unprivileged uid — only its mode was set."
    return 0
  fi

  rm -rf -- "$probe"
  install -d -o root -g root -m 0700 "$probe"

  if ! setpriv --reuid="$uid" --regid="$gid" --clear-groups -- test -d "$probe"; then
    rm -rf -- "$probe"
    echo "89-ftps.sh: uid ${uid} cannot traverse ${MARAN_FTPS_JAIL_ROOT} (mode" \
         "$(stat -c '%a' "$MARAN_FTPS_JAIL_ROOT")). vsftpd chdir()s into the login's home" >&2
    echo "  AFTER dropping to the account's uid, so with this mode EVERY FTPS login fails" >&2
    echo "  with '500 OOPS: cannot change directory' — and under forced TLS the customer" >&2
    echo "  sees only a broken connection, because that message is written in the clear." >&2
    echo "  The base must carry the other-execute bit: 0711." >&2
    exit 1
  fi

  if setpriv --reuid="$uid" --regid="$gid" --clear-groups -- ls -A "$MARAN_FTPS_JAIL_ROOT" >/dev/null 2>&1; then
    rm -rf -- "$probe"
    echo "89-ftps.sh: uid ${uid} can LIST ${MARAN_FTPS_JAIL_ROOT} (mode" \
         "$(stat -c '%a' "$MARAN_FTPS_JAIL_ROOT")). The entries under this base are named" >&2
    echo "  after hosting accounts, so a readable base publishes one directory name per" >&2
    echo "  customer to every local uid on this machine. The base must be traversable and" >&2
    echo "  not readable: 0711, never 0755." >&2
    exit 1
  fi

  rm -rf -- "$probe"
  echo "Jail base ${MARAN_FTPS_JAIL_ROOT} is $(stat -c '%U:%G %a' "$MARAN_FTPS_JAIL_ROOT"):" \
       "uid ${uid} can traverse it and cannot list it, asked as that uid."
}

# render_ftps_pam_service: the PAM stack, on stdout.
#
# Rendered by this file rather than shipped beside it, because the polygon drives
# `install_ftps_pam_service` with only this one file copied into the image: a payload
# asset would turn that assertion into an assertion about a missing COPY.
render_ftps_pam_service() {
  cat <<'PAM'
#%PAM-1.0
# Maran FTPS. This whole file is written by installer/lib/89-ftps.sh and is REWRITTEN
# on every install — an edit made here does not survive an upgrade.
#
# A service of our own, never an edit to /etc/pam.d/vsftpd: both families' stock file
# includes pam_shells.so, and every login this panel creates has a nologin shell, so
# the stock stack refuses all of them. Editing a file the distribution owns to work
# around that would also change what the packaged daemon accepts.
#
# The first line is the authorization, and it is the only one: a login that is not in
# the maran-ftps group cannot authenticate here at all. root, the service account, every
# hosting account and every other system account are therefore refused by this stack
# whether or not they hold a password — which is a stronger statement than a deny-list
# file, because it is a property of what this stack CONTAINS rather than of what
# somebody remembered to add to a list.
#
# pam_unix.so and not password-auth/common-auth: those aggregates pull in the host's
# whole authentication policy — pam_faillock, pam_sss, a directory service an operator
# has configured — and an FTPS login is a local shadow entry and nothing else.
# Including them would make this daemon's answer depend on configuration nobody wrote
# for it.
#
# The membership test is repeated in the account phase on purpose: an auth-only gate
# would be passed once and never re-asked, and `account` is the phase that answers
# "may this user use this service", which is exactly the question being asked.
auth     required   pam_succeed_if.so user ingroup maran-ftps quiet_success
auth     required   pam_unix.so
account  required   pam_succeed_if.so user ingroup maran-ftps quiet_success
account  required   pam_unix.so
session  required   pam_unix.so
PAM
}

# install_ftps_pam_service: put that stack at /etc/pam.d/maran-ftps, root:root 0644.
#
# Staged and renamed rather than written in place. A PAM file read while it is half
# written is a file that denies everything or — worse, depending on which line landed —
# one whose group test has not arrived yet, and the rename is the one operation no
# reader can observe partway through.
#
# 0644 because PAM is read by whatever process authenticates, which here is a daemon
# that has not yet dropped privileges; the file contains no secret, and a mode nobody
# can read is a daemon that cannot authenticate anybody.
install_ftps_pam_service() {
  local staged
  staged="${MARAN_FTPS_PAM_SERVICE}.maran-staging"
  install -d -o root -g root -m 0755 "$(dirname -- "$MARAN_FTPS_PAM_SERVICE")"
  render_ftps_pam_service > "$staged"
  chown root:root "$staged"
  chmod 0644 "$staged"
  mv -f -- "$staged" "$MARAN_FTPS_PAM_SERVICE"
}

# install_ftps_unit: put maran-ftps.service in place, DISABLED and STOPPED.
#
# File placement only — no daemon-reload, no enable, no start. That is what lets this
# function run anywhere systemd is a directory rather than a daemon (the polygon images
# are exactly that), and it is the same separation 86-sftp.sh makes between laying the
# ground and managing a service. `step_ftps` does the one daemon-reload, once.
#
# Nothing here enables the unit, and nothing here ever should: FTPS is off until an
# administrator turns it on.
install_ftps_unit() {
  local source="${SCRIPT_DIR:-}/systemd/maran-ftps.service"
  if [ ! -r "$source" ]; then
    echo "89-ftps.sh: ${source} is missing from the installer payload. Aborting." >&2
    exit 1
  fi
  install -D -o root -g root -m 0644 "$source" "${MARAN_FTPS_UNIT_DIR}/${MARAN_FTPS_UNIT_NAME}"
}

# install_ftps_logrotate: the rotation policy for /var/log/maran/ftps.log.
#
# Every security-relevant choice is argued in a comment inside the shipped file,
# because the file is what an operator reads and this script is not.
install_ftps_logrotate() {
  local source="${SCRIPT_DIR:-}/logrotate/maran-ftps"
  if [ ! -r "$source" ]; then
    echo "89-ftps.sh: ${source} is missing from the installer payload. Aborting." >&2
    exit 1
  fi
  install -D -o root -g root -m 0644 "$source" "$MARAN_FTPS_LOGROTATE_DEST"
}

# report_ftps_is_installed_and_off: state what was actually observed about "not
# listening", and state in the same breath what this environment could not see.
#
# rules/testing.md: a check must be able to observe what it reports on. Two of the
# three things below are answerable by the filesystem, on a booted host and inside an
# image build alike, and they FAIL when the step stops doing its job:
#
#   * the packaged unit is a symbolic link to /dev/null — the mask, observed as the
#     link it is. This goes red if the mask_packaged_vsftpd call is removed.
#   * maran-ftps.service has no entry in any .wants directory — enablement, which is
#     also a symlink fact. This goes red the day something in this step enables it.
#
# The third is the port itself, and it is NOT a check here. Where this step runs during
# an image build there is no booted systemd, so nothing could have started the packaged
# daemon whether or not the mask existed: a "nothing is bound to port 21" assertion is
# green either way, and a check that cannot fail where it runs is decoration. The line
# is printed with its own honesty attached, and the live fact is observed on a real host
# instead.
report_ftps_is_installed_and_off() {
  if ! packaged_vsftpd_is_masked; then
    echo "89-ftps.sh: ${MARAN_FTPS_PACKAGED_UNIT} is not masked after this step ran." >&2
    exit 1
  fi
  echo "Masked: ${MARAN_FTPS_PACKAGED_UNIT_PATH} -> $(readlink -- "$MARAN_FTPS_PACKAGED_UNIT_PATH")"

  local enabled_links
  enabled_links="$(find /etc/systemd/system -name "$MARAN_FTPS_UNIT_NAME" -path '*.wants/*' 2>/dev/null || true)"
  if [ -n "$enabled_links" ]; then
    echo "89-ftps.sh: ${MARAN_FTPS_UNIT_NAME} is ENABLED (${enabled_links}). This step installs" >&2
    echo "  FTPS switched off; enabling it is an administrator action taken in the panel." >&2
    exit 1
  fi
  echo "Not enabled: no .wants entry for ${MARAN_FTPS_UNIT_NAME} under /etc/systemd/system."

  if command -v ss >/dev/null 2>&1; then
    ss -lntp 2>/dev/null | awk 'NR == 1 || $4 ~ /:21$/' || true
  fi
  echo "UNOBSERVED HERE: whether the postinst would have started the packaged daemon —" \
       "no systemd boots in an image build, so the port-21 line above is green either way." \
       "The mask is observed as a symlink instead, and the live fact is observed in" \
       "ftps_on_a_real_host.rs."
}

step_ftps() {
  echo "Installing FTPS (vsftpd), switched off..."
  # The order is the security property. Nothing below moves above the mask.
  mask_packaged_vsftpd
  install_vsftpd_package
  ensure_ftps_group
  ensure_ftps_directories
  assert_jail_base_is_traversable_and_not_listable
  install_ftps_pam_service
  install_ftps_unit
  install_ftps_logrotate
  # The one daemon-reload, after the unit file is in place and before anything reports
  # on it. It enables nothing: a reload makes systemd read the unit, it does not want it.
  systemctl daemon-reload
  report_ftps_is_installed_and_off
  echo "FTPS is installed and OFF: ${MARAN_FTPS_UNIT_NAME} is disabled and stopped, no port is"
  echo "  open, and per-account jails under ${MARAN_FTPS_JAIL_ROOT} are created by the agent."
  echo "  An administrator turns FTPS on in the panel, which opens the ports it needs then."
}
