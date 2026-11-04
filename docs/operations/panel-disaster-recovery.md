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

   `/root` is fine for a manual run. The scheduled job cannot see `/root` or `/home`, so for the
   schedule the passphrase file lives elsewhere (see "Where the secret files go" below).

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

### Where the secret files go

The scheduled job runs with `/root` and `/home` hidden from it (`ProtectHome=yes` in the unit). A
passphrase or credential file under either is invisible to the job, which then refuses with "is not
a regular file". Keep the files of the schedule in a directory of their own, for example
`/etc/maran-backup`, and not under `/etc/maran`: that tree is inside every archive, and a passphrase
stored in the archive it protects protects nothing. `maran panel-backup-schedule enable` checks this
for you: it refuses, naming the key and the path, when `PASSPHRASE_FILE` or, with S3 configured,
`S3_ACCESS_KEY_ID_FILE` or `S3_SECRET_FILE` is under `/root` or `/home`, whether written that way
or reached through a symlink, so the first night's run cannot fail on it. (The manual flow above is unaffected: a manual
run is not under the unit, so `/root/maran-backup.passphrase` keeps working for it.)

```
sudo install -d -m 0700 -o root -g root /etc/maran-backup
sudo install -m 0600 -o root -g root /dev/null /etc/maran-backup/passphrase
sudo editor /etc/maran-backup/passphrase
```

### The configuration file

`/etc/maran/panel-backup.conf`, a regular file (not a symlink) owned by root:root with mode 0600 or
0400, in a directory owned by root that group and others cannot write, and no larger than 4096
bytes. A copy with every key and a comment per key is installed as
`/usr/local/maran/cli/panel-backup.conf.example` (source: `installer/panel-backup.conf.example`);
copy it, then edit:

```
sudo install -m 0600 -o root -g root /usr/local/maran/cli/panel-backup.conf.example /etc/maran/panel-backup.conf
sudo editor /etc/maran/panel-backup.conf
```

The file is read line by line, never executed. A line is blank, a `#` comment, or `KEY=VALUE`, and
the value is everything after the first `=`. Refused, naming the line or the key but never printing
a value: an unknown key, a repeated key, a line without `=`, any control character (this includes
the carriage return of a CRLF file and a tab, in comments too), a NUL byte, and a value holding a
quote, `$`, a backtick or a backslash. Do not quote values or put spaces in them (`SCHEDULE` is the
one value that contains spaces). A key left with an empty value is refused, except the optional
`S3_` keys. The secrets themselves are never in this file: it holds paths to files that are.

| Key | Meaning and exact validation | Default |
|---|---|---|
| `PASSPHRASE_FILE` | Absolute path of letters, digits, `.`, `_`, `-` and `/` (up to 200 characters after the first `/`, no `..`). The file is owned by root, mode 0600 or 0400, its first line at least 20 characters. Required. | none |
| `OUT_DIR` | Absolute path, same alphabet and limits. Owned by root, a real directory (not a symlink), not writable by group or others. Must already exist. | `/var/backups/maran-panel` |
| `KEEP` | Integer from 1 to 365, no leading zero. | `14` |
| `SCHEDULE` | At most 64 characters, one of: `hourly`, `daily`, `weekly`, `monthly`; or an optional weekday (`Mon`..`Sun`, a range `Mon..Fri` or a list `Mon,Wed`), an optional date `YEAR-MONTH-DAY` where each part is `*` or digits, and a time `HH:MM` or `HH:MM:SS` where the hour is `*` or a comma list and the minute is a comma list of numbers. Forms with `/` steps or `*` minutes are not accepted. | `*-*-* 02:30:00` |
| `S3_ENDPOINT` | `https://` followed by a host name or address and an optional `:port`. No user name, path, query or fragment. Leave out every `S3_` key to keep archives on this server only. | none |
| `S3_BUCKET` | Lower-case letters, digits, `.` and `-`, 3 to 63 characters, starting and ending with a letter or digit. | none |
| `S3_REGION` | Lower-case letters, digits and `-`, 2 to 31 characters, starting with a letter or digit. Used to sign the request. | none |
| `S3_PREFIX` | Optional folder inside the bucket: up to 8 components separated by `/`, each up to 64 characters of letters, digits, `.`, `_`, `-`, not starting with a dot, no leading or trailing `/`. | none (objects go to the bucket root) |
| `S3_ACCESS_KEY_ID_FILE` | Absolute path of a file owned by root:root, mode 0600 or 0400, whose first line is the access key id. | none |
| `S3_SECRET_FILE` | The same for the secret key. | none |

