# Threat note: the account lifecycle acts on any system user (issue #83)

`rules/security.md` requires a threat note, written first, for a change to the agent's privileged
boundary, and a second reviewer. The account lifecycle in the agent (`usermod`, `userdel`,
`setquota`, `crontab -u`, `chpasswd`, `pkill --uid`) runs as root and is selected by a NAME. This
change decides which names, and which users behind them, that boundary will act on.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by agent sessions,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it
and signed off. The owner is told at hand-off that this surface carries an outstanding review.

## What an attacker can do with this surface today

The attacker is a compromised `maran-api` process: uid `maran`, unprivileged by design, but able to
speak to the agent socket as the admitted peer. The HTTP API alone cannot reach an existing system
user, because every call site takes the name from an `Account` row written only after
`CreateAccount` succeeded. The agent is the boundary rules/security.md item 1 names, and today the
boundary asks of a name only that it spells `[a-z][a-z0-9_]{2,29}` and that `id -u <name>` exits 0.

- **Lifecycle on a system user.** `SuspendAccount{root}` gives root a `nologin` shell.
  `DeleteAccount{root}` removes root's whole crontab (`crontab -u root -r`, foreign lines included)
  before `userdel` is finally refused; a user with no live process (`backup`, `mail`, `man`, a
  stopped `mysql` on the RHEL family) is removed with its home. `SetAccountQuota` and
  `GetAccountUsage` act on a daemon's uid. `GetAccountSuspensionState` is an oracle on any user.
- **Cron.** The `crontab -u <name>` spool reads and installs (`CrontabSpool`) check no identity at
  all; `SetAccountCronSuspended` and `SetCronEnvironment` rewrite root's crontab unconditionally.
  Only the home-side file work goes through `AccountIds`.
- **From a bystander user to root, through logins.** `AccountIds::resolve` refuses uid 0 and uids
  below `UID_MIN`, but it accepts ANY user at or above the floor. On a cloud image the operator's
  own login (`ubuntu`, `debian`/`admin`, `almalinux`, `rocky`; uid 1000, passwordless sudo) passes.
  `CreateSftpUser` for that name bind-mounts `/home/ubuntu` into a jail, creates a login that shares
  uid 1000 (`useradd --non-unique`) and sets a caller-chosen password with `chpasswd`: write access
  to `~/.ssh/authorized_keys`, so root. `SetAccountLoginsLocked` ends in `pkill -KILL --uid`, which
  kills every process of that uid. `RepairAccountHomeGroups` opens `/home/ubuntu` to the web
  server's group for any admin, with no compromise needed.
- **Name squatting through the HTTP API.** An administrator (or a compromised one) can create an
  account named after an identity that does not exist on this host YET. Two harms, both measured by
  reading `useradd --create-home --user-group <name>`:
  1. The daemon's name is taken. A later package install either fails in its maintainer script or
     runs the daemon under the customer's uid.
  2. `--user-group` makes a private group of the same name. If the name is a group that other
     software trusts and that is absent on this host, the customer owns it: `admin` (the group some
     Ubuntu sudoers files grant `ALL`), `sudo`, `wheel`, and later `docker`, `lxd`, `kvm`. Membership
     of such a group is root.
- **The agent and the API disagree.** The API accepts `-` and 31-32 characters (`^[a-z][a-z0-9_-]{2,31}$`);
  the agent refuses them as `InvalidInput`, after the API has decided the request was fine.

## The change planned, and why it makes the surface safe

Four parts, each owning a disjoint set of files. The common idea: **holding a `ManagedAccount` is
the proof that the host's user behind a name is a hosting account**, the way holding an
`AccountName` is the proof of spelling (rules/rust.md "Validation first"). Every root operation
that selects an account by name takes the proof, not the name.

1. **acct-type** adds `ManagedAccount` to `agent-core::validation::system` (below).
2. **acct-backend** adds the reserved-name rule to the Accounts module and narrows the username
   alphabet to the agent's. This is advice and an early, localized refusal; the agent is the boundary.
3. **acct-ops** makes suspend, unsuspend, suspension state, delete, set quota, usage and the home
   group repair require it, and makes `create` refuse a reserved name before `useradd`.
4. **acct-cron-logins** makes every `crontab -u` and every SFTP/FTPS operation that acts on an
   account by name (including the `chpasswd` ones) require it.

### What `ManagedAccount` proves (all must hold; the order is the order of checks)

1. The name is not reserved (list below). Checked before any host fact is read, so a reserved name
   costs no lookup and cannot be used as an oracle.
2. The local password database (`DistroAdapter::passwd_database()`, the file the agent already
   parses with `utils::system_accounts`) has exactly one row with that name. The local file only: a
   user served by a name service is not one this panel ever created, so it is refused as absent.
3. The row's home is exactly `/home/<name>` (`AgentPaths::ACCOUNT_HOME_ROOT`, the same string
   `AccountOperations::home_directory` builds). This is also what refuses an SFTP or FTPS login
   (`alice_web`): it parses as an `AccountName` and shares alice's uid, but its home is a jail.
4. uid and gid are not 0 and not below `UID_MIN` / `GID_MIN`, by the SAME decision `AccountIds`
   already makes (`is_hosting_account`, `account_id_floors` read from `login.defs`, which is where
   `useradd` takes its own floor). One rule, not two: acct-type widens those two private functions
   of `privs/account_ids.rs` to `pub(crate)` and changes nothing else in that file.

