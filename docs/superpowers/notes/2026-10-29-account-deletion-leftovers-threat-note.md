# Threat note: deleting an account releases its other uid-keyed leftovers (issue #139)

Written **before** the change, as `rules/security.md` ("Sensitive change escalation") requires. It is
filed on `fix/139-uid-leftovers` ahead of the first code commit of that branch. It continues
`2026-10-29-account-deletion-processes-threat-note.md` (issue #103), whose "other uid-keyed leftovers
are OPEN" item this note takes over.

The change adds steps to the root agent's account deletion, two new `agent-core::privs` syscall
wrappers (`unsafe`), four new `AccountError` variants, and edits to the agent's systemd unit.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by agent sessions, which
cannot obtain one. The change may land on a branch and MUST NOT merge to `main` until a human
reviewer has read this note and the diff and signed off. The owner is told at hand-off that this
surface carries an outstanding review.

## The hazard

After #103 a deleted account has no processes. The uid is still a key into several other places on
the host, and `userdel --remove` clears only some of them. The next `useradd` may hand the same uid to
a different tenant, who then inherits whatever still carries the number. The attacker is the deleted
customer (or a program they ran), planting something that outlives them to be read, executed or
triggered under the next owner's identity. Per leftover:

| leftover | real? | verdict |
|---|---|---|
| queued `at` jobs (`/var/spool/at`) | Only where `at` is installed. A job is a file owned by the uid that `atd` runs as that uid at its time. | REAL when `at` exists. Deleted explicitly: `atq` parse, `atrm` per job, re-verify. |
| mail spool (`<mail dir>/<user>`) | Was assumed removed by `userdel --remove`. MEASURED WRONG on AlmaLinux 9 (polygon, PR #176): spools of deleted accounts survived and the next owner of the uid owned them; Ubuntu passed. | REAL on the RHEL family. Removed explicitly by name (see "Mail spool" below); the real-host test pins it on both families. |
| shared temp files (`/tmp`, `/var/tmp`, `/dev/shm`) | Files owned by the uid outside the home; `userdel` does not touch them. A reused uid owns them. | REAL. Swept by a descriptor walk as root, keyed by `st_uid`. |
| SysV IPC (shared memory, semaphores, message queues) | Objects keyed by creator/owner uid survive process death. | REAL. Listed from `/proc/sysvipc/*`, removed with `ipcrm` by id, re-verified. |
| lingering systemd user manager | Present only if linger is enabled or a session left `user@<uid>.service`. | REAL on the systemd lane. `loginctl disable-linger`, `terminate-user`, then poll `/run/user/<uid>`. |

## The PrivateTmp / PrivateDevices finding

`maran-agent.service` runs with `PrivateTmp=true` and `PrivateDevices=true`. The agent therefore sees a
private, empty `/tmp` and `/var/tmp`, and possibly no host `/dev/shm`. A sweep written against those
paths would run, find nothing and report success: **vacuously green**, the shape this repository
forbids. The first thing the change must do is make the host paths reachable.

Owner decision (2026-10-29): `PrivateTmp` stays ON. The unit gains `BindPaths=` that bind the host
`/tmp` and `/var/tmp` (and `/dev/shm` only if it is hidden, to be measured) onto
`/run/maran-host/tmp`, `/run/maran-host/var-tmp` and `/run/maran-host/dev-shm`, directories created by
`tmpfiles.d` as `0700 root`. The sweep walks those mount points. Loosening `PrivateTmp` was rejected:
it would give the agent's own temporary files the host-wide `/tmp` namespace, a cross-process attack
surface for the only root process, to serve one cleanup.

Consequence: the agent now holds a read-write view of the host's world-writable temp directories. That
is the surface the walk below must be safe on (symlinks, mounts, hard links, races).

## Delete order

Inside the existing critical section (`delete_under_lock`, account lock held), after the databases,
both login kinds, the crontab and the pools:

```
crontab(2) -> at jobs -> user manager -> end_account_processes (#103)
  -> SysV IPC -> shared temp files -> mail spool -> re-resolve, compare ids -> userdel --remove
```

Reasons: `atd` and the user manager are respawners and creators of processes, so they go before the
kill; IPC and temp files are only stable once nothing can write them, so they follow the kill; the
re-resolution stays the last check before `userdel`, as in #103. Every new step is idempotent and
fails closed: an error stops the deletion before `userdel`, leaving the account recoverably half
deleted.

## New privs wrappers (unsafe; second reviewer)

Both live in `agent-core/src/privs/`, one file per syscall, each with its own `SAFETY` argument, and
each takes a directory the caller already holds open plus a single entry name checked by
`directory_entry_name.rs`.

- `stat_in_directory.rs`: `fstatat(dirfd, name, AT_SYMLINK_NOFOLLOW)`. Returns uid, mode, device and
  link count of the entry itself, never of a symlink target.
- `remove_directory_in_directory.rs`: `unlinkat(dirfd, name, AT_REMOVEDIR)`. Removes an EMPTY
  directory only; it cannot recurse and cannot follow a link.

Files are removed with the existing `remove_file_in_directory.rs`. The sweep descends with
`open_in_directory` (`O_NOFOLLOW | O_DIRECTORY`), never through a path string.

### The sweep's rules (what a reviewer must hold it to)

1. Root-run walk, decisions by `st_uid == uid` of the entry (not of the path).
2. Never crosses a mount: a child whose `st_dev` differs from the starting directory is skipped.
3. Never follows a symlink; a symlink owned by the uid is unlinked as a link.
4. A directory owned by the uid is emptied depth-first and then removed ONLY if every entry in it was
   the uid's own. If it holds an entry of another owner, deletion **refuses with `SharedFilesRemain`**
   (owner decision: no `chown`, no deletion of someone else's data) and `userdel` does not run.
5. Hard links: an entry with `st_nlink > 1` owned by the uid is unlinked by name; this removes only the
   name and cannot reach another owner's data. Stated, not hidden.
6. A swap between `fstatat` and `unlinkat` can only substitute another entry of the same name inside a
   directory the walk already holds by descriptor; the unlink then removes that name. In a sticky
   world-writable directory this is the semantics of the sticky bit. The re-verification pass (a second
   walk that must find nothing of the uid) bounds the damage to a refusal.
7. The walk is bounded in depth and entries; exceeding either is a refusal, never a partial success.

## New error variants

`AccountError` gains `AtJobsRemain`, `UserManagerRemains`, `IpcRemain`, `SharedFilesRemain` and `MailSpoolRemains`, each
carrying the account name only (no path, id or count reaches the customer; the agent logs the figure
for the operator). All four map to the existing `SystemFailure` code in `account_status.rs`; no proto
change. Every `SystemHost` method and every fake that implements it grows with them (contracts first).

## Per-leftover design

- **at jobs.** Skipped when `atq` is absent (`at` not installed: nothing can exist). Otherwise
  `atq` is parsed for the uid's jobs, each removed with `atrm <id>`, and `atq` is read again; any job
  left, or any output line that cannot be parsed, refuses with `AtJobsRemain`. Unparseable output is a
  refusal, never "none". Binaries come from the distro adapter; argv only; ids are parsed integers.
- **mail spool.** The first design left it to `userdel --remove`; the polygon measured that it does
  not on AlmaLinux 9 (cause of the survival inside `userdel` not isolated by the author). Now an
  explicit step after the shared-temp sweep and before the identity re-check: the mail directory is a
  distro fact (`DistroAdapter::mail_spool_directory`, the REAL directory: `/var/mail` on Debian,
  `/var/spool/mail` on RHEL, the other spelling being a symlink). The directory is opened
  `O_DIRECTORY | O_NOFOLLOW`; `<dir>/<account name>` is `fstatat(AT_SYMLINK_NOFOLLOW)`ed and removed
  with `unlinkat` only if it is a regular file owned by the account uid; a uid-owned entry of any other
  kind (link, directory) is refused with the new `AccountError::MailSpoolRemains` and untouched; an
  entry of another uid is left alone. Own variant rather than `SharedFilesRemain` because the operator
  must look in the mail directory, not a temp directory; same `SystemFailure` mapping. The unit's
  `ReadWritePaths=` gains `-/var/spool/mail -/var/mail`. Residual: only the file named after the
  account is removed, not other files in the directory the uid may own.
- **SysV IPC.** `/proc/sysvipc/shm`, `msg`, `sem` parsed for rows whose `uid` or `cuid` equals the
  account; `ipcrm` run by id; the tables re-read. Anything left refuses with `IpcRemain`.
- **User manager.** Only when the host runs systemd: `loginctl disable-linger <name>`,
  `loginctl terminate-user <name>`, then poll for the disappearance of `/run/user/<uid>`; present after
  the bound refuses with `UserManagerRemains`. Skipped, and said so in a comment, on a host without
  systemd.

## Races and residuals

- Account lock is process-local and does not cover file rpcs (as in #103): a `fork_as_account` child
  running as the uid can write a temp file after the sweep. Short-lived, same residual.
- An `at` job queued between `atrm` and `userdel` needs a privileged creator; the re-verification
  narrows the window to one spawn.
- The sweep reaches only the namespaces the agent can see (see below).
- A deleted account's files in a directory the walk refuses (`SharedFilesRemain`) stay until an
  operator resolves them; the refusal is visible, not silent.

## What the author cannot verify

- That `at` is present on stock images. The polygon images install it for the real-host test; whether
  a given customer host has it is unknown, and the code skips cleanly when it does not.
- Whether `/dev/shm` is hidden under `PrivateDevices=true` (spike A under real systemd decides whether
  that third bind is needed).
- The `ipcrm` option spelling on both families (spike C). Not asserted in this note.
- IPC objects in other IPC namespaces (a container running at that uid) and files in other mount
  namespaces: invisible to this agent, as processes in other PID namespaces are in #103.
- gid-keyed leftovers and `/dev/mqueue`: deliberately NOT handled here (owner decision, follow-up
  issue). A reused gid can inherit group-owned files and queues.
- Ubuntu 22.04, Debian 12/13, Rocky/Alma 10, aarch64, SELinux enforcing.

## Reviewer checklist

1. The unit: `PrivateTmp=true` and `PrivateDevices=true` unchanged; only `BindPaths=` added; sources
   are the intended host paths; targets exist before start (tmpfiles, `0700 root`).
2. The two wrappers: `SAFETY` arguments; `AT_SYMLINK_NOFOLLOW` and `AT_REMOVEDIR` forced and not
   caller-supplied; entry names validated; no path string reaches a syscall.
3. The sweep rules 1 to 7 above, each by reading the code and by a test that fails without it
   (symlink to a foreign file, bind mount, foreign file inside the uid's directory, hard link).
4. `userdel` is NOT reachable on any path where a new step reported a leftover or errored.
5. Argv of `atq`, `atrm`, `ipcrm`, `loginctl`: distro binaries, parsed numeric ids, no
   user-controlled string, no shell.
6. A unit test per new `AccountError` variant, and defaults of the fakes that do not mask the steps.
7. The real-host test (polygon, both families, `at` installed): each leftover created, deletion run,
   a new account created at once inherits none; inverse control with each step removed. The sweep test
   MUST fail if the bind mounts are absent (proof it is not vacuous).
8. Mapping in `account_status.rs` and the panel handler's typed failure.

## Second review: OUTSTANDING

State: no reviewer has read this note or the diff. The branch carrying the change must not merge to
`main`. This is a debt with a name, not an alternative route (rules/security.md).

## Decisions

Taken by the owner (2026-10-29):

1. Host `/tmp`, `/var/tmp` (and `/dev/shm` only if hidden) reach the agent through systemd
   `BindPaths=` into `/run/maran-host/*`; `PrivateTmp` stays on.
2. A uid-owned directory holding another owner's entries makes deletion refuse (`SharedFilesRemain`);
   nothing is chowned.
3. gid-keyed leftovers and `/dev/mqueue` are a follow-up issue.
4. The polygon images install `at` for the real-host test.
