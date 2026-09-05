# Threat note — databases and SFTP

Required by `rules/security.md` ("Sensitive change escalation"). Covers the
plan `docs/superpowers/plans/2026-09-01-maran-databases-sftp.md`: the agent's
`ops::db` and `ops::sftp` areas, the `Databases` and `Sftp` backend modules,
the agent client's `DbService`/`SftpService` invokers, the installer's
`85-mysql.sh` and `86-sftp.sh`, and the account-deletion cascade that removes
what both leave on a host. **This change needs a second reviewer.**

The attacker model is the same one the `privs` note uses: a hosting customer
who fully controls their own home directory and the panel inputs their role
permits, but controls neither the panel's code nor the agent's arguments. A
second model matters here and did not before — a customer who holds a *database
credential* or an *SFTP login*, which are real credentials to real daemons that
were never part of this product's surface until now.

## What this surface is

Two new kinds of credential, each backed by something outside the panel.

A **database** is a real MySQL/MariaDB database with a dedicated user, created
by the agent over the server's local socket. A **SFTP login** is a real system
account in the group `maran-sftp`, with `nologin` as its shell, chrooted by
sshd's `Match Group` block into `/var/lib/maran/sftp/<account>/` — a root-owned
directory with the account's real home bind-mounted at `home` inside it, by a
systemd `.mount` unit.

Both credentials are shown to the customer **once** and never stored. Recovery
is a reset, not a retrieval.

## What defends this now

### 1. Nothing customer-supplied is escaped; it is validated

`DatabaseName`, `DbUserName`, `SftpUserName` and `Password` are validated types
whose alphabets exclude the quote, the backtick, the backslash, the semicolon,
the space and the newline. Every operation in both areas takes those types and
never a `&str`, so the guarantee belongs to the signature rather than to each
call site.

This matters because the database statements **cannot** be parameterised: the
server has no placeholder for an identifier or for the literal in
`IDENTIFIED BY`, so `ops::db` interpolates. A password that could carry a quote
would be a SQL injection running as MySQL's `root`. The same alphabet is what
makes a `chpasswd` line safe (it is fed on standard input, argv-only, never a
shell string) and what keeps a value out of the `Match`-block class of
config-file injection (`rules/security.md` item 4).

### 2. Prefixing isolates tenants at creation, and the panel's rows authorise

Every name is `<account>_<suffix>`, and the suffix may not contain `_`, so the
map from (account, suffix) to a MySQL name is injective and a name decodes at
its **last** separator with a whole-account match. Listing, dropping and sizing
are authorised by the panel's own tenant-scoped rows; the agent's listing is
diagnostic. `starts_with("alice_")` — which aliases `alice` onto `alice_bob` —
appears nowhere.

This was the plan review's first blocking finding, and the same root cause bit
the project twice more during execution (a decode at the first separator; an
account-deletion cascade that identified logins by name and deleted a
neighbouring *account*). Both are fixed and both are pinned by tests, one of
them only on a real host.

### 3. The grant is scoped, and a real server says so

`GRANT ALL PRIVILEGES ON \`db\`.*` is a string until a server parses it. The
polygon creates two accounts' databases, connects as one of them, and asserts
the other's database is **denied** and not even listed. Widening the grant to
`ON *.*` dies to a named test.

### 4. The jail leaves the home alone

OpenSSH refuses to chroot into anything not root-owned or group/world-writable,
and an account's home is `<account>:<web server group> 0750` — an ownership
that sites, nginx and php-fpm depend on. Rather than widening the home, the
chroot is a separate root-owned directory with the home bind-mounted inside it.
Cross-account isolation is exactly what it was; the login gets the **account's
own uid and gid**, so it can read its own files and an uploaded file comes out
owned by the account. It deliberately does **not** join the web server's group:
homes are group-owned by that group, so a login in it would read every account's
home.

### 5. No password can reach a log through an error