### The marker decision: NO marker in this change, and why that is a stated residual

The issue proposes a marker written at creation (a root-owned file under the agent's state
directory) so that a hand-made user which passes every check above is still not the panel's. This
note does NOT implement it, for three reasons:

- rules/architecture.md: the agent stays stateless ("no config files of its own beyond what the
  installer writes") and truth lives in PostgreSQL. A marker file is agent-owned state, with a
  creation path, a drift path, and an adoption pass at start that mutates the host unattended.
- A marker kept in the passwd entry (the comment field) is writable by the customer with `chfn`
  where a login exists. Failing closed on a removed marker means a customer can stop their own
  suspension, which is worse than the problem being solved.
- The design that does fit (root-controlled, stateless, not customer-writable) is membership of a
  system group created by the agent or the installer, read from the group database. It needs a
  `DistroAdapter` method, an installer or `create` step and an adoption instruction, in files no
  part of this branch owns. It is therefore a follow-up that needs the owner's decision.

**What this leaves open, plainly:** an ordinary user at or above `UID_MIN` whose home is
`/home/<name>` and whose name is not on the list below (say a human sysadmin `alice` with sudo) can
still be suspended, deleted, or given an SFTP login by a compromised API. The denylist covers the
default logins of the common cloud images (class F) because those are the case a script finds; it
cannot cover every name an operator chose. The single constructor is where a marker check would be
added, so no operation has to change when it is. The reviewer is asked to decide whether this
residual is acceptable for a v1 that has no deployed base, or whether the group marker is a
blocker.

### Exact public API other parts depend on

All in `agent_core::validation::system`, two files (`managed_account.rs`, `managed_account_error.rs`):

```rust
pub struct ManagedAccount { /* name: AccountName, uid: u32, gid: u32, home: String; private */ }

impl ManagedAccount {
    /// Reserved-name refusal only; no host fact. Called by `create` before `useradd`.
    pub fn refuse_reserved(name: &AccountName) -> Result<(), ManagedAccountError>;
    /// The one constructor. `passwd` is the TEXT of the local password database.
    /// Floors come from `account_id_floors()`; not cached.
    pub fn resolve(name: AccountName, passwd: &str) -> Result<Self, ManagedAccountError>;
    /// The same decision with the floors given, so a test can state them (crate-visible).
    pub(crate) fn admit(name: AccountName, passwd: &str, uid_minimum: u32, gid_minimum: u32)
        -> Result<Self, ManagedAccountError>;
    pub fn name(&self) -> &AccountName;
    pub fn uid(&self) -> u32;
    pub fn gid(&self) -> u32;
    pub fn home(&self) -> &str;
}

#[non_exhaustive]
pub enum ManagedAccountError {
    Reserved,      // on the list
    NotFound,      // no local row of that name
    SystemIdentity,// uid or gid is 0 or below its floor
    WrongHome,     // home is not exactly /home/<name>
}
```

Rules that bind every user of the type:

- **No `Clone`, no `Copy`, never stored.** Like `AccountIds` it is a fact about one moment: a uid
  recycled by `useradd` belongs to someone else an hour later. Resolve at the point of use.
- **Resolve INSIDE the account lock** where the operation takes one. `take_account_lock` stays
  keyed by `AccountName` (sftp, ftps, logins, php and backup call it; changing it breaks them), the
  lock stays outer and never waits, and deletion resolves again after its last wait and compares
  the uid with the first resolution, as it does now with `id -u`. The resolution and the use are
  one critical section; rules/rust.md "What this agent serialises" is unchanged.
- **Wire mapping, no proto change** (additive-only is trivially kept): `NotFound` maps to
  `ERROR_CODE_NOT_FOUND`; `Reserved`, `SystemIdentity`, `WrongHome` map to
  `ERROR_CODE_INVALID_INPUT` with one fixed English message that names no rule (as `NameError`
  does). The customer sees neither; the reason travels in the error value and only the
  accounts area logs it (cron, SFTP, FTPS and logins do not).
- **The passwd text comes from the area's existing host seam** (as `repair_home_groups` reads it
  with `read_password_database(distro.passwd_database())`); the type reads no file itself.
- `create` still answers `AlreadyExists` for ANY pre-existing passwd entry, and never adopts it.
- The stdin-piped `chpasswd` spawns stay their own bodies (rules/rust.md rule of two: an argv-only
  body cannot carry a secret). Only their gate changes.

### Callers that no listed part owns (for the orchestrator)

These break or stay open unless someone owns them; the type does not close the surface without them.

- `agent/crates/ops/src/logins/**` (`set_account_logins_locked`, `end_account_sessions`): the
  `pkill --uid` and `usermod --lock` path. `end_account_sessions` keeps a private copy of the
  "home is exactly `/home/<name>`" rule, which should become a call to the type (rule of two).
- `agent/crates/agent/src/services/{cron,sftp,ftps}/**` and the services' `validated_*.rs`: they
  parse the `AccountName` and must hand the operation what it now needs. `services/cron/validated_account.rs`
  is a pre-existing second copy of `services/wire/validated_account.rs` and imports across services.
- `agent/crates/agent-core/src/privs/account_ids.rs`: two visibility changes, the identity check in
  `resolve` and one new `unsafe` read of `pw_dir` (see review checklist item 7).
