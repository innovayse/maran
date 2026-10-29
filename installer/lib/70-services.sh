#!/usr/bin/env bash
# Step 70: install the systemd units from installer/systemd/ and enable the panel's two.
# The package-install template unit is installed too, but never enabled: only the agent starts it.
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
# The snippet that builds the PHP-FPM pool socket directory. Unlike the api's it has no
# placeholders, so it is installed as it stands (installer/systemd/maran-php.tmpfiles.conf says why
# that directory is not under /run/maran).
readonly MARAN_PHP_TMPFILES_NAME="maran-php.conf"
# The directory that snippet builds. The agent writes pool sockets here
# (AgentPaths::PHP_FPM_SOCKET_DIRECTORY) and the agent unit's ReadWritePaths= names it; a rename
# here without one there breaks every PHP site.
readonly MARAN_PHP_SOCKET_DIRECTORY="/run/maran-php"
# The template unit through which the agent installs a PHP version as root, outside its own sandbox
# (issue #93). The file name is the contract with the agent: ops/src/php starts
# maran-php-install@<package>.service, so a rename here without one there installs nothing.
readonly MARAN_PHP_INSTALL_UNIT_NAME="maran-php-install@.service"

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
  : "${MARAN_USER:?must be set by install.sh before this step is sourced}"
  : "${MARAN_GROUP:?must be set by install.sh before this step is sourced}"
  socket_dir="$(api_socket_directory)"
  sed \
    -e "s#__MARAN_API_SOCKET_DIR__#${socket_dir}#g" \
    -e "s#__MARAN_API_SOCKET__#${MARAN_API_SOCKET_PATH}#g" \
    -e "s#__MARAN_USER__#${MARAN_USER}#g" \
    -e "s#__MARAN_GROUP__#${MARAN_GROUP}#g" \
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
  : "${MARAN_USER:?must be set by install.sh before this step is sourced}"
  sed \
    -e "s#__MARAN_API_SOCKET_DIR__#${socket_dir}#g" \
    -e "s#__MARAN_WEB_GROUP__#${MARAN_WEB_SERVER_GROUP}#g" \
    -e "s#__MARAN_USER__#${MARAN_USER}#g" \
    "${LIB_DIR}/../systemd/maran-api.tmpfiles.conf" > "$out"
  refuse_unsubstituted_placeholder "$out" maran-api.tmpfiles.conf
}

# render_agent_unit: substitutes the agent unit's one placeholder, the service GROUP.
#
# The agent runs as root, and the group of the root process is what the unix socket it creates
# inherits — so this one word is what lets the api reach the only root process on the server. The
# unit used to be installed unrendered because it named nothing an installer decides; it names the
# service group now, and that name is decided in install.sh like the port and the socket path.
# quota_device_binds: the BindPaths= lines that let the agent see the one block device it needs,
# or a comment saying why there are none.
#
# PrivateDevices=true hides every block device from the agent, and `setquota` must stat the device
# behind the filesystem it limits — so without this, creating a hosting account failed with
# "Cannot stat() mounted device" and the panel answered 500 (issue #73). The unit's own comment
# carries the measurement.
#
# BOTH names are bound. The mount table usually gives a symlink (`/dev/mapper/vg-lv`) that resolves
# to the real node (`/dev/dm-0`); setquota opens the name it was handed, and the kernel then needs
# the target, so binding one of the two is not enough.
#
# A host where the device cannot be resolved gets no binds and a comment instead of a broken unit:
# quotas will not work there, which is a limitation the panel reports through its own
# QuotaNotEnforceable alert, rather than an install that refuses to finish.
quota_device_binds() {
  local target="/home" source resolved
  source="$(findmnt -no SOURCE --target "$target" 2>/dev/null | head -1)"
  if [ -z "$source" ] || [ ! -e "$source" ]; then
    echo "# No block device resolved for ${target}; disk quotas will not be enforceable here."
    return 0
  fi

  echo "BindPaths=${source}"
  resolved="$(readlink -f "$source" 2>/dev/null)"
  if [ -n "$resolved" ] && [ "$resolved" != "$source" ] && [ -e "$resolved" ]; then
    echo "BindPaths=${resolved}"
  fi
}

render_agent_unit() {
  local out="$1" binds
  : "${MARAN_GROUP:?must be set by install.sh before this step is sourced}"
  binds="$(quota_device_binds)"
  # Rendered through awk rather than sed because the replacement is MULTI-LINE: `sed s###` with a
  # newline in the replacement is a portability trap, and this one is two lines on most hosts.
  awk -v group="$MARAN_GROUP" -v binds="$binds" '
    { gsub(/__MARAN_GROUP__/, group) }
    /^__MARAN_QUOTA_DEVICE_BINDS__$/ { print binds; next }
    { print }
  ' "${LIB_DIR}/../systemd/maran-agent.service" > "$out"
  refuse_unsubstituted_placeholder "$out" maran-agent.service
}

