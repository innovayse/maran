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

## Website docs needed (for the owner)

Operator guide (enable, ports to open, PTR and port-25 egress, DNS records, DKIM rotation, the master
secret), customer guides (mailboxes, aliases and forwarders, autoresponder, mail client settings,
webmail, attachments), plan-limit reference and a security page entry for the mail stack, in en/ru/hy.
