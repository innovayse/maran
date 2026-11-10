#!/usr/bin/env bash
# Step 68: opt-in unattended OS SECURITY updates (issue #163). Threat note, written first:
# docs/superpowers/notes/2026-11-04-os-security-updates-threat-note.md (second review OUTSTANDING).
#
# This step is the distribution's OWN unattended-update mechanism, switched on and narrowed. It adds
# no Maran daemon, no listening port and no outbound call of Maran's own: the package manager
# contacts the mirrors it already uses for every manual run. The one exception to "no Maran-owned
# timer or unit" is the SUSE family, whose distribution ships no unattended updater: there the step
# writes a oneshot service and a daily timer that run only `zypper patch --category security`.
#
# With --os-security-updates absent (MARAN_OS_SECURITY_UPDATES=no, the default) the step prints one
# line and changes NOTHING: no package, no file, no timer. An unattended root job that changes
# software on a customer's host is a decision the operator makes, not one the installer makes.
#
# With it on, per family:
#
#   Debian family   installs `unattended-upgrades`, writes /etc/apt/apt.conf.d/52maran-os-security-updates
#                   (clears the distribution's default origin lists, then allows ONE origin: the
#                   security archive of this release; holds the packages in MARAN_OS_UPDATE_HOLD;
#                   Automatic-Reboot "false"; periodic lists + upgrade on) and enables the distribution's
#                   apt-daily.timer and apt-daily-upgrade.timer.
#   RHEL family     installs `dnf-automatic` and `yum-utils` (which ships `needs-restarting`), writes
#                   /etc/dnf/automatic.conf WHOLE (upgrade_type = security, apply_updates = yes, the hold
#                   list as `exclude=` under [base]) and enables dnf-automatic-install.timer.
#   SUSE family     (issue #59) is the ONE family whose distribution ships no unattended updater, so this
#                   step writes a small service and timer pair of its own, renders the hold list as a
#                   zypper locks file and a private zypp.conf that names it, and enables the timer. See
#                   "The SUSE mechanism" below for why it is built that way and not with `zypper addlock`.
#
# The SUSE mechanism, and why a persistent lock was refused. `zypper addlock 'nginx*'` is the obvious
# way to hold a package, and it is wrong here: a lock is global, so it would also stop the operator's
# manual `zypper update` (the hold is a policy about the UNATTENDED run only) and, worse, would make
# zypper refuse the later installs this product performs itself (the agent installs a PHP version on
# request, step 85 installs the database server), because a locked name cannot be installed either.
# So the lock list lives in a locks file that ONLY the unattended service reads: libzypp takes the
# location of zypp.conf from the ZYPP_CONF environment variable, and the private zypp.conf is a copy of
# the host's own with one key, `locksfile.path`, pointing at that file. The copy keeps the
# distribution's other settings (the kernel multiversion rule matters: without it a patch run would
# REPLACE the running kernel). Its cost is staleness: the copy is made when this step runs, so a later
# change to the stock zypp.conf reaches the unattended run on the next run of the installer, not before.
# UNVERIFIED (all of it; nothing about SUSE was measured by us): that libzypp honours ZYPP_CONF, that
# the `locksfile.path` key exists, the locks-file grammar, and that `zypper patch` skips a patch whose
# package is locked. The step OBSERVES the result instead of assuming it: `zypper locks` run under the
# private configuration must list every held pattern, or the step fails. Confirm by hand in the pinned
# Leap image with `ZYPP_CONF=/etc/zypp/maran-unattended-zypp.conf zypper --non-interactive locks`
# and `zypper --non-interactive patch --category security --dry-run`.
#
# What the hold list is, and is not (the variable, and the reason, live in 00-common.sh): a policy
# about WHEN a database engine, the web server, PHP or the FTP daemon is changed, never a control and
# never a package-manager-wide hold. It lives only in the unattended run's own configuration, so a
# manual `apt-get upgrade` or `dnf upgrade` still applies a held package and the panel still lists it.
#
# The only values that reach a rendered file are constants, the hold list (a constant, checked for
# shape) and, on the Debian family, the release codename read from the host's own /etc/os-release and
# checked against a positive shape before use (rules/security.md item 4: no caller-supplied value is
# written, so there is nothing to escape — and a damaged os-release cannot inject a line).
#
# Every file is written the way rules/rust.md "Config writes" fixes it for the agent, in shell: render
# into memory, temporary file BESIDE the target, owner and mode set before the rename, fsync the file
# and its directory, atomic rename, then validate by reading the artefact the system itself reads. An
# interrupted run leaves the previous file in place, because the rename is the last write.
#
# A file that is not ours is never replaced silently. The marker line below tells a previous Maran
# render from anything else; anything else is copied to `<file>.maran-previous` (root, 0600) first,
# and the step prints that it did.
#
# Idempotent: a second run renders identical bytes and converges.
set -euo pipefail

