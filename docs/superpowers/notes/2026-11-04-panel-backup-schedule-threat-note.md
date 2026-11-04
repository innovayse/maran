# Threat note: the panel backup schedule, retention and S3 upload (issue #129)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps and to how secrets are handled. The earlier note
(`2026-11-01-panel-disaster-recovery-threat-note.md`) argued the two verbs `maran panel-backup` and
`maran panel-restore`. This note covers what #129 adds on top of them, and it is written BEFORE the
code: every statement below is a requirement the code must meet, not a description of code that
exists. Where the code lands and differs, the code is wrong or this note is amended in the same change.

**Second review: OUTSTANDING — must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch `fix/129-panel-backup-schedule` MUST NOT merge to
`main` until a human reviewer has read it (rules/security.md "Sensitive change escalation"). The owner
is told at hand-off that this surface carries an outstanding review. The earlier note's review is
outstanding as well; nothing here discharges it.

## What is added

1. A root job: `maran-panel-backup.service` (oneshot, `User=root`) started by
   `maran-panel-backup.timer`, running `maran panel-backup --scheduled`.
2. A root-only configuration file, `/etc/maran/panel-backup.conf`, read by an allow-list parser.
3. Retention: after a successful backup, root deletes older panel archives from `OUT_DIR`.
4. An optional upload of the finished encrypted archive to S3-compatible storage with `curl`.
5. A verb `maran panel-backup-schedule enable|disable|status` that validates the config and installs,
   enables or disables the timer.
