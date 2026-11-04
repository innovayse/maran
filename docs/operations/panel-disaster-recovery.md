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

## Taking a backup on a schedule

A timer runs `maran panel-backup --scheduled` as root, keeps the newest archives and, when you ask
for it, uploads each one to S3-compatible storage. Nothing is scheduled until you write the
configuration file and enable the timer: a fresh install runs no unattended root job.

### The configuration file

`/etc/maran/panel-backup.conf`, owned by root, mode 0600 (or 0400). A copy with every key and a
comment per key ships as `installer/panel-backup.conf.example`; copy it, then edit:

```
sudo install -m 0600 -o root -g root installer/panel-backup.conf.example /etc/maran/panel-backup.conf
sudo editor /etc/maran/panel-backup.conf
```

The file is read line by line, never executed. A line is blank, a `#` comment, or `KEY=VALUE`; an
unknown key, a repeated key, a quote, `$`, a backtick or a control character is refused, and so is a
file that anyone but root can read or write. The secrets themselves are never in this file: it
holds paths to files that are.

| Key | Meaning | Default |
|---|---|---|
| `PASSPHRASE_FILE` | Absolute path of the passphrase file (root-owned, 0600, one line of at least 20 characters). Required. | none |
| `OUT_DIR` | Directory the archives are written to. Root-owned, mode 0700, not a symlink. | `/var/backups/maran-panel` |
| `KEEP` | How many of the newest archives to keep, an integer from 1 to 365. | `14` |
| `SCHEDULE` | When to run, a systemd `OnCalendar` value. | `*-*-* 02:30:00` |
| `S3_ENDPOINT` | Base URL of the storage service, `https://` only. Leave out all `S3_` keys to keep archives on this server only. | none |
| `S3_BUCKET` | Bucket name. | none |
| `S3_REGION` | Region used to sign the request. | none |
| `S3_PREFIX` | Folder inside the bucket. | none |
| `S3_ACCESS_KEY_ID_FILE` | Absolute path of a root-only (0600) file holding the access key id. | none |
| `S3_SECRET_FILE` | Absolute path of a root-only (0600) file holding the secret key. | none |

The installer creates the default `OUT_DIR`; it never enables the timer.

### Enabling, disabling, checking

```
sudo maran panel-backup-schedule enable
sudo maran panel-backup-schedule status
sudo maran panel-backup-schedule disable
```

`enable` checks the configuration exactly as the job will, and refuses, naming the first problem,
when the file is missing, readable by others or invalid; it changes nothing before it refuses. When
the file is sound it writes the timer's schedule from `SCHEDULE` and starts the timer. `disable`
stops the timer and removes that schedule; it never touches the archives or the configuration.
`status` only reads and prints. The timer is `Persistent`: a run missed while the server was off
happens at the next boot, and each run is delayed by a random amount of up to 15 minutes.

### Retention

After a backup has been written successfully, the job keeps the newest `KEEP` archives in `OUT_DIR`
and deletes the older ones. Three things hold:

- A failed backup deletes nothing, so a full disk or a broken night cannot trade your last good
  archive for nothing.
- Only files whose whole name is `maran-panel-backup-<UTC time>-<pid>.tar.gpg` are ever removed,
  and only regular files directly inside `OUT_DIR`. Your own files, partial files, links and
  folders in the same directory stay, whatever their age. "Newest" is read from the time in the
  name, not from the file's modification time.
- Retention is local only. It never deletes anything in the bucket: remove old objects with your
  storage provider's lifecycle rule, so a compromised panel cannot erase the off-site copies.

### Uploading to S3

When the `S3_` keys are set, each archive is uploaded after it is written and after retention ran.

- Only the finished, encrypted `.tar.gpg` leaves the server, never the unencrypted contents.
  The object is `S3_ENDPOINT/S3_BUCKET/S3_PREFIX/<archive name>` (path-style URL).
- The connection is HTTPS (TLS 1.2 or newer) with certificate checking always on; an `http://`
  endpoint is refused. Redirects are not followed.
