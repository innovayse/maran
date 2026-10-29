# Changelog

Notable changes, newest first. Dates are the merge date. Until 1.0 the format is prose rather
than a strict category list: what changed and why it matters to somebody running a server.

## Unreleased

**A hung program could freeze the root agent and block every other customer's writes.** Every program
the agent runs as root was started without a deadline, without an output limit and in the agent's own
process group. Measured with a validator that never exits, held under the host-wide configuration lock:
after 75 seconds the call was still running, another tenant's write was still blocked, and both the
program and its child were alive. A program that printed 5 MiB was held entirely in memory. The agent
now starts every program through one bounded path:

- **A deadline per kind of call.** Checks and reads get 60 seconds, ordinary changes 120, service
  reloads 240, database statements 300, package installs 2100, and backup work up to 12 hours. At the
  deadline the whole process group is killed, not only the leader, and then collected. Measured: the
  call returned a typed timeout at 60.0 seconds, the program and its child were gone, the previous
  file content was restored and the other tenant's write finished 3 ms later.
- **A cap on captured output**, 4 MiB per stream by default. Going over it kills the program and is an
  error that returns none of the output; exactly 4 MiB is still accepted.
- **Standard input is never inherited** and the cleared environment is kept.
- **Programs are named by a type that only the distro layer can create**, always by absolute path, so
  a bare string can no longer reach a spawn. `--` now ends the options before a user or unit operand
  wherever the tool supports it.
- **`tar --list` is bounded** (2,000,000 members, 16 KiB per name, 256 MiB of listing), and a backup is
  listed once as it is written, so one that restore would refuse is refused at creation. Database
  clients also ask the server to stop waiting on a lock after 240 seconds.

A timeout reaches the panel as the existing `SYSTEM_FAILURE` code with no tool output, inside a normal
response, not as a gRPC `DEADLINE_EXCEEDED`: the proto is unchanged and no new error code exists. The
agent sets no server-wide timeout or concurrency limit, because that would free nothing and would
report failure for a write that then succeeds. The systemd sandbox needed no syscall or capability
change.

Known residuals: a killed program's work already handed to the service manager or a database server
may still finish; backups written by earlier releases are not re-checked; a deadline-kill under the
real systemd sandbox and the agent-to-API handshake were not measured on the build machine. This
change alters how the only root process starts every program and needs a second human review before
it reaches `main` (#90).

**A signed-in customer could reach any API endpoint that forgot to declare its authorization.** The
fallback policy, which applies to every endpoint with no authorization metadata, only required a
signed-in user at customer level. Measured against a test-only endpoint with no metadata: a customer
got 200, and on an unknown path or a wrong HTTP verb a customer got 404 and 405 instead of a refusal.
The fallback is now administrator-only: anonymous callers get 401, customers get 403 (an empty body)
and administrators are admitted, so a future endpoint that omits its policy is closed to customers by
default. The default policy for a bare `[Authorize]` stays customer-level.

Every endpoint now names its policy instead of relying on either default: the dashboard, session
listing and revoking, sign-out-everywhere and the two-factor management actions are declared as
customer-level, and the setup controller marks its two anonymous actions individually rather than the
whole class. Customer behaviour is unchanged, as measured over the dashboard, session, two-factor,
account and site flows. New tests fail if an action or route-table endpoint names no known policy, if
the set of anonymous endpoints changes, or if the fallback is weakened. This is an authorization
change, so it needs a second human review before it reaches `main`.

**Reserved host names are refused for new sites, aliases, certificates and FTPS.** A tenant could
create a site named for an IP address, `localhost`, a `.local`/`.lan`/`.internal`/`.invalid` name or
the panel's own host name, and have nginx serve it and ACME try to issue for it. Measured before
the change: the agent's `Domain::parse` accepted `203.0.113.7`, `localhost`, `x.local` and the
rest, and the API created a site named `203.0.113.7` and one named for the panel (both 201). The
API and the agent now refuse the fixed set (a numeric final label, `localhost` and its
subdomains, the special-use suffixes `local`, `localdomain`, `internal`, `lan`, `home.arpa` and
`invalid`) on create site, aliases, issue and install certificate and enable FTPS. The API also
refuses the panel's own name, and its `www.` form, for sites, aliases and certificates (not for
FTPS), read from the new, optional `Panel__HostName` setting that the installer now writes into
`panel.env` (a downgrade ignores the extra line). The refusal says only that the name cannot be
used, in English, Russian and Armenian, and never echoes the name.

