#!/usr/bin/env bash
# Step 87: put a working firewall on the host — the nftables package, the two ruleset
# files the agent renders, the include lines that load them at boot, and a running
# service with our table actually in it.
#
# The ordering in this file is the whole design, and every part of it exists because the
# alternative was measured and was worse:
#
# - BOTH files are seeded BEFORE any include mentions them. `nft -f` on an include whose
#   target is missing is not a warning, it is a hard error that aborts the entire load
#   (`Error: File not found`, rc 1), so a host that wired one include and wrote one file
#   comes up from its next boot with nftables.service FAILED and NO FIREWALL AT ALL —
#   not a partial one. Seeding first makes that unreachable.
#
# - The bans file is included FIRST, because file order is load order and the bans table
#   must exist before anything the panel later adds elements to it.
#
# - The ruleset text is rendered BY THE AGENT, never written here. `maran-agent
#   render-firewall-ruleset` and `render-firewall-bans` print the same templates the agent
#   applies at runtime, so the seed and every later mutation come from one source. A copy
#   of that text in shell would be a second source, and the first divergence between them
#   is a firewall that changes shape the first time an administrator touches it.
#
# - The service is STARTED and then looked at. `systemctl enable --now` reports success
#   for a unit whose ExecStart failed on a bad include, so this step asks the kernel
#   whether `table inet maran` is really there.
#
# - firewalld is disabled LAST, after that question has been answered yes. Everything
#   above it can abort, and a step that had already stopped the host's working firewall
#   leaves a RHEL machine wide open with nothing said about it. Two firewalls for the
#   length of three lines is strictly more closed than none for the length of an install.
#
# Idempotent like every step beside it: packages are no-ops when installed, the include
# block is delimited by markers and replaced rather than appended, and the rendered files
# are overwritten with the same bytes. Running it twice converges and reports.
set -euo pipefail

# The agent binary, at the path 50-artifacts.sh unpacks it to and the systemd unit
# executes. Named here so this step renders through the very binary the host will run,
# rather than through whatever `maran-agent` might be on the installer's PATH.
readonly MARAN_FIREWALL_AGENT="/usr/local/maran/agent/maran-agent"

# The `nft` binary, spelled the way `DistroAdapter::nft_binary()` spells it on both
# families. Checked rather than assumed, for the reason 85-mysql.sh checks its client: a
# tool that works for the installer and is missing where the agent looks for it is a
# defect that surfaces on a customer's first firewall change.
readonly MARAN_NFT_BINARY="/usr/sbin/nft"

# The two files the agent owns, matching `AgentPaths::nftables_ruleset_path()` and
# `nftables_bans_path()`. An agent-owned location identical on both families, which is
# why they are constants here and not an adapter question — what differs per family is
# only WHERE the include goes.
readonly MARAN_FIREWALL_CONFIG_DIR="/etc/maran"
readonly MARAN_FIREWALL_RULESET="${MARAN_FIREWALL_CONFIG_DIR}/firewall.nft"
readonly MARAN_FIREWALL_BANS="${MARAN_FIREWALL_CONFIG_DIR}/firewall-bans.nft"

# The panel's configuration file, read here for the two host facts 60-config.sh detected.
readonly MARAN_FIREWALL_PANEL_ENV="${MARAN_FIREWALL_CONFIG_DIR}/panel.env"

# The service unit, matching `DistroAdapter::firewall_service()`: both families register
# the same name for the same upstream service.
readonly MARAN_FIREWALL_SERVICE="nftables"

# The marker recording that THIS installer enabled the firewall service, so the
# uninstaller can put the host back the way it found it. Its absence is a statement too:
# the service was already enabled before Maran arrived, and disabling it on uninstall
# would take away a firewall the operator had before us.
readonly MARAN_FIREWALL_ENABLED_MARKER="${MARAN_FIREWALL_CONFIG_DIR}/firewall-service-enabled-by-maran"

