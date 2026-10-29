# Threat note: deleting an account ends its processes first (issue #103)

Written **before** the change, as `rules/security.md` ("Sensitive change escalation") requires. It
is filed on `fix/103-account-processes` ahead of the first code commit of that branch.

This change adds a kill-and-verify step to the root agent's account deletion, a new error, and
corrects a comment in the agent's unit that describes the deletion path.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by agent sessions,
which cannot obtain one. The change may land on a branch and MUST NOT merge to `main` until a human
reviewer has read this note and the diff and signed off. The owner is told at hand-off that this
surface carries an outstanding review.

## The mechanism is CONFIRMED

Measured on Ubuntu 24.04 (shadow 4.13) and AlmaLinux 9 (shadow 4.9), throwaway containers: `useradd`
an account, start `sleep 600` at its uid under `setsid`, then `userdel --remove`.

| case | Ubuntu 24.04 | AlmaLinux 9 |
|---|---|---|
| root WITH `CAP_SYS_PTRACE` | `userdel: user is currently used by process`, exit 8, user kept | same, exit 8 |
| root WITHOUT `CAP_SYS_PTRACE` (`capsh --drop=cap_sys_ptrace`) | exit 0, passwd entry gone, **the process survives** | same |
| `stat -L /proc/<pid>/root` without the capability | `Permission denied` | same |
| next `useradd` | received the SAME uid; `ps` shows the orphan owned by the new user | same |

Docker's default capability set already lacks `CAP_SYS_PTRACE`, so a plain `userdel` in a default
container behaves like the second row. The reason: `userdel`'s busy scan resolves
`/proc/<pid>/root`, which the kernel guards with `ptrace_may_access`; for a process of another uid
that needs `CAP_SYS_PTRACE`. The agent's `CapabilityBoundingSet` deliberately lacks it (#91), so
every customer process is unreadable to `userdel`, is skipped, and the account is judged idle.

This is NOT a `hidepid` or `ProtectProc` effect. An earlier comment in `maran-agent.service` blamed
`hidepid`; it is rewritten in this change to state the missing capability. No directive is changed.

Consequence: `userdel` is not a safety net on this daemon. Deletion must end the uid's processes
itself and verify they are gone.

## What an attacker can do with this surface today

The attacker is a hosting customer, or a malicious program that customer ran.

1. The customer starts something that outlives a deletion: a `nohup`/`setsid` shell, a reverse shell,
   a miner, a PHP worker mid-request when its pool is removed (a graceful php-fpm reload lets old
   workers finish), a cron job that started in the last minute.
