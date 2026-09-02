#!/usr/bin/env bash
# Runs the installer's own database and SFTP steps inside a polygon image and
# asserts what they left behind. Executed at BUILD time by both
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

readonly INSTALLER_LIB="/tmp/maran-installer/lib"
readonly SSHD_CONFIG="/etc/ssh/sshd_config"

# A throwaway credential, used only to put this container's MariaDB into the
# broken states below and then take it back out. It is not a secret and it never
# leaves the build layer: the negative assertions need a server that really has a
# root password, and there is no way to assert that the installer refuses one
# without creating one.
readonly THROWAWAY_ROOT_PASSWORD="polygon-throwaway"

# shellcheck source=/dev/null
. "${INSTALLER_LIB}/85-mysql.sh"
# shellcheck source=/dev/null
. "${INSTALLER_LIB}/86-sftp.sh"

# fail: an assertion that did not hold, named, on stderr.
fail() {
  echo "assert-installer-steps.sh: $1" >&2
  exit 1
}

# sql: one statement, as root over the socket, with an optional password for the
# states where root has one. Double quotes around identifiers and literals so the
# statement survives the Dockerfile's single-quoted RUN string.
sql() {
  /usr/bin/mysql -u root "$@"
}

# assert_mysql_gate_accepts_socket_auth: the installer's gate, against the server
# as the family's own package leaves it. This is the positive case, and the one
# that would silently stop being run if verify_mysql_socket_auth were deleted —
# the function call below would then be "command not found" and the build fails.
assert_mysql_gate_accepts_socket_auth() {
  [ -x /usr/bin/mysql ] || fail "/usr/bin/mysql is missing; it is the path the agent execs"
  verify_mysql_socket_auth
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
  if output="$( (verify_mysql_socket_auth) 2>&1 )"; then
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
  install_sftp_prerequisites
  install_sftp_prerequisites

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

  if (install_sshd_match_block) >/dev/null 2>&1; then
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

main() {
  assert_mysql_gate_accepts_socket_auth
  assert_mysql_gate_refuses_passwordless_root
  assert_mysql_gate_refuses_password_root
  restore_socket_auth
  assert_mysql_gate_accepts_socket_auth

  assert_sftp_prerequisites
  assert_sftp_validates_before_replacing
  echo "Installer steps 85 and 86 verified inside the polygon."
}

main "$@"