# The marker comments delimiting our include block. They are the whole idempotency
# mechanism: the block between them is deleted and rewritten on every run, so the file
# converges on one current block whether it had none, one, or an older one.
readonly MARAN_FIREWALL_BEGIN_MARKER="# BEGIN Maran firewall — managed by installer/lib/87-firewall.sh,\
 do not edit between markers"
readonly MARAN_FIREWALL_END_MARKER="# END Maran firewall"

# What the markers are RECOGNISED by, as opposed to what they are written as. The prefixes,
# not the full lines, and the uninstaller matches the same way: a host installed last month
# carries whatever marker text shipped then, and an exact match against today's wording would
# not find that block — so the step would append a second one beside it and the file would grow
# a block per release. The prefix is the part that may never change; the tail after it is free
# to say something better next year.
readonly MARAN_FIREWALL_BEGIN_PREFIX="# BEGIN Maran firewall"
readonly MARAN_FIREWALL_END_PREFIX="# END Maran firewall"

# firewall_packages_for_family: the package carrying `nft` and the service unit. Both
# families ship it under the same name, which is a fact about these two distributions
# rather than a rule — asked per family all the same, like every other package list here.
#
# Public on purpose: the polygon images install nftables for their own suites, and taking
# the name from THIS function rather than a literal of their own means a package name that
# stops being right stops both image builds.
firewall_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "nftables" ;;
    rhel)   echo "nftables" ;;
    *)
      echo "87-firewall.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# nftables_include_target: the file this family's nftables.service loads at boot, matching
# `DistroAdapter::nftables_include_target()`.
#
# The one thing about the firewall that genuinely differs between the families, and the
# reason the include is wired here rather than in the agent: Debian's package reads
# /etc/nftables.conf, RHEL's reads /etc/sysconfig/nftables.conf, and neither path exists
# on the other family at all.
nftables_include_target() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "/etc/nftables.conf" ;;
    rhel)   echo "/etc/sysconfig/nftables.conf" ;;
    *)
      echo "87-firewall.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# panel_env_value: the raw remainder of one KEY= line of the generated panel.env.
#
# The value is taken as everything after the FIRST '=' rather than by splitting the line
# into fields and reassembling it — the mistake that once mangled a base64 key, and the
# same reader 60-config.sh and 90-finish.sh use on the same file.
panel_env_value() {
  local key="$1"
  [ -f "$MARAN_FIREWALL_PANEL_ENV" ] || return 0
  awk -v k="$key" 'index($0, k "=") == 1 { print substr($0, length(k) + 2); exit }' "$MARAN_FIREWALL_PANEL_ENV"
}

# ssh_port_flags: `--ssh-port <port>` for every port in a comma-separated list, one word per
# line. Prints nothing and returns non-zero when the list is not one this step may act on.
#
# This function is the most dangerous line of this step, so it is its own unit with its own
# test. `Firewall__SshPorts` is a LIST — sshd listens on every Port directive and every
# ListenAddress that carries a port — and the agent's flag is `--ssh-port`, singular and
# REPEATABLE. Both of the obvious shortcuts are wrong, and they are wrong in opposite ways:
# `--ssh-ports 22,2200` is refused outright by the binary (loud, harmless), while passing only
# the first element renders a policy-drop ruleset that opens one port and closes the others —
# SILENTLY, and the operator discovers it when the connection they were not using stops
# answering.
#
# It RETURNS rather than exiting, and the caller checks. An `exit` here would have been a bug
# rather than a style choice: this function's output is read through a substitution, so the
# exit killed only the subshell, the caller kept the ports printed BEFORE the bad element, and
# a list of `22,abc,2222` seeded a ruleset for port 22 alone at exit status 0. Measured, and it
# is the exact silent lockout the paragraph above warns about.
#
# The whole string is validated before any element is: `22,` and `22,,2200` are the same typo,
# and refusing one while quietly accepting the other is how the difference between them becomes
# a closed port.
ssh_port_flags() {
  local ports="$1" port

  case "$ports" in
    ''|*[!0-9,]*|,*|*,|*,,*)
      echo "87-firewall.sh: Firewall__SshPorts is '${ports}', which is not a comma-separated list of ports." >&2
      return 1
      ;;
  esac

  local IFS=,
  for port in $ports; do
    if [ "${#port}" -gt 5 ] || [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
      echo "87-firewall.sh: port ${port} in Firewall__SshPorts is out of range." >&2
      return 1
    fi
    printf '%s\n%s\n' "--ssh-port" "$port"
  done
  return 0
}

