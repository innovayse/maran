#!/usr/bin/env bash
# Step 70: install the hardened systemd units from installer/systemd/ and enable them.
# Does not start them yet — 80-nginx.sh must install the vhost first so the api's first
# health check has something proxying to it, and both are started together at the end
# of this step's counterpart call order in install.sh (api after nginx is configured
# is not required by systemd itself, only by operator expectation, so we start here
# and let 80-nginx.sh reload nginx afterward).
set -euo pipefail

readonly MARAN_UNIT_DIR="/etc/systemd/system"
# The panel's socket directory is built by systemd-tmpfiles rather than by the api unit's
# RuntimeDirectory=, and installer/systemd/maran-api.tmpfiles.conf says at length why. /etc, not
# /usr/lib: this is a locally installed product and its unit files go to /etc/systemd/system for
# the same reason, so an operator looking for what Maran put on their machine finds all of it in
# one place.
readonly MARAN_TMPFILES_DIR="/etc/tmpfiles.d"
readonly MARAN_API_TMPFILES_NAME="maran-api.conf"

# api_socket_directory: the directory half of MARAN_API_SOCKET_PATH.
#
# Derived, never written down a second time: install.sh decides the socket's full path, and a
# directory spelled separately here is a value that can disagree with it. The unit's
# ReadWritePaths= and the tmpfiles snippet both take it from this one function.
api_socket_directory() {
  : "${MARAN_API_SOCKET_PATH:?must be set by install.sh before this step is sourced}"
  printf '%s' "${MARAN_API_SOCKET_PATH%/*}"
}

# refuse_unsubstituted_placeholder: abort if a rendered file still carries a __MARAN_…__ token.
#
# A placeholder that survives substitution installs a file that names nothing — a unit whose
# ReadWritePaths= is a literal `__MARAN_API_SOCKET_DIR__`, or a tmpfiles line naming a group that
# does not exist. Both fail on a server, at a distance from the mistake, with a message about
# something else. Cheaper to catch here.
refuse_unsubstituted_placeholder() {
  local rendered="$1" source_name="$2"
  if grep -q '__MARAN_' "$rendered"; then
    echo "70-services.sh: ${source_name} still contains an unsubstituted placeholder; aborting." >&2
    exit 1
  fi
}

# render_api_unit: substitutes the api unit's placeholders into a temp file.
#
# The unit names the socket and the directory holding it, because ProtectSystem=strict makes the
# whole filesystem read-only unless ReadWritePaths= names an exception, and because the socket a
# killed panel leaves behind has to be removed before the next start can bind. Both come from
# MARAN_API_SOCKET_PATH — a systemd unit interpolates no shell variable, which is the same reason
# 80-nginx.sh renders the vhost rather than shipping it.
#
# The web server's GROUP is no longer substituted here: the unit does not name it any more, and
# must not. See render_api_runtime_dir.
render_api_unit() {
  local out="$1" socket_dir
  socket_dir="$(api_socket_directory)"
  sed \
    -e "s#__MARAN_API_SOCKET_DIR__#${socket_dir}#g" \
    -e "s#__MARAN_API_SOCKET__#${MARAN_API_SOCKET_PATH}#g" \
    "${LIB_DIR}/../systemd/maran-api.service" > "$out"
  refuse_unsubstituted_placeholder "$out" maran-api.service
}

# render_api_runtime_dir: substitutes the tmpfiles snippet that builds the panel's socket directory.
#
# This file, and not the unit, is where the web server's group belongs. systemd re-applies a unit's
# User=/Group= to its RuntimeDirectory= on every command invocation of that unit, so a group set
# from inside the unit is undone before ExecStart runs and nginx is left unable to reach the
# socket — measured on booted systemd on both families. systemd-tmpfiles runs ahead of the unit and
# nothing the unit does re-applies anything over it. MARAN_WEB_SERVER_GROUP in install.sh is the
# one authority for the name.
render_api_runtime_dir() {
  local out="$1" socket_dir
  : "${MARAN_WEB_SERVER_GROUP:?must be set by install.sh before this step is sourced}"
  socket_dir="$(api_socket_directory)"
  sed \
    -e "s#__MARAN_API_SOCKET_DIR__#${socket_dir}#g" \
    -e "s#__MARAN_WEB_GROUP__#${MARAN_WEB_SERVER_GROUP}#g" \
    "${LIB_DIR}/../systemd/maran-api.tmpfiles.conf" > "$out"
  refuse_unsubstituted_placeholder "$out" maran-api.tmpfiles.conf
}

