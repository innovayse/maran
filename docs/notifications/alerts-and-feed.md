# Operator alerts, recipients and the notification feed

How the panel tells the server's operator that something needs them (issues #142, #164).
Administrators only; there is no tenant dimension, and no API key or reseller reaches any of it.

## One path for every alert

A module that alerts the operator (Monitoring, Backups, Ssl) never reads the recipients or publishes
mail itself. It builds an `OperatorAlert` (source, code, whether the condition began, ended or was a
one-off event, the thing it is about, the figure) and the localized mail text, and calls
`OperatorAlertDispatcher`. The dispatcher, in order:

1. journals the alert through the caller's own journal;
2. publishes `OperatorAlertRaised` (the alert webhook event) and `PanelNotificationRaised` (the feed
   entry), whether or not any mail address exists;
3. publishes one `SendMailRequested` per recipient, or journals `MailSkippedNoSmtp` as a success when
   there is none.

The outputs are independent publishes on local queues: a failing mail server never blocks the feed.

## Alert recipients

`GET` and `PUT /api/v1/notifications/alert-recipients`: up to 10 addresses (the cap is sent as
`maximum`, the screen holds none of its own), stored lower-cased, unique, in list order. The whole
list is replaced on every save. The first recipient is also copied into the old single-address
column for one release so a rollback finds it; nothing reads that copy.

## Alert delivery state

`GET /api/v1/notifications/alert-delivery` answers `ok`, `failing` or `unknown` for the mail and the
webhook channel. Only mail sent with `MailPurpose.OperatorAlert` moves the mail state; a failed
password-reset mail never turns it red. `unknown` means nothing has been attempted yet (a fresh
install) and is not counted as a problem. A `failing` channel is a row of its own in the dashboard's
"Needs attention" block and adds one to the navigation figure.

## The notification feed

`GET /api/v1/notifications/feed` (paged, newest first), `GET .../feed/unread-count` (capped at 99),
`POST .../feed/{id}/dismiss` and `POST .../feed/dismiss-all`. Entries come from failed background
tasks (`TaskRecorder`), server alerts and backup alerts.

- An entry stores a code and up to four short values; the sentence is worded when the feed is read, in
  the reader's language. Text is stripped of control characters and capped before it is stored.
- A dismissal is per administrator. Entries older than 90 days, and all but the newest 500, are
  removed by a daily job; their dismissals go with them.
- The destination of an entry is a name from a closed list (`NotificationDeepLinks`), never a URL.

## Thresholds

The percentages that raise the disk, processor and memory alerts are in
[alert-thresholds.md](../monitoring/alert-thresholds.md).