Neither `DbError` nor `SftpError` has a variant that can hold a string. Every
payload is an `i32`. That is a shape, not a discipline: the realistic leak here
is not a careless log line but the server or the tool quoting back what it
refused, which for `CREATE USER … IDENTIFIED BY '…'` is the password in full.
On the panel's side, `AgentErrorTranslator` redacts the secret the call itself
sent, because the panel minted it seconds ago and can therefore look for that
exact string rather than for a pattern a password does not have.

### 6. Deleting an account removes what it left on the host

Databases, SFTP logins, the jail and its mount unit go before `userdel`, and a
cleanup failure aborts the deletion — a half-deleted account is recoverable, an
orphaned database with a live credential is not.

---

## What is left open

This section is the point of the note. Every item below is a real gap, stated
because `rules/security.md` requires what is open and not only what is safe.

### The agent's MySQL access is root over the local socket

The agent authenticates as MySQL `root` through the `unix_socket` plugin: no
password exists to steal, and the installer verifies the plugin is enabled. The
cost is the mirror image. **Anything that can run as root on the host can use
that access**, with no credential to obtain and nothing to revoke. There is no
least-privilege MySQL account for the agent and no audit inside the server of
which panel operation issued which statement. An attacker who has already
reached root has the whole server anyway — but the point is that this surface
adds nothing that would slow them down, and no alert would fire.

### A dropped database is not backed up first

`DropDatabase` drops. There is no snapshot, no recycle bin and no delay. A
customer who deletes the wrong database, or an operator who deletes the wrong
customer, has lost the data. Backups are a separate module that does not exist
yet; until it does, deletion here is final.

### An SFTP login is a real system account, and what bounds it after login is not this plan

`ForceCommand internal-sftp` and `ChrootDirectory` decide what the *session* is,
and `AllowTcpForwarding no` closes the tunnel a file-transfer credential should
never open. What they do not decide is resource consumption: the login runs on
the host as the account's uid, so its CPU, memory and process limits are the
account's `systemd` slice and the host's limits — not anything this plan
configures. A customer who saturates a disk or a CPU through SFTP is bounded by
whatever the account was already bounded by.

### The uninstall guard is reasoned and only partly proven

`uninstall.sh` unmounts the per-account jails and then **refuses** to delete
`/var/lib/maran` while anything under `/var/lib/maran/sftp/` is still mounted,
because an `rm -rf` across a live bind mount deletes the customer home it points
at. The refusal path is proven on a real host only in one direction: the
account-deletion polygon (M9p) showed that removing the unmount makes `rmdir`
fail with `EBUSY` and abort, so the hazard and the guard are both real. The
**uninstaller itself** has never been run on a VM with live SFTP mounts. The
hand-run `mount --bind` that systemd does not own is handled belt-and-braces and
is likewise unexercised.

### `UsePAM=no` is the one thing the polygon does not run as a real host does

The SFTP polygon starts sshd with `UsePAM=no`, because a container needs its
confinement lifted to bind-mount and, on the RHEL family, a container in that
state has a `pam_unix` that refuses every account. The credential is still
really checked — against the hash `chpasswd` wrote, with a wrong password
refused, which the suite asserts — but the host's **PAM stack** is not: not its
account phase, not its session modules, not an operator's complexity or
lockout policy. If an operator's PAM configuration would refuse these accounts,
nothing here would find out.

The same suite also disables `PerSourcePenalties` where the daemon has it
(OpenSSH 9.8 and later, which on the polygon means the RHEL family only). That
is a test-determinism measure with no production counterpart — a suite that
asserts refusals loads a penalty onto loopback and then cannot log in — but it
does mean the suite never observes the daemon's own brute-force damping.

### Nothing checks that the `Match Group` block still exists on a running host

The installer writes exactly one block and is idempotent about it. After that,
**nothing ever looks again**. A hand-edited or package-upgraded `sshd_config`
that loses the block gives every SFTP customer a full shell session on the
server — as the account's uid, unchrooted — and the panel would report every
login as healthy. There is no health check, no periodic verification and no
alert. This is the single largest open item in the note, and it wants a check
that reads the running daemon's effective configuration (`sshd -T`), not a
comment.

