# Threat note: automatic security-patch updates, `maran update --security-only` on a timer (issue #383)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
installer's privileged steps and to how a signed release reaches a root process. This note covers the
unattended security-patch update: a root-owned systemd timer that runs `maran update` for security
releases only, the signed `security` flag that decides what it may apply, the verbs that switch it,
and the way a failed unattended run reaches the operator. It is written BEFORE the code: every
statement below is a requirement the code must meet, not a description of code that exists. Where the
code lands and differs, the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (rules/security.md), the branch must not merge to main.** The work is
carried out by agent sessions, which cannot obtain a second reviewer. The branch MUST NOT merge to
`main` until a human reviewer has read it (rules/security.md "Sensitive change escalation"). The owner
is told at hand-off that this surface carries an outstanding review. This note builds on
`2026-11-04-panel-self-update-threat-note.md` (the verbs, the layout, the rollback; review state
OUTSTANDING there too) and `2026-09-19-release-bundle-signing-threat-note.md`; nothing here discharges
either.

**Owner approval of the rules/security.md item 10 change: NOT RECORDED.** A timer-driven outbound call
is what item 10 forbids without a spec change first, and the documented phone-home is only the licence
lease. The spec change request is issue #384. Until the owner approves it in writing and that approval
is linked here, this branch is a second reason not to merge. Writing the code on a branch is not the
approval, and this note does not grant it.

## What is added

1. **A signed `security` flag in the release manifest.** `scripts/lib/release-bundle.sh build` gains
   `--security`; the manifest then carries a top-level `"security": true`, and a release without the
   flag carries `"security": false`. It is inside the bytes the ECDSA P-384 / SHA-384 signature covers,
   like `version` and `channel`. The CLI reads it only out of a manifest whose signature verified
   (`cli_release_manifest_field` after `cli_release_verify_manifest`). Absent, `false`, or any other
   spelling means "not a security release" (fail closed).
2. **Two flags on the existing verb** `maran update`:
   - `--security-only`: after the normal manifest verification and version decision, apply the update
     only if the signed manifest says `security: true` AND the new version has the same major number as
     the installed one. Any other release is reported (one line to the terminal, one to the journal)
     and left alone, exit 0. Refused with `--offline-tarball` (an unattended mode has no operator to
     carry a bundle) and on a legacy, unversioned install (no major number to compare).
   - `--scheduled`: what the unit passes. Implies no confirmation (there is no terminal), requires
     `--security-only`, refuses `--check`, `--yes`, `--channel`, `--offline-tarball`, so the unit's
     argv is fixed. It adds one thing: after the run ends in a state the operator should hear about, the
     outcome is reported to the panel (see 5).
3. **A timer and a oneshot service**, `maran-update-security.timer` and `.service`, in
   `installer/systemd/`, installed root:root 0644 by a new installer step `69-update-schedule.sh`
   (the same shape as `maran-panel-backup.timer`). The service runs `/usr/local/sbin/maran update
   --security-only --scheduled` as root. The timer ships its own `OnCalendar=daily` and
   `RandomizedDelaySec=4h` and `Persistent=true`: a documented default interval, with a randomized
   delay so a fleet does not call at the same minute. There is NO operator-supplied value written into
   a unit (unlike the backup schedule), so there is no drop-in and nothing to validate against item 4
   of the checklist.
4. **A verb to switch it**: `maran update-schedule enable|disable|status`
   (`installer/cli/lib/update-schedule.sh`). `enable` and `disable` are `systemctl enable --now` /
   `disable --now` of the one timer and an audit line; `status` reads systemd. They write no file.
5. **A way for a failed unattended run to be seen**: a new closed verb of the panel binary,
   `Maran.Host --cli report-update <outcome> <from> <to>`, run as the service account by the wrapper
   (`cli_dotnet`), which writes one audit-journal entry through the Monitoring module's journal and,
   for the three failure outcomes (`rolled-back`, `rollback-failed`, `interrupted`), asks for an
   operator alert mail through `OperatorAlertMailer` (the one path monitoring and backup alerts take).
   A successful run (`applied`) is journalled and mails nothing.

Not added: no daemon, no listening port, no new RPC, no proto change, no change to the agent, no new
package. The panel process never contacts the release host; the timer's service does, as a root CLI
run, exactly as an operator's `maran update` does.

## What an attacker could do with each surface, and why it is safe

### 1. A network attacker, or a compromised release host, aims at the unattended run

Unattended mode removes the operator who might notice a strange update, so the question is what an
attacker who controls every byte the host serves can make this root job do.

