# Backup alerts

What the panel tells the administrator about its unattended backups, and what it cannot yet observe
(issue #134).

## What mails the administrator

The scheduled sweep (`BackupRunHandler`, every five minutes) sends one operator alert, through the
same outgoing-mail settings and alert address as the monitoring alerts, for each of these:

| Alert | When | Journal subject |
|---|---|---|
| Scheduled run failed | a scheduled run reached the server agent and ended without an archive, or threw before it reported an outcome (`BackupScheduledRunAborted`) | `ScheduledRunFailed:<account>` |
| Destination refused | the schedule's destination could not be used (not configured, or a remote kind this agent does not serve), so no run was started | `DestinationRefused:<account>` |
| Scheduled run missed | a schedule started more than 30 minutes after its named hour — the panel was not running then, or the sweep before it was still busy | `ScheduledRunMissed:<schedule id>` |
| Backup verification failed | the re-read of a finished run's artifact found a problem with it (changed digest, refused member, disagreeing records, missing dump member); raised for scheduled and manual runs alike, see [verification](verification.md) | `VerificationFailed:<backup id>` |

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

A manual run does not mail for its own outcome: whoever asked for it is watching its task. Its
verification is different, because the finding arrives after they have stopped watching, so a failed
verification mails for manual runs too. A verification that could not run (the agent busy, saturated
or unreachable) mails nothing, because it found nothing about the artifact. A failure found by
**Verify now** is mailed once, when the backup moves into the failed state; pressing the button again
on a backup already recorded as failed mails nothing more.

## What is not covered yet

- **A remote destination refusing writes at run time.** The agent serves only the local destination
  today, so a remote target that refuses writes cannot be observed until remote destinations land
  (issue #160). The panel-side refusal above (a destination that is not configured, or of a
  kind this agent does not serve) is the whole of what is seen, and verification likewise reads only
  local artifacts.

## Test evidence

Each of the three conditions the issue names has an integration test that asserts the notification,
all under `backend/tests/Maran.Host.IntegrationTests/`:

- **A corrupted artifact** raises "Backup verification failed": `BackupVerificationAlertTests.cs`
  (a finding after a scheduled run and after a manual run, the row and its localized reason, a sound
  artifact raising nothing, a verification that could not run mailing nothing, no alert address) and
  `BackupVerificationEndpointTests.cs` (the finding mailed once, and nothing more on a repeat).
- **A missed run** raises "Scheduled run missed": `BackupVerificationScheduleAlertTests.cs` (forty-five
  minutes late is mailed, ten minutes late is not).
- **An unreachable target** raises "Destination refused": `BackupVerificationScheduleAlertTests.cs`,
  for a kind the agent does not serve and for a server with no destination configured, and with no
  alert address. A destination deleted from under a schedule is not a case: the database refuses the
  delete while a schedule names it. The part this agent cannot observe (a remote target refusing
  writes after it was accepted) is the limit above, not a test.
