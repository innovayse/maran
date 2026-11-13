#!/usr/bin/env bash
# Step 89 (second half): put an authoritative DNS server on the host — installed,
# configurable, and NOT LISTENING.
#
# The promise this step makes, and the only one it makes: when it finishes, BIND 9 is on the
# machine, the directories it works in exist with the owners and modes the agent relies on,
# the control key exists, and Maran's own unit is installed, DISABLED and STOPPED. No socket
# has been bound at any point during the step, and none will be until an administrator turns
# DNS on. rules/security.md item 10 forbids a new listening port arriving by surprise; the
# spec sanctions the daemon, so what this step owes is that the daemon arrives switched off.
# It is the same promise, and the same shape, as installer/lib/89-ftps.sh.
#
# WHY THE MASK COMES FIRST, which is the whole ordering of `step_dns`: on the Debian family the
# package's postinst starts the daemon under the distribution's own configuration, which
# recurses for the local networks and listens on every address, before Maran's unit exists. An
# installer that installed the package and then disabled the unit would have run an open
# resolver for the length of the install on a machine that is by definition reachable from the
# internet. `systemctl mask` replaces the unit with a symbolic link to /dev/null in
# /etc/systemd/system, which shadows the distribution's own file: systemd refuses to start a
# masked unit, so the postinst's start binds nothing. The mask stays — this panel never runs the
# packaged unit; it runs maran-dns.service against its own configuration. The ordering is made
# mechanical the way the FTPS step makes it: `install_dns_packages` REFUSES to run until the
# masks are observably in place.
#
# UNOBSERVED HERE, and stated rather than papered over: whether the postinst would have started
# the daemon (an image build boots no systemd, and a container's policy-rc.d refuses the start
# for its own reasons), so the artifact observed is the mask link and not the live fact; whether
# each binary path in dns_binaries_for_family exists in the packages as named (this step REQUIRES
# them after the install and fails by name, so a wrong cell is loud); and the AppArmor and SELinux
# rules written below (nothing here has run a named under them — docs/superpowers/notes/
# 2026-11-07-dns-module-threat-note.md section 9).
#
# What this step deliberately does NOT do:
#
# - It does not enable or start maran-dns.service and it opens no port, in the firewall or
#   anywhere else. Turning DNS on is an administrator action in the panel.
# - It does not write /etc/maran/bind/named.conf. The configuration is rendered by the agent
#   through ops::safe_write; a config written by two owners has no owner. This step creates the
#   directory it lands in, the control key and the unit's one-variable environment file.
# - It does not create a zone. Zones are files the agent writes into the zones directory.
#
# Every action is idempotent, because installers get re-run: directories restate owner and
# mode, the key is generated only when absent (rotating it under a running daemon would lock the
# agent out until the next restart), and the files this step owns outright are rewritten whole.
#
# Threat note (written before this file existed, as rules/security.md requires):
# docs/superpowers/notes/2026-11-07-dns-module-threat-note.md. The second-reviewer requirement
# it records is OUTSTANDING, and this branch must not merge to `main` until a human has read it.
set -euo pipefail

# The agent's configuration directory for this daemon, matching
# `AgentPaths::DNS_CONFIG_DIRECTORY`. root:<daemon group> 0750: the daemon's group reads the key
# and the configuration, nobody else but root does.
readonly MARAN_DNS_CONFIG_DIR="/etc/maran/bind"

# The parent of the directory above. Created by step 60 as `root:<service group> 0750` on a real
# install; named here only so that this step, run on its own, does not have to invent a mode for
# a directory somebody else owns.
readonly MARAN_DNS_CONFIG_PARENT="/etc/maran"

# The key file the control channel and `rndc` both read, matching
# `AgentPaths::DNS_CONTROL_KEY_PATH`, and the key's NAME inside it, matching
# `AgentPaths::DNS_CONTROL_KEY_NAME`. Generated here with openssl so that no BIND tool path is
# involved.
readonly MARAN_DNS_CONTROL_KEY="${MARAN_DNS_CONFIG_DIR}/rndc.key"
readonly MARAN_DNS_CONTROL_KEY_NAME="maran-rndc"

