# Panel disaster recovery

Account backups carry your customers' files and databases. They do not carry the panel itself: its
own database (administrators, accounts, plans, sites, schedules, the audit journal), the key every
stored secret is encrypted with, the licence, or `/etc/maran`. Without those, a new server cannot
even read the secrets a copied database holds. `maran panel-backup` and `maran panel-restore` are
the two root-only commands that close that gap.

## What a panel backup holds

| Part | Restored by `panel-restore` |
|---|---|
| The panel database, every schema | yes |
| `Security__EncryptionKey` and `Jwt__SigningKey` from `/etc/maran/panel.env` | yes |
| `/var/lib/maran/licence.json` | yes |
| The rest of `/etc/maran` and `/var/lib/maran` (TLS key, rendered configs, ACME material) | no — inside the archive for manual use |

Everything else in `panel.env` (the host name, ssh ports, the web server's uid) describes the
server it is on, so a restore keeps what the new install wrote.

## Taking a backup

1. Create a passphrase file once, owned by root, mode 0600, holding one line of at least 20
   characters. Keep a copy of the passphrase somewhere other than the server and other than where
   the archives go: without it an archive cannot be opened, by you or anyone.

   ```
   sudo install -m 0600 /dev/null /root/maran-backup.passphrase
   sudo editor /root/maran-backup.passphrase
   ```

2. Write the archive (default directory `/root`; `--out` takes any root-owned directory that group
   and others cannot write):

   ```
   sudo maran panel-backup --passphrase-file /root/maran-backup.passphrase
   ```

   It prints the path of `maran-panel-backup-<UTC time>-<pid>.tar.gpg`, mode 0600. The panel keeps
   running while it is taken.

3. Copy the archive off the server. Whoever holds the archive AND the passphrase holds the panel.

Scheduling and uploading the archive to S3 from the panel are not part of this release; run the
command from root's crontab and copy the file with your own tooling until they are.

## Restoring onto a clean server

1. Install Maran on the new server with the installer, the same release as the backup or a newer
   one. Do not create an administrator in the browser.
2. Copy the archive to the new server, owned by root, and recreate the passphrase file there
   (root-owned, 0600).
3. Restore:

   ```
   sudo maran panel-restore --passphrase-file /root/maran-backup.passphrase \
     --replace-this-panel /root/maran-panel-backup-20261101T120000Z-4242.tar.gpg
   ```

   The command decrypts and checks the archive (a wrong passphrase or a damaged file is refused
   before anything changes), stops the panel, replaces the database in one transaction, writes the
   two keys into `panel.env`, puts the licence back, applies this release's migrations and starts
   the panel. If it is refused part-way it starts the panel again as it was.

4. Sign in with an administrator of the old panel. Every session from the old server has ended.
5. Restore the customers' files, sites and databases from the account backups: a panel backup holds
   the panel's records of them, not their contents.

A licence bound to the old server may need to be re-issued for the new one.
