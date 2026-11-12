#!/usr/bin/env bash
# Step 89 (third part): put the mail stack on the host — installed, configurable, and
# NOT LISTENING.
#
# The promise this step makes, and the only one it makes: when it finishes, Postfix, Dovecot,
# the Redis-protocol server and (where the distribution carries it) Rspamd are on the machine,
# every packaged unit is MASKED and STOPPED, the directories the agent relies on exist with
# their owners and modes, and no mail socket has been bound at any point during the step.
# rules/security.md item 10 forbids a new listening port arriving by surprise; the spec
# sanctions the daemons, so what this step owes is that they arrive switched off. Turning mail
# on is `EnableMail`, an administrator action in the panel, which unmasks the units, renders
# the configuration and starts them in dependency order (agent/crates/ops/src/mail/).
#
# THE HOSTING MODEL, decided here and not left to the agent: the daemons run as the
# distribution's own packaged units against Maran-rendered configuration in the packages' own
# directories. This panel installs no unit of its own for them. A Maran-owned unit against an
# alternate configuration directory would fight the Debian family's `postfix@-` wrapper, the
# RHEL family's SELinux policy (which matches the packaged units' file contexts) and every
# package's own logrotate and tmpfiles; the mask is what keeps the packaged unit from ever
# running on the package's own configuration.
#
# WHY THE MASKS COME FIRST, which is the whole ordering of `step_mail`: on the Debian family a
# package's postinst starts its daemon, and Postfix and Dovecot come up under the
# distribution's own configuration — port 25 and the IMAP ports on every address. An installer
# that installed the packages and then disabled the units would have run an open mail server
# for the length of the install on a machine that is by definition reachable from the
# internet. `systemctl mask` replaces the unit with a symbolic link to /dev/null in
# /etc/systemd/system, which shadows the distribution's file; systemd refuses to start a
# masked unit, so a postinst's start binds nothing. The ordering is made mechanical the way the
# FTPS step makes it: `install_mail_packages` REFUSES to run until every mask is observably in
# place.
#
# WHAT THIS STEP DOES NOT DO, and each is a decision and not an omission:
#
# - It does not add a package repository and it makes no call of its own beyond the package
#   manager's install of what the host's configured repositories already carry. Rspamd is in no
#   RHEL-family base repository (the vendor's repository and EPEL are required), and a new
#   package source is a new trust root and a new outbound call that rules/security.md item 10
#   has not authorised. So on a host whose repositories do not carry it Rspamd is reported as
#   NOT INSTALLED, by name, and `EnableMail` then refuses with that daemon's name. The same
#   holds for the Ubuntu `universe` component, which a minimal image may not have enabled.
# - It does not install ClamAV. The antivirus is optional, off by default, and arrives with the
#   change that lets an administrator turn it on.
# - It does not enable or start any unit and it opens no port, in the firewall or anywhere else.
# - It does not write Postfix's, Dovecot's, Rspamd's or the key-value server's configuration.
#   The agent renders it through ops::safe_write (render -> atomic rename -> validate with the
#   daemon's own checker -> reload, roll back on refusal); a configuration written by two owners
#   has no owner. This step creates only the directories the agent writes into.
# - It does not harden the packaged units with drop-ins. Whether Postfix, Dovecot and Rspamd
#   still work under sandboxing directives is a measurement a booted host owes, and an
#   unmeasured directive that stops a mail daemon is worse than none.
# - It does not touch SELinux. Maildirs under /home/<account>/mail are written by Dovecot under
#   the account's own uid, and whether the stock policy permits that is UNVERIFIED on the RHEL
#   family (research_notes/mail-stack-inventory.md section 5); the remedy there is a labelled
#   path or a small local module, a new privileged installer step that needs its own review.
#
# UNOBSERVED HERE, and stated rather than papered over: whether each postinst would have started
# its daemon (an image build boots no systemd, and a container's policy-rc.d refuses the start
# for its own reasons), so the artifact observed is the mask link and not the live fact;
# whether masking the Debian family's `postfix` wrapper also holds back its `postfix@-` instance
# unit; and whether the RHEL family's `dovecot.socket` needs a mask beside `dovecot.service`.
# The mail ports are listed at the end for the same reason: a line there is evidence, an empty
# list is not.
#
# Every action is idempotent, because installers get re-run: `mask` of a masked unit, `install -d`
# restating owner and mode, `usermod -aG` of a member, and a package install of installed packages
# all converge, and this file appends to nothing.
#
# Threat note (written before the code, as rules/security.md requires):
# docs/superpowers/notes/2026-11-07-mail-module-threat-note.md. The second-reviewer requirement
# it records is OUTSTANDING, and this branch must not merge to `main` until a human has read it.
set -euo pipefail

