# Threat note — account deletion as a critical section, and the identities it is allowed to trust

Date: 2026-09-09
Surface: the agent's privileged account area (`ops::accounts`), the SFTP login path
(`ops::sftp`), the account's crontab removal and the restore's home swap
(`ops::backup::restore_backup`).
Findings this answers, from the agent concurrency audit: **C-3 (HIGH)** — `DeleteAccount` is
seven host mutations with no lock, and any site or SFTP RPC that interleaves leaves residue that
breaks an unrelated tenant or hands a future tenant a live credential; and **C-4 (HIGH)** — the
backup lock protects backups from each other and from nothing else, so a `DeleteAccount` during a
restore recreates the deleted home, owned by a uid the host may have reassigned.

## Second reviewer: OUTSTANDING

This change alters account deletion, the creation of an SFTP login, and the point at which a
restore decides which uid a customer's home belongs to. All three are privileged surfaces under
rules/security.md "Sensitive change escalation", so that rule applies: a second human reviewer and
this note, written first. **No second reviewer has read it. The requirement is OUTSTANDING.** No
agent session can be that reviewer, and this note is written by the author of the change, so it
closes nothing — it exists so the reviewer, when they arrive, reads an argument instead of a diff.
The branch `fix/live-findings` therefore MUST NOT merge to `main` while this line stands.

## What an attacker could do with this surface, before the change

Two states, both established by reading in the audit and neither of them reachable by a hosting
customer acting alone — every one needs two panel-issued RPCs to overlap, which is the panel's
normal operating mode (background schedulers beside interactive requests, no serialisation on
either side) but is not the same thing as an unprivileged exploit. That distinction is kept here
rather than dropped for emphasis.

1. **A live credential into the next tenant's home.** `CreateSftpUser` reads the hosting account's
   uid, then builds a root-owned jail, writes a systemd mount unit, runs `daemon-reload` and
   `enable --now`, and only then runs `useradd --non-unique --uid <the uid it read first>`. A
   `DeleteAccount` for the same account inside that window removes the logins that exist at that
   moment and then runs `userdel`; the `useradd` afterwards still succeeds, because `--non-unique`
   does not care that the uid is now unassigned. What is left is a passwd entry, a jail, an enabled
   bind-mount unit and a password the customer was shown, none of which any operation will look for
   again — `remove_account_sftp` enumerates from the password database at the moment it runs, and it
   will never run for this account again. This host **recycles account names**
   (`accounts/account_operations.rs` says so as a measured fact), so when the name is handed to the
   next customer the surviving unit's `Where=`/`What=` name the new tenant's home and the previous
   customer's password opens it.

2. **One customer's files under another customer's uid.** `RestoreBackup` resolves the account's
   uid once, at the top, and carries it for the whole operation — hours, for a large account. It
   takes a per-account lock that only the four backup operations contend for. A `DeleteAccount`
   landing between the restore's two `rename`s finds no home to remove, so `userdel --remove`
   succeeds; the restore's second rename then **creates** `/home/<account>` and step 8 chowns it to
   the uid it read an hour earlier. `useradd` allocates the lowest free uid, so that uid is handed
   to the next account created on the host, and the agent's own ownership check — comparing uid in
   `files/open_parent_directory.rs` — will then agree that the new tenant owns the previous one's
   files.

3. **PHP off for every tenant on the host.** A `write_pool` landing after `remove_account_pools`
   and before `userdel` leaves `<pool dir>/<account>.conf` naming a user that no longer resolves.
   The next `php-fpm -t` — any tenant's, days later — answers `cannot get uid for user '<account>'`
   and the master refuses to reload or start. `php/remove_pool.rs` exists precisely to close that
   trap; an interleaving reopens it.

## Why it is safe now

**One unit of exclusion: the account, process-wide within the one agent.** The per-account lock
registry that already existed for backups is moved to `ops::accounts::account_lock` — it is the
ACCOUNT's lock and not a backup concept — and is now taken by the operations that make or unmake
the account's identity on the host:

| Operation | Takes the account lock | How |
|---|---|---|
| `AccountOperations::delete` | yes | wait-free; `AccountError::Busy` when held |
| `sftp::create_sftp_user` | yes | wait-free; `SftpError::AccountBusy` when held |
| `backup::{create,restore,delete}_backup`, `recover_restores` | yes, as before | wait-free; `BackupError::AlreadyRunning` |