# nft_check: what `nft -c -f` really thinks of a file, with the noise removed.
#
# Three outcomes rather than two, because this runs in two very different places. Root on a
# real host gets a genuine syntax verdict; a container without CAP_NET_ADMIN gets a complaint
# about the kernel no matter how good the file is. Treating the second as "broken" would refuse
# to install anywhere unprivileged; treating it as "fine" would wave a broken file through.
#
#   0  nft accepted the file outright — a full check passed.
#   1  nft could not reach the kernel and said nothing else — no verdict on the syntax.
#   2  nft complained about the FILE. The complaints are printed on stdout.
#
# The classification is by line, not by substring, and that is the correction of a real defect:
# on a target whose first include resolves, nft prints the capability error AND any syntax error
# TOGETHER, so a substring match for the capability message classified `policy drpo` as "cannot
# check here" and moved it into place. Measured on both families: a typo'd ruleset reached
# through an include prints two `Error:` lines, exactly one of which mentions the capability.
nft_check() {
  local file="$1" output residue
  if output="$("$MARAN_NFT_BINARY" -c -f "$file" 2>&1)"; then
    return 0
  fi
  residue="$(printf '%s\n' "$output" | grep 'Error:' | grep -v 'Operation not permitted' || true)"
  if [ -n "$residue" ]; then
    printf '%s\n' "$residue"
    return 2
  fi
  return 1
}