# The directory holding the lookup tables, matching `AgentPaths::MAIL_CONFIG_DIRECTORY`.
# root:root 0755: Postfix reads its maps as its own user.
readonly MARAN_MAIL_CONFIG_DIR="/etc/maran/mail"

# Where Dovecot's auth process reads the passwd-file from, matching
# `AgentPaths::MAIL_DOVECOT_DIRECTORY`. root:<dovecot group> 0750: the file inside holds password
# hashes and is written world-readable by mode, so THIS directory is the protection.
readonly MARAN_MAIL_DOVECOT_DIR="/etc/maran/mail/dovecot"

# Where the DKIM private keys live, matching `AgentPaths::MAIL_DKIM_DIRECTORY`.
# root:<rspamd group> 0750.
readonly MARAN_MAIL_DKIM_DIR="/etc/maran/mail/dkim"

# The parent of the directories above, created by step 60. Named here only so that this step, run
# on its own, does not invent a mode for a directory somebody else owns.
readonly MARAN_MAIL_CONFIG_PARENT="/etc/maran"

readonly MARAN_MAIL_UNIT_DIR="/etc/systemd/system"

# Where this step records the one key-value server unit it took over, as a bare unit name. The
# uninstaller reads it to stop and disable only that unit (installer/uninstall.sh remove_mail);
# the file lives inside the directory it removes, so it goes with the rest of Maran's mail state.
readonly MARAN_MAIL_KEY_VALUE_UNIT_FILE="/etc/maran/mail/key-value-unit"

# The users and groups of the daemons, matching `MailFacts::dovecot_user` and
# `MailFacts::rspamd_user` (the group of each is its user's name on both families). UNVERIFIED on
# the RHEL family's vendor Rspamd package.
readonly MARAN_MAIL_DOVECOT_GROUP="dovecot"
readonly MARAN_MAIL_RSPAMD_USER="_rspamd"
# Rspamd's configuration directory, the same on every family (`MailFacts::rspamd_config_directory`).
readonly MARAN_MAIL_RSPAMD_CONFIG_DIR="/etc/rspamd"

# mail_key_value_service: the unit, package and user of the Redis-protocol server this release runs,
# matching `MailFacts::key_value_server`. One line, "<service> <package> <user>". The RHEL family
# ships Redis on 9 and Valkey on 10; a release neither covers is refused by name, because the
# agent would refuse it too.
mail_key_value_service() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "redis-server redis-server redis" ;;
    rhel)
      case "${MARAN_OS_VERSION_ID%%.*}" in
        9)  echo "redis redis redis" ;;
        10) echo "valkey valkey valkey" ;;
        *)
          echo "89-mail.sh: no key-value server is known for RHEL-family release '${MARAN_OS_VERSION_ID:-}'" >&2
          exit 1
          ;;
      esac
      ;;
    *)
      echo "89-mail.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

# mail_units: the units this step masks, matching the `*_service()` methods of `MailFacts` and the
# key-value server above. Postfix, Dovecot and Rspamd are named alike on both families.
mail_units() {
  local kv_service
  kv_service="$(mail_key_value_service | cut -d' ' -f1)"
  echo "postfix dovecot rspamd ${kv_service}"
}

# mail_required_packages: what must install, matching `MailFacts::mail_packages` minus Rspamd (which
# is optional here, see below) plus the key-value server's package.
mail_required_packages() {
  local kv_package
  kv_package="$(mail_key_value_service | cut -d' ' -f2)"
  case "${MARAN_OS_FAMILY:-}" in
    debian)
      echo "postfix postfix-lmdb dovecot-core dovecot-imapd dovecot-pop3d dovecot-lmtpd dovecot-sieve ${kv_package}"
      ;;
    rhel)
      echo "postfix postfix-lmdb dovecot dovecot-pigeonhole ${kv_package}"
      ;;
  esac
}