# The line every rendered file carries, in the file's own comment syntax. `uninstall.sh` spells the
# same words (it sources nothing from the installer); the polygon asserts that the two agree.
readonly MARAN_OS_UPDATES_MARKER="Maran-managed by installer/lib/68-os-updates.sh"

readonly MARAN_OS_UPDATES_APT_TARGET="/etc/apt/apt.conf.d/52maran-os-security-updates"
readonly MARAN_OS_UPDATES_DNF_TARGET="/etc/dnf/automatic.conf"
# The SUSE family's files (see the header). The service and the timer are this step's own because the
# distribution ships none; the private zypp.conf and the locks file are read only by that service.
readonly MARAN_OS_UPDATES_ZYPP_CONF="/etc/zypp/maran-unattended-zypp.conf"
readonly MARAN_OS_UPDATES_ZYPP_LOCKS="/etc/zypp/maran-unattended.locks"
readonly MARAN_OS_UPDATES_ZYPP_SERVICE="/etc/systemd/system/maran-os-security-updates.service"
readonly MARAN_OS_UPDATES_ZYPP_TIMER="/etc/systemd/system/maran-os-security-updates.timer"

# os_updates_hold_patterns: prints the hold list one validated pattern per line.
#
# The list is split with `read -a`, which does not expand a glob against the current directory the
# way an unquoted `for p in $LIST` would. Each pattern must be lowercase letters, digits and hyphens
# with at most one trailing `*`: dnf takes it as a glob as it stands, and the Debian rendering turns
# it into an anchored regular expression, which is only safe because no character in the allowed set
# is special to a regular expression or to the apt configuration syntax.
os_updates_hold_patterns() {
  local -a patterns=()
  local pattern
  read -r -a patterns <<< "${MARAN_OS_UPDATE_HOLD:?must be set by 00-common.sh}"
  [ "${#patterns[@]}" -gt 0 ] || {
    echo "68-os-updates.sh: MARAN_OS_UPDATE_HOLD is empty; refusing to write an unattended-update policy with no hold list." >&2
    exit 1
  }
  for pattern in "${patterns[@]}"; do
    [[ "$pattern" =~ ^[a-z0-9][a-z0-9-]*\*?$ ]] || {
      echo "68-os-updates.sh: '${pattern}' in MARAN_OS_UPDATE_HOLD is not a package pattern (lowercase letters, digits, '-', optional trailing '*')." >&2
      exit 1
    }
    printf '%s\n' "$pattern"
  done
}

# os_updates_codename_is_valid: whether $1 is a release codename this step will write.
#
# A positive shape — lowercase letters and digits, non-empty — rather than a list of bad characters,
# so a newline, a quote, a semicolon or a space cannot reach the rendered file. Public on purpose:
# the polygon calls it with a hostile value (must refuse) and with a real one (must accept).
os_updates_codename_is_valid() {
  [[ "${1:-}" =~ ^[a-z0-9]+$ ]]
}

