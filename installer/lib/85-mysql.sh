#!/usr/bin/env bash
# Step 85: make the host able to serve customer databases — MariaDB installed,
# running, and reachable by the agent as `root@localhost` over the UNIX SOCKET.
#
# The socket is the whole point. The agent runs as root and creates, drops and
# measures customer databases with no credential of its own: `ProcessDbHost`
# spawns `/usr/bin/mysql` with an argv array carrying no `--password` and no
# `--user`, and the server authenticates the connection by the uid on the other
# end of the socket (the `unix_socket` plugin; `auth_socket` on a MySQL-derived
# server). That is a deliberate security position, not a convenience: a password
# the agent holds is a password that can be read out of the agent's memory, its
# environment, its configuration file or its process table, and it would be the
# same password on every server we install. There is nothing to steal here.
#
# So when the plugin is NOT socket-based — an operator who set a root password by
# hand, which is the one realistic way this happens — this step FAILS LOUDLY and
# says exactly what to run. It does not prompt for that password, it does not
# store it, and it does not create a second privileged account to work around it.
# Refusing to install is the correct outcome: the alternative is a panel that
# looks installed and cannot create a database, or one that works because it now
# keeps a root password on disk.
#
# The panel's OWN database is PostgreSQL (30-postgresql.sh) and is untouched by
# this step. MariaDB here exists exclusively for customer data.
set -euo pipefail

# The plugin names that mean "authenticated by the uid on the socket". MariaDB
# calls it `unix_socket`; MySQL-derived servers call the same mechanism
# `auth_socket`. Both are accepted so this step does not have to know which server
# a family ships — it asks the server what it is doing.
readonly MARAN_MYSQL_SOCKET_PLUGIN_PATTERN='unix_socket|auth_socket'

# The client the AGENT will use, spelled the way `DistroAdapter::mysql_client_binary()`
# spells it on both families. Named here so this step verifies the path the agent
# is going to execute, rather than whatever `mysql` happens to be first on the
# installer's PATH: a `mysql` that works for the installer and a missing
# `/usr/bin/mysql` for the agent is exactly the class of gap this step exists to
# close before a customer meets it.
readonly MARAN_MYSQL_CLIENT="/usr/bin/mysql"

# mysql_packages_for_family: the server and the client, per family. Both families
# ship MariaDB rather than MySQL proper, which is why `DistroAdapter::mysql_service()`
# returns "mariadb" on each of them.
#
# Public on purpose: the polygon images install MariaDB from THIS list, so a
# package name that stops being right stops both image builds instead of waiting
# to be discovered on a customer's server.
mysql_packages_for_family() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "mariadb-server mariadb-client" ;;
    rhel)   echo "mariadb-server mariadb" ;;
    *)
      echo "85-mysql.sh: unsupported OS family '${MARAN_OS_FAMILY}'" >&2
      exit 1
      ;;
  esac
}

# mysql_service_name: the service unit, matching `DistroAdapter::mysql_service()`.
mysql_service_name() {
  echo "mariadb"
}

# mysql_root_can_connect: makes the agent's own connection and nothing else —
# `/usr/bin/mysql`, over the socket, as root, with no credential of any kind.
# Succeeds exactly when `ProcessDbHost` will succeed.
mysql_root_can_connect() {
  "$MARAN_MYSQL_CLIENT" --protocol=socket --user=root --batch --skip-column-names \
    --execute "SELECT 1" >/dev/null 2>&1
}

# mysql_root_authentication_record: whatever the server will say about how
# `root@localhost` may authenticate, printed on stdout for a pattern match.
#
# Two sources, because one is not enough. MariaDB 10.4+ keeps authentication in
# `mysql.global_priv` as JSON and lets an account hold SEVERAL methods at once;
# the `mysql.user` view can only show one of them, and on a stock Debian-family
# install it shows `mysql_native_password` — with the authentication string
# `invalid`, which no password hashes to — while the socket plugin sits in the
# JSON's `auth_or` list and is the method that actually works. Reading only the
# view is how a correctly configured host gets rejected, which this step did on
# its first run against a real Ubuntu image. MySQL-derived servers have no
# `global_priv`, so the view is the fallback rather than the primary.
mysql_root_authentication_record() {
  "$MARAN_MYSQL_CLIENT" --protocol=socket --user=root --batch --skip-column-names \
    --execute "SELECT Priv FROM mysql.global_priv WHERE User = 'root' AND Host = 'localhost'" \
    2>/dev/null && return 0
  "$MARAN_MYSQL_CLIENT" --protocol=socket --user=root --batch --skip-column-names \
    --execute "SELECT plugin FROM mysql.user WHERE User = 'root' AND Host = 'localhost'" \
    2>/dev/null || true
}