2. The account is deleted (abuse takedown, non-payment, the customer's own request). The agent drops
   the databases, removes both transfer-login kinds and their jails, the crontab and the pools, then
   runs `userdel --remove` and nothing else. `delete_under_lock` has no process step; the process
   cull exists only in suspension (`ops::logins::end_account_sessions`, called only from
   `set_account_logins_locked`).
3. The process now runs under a numeric uid with no passwd entry. The next `useradd` is free to hand
   that uid to a different tenant, and was measured doing so (above).
4. The orphan and the new tenant share a uid, so the orphan can read and write the new tenant's home,
   read their PHP sessions and `wp-config` secrets, signal and `ptrace` their processes, write into
   their quota, and keep ports, sockets and CPU outside any plan. This is cross-tenant access by
   construction. It also defeats an abuse takedown: the deleted customer's miner keeps running.

This is the uid-recycling hazard the delete path already defends against for databases, the crontab,
mounts and logins (rules/rust.md "What this agent serialises"). Processes are the one resource of the
uid it does not yet release.

## Options weighed

- **A. Kill and verify before `userdel` (chosen).** Fixes the cause, works for every uid, reuses the
  allow-listed `pkill`.
- B. Avoid uid reuse (a monotonic allocator or a tombstone of freed uids). Does not stop the orphan:
  a miner keeps running after a takedown, under a uid with no account. Needs persistent allocator
  state and panel/agent agreement, and leaves the kill unsolved. Rejected as the fix.
- C. Add `CAP_SYS_PTRACE` so `userdel` refuses. Rejected: a large unrelated power for the only root
  process; the narrowing was deliberate (#91).
- D. `userdel --force`. Rejected: it does not kill, and removes group and home in unsafe cases.

## The change planned

Inside the existing critical section (`AccountOperations::delete_under_lock`, account lock held), a
new step between the second crontab sweep and the second resolution:

```
resolve_managed -> drop databases -> remove SFTP logins -> remove FTPS logins
  -> remove crontab (1) -> remove php-fpm pools -> remove crontab (2)
  -> END THE UID'S PROCESSES (new) -> resolve_managed again, compare ids -> userdel --remove
```

Shape: up to 5 rounds of { `pkill --signal KILL --count --uid <uid>`; `pgrep --count --runstates D,R,S,T,t,W,X,I,P
--uid <uid>`; exit 1 (nothing matched) means done; exit 0 means pause 200 ms through an injected host
method and go again }. If processes remain after the fifth round, return
`AccountError::ProcessesRemain { username }` and **do not run `userdel`**. Any exit code other than
0 or 1 from either call is a refusal, never a success.

Constraints:

- `pkill` is `distro.pkill_binary()`, already allow-listed for suspension. No new binary, no new rpc,
  no shell, argv only; the uid is a `u32` from the `ManagedAccount`, which refuses uid 0 and any uid
  below the managed floor, so this cannot signal a system user.
- **No `CAP_SYS_PTRACE`.** **No `userdel --force`.**
- `KILL`, not `TERM` then wait, for the reason `end_account_sessions` gives.
- The `pkill` argv constants are shared with `end_account_sessions` rather than spelled twice; the
  verify half and the loop are new.
- A distinct error variant, mapped in `account_status.rs` to the existing `SystemFailure` code (no
  proto change).
- **No `hidepid` refusal.** Deletion does not refuse on a `/proc` mounted with `hidepid`; see the
  blindness residual below.

### Order of operations: what stops a respawn

- **Pool removal first.** The php-fpm master is root and respawns a worker as the account within
  milliseconds of any kill while the pool exists. After the reload it no longer spawns for this uid;
  old workers of a gracefully reloaded pool may live on, and are what the kill is for.
- **Crontab removal (twice, already there) before the kill.** `cron` re-reads the spool about once a
  minute, so a job queued for the current minute can still start. The loop covers it: a process that
  appears is found by the next check and killed by the next pass.
- **SFTP and FTPS logins before the kill.** With logins and jails gone, sshd and vsftpd cannot admit
  a new session at this uid.
- **Suspension first is not required.** Deletion has already removed every way back in.
- **The kill comes after the lock and before the final re-resolution**, so the check immediately
  preceding `userdel` is the freshest fact the sequence holds.
- A tension to review: jail teardown (`umount`) runs BEFORE any kill, so a live `sftp-server` inside
  the bind mount may make it fail with the existing `SftpRemoval` refusal. Not new, and the refusal is
  the safe direction. A best-effort early kill is NOT included; the mandatory gate is the late one.

### Why the loop re-kills instead of only re-checking

`pkill` signals a snapshot of `/proc`; a process forked just before the snapshot survives the first
pass. Each round kills and checks. A signalled process is expected not to be able to fork (recalled
kernel behaviour, not measured), so each pass shrinks the set and a short bound suffices.

## Races, residuals and what is deliberately left uncovered

- **Between the last check and `userdel`.** Only something privileged can create a process at the uid
  (the agent's own `fork_as_account` children, an operator, a second agent). The window is the second
  `resolve_managed` plus one spawn.
- **The account lock does not cover file rpcs.** `WriteFile` and the other file operations run a
  `fork_as_account` child without the account lock. A write started in that window runs as the uid at
  `userdel` time: a short-lived child writing into a home being removed, not a lasting survivor.
  Pre-existing; closing it needs a child registry this agent does not have.
- **Process-local lock.** It excludes this binary's own rpcs, not another agent or an operator's
  `userdel`; the identity re-resolution catches the last.
- **Blindness is possible and the check cannot see it.** Another PID namespace (a container running
  as that uid) and a `/proc` mounted with `hidepid` hide processes from `pkill` as much as from
  `userdel`; `pkill --signal 0` then reports "none" while a process lives, a verification sharing the
  view of the thing it verifies. Stated as a limit, not a closed case. The owner decided against a
  `hidepid` refusal; the reviewer is asked to confirm that is acceptable.
- **Zombies are excluded from the check; D-state is not.** The verification is
  `pgrep --count --runstates D,R,S,T,t,W,X,I,P --uid <uid>` (`distro.pgrep_binary()`, same procps
  package as `pkill`; `--runstates` verified on procps-ng 4.0.4 Ubuntu 24.04 and 3.3.17 AlmaLinux 9),
  not `pkill --signal 0`, because `pkill` matches zombies. A zombie has exited: no memory, fds or
  credential, cannot run code, so a reused uid inherits nothing from it. Counting it refused every
  deletion wherever PID 1 does not reap (the polygon container ran `cargo test` as PID 1, so every
  killed orphan stayed a zombie; PR #123). The polygon now runs with `--init` as well, so the test
  environment reaps like systemd does. A task in uninterruptible sleep cannot be killed until it
  wakes and still counts, ending in `ProcessesRemain` (fail closed). OPEN: how an unkillable
  (D-state) process is handled beyond the refusal and an operator retry. Residual: a zombie's pid
  stays allocated until reaped, which is not an uid-inheritance hazard.
- **Other uid-keyed leftovers are OPEN and out of scope here:** queued `at` jobs under
  `/var/spool/at`, the mail spool, files in `/tmp` and `/var/tmp`, SysV shared memory and message
  queues, a systemd lingering user manager. A follow-up issue is to be filed for them. Whether `atd`
  and `linger` can exist for a managed account was not checked; `atd` in particular is a respawner
  the ordering above does not remove.
- **`pkill` and `pgrep` must exist.** `installer/lib/20-dependencies.sh` already installs `procps` /
  `procps-ng` (`signalling_packages_for_family`), which ships both, so no installer change is needed.

## Interaction with the account lock

- The step runs entirely inside the lock `delete` already holds. The lock stays outer and never
  waits, so no wait-for edge is added (rules/rust.md "What this agent serialises").
- The loop lengthens the hold by at most 5 pauses of 200 ms (rounds are separated by pauses, so four
  of them occur before the fifth round; the bound is about a second plus spawn time). A concurrent
  rpc on the account is refused with `AccountBusy`. The pause is an injected host method, so tests
  drive virtual time (rules/testing.md). It runs inside the deletion's existing `spawn_blocking`.
- `end_account_sessions`' contract (not called without the lock; assumes no `fork_as_account` child
  is running as the uid) holds under the deletion lock for the same reason, except for the file-rpc
  gap above.

## What the error does to the panel's state

`ProcessesRemain` is raised after the databases are dropped, both login kinds and jails removed, the
crontab and pools removed, and before `userdel`. The account is **half deleted by design and
recoverably so**, as with every earlier refusal in this function:

- Host: the Linux user and home still exist; the processes that could be killed are dead.
- Panel: `DeleteAccountCommandHandler` runs the module cascade first, audits the residue, then calls
  the agent, and drops the `Account` row last. An agent refusal leaves the `Account` row, with a
  FAILURE audit entry and a typed failure, not a 500. (To be confirmed against the handler and its
  test before relying on it.)
- Retry: every step is idempotent, so running the deletion again re-attempts the kill and, once the
  process is gone, completes. The final backup was taken first, so a refusal loses no data.
- Error content: the variant carries the account name only. No pid, command line or count reaches the
  customer; the agent logs the figure for the operator (command lines can hold secrets).
- Can a tenant make their account undeletable? Only with a process SIGKILL cannot end (a kernel or
  filesystem fault) or a zombie whose parent is outside the uid. Low, and the result is a visible
  refusal, not a silent success.

## What a second reviewer must check

1. That the comment in `maran-agent.service` states the real reason (missing `CAP_SYS_PTRACE`) and
   that no directive changed, `CapabilityBoundingSet=` in particular; no `userdel` flag was added.
2. That `userdel` is NOT reachable on any path where the check did not report "none" or the step
   errored: read every `?` and early return between the step and `expect_success(userdel)`.
3. The loop re-kills each round; the bound is finite (5 x 200 ms); the pause is injected; exit codes
   other than 0 and 1 from either `pkill` call are refusals.
4. **Prove the check can fail.** On a real host with the agent's capability shape (no
   `CAP_SYS_PTRACE`; a test that runs with it would pass without the fix, which is vacuous): start
   `sleep 600` at the account uid, a double-forked one and a php-fpm worker; delete; the uid has no
   process; create an account at once and assert it inherited none. Inverse control: with the kill
   removed the survivor appears. A process that cannot be killed yields `ProcessesRemain`, the account
   remains, `userdel` did not run, and a retry after it goes completes. Run on Ubuntu 24.04 and
   AlmaLinux 9.
5. A unit test per `AccountError` variant, `ProcessesRemain` included, with a fake host whose second
   `pkill` answers "still there"; and that the existing delete tests' fake answers `pkill` with "none"
   by default rather than masking the step.
6. The argv: `distro.pkill_binary()` first; `--signal`, `KILL` or `0`, `--count`, `--uid`, the numeric
   uid; no user-controlled string. `--uid` is the REAL uid; confirm it matches php-fpm workers and
   sshd session children.
7. The ordering against pool removal and the crontab sweeps, as in "Order of operations".
8. The mapping in `account_status.rs` and that the C# handler logs and answers a typed failure.

## What the author cannot verify

- That killing works for every process shape: seen for `sleep`-style processes and suspension's
  session processes; not seen for php-fpm workers, a double-forked daemon, or a PID-namespaced process.
- Kernel behaviour recalled and not measured: a signalled process cannot fork; `useradd`'s uid
  allocation order.
- Ubuntu 22.04, Debian 12 and 13, Rocky and Alma 10; aarch64; a `hidepid`-mounted `/proc`; SELinux
  enforcing (a confined `pkill` signalling an unconfined-domain process).
- Whether `atd` or a lingering user manager can hold processes for a managed account.
- Real-system timing: whether 5 x 200 ms suffices for exiting tasks to disappear on a loaded host.

## Second review: OUTSTANDING

State: no reviewer has read this note or the diff. The branch carrying the change must not merge to
`main`. This is a debt with a name, not an alternative route (rules/security.md).

## Decisions

Taken by the owner:

1. Kill-and-verify before `userdel`, in the existing critical section.
2. The bound is 5 rounds of 200 ms; survivors after that yield `ProcessesRemain`, which maps to
   `SystemFailure` (no proto change). A dedicated additive `ErrorCode` stays a later option.
3. No `CAP_SYS_PTRACE`, no `userdel --force`.
4. No `hidepid` refusal.

Open:

1. File a follow-up issue for the other uid-keyed leftovers (`at` jobs, mail spool, `/tmp` and
   `/var/tmp` files, SysV IPC, linger).
2. The unkillable (D-state) process case beyond the fail-closed refusal.
