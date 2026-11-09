# Account restore proof (issue #6) — threat note

Status: **second reviewer OUTSTANDING** (#331; rules/security.md "Sensitive change escalation"). This
change adds no rpc, no endpoint and no privileged product code. It adds (a) an installer-polygon stage that destroys and restores an account
through the panel API as root inside a throwaway container, (b) a stage that kills the root agent during a restore, and
(c) an operator runbook that tells a human to do the same on a real server. It is a note because the stage kills the root
daemon in the middle of the only operation that can destroy a working account, and because the runbook is a script for
destroying customer data on purpose. Written before the code; a deviation is added afterwards under "Deviations found when
the code landed". The branch MUST NOT merge to `main` until a second reviewer has read this note and the diff. An agent
session wrote it and cannot be that reviewer.

## The surface

1. `scripts/lib/install-polygon-account-*.sh`: run `docker exec` as root in the polygon container; call the panel API with
   an administrator token; read the account's home with `find`/`stat`/`sha256sum` and its databases with `mysqldump`.
2. The kill leg sends SIGKILL to `maran-agent` while a restore stream is open, then lets systemd restart it
   (`installer/systemd/maran-agent.service` has `Restart=on-failure`).
3. `docs/operations/backups.md`: a manual procedure for a real host.

No new listening port, daemon, outbound call or rpc (rules/security.md items 10, 12). No product file is changed by this
work; a defect the proof finds is fixed under its own issue and its own note.

## What could go wrong, and the requirement for each

R1. **The comparison is blind.** A snapshot that omits ownership, mode, symlinks, empty directories or database rows
    certifies a restore that lost them (rules/testing.md "A check must be able to observe what it reports on"). Requirement:
    the snapshot records path, type, uid, gid, mode, size, sha256 of content and symlink target for every entry under the
    home, and a per-table row count plus a checksum of every row for every database; a positive control plants a change
    in each axis (chmod, chown, one flipped byte, one removed file, one altered row) and requires the comparison to FAIL
    for each; an inverse control compares a snapshot with itself and requires success.

R2. **The kill misses the window** and the stage reports "never half-applied" over a restore that finished. Requirement:
    the stage asserts the stream was in the targeted stage (`restoring_files`, `restoring_databases`, the swap) before it
    kills, as `restore_recovery_on_a_real_host.rs` already asserts; a kill that missed fails the stage, it does not pass it.

R3. **A real host damaged by the runbook.** The runbook MUST name a throwaway account on a server where the operator has a
    current backup of the whole server, MUST show the backup id and the sha256 before the destroy step, and MUST refuse to
    proceed on an account that is not named `proof_*` unless the operator types the account name.

R4. **Secrets in the output.** The harness creates a database password and an administrator token. They go to files at
    mode 0600 inside the container and never to the stage's log, `ps` output or the runbook text (rules/security.md item 8).

R5. **The proof is read as more than it is.** The stage prints its blind spots (rules/testing.md): a container shares the
    host kernel, so a power cut, quota enforcement and SELinux are unobserved; SIGKILL of the agent is not a power cut.

R6. **Panel row after a kill.** The restore note records that the panel's own row is left in whatever state the dropped
    stream gave it. The stage reads the task row and the backup row after the kill and records them; a row that claims
    success over a restore that did not finish is a finding, filed as an issue at once.

## What a reviewer must check

1. The snapshot axes in R1 and that each positive control really fails the comparison (run it once with a planted change).
2. The kill happens only after the targeted stage was observed (R2), and a missed kill fails.
3. The runbook's guard in R3 reads as written, and that nothing in the stage touches a path outside the container.
4. That the stage does not weaken any product guard to make a restore pass (no env override, no skipped confirmation:
   the restore request carries the real `ConfirmAccountUsername`).

## What the author could not verify

No run on a real server or a VM; no power cut; the stage's behaviour on AlmaLinux 9 until it is run there; whether the
window of `restoring_databases` is wide enough to be hit deterministically on a small database (the task sizes the data to
make it so and measures it).