# disable_firewalld: RHEL ships firewalld, and it and a Maran-managed nftables table
# cannot both be in charge honestly.
#
# firewalld owns the ruleset: it flushes and rewrites on its own reloads, so the panel's
# table would vanish at a moment nobody chose and reappear only at the next apply — a
# firewall that is sometimes there is worse than one that is not, because the operator
# believes in it. Disabling it is therefore part of installing, and it is LOGGED rather
# than silent: an operator whose firewalld rules just stopped applying must be able to
# find out why from the install log.
#
# The question asked is whether the UNIT exists, not which family this is. firewalld is a RHEL
# default, but it is packaged for Debian and Ubuntu too and an operator who installed it there
# has exactly the same problem — the panel's table flushed at a moment nobody chose, silently,
# with the panel still reporting its rules as live. A family check would have skipped that host.
# `list-unit-files` rather than `is-enabled`, because is-enabled answers for units that were
# never installed on some systems and the question here is "is firewalld a thing here at all".
disable_firewalld() {
  # THREE answers, never two, and every one of them reaches the log. The query can say "there
  # is a firewalld here", it can say "there is not", and it can fail to say anything — and the
  # third is not the second. An earlier version sent the query's stderr to /dev/null and `|| true`d
  # its status, so a broken systemctl produced an empty string and the empty string was reported
  # as "No firewalld unit on this host" — a statement of fact the code had not established, with
  # the diagnosis that would have contradicted it discarded on the line before.
  #
  # The classification reads the query's OUTPUT, never its exit status, and the reason is
  # measured rather than argued: `systemctl list-unit-files firewalld.service` on a host that
  # simply has no firewalld exits **1** — systemd 255, `0 unit files listed.` on stdout and
  # NOTHING on stderr. A status test would therefore call every ordinary Debian and Ubuntu host
  # broken and shout at its operator on every install, which is how "loud about the real
  # failure" turns into noise nobody reads.
  #
  # So the discriminator is what systemctl SAID. It answered iff it wrote nothing to stderr and
  # exited 0 (a unit matched) or 1 (none did) — the only two statuses that listing has for an
  # answer. Stderr is where systemctl reports a failure to answer, so anything there, and any
  # other status at all (127 for a missing binary, a signal), means the question is open.
  local diagnosis
  diagnosis="$(mktemp)" || {
    echo "87-firewall.sh: cannot stage a file to capture systemctl's own diagnosis." >&2
    exit 1
  }

  local listing="" query_status=0 query_errors=""
  listing="$(systemctl list-unit-files firewalld.service 2>"$diagnosis")" || query_status=$?
  query_errors="$(cat "$diagnosis")"

  case "$listing" in
    *firewalld.service*)
      echo "Disabling firewalld: Maran manages nftables directly, and firewalld rewrites the"
      echo "ruleset on its own reloads, which would erase the panel's table without warning."
      ;;
    *)
      if [ -z "$query_errors" ] && [ "$query_status" -le 1 ]; then
        # The one answer that may proceed quietly: systemctl answered, and the answer was no.
        rm -f "$diagnosis"
        echo "No firewalld unit on this host; nothing to disable."
        return 0
      fi
      # The query BROKE. Loud, and it says what broke rather than inventing an answer — then it
      # tries the disable anyway, because "I could not find out" is a reason to act and check,
      # not a reason to skip.
      echo "WARNING: 'systemctl list-unit-files firewalld.service' could not be answered here" >&2
      echo "         (exit ${query_status}${query_errors:+; systemctl said: ${query_errors}})." >&2
      echo "         That is NOT the same answer as 'this host has no firewalld', so the disable" >&2
      echo "         below is attempted regardless and its result is checked." >&2
      ;;
  esac

  local disable_status=0 disable_errors=""
  systemctl disable --now firewalld 2>"$diagnosis" || disable_status=$?
  disable_errors="$(cat "$diagnosis")"
  rm -f "$diagnosis"

  # And then LOOK, the way start_firewall_service looks at the nftables load. `systemctl
  # disable` returning 0 is not firewalld being off, and an earlier version threw its status
  # away entirely (`|| true`) — so a refused disable finished the install in silence, the panel
  # went on reporting its rules as live, and firewalld erased the panel's table at its next
  # reload. The WORDS decide, not the statuses: `is-enabled` prints `static` at exit 0 for a
  # unit nobody can enable, and `is-active` prints `inactive` at a non-zero one.
  local still_enabled still_active
  still_enabled="$(systemctl is-enabled firewalld 2>/dev/null || true)"
  still_active="$(systemctl is-active firewalld 2>/dev/null || true)"
  case "${still_enabled}:${still_active}" in
    enabled:*|enabled-runtime:*|*:active|*:activating|*:reloading)
      cat >&2 <<EOF
87-firewall.sh: firewalld is still there after 'systemctl disable --now firewalld'
(exit ${disable_status}${disable_errors:+; systemctl said: ${disable_errors}}).

    systemctl is-enabled firewalld  ->  ${still_enabled:-(no answer)}
    systemctl is-active  firewalld  ->  ${still_active:-(no answer)}

firewalld flushes and rewrites the ruleset on its own reloads, so 'table inet maran' would
vanish at a moment nobody chose while the panel went on reporting its rules as live. Disable
it by hand and run the installer again:

    systemctl disable --now firewalld
EOF
      exit 1
      ;;
  esac

  if [ "$disable_status" -ne 0 ]; then
    # It failed, and firewalld is off all the same — the ordinary shape of a host that never
    # had the unit. Recorded rather than swallowed, because this line is the only place an
    # operator can find out that the question was asked at all.
    echo "Note: 'systemctl disable --now firewalld' exited ${disable_status}${disable_errors:+ (${disable_errors})}."
    echo "      firewalld is neither enabled nor running on this host, so nothing but Maran is"
    echo "      in charge of the ruleset."
  fi
}