### `ops::accounts` still spawns `useradd`, `usermod` and `userdel` by bare name

This predates the plan and the plan made it visible rather than causing it.
`account_operations.rs` spawns those three programs **by name, through `PATH`,
as root**. If anything can influence the agent's environment, it chooses which
binary root executes. `ops::sftp`, written in this plan, takes absolute paths
from the distro adapter — which now carries `useradd_binary`, `userdel_binary`
and `chpasswd_binary`, so closing this is cheap: it needs a `usermod_binary`
beside them and three call sites changed. It should not survive another plan.

### Panel rows and host state are not one transaction

The panel's rows and the host's databases and logins are updated by two systems
with no shared transaction. What makes this safe today is that the deletion
cascade enumerates the **host** rather than the panel's rows, so a row lost
without its host resource cannot orphan that resource. A row that exists without
its host resource is possible (a create that lands and then fails to record is
compensated by dropping the database, but a compensation can itself fail), and
the reconciliation is manual.

### There is no audit entry on the account-deletion path

`rules/testing.md`'s Definition of Done requires an audit event per feature, and
every mutating database and SFTP command writes one. Account deletion — which
now destroys databases and logins as a side effect — does not, because
`DeleteAccountCommand` has no request context plumbed into it. So the most
destructive operation in the product is the one with the least record. This is
pre-existing and is booked, not fixed.

### FTPS and phpMyAdmin are not here

The spec's §11 lists both beside SFTP. This plan ships SFTP only, which is the
spec's default; **FTPS is tracked as issue #20** and phpMyAdmin has no issue
yet. Both are deferrals rather than omissions — FTPS is a different daemon with
its own certificate story, phpMyAdmin a separate deployable with its own vhost
and authentication — but the consequence for an operator is the same: if they
need either, they do not have it, and no part of this plan is a partial version
of it.

## Closed after the plan: the accounts area no longer resolves tools through `PATH`

`agent/CLAUDE.md` states the rule plainly — "Processes are spawned with argv
arrays against an allow-list of absolute paths from the distro adapter" — and
`ops::sftp`, `ops::sites`, `ops::ssl` and `ops::db` all followed it. `ops::accounts`
did not. It spawned eight tools by bare name: `useradd`, `usermod`, `userdel`,
`setquota`, `quota`, `id`, `chmod` and `chgrp`.

The agent runs as uid 0. A program named without a path is resolved through
`PATH`, so each of those eight was "whichever binary the first directory in
`PATH` happens to hold". That is not a theoretical concern about a hostile
operator: it is one writable directory, or one inherited environment, away from
arbitrary code as root on every account operation — and the panel calls these on
a schedule an unprivileged customer can trigger, by creating an account.

Every call site now names `self.distro.<tool>_binary()`, and the adapter gained
`usermod`, `setquota`, `quota`, `id`, `chmod` and `chgrp` beside the `useradd`,
`userdel` and `chpasswd` the SFTP work had already added. Both families define
them; no `ops` file names a path.

Two things are worth recording beyond the diff.

**The polygon was itself an instance of the attack.** Its `setquota` stand-in
was installed at `/usr/local/bin/setquota` and worked *only* because `PATH` found
it before the real tool. Closing the hole broke the images, which is the
clearest possible demonstration that the hole was real. The stand-in now sits at
`/usr/sbin/setquota`, the path the adapter names.

**The per-call assertions could not have caught a regression.** Each existing
test pinned one argv, so a NEW call site added by copying its neighbour would
have been asserted by nothing. `every_program_the_accounts_area_runs_is_named_by_an_absolute_path`
sweeps every operation and checks the property itself; putting one bare name
back was verified to turn it red.

**Still open, and unchanged by this:** `quota` is absent from both polygon
images, so its argv is asserted by unit tests and its behaviour by nothing; and
this change was made and reviewed by one pair of eyes. `rules/security.md` asks
for a second reviewer on anything touching the agent's privileged surface, and
that reviewer has not read it yet.