# The one-variable environment file maran-dns.service reads (EnvironmentFile=), holding the user
# the daemon drops to. A systemd unit has no per-family spelling, so this is where the family's
# answer reaches it.
readonly MARAN_DNS_ENVIRONMENT_FILE="${MARAN_DNS_CONFIG_DIR}/dns.env"

# Maran's own unit, and where units live. Named MARAN_DNS_UNIT_DIR and not MARAN_UNIT_DIR:
# run_step sources every step into the SAME shell, and installer/lib/70-services.sh already
# declares that name `readonly`.
readonly MARAN_DNS_UNIT_NAME="maran-dns.service"
readonly MARAN_DNS_UNIT_DIR="/etc/systemd/system"

# The names, below the daemon's state directory, of the two directories signed zones use
# (issue #452), matching `DnsFacts::dns_signed_zone_directory` and `::dns_key_directory`, which
# spell them `<state>/signed-zones` and `<state>/keys`. Compared with the adapter by
# docker/polygon/asserts/parts/dns.sh.
readonly MARAN_DNS_SIGNED_ZONES_SUBDIR="signed-zones"
readonly MARAN_DNS_KEYS_SUBDIR="keys"

# The local override file the Ubuntu AppArmor profile for named includes, and the marker lines
# of the block this step owns inside it. Anything outside the block is the operator's and is
# never touched.
readonly MARAN_DNS_APPARMOR_PROFILE="/etc/apparmor.d/usr.sbin.named"
readonly MARAN_DNS_APPARMOR_LOCAL="/etc/apparmor.d/local/usr.sbin.named"
readonly MARAN_DNS_APPARMOR_BEGIN="# BEGIN maran-dns (installer/lib/89-dns.sh) - rewritten on every install"
readonly MARAN_DNS_APPARMOR_END="# END maran-dns"

# dns_packages_for_family: the packages, per family and release.
#
# The ONE place a package name appears, so a rename stops the polygon image build and not a
# customer's install. The RHEL family on release 9 ships two BIND branches and the default one is
# 9.16; the 9.18 branch is the same version the Debian family runs, which gives every supported
# host one behaviour for response rate limiting, `rndc addzone` and signing policy
# (research_notes/dns-stack-inventory.md section 6). From release 10 the default package is 9.18.
#
# Public on purpose: the polygon images install the package from THIS list.
dns_packages_for_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "bind9 bind9-utils" ;;
    rhel)
      case "${MARAN_OS_VERSION_ID:-}" in
        9|9.*) echo "bind9.18 bind9.18-utils" ;;
        *)     echo "bind bind-utils" ;;
      esac
      ;;
    # UNVERIFIED: BIND is the package `bind` on openSUSE Leap 16 and carries named, the checkers and
    # rndc (`bind-utils` is the dig package and is not needed). Confirm with `rpm -ql bind | grep
    # -E 'sbin/(named|rndc)'` in the pinned image.
    suse) echo "bind" ;;
    *)
      echo "89-dns.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

# dns_packaged_units_for_family: every unit name the distribution's package can start the daemon
# under. The Debian family ships `named.service` with the alias `bind9.service`; masking only one
# of them would leave the other startable.
dns_packaged_units_for_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "named.service bind9.service" ;;
    rhel)   echo "named.service" ;;
    # UNVERIFIED: `named.service`, matching suse_dns_services.rs. Confirm with
    # `rpm -ql bind | grep service` in the pinned image.
    suse)   echo "named.service" ;;
    *)
      echo "89-dns.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

# dns_user_for_family / dns_state_directory_for_family: the identity the daemon drops to and the
# directory it works in, matching `DnsFacts::dns_user`, `::dns_group` (the same word on both
# families) and `::dns_state_directory`. Compared with the adapter by
# docker/polygon/asserts/parts/dns.sh, because a drift is silent by construction: the installer
# would create a directory the daemon cannot write and the agent would render a path nobody made.
dns_user_for_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "bind" ;;
    rhel)   echo "named" ;;
    # UNVERIFIED: user and group `named`, matching suse_dns_services.rs::dns_user. Confirm with
    # `getent passwd named` and `getent group named` after installing `bind` in the pinned image.
    suse)   echo "named" ;;
    *)
      echo "89-dns.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