# mail_package_is_available: whether the host's configured repositories carry a package. The
# answer comes from the package manager's own metadata and calls nothing.
mail_package_is_available() {
  local package="$1" candidate
  case "${MARAN_OS_FAMILY:-}" in
    debian)
      candidate="$(apt-cache policy "$package" 2>/dev/null | awk '/Candidate:/ { print $2; exit }')"
      [ -n "$candidate" ] && [ "$candidate" != "(none)" ]
      ;;
    rhel)
      dnf -q list --available "$package" >/dev/null 2>&1 || dnf -q list --installed "$package" >/dev/null 2>&1
      ;;
    *)
      return 1
      ;;
  esac
}

# mail_stack_is_managed: whether an earlier run of this step already put the mail stack under
# Maran. The directory is the marker, and it is created at the END of the step's work on the
# first run, so a run that died halfway is a fresh install and not a managed one.
#
# It matters because this step is re-run by every update. On a managed host the units may be
# unmasked, enabled and serving mail, and masking them again would make the next restart fail
# until the agent unmasks them, so a managed host is left exactly as it is.
mail_stack_is_managed() {
  [ -d "$MARAN_MAIL_CONFIG_DIR" ]
}

# refuse_to_take_over_a_running_mail_daemon: on a host this step has not managed before, a mail
# unit that is active or enabled belongs to somebody else. Masking it would stop it restarting,
# and installing packages over it would change a mail server an operator is running. So the step
# refuses, by name, and changes nothing.
refuse_to_take_over_a_running_mail_daemon() {
  local unit
  for unit in $(mail_units); do
    if systemctl is-active --quiet "${unit}.service" 2>/dev/null \
      || [ "$(systemctl is-enabled "${unit}.service" 2>/dev/null || true)" = "enabled" ]; then
      echo "89-mail.sh: ${unit}.service is already running or enabled on this host, and Maran did" >&2
      echo "  not start it. Maran will not mask, reconfigure or replace a mail daemon an operator" >&2
      echo "  runs. Stop and disable it (or remove the package) and re-run the installer." >&2
      exit 1
    fi
  done
}

# mail_unit_is_masked: a unit is masked when its file in /etc/systemd/system is a link to /dev/null.
mail_unit_is_masked() {
  [ "$(readlink -- "${MARAN_MAIL_UNIT_DIR}/${1}.service" 2>/dev/null || true)" = "/dev/null" ]
}

# mask_packaged_mail_units: mask every packaged unit BEFORE any package is installed.
#
# A real file where the mask link belongs means something else on this host — an operator, or
# another product — has installed its own unit of that name. It is not deleted: this step refuses
# instead, because a mail daemon that can still be started on the distribution's configuration is
# the window the ordering above exists to close.
mask_packaged_mail_units() {
  local unit path
  for unit in $(mail_units); do
    path="${MARAN_MAIL_UNIT_DIR}/${unit}.service"
    if [ -e "$path" ] && [ ! -L "$path" ]; then
      echo "89-mail.sh: ${path} is a real file, not a mask." >&2
      echo "  Something on this host has installed its own ${unit}.service. Maran will not delete it," >&2
      echo "  and it will not install the mail stack while a packaged daemon can still be started" >&2
      echo "  on its own configuration. Move that unit aside and re-run the installer." >&2
      exit 1
    fi
    systemctl mask "${unit}.service" >/dev/null 2>&1 || true
    if ! mail_unit_is_masked "$unit"; then
      install -d -o root -g root -m 0755 "$MARAN_MAIL_UNIT_DIR"
      ln -sfn /dev/null "$path"
    fi
    if ! mail_unit_is_masked "$unit"; then
      echo "89-mail.sh: could not mask ${unit}.service; refusing to install the packages, whose" >&2
      echo "  postinst would start it on the Debian family. Nothing was installed." >&2
      exit 1
    fi
  done
}

# preseed_postfix: answer the Debian family's Postfix installer questions, so the postinst neither
# prompts nor asks for an "Internet Site" configuration. The agent replaces main.cf whole at enable;
# "Local only" is the answer that leaves a valid file meanwhile. Nothing is asked on the RHEL family.
preseed_postfix() {
  [ "${MARAN_OS_FAMILY:-}" = "debian" ] || return 0
  if ! command -v debconf-set-selections >/dev/null 2>&1; then
    return 0
  fi
  printf '%s\n' \
    "postfix postfix/main_mailer_type select Local only" \
    "postfix postfix/mailname string $(hostname -f 2>/dev/null || hostname)" \
    | debconf-set-selections
}

