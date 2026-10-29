# Threat note: tenant-chosen host names reach nginx `server_name` and ACME issuance (issue #96)

`rules/security.md` requires a threat note, written first, for a change that touches the agent's
privileged boundary, and a second reviewer. A site's domain and aliases are written by the root
agent into an nginx `server_name` directive, and the same names are the subject of a certificate
the panel asks a public CA for. This change decides which names a tenant may put there. It is
written before any code of the branch.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by agent sessions,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it
and signed off. The owner is told at hand-off that this surface carries an outstanding review.

## What an attacker can do with this surface today

The attacker is an ordinary hosting customer using the HTTP API (the agent is reached only through
it). Nothing in the C# module or in the Rust validated type reserves any name. Verified by reading
the code (issue #96 carries the table; each row re-checked against this tree):

- `HostNameRule.IsHostName` (`backend/src/Maran.SharedKernel/Utilities/Network/HostNameRule.cs`)
  accepts two or more labels of `[A-Za-z0-9-]{1,63}`. A dotted quad (`203.0.113.7`) matches, because
  nothing requires the last label to be non-numeric. `localhost.localdomain` and `foo.localhost`
  match. The panel's own host name matches.
- `Domain::parse` (`agent/crates/agent-core/src/validation/web/domain.rs`) is the agent's
  re-validation. It accepts the same alphabet with any label count, so bare `localhost` and any
  single label are accepted at the agent although the API refuses them. Its callers are
  `services/sites/validated_identity.rs` (primary domain), `validated_site.rs` (each alias),
  `services/ftps/validated_ftps_configuration.rs` and `validated_hostname.rs`.
- The customer vhost template carries `server_name {{ domain }} {{ aliases }}` and no
  `default_server`. An exact `server_name` match beats the default server, so a tenant holding the
  server's IP literal answers the bare IP on port 80, and removes the distribution's default answer.
- The panel vhost (`installer/nginx/maran.conf`) listens on 8443 only, so a tenant vhost for the
  panel's name on 80/443 conflicts with nothing and the agent's #85 conflict check (per listen
  address) does not fire. The tenant's document root then answers the panel's name on 80/443,
  including `/.well-known/acme-challenge/` (carved out by the template).
