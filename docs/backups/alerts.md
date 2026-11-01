# Backup alerts

What the panel tells the administrator about its unattended backups, and what it does not yet
(issue #134).

## What mails the administrator

The scheduled sweep (`BackupRunHandler`, every five minutes) sends one operator alert, through the
same outgoing-mail settings and alert address as the monitoring alerts, for each of these:

| Alert | When | Journal subject |
|---|---|---|
| Scheduled run failed | a scheduled run reached the server agent and ended without an archive, or threw before it reported an outcome (`BackupScheduledRunAborted`) | `ScheduledRunFailed:<account>` |
| Destination refused | the schedule's destination could not be used (deleted, not configured, a remote kind this agent does not serve), so no run was started | `DestinationRefused:<account>` |
| Scheduled run missed | a schedule started more than 30 minutes after its named hour — the panel was not running then, or the sweep before it was still busy | `ScheduledRunMissed:<schedule id>` |

Each alert is journalled as `AlertRaised` by the Backups module before the mail is requested. With
no alert address configured the mail is skipped and a second entry, `MailSkippedNoSmtp`, records
that, as succeeded. These steps are not the Backups module's own: they are
`Maran.Sdk/Alerts/OperatorAlertMailer`, the one path the monitoring alerts take too (issue #290),
writing through the Backups journal so the entries carry its actor. The module supplies only the
words of the mail.

Every alert is one event, so none is deduplicated: a schedule is stamped as run before any alert
is sent, which is what keeps the next tick from meeting the same occurrence again. A panel that was
down for several nights catches up with ONE run and one missed-run alert naming the first missed
hour.

Manual backups do not mail: whoever asked for one is watching its task.

## What is not covered yet

- **Verifying the artifact after each run** (re-reading it against its recorded SHA-256, scanning
  its members, parsing its dumps) needs a new server-agent operation and a contract revision; until
  it lands, a corrupted archive is found only by a restore, which already checks every dump's digest
  before loading it.
- **A remote destination refusing writes at run time.** The agent serves only the local destination
  today, so the panel-side refusal above is the whole of what can be observed.
