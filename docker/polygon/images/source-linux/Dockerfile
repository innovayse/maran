# The SOURCE a whole-server migration is rehearsed against (issue #197, phase 2): an ordinary Linux host that
# serves ten generated hosting accounts and has never heard of Maran. It runs sshd (key login for root, the key
# the test harness generates), nginx with one virtual host per account, MariaDB with one database per account,
# a crontab per account and the `rsync` the pull needs. It is read by the target agent over SSH and written by
# nothing: a migration never writes to a generic source (decision D15).
#
# Built by `maran polygon build source-linux`; run by docker/polygon/asserts/server-migration.sh, which is the
# only caller. Nothing here is installed on a real host and nothing in this directory ships.
FROM ubuntu:24.04

ARG ACCOUNTS=10

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
 && apt-get install -y --no-install-recommends openssh-server rsync nginx mariadb-server cron ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /run/sshd

# Ten accounts, each with a home holding a page and a file, a database named for the account that the account owns,
# a crontab with one entry and an nginx virtual host for <account>.source.test. The names are generated, never typed:
# the assertion script reads them back from /etc/passwd the way the inventory does.
COPY populate.sh /usr/local/bin/populate-source
RUN chmod 0755 /usr/local/bin/populate-source && ACCOUNTS="${ACCOUNTS}" /usr/local/bin/populate-source

# Root logs in with a key only; the harness writes /root/.ssh/authorized_keys with the restricted line the panel
# shows (the target's address, no forwarding, no pty) before it starts the container.
RUN printf 'PermitRootLogin prohibit-password\nPasswordAuthentication no\nUsePAM no\n' > /etc/ssh/sshd_config.d/10-source.conf

EXPOSE 22
COPY start.sh /usr/local/bin/start-source
RUN chmod 0755 /usr/local/bin/start-source
CMD ["/usr/local/bin/start-source"]