# os_updates_debian_origin_pattern: prints the one Origins-Pattern entry for this host's security
# archive, read from the host's own /etc/os-release (never from an argument or the environment).
#
# The two distributions name their security archive differently, and a pattern of the wrong shape
# matches NOTHING (a safe failure, but a silent one that applies no update at all), so each is the
# shape that distribution's own unattended-upgrades default uses: Ubuntu's Release files carry the
# archive name `<codename>-security` in the Suite field, matched by `archive=`; Debian's carry it in
# the Codename field, matched by `codename=`. The origin word is a constant per ID, not read.
os_updates_debian_origin_pattern() {
  local id codename origin key
  id="$(. /etc/os-release && printf '%s' "${ID:-}")"
  codename="$(. /etc/os-release && printf '%s' "${VERSION_CODENAME:-}")"
  case "$id" in
    ubuntu) origin="Ubuntu"; key="archive" ;;
    debian) origin="Debian"; key="codename" ;;
    *)
      echo "68-os-updates.sh: /etc/os-release names '${id}', which has no security-update origin this step knows." >&2
      exit 1
      ;;
  esac
  os_updates_codename_is_valid "$codename" || {
    echo "68-os-updates.sh: the release codename in /etc/os-release is not lowercase letters and digits; refusing to write it into a configuration file." >&2
    exit 1
  }
  printf 'origin=%s,%s=%s-security\n' "$origin" "$key" "$codename"
}

# os_updates_apt_blacklist_entries: prints the hold list as the anchored regular expressions
# `Unattended-Upgrade::Package-Blacklist` takes (it matches names with `re.match`, so `php*` as
# written would also hold every name that merely begins `ph`).
#
# The list is read into a variable first, and not through a process substitution: an `exit` inside
# `< <(...)` ends only that subshell, which would turn a refused hold list into an EMPTY one and
# render a policy that holds nothing.
os_updates_apt_blacklist_entries() {
  local pattern patterns
  patterns="$(os_updates_hold_patterns)" || exit 1
  while IFS= read -r pattern; do
    case "$pattern" in
      *'*') printf '^%s.*\n' "${pattern%\*}" ;;
      *) printf '^%s$\n' "$pattern" ;;
    esac
  done <<< "$patterns"
}

# render_os_updates_apt_config: the Debian family's file, to stdout.
#
# `#clear` of BOTH origin lists comes first so the distribution's defaults (which include the
# non-security and ESM archives) cannot survive; the single allowed origin follows. Every value is
# read into a variable before anything is printed, so a refusal ends the render with no output.
render_os_updates_apt_config() {
  local origin entries entry
  origin="$(os_updates_debian_origin_pattern)" || exit 1
  entries="$(os_updates_apt_blacklist_entries)" || exit 1
  printf '// %s\n' "$MARAN_OS_UPDATES_MARKER"
  printf '// Security updates only, unattended; the reboot decision stays the operator'"'"'s.\n'
  printf '// Edits are overwritten by the next run of the installer step.\n'
  printf '#clear Unattended-Upgrade::Allowed-Origins;\n'
  printf '#clear Unattended-Upgrade::Origins-Pattern;\n'
  printf 'Unattended-Upgrade::Origins-Pattern {\n'
  printf '        "%s";\n' "$origin"
  printf '};\n'
  printf 'Unattended-Upgrade::Package-Blacklist {\n'
  while IFS= read -r entry; do
    printf '        "%s";\n' "$entry"
  done <<< "$entries"
  printf '};\n'
  printf 'Unattended-Upgrade::Automatic-Reboot "false";\n'
  printf 'APT::Periodic::Update-Package-Lists "1";\n'
  printf 'APT::Periodic::Unattended-Upgrade "1";\n'
}

# render_os_updates_dnf_config: the RHEL family's file, to stdout.
#
# `dnf-automatic` reads /etc/dnf/automatic.conf whole and has no drop-in directory, so this is the
# whole file. `reboot` is deliberately not set: the documented default is no reboot, and the step
# says it did not set it rather than assuming.
render_os_updates_dnf_config() {
  local pattern patterns excludes=""
  patterns="$(os_updates_hold_patterns)" || exit 1
  while IFS= read -r pattern; do
    excludes="${excludes:+${excludes} }${pattern}"
  done <<< "$patterns"
  printf '# %s\n' "$MARAN_OS_UPDATES_MARKER"
  printf '# Security updates only, unattended; the reboot decision stays the operator'"'"'s.\n'
  printf '# Edits are overwritten by the next run of the installer step.\n'
  printf '[commands]\n'
  printf 'upgrade_type = security\n'
  printf 'apply_updates = yes\n'
  printf 'random_sleep = 3600\n'
  printf '\n'
  printf '[emitters]\n'
  printf 'emit_via = stdio\n'
  printf '\n'
  printf '[base]\n'
  printf 'exclude=%s\n' "$excludes"
}