The S3 keys are all or nothing: any `S3_` key without `S3_ENDPOINT` is refused, and an endpoint
without `S3_BUCKET`, `S3_REGION`, `S3_ACCESS_KEY_ID_FILE` and `S3_SECRET_FILE` is refused, so a
half-written block never quietly means "no upload". The key id and secret files must hold one value
on the first line made only of letters, digits and `/ + = _ . -` (at most 512 characters).

The installer creates the default `OUT_DIR` (root:root, 0700) and `/var/lib/maran-cli-staging`; it
never enables the timer and never writes the configuration.

### A different OUT_DIR

The service unit may write to exactly three places: `/var/backups/maran-panel`,
`/var/lib/maran-cli-staging` and `/run` (`ReadWritePaths=`). For any other `OUT_DIR`,
`maran panel-backup-schedule enable` adds that one directory to the unit itself, so there is nothing
to edit by hand. Create the directory first (root:root, 0700); `enable` refuses, naming `OUT_DIR`,
when it does not exist or is a symlink:

```
sudo install -d -m 0700 -o root -g root /srv/maran-panel-backups
```

then set `OUT_DIR=/srv/maran-panel-backups` in the configuration and run `enable`. It writes
`/etc/systemd/system/maran-panel-backup.service.d/outdir.conf`:

```
[Service]
ReadWritePaths=/srv/maran-panel-backups
```

and reloads systemd. The directory is the validated `OUT_DIR` and nothing else is added to the unit.
With the default `OUT_DIR` no drop-in exists: `enable` removes one left by an earlier configuration,
and `disable` removes it too, so the writable set always matches the configuration. Run `enable`
again after changing `OUT_DIR`.

The directory has to exist whenever the unit starts, or systemd refuses to start it (status
226/NAMESPACE): do not remove it while the schedule is on.
Retention also refuses a directory at the top level, inside a system tree (`/bin`, `/boot`, `/dev`,
`/etc`, `/home`, `/lib*`, `/proc`, `/root`, `/run`, `/sbin`, `/sys`, `/usr`), one of `/var/lib`,
`/var/log`, `/var/backups`, `/var/cache`, `/var/spool`, `/var/tmp`, `/var/mail`, or the panel's own
`/etc/maran`, `/var/lib/maran` and `/var/lib/maran-cli-staging`.

### Enabling, disabling, checking

```
sudo maran panel-backup-schedule enable
sudo maran panel-backup-schedule status
sudo maran panel-backup-schedule disable
```

