# Threat note: starting the panel update from the panel (issue #382)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps, to session-gated authority and to how a signed release reaches a root
process. This note covers the "Update now" button of the panel's own settings screen: an
administrator's web session asks for `maran update` to run. It is written BEFORE the code: every
statement below is a requirement the code must meet, not a description of code that exists. Where the
code lands and differs, the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (rules/security.md), the branch must not merge to main.** The work is
carried out by agent sessions, which cannot obtain a second reviewer. The branch MUST NOT merge to
`main` until a human reviewer has read it (rules/security.md "Sensitive change escalation"). The owner
is told at hand-off that this surface carries an outstanding review. This note builds on
`2026-11-04-panel-self-update-threat-note.md` (the verbs, the layout, the rollback) and
`2026-11-07-security-patch-updates-threat-note.md` (the unattended root unit); both carry their own
review state, OUTSTANDING, and nothing here discharges either.

**Privileged path: option 1 of the issue (a root-owned systemd `.path` unit), applied as the plan's
recommended answer to decision D8 (`research_notes/plan-8.md`).** The issue says the owner chooses; the
lead's brief for this change names the recommended option as the one to code. The owner's written
confirmation of D8 is NOT RECORDED here and is a second reason this branch does not merge.

**Reading of rules/security.md item 10: NOT RECORDED.** Item 10's one documented exception is "the root
`maran` CLI may fetch a signed release manifest ... when an operator runs `maran update`". A press of
the button makes the root CLI fetch it on an administrator's say-so, with no terminal. This note reads
that as the same exception (operator-triggered, root CLI, never the API or the agent, no identifier
sent, `--offline-tarball` remains the call-free path) and the API itself makes NO outbound call. That
reading is the owner's to confirm; until then it is an assumption this note makes, not a fact.

## What is added

1. **A request directory `/run/maran-update-request/`**, created by `systemd-tmpfiles` from
   `installer/systemd/maran-update-request.tmpfiles.conf`, `root:<service group>` mode `0770`. `/run` is
   a tmpfs, so a request never survives a reboot (a stale marker must not start an update at boot). The
   directory is root-owned, so the panel's service account can create and remove entries in it but
   cannot rename, replace or symlink the directory itself.
2. **A request marker `/run/maran-update-request/requested`.** The API creates it with
   `FileMode.CreateNew` (an existing marker is a concurrent request and is refused). Its content is the
   fixed token `update` and a newline, and **root never reads it**: the marker's existence is the whole
   message. It carries no version, address, path, channel or flag, and the API holds none of those to
   send.
3. **Two root units**, `maran-update-request.path` and `maran-update-request.service`, installed
   `root:root 0644` by `installer/lib/69-update-request.sh`. The path unit is `PathExists=` on the
   marker and starts the service. The service is a oneshot run as root with ONE fixed command,
   `/usr/local/sbin/maran update --requested`, one pre-command that deletes the marker
   (`rm -f -- <marker>`; `rm` does not follow a final symbolic link), and the same sandbox as
   `maran-update-security.service`. There is no `EnvironmentFile`, no shell, no template argument.
4. **One new flag of the existing verb, `maran update --requested`.** It stands in for the
   confirmation nobody is there to give, exactly as `--scheduled` does, and takes nothing else
   (`--check`, `--yes`, `--channel`, `--offline-tarball`, `--security-only`, `--scheduled` are refused
   with it). Every step of the update is the operator's: lock, interrupted-update check, signed
   manifest, version decision, floors, artifact sha256, pre-update dump, unpack, switch, migrate,
   restart, health gate, automatic rollback that never restores the database. `cli_update` keeps one
   body; the flag adds a report to the panel and nothing before the download.
5. **Three root-owned facts the panel reads, never writes** (all `root:root 0644` in `/etc/maran`,
   which is `root:<service group> 0750`): `release.state` (existing: `CHANNEL`, `VERSION`, `PREVIOUS`),
   `update-check.state` (new: `CHECKED_AT`, `AVAILABLE`, `RELEASED`, `NOTES_URL`, written
   from the SIGNED manifest of every network manifest fetch of the panel's own channel that the root CLI
   makes, atomically; a `--check --channel` for another channel is not recorded, so a stable panel is
   never shown a beta as available), and
   `update-running.state` (new: `STARTED_AT`, present while an update verb holds the lock, removed in
   its exit trap). The panel treats all three as untrusted text (below).
6. **The panel side** (module `Maran.Modules.Updates`, administrator only): `GET /api/v1/panel-update`
   reads those facts; `POST /api/v1/panel-update` opens a task, writes a ledger line (the task id) to
   the service account's own `/var/lib/maran/panel-update-task`, journals the request, creates the
   marker and answers 202 with the task id. It spawns nothing, holds no root and makes no outbound call.