# render_firewall_file: one of the two files, rendered by the agent into place atomically.
#
# Render, check, then rename — the discipline 80-nginx.sh and 86-sftp.sh use, for the same
# reason and one more of its own. `>` on the live path would truncate the existing file
# before the agent had produced a byte, so a re-run against a broken binary would leave the
# host with an EMPTY ruleset file that the include still names: nftables then loads
# nothing where it used to load a policy, which is a firewall that has quietly become an
# open host. Staged inside /etc/maran, not /tmp, because a rename is only atomic within
# one filesystem.
#
# Three checks, because "the command exited 0" is not enough for a file the boot depends
# on: the exit status, that the file is not empty, and that it actually looks like the
# table it claims to be.
render_firewall_file() {
  local dest="$1"
  shift
  local tmp
  tmp="$(mktemp "${MARAN_FIREWALL_CONFIG_DIR}/.firewall.XXXXXX")" || {
    echo "87-firewall.sh: cannot stage a file in ${MARAN_FIREWALL_CONFIG_DIR}; ${dest} was not touched." >&2
    exit 1
  }

  if ! "$MARAN_FIREWALL_AGENT" "$@" > "$tmp"; then
    rm -f "$tmp"
    echo "87-firewall.sh: '${MARAN_FIREWALL_AGENT} $*' failed; ${dest} was not touched." >&2
    exit 1
  fi
  if [ ! -s "$tmp" ]; then
    rm -f "$tmp"
    echo "87-firewall.sh: '${MARAN_FIREWALL_AGENT} $*' printed nothing; ${dest} was not touched." >&2
    exit 1
  fi
  if ! grep -q '^table inet ' "$tmp"; then
    rm -f "$tmp"
    echo "87-firewall.sh: '${MARAN_FIREWALL_AGENT} $*' printed something that is not an nftables table;" >&2
    echo "                ${dest} was not touched." >&2
    exit 1
  fi

  # And nft's own opinion of it, before it becomes the file a boot depends on. A ruleset that
  # does not parse is not a hypothetical just because the agent rendered it: this step exists
  # to survive the agent being wrong, and an unparseable file left at the live path with the
  # include already wired is nftables.service FAILED at the next boot even though the install
  # aborted.
  # `|| check_status=$?`, and it is not decoration. nft_check returns 1 whenever nft cannot
  # reach the kernel, which is EVERY unprivileged host, and a bare assignment from a command
  # substitution is a plain command: under the `set -euo pipefail` this file sets and install.sh
  # already has, it ended the step right here with no message at all. Measured — the step died
  # after the bans render, rc 1, nothing written and nothing said, which made the diagnostic
  # below unreachable in exactly the situation it was written for.
  local complaints check_status=0
  complaints="$(nft_check "$tmp")" || check_status=$?
  case "$check_status" in
    2)
      rm -f "$tmp"
      echo "87-firewall.sh: the file '${MARAN_FIREWALL_AGENT} $*' rendered does not parse:" >&2
      echo "${complaints}" >&2
      echo "                ${dest} was not touched." >&2
      exit 1
      ;;
  esac

  chown root:root "$tmp"
  chmod 0644 "$tmp"
  mv -f "$tmp" "$dest"
}