# os_updates_zypp_stock_conf: prints the path of the host's own zypp.conf, the file the private copy is
# made from. The administrator's /etc/zypp/zypp.conf wins when it exists; the distribution's default
# under /usr/etc (the location a usr-merged release moves vendor defaults to) is the fallback.
# UNVERIFIED: which of the two a Leap 16 image ships. Confirm with `ls -l /etc/zypp/zypp.conf
# /usr/etc/zypp/zypp.conf` in the pinned image. Neither present is a refusal, never a guess.
os_updates_zypp_stock_conf() {
  local candidate
  for candidate in /etc/zypp/zypp.conf /usr/etc/zypp/zypp.conf; do
    if [ -f "$candidate" ]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  echo "68-os-updates.sh: neither /etc/zypp/zypp.conf nor /usr/etc/zypp/zypp.conf exists; cannot derive the unattended zypper configuration." >&2
  exit 1
}

# render_os_updates_zypp_conf: the SUSE family's private zypp.conf, to stdout: the host's own file with
# every active `locksfile.path` line dropped and one added directly under `[main]`. Only a constant
# path is written, so nothing from the host can reach a line of it except the stock file's own bytes.
render_os_updates_zypp_conf() {
  local stock
  stock="$(os_updates_zypp_stock_conf)" || exit 1
  printf '# %s\n' "$MARAN_OS_UPDATES_MARKER"
  printf '# A copy of %s with locksfile.path pointing at the unattended hold list.\n' "$stock"
  printf '# Read only by maran-os-security-updates.service (ZYPP_CONF); edits are overwritten by the next run of the installer step.\n'
  awk -v locks="$MARAN_OS_UPDATES_ZYPP_LOCKS" '
    /^[[:space:]]*locksfile\.path[[:space:]]*=/ { next }
    { print }
    /^\[main\][[:space:]]*$/ { print "locksfile.path = " locks }
  ' "$stock"
}

# render_os_updates_zypp_locks: the SUSE family's hold list as a zypper locks file, to stdout: one
# glob lock per validated pattern. UNVERIFIED grammar (`type`, `match_type`, `case_sensitive`,
# `solvable_name`, entries separated by a blank line), read from `man zypper` ("locks"); confirm with
# `ZYPP_CONF=... zypper --non-interactive locks`, which the step runs and checks.
render_os_updates_zypp_locks() {
  local pattern patterns
  patterns="$(os_updates_hold_patterns)" || exit 1
  printf '# %s\n' "$MARAN_OS_UPDATES_MARKER"
  printf '# Packages held from the unattended security run only; a manual zypper run still applies them.\n'
  while IFS= read -r pattern; do
    printf '\n'
    printf 'type: package\n'
    printf 'match_type: glob\n'
    printf 'case_sensitive: on\n'
    printf 'solvable_name: %s\n' "$pattern"
  done <<< "$patterns"
}

# render_os_updates_zypp_service / _timer: the SUSE family's unit pair, to stdout. Constants only.
# SuccessExitStatus lists zypper's "patches were applied, a reboot / a zypper restart is advised"
# statuses (102, 103; 100 and 101 for "patches available"); none of them reboots anything, and the
# service has no hardening directive because a package manager has to write all of /usr.
# UNVERIFIED exit statuses, read from `man zypper` ("EXIT CODES"); confirm there in the pinned image.
render_os_updates_zypp_service() {
  printf '# %s\n' "$MARAN_OS_UPDATES_MARKER"
  printf '[Unit]\n'
  printf 'Description=Maran unattended OS security patches (zypper)\n'
  printf 'Wants=network-online.target\n'
  printf 'After=network-online.target\n'
  printf '\n[Service]\n'
  printf 'Type=oneshot\n'
  printf 'Environment=ZYPP_CONF=%s\n' "$MARAN_OS_UPDATES_ZYPP_CONF"
  printf 'ExecStart=/usr/bin/zypper --non-interactive patch --category security\n'
  printf 'SuccessExitStatus=100 101 102 103\n'
}

