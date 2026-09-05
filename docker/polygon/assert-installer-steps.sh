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
# SOURCED IN A CHILD, never here: see run_uninstaller. It is in this list all the same, because
# a missing file must be a failure and not a silently skipped assertion.
readonly UNINSTALLER="${INSTALLER_ROOT}/uninstall.sh"

# A throwaway credential, used only to put this container's MariaDB into the
# broken states below and then take it back out. It is not a secret and it never
# leaves the build layer: the negative assertions need a server that really has a
# root password, and there is no way to assert that the installer refuses one
# without creating one.
readonly THROWAWAY_ROOT_PASSWORD="polygon-throwaway"

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
require_installer_file "${INSTALLER_LIB}/85-mysql.sh" installer/lib/85-mysql.sh
require_installer_file "${INSTALLER_LIB}/86-sftp.sh" installer/lib/86-sftp.sh
require_installer_file "$UNINSTALLER" installer/uninstall.sh

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

  [ -d /var/lib/maran/sftp ] \
    || fail "the SFTP jail base directory /var/lib/maran/sftp was not created"

  local ownership_and_mode
  ownership_and_mode="$(stat -c '%U:%G:%a' /var/lib/maran/sftp)"
  [ "$ownership_and_mode" = "root:root:755" ] \
    || fail "/var/lib/maran/sftp is ${ownership_and_mode}, not root:root:755"

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
# Step 80 needs exactly two things from that step: the `panel` group, because it installs the
# panel's private key root:panel, and /var/log/maran, because the vhost names it in access_log
# and error_log and `nginx -t` opens both. The group is created by step 40's own function. The
# directory is made here with step 40's own user, group and mode instead of by calling
# create_directory_layout, and that is stated rather than hidden: that function also re-modes
# /run/maran to 0750 root:panel, which this image sets 0755 on purpose for the php-pool suites,
# and step 80 never reads it.
prepare_host_for_the_nginx_step() {
  bash -c 'set -euo pipefail
. "$1"
create_panel_user >/dev/null
install -d -o "$MARAN_USER" -g "$MARAN_GROUP" -m 0750 /var/log/maran' _ "$USER_STEP"
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
# function under test creates its TLS directory `-g panel`, so without the group that assertion's
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
# directory came out `2710 panel:panel` on both families and nginx could not open the socket at
# all: systemd re-applies a unit's User=/Group= to its RuntimeDirectory= on every command
# invocation of that unit, so the chgrp was undone before ExecStart ran. A check that reports on
# something it never looked at is worse than no check, because it retires the question.
#
# WHAT THIS ONE SEES, and it is a runtime fact rather than a text one: it runs step 70's real
# `install_units` (so the snippet is rendered by the installer's renderer, from install.sh's own
# socket path and this family's own web server group), its real `build_api_socket_directory` (so
# the directory is made by THIS family's systemd-tmpfiles, against this image's real `panel` and
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
    "d __MARAN_API_SOCKET_DIR__ 2710 panel __MARAN_WEB_GROUP__ -") ;;
    *) fail "${API_TMPFILES}'s directive is '${payload}'. It must be
'd __MARAN_API_SOCKET_DIR__ 2710 panel __MARAN_WEB_GROUP__ -': the type d so an existing directory is
CORRECTED and not only created, 2710 so no other uid can traverse to the socket and so the socket inherits
the group, panel because the unit's User= must be able to bind there, and the placeholders so the path and
the group keep following install.sh." ;;
  esac

  # RUNTIME, from here down. Step 70's own functions, this family's own systemd-tmpfiles.
  run_services_step "$socket_path" "$web_group" 'install_units; build_api_socket_directory; assert_api_socket_directory' \
    || fail "step 70 could not build ${socket_dir} on this family. That directory is the panel's trust boundary
and the api unit does not start without it."

  # The polygon's own eyes, not the installer agreeing with itself.
  expected="2710 panel ${web_group}"
  observed="$(stat -c '%a %U %G' "$socket_dir" 2>/dev/null || true)"
  [ "$observed" = "$expected" ] \
    || fail "${socket_dir} was built as '${observed:-absent}' and must be '${expected}'. At 2710 no other uid on
the machine can resolve a path inside it, so a customer's cron entry or PHP script cannot connect(2) to the
socket; the group is what lets nginx traverse it at all, and the setgid bit is what hands the socket that
group when the panel — which holds no capabilities — creates it."

  # The postcondition is not vacuous: break the directory and watch step 70 refuse.
  chgrp panel "$socket_dir"
  if run_services_step "$socket_path" "$web_group" 'assert_api_socket_directory' >/dev/null 2>&1; then
    fail "step 70's assert_api_socket_directory accepted ${socket_dir} group-owned by panel, which is the exact
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

main() {
  # First: it reads files and touches no service, so it reports the cheapest failure before
  # anything slower has a chance to fail for its own reasons.
  assert_panel_port_has_one_authority
  assert_generated_keys_are_documented
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
  # `install -d -o root -g panel` on the TLS directory, and the `panel` group is created by that
  # assertion's prepare step. Run first, this one restores no certificate — measured, as a red
  # build on its own restoration check rather than as a green assertion that had tested nothing.
  assert_the_panel_certificate_is_never_written_through_a_symlink
  # After it, because it needs the `panel` group that assertion's prepare step creates, and because
  # it is the other assertion that installs files outside /tmp.
  assert_the_panel_socket_directory_is_built_and_then_looked_at
  echo "Installer steps 60, 70, 80, 85, 86 and 87, and the panel port's single authority, verified inside the polygon."
}

main "$@"