# seed_firewall_files: both files, bans first.
#
# The two host facts come from panel.env, which 60-config.sh wrote earlier in this same
# install: the ports the seeded policy opens are then the SAME values the panel binds as
# `Firewall__SshPorts` and `Firewall__PanelPort` and sends with every later mutation. Read
# from one place, they cannot disagree; detected twice, they could.
#
# Neither is defaulted. A missing value aborts the step, because the alternative is
# seeding a `policy drop` ruleset around a guess: guess the SSH port wrong and the
# operator loses the machine they are installing on, with no remote way back in.
seed_firewall_files() {
  local ssh_ports panel_port
  ssh_ports="$(panel_env_value Firewall__SshPorts)"
  panel_port="$(panel_env_value Firewall__PanelPort)"

  if [ -z "$ssh_ports" ] || [ -z "$panel_port" ]; then
    cat >&2 <<EOF
87-firewall.sh: ${MARAN_FIREWALL_PANEL_ENV} does not carry Firewall__SshPorts and
Firewall__PanelPort, which 60-config.sh writes.

They are not guessed here on purpose. The seeded ruleset drops everything it does not
name, so a guessed SSH port is a server the operator can no longer reach and cannot fix
remotely. Re-run the installer so step 60 regenerates the file, then run it again.
EOF
    exit 1
  fi

  # A command substitution, NOT a process substitution, because the status is the point: the
  # flags are only usable if the whole list was usable. Read through `< <(...)` the failure was
  # invisible — the subshell died, the caller kept the flags printed before the bad element,
  # and a malformed list seeded a ruleset for the ports that happened to come first.
  local flags_text
  if ! flags_text="$(ssh_port_flags "$ssh_ports")"; then
    echo "87-firewall.sh: refusing to seed a firewall from an unusable Firewall__SshPorts value." >&2
    echo "                A ruleset seeded from part of that list would drop the ports it left out," >&2
    echo "                and one of them is how you are connected to this machine." >&2
    exit 1
  fi
  if [ -z "$flags_text" ]; then
    echo "87-firewall.sh: Firewall__SshPorts named no port at all; refusing to seed a ruleset." >&2
    exit 1
  fi

  local -a ssh_flags=()
  local flag
  while IFS= read -r flag; do
    [ -n "$flag" ] && ssh_flags+=("$flag")
  done <<< "$flags_text"

  # Bans first, and both before anything includes either: an include naming a file that
  # is not there yet aborts the whole load.
  echo "Rendering ${MARAN_FIREWALL_BANS} through the agent..."
  render_firewall_file "$MARAN_FIREWALL_BANS" render-firewall-bans

  echo "Rendering ${MARAN_FIREWALL_RULESET} (ssh ${ssh_ports}, panel ${panel_port}) through the agent..."
  render_firewall_file "$MARAN_FIREWALL_RULESET" render-firewall-ruleset \
    "${ssh_flags[@]}" --panel-port "$panel_port"
}

# render_include_block: the block itself, on stdout.
#
# The bans file first, because an `include` is processed where it stands and the rules
# table's chain hooks at a priority that assumes the bans table already exists.
render_include_block() {
  cat <<EOF
${MARAN_FIREWALL_BEGIN_MARKER}
include "${MARAN_FIREWALL_BANS}"
include "${MARAN_FIREWALL_RULESET}"
${MARAN_FIREWALL_END_MARKER}
EOF
}

# strip_marker_block: the file without our include block, on stdout; non-zero when the block
# is not a matched pair and the caller must not guess.
#
# A state machine rather than `sed '/BEGIN/,/END/d'`, and the difference is a destroyed
# server. A sed range whose end marker is missing deletes from BEGIN to END OF FILE — measured
# on the uninstaller, where an operator's own `table inet mine` written below a half-deleted
# block was removed along with it. Here the deletion is driven by state, an unterminated block
# is an error rather than a licence to delete the rest of the file, and a stray END or a nested
# BEGIN are errors too: all three mean somebody edited between the markers, which the markers
# themselves ask nobody to do.
strip_marker_block() {
  local file="$1"
  awk -v begin="$MARAN_FIREWALL_BEGIN_PREFIX" -v end="$MARAN_FIREWALL_END_PREFIX" '
    index($0, begin) == 1 { if (inside) { exit 3 } inside = 1; next }
    index($0, end) == 1   { if (!inside) { exit 4 } inside = 0; next }
    !inside               { print }
    END                   { if (inside) { exit 5 } }
  ' "$file"
}

