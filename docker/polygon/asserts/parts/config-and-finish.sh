#!/usr/bin/env bash
# the panel port's single authority, the finish message, and the keys 60-config.sh generates
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


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
. "$2"
step_finish' _ "$COMMON_LIB" "$step" 2>&1
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
