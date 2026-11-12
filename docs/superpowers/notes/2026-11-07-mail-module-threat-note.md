# Mail module: a new internet-facing mail stack, new privileged agent operations, an in-house webmail (issue #177) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"; reviewer
tracking issue #331). This change adds seven internet-facing listening ports (SMTP 25/465/587, IMAP
143/993, POP3 110/995), third-party daemons that run partly as root (Dovecot's master and LMTP drop to a
customer uid per delivery), a closed set of new root agent operations that write host mail
configuration, per-customer credentials and signing keys, and a browser-facing mail reader that renders
attacker-controlled HTML. It touches the agent's privileged paths, the installer's privileged steps and
session/credential handling at once; each escalates on its own.

The work is carried out by agent sessions, which cannot obtain a second reviewer. The branch carrying
issue #177 **may land on a branch and MUST NOT merge to `main`** until a human reviewer has read this
note and checked every item under "What a reviewer must check". The owner is told at hand-off that Mail
carries an outstanding review. Nothing in this note discharges it. Written BEFORE the code, so it can
contradict a choice instead of validating one: every statement below is a requirement the code must
meet, not a description of code that exists. Where the code lands and differs, the code is wrong or this
note is amended in the same change.

**Owner decisions assumed.** The plan (`research_notes/plan-177.md`, section 3) recommends an option for
each of D1-D19. The owner has NOT approved them. This note argues the recommended option of each; if the
owner decides otherwise, the affected argument is void and is rewritten before the code lands. They are
listed at the end.

## What is being added, in one paragraph

Postfix as MTA, Dovecot (with Pigeonhole) for IMAP, POP3, LMTP and Sieve, Rspamd for spam filtering,
DKIM signing and SPF/DKIM/DMARC checks, Redis on a unix socket for Rspamd's rate limiting, ClamAV
optional and off by default (D1, D9). The installer installs them masked or disabled and NOT listening
(the FTPS shape, `installer/lib/89-ftps.sh`, `installer/systemd/maran-ftps.service`). An administrator
enables mail in the panel; the agent then renders the host configuration through the safe-write path,
starts the units and reports what the daemons actually do. Customers create mail domains (only for
hostnames of their own sites, D11), mailboxes, aliases, forwarders, a catch-all and an autoresponder.
The panel keeps the truth in a new `mail` PostgreSQL schema; the agent keeps rendered fragments on disk,
derivable from PostgreSQL and re-pushed at start-up. The webmail is a C# gateway inside the API (MailKit
over unix sockets to Dovecot and Postfix submission) plus a Vue SPA; the browser never holds a mailbox
credential. A new agent service `mail.proto` carries about fifteen closed typed rpcs, designed once up
front so the contract revision is paid a single time.

Not added: no new Maran process (the three-process rule of rules/architecture.md holds; the mail daemons
are host services driven by the agent as nginx, php-fpm and vsftpd are), no Firewall capability (Mail
reports the ports to open and does not open them, D7), no outbound call from the API or the agent beyond
what the mail daemons do on the wire, no change to the existing `Notifications` module (the panel's own
outgoing mail is a different facility) and no change to `MailAuditActions` (a new
`HostedMailAuditActions` is added beside it).

## What changes about the threat model

