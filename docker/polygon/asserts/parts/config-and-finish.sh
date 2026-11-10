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


# panel_host_name_for: runs the installer's own detect_panel_host_name with host_display_name
# replaced by a stand-in that prints `$1` through printf (so `\n` can plant a second line), and
# prints what the function answered. The step is the real one, sourced unmodified after the shared
# helpers; only the machine's name is the stand-in.
panel_host_name_for() {
  bash -c 'set -euo pipefail
. "$1"
. "$2"
fake_name="$3"
host_display_name() { printf "${fake_name}\n"; }
detect_panel_host_name' _ "$COMMON_LIB" "$CONFIG_STEP" "$1"
}

# panel_env_written_with: runs the installer's own write_config, unmodified, against a scratch
# directory, with `$1` as the panel host name argument (and the optional `$2` as the panel public
# address argument), and prints the panel.env it produced. Only
# the things that need a real machine are stand-ins: the service identity check, chown and chmod.
# The rendering, and the `if [ -n ... ]` guarding the Panel__HostName line, are the real ones.
panel_env_written_with() {
  local scratch output
  scratch="$(mktemp -d)"
  output="$(bash -c 'set -euo pipefail
. "$1"
# 60-config.sh declares MARAN_CONFIG_DIR readonly, so it cannot be reassigned after sourcing; the
# one line that names the directory is pointed at the scratch directory as the step is read, and
# every other line is the real one. MARAN_CONFIG_FILE follows from it by the step itself.
. <(sed -e "s#^readonly MARAN_CONFIG_DIR=.*#readonly MARAN_CONFIG_DIR=\"$3\"#" "$2")
MARAN_PANEL_PORT=8443
MARAN_API_SOCKET_PATH=/run/maran/api.sock
MARAN_USER=maran
MARAN_GROUP=maran
require_service_identity() { :; }
chown() { :; }
chmod() { :; }
write_config key token signing 22 "" 101 "$4" "$5"
cat "$MARAN_CONFIG_FILE"' _ "$COMMON_LIB" "$CONFIG_STEP" "$scratch" "$1" "${2:-}")" || { rm -rf "$scratch"; fail "write_config failed when run for real"; }
  rm -rf "$scratch"
  printf '%s\n' "$output"
}

# panel_public_url_verdict_for: runs the installer's own panel_public_url_is_valid, unmodified, on
# `$1` and prints `valid` or `invalid`. The value is passed as an argument, never interpolated into
# the script text, so a hostile shape is exactly what the function receives.
panel_public_url_verdict_for() {
  bash -c 'set -euo pipefail
. "$1"
. "$2"
if panel_public_url_is_valid "$3"; then echo valid; else echo invalid; fi' _ "$COMMON_LIB" "$CONFIG_STEP" "$1"
}

# panel_public_url_detected_with: runs the installer's own detect_panel_public_url with
# panel_public_host_candidates replaced by a stand-in printing `$1` (so the test chooses what the
# machine "looks like"), the override `$2` in MARAN_PANEL_PUBLIC_URL, and prints
# `<exit status>|<stdout>|<stderr>` joined with a literal pipe, the last two with newlines folded.
panel_public_url_detected_with() {
  local out err status scratch
  scratch="$(mktemp)"
  status=0
  out="$(PROBE_CANDIDATES="$1" MARAN_PANEL_PUBLIC_URL="${2:-}" bash -c 'set -euo pipefail
. "$1"
. "$2"
MARAN_PANEL_PORT=8443
panel_public_host_candidates() { printf "%b" "$PROBE_CANDIDATES"; }
detect_panel_public_url' _ "$COMMON_LIB" "$CONFIG_STEP" 2>"$scratch")" || status=$?
  err="$(tr '\n' ' ' < "$scratch")"
  rm -f "$scratch"
  printf '%s|%s|%s\n' "$status" "$out" "$err"
}