The panel vhost's `server_name` is now lower-cased and comes from the same function as the
certificate step; it falls back to `localhost` (it was `hostname -f`, or `_`), so re-running the
installer can change it on a host whose name is unusable. No database or proto change. Sites that
already exist under a reserved name are not removed: they can still be disabled, have their
certificate removed, be inspected and be deleted, because those paths check the name's shape only.

Not covered: the agent does not know the panel's name, so a compromised API could still ask it for
a vhost under that name; the primary domain of a new account is not checked; the nginx and ACME
behaviour was not observed on a real host. This change touches tenant isolation on a root-managed
host and still needs a second reviewer before it reaches `main`.

**The root agent's systemd unit is hardened further.** The unit that runs the agent as root kept
the kernel's control-group files writable, let it load kernel modules, accepted 32-bit system calls
and let it create setuid and setgid files. It now sets `ProtectControlGroups=true`,
`ProtectKernelModules=true`, `SystemCallArchitectures=native` and `RestrictSUIDSGID=true`;
`MemoryDenyWriteExecute=true` stays on. Measured on Ubuntu 24.04 (systemd 255) and AlmaLinux 9
(systemd 252): the agent starts and listens as before, and account, home-mode, site (`nginx -t`,
`php-fpm -t`, reload), certificate, cron, backup, restore, firewall and SFTP mount-unit operations
behave the same with the four directives on. The cgroup view becomes read-only, a 32-bit binary is
killed with SIGSYS, `chmod u+s` is refused, and `systemd-analyze security` exposure falls from 4.4
to 3.6.

`ProtectProc=invisible` and `ProcSubset=pid` are deliberately not set. `ProcSubset=pid` made `pkill`
fail on AlmaLinux 9, so a suspended account's processes stayed alive. The unit comments now say so
and mark what is still unmeasured (quota, other distribution versions, module autoload, cgroup v1).
Hosts must re-run the installer to receive the new unit. The change needs a second human review
before it reaches `main`.

**The root FTPS service now runs inside a systemd sandbox.** `maran-ftps.service`, the unit the
installer installs for the internet-facing, root vsftpd, restricted nothing but its runtime
directory: `systemd-analyze security` scored it 9.4 UNSAFE on Ubuntu 24.04 and 9.6 on AlmaLinux 9.
It now sets fourteen groups of directives (kernel tunables, modules, logs, control groups, clock and
hostname protection, native system calls only, no personality changes, no realtime scheduling, no
write-and-execute memory, no new setuid or setgid files, a short list of address families, a private
`/dev`, and a capability bounding set of ten capabilities), and scores 5.9 MEDIUM on both families.
Measured on Ubuntu 24.04 and AlmaLinux 9 under systemd as PID 1: a real FTPS session with TLS forced
(plaintext login refused, login, upload, passive listing, `SITE CHMOD`, download, logout) passes
unchanged, and the process reads back the new bounding set and a seccomp filter.

`ProtectHome`, `NoNewPrivileges`, `ProtectSystem=strict`, `PrivateTmp` and `RestrictNamespaces` stay
off, with the reasons in the threat note: `RestrictNamespaces` in particular makes vsftpd silently
drop its per-session isolation on Ubuntu 24.04 while logins still succeed. Hosts must re-run the
installer to receive the new unit. A new structure check keeps the rejected directives out of the
unit and the capability set within bounds.

Not shown by these measurements: the installer itself and the panel's enable-FTPS path were not run,
AlmaLinux's per-session isolation and the stock `/etc/shadow` mode 0000 case were not observable, and
`RestrictSUIDSGID` cannot be seen from a session because vsftpd masks the mode itself. This change
touches a root, internet-facing service and still needs a second reviewer before it reaches `main`.

