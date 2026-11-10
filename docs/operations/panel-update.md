# Updating the panel

`maran update` moves the panel to a newer signed release, and `maran rollback` returns to the one
before it. Both are root-only verbs of the `maran` command, run by an operator at a terminal. The one
thing that updates the panel without an operator is the automatic security-patch update, which is
described in "Automatic security-patch updates" below, applies only a release the signed manifest calls
a security release, and can be switched off.

## When the panel contacts the release host

The panel process never contacts the release host, and no background call or call at login exists. The
release host, `releases.maran.innovayse.com`, is contacted in exactly three situations:

- The installer, when it is run online, downloads the release it installs from that host. An offline
  install (`--offline-tarball`) does not.
- The update check is the new, operator-triggered call: `maran update --check` and `maran update`
  contact the host only at the moment an operator runs them, with a plain HTTPS `GET` of the manifest
  and its signature (and, for a real update, of the three artifacts). The request carries no query
  string and no identifier of the panel, the server, the licence or the customer.
- The automatic security-patch update's timer, once a day, when it is switched on (it is on after a
  first online install). It is the same plain `GET` of the manifest and its signature, made by the same
  command, and it fetches the three artifacts only for a release it will apply. The request carries
  nothing beyond what an HTTPS `GET` implies: the server's address, the TLS handshake and the `curl`
  user agent. There is no telemetry and no server, panel or licence identifier. See "Automatic
  security-patch updates".

`maran update --offline-tarball PATH` makes no network call at all. It is the path for a server that
must not reach the internet: carry the release bundle to the server and point the command at it. It is
verified exactly as a downloaded release is (see "How a release is verified").

## The verbs

- `sudo maran update --check`: verifies the manifest and prints the installed and available versions,
  the channel, the release date, the release-notes URL and whether the available release is newer. Changes no release, state or service (see
  "What `--check` touches").
- `sudo maran update`: updates to the newest release of the channel, in the order below. Asks for
  confirmation unless `--yes`.
- `sudo maran update --offline-tarball PATH`: the same update from a bundle on disk, without contacting
  any host.
- `sudo maran rollback`: returns to the previous release (code only) and restarts the services. The
  database is not touched.
- `sudo maran rollback --restore-database`: also restores the dump taken before the update you are
  leaving, after a confirmation. Read "A database restore drops writes" first.

`sudo maran update --security-only` is the same update, applied only when the signed manifest marks the
release a security release of the installed major version (anything else is reported and left alone);
it cannot be combined with `--offline-tarball` or `--check`. `--scheduled`, which the timer passes, adds
the report to the panel and replaces the confirmation; it needs `--security-only` and takes no other
option. `sudo maran update-schedule enable|disable|status` switches the timer.

Options of `update`: `--channel stable|beta` picks the channel for this run (default: the one recorded
in `/etc/maran/release.state`, else `stable`); `--offline-tarball PATH` names a bundle; `--yes` skips
the confirmation. `--channel` and `--offline-tarball` cannot be combined, because the bundle names its
own channel. `rollback` takes `--restore-database` and `--yes`. Each option may be given once.

Exit status: 0 on success or when there is nothing to do, 1 on a failure (including an update that
failed and was rolled back), 64 on a usage error, 77 when not run as root.

## Automatic security-patch updates

An operator who never logs in leaves a known vulnerability running, so a security fix should reach the
server without waiting for someone to run `maran update`. A systemd timer does that, narrowly:

- **What runs.** `maran-update-security.timer` starts `maran-update-security.service` once a day, at
  midnight server time plus a random delay of up to four hours (so a fleet does not call at the same
  minute). The service runs `maran update --security-only --scheduled` as root. The timer ships its own
  schedule; nothing you type is written into a unit. If the server was off at the time, the run
  happens when it next boots.
- **What it applies.** A release only when its signed manifest says `"security": true` **and** its
  major version is the installed one. A feature release, a release of another major version and any
  release on a server with no versioned release (an unconverted legacy install) are reported in the
  journal and left alone, to be applied by hand with `maran update`. The `security` flag is inside the
  bytes the signature covers, so it cannot be turned on or off in transit. The flag is set when the
  release is built (`maran release build --security`), only for a release that fixes a known
  vulnerability.
