#!/usr/bin/env bash
# 85-mysql.sh's root authentication gate
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


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