# render_php_install_unit: renders the package-install template unit for this host's OS family.
#
# The two families name different package managers, and a systemd unit interpolates no shell
# variable, so the family's ExecStart= and Environment= lines are substituted here. Each is ONE fixed
# argv: an absolute program, fixed options, and %i (the instance, which the agent writes as the
# adapter's package name) as the last and only package argument. %i is a systemd specifier, not a
# shell variable, so nothing below touches it. The paths are the ones DistroAdapter::package_manager
# answers; they are spelled in the unit because systemd needs an absolute path at load time.
#
# --no-remove (Debian family) aborts an install that could only proceed by removing another
# package; dnf removes nothing unless given --allowerasing, which is never passed. The Debian family
# also sets DEBIAN_FRONTEND=noninteractive on purpose; the RHEL family has no equivalent, so its
# environment placeholder becomes a comment rather than an empty line.
#
# A family other than debian or rhel is a refusal, never a default: a unit rendered for a package
# manager the host does not have would fail only when an operator first installs a PHP version.
render_php_install_unit() {
  local out="$1" exec_start environment
  : "${MARAN_OS_FAMILY:?must be set by install.sh before this step is sourced}"
  case "$MARAN_OS_FAMILY" in
    debian)
      exec_start="ExecStart=/usr/bin/apt-get install -y --no-remove -- %i"
      environment="Environment=DEBIAN_FRONTEND=noninteractive"
      ;;
    rhel)
      exec_start="ExecStart=/usr/bin/dnf install -y -- %i"
      environment="# No debconf on this family, so no frontend variable to set."
      ;;
    *)
      echo "70-services.sh: no package-install unit for OS family '${MARAN_OS_FAMILY}'; aborting." >&2
      exit 1
      ;;
  esac
  # awk, not sed: the replacement holds `%` and spaces, and sed's `s###` is a portability trap there.
  # The values are passed with -v, which processes backslash escapes; neither value contains one.
  awk -v exec_start="$exec_start" -v environment="$environment" '
    /^__MARAN_PKG_EXEC_START__$/ { print exec_start; next }
    /^__MARAN_PKG_ENVIRONMENT__$/ { print environment; next }
    { print }
  ' "${LIB_DIR}/../systemd/${MARAN_PHP_INSTALL_UNIT_NAME}" > "$out"
  refuse_unsubstituted_placeholder "$out" "$MARAN_PHP_INSTALL_UNIT_NAME"
}