# render_os_updates_zypp_timer: see render_os_updates_zypp_service.
render_os_updates_zypp_timer() {
  printf '# %s\n' "$MARAN_OS_UPDATES_MARKER"
  printf '[Unit]\n'
  printf 'Description=Daily unattended OS security patches (Maran)\n'
  printf '\n[Timer]\n'
  printf 'OnCalendar=daily\n'
  printf 'RandomizedDelaySec=1h\n'
  printf 'Persistent=true\n'
  printf '\n[Install]\n'
  printf 'WantedBy=timers.target\n'
}

# os_updates_keep_previous: copies $1 to `<$1>.maran-previous` (root, 0600) when it exists and is not
# a previous Maran render, and says so. Never overwrites an earlier copy: a name that exists gets a
# UTC stamp, so the first file the operator had survives every re-run.
os_updates_keep_previous() {
  local target="$1" kept
  [ -f "$target" ] || return 0
  if grep -qF -- "$MARAN_OS_UPDATES_MARKER" "$target"; then
    return 0
  fi
  kept="${target}.maran-previous"
  [ ! -e "$kept" ] || kept="${kept}.$(date -u '+%Y%m%dT%H%M%SZ')"
  install -m 0600 -o root -g root "$target" "$kept" \
    || { echo "68-os-updates.sh: could not keep a copy of ${target}; it was not replaced." >&2; exit 1; }
  sync "$kept"
  echo "NOTE: ${target} was not written by Maran and is replaced; the previous bytes are kept in ${kept} (root, 0600)."
}

# os_updates_write: the config-write protocol in shell. $1 target, $2 rendered content.
#
# Temporary file beside the target (same directory, so the rename is atomic), owner and mode set on
# it BEFORE the rename (no moment in which the target is more permissive or foreign-owned), fsync of
# the file and then of its directory, atomic rename last. A failure removes the temporary file and
# leaves the target as it was. A target that already holds exactly this content is left untouched.
os_updates_write() {
  local target="$1" content="$2" tmp
  if [ -f "$target" ] && cmp -s -- "$target" <(printf '%s\n' "$content"); then
    echo "${target} already holds this policy."
    return 0
  fi
  os_updates_keep_previous "$target"
  tmp="$(mktemp "${target}.XXXXXX")" \
    || { echo "68-os-updates.sh: could not create a temporary file beside ${target}." >&2; exit 1; }
  if ! { printf '%s\n' "$content" > "$tmp" \
         && chown root:root "$tmp" \
         && chmod 0644 "$tmp" \
         && sync "$tmp" \
         && mv -f -- "$tmp" "$target" \
         && sync "$(dirname -- "$target")"; }; then
    rm -f -- "$tmp"
    echo "68-os-updates.sh: writing ${target} failed; the previous file is in place." >&2
    exit 1
  fi
}

