# Threat note: running a cron entry now (issue #158)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
agent's privileged surface. This change adds `CronService.RunCronEntry`: the root agent starts an
account's existing cron entry immediately, as that account, and returns what it printed. It is the
first rpc in the tree whose purpose is to make a customer's own program run, so this note argues why
it is not the "run this for me" rpc that rules/architecture.md and rules/security.md item 12 forbid,
and says what is not proven.

**Second review: OUTSTANDING — must not merge to main.** The work was carried out by an agent
session, which cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human
reviewer has read it. The owner is told at hand-off that this surface carries an outstanding review.

## The problem being solved

A customer who writes a cron entry cannot see whether it works until its schedule fires, which may be
a day away, and then sees only the last run's output. The issue asks for a run-now action "as the
account user under the same limits" and for the last runs to be kept and shown.

## Shape

- **Rpc.** `RunCronEntry(account_username, entry_id)`. No program, argument, path, environment or
  limit travels on the wire. The request names an entry the agent itself installed; everything the
  run consists of is read by the agent from the account's own crontab and the entry's own command
  file.
- **Program.** The agent starts `systemd-run` (an `AdapterBinary`, `/usr/bin/systemd-run` on both
  families) with an argv built from agent constants and agent-validated values only:
  `--uid=<uid> --gid=<gid> --slice=user-<uid>.slice --wait --pipe --collect --quiet
  --property=RuntimeMaxSec=<agent constant> --property=WorkingDirectory=~ [--setenv=NAME=VALUE …]
  -- /bin/sh <home>/.maran/cron/<id>.cmd`. The uid and gid come from the passwd database through
  `ManagedAccount`, which refuses a system or reserved account; the id is a validated `CronEntryId`
  that names an entry present in the crontab; the `--setenv` pairs are the crontab's agent-managed
  environment, already validated as `EnvVarName` / `EnvVarValue` on the way in and re-parsed on the
  way out.
- **Identity.** The service manager (PID 1) forks the job and applies `User=`/`Group=` from
  `--uid`/`--gid` before `exec`. The agent never runs the customer's command in its own process and
  never runs it as root. `fork_as_account` is not used, because the job must outlive nothing of the
  agent's and must be placed in a cgroup, which a forked child of the agent cannot be moved into
  without a second privileged step.
- **Limits.** `--slice=user-<uid>.slice` puts the run under the CPU, memory and task ceilings issue
  #133 sets on that slice, the same ceilings the account's own logins run under.
  `RuntimeMaxSec` is enforced by the service manager, which kills the whole transient unit (every
  process in it, not a process group) when it passes. The agent spawns `systemd-run` through
  `run_bounded` under `CallClass::CronRun`, whose deadline (180 s) is above `RuntimeMaxSec`
  (120 s), so a wedged `systemd-run` is killed too. The panel waits 200 s for the answer
  (`AgentCronRunPipeline`), under the reverse proxy's 300 s read timeout, because the run is
  answered inside one HTTP request. Each output stream is captured under a 64 KiB cap; past it the agent
  kills `systemd-run`, returns no output at all, and reports the run as stopped by the output limit.
- **What the panel stores.** The panel keeps the last runs of each entry in `cron.CronRuns`, a
  tenant-filtered table, and the output it stores is the account's own text returned to its owner.
  The audit journal records the entry id and never the command or the output (the rule
  `CronAuditJournal` already states).

## What an attacker could do with this surface, and why it is safe now

1. **A customer runs arbitrary code.** They already can: the command is theirs, it already runs on a
   schedule as their uid, and they hold SFTP to their own home. Run-now changes WHEN, not WHO or
   WHAT. The rpc accepts no program text, so a caller of the agent socket cannot make it run anything
   that is not already an installed entry of that account.
2. **A customer makes root read or follow something.** The agent opens no file of the customer's for
   this rpc: the command file is opened by `/bin/sh` running as the account, inside the unit. The
   agent reads the crontab through the existing `CrontabSpool` path and nothing else. A symlink at
   `.maran/cron/<id>.cmd` is followed by the customer's own shell with the customer's own rights.
3. **A customer escapes the ceilings by running now instead of on schedule.** The run is placed in
   the account's slice, and the transient unit adds `RuntimeMaxSec` on top, so a run-now job is held
   to at least what the account's sessions are held to.
4. **A customer floods the panel.** Output is capped at 64 KiB per stream in the agent, the panel
   stores at most twenty runs per entry and prunes on every insert, and the endpoint sits behind the
   panel's `api` rate-limit policy. Concurrent runs of one entry are not refused (see below); each
   one still costs the account its own slice budget, not the host's.
5. **A suspended account runs its jobs.** A suspended entry is refused before anything is started,
   with the new `ERROR_CODE_ACCOUNT_SUSPENDED`, because the account's suspension is the panel's
   decision and a run-now must not be a way around it. The panel refuses it first as well; the agent
   refuses it again because the crontab, not the panel, is where the suspension is written. A DISABLED entry (the customer's own switch) does run: the customer is asking for exactly
   that run, and disabling an entry is how a customer keeps a job they only want to run by hand.
6. **Argument injection into `systemd-run`.** Every argument is either an agent constant, a number
   formatted from a `u32`, a path built by `AgentPaths` from a validated name and id, or a
   `NAME=VALUE` pair whose two halves refuse control characters. `--` ends `systemd-run`'s options
   before the shell path. An environment value beginning with `-` cannot be read as an option,
   because it is inside the `--setenv=` argument, not an argument of its own.
7. **Another tenant's entry.** The panel resolves the account through `IAccountDirectory`, which
   answers null for an account the caller does not own, so another tenant's entry answers 404. The
   run history is filtered by the same tenant query filter as every other tenant table.

## What the author could not verify

- **Not run on a real host.** `systemd-run --wait --pipe` propagating the job's exit status, and
  `RuntimeMaxSec` killing the unit, are taken from `systemd-run(1)` and `systemd.service(5)`, not
  measured on the polygon images. A reviewer should run an entry that exits 3, one that sleeps past
  the ceiling, and one that prints 1 MiB, on Ubuntu 24.04 and AlmaLinux 9, and check the three
  answers.
- **Whether the agent's own unit sandbox lets `systemd-run` reach the service manager.** It already
  runs `systemctl`, which uses the same bus, so it is expected to; it is not measured.
- **How a timeout is told apart from a slow success.** The agent reports `RUNTIME` when the run
  lasted at least `RuntimeMaxSec`; a job that finishes in exactly that second is reported as stopped.
  The service manager's own `Result=timeout` would be the exact answer, and reading it needs the unit
  to outlive `--collect`; that is left for the reviewer to judge.
- **Concurrent runs of one entry are allowed.** Two clicks start two units. Each is bounded, so this
  is a cost to the account and not to the host; serialising them would need a sixth agent lock
  (rules/rust.md "What this agent serialises"), which this change does not add.
- **Scheduled runs are not in the history.** The history holds the runs the panel started. Runs that
  cron started are still shown as "last run" from the files the installed line writes; adding them to
  the history needs the installed line to record a start time, which is a crontab format change left
  for a follow-up.