- **How it applies it.** Through exactly the path of an operator's update: the shared lock, the
  newer-version check, the free-space floors, checksums against the signed manifest, the pre-update
  database dump, the migration by the new release, the restart and the health gate, and the automatic
  rollback of the code. Only the confirmation question is left out, because nobody is there to answer
  it. The automatic rollback never restores the database.
- **On by default, and told.** A first online install enables the timer and prints a line saying so and
  how to turn it off. An offline install (`--offline-tarball`) does not enable it. Re-running the
  installer and `maran update` never change your choice, so a disabled timer stays disabled across a
  reboot, an installer run and an update. The one exception: a server installed before this feature
  gets the timer enabled by the first installer run that brings the units, because for that server the
  units are new.
- **Servers that were updated, not installed.** `maran update` replaces the code only; it never
  installs or changes a systemd unit. The two units arrive with the installer (step 69), so a server
  that was moved to this release by `maran update` has no timer until the installer is run on it again
  (the installer re-run then enables the timer, because for that server the units are new).
  `maran update-schedule status` says `not-installed` on such a server.
- **Which `maran` runs.** The unit runs the installed command (`/usr/local/sbin/maran`), which an
  update does not replace, against whichever release it fetches. The command and the releases it
  fetches are kept compatible by the same rule as for an operator's update.
- **The interval is fixed.** Daily with a random delay; there is no setting for it, so that nothing
  you type reaches a unit. The switch is on, off or status.
- **Switching it.** `sudo maran update-schedule enable|disable|status`. `status` shows whether the timer
  is enabled and active, when it fires next, how the last run ended and the installed version.
- **When it fails.** A failed run rolls the code back (never the database), and is recorded in four
  places: the journal line `verb=update outcome=<rolled-back|rollback-failed|interrupted>` under the tag
  `maran-cli`; the service unit ending in the failed state; the panel's audit log
  (`PanelUpdateRolledBack`, `PanelUpdateRollbackFailed`, `PanelUpdateInterrupted`, and
  `PanelUpdateApplied` for a success); and an alert mail to the address the monitoring alerts use. A
  release that failed and was rolled back is not tried again by the timer: the next newer release, or
  you with `maran update`, ends that pause (the version is kept in `/etc/maran/security-update-failed`).
  The report to the panel is best effort: a panel that is down cannot take it, and the first two records
  then remain. A run that could not fetch the manifest changes nothing, fails the unit and sends no
  alert.
- **What it cannot protect against.** A compromised release signing key is fatal for any update, and
  unattended mode removes the person who might have noticed; a manifest that is withheld or replayed
  (the manifest has no expiry) keeps a server on its release without any failure showing; a
  legitimately signed security release that starts but misbehaves is caught only if the health gate
  notices. The dump taken before each update is kept for that case. The threat note is
  `docs/superpowers/notes/2026-11-07-security-patch-updates-threat-note.md`; the second review of this
  privileged change is outstanding. The call is the second documented exception to the no-new-outbound-call
  rule (`rules/security.md` item 10); the owner's written approval of that exception is not recorded yet.
  What the installer polygon observes is the units' text and what step 69 leaves and keeps; it boots no
  systemd, so a run of the unit under its sandbox, a reboot and the catch-up of a missed run are not
  observed there.

## Updating from the panel

An administrator can see the version state and start an update from the panel's **Settings, Panel
updates** screen, without a root shell. The screen shows the running version, the channel, the newer
release the last check found (with its date and release-notes link, when the signed manifest carries
them), the version a rollback would restore, and an **Update now** button.

What the button does, and does not do:

- It asks for nothing and sends nothing. There is no version, address or path in the request: the
  release installed is the newer signed release the recorded channel names, verified exactly as for a
  command-line update.
- It runs the same `maran update` as root, with the same dump, health gate and automatic rollback. The
  panel itself spawns nothing and holds no root: it creates a marker file in a directory that only the
  panel's service account can write (`/run/maran-update-request`), and a root-owned systemd path unit
  (`maran-update-request.path`) starts a oneshot service (`maran-update-request.service`) that runs the
  fixed command. The marker's content is never read.
- It is for administrators only: the panel refuses every other caller on both the read and the
  request. The button is advice; the endpoint is the control.
- It is refused while an update is queued or running (one update at a time; the root lock refuses a
  concurrent run too).
- Progress and the result appear in the **task list**: requested, installing, and then done, or failed
  with the plain reason: the update was refused, the health check failed and the panel returned to the
  previous version (the database was not restored; the pre-update dump is kept in
  `/var/backups/maran-update`), or the run was interrupted. A request that nothing picked up, or whose
  report never arrived, is closed by the panel after a few minutes with a reason of its own.