dns_state_directory_for_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "/var/lib/bind/maran" ;;
    rhel)   echo "/var/named/maran" ;;
    # UNVERIFIED: a `maran` subdirectory of the daemon's own working directory /var/lib/named,
    # matching suse_dns_services.rs::dns_state_directory (zones, signed-zones and keys below it).
    # Confirm with `ls -Zd /var/lib/named` and an AVC check after the first `rndc addzone`.
    suse)   echo "/var/lib/named/maran" ;;
    *)
      echo "89-dns.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

# dns_binaries_for_family: the absolute paths this step REQUIRES to exist and be executable once
# the package is installed — the daemon, its configuration checker, its zone checker and its
# control client — matching `DnsFacts::dns_checkconf_binary`, `::dns_checkzone_binary` and
# `::dns_control_binary` and the ExecStart of maran-dns.service. UNMEASURED against the
# packages' file lists (research_notes/dns-stack-inventory.md section 2); this step fails by name
# when one is missing, so a wrong cell is a loud install failure and not a daemon that the agent
# silently cannot drive.
dns_binaries_for_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) echo "/usr/sbin/named /usr/bin/named-checkconf /usr/bin/named-checkzone /usr/sbin/rndc" ;;
    rhel)   echo "/usr/sbin/named /usr/sbin/named-checkconf /usr/sbin/named-checkzone /usr/sbin/rndc" ;;
    # Measured in the pinned Leap 16.1 image (`rpm -ql bind`): the daemon and rndc are in /usr/sbin, the
    # two checkers in /usr/bin, as on Debian. These are the paths suse_dns_services.rs answers.
    suse)   echo "/usr/sbin/named /usr/bin/named-checkconf /usr/bin/named-checkzone /usr/sbin/rndc" ;;
    *)
      echo "89-dns.sh: unsupported OS family '${MARAN_OS_FAMILY:-}'" >&2
      exit 1
      ;;
  esac
}

# packaged_dns_unit_is_masked <unit>: 0 when /etc/systemd/system/<unit> is a symbolic link to
# /dev/null, which is what a mask IS. Asked of the filesystem and not of systemd, so the answer is
# the same on a booted host and inside an image build where no init runs.
packaged_dns_unit_is_masked() {
  [ "$(readlink -- "${MARAN_DNS_UNIT_DIR}/$1" 2>/dev/null || true)" = "/dev/null" ]
}

# mask_packaged_dns: make the distribution's unit(s) unstartable BEFORE the package exists on the
# machine. Same discipline as mask_packaged_vsftpd: `systemctl mask` first, verified, made directly
# if the link is not there, and a REAL file at the path refused rather than deleted.
mask_packaged_dns() {
  local unit path
  for unit in $(dns_packaged_units_for_family); do
    path="${MARAN_DNS_UNIT_DIR}/${unit}"
    if [ -e "$path" ] && [ ! -L "$path" ]; then
      echo "89-dns.sh: ${path} is a real file, not a mask." >&2
      echo "  Something on this host has installed its own ${unit}. Maran will not delete it, and" >&2
      echo "  it will not install DNS while the packaged daemon can still be started with the" >&2
      echo "  distribution's own configuration. Move that unit aside and re-run the installer." >&2
      exit 1
    fi

    systemctl mask "$unit" >/dev/null 2>&1 || true

    if ! packaged_dns_unit_is_masked "$unit"; then
      install -d -o root -g root -m 0755 "$MARAN_DNS_UNIT_DIR"
      ln -sfn /dev/null "$path"
    fi

    if ! packaged_dns_unit_is_masked "$unit"; then
      echo "89-dns.sh: could not mask ${unit}; refusing to install the package, whose postinst" >&2
      echo "  would start it on the Debian family. Nothing was installed." >&2
      exit 1
    fi
  done
}