# verify_mysql_socket_auth: the gate. Two questions, because passing one of them
# alone is not the state the agent needs.
#
# 1. Can the agent connect at all, exactly as it will? A "no" here is almost
#    always an operator who set a root password by hand.
# 2. Is the socket actually what authenticated it? A root account with an EMPTY
#    password also answers "yes" to question 1, and that is not a working
#    installation — it is a server where every local user is root on every
#    customer database. Accepting it because the installer's own connection
#    happened to succeed would be this step certifying a hole it was written to
#    close.
#
# Public on purpose, and called by the polygon images against a real MariaDB: it
# is the assertion that would otherwise exist only as a comment. An image that
# checked socket auth with its own inline `mysql` call would be proving something
# about itself; running THIS function proves the installer still refuses a host
# the agent cannot use.
verify_mysql_socket_auth() {
  if [ ! -x "$MARAN_MYSQL_CLIENT" ]; then
    echo "85-mysql.sh: ${MARAN_MYSQL_CLIENT} is missing; the agent executes exactly this path." >&2
    exit 1
  fi

  if ! mysql_root_can_connect; then
    cat >&2 <<EOF
85-mysql.sh: cannot connect to MariaDB as root@localhost over the unix socket.

Maran's agent connects as root over the socket with NO stored credential, and it
will not be given one: a root password held by a root daemon is a root password
that can be stolen from it, and it would be the same password on every server we
install. So this is not something the installer can work around by asking you for
the password — the fix is on the server.

If the server is not running:

    systemctl status $(mysql_service_name)

If root@localhost has a password, switch it back to socket authentication and
re-run this installer:

    mysql -u root -p -e "ALTER USER 'root'@'localhost' IDENTIFIED VIA unix_socket;"
    mysql -u root -p -e "FLUSH PRIVILEGES;"

(On a MySQL-derived server the plugin is named auth_socket:
    ALTER USER 'root'@'localhost' IDENTIFIED WITH auth_socket;)

Existing databases are unaffected by that change; only how root logs in from this
machine changes. Nothing is installed until it is done.
EOF
    exit 1
  fi

  if ! mysql_root_authentication_record | grep -qE "$MARAN_MYSQL_SOCKET_PLUGIN_PATTERN"; then
    cat >&2 <<EOF
85-mysql.sh: root@localhost accepted a connection with no credential, but NOT because
of the unix socket — it has no password at all.

That is a server on which every local user is root on every customer database,
including the unprivileged accounts Maran creates for customers. Maran will not
install onto it.

Give root socket authentication instead, and re-run this installer:

    mysql -u root -e "ALTER USER 'root'@'localhost' IDENTIFIED VIA unix_socket;"
    mysql -u root -e "FLUSH PRIVILEGES;"

(On a MySQL-derived server the plugin is named auth_socket:
    ALTER USER 'root'@'localhost' IDENTIFIED WITH auth_socket;)
EOF
    exit 1
  fi

  echo "MariaDB root@localhost authenticates over the unix socket."
}

# mysql_server_preexists: was there a MariaDB/MySQL server on this host BEFORE this step ran?
#
# The answer decides whether an insecure root is somebody else's server to leave alone or this
# installer's own mess to clean up (issue #56), so it must be asked BEFORE `pkg_install`.
#
# Two independent signs, because either alone can be wrong: the `mysql` system database under the
# data directory means a server has been INITIALISED here (a package installed but never started
# leaves no such directory), and a server binary already on PATH means one is installed even if its
# data directory lives somewhere unusual. Either is enough to treat the server as pre-existing —
# this errs toward NOT touching a stranger's root, which is the safe direction.
mysql_server_preexists() {
  [ -d /var/lib/mysql/mysql ] && return 0
  command -v mariadbd >/dev/null 2>&1 && return 0
  command -v mysqld >/dev/null 2>&1 && return 0
  return 1
}

