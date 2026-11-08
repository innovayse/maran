# Threat note: the disaster-recovery proof and the release guard on restore (issue #129)

`rules/security.md` requires a threat note, written first, for a change to the installer's privileged
steps and to how secrets are handled. This note amends the two earlier notes
(`2026-11-01-panel-disaster-recovery-threat-note.md`, `2026-11-04-panel-backup-schedule-threat-note.md`)
and discharges neither. It covers only what #129 adds after them, and it is written before the proof
stage was extended: every statement below is a requirement the code must meet, not a description of
code that has run.

**Second review: OUTSTANDING (recorded under #331). The branch merges to `dev` only and MUST NOT merge
to `main` until a human reviewer has read it.** The work is done by agent sessions, which cannot obtain
a second reviewer. The owner is told at hand-off that this surface carries an outstanding review. The
two earlier notes' reviews are outstanding as well.

## What is added

1. `manifest.txt` gains one line, `release=<installed release name>`, written by `panel-backup` from
   `/etc/maran/release.state`. The archive format number stays `1`: the restore matches fixed lines and
   an archive without the line (taken before this change) is still restorable. A host with no
   `release.state` writes no line rather than a guess.
2. `panel-restore` refuses an archive whose `release=` is newer than the installed release, before it
   stops the panel or touches the database, and writes one audit line (`verb=panel-restore
   outcome=refused`).
3. The polygon's panel-recovery stage grows three observations, all on the existing two-container
   harness: the licence half of a restore, a restore onto a fresh panel that holds a schema the archive
   lacks, and the release guard against a real archive and a real fresh install.
4. No new RPC, daemon, port, package or outbound call. No CI workflow and no S3 decision is part of this
   note (both are owner decisions kept outside it).

## What an attacker could do, and why it is safe

- **Forge `release=` in the manifest.** The manifest is inside the gpg-encrypted, integrity-protected
  archive. Whoever can forge it holds the passphrase, which already lets them plant any archive (earlier
  note: "Plant an archive"). The value is data: it is counted by `awk`, extracted by `sed`, matched against
  the release-name pattern of `release-version.sh` BEFORE it is compared or printed, compared by
  `cli_version_gt`, and never evaluated, sourced, put on a command line unquoted or written to
  `panel.env`. A forged OLDER value only removes a refusal the operator would not have had before this
  change; a forged NEWER value makes the restore refuse (an availability effect that needs the passphrase).
  A manifest naming its release twice, or naming one that is not a release name, is refused as malformed.
- **Refusal as a trap.** An operator on a rebuilt server that is, by mistake, older than the dead one is
  stopped before anything changes, with a sentence naming both releases (fixed words plus the two
  validated release names, no path and no tool output). The way forward is to install the archive's
  release or a newer one; `maran update --offline-tarball` exists for that.
- **Fail-open on missing information.** An archive with no `release=` line, or an install with no
  recorded release, cannot be compared. Both are restored, and the operator is told on stderr. This is a
  deliberate choice: the alternative strands every archive taken before this change and every pre-layout
  host, and the only thing the guard protects is a schema mismatch the operator can also find by
  reading the release notes. It is the weakest part of the guard, and it is stated here so a reviewer
  can disagree.
- **The proof stage's licence fixture.** The stage writes a file that is NOT a signed licence into the
  dying server's `/var/lib/maran/licence.json` (owner and mode as `FileLicenceWriter` sets them). The
  verifier's public key is not touched and nothing in the product accepts the fixture: the panel reports
  it as refused, which is the observation (the file reached the new server and the service account can
  read it), and the stage prints that a VALID licence's survival is not observed.
- **The extra-schema leg** plants a schema in the fresh server's database through `psql` as the
  `postgres` OS user, inside a container that is deleted at the end of the run. The product code that
  clears it (`CLI_RESTORE_CLEAR_SQL` in `panel-dump.sh`) is not changed by this note.
- **Secrets in the proof.** The passphrase and the keys are generated per run and live in containers
  that are removed; every product output the stage captures is searched for them (`rec_leak_count`).

## What a reviewer must check

1. `panel-restore.sh`: the release value is validated by positive pattern BEFORE comparison; the refusal
   precedes `systemctl stop`; an archive with no `release=` line restores; an equal and an older
   release restore; a newer one refuses, and the panel service, `panel.env` and the database are
   unchanged. The hand-built cases are in `docker/polygon/asserts/parts/cli-recovery.sh`, the real
   archive case is `rec_release_guard_leg` in `scripts/lib/install-polygon-recovery-guards.sh`.
2. That the polygon's release-guard leg observes the PANEL being left running (the service's
   `ActiveEnterTimestampMonotonic` is the same before and after, so a stop-and-start cannot pass) and
   the DATABASE being left alone (a planted schema, which a real restore would drop, is still there).
3. The licence leg: file present after the restore with the owner and mode `FileLicenceWriter` gives
   it, byte-identical to the dying server's, and read by the panel (state `refused`, not `absent`; an
   unreadable file reports `absent`, so the two differ exactly when ownership or mode is wrong).
4. That the first real run of the stage is reported with its numbers (`N checks`, per OS, PostgreSQL
   major) and that every failure it reports was fixed in the code, not by loosening an assertion.

## What the author could not verify

1. The whole proof on a real host. At the time of writing the stage has not completed once: the
   installer did not get past `installer/lib/65-schema.sh` in the polygon on the author's workstation
   (diagnosis and fix in the #129 issue comments). Until a green `maran installer --os ubuntu24` and
   `--os alma9` run exists, every statement above is a requirement.
2. UNOBSERVED HERE, and printed by the stage: the timer firing on its schedule and `Persistent=true`
   catch-up; SELinux enforcing; real S3 providers; a signed, valid licence surviving a restore;
   PostgreSQL majors and families other than the one each run uses (ubuntu24 is 16 and alma9 is 13, so
   two runs together cover both majors and both families, and nothing else).
3. A clock that jumps; a timer firing during a panel upgrade (the lock covers backup, restore, update
   and rollback overlap, not the installer).
4. A restore onto an install whose `release.state` is hand-edited: the file is read by
   `cli_layout_state_read`, which refuses one that is not root-owned and plain, but root can write
   anything, and root is the attacker model's end.