- Everything the job acts on comes from the SAME verified manifest an operator's update uses: signature
  first (`cli_release_verify_manifest`), then fields. The `security` flag is one of those fields. A
  network attacker cannot turn a feature release into an unattended one, nor the reverse, because
  editing the flag breaks the signature. The release pipeline's selftest holds this: a `security` flag
  flipped after signing must be refused by signature verification, and the inverse control (an
  untouched signed bundle) must be accepted.
- The version must be strictly greater than the installed one (the existing `cli_version_gt` check), so
  a replayed older signed manifest is a no-op; the channel is signed and must equal the one in
  `release.state`, so a beta manifest cannot reach a stable server. `--security-only` adds the
  same-major condition, which is also computed from the signed `version` only.
- The job runs through the SAME path as an operator update: lock, interrupted-update check, version
  decision, free-space floors, artifact sha256 against the signed manifest, pre-update dump, unpack,
  switch, migrate with the new binary, restart, health gate, automatic rollback that never runs
  `pg_restore`. `--scheduled` skips exactly one step, the interactive confirmation, because the
  confirmation exists for an operator at a terminal and a refusal to run without one is what makes the
  unit unable to run without the flags. No other step is skipped or weakened. A reviewer checks that
  `cli_update` has one body and the new flags only add a gate before the download and a report after.
- The request is a plain HTTPS GET of the manifest and signature (and, only when the release is
  applicable, the three artifacts): `cli_release_download` is unchanged, `--proto =https`, TLS 1.2+,
  certificate checks on, no query string, no header added. **The check sends nothing identifying
  beyond what an HTTPS GET of the manifest implies: the server's IP address, the TLS handshake and the
  `curl` user agent. No telemetry, no server id, no panel id, no licence, no customer.** This sentence
  is repeated in the operator documentation.

What this does NOT defend against, stated plainly:

- **A compromised release signing key is fatal, and unattended mode makes it faster.** Whoever holds
  the key can sign a manifest with `security: true`, a higher version and any artifacts, and every
  server with the timer enabled applies it at its next check with no human in the loop, then the health
  gate decides only whether the panel answers, not whether the code is honest. For a manual update the
  same key was already fatal, but an operator could notice something odd before typing the command;
  here nobody is asked. The key-handling note in `2026-09-19-release-bundle-signing-threat-note.md`
  must be reread by the reviewer in that light: the key's custody, its offline use and its rotation
  procedure are now the only thing standing between a leaked key and a fleet-wide root compromise on a
  schedule. The `RandomizedDelaySec` spreads the damage over hours; it is not a mitigation.
- **A freeze or withhold of the manifest.** The manifest carries no expiry. A host that keeps serving
  an older valid manifest keeps every server on a known-vulnerable release and the operator sees no
  failure. This residue is the same as for a manual update and it applies here too: an unattended
  mechanism that silently stops delivering looks exactly like one with nothing to deliver. It is
  accepted; `maran update-schedule status` shows the last result and the installed version so an
  operator can compare, and a manifest expiry is a separate release-pipeline decision.
- **A legitimate security release that breaks the panel.** The health gate and the rollback catch a
  panel that does not start; they do not catch a panel that starts and misbehaves. The pre-update dump
  is kept, and the failure paths below tell the operator.

### 2. What the `security` flag may and may not authorise

- The flag authorises ONE thing: that an update to this version may be applied without an operator.
  It never changes which artifacts are fetched, which channel is used, or any check that follows.
- **Major versions are not crossed unattended.** A security release whose major number differs from the
  installed one is reported and left alone. Reasoning: a major version is where the project allows
  breaking changes (rules/architecture.md "additive evolution inside a major version"), and an
  unattended jump is where a breaking change would land with nobody to read the release notes. Minor
  and patch releases, including pre-releases of the same major on the beta channel, are in scope. The
  comparison is on the SemVer major of two validated version strings (`cli_version_is_semver`); a
  legacy `legacy-<timestamp>` name has no major and is refused for unattended updates.
- The flag is a boolean the BUILD sets (`--security`); nothing derives it from the content of the
  release. Who may pass `--security` is a release-process question: the flag is as trustworthy as the
  person signing. The release documentation says it is set only for a release that fixes a known
  vulnerability, and a wrongly set flag is a feature release delivered unattended, which the health
  gate and the rollback bound but do not prevent. Author could not verify the release process's own
  discipline here.

### 3. The timer and service: a new unattended root job

- Installed by a root installer step from files in the installer package, root:root 0644, with no
  placeholder and no value taken from the environment or the command line. `ExecStart` is the fixed
  argv `/usr/local/sbin/maran update --security-only --scheduled`.
