# In-house webmail (issues #190, #191, #192, #193) — threat note

Status: **Second review: OUTSTANDING** (tracking #331; rules/security.md "Sensitive change
escalation"). This change adds, in one move, a secret that opens every mailbox on the host, a renderer
for bytes chosen by strangers inside the session of an administrator, a reseller or a customer, and a
send path. It is credential handling, a tenant boundary and attacker-controlled rendering at once. The
work is carried out by agent sessions, which cannot obtain a second reviewer. The branches **may land on
a branch and MUST NOT merge to `main`** until a human reviewer has read this note and checked every
item under "What a reviewer must check". The owner is told at hand-off that webmail carries an
outstanding review. Written BEFORE the code, so it can contradict a choice instead of validating one.

Every decision marked **W1..W14** below is the plan's recommended option and is **not yet approved by
the owner**; this note assumes the recommendation and says where the argument changes if the owner
chooses otherwise.

## What is being added, in one paragraph

A Vue reader and composer in the panel, and a C# gateway inside `maran-api` (the Mail module, planned
by issue #177) that opens IMAP and SMTP-submission sessions to the host's own Dovecot and Postfix
(MailKit/MimeKit), on this host only, on behalf of a mailbox the caller may act on. The browser never
holds a mailbox credential. The gateway renders messages: it picks the body, decodes the charset,
sanitizes HTML by allowlist on the server and hands the result to a sandboxed iframe. It also sends: it
validates the From address, builds the MIME message, submits it and appends a copy to Sent. No new
daemon, no new listening port (W1), no agent rpc, no proto change. The only new table is an index of
staged uploads (#193).

## State of the tree this note was checked against

Read on 2026-11-07, because the draft in `research_notes/plan-190-193.md` was written against a tree and
a tree moves:

- **Nothing of Mail exists.** `backend/src/Maran.Modules/` holds no `Mail` folder (measured with `ls`).
  The module, `MailDbContext`, `Mailbox`, `MailAuditJournal` and the Dovecot/Postfix side (master-user
  login, webmail-only listeners, sender-login map) are issue #177's work and are **dependencies of this
  note, not facts**. Every claim below about "Dovecot accepts the master login only on ..." is a
  requirement placed on #177, unobserved here.
- `AccountFeature.Mail` exists (`Maran.Sdk/Contracts/AccountFeature.cs`, "No module yet").
  `ViewAsAccess` has the `Read` and `Refused` shapes used below. `IdempotentAttribute`,
  `RequiresAccountFeatureAttribute`, `EmailAddressRule` and `MailHeaderTextRule` exist
  (`Maran.Sdk/Filters/`, `Maran.SharedKernel/Utilities/Mail/`).
- MailKit 4.18.0 is pinned in `backend/Directory.Packages.props`. Its comment says it is "reached from
  exactly one file (Maran.Modules.Monitoring/Services/SmtpMailer.cs)". That sentence is already stale
  (the file lives in `Maran.Modules.Notifications`, and `grep -rln "MailKit" backend/src` finds only
  that module's csproj and `SmtpMailer.cs`) and becomes false when Mail uses the package. Correcting it
  belongs to the first coder task, in the same change (rules/architecture.md: a comment describing code
  that does not exist is a defect). The existing mailer never attaches a `ProtocolLogger` (grep finds
  none in `backend/src`).
- No HTML sanitizer is pinned (`Directory.Packages.props`) and `frontend/package.json` holds no
  rich-text editor. Both would be new dependencies (security.md item 11; W5, W6).
- The panel's own headers refuse framing and remote images: `SecurityHeadersExtensions.cs` and
  `installer/nginx/maran.conf` send `default-src 'self'`, `img-src 'self' data:` and
  `frame-ancestors 'none'`. That drives the rendering design (concern 6) and W3.
- `installer/systemd/maran-api.service` allows `AF_UNIX AF_INET AF_INET6`, so a unix-socket `connect()`
  to Dovecot needs no unit change. Whether the unit's file-system sandbox lets the API reach the
  Dovecot/Postfix socket paths is **not verified** (the paths do not exist yet).
- `Maran.Host/RateLimiting/` has one named policy per file and none for mail.

## What changes about the threat model

Before: mail content is reachable only by IMAP/POP clients that hold a mailbox password. After: one
process, `maran-api`, holds a secret that can open ANY mailbox on the host, and it decodes and renders
bytes chosen by anyone who can send mail to a customer.

## The argument, by concern

### 1. The master credential (the headline item)

Opening a mailbox on a customer's behalf needs a secret that Dovecot accepts for every mailbox (a master
user, #177). It lives in `panel.env` (root:maran 0640), never in the database, a log, an audit row, a
URL or an API response, and is held as a `SensitiveString` (security.md item 8). Dovecot must accept the
master login only on a webmail-only local listener, not on the public IMAP ports; Postfix must accept the
webmail submission only on a local listener (W1: unix sockets recommended, which add no listening port and
match security.md item 10; loopback TCP would be a new listening socket and needs a spec change first).
The endpoint options type refuses any non-loopback address at startup. MailKit's `ProtocolLogger`, which
writes credentials, is never attached, and a test asserts the client is created without one.

**What this does NOT fix:** an attacker who owns `maran-api` can read every customer's mail through this
path. That was already a total compromise of the panel; it now also includes mail content. Stated, not
hidden. A secret that opens every mailbox is also a new thing for `maran collect-diagnostics`, backups of
`panel.env` and any support bundle to keep out of their output: not yet audited.

### 2. Mailbox IDOR

The IMAP user name is never taken from the request. The route carries a mailbox id; the handler loads
the `Mailbox` through `MailDbContext`, whose query filter shows only rows of accounts the caller may act
on (customer own, reseller envelope, administrator all), and derives the user name from the row, in ONE
place (`WebmailMailboxAccess`). Another account's mailbox answers 404 (`ErrorType.NotFound`), never 403.
A suspended or disabled mailbox, or one of a suspended account, is refused with the same not-found shape.
The folder name and the UID come from the client: a folder must equal a name the server returned for
THIS mailbox, a UID is meaningful only with its UIDVALIDITY (mismatch is a Conflict), and neither is ever
concatenated into an IMAP command (MailKit's typed API only, search text included). `TenantScopeTests`
must reach the new staging entity through its required `MailboxId` relationship.

### 3. An operator reading a customer's mail

Allowed by the issues (administrator; reseller within the envelope), per W2. Every open by anyone other
than the mailbox owner is journalled (`WebmailMailboxOpenedByOperator`: operator, mailbox, account; no
subject, no addresses, no content). View-as (an operator acting as a customer login) is
`ViewAsAccess.Refused` on EVERY webmail action, reads included: looking at a customer's screens must not
become reading their private correspondence. Sub-users and memberships are refused unless #177's grant
model gives them an explicit mail grant. **Not stated by the plan, raised here:** a reseller reading a
customer's mail is a privacy and contractual question, not only a technical one, and the operator-open
journal records the fact but does not stop it.

### 4. Sending as someone else

The From address must be an address of the opened mailbox (its own and, per W8, its aliases), checked in
the gateway AND enforced again by Postfix (`smtpd_sender_login_maps`, #177): two independent layers, each
with a test that proves it refuses and an inverse control that proves it accepts. The envelope sender is
the validated From. The client cannot set any header outside the whitelist (To, Cc, Bcc, Subject,
In-Reply-To, References, Reply-To). Every header value passes `MailHeaderTextRule` (no CR, LF or control
characters) before MimeKit sees it (security.md item 4), every address passes `EmailAddressRule`. The
From check is tested against a spoofing table: a foreign address, case and Unicode normalization
differences, display-name tricks, a quoted local part containing `@`.

### 5. Abuse as a spam source

Per-user send rate limit (minute and hour buckets, proposed 20/min and 100/hour per user+mailbox, W9), a
recipient cap (100), message and attachment size and count caps (W7), the MTA's own per-mailbox hourly
limit (#177), and `[Idempotent]` on send so a double-click or a retry cannot mail twice. A mailbox that
hits a limit is refused with a typed error and journalled. The webmail layer is a second wall, not the
first: the MTA's limit holds for a mailbox that is attacked directly on submission.

### 6. Attacker HTML (the XSS argument)

Layers, each to be tested alone:

a. server-side allowlist sanitization (tags, attributes, URL schemes `http https mailto tel`, inline CSS
   by property allowlist; `<script>`, event handlers, `<form>`, `<iframe>`, `<object>`, `<embed>`,
   `<svg>`, `<math>`, `<base>`, `<meta>`, `<link>` removed; `<style>` blocks dropped per W4; `javascript:`
   and `data:` URLs removed; a CSS value containing `url(`, `image-set`, `expression`, `@`, a backslash
   escape, `behavior` or `-moz-binding` is dropped);
b. the result is serialized once and never re-parsed (mutation XSS needs a re-parse);
c. the server wraps the fragment in a document with a
   `<meta http-equiv="Content-Security-Policy">` of `default-src 'none'; img-src data:; style-src
   'unsafe-inline'`;
d. the iframe is sandboxed WITHOUT `allow-scripts` and WITHOUT `allow-same-origin`, so a document with a
   live `<script>` runs nothing and has an opaque origin (it cannot read the SPA's storage); the
   `sandbox` value is written in exactly one file (`UiSandboxedFrame.vue`, W11);
e. the panel's own CSP is inherited by a `srcdoc` document, so no remote image can load in the frame.

Why `srcdoc` and not a `src=` iframe: the panel's `frame-ancestors 'none'` and `X-Frame-Options: DENY`
would refuse a panel URL, and an iframe cannot send the `Authorization` header, leaving only a credential
in a URL, which security.md item 8 forbids. The reader therefore fetches the sanitized document as JSON
and gives it to the frame. That `srcdoc` is not refused by `frame-ancestors`, and that it inherits the
parent CSP, are **argued from the specifications and unobserved** (see "could not verify"). A Playwright
spec feeds the frame a hostile document directly, bypassing the sanitizer, to prove layers c to e hold
when a fails. vue.md currently forbids `v-html`, `innerHTML` and building markup from strings; a
`srcdoc` attribute is such a string, so the design needs a rule amendment (W11), applied by the owner.

### 7. Tracking pixels and remote content

Every remote reference (`src`, `srcset`, `background`, `poster`, CSS `url()`, `@import`, `image-set`) is
removed and COUNTED; the reader says "N remote items blocked". Plain-text messages get no auto-loaded
anything. Read receipts (`Disposition-Notification-To`) are never answered. Whether the reader may load
remote content on click is W3: because `srcdoc` inherits `img-src 'self' data:`, that means a server-side
fetch inlined as `data:` (a NEW outbound HTTP call, which security.md item 10 forbids without a spec
change first, plus an SSRF guard that refuses non-public addresses, pins the resolved address, caps bytes
and time, and follows no redirect to a non-public address) or not shipping it. **The recommendation is
(c): v1 ships with remote content blocked and no load option.** This note's argument assumes (c); if the
owner chooses (a), a separate addendum is required BEFORE that code, because the fetcher is a new SSRF
surface in the unprivileged panel process with its own review.

### 8. Phishing links

Allowed schemes only; every anchor gets `rel="noopener noreferrer nofollow"` and `target="_blank"`; when
the visible text looks like a URL or domain whose host differs from the `href` host, the sanitizer
appends the real destination host in visible text. The frame is sandboxed with `allow-popups
allow-popups-to-escape-sandbox` only (W13), no top navigation. What it cannot do: stop a customer
clicking a convincing lookalike domain, or an IDN homograph the disclosure shows in a form the reader
cannot tell apart (not addressed; the host should be shown in punycode, to be decided by the coder).

### 9. Attachments

Downloads are always `Content-Type: application/octet-stream`, `Content-Disposition: attachment;
filename*=UTF-8''<sanitized>` (path separators, control characters, bidi overrides such as U+202E and
over-long names removed), `X-Content-Type-Options: nosniff`, streamed from the IMAP part without
buffering. Nothing is rendered inline except raster `cid:` images (png, jpeg, gif, webp; size-capped;
converted to `data:` server-side; never SVG). Uploads are streamed to a staging file under a directory
the API owns, created exclusively with mode 0600; **the user-supplied file name is DATA in a database
row, never part of a path** — the path is derived from a server-generated row id only (security.md
item 2). Size is counted while streaming and the upload is aborted past the cap, a per-user staged-bytes
quota bounds disk use, and a job deletes expired staging rows and orphan files. Sent attachments are not
scanned by the webmail; the MTA's filtering (#177) is the control. Which unit-file `ReadWritePaths`
entry the staging directory needs is not yet decided; adding one changes the unit's measured security
ceiling and is the installer owner's call.

### 10. Denial of service by message shape

Bounds, all configuration with a validated range: message size read for rendering, MIME depth, part
count, header size, decoded text cap (truncated with a visible notice), sanitizer input cap, IMAP command
timeout, IMAP SEARCH BODY time and result caps (W14), concurrent gateway sessions per user and in total.
Nothing is read into memory whole: bodies and attachments are streamed and the renderer reads at most the
cap. The first cut opens one IMAP connection per request, closed in `finally`; a pool would be a hard-capped
singleton and is considered only after measuring Dovecot's auth cost.

### 11. Secrets and personal data in logs and audit

Subjects, addresses, bodies and file names are not logged. Audit entries carry ids and counts only
(`WebmailMessagesMoved`, `WebmailMessagesDeleted`, `WebmailMessageSent`, `WebmailDraftSaved`,
`WebmailMailboxOpenedByOperator`, `WebmailAttachmentDownloaded`), written only through the module's
journal. A sweep test plants a known subject and proves it would find it in an audit row (positive
control). MailKit exceptions can carry server text; error mapping must not return it to a customer.

### 12. CSRF

The panel authenticates with a bearer header, not an ambient cookie, on these routes; there is no
cookie-authenticated mutating route.

## What a reviewer must check

1. The IMAP user name is derived from the loaded `Mailbox` row on every code path, and from nothing the
   client sent; the IDOR test (customer A to B, reseller A to B) answers 404 and has an accepting inverse
   control that fails if the gate is mutated to refuse everything.
2. The master secret: held as `SensitiveString`, present only in `panel.env`, absent from logs, audit
   rows, API responses, diagnostics bundles; no `ProtocolLogger` anywhere in `backend/src`.
3. The Dovecot side (a #177 deliverable): the master login is accepted only on the webmail-only local
   listener, never on the public IMAP ports; the Postfix webmail submission is local only. Read the
   rendered config, not this note.
4. `WebmailOptionsValidator` refuses a non-loopback endpoint, and the fixture does not weaken product code.
5. The sanitizer allowlist against the hostile corpus and the corpus's accept-side controls; the corpus
   test asserts how many hostile samples it saw (vacuity guard); the sanitized output is never re-parsed.
6. The `sandbox` constant in the one file that writes it contains neither `allow-scripts` nor
   `allow-same-origin`; the Playwright spec proves the sandbox alone holds with the sanitizer bypassed,
   in all three browsers.
7. The From check in BOTH layers (gateway policy and Postfix sender-login map), each with its inverse.
8. The filename sanitizer, and that no staging path ever contains client text; an oversize upload leaves
   nothing on disk.
9. `ViewAsAccess.Refused` on every webmail action (`ViewAsAccessDeclarationTests`); `BoundCommandGuardTests`
   covers the route-stamped mailbox id; the Sdk error-kind census decides every new error code.
10. Downloads: always octet-stream plus attachment plus nosniff, including for an `.html` attachment.
11. Rate-limit policies exist for reads, sends and uploads, with 429 tests and inverses.
12. The `IgnoreQueryFilters` count is unchanged by this work (rules/README.md; re-measure with the
    command given there, do not trust the number).
13. If W3 = (a): the SSRF fetcher has its own addendum and review before any code merges.

## What the author could not verify

- **Nothing runs.** No Mail module exists, so no part of this was built or tested; this note is a
  design argument over a plan, checked against the current tree only for the facts listed under "State of
  the tree".
- Dovecot on a unix-socket login listener with a master user, and Postfix submission over a unix socket
  with SASL pass-through of a master login, on every supported family (Ubuntu 22.04/24.04, Debian 12/13,
  Alma/Rocky 9/10, x86_64 and aarch64). Whether the web-facing split (master login local only) is
  expressible in each distribution's packaged Dovecot version.
- Whether `maran-api.service`'s sandbox lets the API reach the mail sockets and the staging directory.
- The chosen sanitizer library's behaviour on mutation-XSS corpora other than the one shipped, and
  whether its default policy can be made restrictive enough without forking it.
- `srcdoc` CSP inheritance and `frame-ancestors` behaviour in Chromium, Firefox and WebKit; the sandbox
  without `allow-same-origin` and its effect on `allow-popups-to-escape-sandbox` links in each browser.
- Throughput of one IMAP login per request against Dovecot's auth cache, and the message-list query on a
  50 000-message mailbox.
- Whether a `data:` URL converted from a `cid:` image can carry anything dangerous after raster-only
  filtering (image decoder bugs are outside this note).
- That the MTA's sender-login map can express "mailbox address plus alias addresses" (W8) in the
  generated config #177 will produce.

## Owner decisions this note assumes (plan recommendations, NOT yet approved)

- **W1** Unix sockets to Dovecot and Postfix, no new listening port (loopback TCP would need a spec change
  to security.md item 10; confirm #177 can listen on sockets on every family).
- **W2** Customer reads own mailboxes; reseller and administrator read any mailbox in scope, every open
  journalled; view-as `Refused` everywhere; sub-users refused unless granted.
- **W3** Remote content blocked in v1 with no load option (option c). Choosing (a) or (b) adds an outbound
  call and needs a spec change plus an addendum here.
- **W4** `<style>` blocks dropped; sanitized inline `style` only.
- **W5** HtmlSanitizer (AngleSharp-based), pinned, restrictive policy.
- **W6** Rich-text compose through a ProseMirror-based editor behind one kit primitive, or plain-text
  compose in v1 (the argument above covers the compose-time sanitizer only if the editor ships).
- **W7** 25 MiB per attachment, 20 attachments, 100 MiB staged per user, 24 h expiry, 100 recipients.
- **W8** From may be the mailbox address or one of its aliases.
- **W9** 20 sends/minute and 100/hour per user+mailbox.
- **W10** New dependencies: HtmlSanitizer (NuGet), a rich-text editor (npm, W6), the corrected MailKit
  comment; each pinned, justified, license-checked.
- **W11** vue.md amendments allowing `srcdoc` and `contenteditable` in exactly one kit component each.
- **W12** Flat `Services/` with a `Webmail` name prefix (no module has a `Services/` subfolder today).
- **W13** Links open in a new tab, escaping the sandbox for the destination, destination host disclosed.
- **W14** Delete moves to Trash, a second delete in Trash expunges; body search allowed with time and
  result caps.

Rule and spec changes (security.md item 10 if W1 or W3 change; vue.md for W11; csharp.md for the Mail
anatomy) are proposed in the PRs and applied by the owner, never by the author.

## Open questions for the owner

1. Is an operator (reseller or administrator) reading a customer's mail acceptable at all, beyond the
   journal? Should the customer be able to see that it happened?
2. W3: is v1 without a "load remote images" action acceptable, given issue #191 literally says proxied?
3. W6: rich-text compose in v1, or plain text only?
4. Who is the human second reviewer for #331, and may webmail go into a beta pre-release with the review
   still outstanding (security.md allows that only by a per-release written owner decision)?
5. Website docs needed (owner writes; agents do not touch `website/` or GitLab): #190 operator note (the
   gateway talks to the local mail server only, who may open which mailbox, operator opens are
   journalled); #191 customer note (what a message shows, why images are blocked, link safety, plain-text
   view); #192 customer guide (folders, reading, search, flags, moving, deleting, quota-full); #193
   customer guide (compose, reply, forward, drafts, attachments and limits, sending limits); all in
   en/ru/hy.