7. **The report back**: at the end of a requested run (and once when the install begins) the verb runs
   `Maran.Host --cli update-request-report <word> <from> [<to>]` as the service account, the way
   `report-update` is run for the timer. It closes or advances the task whose id is in the ledger,
   journals the outcome and removes the ledger. The words are the closed set `installing`, `applied`,
   `unchanged`, `refused`, `rolled-back`, `rollback-failed`, `interrupted`; release names are
   re-validated as plain names; nothing else is accepted.

Not added: no daemon, no listening port, no RPC, no proto change, no change to the agent, no package, no
outbound call from the API or the agent. A new SYSTEMD UNIT PAIR is added, which is installer surface
and which item 10's "no new daemons" line must be read against; the owner confirms it in D8.

## What an attacker could do with each surface, and why it is safe

### 1. A hijacked administrator session (the sharpest surface)

An attacker holding an administrator's access token can press the button. What they get is **not a
choice of code**: the request carries nothing, and the root side installs only a release that (a) the
signed manifest names, (b) has a strictly greater version than the installed one, (c) is on the
recorded channel, (d) matches the signed sha256 of every artifact. They can therefore cause an update
to the newest legitimate release at a moment of their choosing, and the restart that goes with it
(a short outage; the health gate and the automatic rollback bound it). They cannot choose a version,
a source, a downgrade or a path. That is the issue's own bound ("bounded only by the signed-release
check"), and it is why the button asks for nothing.

Required controls and their state:

- **Administrator policy** (`AuthorizationPolicies.AdminOnly`) on the class: GET and POST. A customer,
  a reseller and an API key are refused (403, a documented exception to the 404 rule because there is
  no identifier to hide, as for the licence status). The SPA's hiding of the button is advice only.
- **CSRF**: the panel's `CsrfHeaderMiddleware` already refuses a cookie-bearing `POST` without the
  `X-Maran-Request` header, and the access token is a bearer header. No new mechanism.
- **Rate limit**: the `api` policy, plus the concurrency refusal below.
- **Audit**: one journal entry per request, written BEFORE the marker is created, so a started update
  can never be unaudited; the entry carries the actor, address and agent and the fixed subject
  `request=update`; a refusal is journalled as a failed entry.
- **Concurrent requests**: refused with `PanelUpdateRunning` (409) when the marker exists and is
  younger than five minutes, or `update-running.state` is younger than four hours. The marker is
  created with `CreateNew`, so two simultaneous requests cannot both succeed. The root side
  independently refuses a second run through the panel archive lock (`flock`), which is the control
  that does not depend on the panel being honest.
- **Step-up re-authentication: NOT PRESENT.** The issue requires it "if an existing step-up exists".
  None exists in this panel: no password-confirmation, recent-authentication or second-factor
  challenge exists for any action, and the SPA's button sends no body to carry one. Building one is a
  separate feature (it needs a challenge in Identity and a SPA dialog). **This is a gap, stated, not
  closed**: a hijacked session reaches the button with no extra proof. It is reported to the owner as
  a decision (Identity step-up) and is the main residue of this note.

### 2. A compromised panel process (the service account) aims at root

The panel account can write the request directory and the ledger. What it can do to root:

- **Create the marker at will.** That starts `maran update --requested`, which does only what an
  administrator's press does. It can do this repeatedly, bounded by the service's start limit and
  by the root lock; each run is a no-op when the installed release is already the newest. A process
  that can already stop the panel and read its secrets gains an update trigger, nothing more.
