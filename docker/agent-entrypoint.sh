#!/usr/bin/env bash
# Entry point of the development-only `agent` service in docker-compose.dev.yml.
#
# The polygon image is a real host in every respect except that it boots no init system, so
# nothing inside it is running when the container starts. The agent needs two things that an
# init would otherwise have started: a MariaDB server (every database and every backup rpc talks
# to it) and the socket directory. This script starts MariaDB, waits until it answers, and then
# EXECS the agent, so the agent is pid 1 and `docker stop` reaches it directly.
#
# `exec` is not a detail. A wrapper that merely runs the agent as a child would swallow its exit
# status and keep the container alive after the daemon died, which is exactly the shape of the
# trap this repository keeps meeting: a container that exits 0 having executed nothing.
set -euo pipefail

socket="${MARAN_AGENT_SOCKET:-/run/maran-dev/agent.sock}"
allow_uid="${MARAN_AGENT_ALLOW_UID:-1000}"

if [ ! -x /usr/local/bin/maran-agent ]; then
  echo "no agent binary at /usr/local/bin/maran-agent — build it first: maran agent build" >&2
  exit 1
fi

# MariaDB, the way the polygon image itself starts it at build time: no networking, no syslog.
# `--skip-networking` is deliberate — the agent reaches MariaDB over its unix socket, and a
# development database listening on a host-networked container's port 3306 would be reachable by
# anything on the machine.
if ! mariadb-admin ping >/dev/null 2>&1; then
  install -d -o mysql -g mysql -m 0755 /run/mysqld
  mariadbd-safe --skip-networking --skip-syslog >/var/log/mariadb-dev.log 2>&1 &
  for _ in $(seq 1 60); do
    mariadb-admin ping >/dev/null 2>&1 && break
    sleep 1
  done
fi
if ! mariadb-admin ping >/dev/null 2>&1; then
  echo "MariaDB did not come up inside the agent container; last log lines:" >&2
  tail -20 /var/log/mariadb-dev.log >&2 || true
  exit 1
fi
echo "mariadb: $(mariadb --version)"

# The socket directory is a bind mount from the repository, so its owner and mode are the
# developer's. The agent creates the socket itself and sets it 0660; its GROUP is the process's
# gid, which compose sets to the developer's — see docker-compose.dev.yml for why that is the
# production ownership model rather than a hole in it.
mkdir -p "$(dirname "$socket")"
rm -f "$socket"

exec maran-agent --socket "$socket" --allow-uid "$allow_uid"