# observe_os_updates_apt_config: validation on the Debian family, by the artefact apt itself reads.
#
# `apt-config dump` is apt's own answer to "what do you read", across every file in apt.conf.d, so it
# sees a later file that adds an origin back — which a grep of our own file could never see. It must
# show, with the values written: the single allowed origin and NO entry left in either origin list,
# every held pattern, Automatic-Reboot false and the two periodic switches. `unattended-upgrade
# --dry-run` is not run: it contacts the mirrors.
observe_os_updates_apt_config() {
  local dump origin entries entry bad=0 line
  dump="$(apt-config dump)" \
    || { echo "68-os-updates.sh: 'apt-config dump' failed; the policy was not observed." >&2; exit 1; }
  origin="$(os_updates_debian_origin_pattern)" || exit 1
  entries="$(os_updates_apt_blacklist_entries)" || exit 1

  # Exactly one origin pattern, and it is ours; the older Allowed-Origins list must be empty.
  line="$(printf '%s\n' "$dump" | grep -E '^Unattended-Upgrade::Origins-Pattern:: ' || true)"
  [ "$line" = "Unattended-Upgrade::Origins-Pattern:: \"${origin}\";" ] || {
    echo "68-os-updates.sh: apt's effective Origins-Pattern is not exactly '${origin}':" >&2
    printf '%s\n' "${line:-  (no entry)}" >&2
    bad=1
  }
  line="$(printf '%s\n' "$dump" | grep -E '^Unattended-Upgrade::Allowed-Origins:: ' || true)"
  [ -z "$line" ] || {
    echo "68-os-updates.sh: apt's effective Allowed-Origins is not empty; another file in /etc/apt/apt.conf.d adds origins back:" >&2
    printf '%s\n' "$line" >&2
    bad=1
  }
  while IFS= read -r entry; do
    grep -qFx "Unattended-Upgrade::Package-Blacklist:: \"${entry}\";" <<<"$dump" || {
      echo "68-os-updates.sh: apt's effective Package-Blacklist lacks '${entry}'." >&2
      bad=1
    }
  done <<< "$entries"
  for line in 'Unattended-Upgrade::Automatic-Reboot "false";' \
              'APT::Periodic::Update-Package-Lists "1";' \
              'APT::Periodic::Unattended-Upgrade "1";'; do
    grep -qFx -- "$line" <<<"$dump" || {
      echo "68-os-updates.sh: apt's effective configuration lacks: ${line}" >&2
      bad=1
    }
  done
  [ "$bad" -eq 0 ] || exit 1
  echo "Observed with 'apt-config dump': one security origin, the hold list, Automatic-Reboot false, periodic upgrades on."
  echo "UNOBSERVED HERE: a real security run (no mirror in this step has a security update pending to apply or to hold)."
}

# observe_os_updates_dnf_config: the RHEL family has no offline validator for dnf-automatic, so the
# file is read back from the name dnf reads it by and compared byte for byte with the render. That
# proves what was written, not how dnf-automatic will interpret it, and the output says so.
observe_os_updates_dnf_config() {
  local content="$1"
  cmp -s -- "$MARAN_OS_UPDATES_DNF_TARGET" <(printf '%s\n' "$content") || {
    echo "68-os-updates.sh: ${MARAN_OS_UPDATES_DNF_TARGET} does not read back as the rendered policy." >&2
    exit 1
  }
  echo "Observed: ${MARAN_OS_UPDATES_DNF_TARGET} reads back byte for byte as the render; no 'reboot' key is set (dnf-automatic's documented default is no reboot; not observed)."
  echo "UNOBSERVED HERE: a real security run, and dnf-automatic's own reading of the file (it has no offline validator)."
}

# observe_os_updates_zypp_config: the SUSE family's validation, by what zypper itself reports under the
# private configuration. `zypper locks` run with ZYPP_CONF set must list every held pattern (this is
# what proves both that libzypp read the private file and that the locks file parsed), and the unit
# must carry the same ZYPP_CONF in the environment systemd will give it.
observe_os_updates_zypp_config() {
  local listing patterns pattern bad=0 env_line
  patterns="$(os_updates_hold_patterns)" || exit 1
  listing="$(ZYPP_CONF="$MARAN_OS_UPDATES_ZYPP_CONF" zypper --non-interactive locks)" \
    || { echo "68-os-updates.sh: 'zypper locks' failed under ${MARAN_OS_UPDATES_ZYPP_CONF}; the hold list was not observed." >&2; exit 1; }
  while IFS= read -r pattern; do
    grep -qF -- "$pattern" <<<"$listing" || {
      echo "68-os-updates.sh: zypper's effective lock list (under ${MARAN_OS_UPDATES_ZYPP_CONF}) lacks '${pattern}'." >&2
      bad=1
    }
  done <<< "$patterns"
  env_line="$(systemctl show maran-os-security-updates.service --property=Environment --value)" \
    || { echo "68-os-updates.sh: could not read the service's environment from systemd." >&2; exit 1; }
  grep -qF -- "ZYPP_CONF=${MARAN_OS_UPDATES_ZYPP_CONF}" <<<"$env_line" || {
    echo "68-os-updates.sh: systemd does not give maran-os-security-updates.service ZYPP_CONF=${MARAN_OS_UPDATES_ZYPP_CONF}." >&2
    bad=1
  }
  [ "$bad" -eq 0 ] || exit 1
  echo "Observed with 'zypper locks' under the private configuration: every held pattern is listed; the service carries ZYPP_CONF."
  echo "UNOBSERVED HERE: a real security run (no mirror in this step has a security patch pending to apply or to hold)."
}