- The service is a hardened oneshot: `NoNewPrivileges`, `ProtectSystem=strict` with a short
  `ReadWritePaths` list (`/usr/local/maran`, `/etc/maran`, `/var/backups/maran-update`,
  `/var/lib/maran-cli-staging`, `/run`), `ProtectHome`, `PrivateTmp`, the kernel/clock/hostname
  protections, `RestrictAddressFamilies` limited to what the release download and `systemctl` need,
  a `TimeoutStartSec` that bounds a hung run, `StartLimitIntervalSec=0` like the backup unit. Its
  exposure score has a ceiling in `installer/systemd/unit-security-ceilings.txt`, enforced by
  `maran units`. What the hardening cannot do: the job must write the code root runs, so a flaw in
  `maran update` is a flaw of root in a service sandbox that was opened wide enough to do the update.
- **Off by default, and the default is told (owner decision O-3, issue #550).** The installer installs the
  two units and runs `daemon-reload`, and never enables the timer, online or offline: an unattended root job
  that changes software on a customer's host is the operator's decision. The terminal gets one line
  saying the updates are installed but off and naming `sudo maran update-schedule enable`. A re-run of the
  installer, and `maran update`, never touch the enablement, so an operator's `enable` or `disable`
  survives a reboot (the `systemctl enable|disable` link is persistent), an installer re-run and a panel
  update. `maran update-schedule status` says `installed, off` for a timer never enabled.
- `maran update-schedule enable` refuses when the units are not installed (an older installer) and
  never edits a unit; `disable` stops and disables and writes nothing else. Both write an audit line
  (`verb=update-schedule outcome=enabled|disabled`) through `cli_audit_log`.
- Concurrency: the update, rollback and panel-backup verbs share one lock, so the timer cannot
  overlap an operator's `maran update` or the backup; a second run exits at once with the existing
  message, and the unit reports it as a failed run. The backup timer and this one are not ordered; a
  collision is a lost day for one of them, caught by `Persistent=true` at the next trigger.

### 4. Failure handling: automatic rollback, and why it is seen

- A failed activation rolls the code back through the existing path, which NEVER runs `pg_restore`: a
  restore discards every write since the dump, and no unattended process may decide to discard data.
  The operator is told the dump's path and the explicit command (`maran rollback --restore-database`),
  as for a manual run.
- **Four places the failure is recorded, because a journal nobody reads is not a record.**
  1. the journal line `verb=update outcome=<rolled-back|rollback-failed|interrupted|refused>` the verb
     already writes (`cli_audit_log`, tag `maran-cli`);
  2. the service unit ends FAILED (exit 1), which `systemctl --failed` and `maran update-schedule
     status` show;
  3. the panel's audit journal, through `Maran.Host --cli report-update`, as `PanelUpdateRolledBack`,
     `PanelUpdateRollbackFailed` or `PanelUpdateInterrupted` (and `PanelUpdateApplied` for a success),
     visible to an administrator in the audit log;
  4. an operator alert mail for the three failures, to the address monitoring alerts already use. With
     no alert address configured the entry is journalled as `MailSkippedNoSmtp`, succeeded, like every
     other operator alert.
- **The report is best effort and must say so.** It runs after a rollback, when the panel may be
  unhealthy or may be the OLD release, which cannot know the verb. A report that cannot be delivered is
  one journal line (`outcome=report-failed`) and never changes the update's own exit status or hides
  the failure: items 1 and 2 remain. This is a real gap: a panel that is down cannot mail, and the
  server's own mail setup is the panel's SMTP configuration. Stated as accepted residue; the author could
  not test the report against a panel in the `rollback-failed` state.
- The report carries a fixed outcome word and two release names that passed `cli_version_is_release_name`
  in the shell and an allow-list shape in the verb. It never carries a dump path, a manifest, a URL, a
  tool's output or an error text (rules/security.md item 8).
- **A manifest that cannot be fetched** (network down, host down) is not a failed update: nothing
  changed. The run exits 1 with `outcome=refused` in the journal and the unit shows failed, and NO panel
  alert is raised, so a transient outage does not mail an administrator daily. Persistent failure to
  check is visible as a failed unit and a stale `status`, not as an alert; that is a deliberate trade.

### 5. Argument and file injection (checklist items 3 and 4)

- The verb set stays fixed; typed values are matched against fixed words. `--scheduled` and
  `--security-only` take no value. `update-schedule` takes exactly one of three words.
- The only values passed from the shell to the panel binary are the outcome word and two release names;
  they are passed as separate argv words after `--`, never through a shell string. The verb re-validates
  them (rules/security.md item 1): the outcome against the closed enum, each release name against
  `[A-Za-z0-9._-]{1,64}`, anything else refused with a usage status.
- No value is written into any line-oriented file or unit by this change.

### 6. Dependencies, ports, secrets

- No dependency is added. No listening port. No secret is read or written: the release public key is
  the one `cli_release_verify_manifest` already uses; the report passes no credential.

## What a reviewer must check

1. `cli_release_manifest_is_security` returns success only for a verified manifest whose top-level
   `security` is exactly `true`; run it against a manifest with the flag absent, `"true"`, `1`, and
   nested inside an artifact block (it must fail all four).
2. The `security` flag is inside the signed bytes: `scripts/lib/release-bundle.sh selftest` flips it
   after signing and the verification must be the thing that refuses; the inverse control accepts.
3. `cli_update`: `--security-only` decides AFTER signature, channel and version-greater checks and
   BEFORE `cli_update_require_update_space`, `cli_update_confirm` and any artifact download, and the
   skipped case downloads nothing. `grep -n "security_only\|SECURITY_ONLY" installer/cli/lib/update.sh`.
4. No other step is skipped in `--scheduled` mode: `grep -n "CLI_UPDATE_SCHEDULED" installer/cli/lib/*.sh`
   shows the confirmation, the report, and nothing in the dump, switch, migrate, restart, gate or
   rollback functions.
5. The unit's `ExecStart` and the timer's `OnCalendar` are literals; `ReadWritePaths` names no path the
   unprivileged service account can write (`/var/lib/maran` is NOT in it).
6. The installer never enables the timer, on any install, and never changes its enablement on a re-run.
7. `report-update` re-validates its arguments and writes through `MonitoringAuditJournal`; a failure
   outcome mails through `OperatorAlertMailer` only.
8. The key-handling statements in the release-bundle signing note and `docs/operations/release-process.md`
   still hold with a timer in front of them.

## What the author could not verify

- **A real end to end run.** No systemd, no installed panel and no release host were available to this
  session: the timer firing, `Persistent=true` catch-up, `RandomizedDelaySec`, the unit's sandbox with
  the actual `systemctl restart` and `runuser` calls, and the unit's behaviour on both distribution
  families are UNOBSERVED. The polygon covers the installer step and the verb offline; the first
  real unattended run must be watched.
- **The unit's `ReadWritePaths` list is sufficient** for every path `maran update` writes on both
  families. It was derived by reading the libraries, not by a run under `ProtectSystem=strict`.
- **The report against a broken panel**, and against the old release's binary that predates the verb.
- **The release process's use of `--security`**, which is a human decision no code here can check.
- That `systemctl disable` of a timer survives an installer re-run on every supported distribution; the
  design relies on the unit file already existing, which is true of the code, not measured.
- Item 10: the spec change (#384) and the owner's approval are outside this branch.

## Honest ordering

Written before the code. The only things this note decides that a review could still change are the
major-version rule (proposed: no unattended major), the default interval (daily with a four-hour
random delay) and the best-effort nature of the report; each is stated so a reviewer can overrule it.

## Addendum 2026-11-09 (issue #383)

- **Item 10 wording applied, approval still NOT RECORDED.** `rules/security.md` item 10 and the update
  paragraph of the design spec (section 14, and the outbound-call list of section 15) now name this timer as the
  second documented exception, with the owner's recommended defaults applied: on after an online install and told
  on the terminal, off after an offline install, on/off/status only, a fixed daily schedule, and units that arrive
  through the installer and not through `maran update`. The wording is a proposal until the owner approves it in
  writing; this note does not grant that approval and the branch stays unmergeable to `main`.
- **Observed since the note was written (installer polygon, both families, no systemd):**
  `docker/polygon/asserts/parts/update-security-units.sh` checks the units' effective lines (the fixed
  `ExecStart` with `--security-only`, five writable paths and not `/var/lib/maran`, the daily timer with no second
  trigger) against 41 planted cases, and runs step 69: units root:root 0644 and byte-identical to the package,
  dump directory 0700 and a planted symbolic link refused before any write, timer enabled on an online first
  install, left off on an offline one, and a timer disabled with the real `maran update-schedule disable` still
  disabled after two re-runs of the step.
- **Still UNOBSERVED:** the unit running under its sandbox on a booted host (the list of writable paths is derived
  by reading), reboot persistence of a disabled timer, `Persistent=true` catch-up, a real signed release applied
  end to end by the timer, and the rollback of a release that fails its health gate when started by the unit.
  The first real unattended run must be watched.
- **Existing servers.** `maran update` installs no unit, so a server moved to this release by an update has no timer
  until the installer is run on it again; this is stated in `docs/operations/panel-update.md`. Letting the update
  install the two unit files would widen the root update's writes to `/etc/systemd` and needs its own section here.
- **Second review: OUTSTANDING (#331).**
