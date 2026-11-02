# Running a scheduled task now (issue #158)

"Run now" on a scheduled task asks the server agent to start that entry immediately and waits for it.

- **As whom.** The service manager runs the entry's own command file with `/bin/sh` under the account's
  uid and gid, in the account's `user-<uid>.slice`, so the plan's CPU, memory and process ceilings
  apply. The crontab's managed environment is given to the job as cron gives it. Nothing about what
  runs travels in the request: it names an entry the agent installed.
- **Limits.** The run is stopped after 120 s (`RuntimeMaxSec`), and stopped with no output kept when
  either stream passes 64 KiB. The agent's own client deadline is 180 s and the panel waits 200 s,
  under the reverse proxy's 300 s read timeout. Change them together.
- **Who may.** Anyone who may manage the entry. A disabled entry runs; a suspended account is refused
  (`CronEntrySuspended`, 409) by the panel and again by the agent.
- **History.** Each run is a row in `cron.CronRuns`: start, duration, exit code, outcome, and both
  streams. The newest 20 per entry are kept; the rest, the entry's runs on removal, and an account's
  runs on deletion are removed. Runs started by cron on the schedule are not in it.
- **Audit.** `CronEntryRun` with the entry id; never the command or the output.

Threat note: `docs/superpowers/notes/2026-11-01-cron-run-now-threat-note.md`.
