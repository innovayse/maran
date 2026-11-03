# Backup alerts

What the panel tells the administrator about its unattended backups, and what it cannot yet observe
(issue #134).

## What mails the administrator

The scheduled sweep (`BackupRunHandler`, every five minutes) sends one operator alert, through the
same outgoing-mail settings and alert address as the monitoring alerts, for each of these:

| Alert | When | Journal subject |
|---|---|---|
| Scheduled run failed | a scheduled run reached the server agent and ended without an archive, or threw before it reported an outcome (`BackupScheduledRunAborted`) | `ScheduledRunFailed:<account>` |
| Destination refused | the schedule's destination could not be used (deleted, not configured, a remote kind this agent does not serve), so no run was started | `DestinationRefused:<account>` |
| Scheduled run missed | a schedule started more than 30 minutes after its named hour — the panel was not running then, or the sweep before it was still busy | `ScheduledRunMissed:<schedule id>` |
| Backup verification failed | the re-read of a finished run's artifact found a problem with it (changed digest, refused member, disagreeing records, missing dump); raised for scheduled and manual runs alike, see [verification](verification.md) | `VerificationFailed:<backup id>` |

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
or unreachable) mails nothing, because it found nothing about the artifact.

## What is not covered yet

- **A remote destination refusing writes at run time.** The agent serves only the local destination
  today, so a remote target that refuses writes cannot be observed until remote destinations land
  (issue #160). The panel-side refusal above (a destination that is deleted, not configured, or of a
  kind this agent does not serve) is the whole of what is seen, and verification likewise reads only
  local artifacts.

## Test evidence

Each of the three conditions the issue names has an integration test that asserts the notification:

- **A corrupted artifact** raises "Backup verification failed": the tests in
  `backend/tests/Maran.Host.IntegrationTests/BackupVerification*`, where the agent reports a finding
  about the artifact and the administrator's mail and the `AlertRaised` entry are asserted.
- **A missed run** raises "Scheduled run missed": the schedule tests in
  `backend/tests/Maran.Host.IntegrationTests/BackupScheduleTests.cs`.
- **An unreachable target** raises "Destination refused": the destination tests in
  `backend/tests/Maran.Host.IntegrationTests/BackupDestinationTests.cs`; the part this agent cannot
  observe (a remote target refusing writes after it was accepted) is the limit above, not a test.