# install_mail_packages: install what the distribution carries.
#
# REFUSES to run until every unit is masked, so moving the mask after the install (or deleting it)
# fails the step by name instead of quietly reopening the window.
install_mail_packages() {
  local unit
  for unit in $(mail_units); do
    if ! mail_unit_is_masked "$unit"; then
      echo "89-mail.sh: ${unit}.service is NOT masked, so installing the mail packages would leave" >&2
      echo "  the distribution's own daemon startable — and on the Debian family its postinst starts" >&2
      echo "  it. mask_packaged_mail_units must run FIRST. Nothing has been installed." >&2
      exit 1
    fi
  done
  if ! command -v pkg_install >/dev/null 2>&1; then
    echo "89-mail.sh: pkg_install is not defined. It lives in installer/lib/20-dependencies.sh," >&2
    echo "  which install.sh sources before this step; source it too if you are driving this" >&2
    echo "  step on its own." >&2
    exit 1
  fi
  preseed_postfix
  # shellcheck disable=SC2046
  pkg_install $(mail_required_packages)
  if mail_package_is_available rspamd; then
    pkg_install rspamd
  else
    echo "NOT INSTALLED: rspamd is not carried by this host's configured package repositories."
    echo "  The spam filter and DKIM signer are required for mail to be enabled. Providing them"
    echo "  needs a package source this installer does not add (a new repository is a new trust root"
    echo "  and a new outbound call; rules/security.md item 10). EnableMail will refuse and name it."
  fi
}

# mail_group_exists: whether a group is in the group database.
mail_group_exists() {
  getent group "$1" >/dev/null 2>&1
}

# ensure_mail_directories: the directories the agent writes into, each with the owner and mode
# the agent relies on. A directory whose group does not exist yet (Rspamd's, when it is not
# installed) is created root-only: the agent refuses to enable without Rspamd, so nothing needs it.
ensure_mail_directories() {
  local dovecot_group="root" rspamd_group="root"
  if [ ! -d "$MARAN_MAIL_CONFIG_PARENT" ]; then
    install -d -o root -g root -m 0750 "$MARAN_MAIL_CONFIG_PARENT"
  fi
  install -d -o root -g root -m 0755 "$MARAN_MAIL_CONFIG_DIR"
  if mail_group_exists "$MARAN_MAIL_DOVECOT_GROUP"; then
    dovecot_group="$MARAN_MAIL_DOVECOT_GROUP"
  fi
  install -d -o root -g "$dovecot_group" -m 0750 "$MARAN_MAIL_DOVECOT_DIR"
  if mail_group_exists "$MARAN_MAIL_RSPAMD_USER"; then
    rspamd_group="$MARAN_MAIL_RSPAMD_USER"
  fi
  install -d -o root -g "$rspamd_group" -m 0750 "$MARAN_MAIL_DKIM_DIR"
  # The agent writes the local-submission rule into plugins.d and the DKIM signing condition into
  # lua.local.d, and the config-write protocol writes beside the target and never creates the
  # directory. Some packages (Debian and Ubuntu's among them) ship neither, so they are made here,
  # where Rspamd is installed.
  if [ -d "$MARAN_MAIL_RSPAMD_CONFIG_DIR" ]; then
    install -d -o root -g root -m 0755 \
      "$MARAN_MAIL_RSPAMD_CONFIG_DIR/plugins.d" "$MARAN_MAIL_RSPAMD_CONFIG_DIR/lua.local.d"
  fi
}

# record_taken_over_key_value_unit: write the name of the key-value server unit this step masked, so
# the uninstaller touches that unit and no other. A redis or valkey unit an operator runs under a
# different name stays theirs; the uninstaller never guesses it from the unit names it knows.
record_taken_over_key_value_unit() {
  local unit
  unit="$(mail_key_value_service | cut -d' ' -f1)"
  printf '%s\n' "$unit" >"$MARAN_MAIL_KEY_VALUE_UNIT_FILE"
  chmod 0644 "$MARAN_MAIL_KEY_VALUE_UNIT_FILE"
}

# allow_rspamd_to_reach_the_key_value_server: Rspamd keeps its rate-limit counters in the
# key-value server over a unix socket the agent renders at mode 0660, group-owned by the server's
# own group. Membership of that group is the whole authorisation: any other mode would let every
# local uid, a customer's PHP included, talk to it. Skipped when Rspamd is not installed.
allow_rspamd_to_reach_the_key_value_server() {
  local kv_group
  kv_group="$(mail_key_value_service | cut -d' ' -f3)"
  if getent passwd "$MARAN_MAIL_RSPAMD_USER" >/dev/null 2>&1 && mail_group_exists "$kv_group"; then
    usermod -aG "$kv_group" "$MARAN_MAIL_RSPAMD_USER"
  fi
}

