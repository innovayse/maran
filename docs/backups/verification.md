# Backup verification

What the panel checks about a backup artifact after it is written, what that check cannot tell you,
and where the result shows (issue #134).

## Why it matters

A backup that cannot be read back is found out on the worst day: the day of the restore. A run that
ended without error says the archive was written, not that the bytes on disk are still the bytes
that were written. Verification reads the artifact again, soon after the run and on demand, so a
damaged archive is a finding while the account's live data still exists.

## When it runs

- **After every completed run**, scheduled or manual. The run's own outcome is recorded first; the
  verification follows and is recorded as its own fact.
- **On demand**, with "Verify now" on a backup (`POST /api/v1/backups/{id}/verify`). The
  administrator and the owning account's logins may use it, as for the other backup actions. It is
  refused while viewing as an account.

A later verification replaces an earlier one: the last finding wins.

## What is verified

The server agent re-reads the artifact (rpc `BackupService.VerifyBackup`, contract revision 18). It
is read-only and idempotent, serves the local destination only, and extracts nothing into the
account's home.

| Check | What it establishes |
|---|---|
| Artifact digest | The SHA-256 of the archive, computed again in fixed chunks, equals the one in the sidecar next to it, and the measured size equals the recorded size. |
| Member scan | The archive's member list is within the layout a restore accepts and within the same bounds; the scan is the one a restore runs first, nothing is unpacked. A refused member fails the whole verification. |
| Manifest | `manifest.json` parses, is of a known version, and names this account and this backup. |
| Sidecar and manifest agree | The two records of the same backup say the same thing. |
| Dump members present | Every database the manifest names has its dump member in the archive's listing. Presence only: the member's type and its recorded digest are not checked (see below). |

The result carries the number of members, the number of dump members found and the archive's size
in bytes. The manifest is refused unless it is a plain file with one link and no larger than 4 MiB;
a restore applies the same refusal. An archive with a member outside the layout, or a manifest naming
another account, is a finding about the artifact, for a restore as well.

## What is not verified

- **The content of a dump.** A dump is text that the loader runs against the database server;
  verification does not parse, load or execute it. It establishes that each dump is present in the
  member listing, not that it is complete or valid SQL.
- **A dump member's type or its own digest.** The member listing carries names only, and checking a
  dump's digest would mean extracting every dump to disk after every backup. So a dump that is a
  hard link or symbolic link in the archive is **not** refused by verification, and a dump damaged
  after the archive was written is caught only by the archive's own digest, which covers every byte
  of it. A restore extracts each dump and compares it with its recorded digest before the first drop.
- **The files inside the home.** Their names are scanned; their content is not read.
- **Restorability.** "Verified" is not "restorable". It does not say that the host has room to
  restore, that the databases can be recreated, that the files are the ones the customer meant to
  keep, or that the archive will still be intact tomorrow. The panel says "verified" and gives the
  date, and never "restorable" or "safe".
- **A race between the check and the read.** The archive and its sidecar are checked as plain files
  owned by root with one link, then opened by path, not by a descriptor opened without following
  links. The directory they sit in is root-only and checked first, so only someone who can already
  write root-owned files could swap one in that window. That is the one gap left in path handling.
- **A coordinated replacement.** The sidecar is a label written beside the archive. Someone who can
  write root-owned files can replace the archive, its sidecar and its manifest so that all three
  agree, and verification will pass. It detects corruption and accidental damage, and the case
  where only one of the records changed.
- **A remote copy.** Only the local destination is served by the agent (issue #160).

## The states

| State | Meaning |
|---|---|
| Not verified | No verification has completed for this backup: it predates the feature, or the last attempt could not run. |
| Verified | The last verification found nothing wrong, at the date shown. |
| Failed | The last verification found a problem with the artifact; a short reason is shown. |

A verification that could not run is not a verification that failed, and is journalled on its own
as `BackupVerificationNotPerformed`, not as a success or a failure. When the agent is busy with
another operation on the same account's backups, is saturated, is unreachable or reaches a time bound
on a slow host, the backup keeps its previous state, nothing is mailed, and the attempt can be
repeated. Only a typed finding about the artifact (a changed digest, a refused member, a manifest or
sidecar that disagree, a missing dump) records `Failed`. A failed verification never changes the
run's own status: the run produced an archive, and that stays true.

## Where the panel says so

- **Backups list and detail.** A badge with the state, the date of the last verification, and for a
  failure a reason in words (localized on the server, naming the kind of problem and never a path, a
  member name or a digest).
- **Verify now.** A button on each backup; the result replaces the badge.
- **Alert mail.** A failed verification after a run, scheduled or manual, mails the administrator
  ("Backup verification failed") through the same path as the other backup alerts
  (`OperatorAlertMailer`); see [alerts](alerts.md). A verification that could not run mails nothing.
  A failure found by pressing **Verify now** mails too, by the same rule: **once, when the backup
  moves into the failed state.** Pressing the button again on a backup already recorded as failed
  mails nothing more, so no login can use it to send the administrator one message per press. A backup
  found sound and later found damaged again is a new event and mails again.
- **Rate limit.** Verify now has its own limit of ten requests an hour per hosting account
  (`RateLimiting__BackupVerifyMaxRequests`, `RateLimiting__BackupVerifyWindowSeconds`), because every
  press makes the agent read a whole archive. The verification the panel runs after each backup does
  not count against it.

## What it costs

A verification reads the whole archive once to hash it and once to list its members, and unpacks only
the manifest into a root-only scratch area (`verify/<backup id>` under the agent's scratch root,
removed when the verification ends and at the next agent start if it was killed). It holds the account's backup lock for that time, so it
never reads an archive that a restore is replacing or a delete is removing; if the lock is taken it
is refused at once rather than waited for, and the attempt is recorded as one that could not run.
Every program it starts runs under the agent's bounded-call rules (a deadline that means "wedged",
a capped output drain), with the same bounds a restore has. The panel does not queue or limit
verifications of one backup itself: the agent's lock refuses a second at once, and the verification
after a run is awaited inside that run's handler, up to those bounds. On a very large archive or a slow disk a time bound can make a verification
unavailable; that is recorded as "could not run", never as a failure.

## What has not been verified

The author of this feature could not establish: how a verification behaves under the agent's real
service sandbox, how `tar` reports a truncated archive on every supported distribution and both
architectures, and how long it takes on a very large archive. The security review of this change is
outstanding (threat note `docs/superpowers/notes/2026-11-04-backup-verification-threat-note.md`).