The deletion additionally takes **`ops::cron`'s own per-account lock** around its crontab
removal, and that is a second lock rather than a merger. `AccountOperations::delete` is the
SEVENTH writer of an account's crontab and the only one outside `ops::cron`: the other six take
`cron_lock`, and a removal that did not would be a removal racing an install. An install landing
between the removal and `userdel` re-creates a spool for an account that is about to vanish —
`userdel` removes neither family's spool file, measured, and cron keys that file by NAME on a host
that recycles names, so the survivor is handed whole to the next tenant of the name. The two locks
stay separate because their waiting semantics are load-bearing in opposite directions (see below);
the crontab guard is scoped to the removal statement, so a cron operation for this account never
waits on a `userdel` that has nothing to do with a crontab.

That makes states 1 and 2 above interleavings that can no longer be constructed inside one agent
process: a deletion and a jail creation, and a deletion and a restore, are now mutually exclusive,
and the loser is told so in a typed error the panel can retry on.

**State 3 — the poisoned php-fpm pool — was NARROWED when this note was first written and is now
CLOSED. Both states are recorded, because the reason it was left open is the more useful half.**

When this note was written, taking the lock inside `php::write_pool` had been implemented and then
reverted: unit tests drove `write_pool` for a single shared account name, so a wait-free refusal
made tests belonging to other authors fail intermittently, and a flaky suite is a P1 under
rules/testing.md. The estimate at the time was four test files. That estimate was wrong, and the
way it was corrected is worth a reviewer's attention: rather than counting files by eye, the next
author **instrumented the lock** to print `account -> test thread` on every acquisition and ran the
suite single-threaded. The real figure was **85 acquisitions by 79 tests, 75 of them for the one
name `acme`** — nine `sites` operations, four `ssl` operations, `accounts`, and `php` itself. The
cause was a single fixture hard-coding the name, not four files that happened to agree.

Fixed at the fixture: every test thread now gets its own account, re-measured as 74 distinct names
each taken by exactly one test. One case deliberately keeps the old name because its recording host
answers `passwd -S` and `getent` lines naming it — safe precisely because it is now that name's
only taker. The lock then went **inside `php::write_pool`**, not into its callers: that is the one
place the file is written, so a caller added later cannot forget it. `php::remove_pool` is
deliberately not a taker, because the deletion's own sweep calls it while already holding the lock.

What a reviewer should check: the race test reproduces the consequence rather than the mechanism —
reverting the lock leaves a pool naming a departed user and the host's real `php-fpm -t` then
answers `cannot get uid for user` and `FPM initialization failed`, which is what makes this defect
cross tenants. Stability was measured over 60 consecutive runs of the command that used to
reproduce the flake, not over one.

**Refuse, do not wait — and that is what makes deadlock impossible rather than merely unlikely.**
Every acquisition of the ACCOUNT lock is `try_lock_owned`, so a thread never blocks *on it*, so it
contributes no edge to a wait-for graph. That matters because the deletion now holds it while
waiting for two other locks. The whole graph, enumerated rather than asserted:

| Lock | Scope | Acquisition |
|---|---|---|
| `ops::accounts::account_lock` (this change) | per account | never blocks |
| `ops::cron::cron_lock` (C-2) | per account | waits |
| `ops::safe_write`'s config-tree lock (C-1) | host-wide | waits |
| `ops::firewall`'s mutation lock | host-wide | waits |

Every path that takes more than one, in the order it takes them:

- `AccountOperations::delete` → account lock, then `cron_lock` (released at the end of the crontab
  statement); and account lock, then the config-tree lock (inside the SFTP teardown's and the pool
  sweep's config writes).
- `sftp::create_sftp_user` → account lock, then the config-tree lock (the jail's mount unit goes
  through the config-write protocol).
- the four backup operations → the account lock, and nothing else.

No cycle exists, and three facts make that a proof rather than an inventory somebody must re-check:
**(1)** nothing holding one of the three waiting locks asks for another — `cron_lock` is the first
statement of a cron operation, which reaches neither `accounts` nor `safe_write`; the config-tree
lock is taken INSIDE the `safe_write` protocol, which calls no operation at all (and that placement
is also what makes C-1's two known nestings safe, so it must not be moved to the rpc entry points);
the firewall lock is taken by firewall mutations, which touch no account. All three are leaves.
**(2)** the account lock is never waited on, so it contributes no edge. **(3)** the two per-account
locks are deliberately not merged: merging would give the account lock cron's waiting semantics and
fact (2) would evaporate.

Refusing also keeps a blocking-pool thread from being parked for the length of a twenty-gigabyte
restore, which waiting would have done and which the audit named as an unmeasured risk of its own.
The one place a deletion does wait is `cron_lock`, and a cron operation is two `crontab(1)` spawns
long.

**A uid is re-read inside the critical section, and a mismatch refuses.** Two check-then-act
windows are closed by re-asking rather than by trusting a value read earlier:

- `restore_backup` re-asks the host for the account's ids immediately before the home swap, and
  refuses with `BackupError::AccountIdentityChanged` if the account is gone or its uid/gid have
  moved. The refusal lands **before the first rename**, so nothing has been swapped and the staging
  tree is removed by the cleanup the caller already runs unconditionally. The ids the finalising
  chown uses are the freshly read ones, so the operation cannot chown a home to a uid that has
  since been handed to somebody else.
- `create_sftp_user` re-asks for the hosting account's ownership immediately before `useradd`, and
  refuses if the account is gone or its ids have moved.
- `AccountOperations::delete` re-asserts that the account still exists immediately before
  `userdel`.

A validated `AccountName` proves the name is well formed. It has never proved that the account
exists, still less that it still holds the uid somebody read for it, and each of these three places
had been reading it as if it did.

## What I could NOT verify, named

- **A second agent process, and anything that is not this agent.** Every lock in this agent,
  including this one, is **process-local**: it serialises the RPCs of one running daemon against
  each other and nothing else. What it does NOT serialise against, named the way C-2 named theirs
  rather than left to be assumed: a second `maran-agent` binary an operator runs by hand, an
  installer step, and a customer's or an operator's own `userdel`, `usermod` or `crontab -e`. That
  was true before this change and is still true; it is not made worse, and it is not closed. It is
  also why the three re-reads this change adds are not redundant with the lock — each of them
  catches exactly what a process-local lock cannot see.
- **Orphans already on a real host.** This change prevents new orphans. It removes none of the ones
  a host may already carry. An orphan login is a passwd entry whose home is
  `<sftp jail root>/<account>`; `remove_account_sftp` finds exactly those, so the orphan IS removed
  the next time an account of that name is deleted — but nothing sweeps it before then, and in the
  interval between the name being recycled and that deletion the old customer's password opens the
  new customer's home. There is no operation in this agent today that finds them for an account the
  panel no longer knows about. A reviewer should treat that as an open item and not as covered by
  this note.
- **The residue of a per-account unit of exclusion.** `php-fpm -t`, `nginx -t` and
  `systemctl daemon-reload` are host-wide. Two DIFFERENT accounts' operations can still fail each
  other's validation, and no per-account lock can prevent that. That residue is C-1's finding, and
  C-1's config-tree lock inside `safe_write` is what covers it; the two locks are complementary and
  neither subsumes the other. What remains uncovered by BOTH is a host-wide tool invoked outside
  `safe_write` — `create_sftp_user`'s `daemon-reload` runs as `safe_write`'s validator, so it is
  covered, but a future operation that runs one directly would not be.
- **`set_sftp_password`, `set_account_logins_locked`, `suspend`, `unsuspend`, `set_quota` and
  `create` do not take the lock.** Each was considered. None of them creates or destroys the
  passwd entry or the home, so none of them can produce the three states above; a password set
  against a login being deleted fails on `chpasswd`'s own exit status, which is the correct answer.
  The audit's prose suggested a wider set. I deliberately took the narrower one, because every
  operation that takes an account lock is an operation that can now be refused while a backup runs,
  and refusing a suspension because a nightly backup is in progress is a worse outcome than the race
  it would prevent. A reviewer who disagrees is disagreeing with a judgement, not with a fact.
- **`create_sftp_user` leaves the jail behind when its re-check refuses.** The jail is a root-owned
  `0755` directory and a mount unit; no login and no credential exist at that point, and the next
  deletion of that account name removes both. I judged that inert enough not to add a teardown path
  that would itself be a new privileged removal running on an error path nobody exercises. It is
  written here rather than left for a reader to discover.
- **I have not observed either race on a production host.** What IS measured is both races driven
  deliberately on the polygon images, against the real tools, with the fix reverted to reproduce the
  audit's outcome and restored to close it; the working report for that run was kept only as a
  scratch file, not committed.

## How this composes with the restore-recovery fix that landed today

`recover_restores` reconciles an interrupted swap at startup, **before the socket is bound**.
It takes the same per-account lock, which after
this change lives in `ops::accounts::account_lock` instead of `ops::backup::backup_lock`. Nothing
about when it runs changes, and because it runs before any RPC can arrive the lock is always free
for it — this change neither undoes that fix nor depends on it. The one place the two meet is the
identity re-read: a home reconciled forward by the startup pass is chowned by that pass and not by
this one, so the fresh-ids rule added here applies only to a restore that reaches its own swap.

---

## Addendum, 2026-09-09 — the FTPS half of the cascade (Task 9 of the FTPS plan)

Surface added to this note: `ops::ftps`'s login lifecycle (`create_ftps_user`,
`delete_ftps_user`, `remove_account_ftps`, `ensure_account_jail`, `set_ftps_password`) and the
step they add to `AccountOperations::delete`.

**The second-reviewer requirement above is unchanged and still OUTSTANDING.** This addendum widens
what the reviewer has to check; it discharges nothing. `fix/live-findings` still must not merge to
`main` while that line stands.

### What this addendum changes about what the note claims

The note above says "an orphan login is a passwd entry whose home is `<sftp jail root>/<account>`".
**After this change that sentence is incomplete**, and the correction is the point of this
addendum: there is now a SECOND kind of orphan of the same severity — a passwd entry whose home is
`<ftps jail root>/<account>`, created `--non-unique` on the account's uid and belonging to the
group the FTPS PAM stack authorises. It is left by `userdel` exactly as the SFTP one is, it carries
the same recycled-uid consequence, and until this change **nothing in the cascade removed it at
all**: the account deletion took the SFTP logins, the SFTP jail and the SFTP mount, and left every
FTPS login, jail and bind mount standing.

That is not a regression this change introduces. It is the state the tree was in from the moment
`ops::ftps` gained a jail root, and the reason the cascade now has a fifth step.

### Why it is safe now

- **`remove_account_ftps` runs inside the deletion's critical section**, directly after
  `remove_account_sftp` and before the crontab removal, the pool sweep and `userdel`. It enumerates
  the HOST's password database — not the panel's rows — filtered by "the passwd home is exactly
  this account's FTPS jail", so it finds the logins the panel has forgotten, and only those.
- **The two teardowns cannot reach each other.** Each filters by its own jail root, so the FTPS
  step is blind to SFTP logins and vice versa. An account may hold both; neither unmounts the
  other's jail, because the two `.mount` units have different `Where=` values, different escaped
  names and different file paths.
- **A neighbouring account is unreachable by name.** `FtpsUserName::decode` splits at the LAST
  separator and compares the WHOLE account, so `alice_bob_deploy` is account `alice_bob`'s and is
  never taken by `alice`'s deletion. Both halves are asserted on a real host.
- **`remove_dir`, never `remove_dir_all`.** Under the mount point is the customer's real home. A
  removal that refuses a non-empty directory is what stands between a failed unmount and a
  recursive delete of a customer's website; the refusal aborts the deletion with the account still
  present, which is the recoverable state.
- **No new lock.** `create_ftps_user` takes the SAME per-account lock the deletion,
  `create_sftp_user`, `php::write_pool` and the four backup operations take, and it never waits —
  the four properties in rules/rust.md "What this agent serialises" are untouched, and the table
  still lists five locks. `remove_account_ftps` takes NOTHING: it runs with the lock already held,
  and a second acquisition of a lock that refuses rather than waits would make the deletion refuse
  itself.
- **The identity is re-read inside the critical section.** `create_ftps_user` reads the account's
  uid and gid, builds the jail, and reads them AGAIN immediately before `useradd`, refusing with
  `FtpsError::AccountIdentityChanged` on any difference and with `AccountMissing` on an account
  that has gone. `useradd --non-unique --uid` accepts a number that now belongs to somebody else;
  it is the one tool in this path that cannot be trusted to notice.
- **The jail is built before the login, deliberately.** Reversing it inverts the failure direction:
  a login that exists without its jail is a live credential whose chroot is missing or empty, while
  a jail without a login is a root-owned directory that authenticates nobody. The same ruling
  `create_sftp_user` carries, for the same reason.
- **No password reaches an argument vector.** `chpasswd` is given one `user:password` line on
  standard input, and `Password`'s alphabet excludes the colon and the newline, so the line cannot
  become two. A command line is world-readable through `/proc` to every local user, including every
  other tenant's transfer login.

### What I could NOT verify, named

- **The orphan sweep is still absent, and now has two shapes.** Everything the note says above
  about SFTP orphans applies unchanged to FTPS ones: this change creates none and removes none of
  what a host already carries, the orphan IS removed by the next deletion of that account name, and
  nothing sweeps it before then. Still an open item, still not covered.
- **`create_ftps_user` leaves the jail behind when its identity re-check refuses**, exactly as
  `create_sftp_user` does, and with the same judgement: at that point the jail is a root-owned
  directory and a mount unit with no credential in it, and the next deletion of that name removes
  both. A teardown on that error path would itself be a new privileged removal running where
  nothing exercises it.
- **`set_ftps_password` is crate-private and is NOT a customer-facing password change.** It takes
  no account, no lock and no shadow re-assert, and shipping it public with that shape would
  reintroduce the defect `sftp::set_sftp_password` was repaired for — `chpasswd` REPLACES the
  shadow field, and a suspension is a `!` in front of that same field, which
  `logins::set_account_logins_locked` writes for FTPS logins too. The rpc that Task 10 wires must
  add all three. This is written in the function's own doc comment as well as here.
- **No FTPS protocol-level login was observed.** The real-host suite asserts the artefacts — the
  passwd entry, the live bind mount read out of `/proc/self/mountinfo`, the unit file, the
  customer's own file visible inside the jail — but nothing in it speaks FTPS to a running vsftpd.
  That belongs to the FTPS polygon work, and a reviewer should not read these tests as proof that a
  client can log in.
- **The microsecond window between the jail work and `useradd` is not entered on a real host.** The
  identity re-read is proven by unit tests, including a positive control that asserts the second
  question is really asked. What the real host settles is the LOCK — a creation and a second
  deletion both refused inside a running deletion's critical section — and the absence of orphans
  afterwards. Entering the narrower window would need a test-only hook inside the operation, which
  this repository does not add; the same call was made for `swap_home` above.

---

## Correction, 2026-09-09 — `set_ftps_password` is no longer crate-private

Added by a verification pass over every threat note on `fix/live-findings`, which found this
note VERIFIED except for one claim the tree has since overtaken. The FTPS addendum
above says `set_ftps_password` "is crate-private and is NOT a customer-facing password change" and
that "the rpc that Task 10 wires must add all three" protections. Task 10 has landed:
`ops/src/ftps/set_ftps_password.rs` exposes `pub fn set_ftps_password` (with a
`pub(crate) fn set_ftps_password_under_lock` body for the creation path), and
`proto/agent/v1/ftp.proto` declares `rpc SetFtpsPassword`. The three protections — the account
parameter, the account lock, and the shadow-state re-assert — are argued in
`docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`'s FTPS addendum, which
is where a reviewer should go. Left as a correction rather than an edit, so a reader of the
earlier version can see what changed.

---

## Second review: RECORDED 2026-09-23

Every sentence above that calls the second reviewer OUTSTANDING described the state until this
date. It is kept rather than edited away, because what a note claimed while the debt stood is part
of what a reader is judging.

**Reviewer:** Edgar Poghosyan (edgar2031), the repository owner — the second human the rule asks
for, and legitimately so: he wrote none of these notes. Every one was written by an agent session,
which is the conflict the requirement exists to break.

**Verdict:** accepted, with no condition attached to this note.

**Typed by the agent at the reviewer's instruction**, because the reviewer does not write English.
Recorded here so a later reader can tell whose judgement this is and whose keyboard it came
through — those are not the same person.

The verdict for all twenty-eight notes is tabulated in
`docs/superpowers/notes/2026-09-22-second-review-packet.md`.