# assert_the_panel_public_url_is_chosen_and_written_only_when_valid: the address every invitation and
# reset link starts with (Panel__PublicUrl), threat note
# docs/superpowers/notes/2026-11-07-panel-public-url-threat-note.md. Propositions, each executed
# against the installer's own functions and each with its inverse control:
#   1. the validator accepts real origins (IPv4, IPv6, name, with and without port) and refuses
#      every shape that could end a line, start a second setting or change what the host is;
#   2. write_config records the value when valid, even with no host name, and records NOTHING for a
#      hostile one, and no longer writes the old PasswordReset__PanelUrl key;
#   3. detection prefers a public name over an address, skips a hostile candidate, flags a private
#      address, takes an explicit override, and REFUSES an invalid override instead of falling back;
#   4. the certificate's extra subjectAltName entry is built only from a plain address;
#   5. the finish message and panel.env.example read/document the same key.
assert_the_panel_public_url_is_chosen_and_written_only_when_valid() {
  local value verdict written answer

  for value in 'https://203.0.113.7:8443' 'https://panel.example.com:8443' 'https://panel.example.com' \
    'https://[2001:db8::1]:8443' 'https://localhost.example.org:65535' 'HTTPS://Panel.Example.COM:8443'; do
    verdict="$(panel_public_url_verdict_for "$value")"
    [ "$verdict" = "valid" ] || fail "panel_public_url_is_valid refused the good origin '${value}'; a validator that
refuses everything would pass every refusal below, and every install would record no address."
  done

  for value in '' 'panel.example.com' 'http://panel.example.com' 'https://' 'https://user@panel.example.com' \
    'https://panel.example.com/path' 'https://panel.example.com?x=1' 'https://panel.example.com#x' \
    'https://panel.example.com:0' 'https://panel.example.com:65536' 'https://panel.example.com:80:90' \
    'https://panel.example.com:' 'https://127.1' 'https://999.1.1.1' $'https://b\xc3\xbccher.example' \
    'https://intranet' 'https://panel_host.example.com' 'https://panel.example.com\\evil' \
    'https://pa nel.example.com' 'https://panel.example.com"' 'https://panel.example.com;rm' \
    'https://panel.example.com$x' 'https://panel.example.com=1' 'https://[::1:8443' \
    'https://[fe80::1%25eth0]:8443' 'https://[fe80::1%eth0]:8443' 'https://panel%2eexample.com' \
    $'https://panel.example.com\nPanel__Other=1' $'https://panel.example.com\r' $'https://panel.example.com\t'; do
    verdict="$(panel_public_url_verdict_for "$value")"
    [ "$verdict" = "invalid" ] || fail "panel_public_url_is_valid accepted '${value}'. The value goes into a line-oriented
env file and into mails sent in the panel's name; one byte outside the alphabet is a second setting or a
different host (rules/security.md item 4)."
  done

  written="$(panel_env_written_with '' 'https://203.0.113.7:8443')"
  printf '%s\n' "$written" | grep -qx 'Panel__PublicUrl=https://203.0.113.7:8443' \
    || fail "write_config did not record Panel__PublicUrl for a valid address given with NO host name; the address
customers are mailed is independent of the machine's own name"
  if printf '%s\n' "$written" | grep -q '^PasswordReset__PanelUrl='; then
    fail "write_config still writes PasswordReset__PanelUrl, a key the panel no longer reads"
  fi
  written="$(panel_env_written_with 'shop.example.com' $'https://203.0.113.7:8443\nPanel__Other=1')"
  if printf '%s\n' "$written" | grep -qE '^(Panel__PublicUrl|Panel__Other)='; then
    fail "write_config recorded a hostile public address (a newline planted a second setting); it must leave the key out"
  fi
  printf '%s\n' "$written" | grep -q '^Panel__HostName=shop.example.com$' \
    || fail "refusing the hostile address also dropped Panel__HostName, so this check cannot tell a refusal from a broken writer"

  answer="$(panel_public_url_detected_with 'name shop.example.com\nroute 10.1.2.3\n')"
  [ "$answer" = "0|https://shop.example.com:8443|Panel public address: https://shop.example.com:8443 (this host's name, which resolves to a public address). " ] \
    || fail "detection did not prefer a publicly resolving name over an address. Got: ${answer}"

  answer="$(panel_public_url_detected_with 'ssh 1.2.3.4;rm\nroute 203.0.113.9\n')"
  case "$answer" in
    "0|https://203.0.113.9:8443|"*) ;;
    *) fail "detection used or choked on a hostile candidate instead of skipping to the next. Got: ${answer}" ;;
  esac

  answer="$(panel_public_url_detected_with 'route 10.1.2.3\n')"
  case "$answer" in
    "0|https://10.1.2.3:8443|"*"private address"*) ;;
    *) fail "detection did not flag a private address as unreachable from outside. Got: ${answer}" ;;
  esac

  answer="$(panel_public_url_detected_with '')"
  case "$answer" in
    "0||"*"No public address could be found"*) ;;
    *) fail "detection with no candidate must print nothing on stdout and say why. Got: ${answer}" ;;
  esac

  answer="$(panel_public_url_detected_with 'route 10.1.2.3\n' 'https://panel.example.org:8443/')"
  case "$answer" in
    "0|https://panel.example.org:8443|"*) ;;
    *) fail "an explicit MARAN_PANEL_PUBLIC_URL was not taken over detection. Got: ${answer}" ;;
  esac

  answer="$(panel_public_url_detected_with 'route 203.0.113.9\n' 'http://panel.example.org')"
  case "$answer" in
    "1||"*) ;;
    *) fail "an INVALID MARAN_PANEL_PUBLIC_URL fell back to detection or exited 0; the operator's explicit choice must