**The panel refuses to start on a published or weak key outside Development and Testing.** The
repository's sample `.env.example` ships an encryption key and a token-signing key so a development
loop works out of the box, and nothing stopped an install from running on them: measured with the
published host and `ASPNETCORE_ENVIRONMENT=Production`, the panel booted on both sample keys, so
anyone who has read the source could sign an administrator's access token or decrypt the secrets the
panel stores. Outside Development and Testing the host now refuses to start when either key is the
published sample, decodes to bytes too structured to be random, or when the signing key equals or
contains the encryption key; the message names the setting and the problem and never prints the key.

Installs made by the installer are not affected: it generates two separate random keys. A hand-seeded
install that copied the sample stops at startup. Replacing the signing key costs nothing but a fresh
sign-in; replacing the encryption key makes the values already encrypted under it unreadable, and
there is no re-encryption tool yet.

**Installing a PHP version from the panel failed, and could leave the package database broken.** The
agent ran the package manager itself, inside a sandbox that keeps `/usr` read-only. On Ubuntu 24.04
the install died on "Read-only file system" part-way through and left fifteen packages half-configured
in dpkg, which also blocks unattended security updates; on RHEL-family hosts dnf reported the read-only
mount as a lack of disk space. The agent now asks systemd to start `maran-php-install@<package>.service`,
a one-shot unit that is never enabled and runs only the family's package manager (`--no-remove` on
Debian, no `--allowerasing` on RHEL) for that one package, outside the agent's sandbox, which is not
widened. The install also survives an agent restart or a dropped browser tab. Measured on Ubuntu 24.04
and Rocky Linux 9: the install now succeeds and the agent's sandbox is unchanged.

Hosts must re-run the installer to receive the unit: a host that took a new agent binary alone fails
PHP installs with a "unit not found" error, and there is no in-process fallback. The product still
provisions no Remi or Sury repository, so RHEL-family hosts and Debian releases without the version in
the distribution archive cannot install it regardless of this change.

**The root daemon no longer acts on accounts it did not create.** Until now the agent checked only
that an account name was spelled `[a-z][a-z0-9_]{2,29}` and that the user existed. Measured on real
Ubuntu 24.04 and AlmaLinux 9 containers, suspending, deleting, setting a quota on or rewriting the
crontab of `root`, `mysql` or `games` all went through to `usermod`, `userdel`, `setquota` and
`crontab -u`: root's shell was changed to nologin, `mysql` was removed from `passwd`, `shadow` and
`group`, and `CreateAccount` for `redis` ran `useradd`. Anyone able to reach the panel's API as an
administrator, or a compromised API, could do the same to the host's own users. The fix is in two
places, and the agent is the one that enforces it.

- **A managed-account check in the agent.** Every account lifecycle operation (suspend, unsuspend,
  delete, quota, usage, suspension state), every cron operation, and the SFTP and FTPS login
  creation and password changes now require proof that the user is a hosting account: the name is
  not on a reserved list of 103 names (system, daemon, database, web, mail and cloud-image login
  names, plus anything starting `maran_`), there is exactly one passwd row of that name, its home is
  exactly `/home/<name>`, and its uid and gid are at or above the host's `UID_MIN` and `GID_MIN`.
  A refused request runs nothing, not even `id -u`, and answers `INVALID_INPUT` with a fixed sentence
  that does not say which rule refused; an absent user is still `NOT_FOUND`. `CreateAccount` refuses a
  reserved name before asking the host anything, and still refuses a user that already exists.
- **Deletion resolves the account inside its lock and again before `userdel`.** If the uid or gid
  changed while the cascade ran, the deletion stops with a system failure instead of removing a
  different user.
- **The panel refuses the same names, in three languages.** Creating an account through the API with
  a reserved name now fails validation with `AccountNameReserved`, and the username alphabet is
  narrowed to exactly the agent's rule: no hyphen, 3 to 30 characters. Both messages exist in
  English, Russian and Armenian. The account form in the SPA still offers the old hyphen pattern;
  that is a follow-up.

What an operator will notice: a name such as `admin`, `mysql`, `git` or `ubuntu` can no longer be
given to a hosting account, and `acme-01` style names are no longer accepted. Existing accounts are
not touched. `RepairAccountHomeGroups` now skips users the panel does not manage.

