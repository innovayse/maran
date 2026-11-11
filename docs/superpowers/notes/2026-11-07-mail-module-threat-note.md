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

## Website docs needed (for the owner)

Operator guide (enable, ports to open, PTR and port-25 egress, DNS records, DKIM rotation, the master
secret), customer guides (mailboxes, aliases and forwarders, autoresponder, mail client settings,
webmail, attachments), plan-limit reference and a security page entry for the mail stack, in en/ru/hy.