be refused, not replaced by a guess. Got: ${answer}" ;;
  esac

  for value in 8.8.8.8 172.32.0.1 203.0.113.9; do
    bash -c 'set -euo pipefail; . "$1"; . "$2"; address_is_publicly_routable "$3"' _ "$COMMON_LIB" "$CONFIG_STEP" "$value" \
      || fail "address_is_publicly_routable called ${value} private"
  done
  for value in 10.0.0.1 172.16.0.1 172.31.255.255 192.168.1.1 100.64.0.1 169.254.1.1 127.0.0.1 224.0.0.1 0.1.2.3; do
    if bash -c 'set -euo pipefail; . "$1"; . "$2"; address_is_publicly_routable "$3"' _ "$COMMON_LIB" "$CONFIG_STEP" "$value"; then
      fail "address_is_publicly_routable called ${value} public; links to it would be unreachable from outside"
    fi
  done

  # 4. The certificate's extra subjectAltName: the real function, extracted from the served step,
  # reading a scratch panel.env. IPv4, IPv6 and a name map to the right kind; a hostile value, or a
  # missing key, yields nothing at all.
  local scratch san
  scratch="$(mktemp)"
  for value in 'https://203.0.113.7:8443|IP:203.0.113.7' 'https://[2001:db8::1]:8443|IP:2001:db8::1' \
    'https://panel.example.com:8443|DNS:panel.example.com' 'https://a.b,IP:1.2.3.4:8443|' 'http://203.0.113.7|'; do
    printf 'Other=1\nPanel__PublicUrl=%s\n' "${value%%|*}" > "$scratch"
    san="$(env MARAN_CONFIG_FILE="$scratch" bash -c 'set -euo pipefail
eval "$(sed -n "/^panel_certificate_extra_san()/,/^}/p" "$1")"
panel_certificate_extra_san' _ "$NGINX_STEP")"
    [ "$san" = "${value#*|}" ] \
      || { rm -f "$scratch"; fail "panel_certificate_extra_san answered '${san}' for '${value%%|*}'; expected '${value#*|}'.
A subjectAltName built from anything but a plain address lets a value add a second certificate name."; }
  done
  printf 'Other=1\n' > "$scratch"
  san="$(env MARAN_CONFIG_FILE="$scratch" bash -c 'set -euo pipefail
eval "$(sed -n "/^panel_certificate_extra_san()/,/^}/p" "$1")"
panel_certificate_extra_san' _ "$NGINX_STEP")"
  rm -f "$scratch"
  [ -z "$san" ] || fail "panel_certificate_extra_san invented '${san}' when panel.env records no public address"

  # 5. The same key is read where the operator is told the address, and documented.
  grep -q 'panel_env_value Panel__PublicUrl' "$FINISH_STEP" \
    || fail "90-finish.sh no longer prints the address panel.env records in Panel__PublicUrl; what the operator is
told to open and what customers are mailed can now differ"
  grep -qE '^Panel__PublicUrl=' "$PANEL_ENV_EXAMPLE" \
    || fail "panel.env.example no longer documents Panel__PublicUrl, which the installer writes"
  if grep -qE '^PasswordReset__PanelUrl=' "$PANEL_ENV_EXAMPLE"; then
    fail "panel.env.example still documents PasswordReset__PanelUrl, a key the panel no longer reads"
  fi

  echo "Panel__PublicUrl: valid origins are recorded (even with no host name), hostile shapes record nothing, a public name beats"
  echo "an address, a private address is flagged, an invalid override is refused rather than guessed around, and the"
  echo "certificate's extra name is built only from a plain address. UNOBSERVED HERE: the real network facts"
  echo "(getent, ip route, SSH_CONNECTION) are stand-ins, so what a given host would pick is not measured."
}

