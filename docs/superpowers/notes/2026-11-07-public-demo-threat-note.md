# Threat note: a public demo of the panel, open to untrusted visitors (issue #95)

**Surface:** a second distribution of the panel (`Maran.Host.Demo`) reachable by anyone on the
internet at demo-maran.innovayse.com, signing visitors in with two published credentials, driving a
simulated agent (`Maran.Agent.Simulated`) instead of the root agent. It touches: sign-in and session
handling by consequence (shared published accounts), the write surface of every module controller
(a census decides what is refused), the release and deployment pipeline (a new signed bundle), and the
TLS edge. rules/security.md "Sensitive change escalation" names sign-in and session handling, so this
change needs a second reviewer and a note written before the change. This is that note.

**Status: written BEFORE the change.** Nothing under `backend/`, `frontend/`, `.github/`, `demo/` or
`scripts/` had been edited for the demo when this file was created. The demo guard (the refusal
table, the mutation budgets, the pseudonymised caller address and the mail, ACME and licence
replacements, plan task 3) is the first code written against it.

**Second reviewer: OUTSTANDING (issue #331 is the standing item for these).** The work is done by an
agent session, which cannot get a reviewer and cannot be one. Per rules/security.md the change may
live on a branch and MUST NOT merge to `main` until a second reviewer has read it. It is not covered
by the beta pre-release exception unless the owner records it for the release in writing. The owner is
to be told at hand-off that the public demo carries an outstanding review.

**Owner decisions applied (the plan's recommended options, until the owner says otherwise):**
D6 (a visitor may change resources against the simulated agent, nothing that creates or alters a
credential, session, mail setting, licence, certificate setting, security policy, log level, API key or
whitelist), D7 (account creation is allowed). Both are classifications in one table, so a different
answer is a different table and not a different design.

## The premise

Every visitor is an attacker, or becomes one for free. The two demo identities are published on the
login page, so possession of the credential proves nothing, and anything a visitor can do, every
visitor can do, repeatedly, from many addresses, with automation. The design therefore assumes:
nothing in the demo is secret, nothing in it is worth protecting except (a) availability to the next
visitor, (b) the host it runs on, (c) every other network the host can reach, (d) third parties the
demo could be made to contact, and (e) the personal data of other visitors.

## Why a simulated agent, and what that removes

A real agent on a public login is "root on a box whose only defence is a list of limits", which is the
shape every public hosting-panel abuse story has. The simulated agent removes, by construction and not
by configuration: any process spawn by tenant code, any file written or served, any vhost, any cron job
executed, any database created, any outbound connection a tenant can trigger, and any privileged
process to attack. Content a visitor types is stored as data and shown as text; nothing executes it.
What stays real and attackable: the C# API process, PostgreSQL, nginx and the SPA.

## What an attacker could do, and why it is safe now

T1. **Lock other visitors out of the shared credentials.** Change or reset the password, enrol or
disable two-factor, revoke sessions, log everybody out, rename or delete the identities, trip the
per-username lockout, suspend or delete the account a demo login belongs to, or change the plan that
gives it its screens. Control: a closed, census-checked refusal table
(`Maran.Host.Demo/Guard/DemoEndpointTable.cs`) refuses every credential- or session-affecting action
with `DemoActionRefused`; a mutating endpoint the table does not name is refused too (fail closed),
including a minimal endpoint and a marketplace module's controller; the actions that could disable the
accounts and plans holding the demo logins are allowed only for resources the seed did not mark as
protected (`DemoProtectedResources`, filled by the seeder); the seeded security policy has per-username
lockout off; policy and retention saves are themselves refused. A reviewer must check the census is
exhaustive (the planted-violation and accept-side controls exist for that), that the protected set is
actually filled by the seeder (an empty set silently allows everything it guards; the integration test
reads it back), and that `logout` revokes only the caller's own session row, measured, not read.
Added after the adversarial review (`research_notes/review-demo-security.md`, F-3, F-4): the served process
**refuses to start** when the loader finds nothing to protect (`DemoProtectedResourceStartup` throws), and
independently the guard is **fail closed until the set is loaded** (`DemoProtectedResources.IsLoaded`): every
action that depends on the set is refused as `protectedResource` while it is not. The caller the seed acts as
is an ambient scope **object**: disposing it deactivates it for every task or timer that copied it
(`DemoSeedCallerAccessor`), so "a served request never acts as the seed" is a property of the type and not of
today's call graph.

T2. **Create persistent footholds.** New administrators, new logins for the demo accounts (sub-users,
invitations, membership offers), long-lived API keys. Control: no endpoint creates an administrator
today (only setup does, and setup is complete in the seed and refused); the others are in the refusal
table. A reviewer must check the table against the controller list, not against this note. Account creation
is allowed (D7) but **not with a password the visitor chose**: `DemoInitialPasswordFilter`, an MVC action
filter, refuses any bound `CreateAccountCommand` whose `LoginMode` is `InitialPassword` (the table classifies
actions, and this is a value in a body); creation by invitation stays allowed, and its mail goes to the
capturing sink (review F-5). The reseller-key account endpoints (`ResellerKeyAccountWritesController`,
`ResellerKeyAccountTerminationController`) are refused with the API-key area, as are the keys themselves.

T3. **Use the demo to send mail.** Invitations, password resets, alerts, test mail to any address.
Control: no SMTP is configured in the demo distribution, the SMTP save and test endpoints and the alert
recipient save are refused, and `IMailer` is replaced by a capturing sink that opens no socket and keeps
only the recipient and subject, never the body. Proof: a scan test over the demo project for the types
that open a socket or a process, with a planted-violation control, and the unit's `IPAddressDeny=any`
(T5).

T4. **Make the demo request a real certificate or call a third party.** ACME for tenant sites, the
panel's own certificate, the licence, an S3 backup destination, a webhook. Control: `IAcmeClient` is
replaced by an in-process simulation that signs a short-lived certificate from a throwaway key,
`ILicenceSignatureVerifier` accepts only signatures made by a key generated inside the demo process
(nothing outside the process can sign for it; the demo seeds no licence), the licence is read from and
written to memory (`DemoLicenceStore` replaces the module's file-backed source and writer, so the demo
process does no licence file IO), the panel certificate, backup destination and licence install endpoints are
refused, and the API unit denies all network egress (T5). The replacements are registered LAST in
`Program.cs` (after `AddLicensingStartupCheck`, which registers the Licensing module outside the module
list), because the last registration of a service wins; the review found they were registered first and the
real verifier won (F-1). `DemoRefusalEndpointTests` resolves the verifier, the ACME client, the mailer and the
licence storage from the composed provider and asserts the concrete types.

T5. **Use the demo host as a pivot or an outbound launchpad.** SSRF, a reverse shell after an RCE in a
dependency, abuse of the VM's address. Control (defence in depth, each separately measurable):
`maran-demo-api.service` carries `IPAddressDeny=any` with `IPAddressAllow=localhost` (PostgreSQL over
the unix socket needs none), `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6` as the installer's units
do, no agent unit exists on the VM, the VM holds no Innovayse credential, sits in its own network with
no route to internal ranges, and is rebuilt from an image. Measured by a script that runs `curl` inside
the unit's sandbox and requires failure, with a control that the same command outside the sandbox
succeeds. `maran units` ceilings gain the new unit. (Plan task 5; not part of the guard.)

T6. **Fill the database, memory or disk.** Create accounts, sites, files, cron entries, backups without
bound; large request bodies; many SSE streams; a flood of refused calls that each write an audit row.
Control: a global mutation budget per reset window (`DemoBudgetExhausted` after it, refused until the
next reset, which is a restart of the process), a per-pseudonymous-visitor mutation rate, a bound on the
number of visitors the rate table tracks, a bound on the audit rows refusals may write per reset
window, a request-body size cap and per-address connection and stream caps at nginx, a hard byte and
entry cap in the simulated ledger, and a reset every interval that returns everything to the seed. Sign
in, token refresh, second-factor verification and sign out do not draw on the global mutation budget, so a
script cannot spend it to lock the next visitor out of signing in. They are not free either, because each
sign-in or refresh writes a session row and an audit row (review F-6): they have an allowance of their own,
apart from the budget (`DemoSignInBudget`: `Demo:VisitorSignInsPerWindow` per visitor and
`Demo:GlobalSignInsPerWindow` for the whole demo, per `Demo:VisitorWindowSeconds`, answered 429 with
`Retry-After`; both renew every window, so a flood costs a window and not the reset interval). **A visitor is
a network, not an address** (review F-2): an IPv6 address is reduced to its /64 before it is pseudonymised
(`VisitorNetwork`), and nginx keys `limit_req` and `limit_conn` on the same /64, so a subscriber who owns
2^64 addresses is one visitor, not an unlimited number of them filling the tracked-visitor table and the
global budget. The tracked-visitor table sweeps its expired windows at most once per window length
(`DemoVisitorWindows`, F-7), so a flood of new keys does not walk it under the lock on every request. A
reviewer must check the numbers are enforced by a test that exceeds each of them. **Stated limit:** a visitor
with many addresses can still spend the global budget and leave the demo read-only until the next reset;
that is the safe direction, and the reset bounds it.

T7. **Turn the demo into an amplifier of other people's content.** Offensive or phishing text in names,
domains, file contents, cron commands, notes, shown to the next visitor; a site "visit" link pointing at
an attacker domain. Control: **filter plus contact** (issue #560, owner decision O-231). (1) The SPA
renders customer text as text only (rules/vue.md bans `v-html`); the demo distribution renders no hostname
of a visitor-created site as a live link (frontend task 6 must verify and, where a link exists, drop it in
the demo build). (2) **The content filter:** `DemoContentFilter`, an MVC action filter over the bound
arguments of every action the refusal table lets through (`DemoEndpointRule.ScreensText`; a sign-in is not
screened), refuses a write with a 400 `DemoContentRefused` (en/ru/hy, the member's name and never the text)
before the action stores anything. `DemoFreeText` walks the bound command for every string wherever it sits
(secrets and `SensitiveString` excluded); `DemoTextScreen` judges each one with no outbound call: a term
from `Resources/DemoBlockedTerms.<language>.txt` (whole words or, for a term ending in `*`, any word that
starts with it; after folding case, accents, invisible characters, full-width forms, digit swaps and Latin,
Cyrillic, Greek and Armenian look-alikes, with spaced-out or separator-broken letters read as one word),
or a link (`scheme://host` for any scheme, `http:/host`, `//host.tld`, `www.host`) whose host is not in
`Resources/DemoAllowedHosts.txt` (the `example.*`, `test` and `invalid` names only; never loopback) or, for
`http` and `https`, in `Resources/DemoDocumentationHosts.txt` (`w3.org`, `apache.org`, `schema.org`), or a
script or content scheme (`javascript:`, `vbscript:`, `magnet:?`, `data:` with a media type). Text
the screen cannot read or finish in time is refused, not passed. The refusal is journaled by action and member
under the same bounded allowance as other refusals. (3) **The contact:** `Demo__AbuseContact` (an e-mail
address or an https URL, required: `DemoOptionsValidator` stops the demo starting without one) is returned by
the status endpoint and printed in the banner in every language, with a sentence saying offensive text and
outside links are refused. (4) The banner says what is shown to others and that it resets; the reset bounds
the lifetime. **Residual risk, accepted and stated:** the list is a floor, not a moderator (a paraphrase, a
spelling neither fold maps, or a bare `name.tld` with no scheme and no `www.`, which a domain field must be
able to hold, passes); an abusive string that passes can be shown to other visitors for up to one interval,
and the contact is how it is reported. The word lists are a seed for the owner to extend.

T8. **Reach the stored credentials or keys of the operator.** The demo's JWT signing key, encryption key
and database password live in `/etc/maran-demo/panel.env` (root:service group 0640) on a VM that holds
nothing else. They are generated per deployment, never reused from any real install, and never
committed (rules/security.md item 7: `.env.example` documents the names). The pseudonym key and the
licence key are generated in memory at process start and never leave the process.

T9. **Learn other visitors' addresses.** Session list and audit journal show IP and user agent. Control:
a middleware placed after forwarded-header handling replaces the connection's remote address with a
keyed pseudonym (HMAC-SHA256 of the real address under a key generated at every process start, the
first twelve bytes placed under the IPv6 documentation prefix `2001:db8::/32`, which is never routed),
so rate limiting still partitions per visitor, nothing real is ever journaled, and a reset makes
pseudonyms unlinkable across intervals. The user agent is cut to a coarse family string for the same
reason, and the proxy headers that carry the real address (`X-Forwarded-For`, `X-Original-For`,
`X-Real-IP`, `Forwarded`, `CF-Connecting-IP`, `True-Client-IP`, `X-Client-IP`, `X-Cluster-Client-IP`,
`Fastly-Client-IP`) are removed so no later component can read them. The pseudonym is of the visitor's
network (IPv4 address, IPv6 /64), see T6. A reviewer must check no
code path reads the real address after that middleware (the existing `maran structure` rule "one
spelling of the caller's address" helps: `ClientAddress.Of` is the only reader), and that nginx's own
access log on the VM does not keep addresses and its error log is discarded (a crit line names the peer;
plan task 5). **Stated limit:** the middleware cannot
protect an address that something logs BEFORE it runs; its position in the pipeline is part of what the
reviewer checks. A test now composes the pseudonymising and the guard middleware in their order and requires
the journal to hold the stand-in and the agent family. If nginx is ever not in the trusted proxies, every
visitor pseudonymises to the proxy's address and shares one bucket: an outage, not a leak.

T10. **Use the demo build against a customer.** Install the demo bundle on a real server, or enable demo
mode in a real panel by editing a setting. Control: demo mode is not a setting. The demo is a separate
host assembly that is not referenced by `Maran.Host`, a separate signed bundle whose manifest is signed
by a DIFFERENT key than `installer/keys/release-signing.pub`, so the installer and `maran update` refuse
it by signature (proven by a planted demo bundle fed to the real verifier), and its SPA flavour is a
build-time define absent from the normal bundle (D5). (Plan tasks 5 and 6.)

T11. **Compromise the deploy path.** A CI secret that can write to the VM is the most valuable thing in
this design, because it is code execution on the demo host and a foothold next to the repository.
Control: see D8; whichever way is chosen the credential is single-purpose (forced command, one
directory), kept in a protected GitHub environment, and the VM trusts only bundles verifiable with the
demo public key shipped on the VM. This is the pipeline half of the sensitive change and needs the same
second review as `release.yml`. (Plan task 5.)

T12. **Abuse the admin role on the demo.** The demo administrator reaches every admin screen. In the
simulated shape none of them touches a host. Control: the refusal table classifies each admin write;
view-as is allowed (it is a flagship screen and creates no credential); firewall port rules and bans are
allowed because they change only simulated state (D6 for the whitelist and protected ports); the audit
export and personal-data export stay allowed, rate limited (the existing `audit-export` policy), and
contain only demo data.

T13. **Hide an attack in the noise.** Control: the simulated agent writes no host audit line (there is
no host), and the panel's own journal keeps working; the guard journals refusals (bounded, T6) and the
first exhaustion of the budget; the demo's operational alerts (reset failed, deploy failed, disk, budget
exhausted) go to the team from outside the demo process.

## What the author could not verify

- The refusal table's exhaustiveness is checked by a census test, but whether each refusal is the RIGHT
  product call (for example whether firewall bans should be allowed) is a decision, not a fact (D6).
- That the census sees minimal endpoints: it walks controller actions, as the view-as census does. A
  mutating minimal endpoint is refused at run time unless the guard names it (today only the read-only
  search), and the integration tests read the real endpoint data source for that; nothing in the
  architecture suite does.
- That the real nginx and VM image honour the egress and connection caps: the checks are scripts that
  need the VM; they cannot run in this tree. The unit-file half is covered by `maran units`; the network
  half is measured at first deploy and recorded.
- That a second Wolverine entry assembly composes without regenerated handlers (plan finding 9, risk R1).
- That the order of the pseudonym middleware, the guard and the existing pipeline is right in the
  composed demo host: the middleware is written and unit-tested here, its placement is plan task 5's.
- That a scan test covers the whole demo project: `DemoGuardSourceTests` scans `Adapters`, `Guard`,
  `Pseudonymisation`, `Extensions`, `Services` and `Options` only. `Reset/` (the marker file, by design),
  `Seeding/` and `Cli/` are not scanned; the "scan test over the demo project" in T3 means those six folders.
- That nginx's `map` over `$binary_remote_addr` with a PCRE capture of the first eight bytes behaves as
  written on the target nginx; it was written from the documentation and not run.
- Behaviour under real abuse volume; the numbers in T6 are starting values, to be re-measured.
- No build or test had been run when this note was written (the lead's gate runs them once, after all
  code is written).

## Findings of the adversarial review and where each is closed

`research_notes/review-demo-security.md`: F-1 (verifier registered before its override) T4; F-2 (per-/128
pseudonym) T6 and T9; F-3 and F-4 T1; F-5 T2; F-6 and F-7 T6; F-8 T9; F-9 the table now classifies
`SiteOptionsController.ReplaceAsync` and `SiteLogRetentionController.ChangeAsync` (allowed: simulated state
only) and the reseller-key account actions (refused, API keys); F-10 `maran demo build` runs
`npm run build:demo`, packs `frontend/dist-demo` and requires the demo status request in it, while the
product release refuses `VITE_MARAN_DEMO=1` and a `dist` that contains that request; F-11 the demo seeds no
licence and keeps the installed text in memory; F-12 the API unit carries `MemoryMax`, `TasksMax`,
`LimitNOFILE` and `CPUQuota`, and the one-time setup token moved from the service's environment file to
`/etc/maran-demo/seed.env`, read by the deploy script only; F-13 the scan scope is stated above.
The second review stays OUTSTANDING; these fixes were written by the session that wrote the code.

## Addendum: content filter and abuse contact (issue #560)

T7 changed from "accepted residual, filter undecided (D9)" to "filter plus contact" (owner decision O-231,
applied from the recommended option). What a reviewer checks: `Guard/DemoTextScreen.cs` (the folds, the three
URL patterns and their time limit, fail-closed on malformed Unicode or a timeout), `Guard/DemoFreeText.cs` (what
it reads and what it leaves out by design: secrets, `SensitiveString`, bytes and streams, an upload's content),
`Guard/DemoContentFilter.cs` (which actions it screens: only what the table's `ScreensText` says) and
`Options/DemoOptionsValidator.cs` (the contact is required and well-formed). What the author could not verify:
no build or test had run when this was written; whether `string.Normalize` throws on a lone surrogate on every
supported platform (the screen relies on it to refuse malformed text); the false-positive rate of the seed word
lists on real demo use; and that a command member the walk reads (a path, a DNS value) never needs a host
off the allow-list in a legitimate demo walkthrough. A write that does not pass through an MVC action (a
minimal endpoint, the seed's own bus calls) is not screened; the only mutating minimal endpoint is read-only
search, and the seed is the operator's own.

**Review follow-up (issue #560, second pass).** What changed, and what a reviewer re-checks:

- *Link reading* (`Guard/Text/DemoLinkScreen.cs`). The text is folded for links (`DemoTextFold.ForLinks`) with
  tab, line feed, carriage return and every other control character removed (a browser strips them from a URL, so
  `http://example.com<TAB>.evil.com` is one host and `java<LF>script:` is one scheme) and with accents KEPT (so
  `exämple.com` is non-ASCII and refused, not read as `example.com`). The authority ends at `/ \ ? #`, white space and
  `< >`; quotes and the backtick do NOT end it (a browser keeps them in a host), and only punctuation at its very end
  is taken as the text around the link. A host must be made of `a-z 0-9 . -` in non-empty labels or it is refused:
  percent escapes, quotes inside a host, internationalised names and bracketed IPv6 are beyond what the screen can
  judge, and what it cannot judge it refuses (the screen uses its own strict parser, not `System.Uri`, whose host
  rules differ from a browser's). `http:/x`, `http:x`, `https:///x` and a protocol-relative `//host.tld` are links;
  a link after another scheme (`blob:https://...`) is found in turn. `localhost` and `127.0.0.1` are NOT allowed
  hosts any more (a link a server later fetches must not reach the panel's own services).
- *Other schemes.* `s3://`, `git://`, `php://`, `redis://`, `ssh://` and the like are links subject to the host
  rule: allowed only with an `example.*` / `test` / `invalid` host. `data:` is refused only at the start of a URL
  with a media type (`data:text/html`, `data:,`), so `url(data:image/png...)` in a style sheet and `{data:1}` in a
  script pass; `src="data:image/png..."` in markup is still refused (it is a URL start).
- *Documentation namespaces.* `http` and `https` links to `w3.org`, `apache.org` and `schema.org` (and names under
  them) pass anywhere in visitor text, so XML namespaces, DOCTYPEs, licence headers and JSON schemas in saved files
  are not refused. The list is curated (`DemoDocumentationHosts.txt`), and not allowed for another scheme.
- *Terms.* A listed term ending in `*` is a stem (any word starting with it) so inflected forms of English,
  Russian and Armenian words are found; every other term is a whole word; a stem has at least three letters or the
  screen does not start. Characters written inside a word to break it (`. - * _ ' + ~ ^`) are read through (second
  reading). Digit and symbol swaps (`5h1t`, `@ss`) apply only inside words that have a letter, so `455` or a cron
  field is a number and never a word. Look-alikes now include dotless i, `ø ł đ ħ ŧ ð`, more Cyrillic and Greek
  letters and the Armenian `ս օ ո ց հ ք զ`; Hangul fillers, the blank braille cell and Unicode tag characters are
  dropped. `xxx` and `click here` were removed from the lists (code and markup carry them).
- *The walk* (`Guard/DemoFreeText.cs`). It reflects only into types in the `Maran` namespace (an allow-list; a
  framework or third-party object is never entered), keeps a visited set (a repeat is read once, a cycle ends) and a
  node budget (`MaximumNodes`), and READS `JsonElement`, `JsonNode` and `JsonValue` bodies (strings and property
  names). It REFUSES, with `DemoFreeTextUnreadableException` turned into the usual `DemoContentRefused`, when it
  reaches `MaximumDepth`, exhausts the budget or meets a getter that throws; it no longer ends quietly. Secret
  members are exempt by exact name (`DemoFreeText.SecretNames`), not by substring: `TokenName` and `SecretNote` are
  screened. `ArchitectureTests/DemoFreeTextCensusTests` holds every screened action of the composed panel to the
  depth cap, so a deeper command fails CI instead of every write to it failing in the demo.
- *Banner.* The filter notice and the abuse contact no longer wait for the first recorded reset.

Still stated, not fixed: the content of an uploaded file is not read (only its name); a multi-word term or a link
whose parts are in different fields is judged per field; doubled letters (`fuuuck`) and paraphrases pass; a
`bit.ch`-shaped domain written without a scheme is read as a word by the separator-joining reading; the Russian and
Armenian lists are seeds a native speaker must review; a refusal is a blocklist oracle (the list is in the
repository anyway); a very large saved file is folded several times, so the request-size limit of that endpoint is
the bound on the cost. What the author could not verify (no build or test ran): that every composed command is
within `MaximumDepth` (the census answers it), that `JsonValue.TryGetValue<JsonElement>` behaves as read on every
supported runtime, and the false-positive rate of the seed lists on real demo use.

Second review: OUTSTANDING (#331).

## Reviewer checklist (what the second reviewer must do)

1. Read the refusal table against
   `grep -rhn "^\s*\[Http\(Post\|Put\|Patch\|Delete\)" --include=*Controller.cs backend/src/Maran.Modules`
   and the minimal-endpoint list (`grep -rn "MapPost\|MapPut\|MapDelete\|MapPatch" --include=*.cs backend/src`),
   not against this note.
2. Confirm no change to Identity, Accounts or any module: `git diff --stat backend/src/Maran.Modules`
   must be empty for the demo's own PR series except for explicitly listed seams.
3. Run the planted-violation and accept-side controls of the census
   (`backend/tests/Maran.ArchitectureTests/DemoRefusalCensusTests.cs`).
4. Run the egress measurement on a real VM, with the control.
5. Try the published credentials: change password, log out everywhere, revoke sessions, save SMTP, ban an
   address, suspend or delete the seeded accounts, create 10,000 sites by script, upload a huge file.
6. Feed the demo bundle to the real installer/`maran update` verifier and require refusal.
7. Read where the pseudonym middleware sits against every component that logs or stores an address.