- The access key id and secret are read from their root-only files and handed to `curl` through a
  temporary 0600 file in the root-only staging directory, removed when the job ends. They are
  never on a command line, in the environment, in the journal or on the terminal; nor is the
  storage service's reply.
- Success is measured: the service must accept the upload, and a signed `HEAD` request must report
  an object size equal to the local archive's. A job that ends successfully has therefore already
  checked that the object arrived whole. To look yourself, list the bucket with your provider's
  console or any S3 client and compare the object's size with `ls -l` of the archive in `OUT_DIR`.
- A failed upload keeps the local archive and ends the job as failed.
- The storage operator receives ciphertext, the object name (which carries the UTC time), its size
  and the source address. The passphrase is then the only thing between them and your panel, so
  use a long random one and keep it away from both the server and the bucket.

### When it fails

Every failure (missing or insecure configuration, unreadable passphrase, failed dump, retention,
upload) ends the job non-zero, leaves the unit in the `failed` state and writes one line, with the
verb and the outcome only, to the journal:

```
systemctl status maran-panel-backup.service
journalctl -t maran-cli
journalctl -u maran-panel-backup.service
```

Monitor the unit state or that journal tag. Run the job by hand to see it work:
`sudo systemctl start maran-panel-backup.service`.

A disk filling with archives fails the next backup instead of deleting good ones: lower `KEEP` or
move `OUT_DIR` to a larger volume.

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

## Restoring onto a clean server from an off-site copy

When the server is gone, only the bucket (or your own copy) and the passphrase remain.

1. Install Maran on the new server with the installer, the same release as the backup or a newer
   one. Do not create an administrator in the browser.
2. Download the newest `maran-panel-backup-<UTC time>-<pid>.tar.gpg` from the bucket with your
   provider's console or any S3 client, onto the new server, into a directory only root can reach,
   for example `/root`. Make it owned by root with mode 0600:

   ```
   sudo chown root:root /root/maran-panel-backup-20261101T120000Z-4242.tar.gpg
   sudo chmod 0600 /root/maran-panel-backup-20261101T120000Z-4242.tar.gpg
   ```

3. Recreate the passphrase file from the copy of the passphrase you kept elsewhere (root-owned,
   mode 0600, one line), as in "Taking a backup".
4. Restore, exactly as in "Restoring onto a clean server" above:

   ```
   sudo maran panel-restore --passphrase-file /root/maran-backup.passphrase \
     --replace-this-panel /root/maran-panel-backup-20261101T120000Z-4242.tar.gpg
   ```

5. Sign in with an administrator of the old panel, then restore the customers' files, sites and
   databases from the account backups.
6. To keep protecting the new server, write `/etc/maran/panel-backup.conf` again (the old one was
   not in the panel's restored parts) and run `sudo maran panel-backup-schedule enable`.

### What this does not cover

- Customers' files and databases: they are account backups, a separate feature. A panel backup holds
  the panel's records of them, not their contents.
- A licence bound to the old server: it comes back with the restore but may need to be re-issued
  for the new one.
- The rest of `/etc/maran` and `/var/lib/maran` (TLS key, rendered configs, ACME material): they
  are inside the archive for manual use, and not put back by `panel-restore`.
- The passphrase, the S3 credentials and the configuration file: keep your own copies; without the
  passphrase an archive cannot be opened by anyone.

### No new environment variable

The schedule reads only `/etc/maran/panel-backup.conf`. It adds no variable to `panel.env`, so
`installer/panel.env.example` is unchanged.

### Proven by

The installer polygon runs the whole round trip against a real systemd container: the stage
`stage_panel_recovery` in `scripts/lib/install-polygon.sh` creates administrators, accounts and a
stored secret, runs the scheduled job (retention and an upload to a local S3-compatible stand-in),
installs a clean panel, restores, and signs in as the same administrator. The assertions on the
installed units, the configuration checks and the schedule verb are in
`docker/polygon/asserts/parts/panel-backup-units.sh`. Not observed there: the timer firing on the
clock, and any particular S3 provider accepting the request.