## The second reviewer found the hole the first pass left

`rules/security.md` asks for a second pair of eyes on the agent's privileged
surface. It earned its keep on the first try: the claim above that "every call
site now names `self.distro.<tool>_binary()`" was FALSE.

`ProcessSystemHost::user_exists` — the gate in front of create, suspend,
unsuspend, delete, set_quota and usage — still ran a bare `id` as uid 0. It
survived for a reason worth writing down: the sweep test written to catch
exactly this class of bug runs against `RecordingHost`, a fake whose
`user_exists` is stubbed and spawns nothing. **The one method that chose its own
program was the one method the test could not see.** `ProcessSystemHost` is
deliberately not unit-tested ("cannot be tested without creating real users"),
and the polygon cannot catch it either, because on a polygon `id` is on `PATH`
and a bare name works perfectly.

Fixed by giving `ProcessSystemHost` the distro adapter, and — because no test
could have caught it — by adding a rule to `maran structure` (17b) that refuses
any program spawned by a bare name anywhere in `ops` or `agent`. Verified by
putting the bare `id` back: the gate names the file and line. A grep is a poor
test and a fine gate, and it is the only thing here that would have failed.

Two more findings, both real:

- **No test pinned the new tool paths.** Changing `usermod_binary()` to
  `/usr/bin/usermod` left the whole suite green. Each family's adapter tests now
  carry the nine tool paths as literals, plus a separate test that no path is a
  bare name — two tests because "this is the path we checked" and "this can never
  be a bare name" are different propositions, and only the second names the
  consequence. Both mutations were confirmed to kill the right test.
- **A doc comment claimed a tolerance the code does not have**: `quota_binary`
  said the accounts area survives the tool's absence. It does not — the spawn
  failure propagates and the usage request fails. Corrected, and the correction
  says why it is stated rather than implied.

Still open and unchanged: `quota` is absent from both polygon images, so its path
is asserted by unit tests and its behaviour by nothing; and `setquota` is only
ever exercised against the stand-in this repository installs at that path.

## The flag change was approved, and its documentation was lying

The second reviewer of the command-line change found no production regression —
every launcher passes only `--socket`/`--allow-uid`, and `Restart=on-failure`
with `StartLimitBurst` makes a bad flag a failed unit rather than a restart loop.

What it did find was that two files an operator reads DURING that failure were
wrong, and had been wrong before this change: `maran-agent.service` and
`agent.env.example` both said a missing `MARAN_AGENT_ALLOW_UID` makes the agent
"fall back to its own uid, root". An empty value does; a non-numeric one makes it
refuse to start outright. Both now say which is which. `rules/rust.md` and
`agent/CLAUDE.md` gained the `invocation.rs` row their layout maps require.

## The accounts module journalled nothing at all

The item booked above as "there is no audit entry on the account-deletion path"
was understated. The Accounts module wrote NO audit entries whatever — not for
creation, suspension, reactivation or deletion — while every other module wrote
them, and `AuditActions` named not one account action. The most destructive
operation in the product left no record, and neither did the three beside it.

All four now journal success and failure, through an `AccountAuditJournal` that
mirrors the Databases module's. A refusal is journalled too, including a
"not found" that is really another tenant's identifier, so a cross-tenant probe
leaves a trace naming what was probed for. Mutation-tested: recording a part-way
cascade as a success kills three named tests; dropping the success entry kills
two.

## The SPA held a third copy of the password alphabet

`ProvisionedPasswordGeneratorTests` pins the C# alphabet to the agent's. The SPA
now mints a password too, for the first-run administrator, and nothing pinned
that copy to either. A narrowed alphabet — dropping `=` and `+` — loses entropy
silently and passes every test, because the specification test only catches
WIDENING. `maran structure` rule 17c now compares the two literals by their
constants' names and reports a difference as a difference.