- Test fakes (`SystemHost`, the cron and SFTP/FTPS hosts) in `ops/src/tests/**` and
  `agent/tests/fixtures/**` gain a passwd text. The 2026-09-19 home-group-repair note judged its own predicate
  sufficient; this note supersedes that judgement.
- `frontend/src/pages/accounts/AccountFormPage.vue:34` keeps the wider alphabet
  (`[a-z0-9_-]{2,31}`). SUPERSEDED: finding 6 narrowed it on this branch (`AccountFormPage.vue` and
  the three locales).
- **`rules/rust.md` needs one edit, which needs the owner's approval:** the `validation/system/`
  row of the canonical agent layout lists the kinds in that folder; `managed_account` must be added
  to it (the rule says to extend the map before inventing a location). No other rules file changes.

## The reserved-name list (agent and backend implement exactly this)

Matching is on the whole name, exactly, after the name has already passed the alphabet
`[a-z][a-z0-9_]{2,29}` (so the comparison is case-exact and a hyphenated system name such as
`www-data` or `systemd-network` is excluded by spelling, not by the list). Plus ONE prefix rule:
every name that starts with `maran_`. 103 exact names, in six classes:

| Class | Names | Why |
|---|---|---|
| A. Superuser and base system identities (base-passwd on the Debian family, `setup` on the RHEL family), 25 | `root` `daemon` `bin` `sys` `sync` `games` `man` `mail` `news` `uucp` `proxy` `backup` `list` `irc` `gnats` `nobody` `nogroup` `adm` `halt` `shutdown` `operator` `gopher` `ftp` `www` `nfsnobody` | They exist on every host. The floor already refuses them when they exist; the list refuses them first, before a host call, and gives `create` a clear reason where `useradd` would say only "exists". |
| B. Daemon identities of software a hosting server installs or sits beside, 44 | `nginx` `apache` `httpd` `sshd` `dbus` `polkitd` `rpc` `rpcuser` `chrony` `ntp` `tss` `tcpdump` `avahi` `colord` `sssd` `unbound` `named` `bind` `mysql` `mariadb` `postgres` `redis` `memcached` `mongodb` `mongod` `rabbitmq` `elasticsearch` `postfix` `dovecot` `dovenull` `exim` `opendkim` `clamav` `amavis` `spamd` `vsftpd` `saslauth` `mailnull` `smmsp` `squid` `haproxy` `varnish` `tomcat` `git` | The squatting case: the user does not exist yet, so the floor has nothing to refuse. A customer account holding the name makes the later install fail or run the daemon as the customer. Best effort and said so: completeness is not claimed. For a user that already exists, the floor and the home check are the control, not this list. |
| C. The panel's own identity, 1 exact + 1 prefix | `maran`, and any name starting `maran_` | The installer's unprivileged service user. Its name is operator-configurable (`MARAN_USER`), so a literal list cannot cover a renamed one; that one is a system user (below the floor) and the floor covers it. The prefix keeps the product's own service names (`maran_api`, ...) from being squatted. |
| D. Groups and role names other software trusts, 15 | `sudo` `wheel` `admin` `staff` `users` `shadow` `disk` `tty` `utmp` `dialout` `cdrom` `docker` `lxd` `kvm` `libvirt` | `useradd --user-group` creates a group of the same name. Where that group is absent on this host, the customer's private group IS the group sudoers, the container runtime or the hypervisor tooling trusts. This is the only class whose harm is privilege escalation rather than disruption. |
| E. Role mailbox names, 7 | `administrator` `hostmaster` `postmaster` `webmaster` `abuse` `security` `noc` | Names RFC 2142 gives operational meaning. A customer account bearing one is a confusion and phishing vector against the panel's administrator, and mail systems deliver to them specially. |
| F. Default login users of cloud images, 11 | `ubuntu` `debian` `almalinux` `rocky` `centos` `fedora` `ec2_user` `cloud_user` `vagrant` `azureuser` `opc` | These pass the uid floor and the home check (uid 1000, `/home/<name>`) and are often the operator's only way in, with passwordless sudo. They are the `ubuntu` chain above. This class exists because no structural check separates them from a customer; it is the residual named in the marker section, narrowed by name only. |

Both implementations also carry tests that (a) every entry on the list parses as a valid account
name (a dead entry is a list that stopped meaning anything), (b) the list has no duplicates, and
(c) the 103 names and the prefix are asserted literally, so an edit to one side that is not made to
the other is a red test on that side. Nothing mechanical compares the two lists across stacks; the
literal assertion in each test, against THIS table, is the parity control.

### The backend half (acct-backend)

- The rule lives in `Maran.Modules.Accounts/Domain/Policies/ReservedAccountNamePolicy.cs`, asked by
  `CreateAccountCommandValidator` only. rules/csharp.md's three questions in order: its correctness
  is defined by the agent's host contract for account names, which only Accounts answers for (no
  other module decides what an account is called; Identity's administrator login is a different
  name space), so question 1 is "yes" and it stays in the module, in `Domain/Policies/` as a
  stateless rule over a value. It is not `Security/` (no key, no dial an operator turns) and not
  `Utilities/` (not asked in more than one place; a second asker would move it).
- A distinct machine code `AccountNameReserved` with entries in `Resources/ErrorMessages.resx`,
  `.ru.resx` and `.hy.resx`, so an operator is told "reserved" and not `AgentAlreadyExists`.