# secure_fresh_mysql_root: give root@localhost socket authentication on a server THIS INSTALL just
# created.
#
# Only ever called for a server the installer itself installed moments earlier, which is what makes
# it safe: there is no operator data behind that root, and its passwordless default is the
# installer's own doing. EL8 ships MariaDB 10.3, which leaves root@localhost with no password and no
# `unix_socket` plugin; EL9's 10.5 and every Debian-family package configure socket auth themselves,
# which is why no other version in the matrix reaches this.
#
# It does NOT report success. The verification that follows in step_mysql is what decides, so a
# server where this did not take is still refused rather than assumed fixed (rules/testing.md).
secure_fresh_mysql_root() {
  echo "This server's MariaDB came up with a passwordless root; giving it socket authentication."

  # The PLUGIN FIRST, and the order is not cosmetic — getting it wrong locks root out of the server.
  # EL8's MariaDB 10.3 loads no socket plugin at all: `information_schema.plugins` has no row
  # matching %socket% and root@localhost's `plugin` column is empty. An `ALTER USER ... IDENTIFIED
  # VIA unix_socket` there sets an authentication method the server cannot perform, and root can then
  # connect by NO means whatsoever. Measured, by doing it: after the bare ALTER, "cannot connect to
  # MariaDB as root@localhost over the unix socket".
  #
  # `INSTALL SONAME 'auth_socket'` is the MariaDB spelling that loads the library providing the
  # `unix_socket` plugin. It fails harmlessly when the plugin is already there.
  "$MARAN_MYSQL_CLIENT" -u root -e "INSTALL SONAME 'auth_socket';" 2>/dev/null || true

  # And the alter happens ONLY once the plugin is actually ACTIVE. Without this guard a host whose
  # plugin library is missing would be left with a root nobody can use — strictly worse than the
  # passwordless root this function was called to fix. If it is not active the server is left exactly
  # as it was and verify_mysql_socket_auth refuses with its own message.
  if ! "$MARAN_MYSQL_CLIENT" -u root -N -e \
        "SELECT plugin_status FROM information_schema.plugins WHERE plugin_name = 'unix_socket';" \
        2>/dev/null | grep -q ACTIVE; then
    echo "85-mysql.sh: the unix_socket plugin could not be loaded, so root's login method was left" >&2
    echo "  untouched rather than pointed at a plugin this server cannot perform." >&2
    return 0
  fi

  # Measured on MariaDB 10.3.39: with the plugin ACTIVE this succeeds, root still connects, and
  # mysql.user's plugin column reads `unix_socket`.
  "$MARAN_MYSQL_CLIENT" -u root -e \
    "ALTER USER 'root'@'localhost' IDENTIFIED VIA unix_socket; FLUSH PRIVILEGES;" 2>/dev/null \
    || true
}

step_mysql() {
  echo "Installing MariaDB for customer databases..."

  # Asked BEFORE the install, because afterwards there is no way to tell who created the server.
  local preexisting="no"
  mysql_server_preexists && preexisting="yes"

  # shellcheck disable=SC2046
  pkg_install $(mysql_packages_for_family)

  # `enable --now` is idempotent by design: on a re-run the unit is already
  # enabled and already running, and systemd treats both as success.
  systemctl enable --now "$(mysql_service_name)"

  # A server this installer created, whose root came up with no credential at all, is secured here
  # rather than refused: it is empty, it is nobody else's, and the insecure default is ours (issue
  # #56). A server that PRE-EXISTED keeps the refusal below untouched — Maran does not change how a
  # stranger's root logs in, whatever state it is in.
  if [ "$preexisting" = "no" ] \
     && mysql_root_can_connect \
     && ! mysql_root_authentication_record | grep -qE "$MARAN_MYSQL_SOCKET_PLUGIN_PATTERN"; then
    secure_fresh_mysql_root
  fi

  verify_mysql_socket_auth
  echo "MariaDB ready for customer databases; the panel's own PostgreSQL is untouched."
}