# report_mail_is_installed_and_off: print what was observed, and refuse a unit left unmasked or
# enabled.
report_mail_is_installed_and_off() {
  local unit enabled_links
  for unit in $(mail_units); do
    if ! mail_unit_is_masked "$unit"; then
      echo "89-mail.sh: ${unit}.service is not masked after this step ran." >&2
      exit 1
    fi
    echo "Masked: ${MARAN_MAIL_UNIT_DIR}/${unit}.service -> $(readlink -- "${MARAN_MAIL_UNIT_DIR}/${unit}.service")"
    enabled_links="$(find /etc/systemd/system -name "${unit}.service" -path '*.wants/*' 2>/dev/null || true)"
    if [ -n "$enabled_links" ]; then
      echo "89-mail.sh: ${unit}.service is ENABLED (${enabled_links}). This step installs mail" >&2
      echo "  switched off; enabling it is an administrator action taken in the panel." >&2
      exit 1
    fi
  done
  echo "Not enabled: no .wants entry for any mail unit under /etc/systemd/system."
  if command -v ss >/dev/null 2>&1; then
    # A line here is evidence that something listens on a mail port — possibly a daemon that
    # was on this host before Maran, which this step neither started nor stops.
    ss -lnt 2>/dev/null | awk 'NR == 1 || $4 ~ /:(25|465|587|143|993|110|995)$/' || true
  fi
  echo "UNOBSERVED HERE: whether any postinst would have started its daemon — no systemd boots in an" \
       "image build, so the empty port list above is green either way. The mask is observed as a" \
       "symlink instead, and the live fact is observed in mail_server_on_a_real_host.rs."
}

# refresh_managed_mail_stack: what a re-run does on a host that is already managed: install any
# package that is missing (an upgrade may have added a dependency) and restate the directories.
# It masks nothing and starts nothing; the packages' own postinst for an already-installed package
# does not restart a unit that is stopped, and one that runs is Maran's to keep running.
refresh_managed_mail_stack() {
  # shellcheck disable=SC2046
  pkg_install $(mail_required_packages)
  ensure_mail_directories
  allow_rspamd_to_reach_the_key_value_server
}

# mail_is_offered_on_family: 0 when this family can lay out the mail stack, 1 when it is not
# offered. The SUSE family is not offered until measured: the agent's MailFacts answers `None`
# for dovecot_major and key_value_server there, so it would refuse a stack this step installed.
# UNVERIFIED: the Leap 16 mail packages (postfix, dovecot, rspamd, a key-value server) were not
# inspected; confirm with `zypper info postfix dovecot rspamd valkey` in the pinned Leap image.
mail_is_offered_on_family() {
  case "${MARAN_OS_FAMILY:-}" in
    suse) return 1 ;;
    *)    return 0 ;;
  esac
}

step_mail() {
  if ! mail_is_offered_on_family; then
    # Nothing is installed, masked or written, so there is nothing for the uninstaller to take back.
    echo "Mail is not offered on this OS family; skipping. Nothing was installed or masked."
    return 0
  fi
  echo "Installing the mail stack (Postfix, Dovecot, Rspamd, key-value server), switched off..."
  # The order is the security property. Nothing below moves above the masks.
  if mail_stack_is_managed; then
    echo "Mail is already under Maran on this host: packages and directories are refreshed, and the"
    echo "  units are left exactly as they are."
    refresh_managed_mail_stack
    return 0
  fi
  refuse_to_take_over_a_running_mail_daemon
  mask_packaged_mail_units
  install_mail_packages
  allow_rspamd_to_reach_the_key_value_server
  # Last, because it is the marker above: a run that died before here is a fresh install next time.
  ensure_mail_directories
  record_taken_over_key_value_unit
  systemctl daemon-reload
  report_mail_is_installed_and_off
  echo "Mail is installed and OFF: every packaged mail unit is masked and stopped, no port is open,"
  echo "  and the configuration is rendered by the agent once an administrator turns mail on in the"
  echo "  panel. Opening the mail ports (25, 465, 587, 143, 993, and 110, 995 with POP3) in the"
  echo "  firewall is a separate administrator action."
}