- The username pattern becomes `\A[a-z][a-z0-9_]{2,29}\z` and the maximum length 30: exactly the
  agent's `AccountName::parse`, so the two boundaries stop disagreeing.

## What a second reviewer must check

1. **The order and completeness of the four checks** in `ManagedAccount::admit`: reserved first,
   then the row lookup by EXACT name (a prefix or substring match would select the wrong row), then
   home, then the floors. That a `passwd` line with extra or fewer fields, a duplicate row of the
   same name, a trailing slash or `..` in the home, or a name containing `:` cannot produce a
   `ManagedAccount`. (`utils::system_accounts` skips malformed rows rather than failing: a skipped
   row must mean `NotFound`, never a pass.)
2. **No operation still takes a bare `AccountName` into a root effect.**
   `grep -rn "userdel\|usermod\|setquota\|chpasswd\|crontab_binary\|pkill" agent/crates/ops/src --include=*.rs`
   minus tests: each hit's function must receive a `ManagedAccount` (or a login derived from one).
   The unowned callers list above is where this is most likely to be incomplete.
3. **The lock argument is intact:** `account_lock` is still outer and never waits, resolution is
   inside it, and deletion compares the uid after its last wait.
4. **The two lists are the table above,** entry for entry, and the prefix rule is on both sides.
5. **The 103 names are the right ones.** This note is the only place anyone has been asked to
   judge classes B and F; a missing common daemon is a hole only for users that do not exist yet.
6. **The marker decision** (above) and whether the residual it leaves is acceptable.
7. `privs/account_ids.rs`: two `pub(crate)` widenings, plus `is_managed_identity` in
   `AccountIds::resolve` and one NEW `unsafe` read of `pw_dir` (`CStr::from_ptr`). This DOES change
   behaviour: every `fork_as_account` caller (files, sites, php, ssl, backup) now refuses a reserved
   name and a home other than `/home/<name>`. The reviewer must judge the SAFETY argument of that
   read.

## What the author could not verify