# enable_os_updates_timers: enables the timers; on the Debian and RHEL families they are the distribution's
# own and this step writes no unit, on the SUSE family it is the timer written above.
# The argv is constants, and the result is read back with `is-enabled` rather than trusted.
enable_os_updates_timers() {
  local -a timers=()
  local timer
  case "$MARAN_OS_FAMILY" in
    debian) timers=(apt-daily.timer apt-daily-upgrade.timer) ;;
    rhel) timers=(dnf-automatic-install.timer) ;;
    suse) timers=(maran-os-security-updates.timer) ;;
  esac
  systemctl enable --now "${timers[@]}"
  for timer in "${timers[@]}"; do
    systemctl is-enabled "$timer" >/dev/null 2>&1 || {
      echo "68-os-updates.sh: ${timer} is not enabled after 'systemctl enable --now'." >&2
      exit 1
    }
  done
  echo "Enabled timer(s): ${timers[*]}"
}

# step_os_updates: see the file header.
step_os_updates() {
  local enabled="${MARAN_OS_SECURITY_UPDATES:-no}" content
  case "$enabled" in
    yes) ;;
    no)
      echo "Unattended OS security updates: skipped (opt-in: --os-security-updates)"
      return 0
      ;;
    *)
      echo "68-os-updates.sh: MARAN_OS_SECURITY_UPDATES must be yes or no, not '${enabled}'." >&2
      exit 1
      ;;
  esac

  echo "Enabling unattended OS security updates (held from the unattended run: ${MARAN_OS_UPDATE_HOLD:?must be set by 00-common.sh})..."
  case "$MARAN_OS_FAMILY" in
    debian)
      pkg_install unattended-upgrades
      content="$(render_os_updates_apt_config)"
      os_updates_write "$MARAN_OS_UPDATES_APT_TARGET" "$content"
      observe_os_updates_apt_config
      ;;
    rhel)
      pkg_install dnf-automatic yum-utils
      content="$(render_os_updates_dnf_config)"
      os_updates_write "$MARAN_OS_UPDATES_DNF_TARGET" "$content"
      observe_os_updates_dnf_config "$content"
      ;;
    suse)
      # Nothing to install: zypper is the package manager itself. Written in dependency order (the
      # locks file and the private configuration before the service that reads them); each goes
      # through os_updates_write, so none is replaced non-atomically or without keeping a foreign file.
      os_updates_write "$MARAN_OS_UPDATES_ZYPP_LOCKS" "$(render_os_updates_zypp_locks)"
      os_updates_write "$MARAN_OS_UPDATES_ZYPP_CONF" "$(render_os_updates_zypp_conf)"
      os_updates_write "$MARAN_OS_UPDATES_ZYPP_SERVICE" "$(render_os_updates_zypp_service)"
      os_updates_write "$MARAN_OS_UPDATES_ZYPP_TIMER" "$(render_os_updates_zypp_timer)"
      systemctl daemon-reload
      observe_os_updates_zypp_config
      ;;
    *)
      echo "68-os-updates.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
  enable_os_updates_timers
  echo "Held packages stay listed as pending in the panel and are applied by a manual apt-get/dnf/zypper run; the server is never rebooted unattended."
}