# wire_firewall_includes: puts exactly one current block at the END of the family's include
# target, and refuses to leave a file `nft` cannot load.
#
# Delete-then-append, so a re-run leaves one block rather than two and an edit to the block
# above actually reaches a host installed last month. At the end of the file because Debian's
# shipped file opens with `flush ruleset`: our tables must load after that flush, or the boot
# would erase them a moment after loading them.
#
# Staged in the TARGET'S OWN DIRECTORY, not /tmp. `mv` across filesystems is a copy, and a copy
# is not atomic — the very rule render_firewall_file states, which this function used to break
# by staging in /tmp and renaming into /etc. Both directories are root-owned, so nothing is
# given away by staging there.
#
# Checked with `nft -c -f` before it becomes the live file. That check is not a formality: it
# is the one that catches the failure this whole step is ordered around, because an include
# naming a missing file fails at parse time, and a target that fails to parse is a boot with no
# firewall at all.
#
# The target is a parameter with the family's real default so the wiring can be exercised
# against a scratch file — by the polygon, with real nft and real files behind it, rather than
# by reading this comment and believing it.
wire_firewall_includes() {
  local target="${1:-}"
  if [ -z "$target" ]; then
    target="$(nftables_include_target)" || exit 1
  fi

  if [ ! -e "$target" ]; then
    # RHEL ships its target as a file of comments; Debian ships one with a flush. A family
    # that shipped neither still needs somewhere for the include to live.
    install -d -m 0755 "$(dirname "$target")"
    : > "$target"
    chmod 0644 "$target"
  fi

  local candidate strip_status=0
  candidate="$(mktemp "$(dirname "$target")/.maran-nftables.XXXXXX")" || {
    echo "87-firewall.sh: cannot stage a file beside ${target}; the live file was not touched." >&2
    exit 1
  }
  strip_marker_block "$target" > "$candidate" || strip_status=$?
  if [ "$strip_status" -ne 0 ]; then
    rm -f "$candidate"
    cat >&2 <<EOF
87-firewall.sh: the Maran markers in ${target} are not a matched pair (awk status ${strip_status}).

Something has edited between them, and this step will not guess where the block ends: deleting
from the opening marker to the end of the file would take an operator's own rules with it.
Repair the block by hand — it runs from

    ${MARAN_FIREWALL_BEGIN_MARKER}

to

    ${MARAN_FIREWALL_END_MARKER}

— or remove both markers and everything between them, then re-run the installer.
EOF
    exit 1
  fi
  render_include_block >> "$candidate"

  chmod --reference="$target" "$candidate" 2>/dev/null || chmod 0644 "$candidate"
  chown --reference="$target" "$candidate" 2>/dev/null || true

  local complaints check_status=0
  complaints="$(nft_check "$candidate")" || check_status=$?
  case "$check_status" in
    0) ;;
    1)
      echo "Note: nft cannot reach the kernel here, so ${target} was not fully syntax-checked."
      echo "      Its includes do resolve, which is the part that decides whether a boot has a firewall."
      ;;
    *)
      rm -f "$candidate"
      # Refused either way; the only thing decided here is which sentence the operator reads.
      # A missing include is the failure this step is ordered around and it earns its own
      # words — but the choice happens AFTER the refusal, so no message-matching can wave a
      # file through the way an earlier version's classification did.
      case "$complaints" in
        *"File not found"*)
          echo "87-firewall.sh: the candidate ${target} names a file that does not exist:" >&2
          echo "${complaints}" >&2
          echo "                Left the live file alone. An include whose target is missing aborts the" >&2
          echo "                WHOLE load, so a host wired that way boots with no firewall at all." >&2
          ;;
        *)
          echo "87-firewall.sh: the candidate ${target} does not parse; the live file was not touched." >&2
          echo "${complaints}" >&2
          echo "                A target nft cannot load is a boot with no firewall at all." >&2
          ;;
      esac
      exit 1
      ;;
  esac

  mv -f "$candidate" "$target"
}

# record_firewall_service_enablement: writes the marker when, and only when, THIS installer is
# about to enable a firewall service that was not enabled before.
#
# Its own function because it is the whole of the uninstaller's decision, and because the moment
# it can be answered is narrow: afterwards the unit is enabled either way and nothing can tell
# who did it. Called before the enable, tested on both branches.
record_firewall_service_enablement() {
  if systemctl is-enabled "$MARAN_FIREWALL_SERVICE" >/dev/null 2>&1; then
    # Already enabled before Maran arrived. No marker, so the uninstaller leaves the operator's
    # firewall exactly as it found it.
    return 0
  fi
  : > "$MARAN_FIREWALL_ENABLED_MARKER"
  chmod 0644 "$MARAN_FIREWALL_ENABLED_MARKER"
}