install_units() {
  local tmp
  tmp="$(mktemp)"
  render_api_unit "$tmp"
  install -m 0644 "$tmp" "${MARAN_UNIT_DIR}/maran-api.service"
  render_api_runtime_dir "$tmp"
  install -d -m 0755 "$MARAN_TMPFILES_DIR"
  install -m 0644 "$tmp" "${MARAN_TMPFILES_DIR}/${MARAN_API_TMPFILES_NAME}"
  # Nothing to substitute, so installed straight from the source file.
  install -m 0644 "${LIB_DIR}/../systemd/maran-php.tmpfiles.conf" "${MARAN_TMPFILES_DIR}/${MARAN_PHP_TMPFILES_NAME}"
  render_agent_unit "$tmp"
  install -m 0644 "$tmp" "${MARAN_UNIT_DIR}/maran-agent.service"
  # Installed, NEVER enabled: it has no [Install] section and only the agent starts it.
  render_php_install_unit "$tmp"
  install -m 0644 "$tmp" "${MARAN_UNIT_DIR}/${MARAN_PHP_INSTALL_UNIT_NAME}"
  rm -f "$tmp"
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

# build_php_socket_directory: apply the PHP socket snippet now, for the same reasons as the api's.
#
# The agent writes a pool's socket into this directory the first time a PHP site is created, and
# php-fpm and nginx both resolve a path through it, so it must exist at the right owner and mode
# before the agent starts. `--create` also corrects a directory changed by hand.
build_php_socket_directory() {
  systemd-tmpfiles --create "${MARAN_TMPFILES_DIR}/${MARAN_PHP_TMPFILES_NAME}"
}

# assert_php_socket_directory: the PHP socket directory observed on this host rather than assumed.
#
# Must be 755 root:root. Anything narrower and the web server's user cannot resolve a path inside
# it, so every PHP site answers 502 while the install reports success; anything writable by
# another uid and a tenant could bind or unlink a socket under another pool's name.
assert_php_socket_directory() {
  local expected="755 root root" observed
  observed="$(stat -c '%a %U %G' "$MARAN_PHP_SOCKET_DIRECTORY" 2>/dev/null || true)"
  if [ "$observed" != "$expected" ]; then
    cat >&2 <<EOF
70-services.sh: ${MARAN_PHP_SOCKET_DIRECTORY} is '${observed:-absent}' but must be '${expected}'.

It is built by ${MARAN_TMPFILES_DIR}/${MARAN_PHP_TMPFILES_NAME}; check that file and that
'systemd-tmpfiles --create' accepted it.
EOF
    exit 1
  fi
  echo "PHP socket directory ${MARAN_PHP_SOCKET_DIRECTORY} is ${expected}."
}

# assert_api_socket_directory: the boundary itself, observed on this host rather than assumed.
#
# This is the one host fact the whole peer-credential design rests on, and it is the fact an
# earlier version of this step got wrong while every text-level check went on passing: the
# directory must be 2710 owned <service account>:<web server group>. 0710 has no permissions for
# "other", so a
# customer's uid cannot resolve a path inside it; the group is what lets nginx traverse it at all.
# Checked here, on the real directory, because nothing else in the product can see it — a grep over
# a unit file is not evidence about a directory.
assert_api_socket_directory() {
  local socket_dir observed expected
  socket_dir="$(api_socket_directory)"
  : "${MARAN_USER:?must be set by install.sh before this step is sourced}"
  expected="2710 ${MARAN_USER} ${MARAN_WEB_SERVER_GROUP}"
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
  : "${MARAN_USER:?must be set by install.sh before this step is sourced}"
  expected="660 ${MARAN_USER} ${MARAN_WEB_SERVER_GROUP}"
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

# wait_for_agent_socket: the ROOT daemon really started, and its socket really came out reachable
# by the panel and by nobody else.
#
# The panel's socket had this wait from the beginning and the agent's had nothing but a
# `systemctl restart`, whose exit status reports only that a process was spawned. That asymmetry
# meant the step which installs the root daemon could not observe whether the root daemon runs
# (rules/testing.md), and it hid a real failure: on a real install (Ubuntu 24.04.5) the agent died
# with 226/NAMESPACE on every retry until systemd gave up, and this step still returned 0, the
# install still printed "Maran is installed and reachable", and only /health dissented with
# "agent":"unavailable" (issue #42).
#
# 660 root:<service group> is the boundary this asserts, measured on a working install: root owns
# the socket because the agent IS root, the group is the panel's so the panel may connect, and
# nothing outside that group may. A wrong group here is not a degraded panel but a panel with no
# privileged operations at all — every site, user, database and certificate action fails.
wait_for_agent_socket() {
  local expected observed attempt=0
  : "${MARAN_GROUP:?must be set by install.sh before this step is sourced}"
  expected="660 root ${MARAN_GROUP}"
  while [ "$attempt" -lt 60 ]; do
    if [ -S "$MARAN_AGENT_SOCKET_PATH" ]; then
      observed="$(stat -c '%a %U %G' "$MARAN_AGENT_SOCKET_PATH" 2>/dev/null || true)"
      [ "$observed" = "$expected" ] && { echo "Agent socket ${MARAN_AGENT_SOCKET_PATH} is ${expected}."; return 0; }
    fi
    attempt=$((attempt + 1))
    sleep 1
  done
  cat >&2 <<EOF
70-services.sh: ${MARAN_AGENT_SOCKET_PATH} is '${observed:-absent}' after 60s but must be '${expected}'.

Absent means the root agent never started, so the panel would come up with every privileged
operation failing. A unit that exits 226/NAMESPACE never ran its binary at all and logged nothing
of its own: check systemd's account as well as the agent's.

    systemctl status maran-agent.service --no-pager
    journalctl -u maran-agent.service -n 50 --no-pager
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
  # Same precondition for the PHP pool sockets: the agent unit's ReadWritePaths= names this path.
  build_php_socket_directory
  assert_php_socket_directory
  # Start the agent first: the api's health check depends on an agent handshake, and
  # starting order here matches the After= dependency declared in maran-api.service.
  systemctl restart maran-agent.service
  # Waiting HERE rather than beside the api's wait is deliberate: the api's health check depends on
  # an agent handshake, so letting the agent bind before the api starts makes the order the After=
  # dependency declares actually hold at runtime, not just on paper.
  wait_for_agent_socket
  systemctl restart maran-api.service
  wait_for_api_socket
  echo "Services installed and started."
}