- **Make the marker a symbolic link, a directory, a FIFO or a huge file.** Root only tests its
  existence (systemd's inotify watch) and `rm -f --`s it: `rm` unlinks a link without following it and
  never opens the file, so a link to `/etc/shadow` is removed, not read or truncated. A directory
  named `requested` makes the `rm` fail, the service fails, and the service's start limit (three starts in a minute) stops the
  retrigger loop (a self-inflicted denial of updates by an account that can already stop the panel).
  Root never reads the content, so the content is irrelevant.
- **Replace `/run/maran-update-request` with a link** to aim root's `rm` elsewhere: it cannot. The
  directory is root-owned inside root-owned `/run`; the service account has write on its contents only.
  (Contrast `/var/lib/maran`, which the account owns outright: the marker is deliberately NOT there,
  because a root `rm` through an account-owned parent can be steered by renaming that parent's children.)
- **Forge the three state files.** It cannot write `/etc/maran` (root-owned, group read-only), so
  `release.state`, `update-check.state` and `update-running.state` are root's word. The ledger is the
  account's own file and only names a task id it already can create.
- **Feed the panel's own parsers.** The API treats root's files as untrusted: strict `KEY=value`
  line parsing, a version must be a SemVer string, a timestamp an ISO-8601 instant, the notes URL must
  be a plain `https://` address of at most 2048 characters without whitespace or control characters,
  every other line is ignored, and a file larger than 4 KiB is refused whole. A value that fails its
  check is reported as absent, never echoed.

### 3. A network attacker, or a compromised release host

Unchanged by this note: the root CLI's checks (signature before fields, channel, strictly greater
version, sha256 twice, strict version grammar, TLS with no override) are the ones in
`2026-11-04-panel-self-update-threat-note.md`. The button adds one thing: the manifest fetch can now
be caused by an administrator's session. The request sends nothing identifying beyond what an HTTPS GET
of the manifest implies. **The release text reaches the screen**: the notes URL and release date come
from the signed manifest and are re-validated by the CLI before they are written to
`update-check.state` and again by the API before they are sent. The SPA links an address only when it
starts with `https://`. A freeze or withhold of the manifest (no expiry) is unchanged and accepted.

### 4. The report channel from root to the panel

`update-request-report` runs as the service account with fixed words and release names that passed
`cli_version_is_semver`. It can only advance or close the task named by the ledger and add a journal
entry. A compromised root already owns the box; a compromised panel can run the verb itself, which
lets it mark its own task finished, a statement of no consequence beyond the task list.

### 5. A task that outlives the process that opened it

The update restarts `maran-api`, so the process that opened the task is not the process that sees it
end. Two consequences are handled and tested:

- `StartupTaskReconciler` closes every task still `Running` from before the process start; it now
  leaves the `PanelUpdate` kind alone, because that task is MEANT to outlive a restart.
- A task whose report never arrives (the root unit never started, the report command failed, the
  machine rebooted) is closed by the Updates module's reaper: marker older than five minutes and no
  running state means "never started" (and the stale marker is removed); no marker, no running state
  and a ledger older than two minutes means "outcome unknown". A task is never left running for ever.
- The reaper's "outcome unknown" reading would also fit the seconds between a run taking its running
  state back and its report arriving (no marker, no running state, a ledger minutes old). The exit
  trap in `update.sh` therefore sends the end report BEFORE it takes the running state back, so that
  window does not exist. The opposite window at the start (marker removed by `ExecStartPre`, running
  state not yet published) is closed by the ledger being seconds old then.

What this does NOT defend against, stated plainly:

- **No step-up** (above). A hijacked administrator session can start an update.
- **A compromised signing key** is fatal, as for every update path. The button adds an on-demand trigger
  for it, not a new capability.
- **A legitimate release that starts and misbehaves**: the health gate and rollback catch a panel that
  does not start, not one that runs wrongly. The dump is kept.
- **A freeze of the manifest** (above).
- **Availability**: the service account can make the request directory unusable for itself and can
  trigger updates; it can already stop the panel.

## What a reviewer must check

1. `installer/systemd/maran-update-request.service`: one `ExecStart`, one `ExecStartPre` (the `rm`),
   no shell, no `EnvironmentFile`, the sandbox of the security unit, no `[Install]`. The polygon
   assertion `parts/update-request-units.sh` holds this as code, with planted breaks and accepting
   controls.
2. `maran-update-request.path`: `PathExists=` on exactly the marker, `Unit=` the service, and nothing that runs at boot by itself.
3. `cli_update_parse`: `--requested` takes no value and refuses every other flag; `cli_update` has one
   body and the flag only adds the report.
4. That the marker is never read and that no root code path touches `/var/lib/maran`.
5. That the API's parsers for the three state files are strict (a hostile file is "absent", not an
   exception, not an echo) and that the response carries no path.
6. Item 10 and D8: that the owner has answered both.

## What the author could not verify

- A run of the units under a real systemd on both families: the polygon image boots no systemd, so
  "the path unit starts the service" and "the sandbox leaves the verb what it needs" (runuser, pg_dump,
  `systemctl` from inside the unit, SELinux on AlmaLinux) are read from text, not observed.
- `systemd-analyze security` score of the new service: set to the security unit's ceiling by analogy;
  `maran units` re-measures it.
- That `PathExists=` fires reliably for a file created in a directory on `/run` by another uid on
  both families (inotify on tmpfs is documented to work; not run here).
- The behaviour of the service's `StartLimit` when the path unit retriggers a failing service (the limit is on the service, not the path unit, because the path unit's `TriggerLimit` directives need systemd 253 and Ubuntu 22.04 ships 249).