# install_dns_packages: the packages through the family's own manager — and never before the masks.
install_dns_packages() {
  local unit
  for unit in $(dns_packaged_units_for_family); do
    if ! packaged_dns_unit_is_masked "$unit"; then
      echo "89-dns.sh: ${unit} is NOT masked, so installing the BIND package would leave the" >&2
      echo "  distribution's own daemon startable — and on the Debian family its postinst starts" >&2
      echo "  it. mask_packaged_dns must run FIRST. Nothing has been installed." >&2
      exit 1
    fi
  done

  if ! command -v pkg_install >/dev/null 2>&1; then
    echo "89-dns.sh: pkg_install is not defined. It lives in installer/lib/20-dependencies.sh," >&2
    echo "  which install.sh sources before this step; source it too if you drive this step alone." >&2
    exit 1
  fi

  # shellcheck disable=SC2046
  pkg_install $(dns_packages_for_family)
}

# assert_dns_binaries_present: every program the agent and the unit name by absolute path must
# exist and be executable now. A package whose file list differs from dns_binaries_for_family
# fails HERE, by name, rather than on the first EnableDns.
assert_dns_binaries_present() {
  local binary missing=0
  for binary in $(dns_binaries_for_family); do
    if [ ! -x "$binary" ]; then
      echo "89-dns.sh: ${binary} is missing or not executable after the BIND install." >&2
      missing=1
    fi
  done
  if [ "$missing" -ne 0 ]; then
    echo "  dns_binaries_for_family (and DnsFacts in agent/crates/distro) name paths from the" >&2
    echo "  package's file list as recorded, not measured on this host. Correct both together." >&2
    exit 1
  fi
}

# assert_dns_identity_exists: the package creates the user and group the daemon drops to. They must
# exist before any directory below is given to them.
assert_dns_identity_exists() {
  local identity
  identity="$(dns_user_for_family)"
  if ! getent passwd "$identity" >/dev/null 2>&1 || ! getent group "$identity" >/dev/null 2>&1; then
    echo "89-dns.sh: the BIND package did not create the user and group '${identity}' that" >&2
    echo "  DnsFacts::dns_user and ::dns_group name. Nothing below can give a directory to them." >&2
    exit 1
  fi
}

# ensure_dns_writable_subdirectory <path> <group>: a directory of the daemon's state that the
# daemon itself writes into, `root:<group> 0770`.
#
# The state directory is group-writable, so the daemon's group can put a symbolic link or a
# directory of its own at this name; `install -d` would follow the link and chown and chmod
# whatever it points at, as root. So whatever occupies the path that is not a real directory owned
# by root is removed first (`rm -rf` on a link removes the link and never its target), and the
# directory is made afresh. What is lost by that is the daemon's own scratch files and a signed
# copy it rebuilds; the zone files and keys are re-sent by the panel's reconciler.
ensure_dns_writable_subdirectory() {
  local path="${1:?}" group="${2:?}"

  if [ -L "$path" ] \
    || { [ -e "$path" ] && [ ! -d "$path" ]; } \
    || { [ -d "$path" ] && [ "$(stat -c '%u' "$path")" -ne 0 ]; }; then
    rm -rf -- "$path"
  fi
  install -d -o root -g "$group" -m 0770 "$path"
}

