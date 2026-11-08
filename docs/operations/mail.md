# Mail server (host daemons)

The panel can run a mail server for its customers' domains (issue #177): Postfix for SMTP, Dovecot
for IMAP, POP3 and delivery, Rspamd for spam filtering and DKIM signing, and a Redis-protocol
server (Redis or Valkey) that Rspamd keeps its rate-limit counters in. This page covers the
daemons on the host. Domains, mailboxes, aliases and DKIM keys arrive in later phases and are
documented when they ship.

## What the installer does, and does not do

`installer/lib/89-mail.sh` installs the daemons and **leaves them switched off**:

- The distribution's own units (`postfix`, `dovecot`, `rspamd`, and the key-value server's unit:
  `redis-server` on the Debian family, `redis` on release 9 of the RHEL family, `valkey` on
  release 10) are **masked before any package is installed**. On the Debian family a package's
  post-install script would otherwise start Postfix and Dovecot on port 25 and the IMAP ports under
  the distribution's own configuration, for as long as the install runs.
- The panel installs no unit of its own for them. The packaged units run against configuration the
  agent renders into the packages' own directories.
- No port is opened, in the firewall or anywhere else, and no unit is enabled.
- **No package repository is added.** Rspamd is in no RHEL-family base repository, and on Ubuntu it
  is in the `universe` component. When the host's repositories do not carry it, the installer
  prints `NOT INSTALLED: rspamd` and carries on; enabling mail then refuses and names it.
- ClamAV is not installed. The antivirus is optional and arrives separately.
- On a host where a mail unit is already running or enabled and Maran did not put it there, the
  installer refuses, by name, and changes nothing. A re-run on a host Maran already manages
  refreshes packages and directories and leaves every unit exactly as it is.

| Path | Owner and mode | Holds |
|---|---|---|
| `/etc/maran/mail/` | `root:root` 0755 | the lookup tables Postfix reads, written by the agent |
| `/etc/maran/mail/dovecot/` | `root:dovecot` 0750 | the Dovecot passwd-file, which holds password hashes |
| `/etc/maran/mail/dkim/` | `root:_rspamd` 0750 | DKIM private keys and the selector map |

## Turning it on and off

`EnableMail` needs certificate material for the server hostname to be installed already (the mail
stack never creates one), and every daemon's program to be on the host. It then, in this order for
the key-value server, Rspamd, Dovecot and Postfix: unmasks the unit, writes the daemon's files as
one change, has the daemon's own checker (`postfix check`, `doveconf -n`, `rspamadm configtest`)
read them at their real paths, starts the unit and only then puts it in the boot sequence. It waits
for the mail ports to be bound on an address other hosts can reach, reading the kernel's socket
table. A refusal restores the previous files; when nothing was running before, it also stops and
masks every daemon again, so a failed first enable leaves the host as the installer left it.
Repeating it on a healthy stack writes nothing and restarts nothing.

`DisableMail` stops, disables and masks every unit. The configuration, the lookup tables and the
mailboxes stay.

`ReloadMailTls` reloads Postfix and Dovecot after a certificate was renewed. A stack that is off is
left off.

Ports to open in the firewall once mail is on (the panel opens none): 25, 465, 587, 143 and 993,
plus 110 and 995 when POP3 is switched on.

## Release-dependent facts

- Debian 13 ships Dovecot 2.4, whose configuration language is not 2.3's. The agent renders the
  generation the release ships and refuses a release it has no answer for.
- Postfix lookup tables are written in the `lmdb` type, because the Berkeley DB backend behind
  `hash:` is not on every supported release.
- These templates and the unit, user and path names behind them were written from the
  distributions' documentation and have not yet been run on a booted host of each family.