"The last check" is the last time this server fetched the signed manifest, whether by
`maran update --check`, by an update, by the daily security-patch timer, or by the **Check for updates**
button below. The panel itself never contacts the release host. On a server where none of those has
run yet, the screen says "Not checked yet" and offers no update button; press **Check for updates**,
run `sudo maran update --check` once, or leave the timer on.

To switch the button off, disable the path unit: `sudo systemctl disable --now maran-update-request.path`.
The screen then still shows the versions, and a press is closed by the panel as "never started".

### Checking for updates from the panel

The **Check for updates** button asks the server to look for a newer release now. It installs nothing:

- It runs the fixed command `maran update --check` as root, which takes the update lock, verifies the
  signed manifest and records the result in `/etc/maran/update-check.state`, which is what the screen
  reads. The request carries nothing; there is no channel, address or version to send.
- The mechanism is the update button's own: the panel creates a marker file in the same root-owned
  directory (`/run/maran-update-request/check-requested`) and a root-owned path unit
  (`maran-update-check.path`) starts `maran-update-check.service`, which removes the marker when the
  check has ended, whatever its outcome. The marker is what the screen shows as "Checking for
  updates"; the screen re-reads the state every two seconds while it exists.
- The screen ends the check by comparing the **last checked** time with what it was before: a new time
  means the check recorded a result; the same time means the server could not finish the check (the
  release host was unreachable, or an update held the lock), and the screen says so. The reason is in
  the unit's journal: `journalctl -u maran-update-check.service`.
- It is for administrators only, audited (`PanelUpdateCheckRequested`), and limited to ten requests an
  hour per administrator (`RateLimiting__UpdateCheckMaxRequests` and
  `RateLimiting__UpdateCheckWindowSeconds`, a 429 beyond it). It is refused while an update is queued or
  running, and while another check is.
- A marker nobody took within five minutes (the path unit is off, or the service is not installed)
  stops counting as a check in progress, and the next press replaces it.

To switch it off, disable its path unit: `sudo systemctl disable --now maran-update-check.path`. The
threat note is `docs/superpowers/notes/2026-11-09-panel-update-check-threat-note.md`; the second review of
this privileged path is outstanding.

The second review of this privileged path is outstanding
(`docs/superpowers/notes/2026-11-07-panel-update-from-panel-threat-note.md`), and the panel does not
ask an administrator to confirm their password or second factor before the update starts: no such
re-authentication exists in the panel yet.

## Channels

A release is published to one channel, `stable` or `beta`, and its signed manifest carries a `channel`
field saying which. Fetched from the network, a manifest without that field, or with a different
channel than the one requested, is refused, so a beta release cannot reach a server that asked for
stable and an old manifest cannot be replayed into either. An offline bundle keeps the channel it
declares; a bundle with no channel field is accepted offline and recorded under the channel in effect.
The channel in use is recorded in `/etc/maran/release.state`.

An update must be to a strictly newer version (SemVer 2.0 precedence, pre-releases ordered correctly).
A release that is not newer is refused with exit 1; the same version is reported as "nothing to do"
with exit 0. `maran rollback` is the only verb that goes backwards.

## Release date and notes

The signed manifest also carries `released` (the UTC build date, `YYYY-MM-DD`) and `notes_url` (an
`https://` address of the release notes). `--check` prints both as `released:` and `release notes:`
and never fetches the address. Both fields are additive: a manifest without them is still valid, and
`--check` prints `no release notes published` for it. Each value is checked before it is printed.
A date that is not a real calendar date, or a URL that is not `https://`, longer than 512 bytes, or
holds anything outside a plain ASCII set (whitespace, control characters, quotes, backslashes,
non-ASCII, and the characters a shell reads) is replaced by `not shown` with a warning on stderr,
never printed raw: the text is written by a remote party and shown on a root terminal. The signature
only proves the release key wrote the value; the check proves it is safe to display.

## The layout on the server

```
/usr/local/maran/releases/<version>/{api,agent,frontend}   one directory per release, root-owned, read-only
/usr/local/maran/current                                   symlink to releases/<version>, the release that runs
/usr/local/maran/{api,agent,frontend}                      symlinks into current/, set once
/etc/maran/release.state                                   CHANNEL, VERSION and PREVIOUS
/var/backups/maran-update/                                 pre-update database dumps, root only (0700)
```