# start_firewall_service: enable, start, and then LOOK.
#
# The last two lines are the point of the function. `systemctl enable --now` returns success for
# a unit whose ExecStart failed, so this asks the kernel directly whether the table is loaded.
# An installer that enables a unit and never looks at it is the defect this plan has found three
# times.
start_firewall_service() {
  record_firewall_service_enablement

  systemctl enable --now "$MARAN_FIREWALL_SERVICE"
  # A unit that was already running is loading the OLD ruleset; the includes we just wired
  # reach the kernel only on a reload.
  systemctl reload "$MARAN_FIREWALL_SERVICE" 2>/dev/null \
    || systemctl restart "$MARAN_FIREWALL_SERVICE" 2>/dev/null \
    || true

  require_maran_table_loaded "the ${MARAN_FIREWALL_SERVICE} service is enabled"
}

# require_maran_table_loaded: asks the KERNEL whether the panel's table is in force, and stops
# the install where it is not.
#
# Its own function because step_firewall asks it twice, at the two moments the answer can
# change: once when the service has been started, and again after firewalld has been taken
# away — a firewalld shutdown rewrites the ruleset, and "we disabled the thing that was
# flushing our table" is a claim worth checking rather than assuming. Both calls name the
# moment they are asking about, so the operator reading the failure knows which one it was.
require_maran_table_loaded() {
  local moment="$1"
  "$MARAN_NFT_BINARY" list table inet maran >/dev/null 2>&1 && return 0
  cat >&2 <<EOF
87-firewall.sh: ${moment}, but 'table inet maran' is not loaded in the kernel.

The panel's own ruleset is therefore not in force, and every firewall rule an administrator
creates in the panel would be written to a file nothing is reading. Find out what the unit
did with the include:

    systemctl status ${MARAN_FIREWALL_SERVICE}
    ${MARAN_NFT_BINARY} -c -f $(nftables_include_target)
EOF
  exit 1
}

step_firewall() {
  echo "Installing the firewall (nftables)..."

  # Resolved first, into a plain assignment, so a family neither function knows stops the step
  # here instead of at the third call site with a package manager already run without arguments.
  local packages
  packages="$(firewall_packages_for_family)" || exit 1
  # Unquoted on purpose: the answer is a package LIST and the words are the packages.
  # shellcheck disable=SC2086
  pkg_install $packages

  if [ ! -x "$MARAN_NFT_BINARY" ]; then
    echo "87-firewall.sh: ${MARAN_NFT_BINARY} is missing; the agent executes exactly this path." >&2
    exit 1
  fi
  if [ ! -x "$MARAN_FIREWALL_AGENT" ]; then
    echo "87-firewall.sh: ${MARAN_FIREWALL_AGENT} is missing; step 50 installs it and this step" >&2
    echo "                renders both ruleset files through it." >&2
    exit 1
  fi

  # The order of these four is the difference between a host that is always behind SOME
  # firewall and one that is briefly behind none.
  #
  # firewalld is taken away LAST, and only once the kernel has confirmed the table that
  # replaces it. Disabling it first — which this step did, and which is how the ordering was
  # measured rather than argued — stops the working firewall of a RHEL host before a single
  # ruleset byte exists, and EVERY line below can still abort: a render that fails or prints
  # nothing, a rendering nft rejects, an unusable Firewall__SshPorts, a damaged marker pair in
  # the include target, a candidate that does not parse, a failing `systemctl enable --now`.
  # Any one of them left the host wide open with the log's last word on the subject a
  # present-tense promise that Maran was now managing the firewall.
  #
  # The two firewalls overlap for the length of these three lines instead, which costs
  # nothing: firewalld keeps its own tables, ours are `inet maran` and `inet maran_bans`, and
  # the kernel enforces the union — strictly more closed than either alone, never less.
  seed_firewall_files
  wire_firewall_includes
  start_firewall_service
  disable_firewalld
  # And again, because disabling firewalld is the one thing here that can UNDO the line above:
  # firewalld rewrites the ruleset as it shuts down. Asked of the kernel rather than assumed.
  require_maran_table_loaded "firewalld has been disabled"
  echo "Firewall active: table inet maran is loaded, and bans land in table inet maran_bans."
}