# assert_the_panel_host_name_is_recorded_from_the_name_the_vhost_serves: issue #96. panel.env's
# Panel__HostName is what the API refuses to customers as the panel's own name, so it must be the
# very name nginx serves. Propositions, each with a planted break:
#   1. the value is host_display_name lower-cased, and a name that is not a plain single-line
#      host name (the injection shapes) or is a placeholder records NOTHING, never `_`;
#   2. 60-config.sh writes Panel__HostName only from the detected value and 80-nginx.sh renders its
#      server_name from the same host_display_name, not from a second evaluation;
#   3. panel.env.example documents the key.
assert_the_panel_host_name_is_recorded_from_the_name_the_vhost_serves() {
  local answer placeholder hostile too_long documented

  answer="$(panel_host_name_for 'Shop.Example.COM')"
  [ "$answer" = "shop.example.com" ] \
    || fail "detect_panel_host_name answered '${answer}' for 'Shop.Example.COM'; expected shop.example.com"

  for placeholder in '_' 'localhost' 'LocalHost'; do
    answer="$(panel_host_name_for "$placeholder")"
    [ -z "$answer" ] \
      || fail "detect_panel_host_name answered '${answer}' for the placeholder '${placeholder}'; it must record nothing,
because a written placeholder would reserve a name that is not the panel's."
  done

  for hostile in 'a\nPanel__Other=1' 'a b' 'a;b' 'a=b' 'a_b' 'a/b'; do
    answer="$(panel_host_name_for "$hostile")"
    [ -z "$answer" ] \
      || fail "detect_panel_host_name answered '${answer}' for the hostile name '${hostile}'; a byte outside letters, digits,
dots and hyphens must record nothing, or a machine name becomes a second setting in panel.env."
  done

  too_long="$(printf 'a%.0s' $(seq 1 254))"
  answer="$(panel_host_name_for "$too_long")"
  [ -z "$answer" ] || fail "detect_panel_host_name accepted a 254-character name; the limit is 253"
  too_long="$(printf 'a%.0s' $(seq 1 253))"
  answer="$(panel_host_name_for "$too_long")"
  [ "$answer" = "$too_long" ] || fail "detect_panel_host_name refused a 253-character name, the longest valid one"

  grep -q 'Panel__HostName=\${panel_host_name}' "$CONFIG_STEP" \
    || fail "60-config.sh no longer writes Panel__HostName from the detected panel_host_name"
  grep -q 'panel_host_name="\$(detect_panel_host_name)"' "$CONFIG_STEP" \
    || fail "step_config no longer takes the panel host name from detect_panel_host_name"
  grep -q 'host_display_name' "$NGINX_STEP" \
    || fail "80-nginx.sh no longer renders server_name from host_display_name, the value panel.env records;
the vhost and the API's reserved name can now drift apart."
  if grep -v '^[[:space:]]*#' "$NGINX_STEP" | grep -q 'hostname -f'; then
    fail "80-nginx.sh evaluates 'hostname -f' on its own again; the vhost's server_name is no longer the recorded value"
  fi

  documented="$(grep -oE '^Panel__HostName=' "$PANEL_ENV_EXAMPLE" || true)"
  [ -n "$documented" ] || fail "panel.env.example no longer documents Panel__HostName, which the installer writes"

  # The behaviour itself, not the text of the source: write_config is executed. The positive control
  # is the non-empty run, which must contain exactly one line; the empty run must contain none.
  local written_with_name written_without_name count
  written_with_name="$(panel_env_written_with 'shop.example.com')"
  count="$(printf '%s\n' "$written_with_name" | grep -c '^Panel__HostName=shop.example.com$' || true)"
  [ "$count" = "1" ] || fail "write_config wrote ${count} 'Panel__HostName=shop.example.com' lines for a detected name; expected exactly one"
  printf '%s\n' "$written_with_name" | grep -q '^Firewall__PanelPort=8443$' \
    || fail "the executed write_config produced no Firewall__PanelPort line, so this check cannot see what it reports on"
  written_without_name="$(panel_env_written_with '')"
  printf '%s\n' "$written_without_name" | grep -q '^Firewall__PanelPort=8443$' \
    || fail "the executed write_config produced no Firewall__PanelPort line for the empty run, so this check is blind"
  if printf '%s\n' "$written_without_name" | grep -q '^Panel__HostName='; then
    fail "write_config wrote a Panel__HostName line although no host name was detected; absent must mean absent"
  fi

  echo "Panel__HostName is the lower-cased name the vhost serves; placeholders, injection shapes and over-long names record nothing."

  assert_the_panel_public_url_is_chosen_and_written_only_when_valid
  assert_the_agent_is_told_every_name_the_panel_is_served_under
}