6. A restore-onto-clean-install proof in the installer polygon (the issue's closing criterion).

Not added: no daemon beyond the systemd units root already runs, no listening port, no RPC, no new
package (`curl` is already an installer dependency; the step confirms it on both families and adds it
where missing), no change to the archive format.

## 1. The scheduled job

- The service runs the same code path as the manual verb with one added flag; it adds no second
  implementation of the backup. `--scheduled` only changes where the arguments come from (the config
  file instead of the command line) and what happens after the archive exists (retention, upload).
- Requirement: unit hardening as strict as the verb allows — `PrivateTmp=yes`, `NoNewPrivileges=yes`,
  `ProtectHome=yes`, `ProtectSystem=strict` with `ReadWritePaths` limited to `OUT_DIR`,
  `/var/lib/maran-cli-staging`, gpg's staging and `/run`. A hardening directive that the verb cannot
  live with (it reads `/etc/maran`, `/var/lib/maran`, and runs `runuser` to `maran` for `pg_dump`) is
  left out and the reason is written in the unit, not silently dropped. The unit must pass
  `systemd-analyze verify`.
- Requirement: the timer is NOT enabled by the installer. The operator writes the config first and
  runs `maran panel-backup-schedule enable`; a fresh install therefore runs no unattended root job
  until a human has chosen a passphrase file.
- Requirement: the schedule value lives in a systemd drop-in that `enable` writes. `SCHEDULE` must
  match a strict regex allow-list (security.md item 4: a value written into a systemd unit rejects
  newlines, carriage returns and control characters). Validation is a positive match against the
  accepted `OnCalendar` shapes, not a search for forbidden characters. The drop-in is rendered by
  root from the validated value and written beside its target, then renamed into place.
- Requirement: any failure — missing or insecure config, unreadable passphrase, failed dump, failed
  retention, failed upload — returns non-zero and writes ONE line to the journal under the tag
  `maran-cli` (verb and outcome only, never an argument, path of a secret, or tool output), the same
  audit shape the manual verbs use. The unit then shows `failed` in `systemctl`, which is what an
  operator monitors.
- Requirement: a concurrent run must not corrupt the archive or the staging directory. The archive
  name already carries the PID and is created with `O_EXCL`; the stage directory is per run. A second
  run that finds the first still running exits non-zero rather than waiting or sharing state.

## 2. The configuration file

The file is read by root, and it names a passphrase file, an output directory, credentials files and
an endpoint, so whoever can write it steers a root process.

- Requirement: refused unless it is a regular, non-symlink file owned by root:root with mode 0600 or
  0400 (the check the passphrase file already gets), inside a directory that is not writable by anyone
  but root. A world- or group-readable or writable config is refused before one byte is parsed,
  naming the first problem.
- Requirement: the file is NEVER sourced, evaluated or passed to a shell. It is read line by line;
  each line must be blank, a comment, or `KEY=VALUE` with KEY in a fixed allow-list (PASSPHRASE_FILE,
  OUT_DIR, KEEP, SCHEDULE, S3_ENDPOINT, S3_BUCKET, S3_REGION, S3_PREFIX, S3_ACCESS_KEY_ID_FILE,
  S3_SECRET_FILE). An unknown key, a duplicate key, a line without `=`, a value containing a control
  character, a quote, `$`, a backtick or a newline is a refusal, not a skip. The value is data in a
  variable and reaches commands only as one argv element.
- Requirement: each value is validated by type before use, again at the point of use. PASSPHRASE_FILE
  and the two credential files are absolute paths that pass the existing file checks; OUT_DIR is an
  absolute path with no `..`; KEEP is an integer from 1 to 365; S3_ENDPOINT begins with `https://` and
  carries no userinfo, query or fragment; S3_BUCKET, S3_REGION and S3_PREFIX match fixed narrow
  alphabets (no `/..`, no whitespace, no control characters).
- The secret values themselves (passphrase, access key id, secret key) are never IN this file. It
  holds paths to 0600 files, so the config can be shown to a reviewer or attached to a ticket without
  leaking a credential, and the secret files carry the checks that matter.

## 3. Retention: a deletion primitive run by root

This is the most dangerous addition. It is `rm` run by root against a directory, driven by a number
from a config file. The requirements are written as the ways it must be unable to go wrong.

- **Only after a successful backup.** Retention runs after the new archive exists, is non-empty and
  has been verified to be listed by the same enumeration retention uses. A failed backup deletes
  nothing, so a disk-full or broken-gpg night cannot trade the last good archive for nothing.
- **Exact name pattern.** A candidate is deleted only when its basename matches
  `maran-panel-backup-<UTC>-<pid>.tar.gpg` in full, anchored at both ends, with the UTC and pid fields
  matched by digit classes. Anything else in the directory — an operator's own file, a partial
  `.tar.gpg.part`, a symlink, a directory — is never touched, whatever its age.
- **Never follows symlinks.** A candidate must be a regular file (`-f` and not `-L`, checked on the
  entry itself) directly inside `OUT_DIR`; the enumeration does not recurse and no candidate path is
  built by concatenating text from outside `OUT_DIR`. Removal is by argv array with `--` before the
  names, never through a shell string or a glob expanded after the check.
- **Only inside OUT_DIR.** `OUT_DIR` is checked to be root-owned, a real directory (not a symlink) and
  not writable by anyone else (0700 as created by the installer step) before any deletion; otherwise
  retention is refused. `OUT_DIR` equal to `/`, `/root`, `/etc`, `/home` or any path shallower than a
  fixed depth is refused as a guard against a typo in the config that would aim the primitive at a
  directory of other things. The pattern makes that harmless today; the guard is there for the day the
  pattern is loosened.
- **Newest N kept, ordering from the name, not the mtime.** The UTC stamp in the name orders the
  archives, so a `touch` by someone else cannot reorder them. The newest `KEEP` are retained; the one
  just written is always among them even if `KEEP` were lower than its rank (the integer floor of 1
  already guarantees this).
- **Remote copies are never deleted by this command.** Retention is local only. An S3 bucket keeps
  what was uploaded until the operator's own lifecycle rule removes it; a compromised panel therefore
  cannot erase the off-server copies through this verb. This is a deliberate asymmetry and is stated
  in the operator documentation.
- An attacker who can plant files in `OUT_DIR` already has root (the directory is root-only), so the
  realistic threat to this primitive is the config, covered above, and an operator typo, covered by
  the guards.

## 4. The S3 upload: a configured outbound call from the server

`rules/security.md` item 10 says no new outbound calls; the only phone-home is the documented licence
lease; anything else needs a spec change first. This feature IS an outbound call, so it is listed
here as an exception that needs the owner's decision recorded and the spec amended, and the argument
for it is:

- **It is operator-configured, like account-backup destinations.** The panel already ships customer
  backups to destinations the operator names. This is the same act applied to the panel's own
  archive: nothing is sent unless the operator wrote `S3_ENDPOINT` into a root-only file and enabled
  the timer. With no S3 keys in the config, the code path does not execute and no connection is made.
  It is not telemetry, not a phone-home, and the project's servers are never the destination.
- **It carries only the encrypted archive.** What leaves the host is the `.tar.gpg` after gpg has
  encrypted it (the earlier note's AES-256 with an iterated, salted S2K). The plaintext exists only
  in the root-only staging directory and is never the thing uploaded; the upload step takes the
  finished archive by path and cannot be pointed at anything else.
- **https only.** `--proto =https` and `--tlsv1.2`; an `http://` endpoint is refused at config
  validation and again by curl. Certificate verification is never disabled (no `-k`, no
  `--insecure`, no `--ssl-no-revoke`).
- **Credentials never on argv, in the environment, or in a log.** The access key id and secret are
  read from their 0600 files by root and written into a curl config file (`-K`) created with
  `umask 077` and `O_EXCL` inside the root-only staging directory; curl reads them from it. That file
  is removed by the same EXIT trap that removes the plaintext. `--user` and the signing material
  never appear in `ps`, in `/proc/<pid>/environ`, or in a `set -x` trace (tracing is never enabled in
  these libraries). Requirement: the script prints no URL query, no signed header and no credential,
  and the one journal line carries only the verb and outcome.
- **SSRF and endpoint abuse by someone who can write the config.** The config is root:root 0600; a
  party who can write it already holds root and can run `curl` themselves, so the file is not a new
  privilege boundary. It is still defended, because a root service that fetches a URL it was given is
  the pattern an SSRF audit looks for: the call is a PUT of one fixed object name to
  `ENDPOINT/BUCKET/PREFIX/NAME` assembled from validated parts, `--max-redirs 0` and no `-L`, so a
  hostile or compromised endpoint cannot bounce the request, with its signature, to an internal
  address; no value from a response (headers, body, `Location`) is ever used to build another request.
  The honest limit: an `https://` endpoint that resolves to a private address (`https://127.0.0.1`,
  an internal MinIO) is a legitimate configuration — operators run S3-compatible storage on their own
  network — so the code does not and cannot block private ranges. The control is that the file is
  root-only, not that the destination is public.
- **Response bodies never logged or printed.** curl runs with output discarded (`--output /dev/null`)
  and `--write-out` of the HTTP status only, `--silent --show-error` with stderr mapped to a fixed
  message. An S3 error document can echo the access key id, the request id or a bucket name; none of
  it reaches the journal or the terminal.
- **Success is measured, not assumed.** The PUT must return a 2xx (`--fail`, status checked), and a
  signed `HEAD` of the same object must report a length equal to the local archive's size, before the
  verb says it succeeded. A 200 with a truncated body is therefore not a success.
- **Failure keeps the local archive.** A failed upload never removes, renames or truncates the local
  archive, never skips retention's rule that retention ran only after a good backup (retention may
  already have run on the local copy; an upload failure does not undo it and does not delete
  anything), exits non-zero and writes one journal line. The local copy is the recovery path that
  does not depend on a network.
- **Residual exposure.** The destination operator receives ciphertext and metadata (object name with
  UTC time, size, source IP, timing). A weak passphrase is the attacker's route here too, and the
  object now lives outside the owner's control, so the passphrase floor of the earlier note matters
  more, not less; the operator documentation says so.

## 5. The schedule verb

`maran panel-backup-schedule enable|disable|status` is a fixed-verb wrapper like its neighbours:

- Requirement: root only; the wrapper refuses a non-root caller with 77 before reading anything.
- Requirement: `enable` validates the config exactly as the job will (section 2), refuses when it is
  missing, insecure or invalid and names the first problem only, then writes the drop-in and runs
  `systemctl daemon-reload`, `systemctl enable --now maran-panel-backup.timer` as an argv array.
  `disable` stops and disables the timer and removes the drop-in. `status` reads and prints, writes
  nothing.
- Requirement: `uninstall.sh` removes the units, the timer and the drop-in, and NEVER the operator's
  archives or the config file's secrets; the archives are the point of the feature.
- Requirement: the unit files are installed by `installer/lib/67-panel-backup.sh` (root:root 0644)
  with `OUT_DIR` created root:root 0700; the step does not enable anything.

## 6. The end-to-end proof

The earlier note listed the full round trip as unrun. #129's closing criterion is that proof, and the
polygon stage (against a real systemd container) will make it: create administrators, accounts and a
stored secret on a panel; run the scheduled job; confirm retention and (against a local S3-compatible
stand-in) the upload; install a clean panel; restore; start; sign in as the same administrator; read
the same accounts; decrypt the same secret.

Stated limits of that proof, so it is not mistaken for more:

- The polygon container has a systemd stand-in or real systemd depending on the image; where a step
  cannot observe the timer firing on its schedule, the stage invokes the service unit by name and the
  output says it did not wait for the clock. UNOBSERVED HERE: the timer firing at `SCHEDULE`, and
  `Persistent=true` catching up after downtime.
- The S3 leg in the polygon talks to a stand-in on a private address over a certificate the polygon
  creates; it proves the request is formed, signed and size-checked, not that any particular provider
  accepts it.
- A restore proof run once proves one restore. Both PostgreSQL majors (13 and 16) and both families
  are only covered to the extent the polygon matrix runs the stage on them.

## What an attacker could do with this surface

- **Read the config or a credential file.** They get paths and an endpoint, not secrets; the secret
  files are 0600 root, so the reading already needs root.
- **Plant a config.** Needs root. A planted config can aim the upload at an attacker's bucket, so the
  attacker receives the encrypted archive; to read it they still need the passphrase file's contents.
  Same bound as stealing an archive in the earlier note: the archive is as sensitive as root.
- **Make retention delete something valuable.** Requires a config naming a different `OUT_DIR`; the
  exact-name pattern means only files that look like our archives go. The worst case is loss of
  archives in a directory the operator pointed at, which is the operator's act and is documented.
- **Fill the disk with archives.** `KEEP` is capped at 365 and the archive size is the panel's size;
  an operator who sets 365 on a large panel can fill `/var/backups`. Disk-full fails the backup
  (non-zero, logged), and retention does not run, so nothing good is deleted. Stated, not prevented.
- **A process running as `maran` gains nothing.** The wrapper is 0700 root:root, the config and
  secret files are root-only, `OUT_DIR` is 0700 root, the units run as root but are written only by
  the installer, and no setuid file, sudoers entry, socket or group membership is added.

## What a reviewer must check

1. The config reader: read it line by line against section 2. Feed it a value with `$(...)`, a
   backtick, a quote, a trailing backslash, a duplicate key, an unknown key, CRLF line endings and a
   NUL. It must refuse each and must still accept a well-formed file (inverse control).
2. The retention function against section 3 with a directory holding: a symlink named like an archive
   pointing outside, a directory named like an archive, a near-miss name (extra suffix, missing pid),
   an archive from the future, and fewer than `KEEP` archives. Only the right files are removed, and
   a failed backup removes nothing. Check the order is taken from the name.
3. The curl invocation: print the full argv and the full environment of the curl process (not just the
   script) during an upload and confirm no secret appears; confirm the `-K` file's mode and that the
   EXIT trap removes it on success, failure and a signal; confirm `--max-redirs 0`, `--proto =https`,
   no `-k`, and that stdout/stderr of curl cannot reach the journal.
4. That a failed upload and a failed HEAD size check each exit non-zero, log one line, and leave the
   local archive byte-identical.
5. The unit: `systemd-analyze verify`, `systemd-analyze security maran-panel-backup.service`, and
   whether each hardening directive left out is justified in a comment.
6. That `enable` refuses on a bad config and writes nothing before refusing; that `disable` leaves
   the archives; that `uninstall.sh` does the same.
7. Item 10 of rules/security.md: that the owner has recorded the decision to accept this configured
   outbound call and that the spec is amended, since this note argues the case but cannot grant it.

## What the author could not verify

1. Nothing in this note has run: the code does not exist yet, and this session cannot run the installer
   or the polygon. Every statement above is a requirement until a gate pass and a reviewer say
   otherwise.
2. The restore-onto-clean-install round trip on a real host, on both PostgreSQL majors and both
   families, is the closing criterion and is unproven at the time of writing.
3. `curl --aws-sigv4` behaviour across the curl versions of the supported systems (it needs curl 7.75
   or later; Ubuntu 22.04, Debian 12/13 and AlmaLinux/Rocky 9 and 10 ship versions that must be read
   from each distribution's own documentation, not assumed) and against real S3-compatible providers,
   including their handling of path-style URLs and of the signed HEAD used for the size check.
4. That `ProtectSystem=strict` with the listed `ReadWritePaths` leaves the verb everything it needs
   (`runuser` to `maran`, gpg's home inside the staging directory, `restorecon` on AlmaLinux) with
   SELinux enforcing; the list is derived by reading the verb, not by observing it under the unit.
5. That `OnCalendar` regex allow-list accepts every schedule an operator reasonably writes and
   nothing systemd would interpret beyond a calendar expression; it is checked against the grammar in
   `systemd.time(7)`, not against every systemd version's parser.
6. Behaviour when the clock jumps, the timer fires during a panel upgrade, or two runs overlap beyond
   the guard described in section 1.