- `IssueCertificate` looks the domain up among the caller's OWN sites, so holding the vhost is the
  only precondition for a publicly trusted certificate and private key for the panel's host name,
  when DNS for that name points at this server (HTTP-01 validates against the tenant's root).
  Inferred, not exercised: the CA step was not run.
- It also permanently blocks the operator from serving or certifying the panel on 80/443 under that
  name: it already belongs to a tenant.

What is NOT true (corrected from the original hypothesis, as in the issue): a tenant site does not
capture the panel login on a default install, because the panel is on 8443. IPv6 literals are not
creatable (`:` is outside the alphabet).

## The change planned, and why it makes the surface safe

The common idea: a host name is checked against a reserved set in the backend (early, localized
refusal and the only place that can know the panel's own name) and the static part of the set is
checked again in the agent's validated type (rules/security.md item 1: revalidated in the agent).
The set is split by what each side can know.

### The reserved set

STATIC (identical in both stacks; compared after lower-casing and after dropping ONE trailing dot
in the backend; the agent's `Domain::parse` already refuses a trailing dot as an empty label):

1. A name whose FINAL label is all digits. This refuses every IPv4 literal in dotted form and also
   `1.2.3`, `10.1`, and a lone `12345`, which some resolvers and nginx's own matching treat as
   address forms (`inet_aton` semantics). It is categorical on purpose: no list of the server's
   addresses is needed, and a list would go stale (rules/security.md item 10: no new probe).
2. `localhost` and any name ending `.localhost`.
3. Special-use suffixes that never name a public host: `.local`, `.localdomain`, `.internal`,
   `.lan`, `.home.arpa`, `.invalid`. (`.test` and `.example` are NOT on the list: the project's own
   fixtures and a tenant's staging may use them. Reviewer decision, see below.)

HOST-SPECIFIC (backend only, behind a window the Sites, Ssl and Ftp modules can ask):

4. The panel's own host name and its `www.` form, matched exactly and case-insensitively. Names
   under the panel's domain (`x.panel.example.com`) are NOT refused: sibling subdomains of the
   operator's domain are an operator policy, the default is empty (as the issue recommends).

### The four parts

1. **agent-domain (wave 1).** `Domain::parse` in `validation/web/domain.rs` refuses the static set
   with new `DomainError` variants in `domain_error.rs` (`#[non_exhaustive]` already, so additive):
   `NumericFinalLabel`, `LocalhostFamily`, `SpecialUseSuffix`. A small crate-internal helper file
   holds the predicate if `domain.rs` would otherwise carry two units (one file, one unit). The
   panel name is NOT refused by the agent in this change; see the residual below. Note for the part
   owner: `validation/domain.rs` and `validation/domain_error.rs` at the top of the tree are not
   declared in `validation/mod.rs` and are never compiled; they are dead twins of the `web/` files.
   Edit only the `web/` copies and report the dead twins.
2. **csharp-rule (wave 1).** `ReservedHostNameRule` in `Maran.SharedKernel/Utilities/Network/`,
   beside `HostNameRule`, with the static set only and `IsReserved(string?)`. Pure BCL (string comparison, no regex), so one source decides for every module. Also a narrow Sdk
   interface `IReservedHostDirectory` in `Maran.Sdk/Interfaces/` answering one question,
   `IsPanelHostName(string)`: the narrowest value that answers it (the module may not reference
   Identity's `PanelOptions`, rules/architecture.md module isolation).
3. **csharp-callers (wave 2).** `CreateSiteCommandValidator` (domain and each alias),
   `IssueCertificateCommandValidator` and `InstallCustomCertificateCommandValidator`,
   `EnableFtpsCommandValidator`: add a `Must` using `ReservedHostNameRule` (all four) and the Sdk
   window (Sites, Ssl; see FTPS decision below), each with a new bare-key resx entry in en, ru and
   hy. The Host implements the window from a validated options type holding the panel's name.
   Messages name no rule beyond "this name cannot be used" (no oracle for which part matched).
4. **installer-config (wave 2).** The installer writes `Panel__HostName=<name>` into `panel.env` and
   documents it in `installer/panel.env.example` and `.env.example` (rules/security.md item 7). The
   value is the SAME one rendered into the panel vhost. Today the vhost renders
   `hostname -f || _` (`80-nginx.sh` `render_vhost`) while the certificate and the finish step use
   `host_display_name`, which has more fallbacks, so two installer expressions already disagree on
   hosts without a `hostname` binary. The part owner must take the value from one place and write it
   only when it is a real name (not `_`, not `localhost`), so a missing value means "no
   host-specific reservation", never a reserved `_`.

### Why this makes the surface safe

- Through the HTTP API a tenant can no longer obtain a vhost, and therefore an ACME certificate,
  for an address literal, a loopback or special-use name, or the panel's name. Because
  `IssueCertificate` and `InstallCustomCertificate` share the rule, the reservation cannot be
  bypassed on another door (`grep -rn HostNameRule backend/src` lists every door: Sites, both Ssl
  commands, Ftp, and Accounts' primary domain).
- The agent re-validates the static set, so a request that skips the API validator is refused at
  the root boundary too, and the refusal does not depend on nginx's per-address conflict warning.
- Failing direction: a missing or empty panel name configuration reserves nothing host-specific
  (permissive); the static set is unconditional. The panel name check is an operator-facing guard,
  not a boundary against a compromised API (below).

## Exact public API any part depends on from another part

- `Maran.SharedKernel.Utilities.Network.ReservedHostNameRule` (csharp-rule), used by csharp-callers:
  `public static class ReservedHostNameRule { public static bool IsReserved(string? candidate); }`
  `true` for the static set above; `false` for null/empty (those are other rules' concern).
- `Maran.Sdk.Interfaces.IReservedHostDirectory` (csharp-rule), used by csharp-callers, implemented
  in Maran.Host (csharp-callers owns the wiring): `bool IsPanelHostName(string candidate);`
  case-insensitive, true for the configured panel name and its `www.` form, false when none is
  configured.
- Configuration key `Panel__HostName` (installer-config writes, csharp-callers reads through a
  validated options type bound at startup, rules/csharp.md "Options validated at startup").
- `agent_core::validation::web::domain_error::DomainError` gains variants only; callers match with
  a wildcard already (`#[non_exhaustive]`), and all four callers convert it via `to_string()`.
- Resx keys are added by csharp-callers alone; csharp-rule adds none.

## What a second reviewer must check

1. The static set is spelled the same in `domain.rs` helper and `ReservedHostNameRule`, including
   the numeric-final-label rule applied to a single label (`12345`) and the `.home.arpa` two-label
   suffix. Each stack's tests hold their own literals (`reserved_host_name_tests.rs`, `ReservedHostNameRuleTests.cs`); there is no shared fixture and no mechanical cross-stack comparison, so the two are kept equal by review and a row added on one side must be mirrored.
2. Case, trailing dot, and Unicode: the backend compares ordinal and lower-invariant; `HostNameRule`
   already refuses non-ASCII, so an IDN or full-width digit never reaches the rule. Confirm the
   rule is not run on a value the shape check has not already passed.
3. Every door: re-run `grep -rn HostNameRule backend/src` and confirm each tenant-reachable one is
   covered. `CreateAccount`'s primary domain is also a `HostNameRule` user and is NOT in this
   branch's parts; decide whether it needs the same rule (it names no vhost itself, but it feeds
   later site creation).
4. The agent's other readers of `Domain`: `ops/src/sites/inspect_account_sites.rs` parses vhost
   file stems with `Domain::parse`. After this change a pre-existing vhost named with a now-reserved
   name is reported "cannot observe" and a suspension of it is refused. Confirm that is acceptable
   (it fails safe) and that no existing production site can be in that state (a reserved name was
   creatable before this change, so one can exist; this change does not remove it).
5. The FTPS decision below.
6. The `Panel__HostName` source: that the installer value equals the name the vhost really serves.
7. That nothing in the error text or resx reveals which rule matched, or the panel's name.

## Decisions taken in this note that the reviewer may overturn

- **FTPS.** `EnableFtps` is administrator-only (`AdminOnly` policy) and an operator may legitimately
  name the FTPS endpoint after the panel's own host. So `EnableFtps` gets the STATIC rule only, not
  the panel-name window. If the reviewer wants the stricter reading, it is one line in the
  validator.
- **`.test` / `.example` not reserved.** Kept permissive so fixtures and staging work.
- **Agent does not refuse the panel name.** The agent cannot know the configured name except from a
  caller-supplied string (which a compromised API controls, making it theatre) or by reading the
  machine's host name (`gethostname`, an unsafe libc call the validated type would have to own, and
  not the same string as the panel's configured name on many hosts). This change therefore
  enforces the panel name in the backend only.

## Residual risks stated plainly

- A compromised `maran-api` (the agent's threat model) can still create a vhost named after the
  panel's host name by calling the agent directly. Closing that needs the agent to learn the name
  from a root-owned source (for example a line the installer writes into `agent.env`); that is a
  follow-up needing the owner's decision, because it adds a new agent input.
- Names under the panel's domain stay creatable by design (operator policy, default empty).
- Rows created before this change are not removed or re-validated.
- Other tenants' real names are unaffected; DNS-level control of a name is not checked here
  (a tenant can still point a vhost at a name they do not own; this is the existing #85 surface).

## What the author cannot verify

- That `hostname -f` equals the name the operator reaches the panel by (a CDN or alias name differs).
- Default-server behaviour of the stock distribution nginx packages (read from knowledge, not
  measured here; Phase B measures `nginx -T | grep default_server` on the polygon images).
- That a CA validates end to end for the panel's name (needs a public name pointing at a test
  server); the harm is inferred from the template and the handler.
- Nothing about the build state: this note was written in Phase A, with no build, test or measure
  run. The resolver semantics of numeric labels (`inet_aton`) are from general knowledge.

## Review of this note against the code (second Phase A pass)

An interrupted earlier run wrote the note above before any code. It was re-checked against this
tree and kept; only the points below are added or pinned.

- Verified again: `validation/mod.rs` declares `web` and not the top-level `domain.rs`, so the
  top-level twins are dead; `DomainError` is `#[non_exhaustive]`; `Domain::parse` has the four
  service callers listed plus `ops/src/sites/inspect_account_sites.rs:141`; `FtpsServerController`
  is `AdminOnly`; `render_vhost` still calls `hostname -f || "_"` at `80-nginx.sh:374` while the
  certificate step uses `host_display_name`.
- Pinned decision for the numeric rule: the final label being ALL digits is the contract both
  stacks implement (the issue's minimum). A scout proposed the wider "final label starts with a
  digit" (also catching `0x7f.0x1`, `1e3.1e3`); no public suffix starts with a digit, so the wider
  form costs no legitimate name. The reviewer chooses; whichever is chosen MUST be identical in the
  Rust helper and `ReservedHostNameRule`, and the two test tables must stay equal by review (nothing compares them).
- Precedent for the Rust helper's shape: `validation/web/ipv4_disguise.rs` is a subject-named,
  crate-visible predicate with no error type of its own, each caller wording its refusal in its own
  error enum. A reserved-name predicate follows that shape if `domain.rs` would hold two units.
- The Sdk window is named `IReservedHostDirectory` here; a scout suggested `IPanelHostDirectory`.
  Either is acceptable, but the name must be used identically by csharp-rule and csharp-callers:
  this note's spelling (`IReservedHostDirectory.IsPanelHostName`) is the contract.
- `Panel__HostName` must be re-detected on every installer run (not preserved through the
  existing-value read-back), written only when it is a well-formed single-line `[A-Za-z0-9.-]`
  name that is not `_` or `localhost`, and `render_vhost` and the env line must take the value from
  one evaluation of `host_display_name`.
- Not in this branch's parts and left as a stated gap: `CreateAccount`'s primary domain.

## Independent agent review of the Phase A code (five lenses)

Five agent reviewers read the Phase A code against this note and the rules: correctness, security,
rules, compatibility and design. **This is an agent review. It is NOT the second human review, and it
does not discharge it. The human second review stays OUTSTANDING, and this branch MUST NOT merge to
`main` until a human reviewer has signed off** (rules/security.md "Sensitive change escalation").
The owner is told at hand-off that this surface carries an outstanding review.

Verdicts: correctness approve, rules approve, design approve, security changes_required,
compatibility changes_required. Two lenses blocked, on the same defect (finding 1 below). The decisions below were applied by the fixers; this section records the decision on each finding, not the diff.

### Security-lens checklist result (rules/security.md, as reported by the reviewer)

| # | Item | Result | Note |
|---|---|---|---|
| 1 | Input validated at the boundary, revalidated in the agent | yes | Validators run the reserved rule only after the shape rule passes; the agent revalidates the static set. The panel name is backend only, by the decision above. |
| 2 | Paths contained by descriptor walk | n/a | No path handling changed. |
| 3 | No shell strings | yes | None added; installer additions are plain functions, no eval. |
| 4 | Config-file injection | yes | `detect_panel_host_name` allows only `[a-z0-9.-]`, at most 253 characters, before writing `panel.env`. The vhost value is not re-validated (minor finding 4). |
| 5 | SQL parameterized | n/a | No SQL. |
| 6 | AuthZ / account scoping | n/a | No endpoint or entity added; `EnableFtps` stays `AdminOnly`. |
| 7 | Configuration documented | yes | `Panel__HostName` is in `.env.example` and `installer/panel.env.example`; no secret. |
| 8 | Secrets / error leakage | yes | Resx and `DomainError` texts carry neither the matched value, the panel name, nor which rule matched. |
| 9 | Crypto | n/a | None. |
| 10 | Surface | yes | No port, daemon or outbound call added. |
| 11 | Dependencies | yes | None added. |
| 12 | Agent rpc closed, typed, re-validated | **no, as first reviewed** | No new rpc and revalidation is present, but it also gated the existing-site rpcs. Resolved by finding 1; to be re-confirmed in Phase B. |
| 13 | Module reach into the agent declared | yes | No new agent client or capability; `IReservedHostDirectory` is a read-only Sdk verdict. |
| - | Escalation: note is a file, written first, review OUTSTANDING, plan matches code | yes | Matches apart from the omissions closed in finding 5. |

### Blocking and major findings, and the decision on each

1. **MAJOR (security and compatibility, same defect; blocking).** `Domain::parse` carries the
   reserved set and is also the identity parse for `enable_site`, `disable_site`, `delete_site`,
   `tail_site_log`, `update_site_php_version` (via `validated_identity`) and for
   `inspect_account_sites`. A vhost with a now-reserved name that a tenant created before this fix,
   the exact object the issue is about, could no longer be disabled, deleted, tailed or inspected,
   and account deletion may be blocked too. The fix would close the hole on creation and block the
   cleanup of the hole already open. This contradicts the earlier "fails safe" reading in review
   item 4 above, which considered only suspension and not removal.
   **Decision: ACCEPTED, fix applied by the fixers.** The reservation applies only to inputs that
   create or re-render serving config: `create_site` primary domain and aliases, alias updates,
   certificate issue and install, FTPS enable. As built, the shape-only parse (`validated_existing_identity`, `validated_existing_site`) serves exactly disable, delete, log tail, remove-certificate and inspection/suspension, so a legacy reserved-named site can still be found, suspended, disabled and removed. `enable_site` and `update_site_php_version` still go through `validated_site` and DO enforce the set: a legacy reserved-named site cannot be enabled or have its PHP version changed (it can be disabled and deleted). Any earlier text in this note listing enable and PHP version as shape-only is superseded by this sentence. The weaker alternative (document and give the
   operator a manual cleanup path) was rejected. The test pass MUST add: delete and disable of a
   site named like `x.local` and like a dotted quad still succeed, and creating the same name is
   refused. The backend delete and suspend commands do not apply the reserved rule, so the backend
   is already consistent. Residual-risk line "Rows created before this change are not removed or
   re-validated" stays true by design: they are not removed, but they can now be removed by the
   tenant or the operator.

No other finding was rated blocking or major by any lens.

### Minor findings, and the decision on each

2. **C# versus agent parity on the bare name `home.arpa`** (raised by correctness, security, rules,
   design). The agent refuses a name equal to a special-use suffix; `ReservedHostNameRule` matched
   only `.` plus suffix, so `home.arpa` (two labels, accepted by `HostNameRule`) passed the API and
   was refused later by the agent with an unlocalized error. Fails closed, no hole, but the doc
   claim that the sets are identical was not exact. **Decision: ACCEPTED.** The C# rule also treats
   a name equal to a bare suffix that is a valid host name (`home.arpa`) as reserved; both stacks' test tables list the same names, including every bare suffix (spelled twice, kept equal by review only).
3. **`render_vhost` fallback changed from `_` to `localhost`, and the value is not lower-cased**
   (correctness, security, compatibility, design). On a host where `hostname -f` fails, `server_name`
   changes on re-run; vhost and `Panel__HostName` could differ in case only. **Decision: ACCEPTED.**
   As built: `detect_panel_host_name` (60-config.sh) and `render_vhost` (80-nginx.sh) each call `host_display_name`, lower-case the result and accept only `[a-z0-9.-]`; no shared variable is exported. They are two evaluations of one function, so they agree unless the host name changes between them, and differ only in the fallback: the env line records nothing for an unusable name, the vhost falls back to `localhost`. The change of fallback is deliberate and goes
   in the CHANGELOG; the stale comment on `installer/nginx/maran.conf` line 9 is updated.
4. **Doc-truth and tracking items** (security, rules, compatibility, design). Accepted as follow-ups
   in this branch: a CHANGELOG entry under Unreleased (reserved names refused for new sites and
   certificates; `Panel__HostName` is new and optional; existing reserved-named sites are not removed
   but can be removed; the installer fallback change; no migration, no schema or proto change; a
   downgrade ignores the extra line); doc comments in `validated_identity.rs`, `validated_site.rs`
   and the FTPS validators; the reserved-name `<para>` structure in the validator remarks.
5. **Note gaps** (security). This note now records the `reservation_of` helper returning an
   `Option<DomainError>`, the pre-existing-vhost consequence (finding 1) and this review.
6. **Rules and dead files need OWNER approval, flagged, not done by agents.** (a) One line each in the
   `rules/csharp.md` Utilities map (`ReservedHostNameRule.cs`) and the `rules/rust.md` `web/` row
   (`reserved_host_name`, a shared predicate with no `_error` file). (b) Deleting the dead twins
   `agent-core/src/validation/domain.rs` and `domain_error.rs`, which are undeclared in `mod.rs` and
   now contradict the live `web/` copies. Per rules/git.md a file holding real code is not removed
   without a go-ahead, and rules change only with the owner's approval. **Decision: DEFERRED to the
   owner.**
7. **Design style items** (design). `.Cascade(CascadeMode.Stop)` instead of the repeated
   `!IsHostName(x) ||` guard; one shared trailing-dot normalisation instead of the copy in
   `PanelHostDirectory`. **Decision: DEFERRED, not required.** Both are behaviour-neutral. The guard
   is kept because it is explicit and tested; the duplication is acceptable and is covered by a test
   that both normalisations agree. Revisit if a third copy appears (rule of three).
8. **A test file written in the code phase** (rules). **Decision: KEPT.** It is small and the
   `#[path]` module needs it to compile; it is extended in the test pass.
9. **Tests that no longer compile or pass** (correctness, rules): validator tests using a
   parameterless constructor, agent tests using `localhost`, dotted quads or `.local`, and the
   error-status table. **Decision: Phase B**, with a fake `IReservedHostDirectory`.

### Still outstanding after this review

- The human second review (this section does not replace it).
- The owner decisions in finding 6, and the numeric-final-label choice pinned earlier (all digits
  versus starts with a digit).
- Phase B measurement: that the fix for finding 1 leaves delete and disable working for a legacy
  reserved-named vhost, and that checklist item 12 is then "yes".

## Phase B verification evidence

This section records what was run on the branch after the code was written. It does not discharge
the human second review, which stays OUTSTANDING (see the end of this section).

### Gates

- `maran check`: all 8 toolchain items OK. `maran structure`: STRUCTURE-OK.
- `maran format --check backend`: FORMAT VERDICT OK, before and after the test-double fix.
- `maran test rust`: TEST VERDICT OK, 30 targets, 2248 passed / 0 failed / 141 ignored. The first
  `cargo fmt --check` failed on four agent files; `cargo fmt` fixed them and the recheck is clean.
  `cargo clippy --all-targets --all-features -- -D warnings` and
  `RUSTDOCFLAGS=-D warnings cargo doc --no-deps --workspace` are clean.
- Baseline: four rows in `scripts/test-baseline.txt` rose (maran_agent 351 to 372, handshake 8 to 9,
  maran_agent_core 404 to 424, maran_ops 1347 to 1348); nothing fell. `--accept` stripped the file's
  comment prose, so it was restored by hand and only those four rows re-applied. The baseline edit
  belongs in its own commit. Backend baseline rows were NOT re-accepted.
- Backend, run one project at a time: SharedKernel 230/0, Sdk 212/0, Host.Tests 346/0, Ftp 128/0,
  ArchitectureTests 80/0, Host.IntegrationTests (ReservedHostName filter only) 47/0. Sites and Ssl
  first showed 2 failures each: the two test doubles matched only the exact panel name, while the
  real directory also treats the `www.` form as the panel's own. Production code was right; the
  stubs were fixed. Re-run: Sites 219/0, Ssl 205/0.

### Measurement (before = dev, after = this branch, byte-identical to the current tree)

- Rust `Domain::parse`: before, all 18 sampled names were accepted. After, `203.0.113.7`,
  `127.0.0.1`, `12345`, `1.2.3`, `0x7f.1` give NumericFinalLabel; `localhost`, `foo.localhost` give
  LocalhostFamily; `localhost.localdomain`, `x.local`, `x.internal`, `x.lan`, `foo.home.arpa`,
  `x.invalid` give SpecialUseSuffix. Controls `panel.example.com`, `shop.example.com`, `shop`,
  `shop.example.com2`, `my-site.example.org` stay accepted.
- Real Host over HTTP (Testcontainers PostgreSQL, counting agent double, `Panel:HostName` set by
  the probe): before, create-site for `203.0.113.7` and `panel.example.com` returned 201 and
  issue/install certificate for those names got past validation; 3 agent create calls. After, all
  of those return 400 with the reserved message, `shop.example.com` still gets 201 and past
  validation, and only 1 agent call is observed.

### Mutation proof

- Rust: 23 mutations (final-label selector, numeric predicate, localhost, `.localhost`, each of six
  suffixes, label boundary, reason order, lower-casing, parse/parse_existing, shape order, message
  text, and the three call sites that must use `Domain::parse`): all KILLED, restores verified
  with `cmp`.
- Backend (scored per owning project, not whole-suite): 43 mutations, 41 killed; 12 more on the
  integration project, all killed. Two survived, both EQUIVALENT: the `localhost` and `_` checks in
  `PanelHostDirectory.Normalise` are unreachable because `HostNameRule.IsHostName` already refuses
  every single-label name.
- Installer: 8 of 8 mutations killed by the polygon host-name assertions, run on copies in a
  throwaway container. One assertion helper was repaired because it reassigned a readonly
  variable.

### Unverified, stated plainly

- `maran mutate` DID NOT RUN against the whole backend solution (OOM under the memory cap), and
  the whole backend suite could not be run on this workstation. Backend mutation scores are
  per-project, a deviation from rules/testing.md. Host.IntegrationTests outside the ReservedHostName
  filter, Agent.Client.Tests and the other module projects are left to CI.
- No nginx was involved: the issue's `nginx -T | grep server_name` check was not observed, nor
  distribution default-server behaviour, nor ACME against a real CA. The agent was a counting
  double in the HTTP measurement; the unix-socket handshake test covers the agent's own refusal at
  the three doors, in process, without root.
- The installer polygon was not run end to end; it stops on an unrelated MariaDB assertion in this
  image. The host-name block passed.
- The agent still does not refuse the panel's own name; a compromised maran-api calling the agent
  directly can still create that vhost. Closing it needs a new agent input and is an owner
  follow-up.
- Not probed: uppercase and trailing-dot spellings over HTTP, IDN names, `www.<panel name>`
  over HTTP, missing or malformed `Panel__HostName` in a live host, and the legacy reserved-named
  vhost delete/disable path (finding 1 and checklist item 12 stay open for the reviewer).
- Doc follow-ups (CHANGELOG, the rules/*.md layout lines for `reserved_host_name` and
  `ReservedHostNameRule.cs`, the dead twin files `validation/domain.rs` and `domain_error.rs`)
  need owner approval or a separate change; no rules file was edited.

### Second review: OUTSTANDING

This change is SENSITIVE under rules/security.md. The agent reviews and gates above are evidence,
not the human second review. That review has NOT happened, and the branch must not merge to main
until it does.

## Closing criterion (from the issue, restated)

- Before: create-site with `domain=<server IPv4>` and with `domain=$(hostname -f)` returns 201.
- After: both return 4xx with the reserved-name resx key; the agent rpc called directly with the
  static names returns `InvalidInput`; unit tables list IPv4 literals, `localhost` forms and special-use
  suffixes as refused with `shop.example.com` accepted as the positive control.