install_units() {
  local tmp
  tmp="$(mktemp)"
  render_api_unit "$tmp"
  install -m 0644 "$tmp" "${MARAN_UNIT_DIR}/maran-api.service"
  render_api_runtime_dir "$tmp"
  install -d -m 0755 "$MARAN_TMPFILES_DIR"
  install -m 0644 "$tmp" "${MARAN_TMPFILES_DIR}/${MARAN_API_TMPFILES_NAME}"
  rm -f "$tmp"
  install -m 0644 "${LIB_DIR}/../systemd/maran-agent.service" "${MARAN_UNIT_DIR}/maran-agent.service"
}

# build_api_socket_directory: apply the tmpfiles snippet now, rather than waiting for a reboot.
#
# systemd-tmpfiles-setup.service reads /etc/tmpfiles.d at every boot, but this install must not
# require one: the api is started a few lines below and needs the directory to exist, at the right
# ownership, before it binds. `--create` also CORRECTS a directory that already exists, which is
# what makes re-running the installer put right a host where the ownership was changed by hand.
build_api_socket_directory() {
  systemd-tmpfiles --create "${MARAN_TMPFILES_DIR}/${MARAN_API_TMPFILES_NAME}"
}

# assert_api_socket_directory: the boundary itself, observed on this host rather than assumed.
#
# This is the one host fact the whole peer-credential design rests on, and it is the fact an
# earlier version of this step got wrong while every text-level check went on passing: the
# directory must be 2710 owned panel:<web server group>. 0710 has no permissions for "other", so a
# customer's uid cannot resolve a path inside it; the group is what lets nginx traverse it at all.
# Checked here, on the real directory, because nothing else in the product can see it — a grep over
# a unit file is not evidence about a directory.
assert_api_socket_directory() {
  local socket_dir observed expected
  socket_dir="$(api_socket_directory)"
  expected="2710 panel ${MARAN_WEB_SERVER_GROUP}"
  observed="$(stat -c '%a %U %G' "$socket_dir" 2>/dev/null || true)"
  if [ "$observed" != "$expected" ]; then
    cat >&2 <<EOF
70-services.sh: ${socket_dir} is '${observed:-absent}' but must be '${expected}'.

That directory is the panel's trust boundary: at mode 2710 no other uid on this machine can
resolve a path inside it, and the group is what lets nginx reach the socket. With the wrong
ownership the panel answers 502 to every API call, or — worse — becomes reachable by accounts
that must never reach it. It is built by ${MARAN_TMPFILES_DIR}/${MARAN_API_TMPFILES_NAME};
check that file and that 'systemd-tmpfiles --create' accepted it.
EOF
    exit 1
  fi
  echo "Panel socket directory ${socket_dir} is ${expected}."
}

# wait_for_api_socket: the api really bound its socket, and the socket really came out reachable
# by nginx and by nobody else.
#
# Type=simple means `systemctl restart` returns when the process was spawned, not when it bound, so
# this waits rather than looks once. What it then checks is the other half of the boundary: mode
# 660 (the panel narrows it at startup; Kestrel creates it world-connectable) and the web server's
# group, inherited from the setgid directory. An install that ends without this has handed the
# operator a panel that answers 502 and a log line saying everything went well.
wait_for_api_socket() {
  local expected observed attempt=0
  expected="660 panel ${MARAN_WEB_SERVER_GROUP}"
  while [ "$attempt" -lt 60 ]; do
    if [ -S "$MARAN_API_SOCKET_PATH" ]; then
      observed="$(stat -c '%a %U %G' "$MARAN_API_SOCKET_PATH" 2>/dev/null || true)"
      [ "$observed" = "$expected" ] && { echo "Panel socket ${MARAN_API_SOCKET_PATH} is ${expected}."; return 0; }
    fi
    attempt=$((attempt + 1))
    sleep 1
  done
  cat >&2 <<EOF
70-services.sh: ${MARAN_API_SOCKET_PATH} is '${observed:-absent}' after 60s but must be '${expected}'.

Absent means the api never bound its socket; a different owner, group or mode means nginx cannot
open it and every API call will answer 502. Read the panel's own account of it first:

    journalctl -u maran-api.service -n 50 --no-pager
EOF
  exit 1
}

step_services() {
  echo "Installing systemd units..."
  install_units
  systemctl daemon-reload
  systemctl enable maran-agent.service maran-api.service
  # Before the api starts, and it is a precondition rather than a tidy-up: the unit's
  # ReadWritePaths= names this directory, so a unit whose directory does not exist does not start.
  build_api_socket_directory
  assert_api_socket_directory
  # Start the agent first: the api's health check depends on an agent handshake, and
  # starting order here matches the After= dependency declared in maran-api.service.
  systemctl restart maran-agent.service
  systemctl restart maran-api.service
  wait_for_api_socket
  echo "Services installed and started."
}