Known residual: there is no marker on managed accounts. An ordinary hand-made user at or above the
id floor, with home `/home/<name>` and a name not on the list, is still manageable. The decision and
its reasoning are in `docs/superpowers/notes/2026-10-26-account-reserved-names-threat-note.md`.
This change touches root-run account operations and needs a second human review before it reaches
`main` (#83).

## 1.0.0-beta.3 — 2026-10-03

**Maran could not be installed on half the systems it claimed to support, and now it can.** The
supported matrix is Debian 12/13, Ubuntu 22.04/24.04/26.04 LTS, and RHEL, AlmaLinux, Rocky Linux and
Oracle Linux 8/9/10 — thirteen distributions, both families, every one of them now installed from
nothing to a working panel and verified by the panel's own health endpoint rather than by the
installer's exit code.

The reason so much was broken at once is that nothing had ever executed the documented install path
against a real init. Two polygon images run no init at all, which is why a fake `systemctl` had to
exist — and its own header admits it "starts or stops NOTHING". A fake `systemctl` cannot fail on a
unit directive, a mount namespace, or a binary that will not execute, and every defect below is
exactly one of those. `maran installer` closes that hole: a fresh server with real systemd as PID 1,
carrying nothing Maran needs, on both families and both architectures.

What an operator would have hit:

- **On AlmaLinux, Rocky or Oracle 8, the panel never started.** The distribution's default PostgreSQL
  is 10 and the installer checked no version, so the panel aborted on a system catalogue column that
  arrived in PostgreSQL 11. There is now a floor of 13 — the oldest version actually proven — and on
  EL8 the installer enables a newer module stream from the distribution's own AppStream, needing no
  third-party repository.
- **On those same systems no release could even be verified.** The manifest's signature was Ed25519
  through `openssl pkeyutl -rawin`, an option that arrived in OpenSSL 3.0; EL8 ships 1.1.1, whose
  command line cannot verify Ed25519 at all. The installer reported that as a signature mismatch and
  told the operator to suspect a compromised mirror. Releases are now signed with ECDSA P-384 and
  verified with `openssl dgst`, which every supported openssl has — one code path, no "verify
  whichever this host can manage" branch.
- **On Oracle Linux the root daemon refused to start on every version.** The installer resolved the
  family from `ID_LIKE` and accepted the host; the agent kept its own list and answered `unsupported
  distro: ol`. `rhel` was missing from that list too, which no test could have caught because RHEL's
  own images carry no nginx or PostgreSQL without a subscription. The two lists are now held equal by
  a structure check.
- **On Ubuntu 22.04, Debian 12 and EL8 the agent could not run at all.** It was built on the release
  host and linked whatever glibc that machine had. It is now built inside an AlmaLinux 8 container —
  the oldest glibc in the matrix — and the release refuses to ship an agent that needs anything
  newer. A static musl build would have sidestepped glibc and broken account lookups wherever SSSD
  or LDAP answers them, so it was rejected rather than taken.
- **An install could report success with the root daemon dead.** Step 70 waited sixty seconds for the
  panel's socket and did not look at the agent at all, so the one step that installs the privileged
  daemon could not observe whether it runs. It now waits for the agent's socket with the same
  specific refusal. That wait is what caught three of the defects above.
- **The agent died before running an instruction on a fresh host**, because its unit named
  `/var/lib/maran-ftps` as a mandatory writable path and step 89 created it nineteen steps after step
  70 started the service. systemd does not skip a missing writable path; it fails to build the mount
  namespace. The directory is created with the rest of the layout, and a structure check now requires
  every mandatory `ReadWritePaths=` target to exist before the step that starts the units.
- **Three dependencies were assumed rather than installed:** `openssh-server`, which step 86
  configures and did not install, failing at step 86 of 16 after PostgreSQL, nginx and MariaDB were
  already in place; `libicu`, without which the self-contained panel refuses to start and says so
  only in the journal; and the `hostname` binary, absent on Oracle Linux 8, where both halves of
  `hostname -f || hostname` called the same missing program and the install died at exit 127.
- **On EL8 the installer installed MariaDB and then refused to continue because of it.** That
  distribution's MariaDB leaves root with no password at all — a server where every local uid,
  including the accounts Maran creates for customers, is root on every customer database. The refusal
  was right and is kept for a server that pre-existed the install; a server the installer created
  seconds earlier is now secured instead, with the socket plugin loaded *before* root's login method
  is changed, because doing it the other way round locks root out entirely.

**A suspended login could still obtain working access tokens.** Refreshing a session minted a fresh
access token while consulting only whether the user still existed — not whether the login may be used
at all. Suspension writes the state and revokes the sessions as two separate operations, so anything
that stopped the second (a cancelled request, a failure in the session store, a process that died in
between) left a suspended login holding live sessions, and each refresh rotated one into a new token
indefinitely. The question now has a single definition on the domain entity, both paths that mint a
token ask it, and a structure check refuses any future file that issues one without asking. Measured:
the test for it fails without the check and the active-login control passes either way, and only two
places in the codebase mint a session or a token at all.

**Dependency updates are made deliberately again.** Dependabot is off, and the ten updates worth
taking from its last three batches are in — with the EntityFrameworkCore companions brought back
into step with the provider, which had drifted a patch version apart. Nine are refused, each with a
reason: the .NET 10 line (this tree pins the SDK to 9.0.317 with `rollForward` disabled), TypeScript
7 as a major compiler change, and the coupled tonic/prost/askama breaking releases, which touch the
generated proto contract and need their own work. The cost is stated rather than buried: security
updates no longer arrive on their own.

**The polygon's assertion script is twelve files instead of one.** It was 7381 lines holding 61
assertions — the largest violation in the tree of the rule that one file is one unit. The split is
by subject and preserves the original order exactly, verified by the parts concatenating
byte-identically to what was removed and by the assertion output being the same 433 lines before and
after.

**ARM64 is built and runs.** `maran release build --arch both` produces a manifest carrying all six
artifacts; the aarch64 agent and panel were verified by executing them on aarch64 with glibc 2.28.
`installer/lib/10-preflight.sh` has accepted `aarch64` from the beginning while the release built
only x86_64, so an ARM64 operator passed the gate and found nothing to install. One step, the
firewall, cannot complete under emulation — an aarch64 `nft` cannot drive an x86_64 kernel — so ARM64
is not yet claimed as verified end to end.

## 1.0.0-beta.2 — 2026-09-23

**The first beta could not install on a Debian-family host.** `30-postgresql.sh` searched
`/etc/postgresql` to a depth of two for `postgresql.conf`, and Debian and Ubuntu put it three levels
down at `/etc/postgresql/<major>/<cluster>/postgresql.conf`. The packages installed, `initdb`
created the cluster, and the installer then refused to find its own configuration. Both resolvers
had it — `postgresql.conf` and `pg_hba.conf`.

It was found by the first real install anybody ran, on Ubuntu 24.04, and it survived because the
polygon copies that step into its image without ever executing it: the image installs MariaDB and
sshd, not PostgreSQL. An assertion now exercises the resolver against the layout Debian creates,
with the inverse control that the old depth must fail on the same tree.

The refusal also prints what it searched for and lists what `/etc/postgresql` holds. Diagnosing the
original needed a photograph of a console.

Nothing else changed: the panel, the agent and the SPA are the same software as beta.1.

## 1.0.0-beta.1 — 2026-09-23

**A beta, and the README says what that means: do not put somebody else's customers on it.** The
software installs, runs and does what its screens say. Three things a paid hosting product must be
trusted about have been exercised only in this repository's own containers — no certificate has
been issued by a real authority, no disk limit has been enforced by a real filesystem, and no
restore has been run on real hardware.

The threat notes covering the privileged parts were reviewed and accepted on 2026-09-23; the
per-note verdict is in `docs/superpowers/notes/2026-09-22-second-review-packet.md`. That discharges
`rules/security.md`'s second-reviewer requirement. It is not an external audit.

What is absent by design: DNS, mail, and a web database manager.


### Added

- **Panel authentication.** First-run setup from the installer's one-time token, sign-in with
  Argon2id, JWT access tokens of fifteen minutes, refresh rotation in an httpOnly SameSite=Strict
  cookie where a reused token revokes the whole family, TOTP with recovery codes, and sessions you
  can list and revoke — including "sign out everywhere".
- **Account lockout.** Ten consecutive failures lock an account for fifteen minutes. A locked
  account is refused before its password is checked and with the same error a wrong password
  gets, so the response is not an oracle.
- **Hosting accounts as real Linux users.** Creating an account provisions its system user, home
  directory and the plan's disk quota through the agent; suspend, reactivate and delete reach the
  host too. The agent acts first and the database row follows, so the panel never claims an
  account the server does not have.
- **An append-only audit journal** of every sign-in and every mutation, with an
  administrators-only screen.
- **The `maran` CLI** — one entry point to the whole toolbox (`maran` lists it).
- **Websites.** Static, PHP and reverse-proxy sites, with domain aliases, a document root inside
  the account's home, and enable/disable. The agent renders the vhost, writes it beside the
  running configuration, makes the server's own `nginx -t` validate the result, and rolls the
  file back if either the validation or the reload refuses — so a bad render cannot take a
  server's other sites down with it. Every value written into a configuration file is validated
  rather than escaped: a domain that is not letters, digits, hyphens and dots is not a domain, so
  there is no newline to end a directive and start one of the caller's choosing.
- **Several PHP versions side by side.** Each account gets its own php-fpm pool per version,
  running as that account's Linux user, with the worker budget taken from its plan. Customers may
  change a whitelisted subset of settings; a name outside the list is refused rather than quietly
  dropped, and the hardening lines above them are written as `php_admin_value`, which a customer
  setting cannot countermand.
- **SSL.** Certificates ordered over ACME with an HTTP-01 challenge served from the site's own
  document root, custom material an operator can upload, and a renewal pass thirty days before
  expiry. Key material is written to a root-only store outside every account's home, because a
  site's PHP runs as that customer and a key inside the home is a key the site could read.
- **Live log tailing.** A site's access and error logs stream into the interface. The stream
  names a site and a log kind, never a path, and is bounded: a per-account limit on how many may
  be open at once, a heartbeat so a quiet log is not mistaken for a dead connection, and the
  reverse-proxy timeouts the installer writes to match.
- **Databases.** MySQL/MariaDB databases with a dedicated user each, per-plan limits, a size
  reading per database, and a password reset. The password is generated by the panel, shown once
  and never stored — not even encrypted — so recovery is a reset rather than a retrieval. The
  server has no idea what a tenant is, so isolation is built rather than assumed: every name is
  prefixed with the account, the grant is scoped to that one database, and a real server refuses
  a neighbouring account's data to the user the panel created. Creating the same database twice
  reports that it exists and leaves the first password working, because a retry after a lost
  response must not invalidate a credential the customer has already been shown once.
- **SFTP logins.** Per-account file transfer credentials served by the host's own OpenSSH, with
  per-plan limits and a password reset. Each login is chrooted into a root-owned jail with the
  account's real home bind-mounted inside it, so nothing about the home's ownership had to change
  and no other account becomes visible. The login carries the account's own uid, so an uploaded
  file belongs to the account exactly as one it created itself, and it gets `internal-sftp` and no
  shell — asserted as a real session being refused a command, not as a directive being present in
  a file. Port forwarding is off: a file transfer credential that can forward ports is a tunnel
  into the server's private network.
- **Deleting an account now removes what it left on the host.** Its databases, its SFTP logins,
  its jail and the jail's mount unit go before the system user does, and a cleanup failure aborts
  the deletion rather than leaving an orphan. A re-created account of the same name inherits
  nothing — not a database, not a login, not a password.
- **Container polygons for the agent.** Ubuntu 24.04 and AlmaLinux 9 images that run the agent's
  privileged suites against real `nginx -t`, real `php-fpm -t` and real system accounts, so the
  validation the whole design rests on is exercised against the software it will meet on a
  customer's server rather than against a fake that always agrees.

### Known limitations shipping with this

Named here rather than in a footnote, because each of them is something an operator would
otherwise find out the hard way.

- **The ACME client has never spoken to a real certificate authority through to an issued
  certificate.** It reaches Let's Encrypt staging's directory and nonce endpoints and is refused
  at account registration until a contact address is configured; ordering, challenge validation,
  finalising and downloading have been exercised only against a fake authority in tests.
- **HTTP-01 only.** No wildcard certificates and no DNS-01; those arrive with DNS management.
- **A reverse-proxy upstream is checked for shape and not for destination**, so a site can be
  pointed at a loopback service on the same machine.
- **`Acme:CertificateStorePath` is read by nothing**; the store path is fixed in the agent.
- **No web database manager.** There is no phpMyAdmin or equivalent; it is planned as a separate
  deployable with its own vhost and authentication rather than partially done here, so an operator
  who needs one does not have it.
- **The database-grant escape is forward-only.** A database-level `GRANT`'s name is a LIKE pattern,
  not an identifier, and an unescaped `_` in it matches any single character — so a build before
  this fix could grant an account access it never issued, to a same-length account whose name
  collided at the right position. The fix escapes every grant `create_database` writes from now on;
  it does **not** touch a row a previous build already wrote, because rewriting a live customer's
  access with nobody watching is worse than leaving it as found. A host that created databases
  before this change carries unescaped rows until an administrator runs the repair from the panel's
  Databases screen (`RepairDatabaseGrants`, `agent/crates/ops/src/db/repair_grants.rs`), which
  reports what it would change before changing anything. See
  `docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md` and
  `docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md`.
- **FTPS ships, and this entry used to say it did not.** The line above read "No FTPS … File access
  is SFTP only — there is no FTP daemon installed and no second listening port … FTPS is tracked as
  issue #20", and every clause of it was false by the time it was read. The correction is stated
  rather than made quietly, because an operator who believes no FTP daemon is installed does not go
  looking for one on their own host. What is true: the installer installs vsftpd
  (`installer/lib/89-ftps.sh`), and FTPS ships end to end — `agent/crates/ops/src/ftps/`,
  `backend/src/Maran.Modules/Ftp/`, `backend/src/Maran.Agent.Client/Services/FtpsService/` and
  `frontend/src/locales/{en,ru,hy}/ftp.json`. It is installed **switched off**: nothing listens, no
  configuration is rendered, and the distribution's own `vsftpd.service` is masked until an
  administrator turns FTPS on from the panel. `README.md` describes what "on" then means.
- **A dropped database is not backed up first.** There is no snapshot, no recycle bin and no
  delay: dropping is final. The Backups module ships (`backend/src/Maran.Modules/Backups/`) and
  this clause used to say it was "not here yet" — but it takes scheduled account backups, and
  nothing in the drop path consults it: `grep -rni backup backend/src/Maran.Modules/Databases/Commands/DropDatabase/`
  finds nothing. A database is restorable only from whatever backup already existed.
- **Nothing re-checks sshd's configuration after the install.** The installer writes the one
  `Match Group` block that turns an SFTP account into a jailed file transfer login and is
  idempotent about it, but nothing looks again afterwards. A hand-edited or upgrade-reset
  `sshd_config` that loses the block would give every SFTP customer a full shell session and the
  panel would report every login as healthy.
- ~~**Account deletion writes no audit entry.**~~ Corrected: it does, and this entry was wrong.
  `DeleteAccountCommandHandler.cs:288` records `AuditActions.AccountDeleted` on success and
  `:423` records the failure, alongside the final-backup outcome at `:343`, `:353` and `:366`
  (`grep -n AuditActions backend/src/Maran.Modules/Accounts/Commands/DeleteAccount/DeleteAccountCommandHandler.cs`).
- **An account created before this release cannot serve a site.** Creating an account now
  group-owns its home by the web server's group, at mode `0750`, so nginx can traverse into the
  document root without the home being opened to every other local user. Homes created earlier do
  not have it and there is no repair command; `chgrp www-data /home/<account>` (`nginx` on the
  RHEL family) fixes one by hand.

### Fixed

- **The installer's panel vhost was never the file `nginx -t` checked, and the vhost it shipped
  did not load.** Step 80 wrote the rendered vhost to `maran.conf.staging` and then ran
  `nginx -t`; nginx includes `conf.d/*.conf`, which `.staging` does not match, so the test parsed
  a tree the new file was not in, printed "test is successful", and the installer moved a file
  nothing had read over the served path. It hid a second defect for as long as it existed: the
  shipped vhost asked for HTTP/2 with `http2 on;`, a directive that arrived in nginx 1.25.1, while
  the supported families ship older ones — so on Ubuntu 24.04 and AlmaLinux 9 the panel's own
  vhost was an "unknown directive" and nginx would not start with it. The vhost now reaches disk
  through the same render-swap-validate-roll-back protocol the agent uses for customer configs, so
  what is validated is the file that will be served; a vhost nginx refuses now stops the install
  with the previous configuration restored and nothing reloaded, instead of surfacing at the next
  reload with a successful `nginx -t` in the log as evidence that it was fine. Validating after the
  swap means there is one `nginx -t` during which a file nobody has approved sits on the served
  path, so an install interrupted inside it — Ctrl-C, a dropped SSH session, a stopped container —
  now puts the previous vhost back before the installer dies, and re-raises the signal. After a
  `SIGKILL`, which nothing can catch, the next run prefers the rollback copy the installer left
  behind to the file on the served path — and asks `nginx -t` about that copy before serving it,
  because a copy this step did not write may be a fragment a hard reset left behind. One it cannot
  load is left where it is, named, with the panel vhost removed rather than replaced by it: the
  panel is then unreachable until an install finishes, which costs the panel, where an nginx that
  will not start costs every site on the host.
- **Deleting a customer armed a trap for every other customer.** A php-fpm pool names the account
  it runs as and php-fpm resolves that name at startup, so a pool left behind by a deleted account
  made the next reload — for any reason, days later — fail with `cannot get uid for user` and take
  PHP down for every tenant on the server. Nothing in the agent removed a pool at all. Deleting an
  account now removes every pool it owns, before `userdel` makes their user unresolvable; deleting
  a site or switching its PHP version removes the pool left behind when, and only when, no other
  site of that account still uses it.
- **Certificate renewal never ran.** The handler was named `…Job`, which Wolverine does not
  discover, so every daily trigger was dropped with "No routes can be determined" and every
  certificate the panel issued would have expired unwatched. Renamed to the convention, and a test
  now asks the running panel whether the message has a handler rather than reading a registration.
- **A fresh installation had no plans, so no account could be created at all.** The plan seeder
  existed, was unit-tested and had no caller anywhere in the product; it now runs at startup,
  inserting only what is absent so an operator's edited plan survives a restart.
- **A newly created PHP site had no php-fpm pool**, so its vhost pointed at a socket nothing had
  bound and the site answered 502 until somebody changed its PHP version — the only operation that
  wrote a pool. Creation and the version switch now write it through one shared function.
- **No web server could read any document root.** Every account home was `0750` owned by the
  account, and nginx refused every request with `stat() … (13: Permission denied)`.
- **The SSL tab could not be used.** Its API composable was still a seam that rejected every call,
  so a site whose certificate nginx was serving was shown "Certificates are not available on this
  server yet". The four endpoints are wired.
- **The third plan on the create-account form could not be clicked**, clipped by the card the form
  is drawn in, and two sidebar entries linked to an upgrade wall for modules the licence includes.
- The login rate limiter partitioned on a `username` **query** value while the endpoint
  authenticates the request **body**, so an attacker could get a fresh partition per request and
  guess passwords without limit. It now keys on the caller's address alone.
- Nothing read `X-Forwarded-For`, so behind the installer's own nginx every request appeared to
  come from `127.0.0.1`: one rate-limit budget for the entire internet, and an audit journal
  whose "from where" was always loopback. The header is now honoured for one hop, and only from
  loopback — it is written by the client, and trusting it from anywhere would hand the limiter a
  key the caller picks.
- Agent operations ran with no timeout at all: the resilience pipeline was registered and
  resolved by nobody. The health probe's timeout was hard-coded, which made
  `Agent:ProbeTimeoutSeconds` configuration nothing read.
- Command validators were registered but never invoked, so a weak password reached the database.
- `.env.example` told developers to copy it to `.env`, and nothing read the result.

## 0.1.0 — foundation

The contract, the Rust agent skeleton with `SO_PEERCRED` on its unix socket, the C# modular
monolith, the Vue shell, the development environment and CI.
