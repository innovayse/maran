# Leaving an account out of scheduled backups

An account can be created with the instruction to leave it out of the unattended backup schedule
(issue #194, the "Back up this account" choice on the new-account form). This page says what that
records and what it does not change.

## What is stored

One row in `backups."AccountBackupOptOuts"` per opted-out account: the account id (the key) and the
instant the choice was recorded. An account created the ordinary way has no row. The absence of a row
means "back it up", so a failure to write the row can only cost disk, never leave an account unprotected.

The row is written by the Backups module when it is told an account was created without backups
(`AccountCreated.BackupEnabled` is false). It is idempotent: the account id is the primary key, so a
repeated announcement writes nothing. One audit line, `BackupOptedOut`, names the account. The row is
removed with the account when the account is deleted, and it appears in the account's personal-data
export as the `scheduleOptOut` section of the backups module.

## What it changes

The nightly sweep (`BackupRunHandler`) removes opted-out accounts from the list before it applies any
schedule. Neither the host-wide schedule nor a schedule that names the account takes a backup of it, and
the missed-run alert does not count it.

## What it does not change

- A backup asked for by hand, by the owner or by an administrator, is taken as usual.
- The safety copies taken before a restore and before an account is deleted are taken as usual.
- Nothing in the panel can flip the choice after creation yet. Until a follow-up adds that, an
  administrator who wants an opted-out account in the schedule has to remove the row by hand.

## Tenant scoping

The table is tenant-scoped like every other table carrying an account id: a customer sees at most their
own account's row, an administrator sees all of them, and a reseller or an account-less login sees none.
The sweep has no principal, so it reads the table with the tenant filter deliberately bypassed, with the
reason on the line.