The systemd units and the web server keep using `/usr/local/maran/api` and friends; only `current`
moves. A fresh install by the installer creates this layout. A server installed before it existed is
converted once, by its first `maran update` (or by an installer re-run): see "A server installed
before the release layout".

`release.state` holds three lines: `CHANNEL=`, `VERSION=` (the installed release) and `PREVIOUS=`
(the one `maran rollback` returns to, empty when there is none). The three newest dumps are kept in
`/var/backups/maran-update/`, named `pre-<from>-to-<to>-<UTC time>.dump`, mode 0600; "newest" is read
from the time in the name, not from the file's modification time. After a successful update the three
newest release directories (by version) are kept, plus the running one and the previous one.

Treat a dump as a secret: it holds every table, including the columns the panel encrypts, and with a
copy of `/etc/maran/panel.env` it is the whole panel.

## The order of an update

`maran update` runs these steps in this order. A refusal in steps 1 to 7 changes nothing on the live
panel; step 8 adopts a legacy layout, which changes the layout.

1. The lock. Update, rollback, `panel-backup` and `panel-restore` share one lock; a second one exits at
   once with "another panel backup, restore, update or rollback is running". The lock also creates the
   staging directory (see "What `--check` touches").
2. The interrupted-update check: `current` and `release.state` must agree (see "After a kill in the
   middle of an update"). Stale `releases/*.partial` directories are removed here.
3. The installed version is read. A host with no release layout and no legacy install is refused,
   because its version is unknown. The channel is resolved: `--channel`, else `release.state`, else
   `stable`.
4. The free-space floors, before anything is downloaded (see "Free space").
5. The manifest and its signature are fetched and verified; the channel and the version are checked.
   `--check` stops after this step and prints its report. A version that is not newer is refused.
6. The confirmation, unless `--yes` was given. With no terminal and no `--yes` the command refuses, so a
   pipe or a cron job cannot update the panel by accident.
7. The three artifacts for this architecture are downloaded and their sha256 checked against the
   signed manifest. (An offline bundle was already unpacked and checked in step 5.) The free space on
   the install filesystem is checked again.
8. On a server installed before the release layout, the legacy directories are adopted into
   `releases/` now, before the dump, so the dump can be named after the release being left.
9. The database is dumped to `/var/backups/maran-update/` (the dump directory's free space is checked
   again first). A failed or empty dump refuses the update.
10. The release is installed: each archive's sha256 is checked again against the manifest, then
    unpacked into `releases/<version>.partial` and renamed to `releases/<version>` only once complete.
    A directory with the final name always holds a verified release. A leftover directory of the same
    version from a failed update is replaced; the current and the previous release are never replaced.
11. From here a kill counts as an interrupted update. `release.state` is rewritten with `PREVIOUS` set
    to the release being left and `VERSION` unchanged. Then `current` is switched atomically.
12. The new release runs its database migrations, before any service restarts.
13. `maran-api` is restarted, then `maran-agent`.
14. The health gate waits up to 120 seconds (a fixed value, there is no option for it) for the api to
    answer on `/health/ready` on its socket, the agent socket to exist, and `/health` to report the
    agent as connected. It polls every 2 seconds.
15. If anything fails in steps 11 to 14, the code is rolled back automatically (next section).
16. Only after a healthy gate, `release.state` is written last with the new `VERSION`, the old release
    as `PREVIOUS` and the channel. Old releases and dumps are then pruned (a failed prune is a
    warning, the update still counts as done) and a line goes to the audit log.

A dump taken for an update that was refused before the switch is removed again. Migrations follow the
expand-then-contract rule (rules/architecture.md): the previous release must keep running against the
new schema. That is what makes the next section safe.

## What `--check` touches

`--check` is not free of side effects, and it is not read-only. It does not change a release, the
layout, `release.state`, the database, a service or the dump directory. It does:

- take the shared lock, so it is refused while a backup, restore, update or rollback runs;
- create the staging directory `/var/lib/maran-cli-staging` (root only, 0700) and a working directory
  in it, which is removed when the command ends;
- remove stale `releases/*.partial` directories and stale staging directories left by a killed run;
- refuse on an interrupted update (current and `release.state` disagree), on an unknown installed
  version, or on less than the free-space floor;
- make the network calls for the manifest and its signature, or, with `--offline-tarball`, copy and
  unpack the bundle into the working directory;
- write one line to the audit log.

Its output is three lines, `current:`, `available:` and `channel:`, and one sentence saying whether the
available release is newer than the installed one, equal to it, or not newer. On a server not yet
converted to the release layout, `current:` reads "unversioned legacy install". It prints no
release-notes address; the release page is where notes are published.

## When the health gate fails

If anything fails from step 11 to step 14, the command rolls back the code on its own, once: `current`
is switched back to the previous release, the agent and the api are restarted (agent first) and the
gate runs again, with the same 120 seconds. It prints the path of the dump it took and the exact
command that would restore it, and exits with status 1.

**It never restores the database by itself.** A restore discards every write since the dump, and a
failed health check can be a transient. Deciding to lose data is yours. If the gate still fails after
the code rollback, the command stops with a "did not become healthy either" message and does nothing
more: no retry, no second attempt. The audit log records `rolled-back` or `rollback-failed`.

The release that failed stays on disk. The next `maran update` to that version replaces it.

## After a kill in the middle of an update

If the terminal closed, the power failed or the process was killed after the switch, `current` may
point at the new release while `release.state` still names the old one. The next `maran update`
notices that disagreement and refuses. It prints both versions, the directory that holds the dumps
(`/var/backups/maran-update`) and the command to run:

```
sudo maran rollback
```

It does not guess which side is right. A process that was killed after the switch also prints that
instruction, and the dump's path, as it ends. A kill before the switch leaves the old release running
and only stale staging directories, which the next run removes. A signal never starts a rollback by
itself. Do not reboot into a half-finished update if you can avoid it: a reboot starts whatever
`current` points at.

## Rolling back

`maran rollback` recognises three situations from `release.state` and `current`:

- **Switch**: `PREVIOUS` names another release than the running one. `current` is pointed at it, the
  agent and then the api are restarted, and the same 120-second gate runs. No migration runs; the
  previous release is built to run against the newer schema.
- **Finish**: `PREVIOUS` is already running but `VERSION` still names the release that was left, which
  means an earlier rollback was killed between the switch and the state write. The state write is
  completed and the run continues as above.
- **Restore only**: `PREVIOUS`, `VERSION` and `current` all agree, which is the state after `maran
  update` rolled back by itself. There is nothing to switch, so the command refuses unless
  `--restore-database` is given; with it, only the database is put back, from the dump taken for the
  newest release on disk above the running one (the update that failed).

It refuses when no previous release is recorded, or when the previous release is missing or
incomplete on disk. After a manual rollback `PREVIOUS` is written empty, on purpose: a second
`maran rollback` is refused, so two rollbacks in a row cannot swap the releases back and forth. Going
forward again is an update, which verifies the release again and replaces the copy still on disk.

With `--restore-database` the command also: finds the newest dump taken for the update to the release
being left (it must be a regular file owned by root, mode 0600, in a root-only 0700 directory), shows
the warning, and asks for `--yes` or the typed word `restore-database` on a terminal (no terminal and
no `--yes` is a refusal); stops the api and then the agent; restores the dump, which is a single
transaction, so a failed restore leaves the database unchanged; and then continues as above. If the
run ends before its own restart, the two services are started again.

## A server installed before the release layout

A server whose `/usr/local/maran/api`, `agent` and `frontend` are still real directories is a legacy
host. Its first `maran update` adopts it, before the dump:

- the three directories are moved into `releases/legacy-<UTC time>` (or into `releases/<version>` when
  `release.state` already names a version). A move inside one filesystem keeps running processes on
  their files; another filesystem is refused. The symlinks are created, and the units and the web
  server are untouched;
- the adopted release is the "previous" one: the dump is named `pre-legacy-<time>-to-<version>-<time>.dump`,
  and `maran rollback` can return to it like to any other release;
- a legacy release counts as older than every published version, so any published release is an
  update from it, and `--check` reports it as "unversioned legacy install";
- an adoption that was killed half way is resumed by the next run.

A host with neither a layout nor a legacy install, or whose `current` cannot be resolved, is refused
rather than guessed at.

## Free space

The manifest carries no sizes, so the command uses a fixed floor: 2 GiB free on each of the staging
base (`/var/lib/maran-cli-staging`), `/usr/local/maran` and `/var/backups/maran-update` (its nearest
existing parent when the directory does not exist yet). All three are checked before anything is
downloaded; `/usr/local/maran` is checked again after the download and the dump directory again just
before the dump. These are the same fixed figure each time, not the real size of the release. A full
disk is refused with nothing switched.

Downloads are capped so they cannot fill the disk past that floor: the manifest and its signature at
60 seconds and 1 MiB each, each artifact at 1800 seconds, and the three artifacts together at 2 GiB
(each one at most 1 GiB). An offline bundle larger than 2 GiB is refused.

## How a release is verified

The manifest is signed with ECDSA P-384 over SHA-384. The server holds the public key at
`/usr/local/maran/cli/keys/release-signing.pub` (root-owned, in a root-owned directory), installed by
the installer. There is no setting or option that points `maran update` at another key or another
host; the host and the TLS rules (HTTPS only, including redirects, at most 3 of them, TLS 1.2 or
newer) are constants.

The signature covers the whole manifest, so the version, the channel and every download address and
checksum are authentic before any of them is read. Every artifact address must sit directly under the
channel's path on the release host. Each artifact's sha256 comes from that manifest and is checked
after the download and again, inside the install step, before unpacking. A mismatch at any point
refuses the update with nothing changed.

Archives are vetted before they are extracted: a member with an absolute path, a `..` component, a
backslash in its name, a hard link, a device node, a FIFO or any symlink is refused (a release tree
holds only regular files and directories). Extraction restores no owner and no permissions from the
archive; the tree is then made root-owned with group and world write and set-id bits removed. The tree
is never unpacked into the live one.

### Offline bundles

`--offline-tarball PATH` is accepted only when all of these hold:

- the path is absolute and made of letters, digits, `.`, `_`, `-` and `/`, with no `..`;
- the file is a regular file, not a symlink, owned by root, and not writable by group or others;
- the directory that holds it is owned by root and is not writable by group or others. `/tmp` is
  refused for that reason; put the file in `/root` instead;
- it is not larger than 2 GiB.

The file is copied once into a root-only working directory and only the copy is read from then on.
The bundle's manifest signature and each archive's sha256 are verified exactly as for a download.

## A database restore drops writes

`maran rollback --restore-database` restores the dump from before the update. Everything written
after the dump is gone, and so is every table or schema a newer release added: new accounts, sites, schedules, password changes, audit entries, and the
messages waiting in the queue. It asks for `--yes` or a typed confirmation and says so. Use it only
when the new release changed data in a way the old release cannot read, and take a
`maran panel-backup` of the current state first if you might need anything in it. A plain
`maran rollback` never touches the database.

## Limits

These are the honest limits of this change.

- The network path of `maran update` and of the online installer has not been exercised against the
  real release host. Treat the first real network update as untested and have the offline path ready.
- The scheduled panel backup is not stopped during an update: its timer keeps running, and a run that
  fires meanwhile is refused by the shared lock. Nothing in this change was verified about when that
  backup is tried again.
- Re-running the installer on the same version keeps the tree already on disk when it is intact, and
  repairs it from the verified archives when it is not. The installer refuses a lower version than the
  one installed, so it cannot be used to downgrade; `maran rollback` is the way back.
- The panel's update button (see "Updating from the panel") starts the same root command through a
  path unit; it has not been run on a booted host, and it asks for no password or second factor.
- There are no automatic security patches yet. Every update is run by an operator, and the update check
  happens only when an operator runs it.
- A release that needs a new systemd unit directive cannot be delivered by `maran update`; the units
  are installed by the installer, not by this verb. The same goes for the `maran` command itself and
  the signing key.
- A release that deliberately breaks the expand-then-contract rule says so in its notes; rolling back
  across it needs the database restore.
- `maran rollback --restore-database` returns the database to the dump's state even when the update
  added schemas or tables: before the dump is replayed, every schema except `public` and the system
  ones is dropped in the same transaction, so what the newer release created is removed with the rest.
  Objects a newer release put in `public` are not removed; the panel creates none there.
- The manifest has no expiry. A host that serves an older, still valid manifest can keep a server on
  an old release; the check stops a downgrade, not a stale upgrade. Compare against the release page.
- This change has not yet had its second security review: it is outstanding, and the change must not
  merge to `main` until a reviewer has read it (see the threat note,
  `docs/superpowers/notes/2026-11-04-panel-self-update-threat-note.md`). The operator-triggered call to
  the release host also still needs the owner's recorded decision (rules/security.md item 10).
