# Local submission binding (issue #518)

Mail an account submits locally (`sendmail`, PHP `mail()`, cron) enters Postfix through
`postdrop` → `pickup` → `cleanup`, which bypasses every `smtpd` restriction. This document is
the contract that binds that path to the account's own sender addresses and to the same
per-account hourly send limit as authenticated SMTP submission.

## Decision

A **milter on the non-SMTP path**, implemented as a Lua rule inside the Rspamd proxy that
already sits in `smtpd_milters` / `non_smtpd_milters` (no new process: `rules/architecture.md`
keeps three). Rejected: a sendmail-compatible submit helper with per-account SMTP credentials
(owner decision 2026-11-11).

## Which road a message came by

The road is a fact of the **server**, never of the message or the connection. Postfix presents a
message from `sendmail`/`postdrop` to the milter as an ESMTP connection from `localhost`
(`127.0.0.1`): `cleanup` pretends so for non-SMTP mail. A client address, a hostname or a login
therefore says nothing about the road, and a rule that treated "has a client address" as "SMTP"
would let every local message through unexamined (the defect the #518 security review found).

`master.cf` gives each road its own `milter_macro_daemon_name`, which Rspamd reads as the
`MTA-Name` request header:

| Service | Reports |
|---|---|
| `smtp` (port 25, no login) | `INBOUND` |
| `submission`, `smtps` (login) | `ORIGINATING` |
| `cleanup_local`, used only by `pickup` (`-o cleanup_service_name=cleanup_local`) | `MARAN_LOCAL` |

The rule decides on that name FIRST, before it looks at any login, address or header:

- `MARAN_LOCAL`: the local-submission rule below.
- `INBOUND`: not a submission, left alone.
- `ORIGINATING`: authenticated mail; the rule binds its `From:` domain to the login's account and charges the bucket (below).
- any other or missing name: soft reject (fail-closed), so a Postfix or Rspamd version that does
  not deliver the name blocks mail loudly instead of opening the local road.

Mail Postfix generates itself (bounces, notices) skips the milters (internal cleanup flags), so the
fail-closed rule does not break bounces. A message requeued with `postsuper -r` comes from the
Postfix owner, which is exempt.

## What identifies the submitter

`pickup` writes the first `Received:` line of a locally submitted message itself:
`Received: by <host> (Postfix, from userid <uid>)`, the owner `postdrop` recorded for the queue file
(the caller's real uid). It is unconditional and sits above everything the customer wrote. Postfix does
**not** show that line to a milter (Rspamd never sees it), so the uid is carried to the rule another way:
the `cleanup_local` service, which only `pickup` feeds, runs a `header_checks` table
(`/etc/maran/mail/local_submitter_headers.regexp`, rendered by the agent) that copies the
uid out of the line into a header, `X-Maran-Local-Submitter: <uid>`. The table prepends,
and pickup's line is first in the message, so that header is the **topmost** one; a header of that
name, or a `Received:` line of that shape, written by the customer sits below it and only the topmost
`X-Maran-Local-Submitter:` header is trusted, and only on the `MARAN_LOCAL` road. `mail_name` is pinned to
`Postfix` in `main.cf` because it is the text the table matches. A message on the local road without a
topmost header of exactly that shape is refused. The header stays on the delivered message; it names a
system uid and a queue id, nothing a recipient can use.

Exempt uids are exactly root (`0`) and the Postfix owner (`$mail_owner`), rendered by the agent from the
host's user database and never derived from a customer. No other user is exempt. In particular no
Dovecot or per-account delivery uid is: Dovecot delivers over its LMTP socket and nothing in the
rendered Dovecot configuration submits mail through `sendmail` (no Sieve vacation or redirect). Should
that ever change, that uid must be listed as an account in `local_submit_accounts.map` or added to the
exempt list deliberately, in the same change. The mail of an exempt uid records the DKIM proof of its `From:`
domain only when that domain is the host's own mail name (`host_mail_name`, rendered into the settings from the
mail server hostname); a trusted uid is never proven for a tenant's domain.

Verified against Postfix 3.5 and 3.10: a milter is NOT handed pickup's own `Received:` line (issue #591),
`header_checks` of the `cleanup_local` service does see it, and the header it prepends reaches the milter
first. UNVERIFIED (polygon case 1 proves it on a live stack): that the `MTA-Name` header carries
`{daemon_name}` through Rspamd, and the exact header text per Postfix version.

## Data the agent renders (`/etc/maran/mail/`, root-owned, Rspamd-readable)

| File | Content |
|---|---|
| `local_submit_accounts.map` | `<uid> <account>` for every account allowed to submit locally |
| `account_sender_domains.map` | `<account> <domain>` one line per domain the account owns |
| `account_send_limits.map` | `<account> <messages-per-hour>` |
| `mailbox_accounts.map` | `<mailbox-login> <account>` so authenticated SMTP shares the account bucket |
| `alias_fanout.map` | `<alias-address-or-@domain> <outside-delivery-count> <account>`: recipients that expand outside the server (issue #531). The third column names the owner so an account's lines are replaced whole. The lines come from `SetLocalSubmissionRequest.alias_fanouts`: the panel counts, per alias and catch-all of the account, the deliveries on domains the account does not host (nested aliases followed, a catch-all carried as `@domain`, capped at the expansion limit 100) and sends them in the same call as the domains, so the map is replaced together with the rest of the binding; the threat note amendment #531 part 4 states what this does and does not bound |

Maps are written with the safe-write protocol (whole file, validated, restored on refusal), so a torn
write is never read. An empty table is never zero bytes: it holds the comment line `# no entries`, because
Rspamd does not feed a map callback from an empty file and the filter would keep the last populated copy
(verified on live Rspamd 3.4, 3.8 and 4.2: a map holding only the comment line refreshes the filter's copy to empty; the comment is skipped by the rule's parser and by Postfix's table reader).

### One owner per key, and one lock

The five tables are read, edited for one account and written back as one critical section (a process-wide
mutex taken outside the config-write lock), so two RPCs cannot interleave a read with the other's write.
Keys belong to one account at a time: binding a uid removes any other account's line with that uid (the
newest binding wins; the displaced account is unbound and its mail refused); a sender domain or mailbox
login that another account holds (compared case-insensitively) is refused with
`MailError::LocalSubmissionConflict` (wire `ALREADY_EXISTS`), nothing written. A domain moving between
accounts therefore binds only after its previous owner's push has removed it. `maran_local_submit_users` (Postfix `authorized_submit_users`, #500) lists the same
accounts' system users; an account absent from it cannot `postdrop` at all.

## Rule (`templates/rspamd/lua/maran_local_submit.lua` + `local.d/` settings)

**How Rspamd loads it (verified on Rspamd 3.8, 3.9, 3.14 and 4.2).** Rspamd reads neither `lua.local.d/` nor a
`local.d/<name>.conf` of a module it does not ship, so the rule is installed as a MODULE:
`plugins.d/maran_local_submit.lua` (the file name is the module name), and its settings are included by a section of
`rspamd.conf.local` (`maran_local_submit { .include "$LOCAL_CONFDIR/local.d/maran_local_submit.conf" }`) that the agent
renders. `maran_dkim_condition.lua` stays out of `plugins.d/` (Rspamd would try to load it as a module). `discard`
is not a built-in Rspamd action: `local.d/actions.conf` defines it (`discard { flags = ["no_threshold"]; }`), and a
`discard` the Rspamd in use cannot apply is retried as a `reject` by the rule itself (`pcall` around
`set_pre_result`). Rspamd's milter prepends its own class and enhanced code to the reply text (`451 4.7.1` for a
soft reject, `554 5.7.1` for a reject), so the texts below are the rule's message and not a status code: a refused
message reads `554 5.7.1 sender not owned by account`, a deferred one `451 4.7.1 local submission maps unavailable`.

On the `MARAN_LOCAL` road:

1. Topmost `X-Maran-Local-Submitter` uid, then account via `local_submit_accounts.map`. An unrecognised shape or an
   unknown uid (not root / `$mail_owner`) is **discarded** (see "Refusals and backscatter").
2. The envelope sender domain must be one of the account's `account_sender_domains.map` entries, else
   **discard** (`sender not owned by account`). Every `From:` domain must be too (and there must be
   one), else **reject** (`sender not owned by account`).
3. More than `max_recipients` envelope recipients (the SMTP recipient limit, 100): reject (`too many recipients`).
   `smtpd_recipient_limit` does not apply to `pickup`, so the rule enforces it.
4. Rate limit: bucket `maran:send:<account>` in the existing Redis, one counter for local and
   authenticated (via `mailbox_accounts.map`) mail. A message costs **one per envelope recipient**
   (`INCRBY`), not one per message; a recipient that expands outside costs **its outside delivery count**
   from `alias_fanout.map` instead (EVERY alias of the account is a key there, one that stays inside the account with
   `0`, which costs one like a mailbox, so an internal alias never falls through to the catch-all; an address that
   is neither alias nor mailbox takes the catch-all. Postfix expands after the filter counted it once, and
   `virtual_alias_expansion_limit = 100` bounds the expansion). The recipient is looked up the way Postfix
   resolves it: the address, then the address without its `+extension`, then the domain's catch-all
   (`@domain`); a mailbox of the account is one delivery. The authenticated road pays the same cost into the
   same per-account bucket. Over the hourly limit: soft reject (`hourly send limit reached`, deferred).
5. A message that passed every check above records, on the message, the proof that its `From:` domain
   is bound to the submitting account (`task:cache_set`, under the name the rendered settings call
   `bound_domain_key`); only a message with exactly one `From:` domain gets one. The DKIM signing
   condition reads it back (below).
6. A missing or unreadable map, `alias_fanout.map` included: soft reject (`local submission maps unavailable`; `milter_default_action = tempfail` covers Rspamd
   being down).

Authenticated SMTP (`ORIGINATING` with a login) shares the bucket through `mailbox_accounts.map`. Postfix
bound only the ENVELOPE sender to the login; the rule binds the `From:` header too: every `From:` domain
must be one of the login's account's domains (`account_sender_domains.map`), else **reject** (`sender not
owned by account`), and a message that passes records the same bound-domain proof as the local road.
The login is lower-cased on both sides, and a login the map does not contain is a soft reject (`mailbox not bound to an account`)
(fail-closed): a mailbox exists only inside an account, so an unknown one means the map lags or is stale.

A settings section that is missing or lacks a map path does not skip registration: the rule registers a
stand-in prefilter that soft-rejects on the local road, on the authenticated submission road (`ORIGINATING`) and on any
unidentified path, and passes only inbound mail (`INBOUND`). `enabled = false` is the only way to switch the rule off.

### Retries and counting once

A deferral (`4.x`) from the milter leaves a locally submitted message in the maildrop queue and
`pickup` offers it again on a later scan under a **new** queue id (verified: polygon case 10), so no
queue id or other per-offer value can name the message. Counting is therefore not keyed on the message at
all: the script charges the bucket only when the message fits (`current + cost <= limit`) and otherwise
leaves it untouched and answers the deferral. A message that is still over the limit when retried is
deferred again without growing the bucket until the hour rolls over, and one that fits is charged once, when
it is accepted. A message the milter defers for another reason after it was charged is charged again on the
retry, which only over-counts. `Message-ID` is never used: the sender controls it.

### Refusals and backscatter

A refused message from `pickup` is bounced by Postfix to its envelope sender (UNVERIFIED: that
`pickup` sets the bounce flag on a milter reject). With `sendmail -f victim@example.net` that would mail
a delivery failure notice to a third party the customer chose. So a refusal whose notice would reach an
unverified address is a **discard** (`discard` action: dropped, logged by Rspamd with the reason, no
notice): an unknown submitter and an envelope sender the account does not own. A refusal whose envelope
sender is already proven to be the account's own (a foreign `From:` header, too many recipients) is a
**reject**, whose notice goes to the account itself. If this Rspamd cannot apply `discard`, the rule
falls back to `reject` (a refusal never degrades to acceptance), which restores the backscatter; polygon
case 2 fails in that situation so it is noticed.

`sendmail` returns before `pickup` runs, so no refusal can reach the calling script; PHP `mail()` returns
true for a message that is later discarded. The customer learns of it from the notice (rejects) or the
account's mail log.

### Failing closed

Any Lua error in the prefilter is a soft reject, never an acceptance (without this, Rspamd
carries on scanning and delivers). The prefilter runs at the highest priority (`100`; verified on Rspamd 3.4, 3.8 and 4.2 with an
ordering probe: higher numbers run first) so no settings or whitelist entry for `127.0.0.1` is reached before it.

## PHP `mail()` and the supported configuration

PHP `mail()` without a configured sender runs `sendmail -t -i` with no `-f`, so the envelope sender is
`<user>@<myorigin>` (the server's host name), a domain no account owns. **Every such message is
discarded.** The supported configuration sets the sender explicitly, in the account's PHP
configuration:

```
sendmail_path = "/usr/sbin/sendmail -t -i -f noreply@your-domain.example"
```

(or a framework mailer that passes `-f` / `additional_params`, or sends over authenticated SMTP). The
address must be on a domain the account owns. The panel's mail guide states this for customers.

## Agent and API

- Proto (mail.proto): `SetLocalSubmission(account, domains[], send_limit_per_hour,
  mailbox_logins[])` (the agent reads the system uid from the account's system user) and `RemoveLocalSubmission(account)` — the minimal data-plane inputs #518
  needs; validated types in agent-core; maps written with the safe-write protocol, then
  `postmap` (for `maran_local_submit_users`) and an Rspamd reload.
- `SetLocalSubmission` first checks the host invariants the uid stamp rests on and refuses with a typed
  error (`MailError::UnsafeLocalSubmission`, wire code `SYSTEM_FAILURE`, nothing written) when:
  the account's uid is not in the user database the agent reads (a directory-service user cannot be judged);
  the account's system user is in the `postdrop` (`maildrop` on SUSE) or `postfix` group, as primary or
  supplementary group (it could write the queue or reach the milter socket without the stamp); or
  `/usr/sbin/postdrop` is not mode `2755` owned by root and the drop group (a writable copy or another
  setgid helper opens the queue); or the drop group is not in the group database.
- After the Rspamd maps are written and the reload succeeded, `SetLocalSubmission` runs
  `rspamadm configdump maran_local_submit` and refuses (`UnsafeLocalSubmission`, hazard `RuleNotLoaded`) when
  the output lacks the rendered settings; Postfix's `maran_local_submit_users` is written only after this check,
  so `postdrop` never admits an account whose rule is not loaded. UNVERIFIED: that the command prints the
  section of a reloaded configuration in this form, and that it reflects the worker's registered symbol and
  not only the files on disk.
- `RemoveLocalSubmission` deletes the account's lines from all five tables in the order that stops
  `postdrop` admitting it before the rule forgets it, so a system uid reused after the account is deleted
  starts unbound. Files the account already left in the maildrop queue are not touched by it: they are
  judged against the tables as they stand when `pickup` reads them (so a removed account's backlog is
  refused). Purging the maildrop files of a deleted uid belongs to the account-deletion operation and is
  NOT implemented by this change (OUTSTANDING; the map removal above already makes such files refused).
- Backend Mail module pushes them on account create/suspend/resume/delete, domain add/remove,
  mailbox add/remove and plan send-limit change. Suspension removes the account.
- The agent cannot list what it holds, so the panel keeps a ledger (`mail.MailSubmissionBindings`, one row per
  system user: current, out of date, withdrawn, or removal owed). The binding is built from the account's rows
  read as the system (a reseller, an API key or a job is shown no rows by the tenant filter, and an empty binding
  would replace the real one). A push that fails after a committed change marks the user out of date; a scheduled
  reconcile (five minutes) pushes it again. Enabling mail pushes every account that is not suspended and removes
  the bindings of users that are suspended or no longer exist (turning mail off does not clear the maps). A
  `RemoveLocalSubmission` answered with NotFound is a success. Changes of one user take turns, and a user whose
  binding was withdrawn is not bound again by a domain or mailbox change, only by the resumption.

## DKIM signing follows the binding (issue #531)

`dkim_signing` no longer signs by connection address (a customer reaches `127.0.0.1` as easily as the
server does) or by the `From:` header. `sign_condition` is Lua SOURCE (not a path):
`return dofile("<rspamd dir>/lua.local.d/maran_dkim_condition.lua")(...)`, run for every message. The condition
decides on the daemon name: `ORIGINATING` (authenticated submission) and `MARAN_LOCAL` are signed only when the rule
above recorded a bound domain equal to EVERY `From:` domain (the module's own multiple-From checks are skipped under
a condition); `INBOUND`, a missing or an unknown daemon name is never signed; a `From:` domain in its Unicode
spelling is not signed (the proof and the key files are named by the A-label). With a condition the module signs
with only what it returns, so the condition reads the selector map and key path from the module's own settings and
returns `{ domain, selector, key }`, or `false` to leave the message unsigned (an empty table is an error and signs
nothing). `sign_local` and `sign_authenticated` are rendered `false` as the fail-closed fallback: if the condition
ever fails to load the module signs nothing, where the earlier bare path (read as Lua source, which failed to load)
made it sign every message from `127.0.0.1`. What "fails to load" does depends on the Rspamd major version (verified on 3.4, 3.8 and 4.2 with a deliberately broken condition file): on 3.x Rspamd starts, accepts every road and signs nothing (the log names `maran_dkim_condition.lua`); on 4.x Rspamd does not start at all because the file is read while the configuration is parsed, so the milter socket is gone and Postfix queues all mail (`milter_default_action = tempfail`). Both fail closed. The agent's Rspamd stage runs `rspamadm configtest` on the written files before it reloads Rspamd and puts the previous files back when it refuses (`converge_mail_stage`), so a broken file is not meant to go live; UNVERIFIED: that `configtest` itself refuses a broken condition file on 3.x, where the file is only loaded when a message is signed (on 4.x the parse error is the one that stops the daemon, and `configtest` reads the same configuration). `use_esld = false`: the key is read for the exact `From:` domain, never
for its registrable parent, so a hosted subdomain cannot sign with its parent's key. The packaged normal worker
(a loopback TCP port that takes the road from a request header) is switched off by
`override.d/worker-normal.inc`.
Closing port 25 to local customer processes is the other part of #531: the Postfix service refuses a client whose address is one of the host's own (`self_clients.cidr`, rendered by the agent from the kernel's address tables with every `EnableMail`; the firewall's output rule is the second layer).

## Tests

Goldens for the Lua rule's settings, `master.cf` and `main.cf`; Lua unit tests (pure Lua, stub input, run
by the templates golden test) including the local road with a client address present, forged login
attributes, unknown and missing daemon names, recipient cost and cap, discard versus reject, and the
the unknown-login refusal and the unconfigured stand-in; agent tests for the host invariants, key conflicts, the comment-line empty table, the loaded-rule check and the table lock; polygon (`mail-local-submit.sh`, twelve cases): local
`sendmail` as an allowed account with its own domain is accepted, with another account's envelope sender
discarded, with another account's `From:` rejected, as an unlisted uid refused, the N+1th message in an
hour deferred, a forged `Received` line and submitter header ignored, the recipient cap enforced, the default PHP
sender refused, a hand-built `postdrop` stream with forged `sasl_username`/client attributes still
refused, a retry not counted twice, the drop-group and `postdrop` invariants, and the last account removed (comment-only maps refresh the rule).

Threat note: `docs/superpowers/notes/2026-11-07-mail-module-threat-note.md`, amendment #518 —
Second review: OUTSTANDING (#331).