Before: the reachable surfaces are the panel (nginx), SSH/SFTP, optional FTPS and customer sites.
After: the host also speaks SMTP and IMAP to the whole internet, accepts attacker-chosen bytes from
unauthenticated peers into C parsers (Postfix, Dovecot, Rspamd), can send mail for the customers'
domains (so a compromised mailbox is a spam source with the operator's IP reputation), and stores
customer mail under customer uids. The panel gains a path that renders attacker HTML in an
administrator's or customer's browser session. rules/security.md item 10 ("no new listening ports, no
new daemons, no new outbound calls") is exceeded on purpose and needs the spec amendment and the rule
wording change first (D3); this note is not that amendment.

## The argument, by concern

1. **Listening ports.** None open at install; none opened by the panel (the Mail manifest declares
   `AgentCapability.Mail` only, not Firewall, not Ssl, not Files). `GetMailStatus` reads what the
   daemons actually bind from the kernel and the effective values from the daemons (`postconf -n`,
   `doveconf -n`), not the rendered config, so "mail is off" is observed, not asserted. A gate that
   only reads the files it wrote proves nothing (rules/testing.md, a check must be able to observe).
2. **Open relay and abuse.** Port 25 accepts only for local mail domains; relay only after SASL on
   587/465; `smtpd_sender_login_maps` ties the envelope and From to the authenticated mailbox's own
   addresses; a per-mailbox hourly send limit (Rspamd ratelimit on Redis, D9); message size, recipient
   and connection ceilings; a mailbox over its limit is deferred and audited; the administrator can
   suspend a mailbox. Every refusing gate has a test AND an accepting inverse control (relay refused for
   a foreign recipient from an unauthenticated peer, accepted for an authenticated sender; sender-login
   mismatch refused, matching sender accepted).
3. **Tenant isolation.** A mail domain must equal a hostname of a site in the SAME account (D11) and is
   globally unique by a database index; mail is stored in `/home/<account>/mail/...` owned by the
   account uid (D2), Dovecot serves it under that uid, Postfix never touches it (delivery is LMTP to
   Dovecot over a unix socket); every Mail entity reaches `AccountId` through a required relationship so
   `TenantScopeTests` covers it; another account's resource answers 404, never 403; alias and forwarder
   targets are validated and a catch-all matches exactly its own domain. `MailServerSettings` is a
   singleton with no account and is named in the census exemption list with its reason.
4. **Config-file injection.** Every value reaching a Postfix map, a Dovecot passwd-file, an Rspamd
   settings file or a Sieve script is a validated Rust type with a grammar (local part, domain, quota,
   selector, password hash scheme and an alphabet without `:`, autoresponder text without control
   characters and without a line beginning with `.`). Validated, not escaped (rules/security.md item
   4). The Sieve autoresponder (D18) is user text in a script language: the script is a fixed template
   with the text passed as a string literal whose grammar forbids the characters that could close it;
   if that cannot be made airtight the owner is told and autoresponders leave v1.
5. **Credentials.** Mailbox passwords are hashed in the API (Argon2id, D4); the plaintext is never
   stored or logged and the hash travels as a sensitive string (`SensitiveString`). DKIM private keys
   are generated in C# (BCL RSA-2048), stored encrypted at rest with the existing `IEncryptionService`,
   sent to the agent as a sensitive value, written root-owned 0640 to the signing daemon's group, never
   returned by any API, never in a log. The webmail master secret (D5) lives in `panel.env` (root:maran
   0640), its Dovecot-side hash is delivered through the enable rpc, and it is accepted only on the
   webmail unix-socket listeners. **It is one secret that can reach every mailbox; it is the headline
   item of this note** and gets its own section ("webmail gateway") when the gateway task lands.
6. **Suspension and deletion.** Suspending an account removes its mailbox entries from the passdb AND
   kicks live sessions (the SFTP precedent: a lock alone does not end sessions), disables Sieve forwards
   and autoresponders and defers delivery. Deleting removes mail configuration, Maildirs, Sieve files
   and DKIM keys BEFORE `userdel`, so a reused uid never inherits another customer's mail. The order is
   written into the account-deletion ordering list of rules/rust.md in the same change.
7. **Webmail.** HTML is sanitised server-side by allowlist, rendered in a sandboxed iframe without
   `allow-scripts` or `allow-same-origin` under a strict CSP, remote content blocked by default and
   loaded only on a click, directly and without a referrer, with no proxy (D10: a proxy is a new
   outbound HTTP call and an SSRF surface and would need a spec change first). Mailbox access passes the
   IDOR check (account scope, reseller envelope); messages are streamed, never read whole into memory;
   compose validates the From address against the user's own mailboxes server-side AND Postfix enforces
   it; there is a per-user send rate limit; attachments are served with `Content-Disposition:
   attachment` and never rendered inline as HTML.
8. **Disk.** Mail lives under the account home, so the existing quota and the free-space floor apply;
   per-mailbox quota via Dovecot; Postfix queue and message size are bounded.
9. **Logs and personal data.** Mail logs hold addresses; `auth_verbose_passwords=no`, no debug auth in
   the shipped configuration; retention through the existing log-retention step; an
   `IPersonalDataProvider` lists a person's mailboxes and aliases for the #146 export and erasure.
10. **Privileged code paths.** Every new rpc is a closed typed idempotent command, re-validated in the
    agent, audited (listed in `audit/mutating_rpcs.rs` or `read_only_rpcs.rs`); customer file operations
    run in `fork_as_account` through the `open_parent_directory` descriptor walk (no `resolve_in_home`
    as containment); config writes go through the safe-write sequence (render, temporary file, rename,
    validate with `postfix check` / `doveconf -n` / `rspamadm configtest` on the real tree after the
    rename, reload, rollback). No rpc executes a caller-supplied program, string or template.
11. **Agent capability.** `AgentCapability` has no `Mail` value today (current enum: Accounts, Backup,
    Cron, Db, Files, Firewall, Ftps, Monitor, Php, Sftp, Sites, Ssl, System). Adding the value and
    declaring `[AgentCapability.Mail]` in the Mail manifest is mandatory; without it
    `AgentCapabilityGuard` throws (rules/security.md item 13).
12. **View-as and tiers.** `AccountFeature.Mail` already exists (bit 0, part of `All`) and a plan can
    switch it off; the customer endpoints declare `RequiresAccountFeature(AccountFeature.Mail)`. The
    licence tier is `Included` as one constant (D14). View-as: GETs `Read`; creating a mailbox,
    changing a password, a forwarder, an autoresponder and DKIM rotation are `Refused` (each creates a
    credential, an access path or running work).
13. **Daemon units.** Packaged units are masked before the package's post-install can start them (the
    FTPS lesson: apt starts the daemon otherwise); Maran-rendered configuration; units enabled only by
    `EnableMail`; hardening drop-ins measured into `installer/systemd/unit-security-ceilings.txt` with
    `maran units`, not declared.

## What a reviewer must check

1. The relay and sender-login tests and their inverse controls, run against a real daemon in the
   polygon, not against rendered text.
2. The grammar of every validated type against the file format it is written to (Postfix map, Dovecot
   passwd-file where `:` separates fields, Rspamd settings, Sieve string literal).
3. The unix-socket-only webmail listeners and the restriction of the master-user login (which
   listeners, which users, no TCP exposure); where the master secret is generated, stored and rotated.
4. The order of operations in account deletion (mail removal before `userdel`) and the test that no
   file or configuration owned by the uid survives.
5. Maildir creation by descriptor walk inside `fork_as_account`, including the symlink-swapped
   component test; mode 0700; uid and gid checked.
6. That account B's uid cannot read account A's Maildir, and that Dovecot serves each user under its own
   uid.
7. The unit security scores in `unit-security-ceilings.txt` and what each hardening option removes from
   the daemons (LMTP uid switching needs capabilities a strict sandbox may deny).
8. That `GetMailStatus` reads daemon state, not rendered state.
9. The SQL index that makes a mail domain globally unique, and the site-ownership check of D11 (see the
   unverified item below: the current site window looks up the primary domain only).
10. The view-as declaration of every action and the 404-never-403 answer of every tenant endpoint.
11. The DKIM key path end to end: generation, encryption at rest, transport, file mode, absence from
    every response and log.
12. The HTML sanitiser policy and the hostile corpus with accept-side controls; the iframe sandbox
    attributes are set in exactly one place.
13. That no rpc, setting or template lets a caller choose a binary, an argument or a path.
14. The expand-only migration (`maran migrate guard` says `MIGRATIONS-ADDITIVE`) and the plan-column
    additions in Accounts.
15. Every added `IgnoreQueryFilters` carries its inline reason and the rules/README.md count line is
    raised with the command that regenerates it. Measured in this tree at the time of writing the
    command printed 48 for `backend/src` while the README sentence says 35: the README is stale or the
    count includes in-flight work, and a reviewer re-measures rather than trusting either number.

## Owner decisions this note assumes (recommended option, not yet approved)

- **D1** Postfix + Dovecot (+Pigeonhole) + Rspamd (+Redis on a unix socket); ClamAV optional, off by
  default.
- **D2** Per-account uid, Maildir under `/home/<account>/mail`.
- **D3** Spec section 3, section 2 principle 4, section 11 and rules/security.md item 10 are amended
  with the owner's approval; language of the amendment (Russian like the existing ones, or English)
  open.
- **D4** Argon2id for mailbox passwords, if each family's Dovecot build supports it; otherwise a
  fallback needs an explicit ruling because rules/security.md item 9 says Argon2id.
- **D5** Master-user secret held by the API, accepted only on unix-socket listeners.
- **D6** v1 computes and displays DNS records plus an Sdk read window; the DNS module (#178, open)
  consumes it later.
- **D7** Mail reports the ports to open and does not open them.
- **D8** External forwarders with SRS (postsrsd) if packages exist on all families, otherwise
  internal-only plus an administrator switch.
- **D9** Redis on a unix socket for per-mailbox outbound rate limiting.
- **D10** Remote content blocked, loaded on click, no proxy.
- **D11** Mail domain must equal a hostname of a site in the same account.
- **D12** One server hostname and its certificate from the existing panel-certificate flow.
- **D13** New plan fields (`MaxMailboxes`, default mailbox quota, hourly send limit, pooled quota
  ceiling, reseller envelope ceilings); names and defaults to confirm.
- **D14** Licence tier `Included` as one constant.
- **D15** POP3 default off, switchable per server.
- **D16** DMARC `p=none` without `rua`, SPF `~all`.
- **D17** Defaults: 50 MiB message, 100 recipients, 100 messages/hour per mailbox, 1 GiB mailbox quota.
- **D18** Autoresponder through Sieve with a strict grammar.
- **D19** New dependencies (MailKit, MimeKit, an HTML sanitiser, Argon2 support if the existing hasher
  cannot emit a Dovecot-compatible string), each pinned, justified and licence-checked.

## What the author could not verify

- Dovecot's Argon2id support (libsodium-backed scheme) in each family's package build.
- Availability, versions and unit names of Rspamd, Redis, postsrsd and ClamAV on EL10 and Debian 13, and
  which packages' post-install scripts start a daemon. The stack inventory (plan task T0.2) is not yet
  written, and the supported-systems rule forbids filling these cells from memory.
- Whether Postfix and Dovecot run correctly under Maran-owned units on both families, versus packaged
  units masked at install.
- Behaviour of Dovecot LMTP uid switching under the chosen unit hardening.
- Real outbound deliverability (reputation, reverse DNS, port-25 egress), which needs a public host and
  cannot be measured in the polygon.
- Whether the Ssl module publishes a usable certificate-changed signal for Mail. The code shows
  `CertificateAuditActions` and the FTPS reloader (`Ftp/Services/FtpsTlsReloader.cs`) reacting to
  certificate replacement, but this note did not trace the event Mail would subscribe to (D12).
- Whether the site window fits D11. `ISiteDirectory.FindByDomainAsync` is documented as reading a site
  by its PRIMARY domain, scoped to the current user and returning null for another tenant. D11 says "a
  hostname of a site", which may include secondary hostnames, and an unattended reconciler has no
  principal. A window that answers the real question may be needed in the Sdk.
- The Argon2id cost per IMAP login against Dovecot's auth cache TTL, which sets the parameter dial
  (D4 follow-up).
- The performance of the account-wide synchroniser and reconciler on a host with thousands of accounts.
- Whether the in-flight working tree changes the facts above: this note read the tree with uncommitted
  modifications present (`PROTO_VERSION` is 20 there; the plan's revision arithmetic assumes that).

## Amendment 2026-11-09 (issue #458): port 25 trusted loopback

**What was wrong.** The rendered `main.cf` set `mynetworks = 127.0.0.0/8 [::1]/128` and listed
`permit_mynetworks` in `smtpd_relay_restrictions` and `smtpd_recipient_restrictions`. The `smtp inet`
service (port 25) overrides none of them, so any process on the host, such as a customer's PHP site, cron
job or uploaded script, could open `127.0.0.1:25` or `[::1]:25` and send to any recipient on the internet
with any envelope sender and From, with no login. Concern 2 above says relay is possible only after SASL
on 587/465 and that the sender-login check and the per-mailbox send limit bind every relayed message; for
loopback the rendered configuration did not do that, and every account became an unauthenticated,
forgeable, unmetered relay under the operator's IP reputation.

**What changed.** No trust by network address. `mynetworks` is written explicitly empty (left unset,
Postfix derives it from the interfaces). `permit_mynetworks` and `permit_sasl_authenticated` are gone from
the two restriction lists of the port 25 service, which takes no login (`smtpd_sasl_auth_enable = no`):
it now runs `reject_unauth_destination` and `reject_non_fqdn_recipient` only, so it accepts mail for a
served virtual domain and refuses every other recipient, whatever address the peer connects from. Relaying
exists only on `submission` and `smtps`, which already require TLS, SASL, `reject_sender_login_mismatch`
and `permit_sasl_authenticated,reject`; the send limit keys on that authenticated mailbox.

**The panel's own mail.** The panel keeps no network exemption. Its outgoing mail uses the existing SMTP
settings (Notifications): point them at `127.0.0.1:587` with STARTTLS and the credentials of a mailbox
that owns the sender address. Port 25 on this host no longer relays for the panel, so a panel configured
for `127.0.0.1:25` without credentials stops delivering to external recipients until it is changed.

**Why it is safe now.** There is no input to the relay decision that a local process can supply without a
mailbox credential: the only relaying services demand one, and the rendered golden, a test that fails on
the old template and a test that the submission services still take a login and bind the sender (the
accepting control) pin both halves.

**Residual, not closed by this change (OUTSTANDING).** The local submission path is separate from SMTP:
a process that runs `/usr/sbin/sendmail` or `postdrop` (PHP's `mail()` does, the pool keeps it enabled)
writes into the `maildrop` queue and `pickup` delivers it with no smtpd restriction at all. That path
skips SASL, the sender-login check and the send limit too. Closing it means either removing `pickup` (and
with it PHP `mail()` for customers) or giving local submission an authenticated, metered route; that is a
product decision the owner has not taken and it is tracked separately. Not verified here: a real Postfix
refusing a loopback relay attempt, because no polygon suite for mail exists yet (the assertions are on
the rendered files). UNOBSERVED HERE: the daemon's own answer (`postconf -n`, an SMTP exchange).

Second review: OUTSTANDING (#331).

## Amendment 2026-11-09 (issue #500): local submission

**What was wrong.** The residual named under the #458 amendment. A process that runs `/usr/sbin/sendmail`
or `postdrop` (PHP's `mail()` does) writes into the `maildrop` queue and `pickup` delivers it, and no smtpd
restriction sits on that road. Postfix's `authorized_submit_users` defaults to everybody, so any customer
script could send to any recipient with any envelope sender, with no login, no `reject_sender_login_mismatch`
and no per-mailbox hourly limit.

**What changed.** `main.cf` now sets `authorized_submit_users = root, $mail_owner, <map type>:<postfix
directory>/maran_local_submit_users`. `postdrop` (which `sendmail` also goes through) refuses a user the
list does not name. The table is seeded EMPTY by `seed_mail_maps` and never rewritten once it exists, so
the state of an enabled host is closed: only root and the Postfix owner may submit locally, and no
customer process can until the mail data plane lists that account's system user. The data plane replaces
the table whole, as it does the other lookup tables. The table sits in Postfix's own configuration
directory and not under `/etc/maran` because `postdrop` reads it as the submitting customer's own uid, and
`/etc/maran` is `root:maran 0750`. Nothing about the port 25 or submission services changed (#458 stays).

**Why it is safe now.** There is no way for an account the operator has not enabled to put a message into
the queue: the gate is Postfix's own, evaluated by the setgid helper before it writes. The rendered golden,
a test that the line lists no `anyone` and no `/etc/maran` path, a test that the path follows the field and
the map type (the accepting control), a test that both families name the table in their own Postfix
directory, and the seed test (the table is created empty on a host that has none, and a populated one is
never touched) pin it.

**Residual, not closed by this change (OUTSTANDING).** For an account the data plane DOES list, a local
submission still carries no per-mailbox identity: the envelope sender is whatever `sendmail -f` or the
script chose, and nothing counts it against the account's hourly send limit. Postfix has no sender-to-user
map for the maildrop road, and the send limit itself is not rendered anywhere yet (the data plane owns it).
Closing it needs one of: a submit helper that speaks SMTP to the submission service as the account's
mailbox (so the existing login, sender check and limit apply), or a milter that sees the submitting uid.
Both are new components and a product decision, tracked separately. UNOBSERVED HERE: Postfix's own refusal
by `postdrop` (`postconf -n`, a real `sendmail` as a customer uid) and the effect of the table being
unreadable to a customer uid (it fails closed: a user the lookup cannot find is refused). No polygon suite
for mail exists, so the assertions are on the rendered files and the plan.

Second review: OUTSTANDING (#331).

## Amendment 2026-11-11 (issue #518): local submission bound to the account

**What was wrong.** The residual named under the #500 amendment: for an account the data plane lists, a
local submission carried no sender identity and no send-limit accounting.

**Decision.** A Lua rule inside the Rspamd proxy already in the milter chain (no new process), contract
`docs/mail/local-submission.md`. A submit helper with per-account SMTP credentials was rejected (owner,
2026-11-11).

**Security review finding and fix (2026-11-11).** The first design decided "this is a local submission"
by the absence of a client IP. Postfix presents `sendmail`-injected mail to the milter as a connection from
`127.0.0.1`, so the test was always false and the rule never fired: a total bypass the stub-based unit
tests could not see. The road is now a fact of the server: `pickup` uses its own `cleanup_local` service
reporting `milter_macro_daemon_name=MARAN_LOCAL`, port 25 reports `INBOUND`, the login services
`ORIGINATING`; the rule decides on that name before it reads any login, address or header, and any other or
missing name is a soft reject (fail-closed). A forged `sasl_username`/client attribute in a hand-built
queue file therefore cannot take a message off the local road (polygon case 9).

**Trust of the topmost `Received` header.** `pickup` writes `Received: by <host> (Postfix, from userid
<uid>)` itself, on top of whatever the customer wrote (`mail_name` is pinned so the text cannot drift with a
package default). The rule trusts only the topmost `Received` header, and only on the `MARAN_LOCAL` road; a
customer-forged second header sits below it and is ignored (polygon case 6). Any other shape is refused.
The uid maps to an account through `local_submit_accounts.map`; the envelope sender and `From:` domain must
both be that account's domains (`5.7.1`); the counter is shared with authenticated SMTP (`4.7.1` deferral
over the limit) and counts per envelope recipient, once per message identity (`SET NX` guard keyed on the
maildrop id in the topmost `Received` header, so a re-offered message is not counted twice); a local message is capped at the SMTP recipient limit (`5.5.3`). Exempt uids are
exactly root and the Postfix owner, rendered by the agent; no Dovecot uid submits locally. A missing or
unreadable map tempfails, and a Lua error in the prefilter is a soft reject, not an acceptance. The
installer adds no new directory or permission: the maps sit in the existing root-owned, readable
`/etc/maran/mail`, and uninstall removes them with it.

**Backscatter.** A refusal from `pickup` is bounced to the envelope sender, so `sendmail -f victim@x` would
mail a third party. Refusals whose notice would reach an unverified address (unknown submitter, envelope
sender not owned) are therefore `discard`, logged by Rspamd; a foreign `From:` header or too many
recipients (envelope sender already proven the account's own) stay `reject`. PHP `mail()` without `-f` uses
`user@<myorigin>` and is discarded; the supported configuration is `sendmail_path` with `-f`
(`docs/mail/local-submission.md`).

**Host invariants.** The uid stamp is only trustworthy while the maildrop road is sealed. The agent refuses
`SetLocalSubmission` (typed error, nothing written) when the account's system user is in the `postdrop`
(`maildrop` on SUSE) or `postfix` group or `/usr/sbin/postdrop` is not `2755` owned by root and the drop
group; polygon case 11 asserts the same on a live host. Removal deletes the uid line from all tables so a
reused uid starts unbound.

**Residuals (OUTSTANDING).** (1) UNVERIFIED on a live stack: that Postfix passes pickup's `Received:` line
to the milter as the first header, that `MTA-Name` carries `{daemon_name}`, that the `discard` action and
`task:has_pre_result` exist in the shipped Rspamd (a missing `discard` falls back to `reject`, restoring
backscatter), that higher prefilter priority runs first, that `pickup` rescans a deferred file, and which
envelope attribute records `postdrop`/`pickup` forward; the rule fails closed on a shape it does not
recognise. (2) Customer code running as the account's own uid can still send as any domain of that
account, by design. (3) Two accounts sharing one system uid cannot be told apart; the data plane maps a
uid to exactly one account. (4) The polygon images run no Postfix or Rspamd, so the eleven polygon cases
were not run there; the assertion states the gap and runs where the stack exists. (5) `postdrop`'s own
refusal of an unlisted user (#500) is separate and still unobserved. (6) **Unauthenticated `localhost:25`
is not covered**: a customer can reach port 25 from the host, claim another tenant's `From:` and receive
that tenant's DKIM signature (`dkim_signing` has `sign_local = true`, `use_domain = "header"`; Rspamd also
treats `127.0.0.1` as local), and a virtual alias to an external address makes port 25 a limitless relay.
Separate issue (client restrictions on port 25 for local addresses, a firewall rule by uid, DKIM signing
conditional on a bound sender, forbidding or counting external alias targets). (7) Maildrop files of a
deleted uid are refused by the rule at `pickup` time but not purged; purging belongs to account deletion.

**Code review fixes, agent side (2026-11-11).** (H1) The tables were read before the config-write lock was
taken, so a suspension and a domain change running together could re-render the suspended account's lines
from a stale read and bind it again. The whole read-render-write of the five tables now runs under one
mutex (`hold_tables`), taken outside `config_tree_lock` and never inside it; seeding takes it too.
(M1) A uid, a mailbox login and a sender domain are held by one account: binding a uid drops every other
account's line with that uid (the newest binding wins, the older account becomes unbound and its mail is
refused); a login or domain that another account holds is refused with the typed
`LocalSubmissionConflict` (wire code `ALREADY_EXISTS`) before anything is written, so a moved domain waits
for its previous owner's removal to land. (M4) An authenticated login missing from `mailbox_accounts.map`
is a soft reject (`4.4.3`), not an exemption; the lookup and the map keys are lower-cased. (M5) The
"counted once" key is the maildrop id from the topmost `Received` header, which survives re-offers (the
cleanup queue id does not); a message without it is counted every time. `Message-ID` is deliberately NOT a
fallback: it is the sender's own text, so repeating one would exempt a customer from the limit. (M6) A
missing or incomplete settings section registers a stand-in that soft-rejects the local road and any
unidentified path (smtpd roads pass), and after the reload the agent runs `rspamadm configdump
maran_local_submit` and refuses to tell Postfix to admit the account when the section is absent. (M7) An
emptied map is a comment line, never zero bytes. (L1) A uid that is not in the user database is refused
(`SubmitterUnknown`), not treated as safe. New residuals: the configdump check proves the reloaded
configuration holds the section, not that the running worker registered the symbol (a symbol listing needs
the controller and a password); the uid takeover leaves the displaced account's `maran_local_submit_users`
line until its own removal, which only matters to an account whose rules are gone (its mail is discarded);
M2, M3, M8 and H2 (backend, publisher) are outside this change.

Second review: OUTSTANDING (#331).

## Amendment (#531 part 3): DKIM follows the binding, aliases carry their fan-out

Surface: which mail is signed with a tenant's key, and what an alias recipient costs the hourly bucket.
Attacker: a customer process that connects to `127.0.0.1:25` (INBOUND road) and types a hosted tenant's
`From:`, or one that mails an alias with many external targets to multiply its sent volume. Controls: the
signing condition is an allow-list of two roads decided on the daemon name; the local road is signed only
with a per-message proof written by the rule after the envelope, `From:` and rate checks passed (the proof
name is one rendered setting both files read; no `task:set_user`); a message with several `From:` domains
gets no proof; the rule charges `max(1, external targets)` per recipient from `alias_fanout.map`, and a
missing map defers local mail (`4.4.3`); Postfix bounds expansion with `virtual_alias_expansion_limit`.
Could not verify: that Rspamd accepts the condition file's returned function and an empty table as "sign
as configured", that the signing callback runs after the prefilter that records the proof, `task:cache_set`
and `cache_get` semantics, and that `sign_local = true` is required for the bound local road (the design
text said `false`; with `false` Rspamd skips every 127.0.0.1 message before the condition is consulted, as
far as the module's source is remembered). Residuals: authenticated submission is not charged alias fan-out
(smtpd bounds its recipients, the expansion is bounded by the limit above); a lagging `alias_fanout.map`
undercharges until the next push.

Second review: OUTSTANDING (#331).

## Amendment (#531 part 1): port 25 refuses the host's own users at the Postfix service

Surface: the public SMTP service (`smtp inet` in master.cf) and the table the agent renders for it.
Attacker: a customer process on the server that opens its own SMTP session to `127.0.0.1:25`, `[::1]:25` or
the server's public address, which skips `postdrop`'s user table, the local-submission rule's road and its
per-account limit, and arrives as INBOUND mail. Controls: the service carries
`smtpd_client_restrictions=check_client_access,cidr:/etc/maran/mail/self_clients.cidr,permit`; the agent
renders that table (`127.0.0.0/8`, `::1/128` and every address of an interface read from the kernel's own
tables, each a `REJECT`) as a third file of the Postfix stage, so it is written with `main.cf` and
`master.cf` as one change, checked by `postfix check`, and re-rendered on every `EnableMail` (an address the
host gained makes the stage differ, an unchanged host writes nothing); unreadable address tables refuse the
enable with nothing written, because a partial list would leave an address open. Any other client falls
through to `permit` and still meets the relay and recipient restrictions of main.cf. The layers are
independent of the firewall: stopping nftables loses only the second layer, this one is Postfix's own.
Could not verify: that a `cidr:` table named after `check_client_access,` in an `-o` value is read by
`smtpd` exactly as the same words in main.cf (master.cf splits `-o` values at whitespace, so the comma form
is used), that `postfix check` rejects nothing about the table, that smtpd reads a changed table after the
reload (it opens tables at process start and `max_use` recycles processes), and that Postfix matches an
IPv6 client against a lower-case compressed `/128` entry. Residuals: an address added between two
`EnableMail` calls is open until the next call (the panel's reconcile is what repeats it); a customer
process cannot be told from a remote client when it connects through an address the host does not carry
(for example a NAT or tunnel address of another machine), which is not a local submission.

## Amendment (#531 part 2): the firewall refuses customer uids to the host's own port 25

Second layer of the part 1 lockdown, in the kernel: `table inet maran` gets an `output` chain (hook output,
`policy accept`) with two rules, `meta skuid >= UID_MIN ip daddr @self_v4 tcp dport 25 reject with tcp reset`
and the `ip6` twin over `@self_v6`. The sets hold `127.0.0.0/8` and `::1` as constants and the host's own
addresses beside them. The test is the uid RANGE and not a group, because an account's group includes the web
server. `UID_MIN` is read from the host's `login.defs` through the distro adapter (`login_defs_paths`; absent
key means the documented 1000; a value that is zero or not a number refuses the apply with
`CustomerUidFloorUnusable` instead of being guessed), and the addresses from the kernel's two address tables
through the same parsers the Postfix stage uses. Both are host facts re-read on every apply and are masked when
the ruleset file is read back, so an address leased or a `login.defs` edited since the last apply does not make
the agent's own file foreign; the output chain's shape (names, hook, policy, ports, verbs) is compared in full,
so an edited chain is refused as a foreign ruleset. A file written before this change (tail of two closing
braces) is still read and gets the chain on its next mutation. Bounds: root, the mail stack's users and every
other uid below the floor are unaffected, as is every port other than 25 and every destination other than the
host itself. Stopping nftables loses only this layer; the Postfix refusal of part 1 is independent.
Could not verify: that `reject with tcp reset` over an `inet` output chain with a set-typed `daddr` loads on
the oldest nftables of the three families, that `flags interval` accepts the loopback `/8` beside single host
addresses on every release, and the `/usr/etc/login.defs` location on the SUSE family. Residuals: an address
that appears after the last apply is not in the set until the next apply (the Postfix layer reads its table at
enable time too, so the two layers share this window).

Second review: OUTSTANDING (#331).

## Amendment (#531 part 4): the alias fan-out reaches the host from the Mail module

Surface: where `alias_fanout.map` gets its counts. Attacker: a customer mailing an alias with many outside
targets to multiply its sent volume past the hourly limit. Controls: the panel computes, from the Mail module's
own alias rows read as the system and closed over the account id, how many targets of each alias lie on a domain
the account does not host (the same test as the forwarder kind), and sends them in `SetLocalSubmissionRequest.alias_fanouts`
with the rest of the binding, so the map is replaced whole and an alias deleted or retargeted disappears from it
on the next push. The agent revalidates each entry (address grammar, count `1..=20`, no repeat, alias on one of
the account's own domains, list ceiling before any element is read) and refuses the call on the first fault, so
an alias cannot be written under another account's binding and a separator cannot split a map line. A zero
count is refused, never read as "no fan-out": an alias with no outside target is left out. The contract moved
to revision 29 with the panel's window, so an agent that predates the field is refused at the handshake instead
of silently leaving every alias charged once. Could not verify: that the oldest supported Postfix expands an
alias exactly once per target for a local submission (the count assumes it), and the agent-side render of the
map, which another change owns. Residuals: a catch-all has no address to key on, so mail to it is charged once
whatever its targets (the expansion limit bounds it); a target on another account's domain of this host counts as
outside (the safe side); a lagging push undercharges until the reconciler's next round, as before.

Second review: OUTSTANDING (#331).

## Amendment (#531 review): the authenticated road is bound too, the fan-out is the whole expansion, the normal worker is off

Surface: the From: header of authenticated mail, what a recipient costs, Rspamd's own listeners, the output
chain's life cycle. Attacker: (1) a tenant who logs in with its own mailbox, uses its own envelope sender (which
`reject_sender_login_mismatch` checks) and types another tenant's domain in `From:`; (2) a customer mailing an
alias of aliases, a catch-all or `alias+tag@`; (3) a hosted subdomain of another tenant's registrable domain;
(4) a local process posting a message to a loopback worker with a forged `MTA-Name`; (5) a host upgraded from a
ruleset without the output chain. Controls: (1) the rule binds `From:` on `ORIGINATING` as on the local road
(every `From:` domain must be one of the login's account's domains, else `reject 5.7.1`), records the same
proof, and the condition signs BOTH roads only on that proof; there is no unconditional branch left. (2) the panel
resolves the whole expansion of every alias and catch-all of the account (nested aliases, cycle guard, an
address extension cut at `+` as Postfix cuts it, catch-all keyed `@domain`, destinations counted once, capped at
the expansion limit 100, which is also the figure the agent accepts), the rule looks a recipient up as Postfix
resolves it (address, address without extension, `@domain`; a mailbox of the account is one delivery), and the
authenticated road is charged the same cost in the same per-account bucket. (3) `use_esld = false` in the signing
settings, verified on the 3.8.1 image to default to `true`: the selector map is read for the exact `From:`
domain, and the proof compares that exact domain, so a subdomain hosted by one tenant is never signed with the
parent's key. Cross-tenant subdomain policy: domain uniqueness is not changed here; with `use_esld` off a tenant
that hosts `shop.example.org` while another hosts `example.org` can sign only if a key exists for the exact
name, so the question left is DNS control of the subdomain, which the DKIM record publication already requires.
(4) `override.d/worker-normal.inc` sets `enabled = false`; verified on the 3.8.1 image that the packaged worker
listens on 11333 and that the override removes it, leaving only the proxy and controller (unix sockets in the
shipped overrides); the polygon asserts that Rspamd has no TCP listener. (5) the agent reconciles the output chain
at start (`reconcile_output_chain`): a ruleset file without it, or with other host facts, is re-rendered from the
file itself with the rules carried over byte for byte (no ports needed) and loaded, a current file whose chain is
not live is loaded, a file this agent did not write is untouched, and every failure is logged at error because the
host is then on one layer of two. The host-fact masking is validated (the uid is decimal digits fitting `u32`, the
element list is addresses and networks only), both address readers are one function that refuses an unreadable IPv4
table, and the panel's mail settings refuse `localhost` port 25 (the mail server of the machine closes it to every
local client; the panel submits on 587 with a login). Could not verify: the callback order of `sign_condition` and
the prefilter that records the proof, `task:cache_set`/`cache_get`, that Rspamd rejects at DATA with the 5.7.1
text on a milter, how a Postfix catch-all interacts with an address that is also a mailbox (the rule charges the
mailbox once), and the whole authenticated case against a live SASL login (the polygon covers the local road only).
Residuals: an alias of a second account that lists outside addresses is not followed when the first account's
alias names it (the first account's figure counts it as one delivery; it needs two accounts of one owner); a
lagging push undercharges as before; addresses that appear after startup are not in either layer until the next
enable or apply, and provider-routed prefixes are not listed (issue #535).

Second review: OUTSTANDING (#331).

## Amendment (#535): the host's addresses are followed while the agent runs

Surface: the two address collections of the port-25 lockdown, the nftables sets `self_v4` / `self_v6` of the
output chain and Postfix's `self_clients.cidr`. Attacker: a customer process that uses an address the host gained
after the last firewall change or mail enable (a DHCP lease, a new interface, an IPv6 privacy address, a block
routed to the host with `ip route add local`), because the kernel layer did not list it and the mail layer listed
only single addresses. Controls: (1) the agent reads the host's addresses ONCE per check, at start and then every
60 s (`address_watch`), through the one reader the mail stage, the firewall reconcile and the migration policy
already share, and hands that one reading to both layers, so they cannot follow different hosts; the reading is
compared sorted and de-duplicated and a layer is written only when it differs from what it was last brought to.
(2) The firewall layer replaces only the contents of the two sets with one `nft -f` transaction (flush and add per
set, checked with `nft --check` first); no rule, counter or ban is reloaded and the sets are never seen empty. (The first
version left the ruleset file as it was; the review amendment below writes it too.) `SelfAddressSets` is the single
place that decides what the sets hold. (3) The mail layer rewrites the one table through the config-write protocol,
Postfix's own `postfix check` judging it and a reload following, with the old table put back when either refuses; it
writes nothing for a host that never enabled mail (the review amendment below makes the reload one that cannot start a
stopped Postfix). (4) The reader now also lists the IPv4 ranges the routing table
marks local (`/24 universe LOCAL`, created by `ip route add local`), which the review found missing: both layers
refuse the range as a whole, and an address inside a kept range, a range inside a wider one and any range that
overlaps the loopback constant are dropped because an interval set refuses overlapping elements. (5) An unreadable
table is never read as an empty host: nothing is written and the failure is logged at error; a layer that fails is
logged at error, keeps what it has and is asked again at the next check, alone, while the layer that succeeded is not
written twice; a panic in a check is caught and logged, the loop goes on. Could not verify: the exact text the
kernel prints for a `LOCAL` prefix route on every supported kernel (the parser reads `/<length> <scope> LOCAL` under
a leaf), that `nft` flushes and refills an interval set in one transaction on the oldest supported nftables (the
polygon case 10 asserts it on a real kernel), and IPv6 routed blocks (`local 2001:db8::/48`), which
`/proc/net/if_inet6` does not list and the IPv6 set cannot hold as intervals. Residuals: an address is outside both
layers for up to one interval (60 s) after it appears; a range that contains the loopback range (`local 0.0.0.0/0`,
a host that answers everywhere) is out of this lockdown's reach; the migration source policy still compares single
addresses only, not ranges.

Second review: OUTSTANDING (#331).

## Amendment (#535 review): followed means written, a stopped Postfix is never started, the file and the kernel agree

Surface: the same two collections. Attacker: a customer process that reaches the host's own port 25 through an
address the host gained while a layer was not in a state to take it, and an administrator's "mail is off" that a
reload undoes. Findings and controls: (1) The watch used to record a layer as brought to a reading when the layer
had found nothing to do (no `self_v4` set in the kernel, Postfix not running); a layer was then "current" with the
new address in neither it nor, after Postfix started, the table. Now a layer answers followed or pending and only
"followed" is recorded; a pending layer is asked again at every tick, which is two small reads. `nft list set`
failing is told apart by its message ("No such file or directory", the locale is pinned to `C`): any other failure is
an error and is retried, never "nothing to refresh". A stopped Postfix gets the table written and checked without a
reload, so whoever starts it next starts it from the host's current addresses. (2) The ruleset file is rewritten by
the refresh through the same atomic writer a rule change uses, BEFORE the kernel gets the sets, so a later `systemctl
reload nftables` re-applies the current addresses and cannot revert the kernel to the file's older ones; a failure
between the two leaves the file ahead of the kernel, which the next attempt finishes. (3) The refresh reloads Postfix
with the plain `reload`, which the service manager refuses for a stopped unit, and not `reload-or-restart`, which starts
it; and the running check and the reload are one critical section with `enable_mail` and `disable_mail` (a new mail
lifecycle lock, taken outside the config-tree lock), so a disable cannot land between them and port 25 and 587 do not
start listening while mail is off (rules/security.md item 10(8)). (4) The uid floor and the addresses are read inside
the firewall lock, as every other firewall operation reads them, and when the host moved since the watch's reading
the fresh one is applied and reported, so an older reading cannot overwrite a newer one. (5) A chain seeded inert
because the host's facts were unreadable at install (default uid floor, empty sets) is repaired by the watch: when
anything of the chain beyond its set elements differs, the whole ruleset is re-rendered, written and loaded as a rule
change loads it, and the log says "Repaired" and not "Refreshed". (6) A `/32` route of type `LOCAL` is an address of
the host whatever its scope (`ip route add local 192.0.2.5/32 dev lo` prints `universe`), and both layers now use one
loopback filter (`HostAddresses::ranges_beside_loopback`), so a wide local route around the loopback network is
dropped by both and no longer listed by the mail table alone. Could not verify: the text `nft` prints for an absent
set on every supported nftables (a different text is an error that is retried, which is the safe direction), and that
`systemctl reload` on an inactive Postfix is refused on every supported systemd (the fake models it as documented).
Residuals: IPv6 local ranges (`ip -6 route add local 2001:db8::/64 dev lo`) are still not read, since
`/proc/net/if_inet6` lists interface addresses only and neither layer has an IPv6 range type; a host that was given
such a route by hand is outside the lockdown for those addresses. The edit and the check are not one atomic step with
the system's own reload of `nftables.service` at boot: a reboot loads the file's addresses, which are now the last
refresh's and not the last rule change's.

Second review: OUTSTANDING (#331).

## Amendment (#531 second review): the rule is loaded, the condition is real, aliases are always keyed

Surface: the Rspamd rule and condition of the local-submission binding, the panel's alias handlers and the
settings form of the panel's own mailer. Attacker: a customer process or login that wants to send as a domain it
does not own, to pay one unit for a large fan-out, or to defeat the lockdown by a rule that is not running. A live
Rspamd (3.8 to 4.2) check found three defects that made the design fail open, all fixed here: (1) Rspamd reads neither
`lua.local.d/` nor an unknown `local.d` file, so the rule was never loaded (the local road accepted unexamined); it is
now a module in `plugins.d/` with its settings included from `rspamd.conf.local`, and the condition stays outside
`plugins.d/`. (2) `sign_condition` was a path where Rspamd wants Lua source: the condition failed to load and the module
fell back to `sign_local`, signing inbound mail and foreign domains; it is now `return dofile(...)(...)`, returns the
`{ domain, selector, key }` table the module needs (or `false`), and `sign_local` / `sign_authenticated` are `false` so
a condition that cannot load signs nothing. (3) `discard` is not an Rspamd action and raised, deferring forever; it is
defined in `actions.conf` and the rule retries a refused action as `reject`. Reply texts no longer repeat the
enhanced code Rspamd prepends.

Review findings closed: N1 alias create/update/delete republish the account's binding after the commit, through the
same convergent publisher and ledger, so a new fan-out is charged at once and a removed one stops being charged. N2
every alias is a key of the fan-out map, an internal one with `0` (cost one), so it never falls through to the
catch-all; the lookup is address, then `+extension`, then the catch-all only for an address that is neither alias nor
mailbox. N3 the agent seeds a missing `alias_fanout.map` empty at start for a host that already enabled mail, so an
upgrade does not defer every message until the next publish. N4 (accepted behaviour change, documented in
docs/mail/local-submission.md): an authenticated sender can send only as a domain of its own account (5.7.1 otherwise),
which includes every alias and catch-all address of that account; the panel's mailer validator message now says so.
N5 root and the Postfix owner mail is signed with the key of its single `From:` domain when the host has one and is
unsigned otherwise; a customer is never exempt. N7 `From:` domains are compared as A-labels, an unreadable one is the
empty name and refuses the message, and a Unicode spelling is never signed. N8 the alias expansion is iterative
with the same cycle guard and cap. N9 the panel refuses port 25 of the machine itself in any spelling (loopback,
interface addresses, the host name and its FQDN); a deployment whose port 25 is a relay Maran does not manage opts in
with `Notifications__Smtp__AllowLocalPort25Relay=true`, which removes only the early refusal. N10 the installer's
firewall seed no longer fails on an unsupported distribution or an unreadable `fib_trie`: it logs at error and seeds an
inert output chain (empty address sets) that the agent's start-up pass fills in; the mail server's own refusal stands
meanwhile.

Could not verify: the real Postfix side (`{daemon_name}` in the connect macros, the pickup `Received:` placement,
backscatter on `discard`), Debian 12's Rspamd 3.4.1 and the RHEL/SUSE packages (no image exists; every tested
version agrees), and the exact IDN lower-casing of scripts beyond Latin-1, Greek and Cyrillic (an uncovered letter keeps
its case and the comparison refuses, the safe side). Residuals (OUTSTANDING): a DNS name that merely points at the host
is not recognised by the panel's early refusal (the mail server's refusal of the host's addresses is the enforcement);
account A mailing a catch-all or alias of account B is charged B's count (over-charge only); an alias of a second
account named by the first account's alias counts as one delivery; the fan-out map still lags the panel by one push.

Branch security review (issues #531-#561), amendments. The local-submission rule that is not configured (its
settings section lost) now soft-rejects the authenticated submission road as well as the local road; only inbound mail
passes, so a mailbox login is never sent unbounded and unaccounted because a settings file went missing. The mail of
root and the Postfix owner records the DKIM proof of its `From:` domain only when that domain is the host's own mail
name (rendered into the rule's settings as `host_mail_name`), so a compromised Postfix-owner process or a root job built
from user data cannot obtain a signature of a tenant's domain; with no host name rendered it records no proof. The
firewall and the account module read `UID_MIN` through one parser over one list of files, so the second layer of the
port-25 lockdown and the id checks cannot disagree about the customer range. The real-stack browser lane keeps no
Playwright trace (a trace records the sign-in request bodies and was uploaded as a workflow artifact); a failure keeps
its screenshot only.

Second review: OUTSTANDING (#331).

Second review: OUTSTANDING (#331).

## Website docs needed (for the owner)

Operator guide (enable, ports to open, PTR and port-25 egress, DNS records, DKIM rotation, the master
secret), customer guides (mailboxes, aliases and forwarders, autoresponder, mail client settings,
webmail, attachments), plan-limit reference and a security page entry for the mail stack, in en/ru/hy.
