#!/usr/bin/env bash
# Starts the services of the migration source and keeps the container in the foreground on sshd.
set -Eeuo pipefail

mysqld_safe >/dev/null 2>&1 &
nginx
cron
exec /usr/sbin/sshd -D -e
