# Account backups and restores

How an account backup is taken and, above all, what a restore does to a live account. This page
describes what the code does. Where something was reasoned and not measured, it says so.

Related pages: [verification](../backups/verification.md) (what "verified" means in detail),
[alerts](../backups/alerts.md) (what mails the administrator), and
[panel disaster recovery](panel-disaster-recovery.md) (the panel's own database and keys, which an
account backup does not hold).

## What a backup is

One backup is one account: its home directory (`/home/<account>`) and every database the panel lists
for it. The server agent writes them to a single archive and a small record beside it:

```
/var/backups/maran/<account>/<backup id>.tar.gz
/var/backups/maran/<account>/<backup id>.meta.json
```

The directory is root-only. The archive is published under its final name only when the whole backup
has succeeded; a run that fails part of the way removes what it wrote, so a partial archive is never
mistaken for a backup. The panel records the archive's SHA-256 on the backup's row when the run
completes, and a restore refuses an archive whose digest differs from that one.

A backup does not hold the account itself, its web-server configuration, its certificates, its
scheduled tasks, its firewall rules or its SFTP and FTPS logins. Those live in the panel's database
or outside the home, not in the archive.

Backups run on a schedule, or by hand. Retention removes old scheduled and manual backups; a backup
taken automatically before an account is deleted is never pruned.

## What a restore replaces, and what it leaves alone

A restore is "put this copy back over the account". It is not an undo.

| Replaced | Left exactly as it is |
|---|---|
| The home directory: the whole tree is swapped for the one in the archive, then owner, group and mode of the home root are re-applied | Sites, their web-server configuration and their certificates |
| Each database the archive holds, if the panel still lists it for the account: it is dumped (as a rollback copy), dropped, re-created and loaded from the archive | Scheduled tasks, firewall rules, SFTP and FTPS logins |
| | The account itself (the system user and its panel row) |
| | A database the account has that is not in the archive |

So an account restored from a two-week-old backup is a two-week-old home and two-week-old databases
under today's site configuration. Every file and database row written since the backup is gone.

**A backup of the account as it stands now is taken first, and the restore does not start without it.**
Before the restore opens its task or asks the agent for anything, the panel takes a backup of the
account of kind `PreRestore` through the same path as every other backup, to the default destination,
and waits for it to finish. If that backup cannot be made (no destination, the agent refused or lost the
stream, the run produced no digest) the restore is refused with `PreRestoreBackupFailed`, the account is
exactly as it was, and the failed `PreRestore` row stays in the list as the record of what refused. A
`PreRestore` backup is never pruned by retention, so it stays until someone deletes it; it appears in
the backup list like any other, and restoring from it undoes the earlier restore, within the same scope
(home and databases only). The agent itself never takes this backup: it expects its caller to, and the
panel is that caller.

## Starting a restore, and the confirmation

`POST api/v1/backups/{id}/restore` with a body naming the account:

```
{ "confirmAccountUsername": "<the account's system user name>" }
```

The confirmation is the account's system user name, typed by the person. It is compared exactly
(case-sensitive) against the account that owns the backup, never against the caller's own name, so an
administrator restoring for a customer types the customer's account name. The Backups screen's dialog
does not fill it in. A restore is refused while an administrator is viewing as an account, and it is
limited to three requests an hour per hosting account (`RateLimiting__BackupRestoreMaxRequests` and
`RateLimiting__BackupRestoreWindowSeconds`).

Everything below is refused before the agent is asked to do anything, so a refusal changes nothing:

- the backup does not exist, or belongs to another customer (answered as not found);
- the backup is not completed (still running, or failed);
- the account no longer exists (the final backup of a deleted account is kept, and is not restorable);
- the confirmation is missing or is not the account's name;
- another backup or restore of the same account is running (also refused by the agent, per account);
- the home's filesystem has less free space than the agent's free-space floor (default 1 GiB, the
  agent's `--free-space-floor-mib`, set from `MARAN_AGENT_FREE_SPACE_FLOOR_MIB`);
- the archive's digest differs from the recorded one, its member list is outside the archive layout,
  its manifest names another account or backup, or it names a database the panel no longer knows.

Each refusal is written to the audit journal as a failed `BackupRestored` entry. A completed restore
writes a successful one, and the restore is a task in the account's task list, with progress.

## What happens while it runs

The task reports these stages:

| Stage | Percent | State of the account |
|---|---|---|
| `verifying` | 0 to 10 | Untouched. The digest, member list and manifest are checked; the dumps and the home are unpacked into the agent's private scratch and staging areas. |
| `restoring_databases` | 30 to 70 | **The first `DROP DATABASE` inside this stage is the point of no return.** For each database in turn: dump it for rollback, drop it, load the archive's dump. |
| `restoring_files` | 70 to 95 | The databases are already replaced. The current home is renamed aside and the staged home is renamed into its place. |
| `finalising` | 95 to 99 | Ownership and mode are re-applied; the parked old home and the scratch are removed. |

A local restore never reports `downloading`. The percent never reaches 100 until the final result.

If a database fails to load, the databases already replaced are reloaded from their rollback dumps in
reverse order, and the restore reports a failure naming what was put back. The home is untouched at
that point. If the second home rename fails, the first is reversed at once. A restore the agent
reports as complete returns the number of databases replaced; the panel calls it a success only when
the home was replaced and every database it set out to replace was.

Two outcomes are not a success, and the panel's answer says which:

- **Partly replaced.** The agent stated how far it got: the home replaced or not, and `N of M`
  databases. The account is in neither the old nor the new state. **Do not restore over it again.**
  Read the task the panel recorded and the agent's log first. The panel does not carry the names of
  the databases that were or were not replaced; the agent writes those only in its log.
- **Outcome unobserved.** The panel stopped following the stream, or the stream ended with no result.
  The account may already have been replaced. Check its files and databases before anything else.

## When the agent dies in the middle of a restore

If the panel loses the connection, the restore does not stop: the agent runs it to completion and
releases its own lock (it deliberately cannot see that the caller went away). The panel's task then
ends as failed with "the panel stopped following this restore before your server reported a result",
and the audit journal records a failed `BackupRestored`. The backup's own row is not changed by a
restore.

If the agent *process* dies (killed, out of memory, power cut), the restore dies with it. At the next
start, before it accepts any request, the agent reconciles what it finds under `/home/.maran-restore`
and cleans `/var/lib/maran-scratch`. Nothing needs to be run by hand for the cases below. It logs one
line per decision to the journal (`journalctl -u maran-agent`) and a summary line, `interrupted
restores reconciled and the bulk scratch reaped`, with the counts.

The agent writes a small marker file immediately before the two home renames, and only after every
database was replaced. The marker's presence therefore means "the databases are done". Recovery
**finishes the restore forward** rather than undoing it, because undoing would leave the old home
over the new databases, a state nobody asked for:

| What was found | What recovery does |
|---|---|
| Killed after the marker, before the first rename (the live home was never moved) | Removes the staged copy and the marker. The live home is not touched. |
| Killed between the two renames (the account has no home) | Renames the staged home into place, re-applies owner and mode, removes the parked old home and the marker. If that rename fails, the parked old home is put back, so the account has a home either way. |
| Killed after the second rename, before finalising | Re-applies owner and mode, removes the parked old home and the marker. |
| Killed after finalising, before the marker was removed | Removes the marker only. |
| A home exists *and* a parked old home exists *and* a staged home exists | Touches none of the homes (something else re-created the home); removes the staged copy, **leaves the parked home**, and logs an error naming its full path. A person decides which one is wanted. |
| A symlink or non-directory where a directory belongs, or a marker that does not parse | Touches nothing at all and logs an error. |
| Entries in the staging area with no marker (a kill before the marker, or an older build) | Left in place and listed in the log. |

Recovery does **not** put back databases. If the process died while databases were being replaced
(before the marker), some are new and some are old. The rollback dump of each database that was
already dropped (a per-database file the agent takes inside the restore, not the `PreRestore` backup) is kept:

```
/var/lib/maran-scratch/backup/<backup id>/rollback/<database>.sql
```

The agent logs a warning at start naming that directory and the dumps in it (`a killed restore left
pre-restore database dumps behind`). Loading them back is a decision for a person, because the
application may have written to the new schema since, and the agent does not do it unattended. At
most four such sets are kept; when more exist the oldest is removed, with a warning. A restore that
returns, successfully or not, removes its own scratch.

What was reasoned and not measured: that the marker and the renames survive a **power cut** (the agent
syncs the marker file and its directory before the first rename, and relies on the filesystem's own
journalling of renames). What was measured, on the polygon images, is a real `SIGKILL` of the agent
inside the swap window followed by recovery. The panel's task and audit entry cannot be updated by
recovery, because the request that asked for the restore died with the process; the agent's log is
the host's answer to compare them against.

## What "verified" means

The panel verifies every completed backup, and on demand ("Verify now", `POST api/v1/backups/{id}/verify`).
A **Verified** badge says: at the date shown, the archive was read back, its SHA-256 and size matched
what the run recorded, its member list was inside the archive layout, its manifest parsed and named
this account and backup, and every database the manifest names had its dump present in the archive.

It does **not** say the backup is restorable. No dump was loaded or parsed; the files inside the home
were not read; nothing was said about free space, about the databases still existing to be recreated,
or about the archive being intact tomorrow; and someone who can write root-owned files could replace
the archive and its records so they agree. **Not verified** means no verification has answered, not
that the backup is bad. The only way to know a backup restores is to restore it, which is what the
procedure below does. See [verification](../backups/verification.md) for the full list.

## Proving a restore on a real server

Use this to show, on a server you operate, that an account with files and databases can be damaged
and brought back. It destroys data on purpose, so read the guard first.

**Guard.** Run it only on a throwaway account named `proof_<something>`, created for this purpose,
on a server where you hold a current backup of the whole server (the other accounts, the panel's own
backup from [panel disaster recovery](panel-disaster-recovery.md), and the account backups). Do not run
it against an account with any other name unless you have typed that account's name out in full, in
your own notes, before step 4, and accept that its data from after the backup will be lost. Before the destroy step, write down the backup id and the
archive's SHA-256, and do not continue if you cannot.

1. **Create** the account `proof_check` with a plan that includes backups, and a site. Put files of
   every kind in its home: a few hundred small files, one larger file, an empty directory, a symlink,
   a file with mode `0600` and one with mode `0755`. Create a database with the panel and put several
   tables and rows in it. Keep any database password in a file with mode `0600`; never paste it into
   a shell history or a ticket.

2. **Snapshot A.** As root, record the home and the database:

   ```
   sudo find /home/proof_check -printf '%p\t%y\t%U\t%G\t%m\t%s\t%l\n' | sort > A.files
   sudo find /home/proof_check -type f -exec sha256sum {} + | sort -k2 > A.sums
   sudo mysqldump --skip-comments --order-by-primary <database> | sha256sum > A.db
   ```

   The first line records path, type, owner, group, mode, size and symlink target; the second the
   content of every file; the third the database. Modification times are left out on purpose, because
   a restore does not promise them. `mysqldump` as root needs the database server to accept root over
   its local socket; if yours does not, use the credentials you have, from a `0600` option file.

3. **Back up** through the panel (Backups, Back up now). Wait for status `Completed` and a **Verified**
   badge. Note the backup id, then read the digest from the record the agent keeps beside it and check
   it by hand:

   ```
   sudo sha256sum /var/backups/maran/proof_check/<backup id>.tar.gz
   sudo cat /var/backups/maran/proof_check/<backup id>.meta.json
   ```

   The two digests must be equal. Write the id and the digest down. A **Verified** badge is not the
   proof; the restore below is.

4. **Destroy** the account's state: delete the site's files, `DROP DATABASE`, and `chmod` and `chown`
   something. Then take **Snapshot B** the same way and confirm it **differs** from A. A comparison that
   cannot see the damage proves nothing:

   ```
   diff A.files B.files; diff A.sums B.sums; diff A.db B.db
   ```

5. **Restore** from the backup in step 3 (the dialog, or `POST api/v1/backups/<id>/restore`). First try
   a wrong confirmation, for instance `proof_chec`, and confirm it is refused and **Snapshot B still
   matches the damaged state**. Then type `proof_check` and wait for the task to finish. Expect "Replaced"
   for the home and "N databases replaced".

6. **Snapshot C**, then compare with A. The three diffs must be empty:

   ```
   diff A.files C.files; diff A.sums C.sums; diff A.db C.db
   ```

   Check also that the home is owned by the account and that its site, certificate and scheduled tasks
   are as they were before the restore (a restore does not touch them), and that the audit journal has a
   successful `BackupRestored` entry for the account.

7. **Optional, and it affects the whole server.** To see an interrupted restore recover, start a restore
   of `proof_check` again and, while the task shows `restoring_databases` or `restoring_files`, run
   `sudo systemctl kill -s SIGKILL maran-agent`. This stops the agent for every account on the server
   for a few seconds (systemd restarts it after two seconds) and fails any other operation in flight,
   so do it only when nothing else is running. Then read `journalctl -u maran-agent` for the
   reconciliation lines, compare the account against A, confirm `/home/.maran-restore` is empty and
   look at the panel's task (it should be failed) and the audit journal. If you killed before the first
   database was dropped, the account is simply as it was; if between the two home renames, it is
   restored forward. If the kill landed in `restoring_databases` the databases may be mixed, as described
   above, and the rollback dumps are kept for you. A kill that lands outside the stage you wanted is a
   miss, not a result: repeat it.

8. **Record** the date, the host, the operating system, the backup id, the SHA-256, the three diff
   results and what the interrupted restore did. A pass is "step 6 showed no difference". Delete the
   throwaway account when finished.

What this procedure cannot show: a power cut (a `SIGKILL` of the agent is not one), quota enforcement
on the restored home, and SELinux labels, none of which the comparison above records.