# ensure_dns_directories: the configuration directory, the daemon's state directory and the
# directories below it, each with owner and mode restated rather than inherited from an umask.
#
#   /etc/maran/bind          root:<group> 0750  the daemon's group reads the key and the config
#   <state>                  root:<group> 0770  the daemon WRITES here: the zone list it keeps
#                                               itself (`rndc addzone`)
#   <state>/zones            root:<group> 0750  the agent writes zone files, the daemon reads
#                                               them and cannot change them
#   <state>/signed-zones     root:<group> 0770  zone files of SIGNED zones: the agent writes the
#                                               file, and inline signing makes the daemon write
#                                               the signed copy and its journal beside it
#   <state>/keys             root:<group> 0770  a signed zone's key files: the agent writes
#                                               K<zone>.+013+<tag>.key and .private, and the
#                                               daemon writes the .state file of each key
#
# The two writable directories exist so that the plain zones directory can stay read-only to the
# daemon for every zone that is not signed.
#
# `install -d` alone would exit 0 on a path somebody else had put there and would follow a symbolic
# link, so whatever occupies a state path is removed first unless it is already a real directory
# owned by root: only root can create a root-owned directory under /var/lib or /var, which is what
# makes the exception not a weakening.
ensure_dns_directories() {
  local group state
  group="$(dns_user_for_family)"
  state="$(dns_state_directory_for_family)"

  if [ -L "$state" ] \
    || { [ -e "$state" ] && [ ! -d "$state" ]; } \
    || { [ -d "$state" ] && [ "$(stat -c '%u' "$state")" -ne 0 ]; }; then
    rm -rf -- "$state"
  fi
  install -d -o root -g "$group" -m 0770 "$state"
  install -d -o root -g "$group" -m 0750 "${state}/zones"
  ensure_dns_writable_subdirectory "${state}/${MARAN_DNS_SIGNED_ZONES_SUBDIR}" "$group"
  ensure_dns_writable_subdirectory "${state}/${MARAN_DNS_KEYS_SUBDIR}" "$group"

  if [ ! -d "$MARAN_DNS_CONFIG_PARENT" ]; then
    install -d -o root -g root -m 0750 "$MARAN_DNS_CONFIG_PARENT"
  fi
  install -d -o root -g "$group" -m 0750 "$MARAN_DNS_CONFIG_DIR"
}

# ensure_dns_control_key: the shared secret of the control channel, in the daemon's own syntax.
#
# Generated only when absent. Written under umask 077 and renamed into place, so no reader sees it
# half written or with a wider mode than it ends with, and its content is never echoed. 32 random
# bytes, base64: the key is hmac-sha256.
ensure_dns_control_key() {
  local group staged secret
  group="$(dns_user_for_family)"

  if [ ! -s "$MARAN_DNS_CONTROL_KEY" ]; then
    staged="${MARAN_DNS_CONTROL_KEY}.maran-staging"
    secret="$(openssl rand -base64 32)"
    (
      umask 077
      printf 'key "%s" {\n    algorithm hmac-sha256;\n    secret "%s";\n};\n' \
        "$MARAN_DNS_CONTROL_KEY_NAME" "$secret" > "$staged"
    )
    mv -f -- "$staged" "$MARAN_DNS_CONTROL_KEY"
  fi
  chown root:"$group" "$MARAN_DNS_CONTROL_KEY"
  chmod 0640 "$MARAN_DNS_CONTROL_KEY"
}

# install_dns_environment_file: the one-variable file maran-dns.service expands in its ExecStart.
# Rewritten whole on every run, root:root 0644; it names a user and holds no secret.
install_dns_environment_file() {
  local staged="${MARAN_DNS_ENVIRONMENT_FILE}.maran-staging"
  printf 'MARAN_DNS_USER=%s\n' "$(dns_user_for_family)" > "$staged"
  chown root:root "$staged"
  chmod 0644 "$staged"
  mv -f -- "$staged" "$MARAN_DNS_ENVIRONMENT_FILE"
}

# install_dns_unit: Maran's own unit, root:root 0644. Not enabled and not started.
install_dns_unit() {
  local source="${SCRIPT_DIR:-}/systemd/maran-dns.service"
  if [ ! -r "$source" ]; then
    echo "89-dns.sh: ${source} is missing from the installer payload. Aborting." >&2
    exit 1
  fi
  install -D -o root -g root -m 0644 "$source" "${MARAN_DNS_UNIT_DIR}/${MARAN_DNS_UNIT_NAME}"
}

