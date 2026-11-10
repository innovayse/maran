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

**From the panel.** While mail is off an administrator who opens Mail domains, Mailboxes or
Forwarders and aliases is told so, in place of the create form, with a host name field and a Turn
mail on button. That calls `POST /api/v1/mail-server/enable` with the host name typed and the
starting values the panel states in `GET /api/v1/mail-server` (`startingValues`, which come from
`MailDefaults`, so the SPA keeps no figure of its own). Customers and resellers never read that
status: they keep the form, and a create is refused with "Ask the server administrator to enable
it". The notice is advice; every mail operation re-checks that mail is on.

`DisableMail` stops, disables and masks every unit. The configuration, the lookup tables and the
mailboxes stay.

`ReloadMailTls` reloads Postfix and Dovecot after a certificate was renewed. A stack that is off is
left off.

Ports to open in the firewall once mail is on (the panel opens none): 25, 465, 587, 143 and 993,
plus 110 and 995 when POP3 is switched on.

## The mail queue

The administrator's **Mail** section holds a **Mail queue** page (`/mail/queue`) over the host's
queue. It is administrator-only: the queue holds every account's mail in flight, so a reseller or a
customer is neither offered the item nor allowed the endpoints (`GET /api/v1/mail-queue`, `GET
/api/v1/mail-queue/count`, `DELETE /api/v1/mail-queue/{id}`, `POST /api/v1/mail-queue/flush`; all
refuse any other caller with 403).

- **The list** is read from the agent in one bounded call (the newest 100 messages by default, at
  most 500) and paged, searched and sorted by the panel: search matches the message id and the
  sender, and the sorts are arrival time (newest first by default), sender, recipients and size.
  When the queue holds more than the agent lists, the page says so and the search and the pages
  cover the newest messages only. The sender is untrusted text and is drawn as text.
- **Removing one message** asks for confirmation, then deletes it from the queue: it is never
  delivered. **Retry deferred mail** asks for confirmation, then asks the mail transfer agent to try
  every deferred message again now; messages that still cannot be delivered stay queued.
- **The navigation badge** on the Mail queue item is the queue's total, read once a minute by an
  administrator's session from `GET /api/v1/mail-queue/count`. The panel asks the agent for a single
  entry and reads the total from the answer, so the cost does not grow with the queue. A queue the
  panel could not read draws **no badge** (never a zero), and an empty queue draws none. Where the
  administrator never turned mail on, the figure is a real zero without asking the agent.

## Release-dependent facts

- Debian 13 ships Dovecot 2.4, whose configuration language is not 2.3's. The agent renders the
  generation the release ships and refuses a release it has no answer for.
- Postfix lookup tables are written in the `lmdb` type, because the Berkeley DB backend behind
  `hash:` is not on every supported release.
- These templates and the unit, user and path names behind them were written from the
  distributions' documentation and have not yet been run on a booted host of each family.
