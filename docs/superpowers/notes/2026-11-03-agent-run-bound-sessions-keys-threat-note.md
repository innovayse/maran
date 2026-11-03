# Threat note: cron run bound and unit stop, SFTP session end, SFTP key-file lifetime (issues #333, #336, #338, #339)

Second reviewer: **OUTSTANDING** (rules/security.md, "Sensitive change escalation"). This note was
written beside the change by its author, so it closes nothing; it is what the reviewer reads instead
of a diff. The change touches the agent, which runs as root, and adds one rpc (`EndSftpSessions`) and
two new root spawns (`pkill --full` by pattern, `systemctl stop` of a named unit). Proto revision 15.

## What is changing, and what an attacker could do with it

1. **Run-now was unbounded (#333).** Each `RunCronEntry` holds a blocking-pool thread for up to three
   minutes. The only limit was a request rate, so a customer scripting the button could occupy the pool
   every other rpc of the root daemon shares (file operations, `userdel`, backups).
2. **The output cap killed the client, not the job (#336).** `systemd-run --wait --pipe` is a client of
   a unit the service manager runs. Past the output ceiling the bounded core killed the client; the job
   kept running to `RuntimeMaxSec` while the customer was told it had been stopped.
3. **A key file could outlive its login (#338).** `delete_sftp_user` ran `userdel` and then removed the
   key file, `create_sftp_user` purged nothing, and no sweep existed. A deletion interrupted between the
   two steps left `/etc/ssh/maran-sftp-keys/<login>`; the panel reuses names, so the next login created
   under that name authenticated with the previous login's keys.
4. **Revoking a key left its sessions open (#339).** sshd reads the key file at authentication and never
   again, so a session opened with a revoked key stayed alive.

## The changes, and why each is safe

- **Bound.** `ops::cron::run_now_slots`: a host-wide semaphore (16) and one per account (2), both taken
  without waiting as the first statement of `run_cron_entry`, released by dropping the grant. Refusal is
  `CronError::RunCapacityReached` -> `ERROR_CODE_AGENT_SATURATED`, before the crontab is read. It is not
  one of the agent's locks (it never waits, so it is no wait-for edge; rules/rust.md says so).
- **Stop.** The unit is named by the agent: `maran-cron-run-<uid>-<entry id>-<counter>`, built only from
  a password-database uid, a validated `CronEntryId` and a counter (`[a-z0-9-]`, no request text).
  When the bounded core ends the client (`SpawnError::Bound`: output cap or deadline) the host runs
  `systemctl stop -- <name>.service` with the `service_manager` `AdapterBinary`, argv only, class
  `Reload`. `TimeoutStopSec=10` is set on the unit so a job ignoring SIGTERM is killed in ten seconds.
  A stop that cannot be confirmed (any status except 0 and 5, "not loaded") is `RunNotStopped` ->
  `SYSTEM_FAILURE`, never a run reported as stopped.
- **Key files.** Deletion removes both key-file names BEFORE `userdel`. Creation removes both names of a
  new login's file BEFORE `useradd`, only when the name is absent from the password database (a repeated
  create finds the login and must not delete a working login's keys). `reconcile_orphan_key_files` runs
  once at start, before the socket, listing the directory first and the password database second, and
  removes only files whose name has the exact shape of a login's key file (`[a-z0-9_]` holding the
  separator, optional `.suspended`) and whose login is absent. An unreadable password database removes
  nothing.
- **Sessions.** `EndSftpSessions(account, suffix)` rebuilds the name through `SftpUserName::for_account`,
  requires the login to be in the account's jail enumeration (otherwise NOT_FOUND, no signal), then runs
  `pkill --signal KILL --count --full -- '^sshd(-session)?: <login>( \[priv\]|@)'`. The pattern is built
  only from a validated login name (`[a-z0-9_]`, no regex metacharacter), follows the end-of-options
  marker, and is anchored so a command line that merely mentions it does not match.

## What an attacker could still try

- **Retitle a process to match another login's pattern.** The only effect is that the attacker's own
  process dies when the other login's sessions are ended on the panel's request. The pattern cannot reach
  a process of another uid that did not retitle itself.
- **Make the pattern match too much.** `alice_web` against `alice_webdav`: the name is followed by a space
  or `@`, neither in the login alphabet. A test pins the exact string.
- **Hold the account lock to block a revocation.** `end_sftp_sessions` deliberately takes no account
  lock: the lock never waits, so a backup of the account would refuse a revocation for minutes. The signal
  is not uid-wide, so the lock's reason (our own `fork_as_account` children) does not apply.
- **Starve the other accounts of run slots.** An account holds at most 2 of the 16; the rest of the host's
  rpcs share a pool of 512. Sixteen accounts at their ceiling can still refuse a seventeenth account's
  run; that is the bound doing its job, answered `AGENT_SATURATED` and retryable.

## Accepted residue

- **The run slot table is process-local.** The agent's semaphores (`run_now_slots.rs`) exist only in the
  agent's memory: an agent restart makes every count full again, so a run its previous process left going
  is not counted until it ends and the host can briefly carry more than 16. The panel's own counter
  (`CronRunSlots`) is a separate table in each panel process, so a second panel instance multiplies the
  panel-side bound and leaves this table as the only one that spans both; it still holds the host at 16
  and an account at 2 and refuses with `AGENT_SATURATED`. Neither table is shared or persisted.
- **A restart of the agent resets the unit counter**, so a run started in the first seconds can meet a
  still-running unit of the old process with the same name; the service manager refuses the name and the
  run reports unavailable. It never collides with a live job.
- **The sweep can remove the keys of a login created and keyed between its password read and its
  removal.** It fails closed (the customer sets them again) and at start-up, where it runs, no request has
  been accepted.
- **`pkill` matches by process title, which is sshd's own convention** (`sshd:` before OpenSSH 9.8,
  `sshd-session:` from it). A future sshd that changes the title would end no session, and the call would
  answer success with zero. The polygon suite should assert a real session dies; it is not written.

## What the author could not verify

Nothing here was built or run (the owner runs one gate pass after all code). In particular: the regular
expression against a live sshd of each supported family; `systemctl stop` exit statuses on RHEL; and
`pkill --full --count` output on procps-ng. The reviewer should run a polygon session test on Ubuntu 24.04
and AlmaLinux 9 before this reaches `main`.