# allow_dns_under_apparmor: on a host whose AppArmor profile confines /usr/sbin/named, let the
# daemon read the agent's configuration directory and write its state directory.
#
# The profile attaches by executable path, so maran-dns.service running the same binary is
# confined by the same profile whatever the unit is called; without these rules a named under it
# cannot read /etc/maran/bind or write its zone list, which is the single most likely first-run
# failure (research_notes/dns-stack-inventory.md sections 2.2 and 6). The rules live in a block
# this step owns inside the profile's local override file, replaced whole on every run; the
# distribution's own profile is never edited. Does nothing — and says so — on a host with no such
# profile.
allow_dns_under_apparmor() {
  local state staged signed_zones keys
  if [ ! -f "$MARAN_DNS_APPARMOR_PROFILE" ]; then
    echo "AppArmor: no ${MARAN_DNS_APPARMOR_PROFILE} on this host, so no rule was written for named."
    return 0
  fi

  state="$(dns_state_directory_for_family)"
  signed_zones="${state}/${MARAN_DNS_SIGNED_ZONES_SUBDIR}"
  keys="${state}/${MARAN_DNS_KEYS_SUBDIR}"
  install -d -o root -g root -m 0755 "$(dirname -- "$MARAN_DNS_APPARMOR_LOCAL")"
  [ -f "$MARAN_DNS_APPARMOR_LOCAL" ] || : > "$MARAN_DNS_APPARMOR_LOCAL"

  staged="${MARAN_DNS_APPARMOR_LOCAL}.maran-staging"
  {
    awk -v begin="$MARAN_DNS_APPARMOR_BEGIN" -v end="$MARAN_DNS_APPARMOR_END" '
      $0 == begin { skipping = 1; next }
      skipping && $0 == end { skipping = 0; next }
      !skipping { print }
    ' "$MARAN_DNS_APPARMOR_LOCAL"
    printf '%s\n' "$MARAN_DNS_APPARMOR_BEGIN"
    printf '%s/ r,\n%s/** r,\n' "$MARAN_DNS_CONFIG_DIR" "$MARAN_DNS_CONFIG_DIR"
    printf '%s/ rw,\n%s/** rwk,\n' "$state" "$state"
    # The two directories signed zones use (issue #452). The state rule above already reaches
    # them (`**` matches below any depth); they are stated again so that narrowing the state rule
    # later cannot silently stop the daemon writing its signed copies, journals and key state.
    printf '%s/ rw,\n%s/** rwk,\n' "$signed_zones" "$signed_zones"
    printf '%s/ rw,\n%s/** rwk,\n' "$keys" "$keys"
    printf '%s\n' "$MARAN_DNS_APPARMOR_END"
  } > "$staged"
  chown root:root "$staged"
  chmod 0644 "$staged"
  mv -f -- "$staged" "$MARAN_DNS_APPARMOR_LOCAL"

  if command -v apparmor_parser >/dev/null 2>&1; then
    apparmor_parser -r "$MARAN_DNS_APPARMOR_PROFILE" \
      || echo "AppArmor: apparmor_parser could not reload the named profile; the rules apply at the next load." >&2
  fi
  echo "AppArmor: wrote the maran-dns block into ${MARAN_DNS_APPARMOR_LOCAL}." \
       "UNOBSERVED HERE: a named running under it — nothing in this step starts one."
}

