# Restoring one database or one site

A restore can replace the whole account, one of its databases, or one of its sites. The smaller
scopes exist for the common mistake: a dropped table or an overwritten site that should not cost the
customer everything written since the backup to every other file and database.

This covers a copy kept on this server. Restoring from a remote destination, and restoring an
encrypted copy, are separate.

## What each choice replaces

| Choice | Replaced | Left alone |
|---|---|---|
| The whole account | The home directory and every database the panel knows the account owns | Sites' web-server configuration, certificates, scheduled tasks, firewall rules, SFTP and FTPS logins |
| One database | That database, dropped, re-created and loaded again from the copy | Every file and every other database |
| One site | The site's directory (`sites/<domain>` in the home), swapped for the copy's | Every other file, every database, and the site's web-server configuration and certificate |

A restore is replace-within-scope, not undo, at every scope: what the copy holds replaces what is there.

## What stays the same at every scope

- The account's system user name is typed to confirm, and compared with the account being replaced.
- The panel takes a backup of the whole account as it stands now, waits for it, and refuses the restore if
  it cannot be made. That backup (kind "pre-restore") is the way back; it is kept until deleted by hand.
- The copy's checksum is compared with the digest the panel recorded before anything is unpacked.
- Only one restore, backup or verification runs for an account at a time.

## Choosing the part

The dialog lists the account's CURRENT databases and sites, because the panel does not read inside an
archive. A database or site created after the copy was taken is therefore listed but cannot be
restored from that copy: the server answers that the copy does not hold it (or that the copy file is no
longer on the server), and nothing is changed.

The list is the panel's own, and so is the check: a name that is not this account's is refused as not
found, whoever owns it. A site is named by its domain and the server works out the directory; no path is
ever sent.

## If something goes wrong

- A refused restore (unknown part, mismatched confirmation, unusable copy) changes nothing.
- A database restore that fails after the drop reloads the dump taken just before it, and reports which
  database it could not put back.
- A site restore moves the current directory aside first and deletes it only after the restored one is in
  place. If the second move fails the first is reversed at once. If the reversal also fails, the error names
  the directory where the previous contents are parked; one `mv` puts the site back. A kill of the agent
  between the two moves leaves the same parked directory and no automatic recovery; the pre-restore backup
  covers that case.

## For operators

- Requires panel and agent from the same release: an older agent ignores the scope and would restore the
  whole account, which is why the panel and the agent are refused as a pair when their contract revisions
  differ.
- The audit entry is the usual "restore" action. Its subject is the account, followed by `/database/<name>`
  or `/site/<domain>` for a narrowed restore.
