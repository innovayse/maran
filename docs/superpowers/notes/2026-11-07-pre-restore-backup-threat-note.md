# Threat note addendum: the panel takes a PreRestore backup before a restore (issue #433)

Addendum to `2026-11-07-account-restore-proof-threat-note.md`. It changes the panel's restore path
(`RestoreBackupCommandHandler`), which drives the only operation that can destroy a working account,
so it is recorded as a sensitive change (rules/security.md "Sensitive change escalation").

**The second reviewer requirement is OUTSTANDING (#331).** An agent session wrote this and cannot be
that reviewer. The branch MUST NOT merge to `main` until a human has read this note and the diff.

## The decision: one meaning for `BackupKind.PreRestore`

`BackupKind.PreRestore` means exactly one thing: the backup of the account as it was the moment before
a restore replaced it, taken by the panel. The agent's `restore_backup` documents that it does not take
this backup and expects its caller to; the panel is that caller. The other reading (delete the enum
value and tell the operator to back up by hand) was rejected: a safety net that relies on a person
remembering is the one a mistaken restore finds missing.

## What changed

- `RestoreBackupCommandHandler` asks `SafetyBackupService` for a `PreRestore` backup after every
  existing refusal and before it opens its task or asks the agent for anything, waits for it, and
  refuses with `PreRestoreBackupFailed` (kind Failure) if it fails. No new rpc, endpoint, permission or
  port. `SafetyBackupService` is the former body of `AccountBackupService` made shared; the final
  backup before an account deletion goes through the same code with kind `PreDeletion`.
- The restore dialog states the safety net instead of "no copy is taken".

## What an attacker could do, and why it is safe

- **Fill the disk by repeating restores.** Each restore now also writes one archive. The endpoint is
  confirmed by typing the account name and rate-limited as before; a `PreRestore` row is never pruned
  by retention (`Backup.MayBeRetentionPruned` is an allow-list of Manual and Scheduled), so the
  growth is real and bounded only by how often an authorised caller restores. Mitigation: an
  operator deletes old `PreRestore` rows; a quota on them is not built.
- **Read another account's data through the new copy.** The row is tenant-scoped like every backup, so
  another customer's copy is a 404; the archive is made by the same agent path as any backup, as the
  account's own data.
- **Make a restore skip the net.** The handler returns on a failed result before the task or the
  agent call; there is no flag, parameter or request field that bypasses it. A cancelled request
  throws out of the handler before the agent is asked to restore.
- **Hide a restore by making the net fail.** The failed attempt is journalled as a failed
  `BackupRestored` entry and as a failed `BackupCreated` entry, and the failed `PreRestore` row stays.

## Stated limits (what the author could not verify)

- Not run: no build, unit test or polygon run was made by the author; the lead's gate does that.
- The net covers the home and the databases only, like every backup; vhosts, certificates, crontab,
  firewall rules and SFTP logins are outside it, as they are outside a restore.
- A `PreRestore` backup that completed but later fails verification is recorded and mailed, never turned
  into a refusal of the restore; an operator who wants that strictness has not been given it.
- The backup is taken, then the restore starts; between the two the account can change (a customer
  writing files). The net is as of the moment the backup finished, not of the instant of the first
  `DROP DATABASE`.

## What a reviewer must check

1. In `RestoreBackupCommandHandler` there is no path from the confirmation match to
   `_runner.RunAsync` that skips `_safetyBackups.TakeAsync` or ignores its result.
2. `SafetyBackupService` stamps the kind it is given on the row and never changes it, and
   `AccountBackupService` still passes `PreDeletion` (the account cascade depends on it).
3. `PreRestoreBackupFailed` has a decided kind in the Sdk census and a translation in en, ru and hy.

## Amendment 2026-11-09: the safety copies are bounded (#479, #478)

**What was wrong.** The first bullet of "What an attacker could do" treated the growth of `PreRestore`
copies as bounded only by how often an authorised caller restores, and "Stated limits" accepted that.
A customer is an authorised caller: at the default of three restores per account per hour each restore
wrote one more full archive into the shared backup root, outside the customer's quota, and retention
never removed it. Reaching the agent's free-space floor refuses writes for every tenant. Manual
backups had the same hole (#478): nothing counted or bounded them either.

**What changed.**
- `Domain/Policies/AccountBackupAllowance`: at most 3 `PreRestore` copies are kept per account
  (`MaximumPreRestoreCopies`), and manual plus pre-restore backups together may number at most 10
  (`MaximumCustomerBackups`).
- `Backup.MayBeSafetyCopyPruned()` is a SIBLING of `MayBeRetentionPruned()`, not a widening of it: the
  schedule's retain figure keeps counting only manual and scheduled backups, so safety copies cannot expire
  a customer's real history and the two counts stay separately stated. `PreDeletion` is still in neither.
- After a `PreRestore` copy completes, `SafetyBackupService` publishes `PreRestoreRetentionRequested`;
  `RetentionHandler` removes the surplus (archive first, row second, oldest first, stopping on an agent
  refusal). A scheduled retention pass bounds the same copies, so a lost message is made up for later.
- `CreateBackupCommandHandler` counts the account's rows against the allowance (running and completed
  manual and pre-restore only; failed and scheduled rows do not count) and refuses with
  `BackupAllowanceReached` (Conflict, en/ru/hy) before anything is written. A restore is never refused by
  the allowance: its safety copy must always be possible, and the pass makes room afterwards.

**Why it is safe now.** The volume a customer can fill through these two kinds is bounded by
13 archives of their own account (10 allowance plus the 3 safety copies of the moment) instead of by time.

**Stated limits.** The count is not atomic with the write: concurrent creations can overshoot by the
number in flight, and the agent's per-account lock serialises the runs, so the next call is refused.
The bound is per account, so many accounts still multiply it; a host-wide byte quota and a dedicated
rate limit for `POST /api/v1/backups` are not built. Pruning stops at an agent refusal, so a refusing
destination leaves surplus copies until it recovers.

**Second review: OUTSTANDING (#331).** A reviewer must check that no path publishes
`PreRestoreRetentionRequested` for another kind, that `MayBeSafetyCopyPruned` admits only completed
`PreRestore` rows, and that `CountsAgainstAllowance` cannot count a `PreDeletion` row.
