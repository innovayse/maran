# A customer's own backup schedule

A customer can choose when their own account is backed up automatically (issue #147). It is the same
`backups."BackupSchedules"` row an administrator's per-account override uses; the customer's page and
the administrator's page write one row, not two.

## The API

| Method and route | Who | Answer |
|---|---|---|
| `GET /api/v1/me/backup-schedule` | a signed-in customer; an operator viewing as them may read | `200` with `schedule`, or `schedule: null` when the account has saved none |
| `PUT /api/v1/me/backup-schedule` | a signed-in customer; refused to an operator viewing as them | `200` with the stored schedule |

Both require the account's plan to include backups (`AccountFeature.Backups`). Neither route names an
account: the server takes it from the login, so there is no identifier to substitute. A caller with no
account of their own (an administrator, a reseller, an account-less login) is answered `404`, never
`403`. The administrator's `api/v1/backup-schedules` stays administrators-only.

The body is `{ frequency, hourUtc, dayOfWeekUtc, retainCount, enabled }`. It carries no account and no
destination: a body that names either has it discarded. A schedule keeps the destination an
administrator gave it, or the server's default one when it is new.

## What a customer may choose

| Setting | Customer | Administrator |
|---|---|---|
| Cadence | daily or weekly | daily or weekly |
| Hour (UTC) | 0 to 23 | 0 to 23 |
| Copies kept | 1 to 7 (`CustomerBackupLimits.MaximumRetainCount`) | 1 to 365 |
| Destination | never | any configured destination |
| Host-wide policy | never | yes |

The lists the form offers come from `GET /api/v1/backup-options` (`customerFrequencies`,
`customerRetainCounts`), so the interface holds no list of its own. An out-of-range value is refused
with `BackupScheduleCustomerRetainTooHigh`, `BackupScheduleCustomerRetainTooLow` or
`BackupScheduleCustomerFrequencyNotAllowed`.

## What the operator should know

- A save is journalled as `BackupScheduleSaved` with the customer as the actor and their account as the
  subject.
- To stop customers scheduling, remove backups from the plan, or save the account's row disabled.
- If a host-wide policy and a customer's own schedule are both enabled, the account is backed up by
  both. The administrator's per-account override behaves the same way today.
- Threat note: `docs/superpowers/notes/2026-11-07-customer-backup-schedule-threat-note.md`.