# label_dns_under_selinux: on an enforcing host, give the daemon's state directory the type it may
# WRITE and the zones directory the type it may only READ.
#
# The state directory sits below /var/named and would inherit `named_zone_t`, which `named_t` writes
# only with the `named_write_master_zones` boolean; `named_cache_t` is writable by default, and is
# what the zone list needs. The zones directory keeps `named_zone_t`: the daemon reads zone files
# the agent wrote and never changes them.
#
# The signed-zones and keys directories (issue #452) need NO line of their own: the first rule
# below, `<state>(/.*)?`, matches `<state>/signed-zones` and `<state>/keys` and everything under
# them, so they are `named_cache_t` and the daemon writes them by default. The second rule,
# `<state>/zones(/.*)?`, does not match them: the regular expressions are anchored at both ends and
# `signed-zones` is not `zones`. The echo at the end prints both labels as they are on disk. `semanage fcontext -a` fails on a rule that already
# exists, so a re-run falls back to `-m`. Does nothing — and says so — where SELinux is not
# enforcing.
label_dns_under_selinux() {
  local state
  if ! command -v selinuxenabled >/dev/null 2>&1 || ! selinuxenabled; then
    echo "SELinux: not enabled on this host, so no file context was set for named."
    return 0
  fi
  if ! command -v semanage >/dev/null 2>&1; then
    echo "89-dns.sh: SELinux is enabled but semanage is not installed, so named cannot be" >&2
    echo "  allowed to write its state directory. Install policycoreutils-python-utils and re-run." >&2
    exit 1
  fi

  state="$(dns_state_directory_for_family)"
  semanage fcontext -a -t named_cache_t "${state}(/.*)?" 2>/dev/null \
    || semanage fcontext -m -t named_cache_t "${state}(/.*)?"
  semanage fcontext -a -t named_zone_t "${state}/zones(/.*)?" 2>/dev/null \
    || semanage fcontext -m -t named_zone_t "${state}/zones(/.*)?"
  restorecon -R "$state"
  echo "SELinux: ${state} is $(stat -c '%C' "$state" 2>/dev/null || echo unknown)," \
       "${state}/zones is $(stat -c '%C' "${state}/zones" 2>/dev/null || echo unknown)," \
       "${state}/${MARAN_DNS_SIGNED_ZONES_SUBDIR} is $(stat -c '%C' "${state}/${MARAN_DNS_SIGNED_ZONES_SUBDIR}" 2>/dev/null || echo unknown)," \
       "${state}/${MARAN_DNS_KEYS_SUBDIR} is $(stat -c '%C' "${state}/${MARAN_DNS_KEYS_SUBDIR}" 2>/dev/null || echo unknown)." \
       "UNOBSERVED HERE: an AVC check of a running named (ausearch -m avc)."
}

# report_dns_is_installed_and_off: the mask is in place, the unit is not enabled, and nothing
# listens on the ports this feature uses. Fails loudly if any of the first two is untrue.
report_dns_is_installed_and_off() {
  local unit enabled_links
  for unit in $(dns_packaged_units_for_family); do
    if ! packaged_dns_unit_is_masked "$unit"; then
      echo "89-dns.sh: ${unit} is not masked after this step ran." >&2
      exit 1
    fi
    echo "Masked: ${MARAN_DNS_UNIT_DIR}/${unit} -> $(readlink -- "${MARAN_DNS_UNIT_DIR}/${unit}")"
  done

  enabled_links="$(find /etc/systemd/system -name "$MARAN_DNS_UNIT_NAME" -path '*.wants/*' 2>/dev/null || true)"
  if [ -n "$enabled_links" ]; then
    echo "89-dns.sh: ${MARAN_DNS_UNIT_NAME} is ENABLED (${enabled_links}). This step installs DNS" >&2
    echo "  switched off; enabling it is an administrator action taken in the panel." >&2
    exit 1
  fi
  echo "Not enabled: no .wants entry for ${MARAN_DNS_UNIT_NAME} under /etc/systemd/system."

  if command -v ss >/dev/null 2>&1; then
    ss -lntup 2>/dev/null | awk 'NR == 1 || $5 ~ /:(53|953)$/' || true
  fi
  echo "UNOBSERVED HERE: whether the postinst would have started the packaged daemon — no systemd" \
       "boots in an image build, so the listener lines above are green either way. The mask is" \
       "observed as a symlink instead, and the live fact is NOT yet observed: no polygon suite starts a named."
}

step_dns() {
  echo "Installing DNS (BIND 9), switched off..."
  # The order is the security property. Nothing below moves above the masks.
  mask_packaged_dns
  install_dns_packages
  assert_dns_binaries_present
  assert_dns_identity_exists
  ensure_dns_directories
  ensure_dns_control_key
  install_dns_environment_file
  allow_dns_under_apparmor
  label_dns_under_selinux
  install_dns_unit
  # The one daemon-reload, after the unit file is in place and before anything reports on it. It
  # enables nothing: a reload makes systemd read the unit, it does not want it.
  systemctl daemon-reload
  report_dns_is_installed_and_off
  echo "DNS is installed and OFF: ${MARAN_DNS_UNIT_NAME} is disabled and stopped, no port is open,"
  echo "  and the zones are written by the agent once an administrator turns DNS on in the panel."
  echo "  Opening port 53 (tcp and udp) in the firewall is a separate administrator action."
}