# agent_env_written_with: runs the installer's own write_agent_env, unmodified, against a scratch
# directory with `$1` as the panel host name and `$2` as the panel public address, and prints the
# agent.env it produced. Only what needs a real machine is a stand-in: the service identity check,
# `id`, chown and chmod. The rendering and the list of names are the real ones.
agent_env_written_with() {
  local scratch output
  scratch="$(mktemp -d)"
  output="$(bash -c 'set -euo pipefail
. "$1"
. <(sed -e "s#^readonly MARAN_CONFIG_DIR=.*#readonly MARAN_CONFIG_DIR=\"$3\"#" "$2")
MARAN_USER=maran
MARAN_GROUP=maran
require_service_identity() { :; }
id() { echo 101; }
chown() { :; }
chmod() { :; }
write_agent_env "$4" "$5"
cat "$3/agent.env"' _ "$COMMON_LIB" "$CONFIG_STEP" "$scratch" "$1" "$2")" || { rm -rf "$scratch"; fail "write_agent_env failed when run for real"; }
  rm -rf "$scratch"
  printf '%s\n' "$output"
}

# assert_the_agent_is_told_every_name_the_panel_is_served_under: issue #468. The API reserves the
# vhost's name AND the host of Panel__PublicUrl, and the agent refuses the same names at its own
# boundary, learning them from agent.env. Propositions, each executed rather than grepped:
#   1. both names reach agent.env, once each, whichever of them differ;
#   2. an address, an IPv6 literal and localhost add no name (the fixed set refuses them), and a host
#      with neither writes the key EMPTY, which the unit passes as one empty argument;
#   3. the unit hands the key to the agent through `--reserved-host-names=`, glued with `=`;
#   4. agent.env.example documents the key.
# The positive control is the ALLOW_UID line, present in every run, so an empty result is not an
# unread file. UNOBSERVED HERE: systemd's expansion of the unit line, and the agent's own parse of it
# (the agent's invocation tests hold the empty and the repeated forms).
assert_the_agent_is_told_every_name_the_panel_is_served_under() {
  local written line

  written="$(agent_env_written_with 'srv1.provider.net' 'https://Panel.Example.com:8443')"
  printf '%s\n' "$written" | grep -q '^MARAN_AGENT_ALLOW_UID=101$' \
    || fail "the executed write_agent_env produced no MARAN_AGENT_ALLOW_UID line, so this check cannot see what it reports on"
  line="$(printf '%s\n' "$written" | grep '^MARAN_AGENT_RESERVED_HOST_NAMES=' || true)"
  [ "$line" = "MARAN_AGENT_RESERVED_HOST_NAMES=srv1.provider.net,panel.example.com" ] \
    || fail "agent.env carries '${line}' for a vhost name and a different public host; expected both names, once each"

  written="$(agent_env_written_with 'panel.example.com' 'https://panel.example.com')"
  line="$(printf '%s\n' "$written" | grep '^MARAN_AGENT_RESERVED_HOST_NAMES=' || true)"
  [ "$line" = "MARAN_AGENT_RESERVED_HOST_NAMES=panel.example.com" ] \
    || fail "agent.env carries '${line}' when the vhost name and the public host are the same; expected the name once"

  for line in 'https://203.0.113.7:8443' 'https://[2001:db8::1]:8443' 'https://localhost:8443'; do
    written="$(agent_env_written_with 'srv1.provider.net' "$line")"
    printf '%s\n' "$written" | grep -qx 'MARAN_AGENT_RESERVED_HOST_NAMES=srv1.provider.net' \
      || fail "agent.env gained a name from the public address '${line}', which the fixed reserved set already refuses"
  done

  written="$(agent_env_written_with '' '')"
  printf '%s\n' "$written" | grep -q '^MARAN_AGENT_ALLOW_UID=101$' \
    || fail "the executed write_agent_env produced no MARAN_AGENT_ALLOW_UID line for the empty run, so this check is blind"
  printf '%s\n' "$written" | grep -qx 'MARAN_AGENT_RESERVED_HOST_NAMES=' \
    || fail "agent.env must carry the key EMPTY when the host has no name; absent is a different state the unit also tolerates, but an empty one is what the installer promises"

  grep -qF -- '--reserved-host-names=${MARAN_AGENT_RESERVED_HOST_NAMES}' "$AGENT_UNIT" \
    || fail "$(basename "$AGENT_UNIT") no longer passes MARAN_AGENT_RESERVED_HOST_NAMES to the agent as --reserved-host-names=...; the agent would not refuse the panel's names"
  grep -q '^MARAN_AGENT_RESERVED_HOST_NAMES=' "${INSTALLER_ROOT}/agent.env.example" \
    || fail "agent.env.example no longer documents MARAN_AGENT_RESERVED_HOST_NAMES, which the installer writes"

  echo "agent.env names every host name the panel is served under (the vhost's and the public address's), once each, and an empty host writes the key empty."
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

  # Every listen directive, as "<port> <tls|plain>": `listen 8443 ssl;` and
  # `listen [::]:8443 ssl;` are the same number written two ways, and the port is what follows
  # the last colon. The TLS listens are the panel and must equal the authority. The plain ones
  # belong to the ACME HTTP-01 block (issue #132) and must be 80: RFC 8555 fixes the challenge
  # to port 80, so that number is the protocol's and not a second panel port.
  local listen_ports tls_checked=0 port kind
  listen_ports="$(awk '$1 == "listen" { spec = $2; sub(/;$/, "", spec); n = split(spec, parts, ":");
      kind = "plain"; for (i = 3; i <= NF; i++) if ($i ~ /^ssl;?$/) kind = "tls"; print parts[n], kind }' \
    "$PANEL_VHOST")"

  while read -r port kind; do
    case "$port" in
      ''|*[!0-9]*)
        fail "a listen directive in ${PANEL_VHOST} does not end in a port this check can read: '${port}'"
        ;;
    esac
    [ "$kind" = tls ] \
      || fail "${PANEL_VHOST} has a plain-HTTP listen on ${port}; the panel vhost binds its TLS port and nothing else, and the only plain block is the ACME challenge in ${PANEL_PORT80_BLOCK}"
    [ "$port" = "$authority" ] \
      || fail "the vhost listens on ${port} while install.sh sets MARAN_PANEL_PORT=${authority}; they are one number"
    tls_checked=$((tls_checked + 1))
  done <<< "$listen_ports"

  # The ACME block lives in its own file so that the agent can take it out of nginx's tree while
  # Apache owns port 80 (issue #517): in apache mode nginx binds the panel port and NOTHING else.
  # Its listens must therefore be plain and port 80, and it must hold no other port, TLS block or
  # proxy - a second use for the file would be a reason to keep it in place when Apache needs 80.
  local port80_listens plain_checked=0
  port80_listens="$(awk '$1 == "listen" { spec = $2; sub(/;$/, "", spec); n = split(spec, parts, ":");
      kind = "plain"; for (i = 3; i <= NF; i++) if ($i ~ /^ssl;?$/) kind = "tls"; print parts[n], kind }' \
    "$PANEL_PORT80_BLOCK")"
  while read -r port kind; do
    [ "$kind" = plain ] && [ "$port" = 80 ] \
      || fail "${PANEL_PORT80_BLOCK} has a listen on '${port}' (${kind}); it is the ACME challenge block, plain HTTP on port 80 and nothing else"
    plain_checked=$((plain_checked + 1))
  done <<< "$port80_listens"
  [ "$plain_checked" -gt 0 ] \
    || fail "no listen directive was found in ${PANEL_PORT80_BLOCK}; a check that reads nothing agrees with everything"
  # The vacuity guard is on the TLS axis: a vhost whose TLS listens vanished would otherwise
  # pass on its challenge block alone.
  [ "$tls_checked" -gt 0 ] \
    || fail "no TLS listen directive was found in ${PANEL_VHOST}; a check that reads nothing agrees with everything"

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

  echo "All ${tls_checked} TLS listen directives of the panel vhost (which binds no port 80; the ${plain_checked} port-80 listens are the ACME block in maran-port80.conf), panel.env.example, 60-config.sh and 10-preflight.sh follow"
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