- **No second reviewer.** This is an agent session.
- **Nothing was run.** This note, and the phase it belongs to, were written before any build
  (the owner's instruction). Whether shadow-utils refuses `userdel` for a login that shares an
  account's uid and whose home is a jail (the `alice_web` case) is unmeasured: the home check
  refuses it by construction, so the claim does not depend on the answer, but the current
  behaviour is not known.
- **Class B and F completeness, and the exact default users of each image,** were written from
  knowledge of the distributions, not read from the images. Neither family's support matrix
  (Ubuntu 22.04/24.04, Debian 12/13, AlmaLinux and Rocky 9/10) was inspected on a live host.
- **The group sudoers claim for `admin`** (class D) depends on the host's sudoers file and on the
  group being absent; it was not reproduced. The list refuses the name either way.
- **Name-service users** (LDAP and the like) are refused by reading the local file only; a host
  that serves hosting accounts from a directory would find every operation refusing them. This is
  believed not to exist (the panel creates local users) and was not checked against a deployment.
- **Existing installs.** No migration or adoption is needed because no marker is introduced, but
  an existing account whose name is on the list (created before this change) would stop being
  manageable. Whether any exists on a real server is unknown; the backend list check applies only
  to creation, so such a row would have to be handled by hand.

## Independent agent review (recorded 2026-10-27)

Five agent lenses (correctness, security, rules, compatibility, design) reviewed the phase A code.
All five returned `changes_required` and none closed the issue. This section records the result
and the decision taken on every blocking or major finding. It records decisions, not diffs; fixers
apply them on this branch. **The human second review is still OUTSTANDING.** An agent review is not
that review, it is the argument the human reviewer will now read. This branch MUST NOT merge to
`main`, and the owner is told at hand-off that this surface carries an outstanding review.

Where this section contradicts the plan above, this section wins. In particular the plan said the
services "hand the operation what it now needs"; in the code the host trait resolves
`ManagedAccount` itself (read the passwd text, then `ManagedAccount::resolve`), and the services
only map the error.

### Security-lens checklist result (rules/security.md, run verbatim)

| # | Item | Result | Note |
|---|---|---|---|
| 1 | Input validated at the boundary, revalidated in the agent | yes | Backend validator narrowed to the agent alphabet plus the reserved-name rule. The agent revalidates through `AccountName` and `ManagedAccount`. The two lists were compared with `python3 re.findall` over `managed_account_policy`'s Rust list and the C# policy list: 103 names each, empty symmetric difference (re-run by the doc-truth lens). |
| 2 | Paths: descriptor walk | yes | No new path writes. The home is compared by exact string equality as an identity predicate, not as containment. See finding S2 for callers that stay on `AccountIds` alone. |
| 3 | No shell strings | yes | New spawns are argv arrays. The stdin-piped `chpasswd` bodies are unchanged. |
| 4 | Config-file injection | n/a | No new value is written into a config file. Names are `[a-z][a-z0-9_]` and are compared exactly against the passwd row. |
| 5 | SQL | n/a | No SQL change. |
| 6 | AuthZ | n/a | No endpoint change. `/api/v1/accounts` stays admin-only. |
| 7 | Configuration documented | n/a | No new variable, no secret. |
| 8 | Secrets and error leakage | yes, with a defect | Errors are fixed sentences that name no rule. The cron, SFTP and FTPS refusals reach the wire as `SystemFailure` through a wildcard arm. No leak, wrong code (finding C1). |
| 9 | Crypto | n/a | Untouched. |
| 10 | Surface | yes | No new port, daemon or outbound call. |
| 11 | Dependencies | n/a | None added. |
| 12 | Agent: closed typed commands, re-validated, audited | yes, incomplete | No new rpc, proto untouched. Lifecycle, cron, SFTP and FTPS are gated and log refusals. Logins, files, php, backup, sites and ssl are not (S1, S2). |
| 13 | Module reach into the agent | n/a | No new agent service or capability. |
| Escalation | Threat note exists as a file, was written first, states OUTSTANDING, plan matches the code | no | The note exists and says OUTSTANDING. The plan did not fully match the code (wire mapping, host-trait resolution, unowned callers). This section corrects the plan. |

### Findings and decisions

Finding ids: C = correctness, S = security, R = rules, K = compatibility, D = design. Several
lenses raised the same defect independently; they are grouped.

**1. Wire mapping missing for cron, SFTP and FTPS (C1, S4, R1, K1, D1; major, raised by all five lenses).**
`CronError::Account`, `SftpError::AccountNotManaged` and `FtpsError::AccountNotManaged` fall into
the `non_exhaustive` wildcard of `cron_status.rs`, `sftp_status.rs` and `ftps_status.rs` and reach
the panel as `SystemFailure`, a 5xx-class host fault, for what is a client refusal. The refusal
itself holds and nothing runs.
Decision: accepted as a defect, to be fixed. Explicit arms: an absent row maps to `NotFound`; every
other refusal maps to `InvalidInput` with one fixed sentence that names no rule, as the accounts
service already does. The three status files get an owner and tests. The wildcard stays (the enums
are `non_exhaustive`), so each new variant must be covered by a test that fails if it falls through.

Status: APPLIED (explicit arms in the cron, SFTP, FTPS and logins status mappers; tests owed in the test pass).

**2. Logins area still acts on any user with uid != 0 and home `/home/<name>` (S1, D2; major).**
`end_account_sessions` keeps a private copy of the home rule and refuses only uid 0.
`set_account_logins_locked` (`usermod --lock`, `pkill -KILL --uid`) is reachable by
`SetAccountLoginsLocked{username:"ubuntu"}` and would kill every process of uid 1000. This is the
`ubuntu` chain this note names, and review check 2 (no operation still takes a bare `AccountName`
into a root effect) fails on it. The acct-ops report called these files unreachable by RPC today;
that lowers the urgency and does not change the decision.
Decision: accepted, to be fixed. The logins area and the services' `validated_account` helpers get
an owner. `set_account_logins_locked` resolves `ManagedAccount` inside the account lock and hands it
to `end_account_sessions`, which loses its private home and uid copy (rule of two). The wire mapping
of item 1 applies. Until this lands, check 2 is recorded as failing.

Status: APPLIED by the security-lens fixer. `set_account_logins_locked_under_lock` resolves the
proof first, through `logins::resolve_managed_account` (the area's one resolution, over the host
seam's parsed rows), for the lock AND the unlock direction, before any login is enumerated; the
resulting `ManagedAccount` is what `end_account_sessions` takes, and its `pkill --uid` value is
`ManagedAccount::uid()`. The private uid-0 and home guards in `end_account_sessions` are removed,
because the type decides and a second copy was the defect (it refused uid 0 and a foreign home and
nothing else, so an ordinary login homed at `/home/<name>` was culled). `LoginsError::AccountNotManaged`
(which replaced the two `SessionCullRefused*` variants) maps to `InvalidInput` in `services/sftp/logins_status.rs`.
Phase B must prove, with a fake host, that `ubuntu` and a reserved name are refused with no `usermod`
and no `pkill` spawned, in both directions, and that an accepted account still gets both.

**3. Other root-adjacent paths gate on `AccountIds::resolve` only (S2; major).**
`ops::files` (`write_in_home`, `remove_in_home`), `sites`, `ssl`, `php` and `backup`
(`restore`, `extract_home_as_account`) select an account by name through `AccountIds::resolve`,
which accepts any user at or above `UID_MIN` and knows nothing of the reserved list. They act as
that uid inside its home (for example `/home/ubuntu/.ssh/authorized_keys`). The SFTP login route to
that file is closed by this branch; the file-write, restore and PHP routes are not.
Decision (revised by the security-lens fixer; the earlier text accepted these as an open residual):
fixed structurally, in the one place every one of those callers already goes through.
`AccountIds::resolve` (agent-core `privs/account_ids.rs`) now applies the identity rules of the
managed-account type after `getpwnam_r` and before the id floors: a reserved name
(`ManagedAccount::refuse_reserved`, asked and not copied) or a home other than
`<ACCOUNT_HOME_ROOT>/<name>` (read from the entry's `pw_dir`) is `PrivError::SystemAccount`, one
answer that names no rule. Every `fork_as_account` caller (files, sites, ssl, php, backup restore and
`extract_home_as_account`, cron entry files, SFTP/FTPS ownership) therefore refuses `ubuntu` and a
foreign-homed user without a per-area change. What this does NOT add is the uid/gid floor from the
type's own passwd read (the resolve path already applied the floors) or the duplicate-row rule
(`getpwnam_r` answers the first row). The same marker residual of item 4 applies: a hand-made
ordinary user off the list with home `/home/<name>` still resolves. New unsafe: one `CStr::from_ptr`
over `pw_dir` inside the existing unsafe module, with its SAFETY argument. Phase B must prove with
`is_managed_identity`'s unit test that a reserved name and a wrong home are refused and a managed
one is accepted, and that every polygon suite still passes, because their accounts are created by the
real `create` with home `/home/<name>`.

**4. Marker at creation and adoption (issue fix items 1 and 3) not implemented (S3; major).**
Decision: unchanged from "The marker decision" above, and now explicit. It remains an accepted
residual awaiting the OWNER's decision between the group-membership marker and accepting the
residual for a v1 with no deployed base. A hand-made ordinary user off the list, with home
`/home/<name>` and a uid at or above the floor, can still be suspended, deleted or given a login by
a compromised API. A follow-up issue is filed for the marker; #83 is not closed by this branch.

**5. Test files and fakes do not compile or pass (C2, R3, D4; major in two lenses, expected in phase A).**
`managed_account.rs` declares `#[path] mod tests` for a file that does not exist, so `agent-core`
and every dependent crate fail to build. The cron, SFTP and FTPS fakes, `RecordingHost`, the
`agent/tests/*_on_a_real_host.rs` files and the status-mapping tests are stale.
Decision: accepted; this is the order of work in rules/testing.md (code first, tests in a dedicated
pass), and the branch is not mergeable until the pass lands. The test pass must: write
`managed_account_tests.rs`; assert the 103 names and the prefix literally on both sides; pair every
refusal with an accepting control and assert that no command ran and no database, SFTP, FTPS, cron
or PHP fake was called; cover `IdentityChanged` through a passwd sequence, the
`repair_home_groups` filter, a duplicate row, a malformed second row, an unreadable database, and
the uid and gid floors; and assert the check order (reserved before any host read).

**6. Frontend still offers hyphens and 3 to 32 characters (C3; major in one lens, minor in K3, R6, D9).**
`AccountFormPage.vue` allows `[a-z0-9_-]{2,31}` and the `nameInvalid` strings say hyphen and 3-32,
so the advice layer disagrees with the backend within this branch. The earlier plan filed this as a
follow-up.
Decision: fix on this branch. Narrow the pattern to `^[a-z][a-z0-9_]{2,29}$` and update the
`nameInvalid` text in en, ru and hy. It stays advice; the backend and the agent are the boundary.

**7. Existing installs and pre-existing reserved names (K2; major).**
Beta tags exist. The check applies to every lifecycle call, so an account created earlier under a
name that is now reserved (`git`, `admin`, `list`, `operator`, any `maran_` name) cannot be
suspended, unsuspended, deleted, quota-set or cron-managed. Hosts with a non-default `UID_MIN` or a
home other than `/home/<name>` are cut off the same way. This note previously said only that such a
row "would have to be handled by hand".
Decision: no delete-only exemption in this branch, because an exemption for a name that can be
reserved is the hole the list exists to close. Required before release, not before this branch is
reviewed: (a) an upgrade note in the release notes naming the refusal and the manual recovery;
(b) a preflight, in the installer or the `maran` upgrade path, that lists `Account` rows and host
users that fail `ManagedAccount`; (c) a statement of what the backend does when the agent refuses a
delete (the row must not be dropped silently and must not stay in `Deleting`). Whether a
delete-only exemption is acceptable is a question for the human reviewer. Tracked as an issue.

**8. Gate threaded as a dropped result; SFTP and FTPS delete paths ungated (D3, R5; major in one lens, minor in two).**
`host.managed_account(...)?;` discards the proof in create and set-password, and
`delete_sftp_user` / `delete_ftps_user` / `remove_account_*` run `userdel` on a login name with no
gate.
Decision: create and set-password keep the gate as a precondition. Their `chpasswd` and `useradd`
targets are logins whose safety rests on the existing account-ownership and jail-home checks, and
phase B must prove with a test that a login not owned by the account is refused after the gate and
that the gate precedes the jail read and the `chpasswd` spawn (S5). The delete paths are recorded as
UNGATED BY DESIGN and protected only by the jail-ownership enumeration; that is an argument, not a
measured fact, and is listed under what the author could not verify. If phase B cannot show that a
login name outside the account-prefixed set cannot reach `userdel`, the delete paths are gated too.
Passing the proof into the `under_lock` bodies is preferred when they are next touched.

**9. Duplicated resolution step and error mappers (D2; major).**
The "read passwd, then resolve" sequence is written separately in the accounts, cron, SFTP and FTPS
hosts, and `SftpError` and `FtpsError` each carry an identical `from_managed_account`.
Decision: accepted in principle. Collapse into one helper (a file-based resolve on the type or one
function in `ops`) once the logins area of item 2 is gated, so the helper is written once with all
callers known, not three times. If the helper is not written in this branch, it stays a rule-of-two
debt named in the hand-off.

Status: PARTIAL. The logins area now has its one resolution (`logins::resolve_managed_account`). The accounts, cron, SFTP and FTPS hosts still each read and resolve; collapsing them is the remaining rule-of-two debt.

**Minor findings, with decisions** (listed so none is unmentioned).

- Floors read from a `login.defs` literal inside `agent-core` (S6, R4, K5, D5). Pre-existing; the
  type reuses `account_id_floors`. Decision: accepted as a conscious deviation from "no platform
  literal outside the distro crate"; moving it behind a `DistroAdapter` method is a separate
  reviewed change, tracked as an issue.
- Lock map growth (S8, C5, D7, R7). `delete`, SFTP and FTPS take `take_account_lock(name)` (cron takes only its own `cron_lock`)
  before resolving, so a compromised API sending many distinct valid names grows `ACCOUNT_LOCKS`;
  entries are never evicted. Not new (the old existence check was also inside the lock), but the
  doc comment claimed a bound. Decision: recorded here as a known residual; the doc comment is
  corrected to not claim a bound. Eviction of idle entries is a follow-up. The reviewer has the call.
- `repair_home_groups` resolves once per row, scanning the passwd text each time, and the type
  counts rows by raw lines (C5, D6). Decision: accepted for v1 (a passwd file is small and the call
  is administrator-driven); a row-based constructor is the remedy if it is ever measured.
- A duplicate or malformed second row with the same name yields `NotFound` for every lifecycle call
  (K4, S6). Decision: fail closed is kept. The accounts area logs the ambiguity at warn level in
  `create` and `resolve_managed`; the other areas do not log.
- An unreadable passwd file is reported as an absent account in cron, SFTP and FTPS (C6).
  Decision: still fails closed. Optionally a distinct system-failure error, as `AccountOperations`
  does with `HomeInspection`; not a blocker.
- SUPERSEDED by `ops::accounts::read_managed_account` (see the design review below): the cron, SFTP and FTPS hosts read passwd with `fs::read_to_string` rather than through a host
  seam (S6). Decision: acceptable in phase A; phase B adds a fake for it.
- Stale doc claim in logins and validated_account that `AccountIds::resolve` is the one lookup
  (C4). Decision: the doc comments are corrected with item 2.
- `rules/rust.md` edits D1 to D3 (layout row for `validation/system/`, the validation-first
  sentence, the "deletion compares the uid" sentence) are NOT applied (R2, S9, D8). They need the
  owner's approval and are flagged to the owner here.
- PrimaryDomain and PlanId validator rules keep English literal messages that the exception
  middleware discards (R6, D9). Pre-existing; left for a separate fix.
- The 103-name list is held twice, in Rust and C#, with no mechanical link (R6). Decision: the
  literal assertion of this note's table in each stack's test is the parity control, as before.

- Design review (2026-10-27): the wire mapping for cron, SFTP, FTPS and logins is explicit now
  (`Account(NotFound)` and `AccountMissing` to `NotFound`, every other refusal to `InvalidInput`,
  a fixed sentence). The cron, SFTP and FTPS hosts share one passwd-read step
  (`ops::accounts::read_managed_account`), the login area resolves through
  `ManagedAccount::resolve_rows`, and `end_account_sessions` takes the proof and keeps no home or
  uid rule of its own.
- `delete_sftp_user` and `delete_ftps_user` (and the `remove_account_*` cascades) stay ungated by
  design: `userdel` runs only on a name found in the account's own login enumeration, which admits
  a row only when its passwd home is exactly that account's jail directory. No reserved, system or
  operator user has such a home, so the jail home is the proof. Argued in the doc comment of both
  operations.
- The floors still come from `account_id_floors()` reading `login.defs` inside agent-core
  (pre-existing platform literal). Recorded as a conscious deviation from "no platform literal
  outside the distro crate"; moving it behind a `DistroAdapter` method is a separate reviewed
  change and needs the owner's call.

### What remains OUTSTANDING after this review

1. The human second review, per rules/security.md. Nothing here discharges it.
2. The owner's decisions: the marker (item 4), the open residuals of item 3 and item 8, whether a
   delete-only exemption is acceptable (item 7), and approval of the `rules/rust.md` edits.
3. Phase B: build, the test pass of item 5, and measurement. No finding above has been verified by
   running code; the reviewers read it. Findings 1, 2 and 6 are applied by fixers whose work this
   review has not re-read.

## Phase B verification evidence (recorded 2026-10-27)

This section records what was run, what it showed and what is still unverified. It does not
discharge the human second review.

### Gates

| Gate | Result |
|---|---|
| `maran check` | OK for all tools |
| `maran structure` | first run: 1 violation (docker/README.md did not name the new polygon suite); fixed; STRUCTURE-OK |
| `maran format --check backend` | first run FAILED (IDE0053, xUnit2008 in `AccountNameLockstepTests.cs`); fixed; FORMAT VERDICT: OK |
| `maran proto` | PROTO-OK, no proto file changed (additive-only holds trivially) |
| `maran agent lint` | first run 2 clippy errors in test files; fixed; clean |
| `maran test rust` | 2115 passed / 0 failed / 123 ignored over 28 targets. TEST VERDICT: FAILED only because the new target `account_identity_on_a_real_host` had no baseline row; nothing failed or fell. Baseline rust rows then edited by hand (the `--accept` run clobbered the file, which was restored first). The whole-suite verdict was NOT re-run after that edit. |
| `maran test backend` | NO VERDICT. The kernel OOM killer killed the memory-capped scope in the test phase on every attempt. Compile, warnings and format are clean. |
| `maran handshake` | NOT verified: PostgreSQL authentication failed (28P01) for the dev user, an environment fault. |
| `maran api`, `migrate check/guard`, `licenses --check`, `test spa`, `cargo doc` | not run (no migration, proto, dependency or manifest change; api and spa need the backend build). |

Baseline numbers come from a branch based on 7e75b67; dev has moved on (b8b1899). Dev must be
merged and the rust (and, once it can run, backend) baseline re-accepted before merge. The
mutation stage added one test, so `maran_agent_core` is now 404 and the baseline file still says
403.

### Measurement on real hosts (Ubuntu 24.04.4 and AlmaLinux 9.8 containers)

Probe over the real agent server on a unix socket, with a spawn-logging shim over `usermod`,
`userdel`, `useradd`, `chpasswd`, `setquota`, `quota`, `crontab`, `pkill` and `id`.

- Before (dev): on both families, Suspend/Unsuspend/SetQuota/SetCronEnvironment/
  SetAccountCronSuspended/DeleteAccount reached the tools for `root`, `mysql` and `games`.
  Root's shell became nologin and its crontab was rewritten or deleted. `mysql` was removed
  (passwd, shadow and group entries gone) while mariadbd ran as it. `GetAccountSuspensionState`
  was an oracle on any user. `CreateAccount{redis}` ran `useradd`.
- After (branch): all nine account and cron RPCs for `root`, `mysql`, `games`, and `CreateAccount`
  for `redis`, answered `INVALID_INPUT` with a fixed sentence naming no path, uid or rule. Zero
  logged spawns (not even `id -u`); the host snapshot (passwd, shadow, group, `passwd -S`, root
  crontab with a planted line, home listing) was identical before and after.
- Inverse control: a panel-created account (`probeacme`) still goes through create, duplicate
  create (`ALREADY_EXISTS`), suspend, unsuspend, quota, usage, cron and delete, with results equal
  to dev.
- Residual measured: a hand-made `opsadmin` (uid at or above the floor, home `/home/opsadmin`,
  unlisted name) is still suspended and unsuspended on the branch. This is the stated no-marker
  residual, now observed, not just argued.
- Polygon suites on both families: the new `account_identity_on_a_real_host` 6/6; `cron` 6/6;
  and, in a privileged container, `account_deletion` 5/5, `sftp` 13/13, `ftps` 15/15. In a
  non-privileged container `account_deletion` fails 5/5 identically on pristine dev (needs
  `--privileged`), so it is not a regression.

### Mutation proof

About 70 single-line mutants over `managed_account.rs`, `account_ids.rs`, the account operations,
the account/cron/SFTP/FTPS/logins wire mapping, and every cron and chpasswd gate were scored on
the whole Rust suite. All compiling mutants were KILLED by a named test, with these exceptions:

- Survivor: matching a row by `starts_with(name)` instead of the exact name field (an `acme` row
  beside an `acmex` row was wrongly refused). A witness test,
  `a_neighbouring_longer_name_does_not_make_the_account_ambiguous`, was added and the rerun killed
  it.
- Masking: with the reserved check, the home check and the id floor all disabled together, 63
  tests fail, including the root rows, so no row is protected by only one layer unseen. Each check
  alone is killed by its own named test.
- One mutant did not compile (8a); its intent is covered by the combined run.

NOT run: the C# mutants (list entry, prefix, `IsReserved`, validator pattern/length/`Must`/
message, census row, resx rows, handler journalling) because the backend suite is OOM-killed, and
`rules/testing.md` forbids scoring against a subset; the polygon-only Rust mutants (delete
reorder, floor to 0, home `starts_with` on a `/srv` home, cron call removal); the `azureuser` list
mutation; the multi-line create-order variant. One test-only fix was made during the stage:
`CultureInfo.InvariantCulture` on `int.Parse` in `AccountNameLockstepTests.cs` (CA1305); it was not
format-checked afterwards.

### Still unverified

- Every C# test, including the new endpoint and localization tests (Testcontainers), and the
  architecture lockstep test: never run to a verdict.
- The wire probe went straight to the agent, not through the API, so the backend rule and the
  narrowed alphabet were not measured end to end.
- Not measured on the wire: `chpasswd` refusals (SFTP/FTPS password), `SetAccountLoginsLocked`,
  `RepairAccountHomeGroups`, `EndAccountSessions` (`pkill --uid`); these rest on unit tests and on
  the green legitimate-path suites. `NOT_FOUND` for an absent row was not probed on the wire.
- Of the 103 names only `root`, `mysql`, `games`, `redis` and `ec2_user`-style cases were probed
  live; the rest, the `maran_` prefix and non-default `login.defs` floors rest on tests.
- Quota enforcement is a stand-in on the container filesystem. Mariadb users and databases of the
  refused names were not inspected after refusal; the DB client is not shimmed.
- `userdel` refusing a user with live processes was NOT observed (it removed `mysql` under a running
  server), so the issue's claim about that safeguard did not hold in this setup.
- Docker images used for measurement were built from dev and removed; the existing polygon images
  carry no fingerprint label, so `maran polygon verify` would abort on logs from them.

### Status of the open items

1. The human second review (rules/security.md) remains OUTSTANDING. Nothing in this section
   discharges it. The branch must not merge to main.
2. The owner's decisions (the marker and the measured residual above, item 3 and 8 residuals,
   delete-only exemption, the `rules/rust.md` edits D1 to D3, the `login.defs` literal) remain open.
3. Before merge: merge dev, re-accept baselines, get a backend verdict on a machine with enough
   memory, fix the dev database credential and re-run `maran handshake`, run `maran polygon verify`.