`enable` first refuses, naming the first problem and changing nothing, when the timer or service
unit is not installed, or the configuration is missing, readable or writable by others or invalid
(it checks exactly what the job checks, including that the passphrase and the S3 credential files
pass their file checks), one of those files is under `/root` or `/home` (see "Where the secret files
go"), a non-default `OUT_DIR` is not a plain directory, or `systemd-analyze calendar` does not accept
`SCHEDULE`. Only then does it write the timer's schedule as a drop-in,
`/etc/systemd/system/maran-panel-backup.timer.d/schedule.conf`, write or remove the `OUT_DIR` drop-in
(see "A different OUT_DIR"), reload systemd and run `systemctl enable --now` on the timer. It prints
only fixed text, not the schedule or any configured value:

```
Scheduled panel backup enabled.
Check the next run with: maran panel-backup-schedule status
```

Run `enable` again after editing `SCHEDULE` or `OUT_DIR`; it rewrites the drop-ins. `disable` stops
and disables the timer, removes both drop-ins and prints `Scheduled panel backup disabled. Existing archives and
the configuration are untouched.` It never touches the archives or the configuration. `status`
reads systemd only (never the configuration) and prints, for example:

```
Scheduled panel backup:
  enabled      enabled
  active       active
Timer:
  NextElapseUSecRealtime=Thu 2026-11-05 02:41:17 UTC
  LastTriggerUSec=Wed 2026-11-04 02:38:52 UTC
Last run:
  Result=success
  ExecMainStatus=0
  ExecMainExitTimestamp=Wed 2026-11-04 02:39:40 UTC
```

`status` exits 0 only when the timer is both enabled and active, so it can serve as a probe; it exits
non-zero otherwise. The timer is `Persistent`: a run missed while the server was off happens at the
next boot, and each run is delayed by a random amount of up to 15 minutes after the scheduled time.
The service has no `[Install]` section, so nothing but the timer (or your own `systemctl start`)
starts it.

### What one run does

In this order: read and check the configuration, take the backup into `OUT_DIR` (the same code as
the manual verb), apply retention, upload when S3 is configured. A failed retention does not skip
the upload and a failed upload does not undo retention; either makes the run fail at the end, after
both were tried. Only one backup, restore, update or rollback runs at a time on a server: a second one exits at once
with "another panel backup, restore, update or rollback is running". On success the run prints the path of the new
archive:

```
Retention: kept the newest 14, removed 0 older panel archive(s).
Scheduled panel backup written: /var/backups/maran-panel/maran-panel-backup-20261104T023917Z-4242.tar.gpg
```

### Retention

After a backup has been written successfully, the job keeps the newest `KEEP` archives in `OUT_DIR`
and deletes the older ones. These hold:

- A failed backup deletes nothing, so a full disk or a broken night cannot trade your last good
  archive for nothing. Retention also refuses to run unless the archive just written is present,
  non-empty and in its own listing.
- Only files whose whole name is `maran-panel-backup-<UTC time>-<pid>.tar.gpg` are ever removed,
  and only regular files directly inside `OUT_DIR`. Your own files, partial files (`.part`), links
  and folders in the same directory stay, whatever their age. "Newest" is read from the time in the
  name (then the number), not from the file's modification time. A manual backup written into the
  same directory has the same name shape and counts like any other.
- The archive just written is never removed. If the directory holds an archive whose name carries a
  time later than now (a clock that was set back, or a file copied in), that archive ranks as the
  newest, and `KEEP` + 1 archives can remain until it ages out of the window.
- Retention is local only. It never deletes anything in the bucket: remove old objects with your
  storage provider's lifecycle rule, so a compromised panel cannot erase the off-site copies.

### Uploading to S3

When the `S3_` keys are set, each archive is uploaded after it is written and after retention ran.

- Only the finished, encrypted `.tar.gpg` leaves the server, never the unencrypted contents.
  The object is `S3_ENDPOINT/S3_BUCKET/S3_PREFIX/<archive name>` (path-style URL), with the prefix
  part left out when `S3_PREFIX` is empty. The request is signed by `curl --aws-sigv4`, which needs
  curl 7.75 or later; the payload is sent unsigned (the connection is TLS).
- The connection is HTTPS (TLS 1.2 or newer) with certificate checking always on; an `http://`
  endpoint is refused. A storage service with a private certificate authority must have it in the
  system trust store: there is no option to skip verification. Redirects are not followed, and
  curl's own configuration file for root is ignored.
- The access key id and secret are read from their root-only files and handed to `curl` through a
  temporary 0600 file in the root-only staging directory, removed when the upload ends (and by the
  job's exit handling if it is killed). They are never on a command line, in the environment, in
  the journal or on the terminal; nor is the storage service's reply.
- Timeouts and retries are fixed: 30 seconds to connect, two hours per request, up to three tries
  with a 10 second pause, and only for connection failures and 5xx answers. A 4xx answer (wrong
  key, wrong signature, no such bucket) is final at the first try.
- Success is measured: the service must accept the upload with a 2xx answer, and a signed `HEAD`
  request must report a `Content-Length` equal to the local archive's size. A job that ends
  successfully has therefore already checked that the object arrived whole. A service whose `HEAD`
  answer carries no `Content-Length` is reported as a size mismatch.
- A failed upload keeps the local archive and ends the job as failed.
- The storage operator receives ciphertext, the object name (which carries the UTC time), its size
  and the source address. The passphrase is then the only thing between them and your panel, so
  use a long random one and keep it away from both the server and the bucket.

#### Checking that an upload arrived

The journal line `verb=panel-backup-upload outcome=uploaded` is written only after the size check
above passed:

```
journalctl -t maran-cli --no-pager | grep 'verb=panel-backup-upload outcome=uploaded'
```

To look from outside the server, ask the storage directly and compare with the file in `OUT_DIR`.
With the AWS command line, for example (the credentials being those of the key files):

```
aws s3api head-object --endpoint-url https://s3.example.com --bucket example-bucket \
  --key maran-panel/maran-panel-backup-20261104T023917Z-4242.tar.gpg
sudo stat -c %s /var/backups/maran-panel/maran-panel-backup-20261104T023917Z-4242.tar.gpg
```

`ContentLength` must equal the second number. Downloading the object and comparing `sha256sum`
against the local file is the strongest check, and a restore test from the downloaded copy
(below) is stronger still.

### When it fails

Every failure (missing or insecure configuration, unreadable passphrase, failed dump, retention,
upload) ends the job non-zero and leaves the unit in the `failed` state. The reason, as a short
fixed sentence that never contains a secret, a path of a secret or the storage's reply, is on the
unit's output (`journalctl -u maran-panel-backup.service`). Lines under the tag `maran-cli` carry the
verb and outcome only:

```
verb=panel-backup-scheduled outcome=written operator=root
verb=panel-backup-scheduled outcome=failed-1 operator=root
verb=panel-backup-upload outcome=uploaded operator=root
verb=panel-backup-upload outcome=rejected-403 operator=root
```

The scheduled line is written once per run, whatever the outcome. The upload writes its own line as
well: `uploaded`, `size-mismatch`, `rejected-<HTTP status>` (the storage refused, or `-000` when
there was no answer and no retry applies, for instance a certificate that is not trusted),
`failed-<status>` (no usable answer after three tries) or `refused` (a value or file failed its
check before any request was made). So a failed upload shows two failure lines, and a good run with
an upload shows two lines.

```
systemctl status maran-panel-backup.service
journalctl -t maran-cli
journalctl -u maran-panel-backup.service
```

Monitor the unit state or that journal tag. Run the job by hand to see it work:
`sudo systemctl start maran-panel-backup.service`.

A disk filling with archives fails the next backup instead of deleting good ones: lower `KEEP` or
move `OUT_DIR` to a larger volume (see "A different OUT_DIR").

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
6. To keep protecting the new server, recreate the passphrase and credential files (for the schedule
   in `/etc/maran-backup`, not under `/root` or `/home`), write `/etc/maran/panel-backup.conf`
   again (the old one was not in the panel's restored parts) and run
   `sudo maran panel-backup-schedule enable`.

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

The installer polygon runs the whole round trip against a real systemd container. The stage
`stage_panel_recovery` (`scripts/lib/install-polygon-recovery.sh`, called from
`scripts/lib/install-polygon.sh`; skipped only by `--no-panel-recovery`, and it must make at least
100 checks or the run fails) goes through these parts:

- `rec_seed_panel` creates an administrator's second factor (stored encrypted in the database), a
  hosting account and its owner's login through the panel's own API
  (`scripts/lib/install-polygon-panel-api.sh`).
- `rec_schedule_leg` (`scripts/lib/install-polygon-schedule.sh`) writes the configuration under
  `/etc/maran`, checks that a world-readable file, a symlinked file, a `SCHEDULE` holding a shell
  command and an unknown key are each refused and ran nothing, then runs the job three times under
  `KEEP=2` (only the newest two archives remain, the operator's own files, a `.part` file, a link
  and a folder survive, and a failing backup deletes nothing), uploads to an S3-compatible stand-in
  over TLS (`scripts/lib/install-polygon-s3.sh`, `docker/polygon/stand-ins/s3-stand-in.py`; the
  stand-in recomputes the request signature, so a wrong key id, a wrong secret and an untrusted
  certificate are each refused and a good upload is size-checked), runs `enable`, `status` and
  `disable`, and starts `maran-panel-backup.service` by name.
- `rec_manual_backup`, then `rec_take_archives_off` and `rec_fresh_leg` stop the first server for
  good and install a second, fresh one whose keys are shown to differ.
- `rec_restore_leg` shows a wrong passphrase is refused with the fresh panel unchanged, then
  restores the scheduled archive and shows the same administrator signs in with the same password
  and the same one-time-code secret (so the encrypted column decrypted under the restored key),
  the account and its owner exist and the fresh install's own host settings were kept, then
  restores once more from the manual archive.

The assertions on the installed units, the configuration checks and the schedule verb are in
`docker/polygon/asserts/parts/panel-backup-units.sh`. Not observed there, and printed as such by
the stage: the timer firing on the clock and `Persistent=true` catching up, any particular S3
provider accepting the request (the stand-in is one bucket, path-style), the upload's `curl`
command line while it runs, the licence half of a restore (the polygon install holds no licence),
SELinux enforcing, and any PostgreSQL major or family other than the one the run was started on.
The upload leg is skipped, with the reason printed, where the container's `curl` is older than 7.75.
