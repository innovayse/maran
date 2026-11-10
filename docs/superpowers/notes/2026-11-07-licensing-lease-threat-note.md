# Threat note: licence entitlements, the request-time gate and the lease seam (issue #8)

`rules/security.md` names licence verification among the surfaces that need a second reviewer and a
written threat note before the change. This change decides, on every request, whether a paid module
may run, and it reads a signed object (the lease) that arrives from outside. It is written BEFORE the
code: every statement below is a requirement the code must meet, not a description of code that
exists. Where the code lands and differs, the code is wrong or this note is amended in the same
change.

**Second reviewer: OUTSTANDING (#331).** The work is carried out by agent sessions, which cannot
obtain one. The branch may exist and MUST NOT merge to `main` until a human reviewer has read this
note against the code and checked every item under "What a reviewer must check". The owner is told at
hand-off that this surface carries an outstanding review.

This note builds on `2026-09-22-licence-verification-threat-note.md` (offline verification; second
review RECORDED 2026-09-23; its attacker model in section 1 and its failure-direction argument in
section 2 still hold) and `2026-09-22-licence-installation-threat-note.md` (the write side). It does
not repeat them and does not reopen them.

## State of the tree when this note was written (2026-11-07, branch `feature/146-425-security`)

Measured by reading the tree:

- Shipped, in `backend/src/Maran.Modules/Licensing/`: offline Ed25519 verification
  (`Services/LicenceVerifier.cs`, `Ed25519LicenceSignatureVerifier.cs`), the three-state status
  (valid, absent, refused with a reason), machine-id binding, the install endpoint with an atomic file
  writer and an install lock, `GET api/v1/licence-status`, a start-up check that never throws, and an
  audit journal.
- **Nothing consumes a licence verdict.** `backend/src/Maran.Host/Modules/ModuleStateResolver.cs` is
  still `Of(LicenceTier tier)` and maps `AddOn` and `PlanGated` to `Locked` unconditionally. The
  filter family in `Maran.Sdk/Filters/` has no licence filter. Every shipped module declares
  `LicenceTier.Included`, so nothing is exposed today.
- **There is no lease and no grace policy.** A word search for "lease" in `backend/src` finds only
  unrelated concurrency and idempotency leases, plus the interface below.
- **An uncommitted, untracked file already exists:** `backend/src/Maran.Sdk/Interfaces/ILicenceLease.cs`
  (`ExpiresAt`, `IsEntitled(moduleId)`, `DecryptionKeyFor(moduleId)` returning key bytes). It belongs
  to the module-loader work (#184), not to this change, and it differs from what this note requires:
  it is a window that can carry key material. See surface 1 and decision D4.
- In the same working tree, uncommitted and owned by other work: `Maran.Host/Modules/ModuleDto.cs`,
  `ModuleRegistry.cs`, `ModulesEndpoint.cs` (plus new untracked files in that folder) and
  `Maran.Modules/Licensing/LicensingManifest.cs` (now `ModuleKind.Core`, no schema, no menu, no
  route). The module-state resolution this change must edit is therefore in motion; sequencing is
  below.
- The closed Rust/NativeAOT binary and the plug-in loader do not exist in this repository and this
  change does not create them. It ships only the open interfaces, the open lease check, the grace rule
  and conformance vectors.

## What is added

1. **`ILicenceEntitlements`** (Maran.Sdk): a read-only window answering, for one module id, one of
   Covered, NotCovered or Lapsed, plus the effective end of cover. It exposes the narrowest value that
   answers the question: no licence text, no signature, no machine id, no lease body, no key.
2. **`RequiresLicensedModule`** attribute and filter (Maran.Sdk/Filters): a controller of a module
   that is not `Included` is refused with 403 and the code `ModuleLicenceRequired` unless the window
   says Covered. The administrator is refused too: the licence binds the server, not the role.
3. **`ModuleStateResolver` reads the same window**, so the menu and the gate cannot disagree. The menu
   is advice; the gate is the boundary.
4. **The lease seam:** `ILicenceLeaseSource` (Maran.Sdk) with an open default that reads
   `/var/lib/maran/lease.json`; an open Ed25519 check of the lease signature against a lease key
   pinned in the build; `LeaseGracePolicy`, a pure rule over (lease times, clock). Who FETCHES a lease
   is not in this change (decision D4, #186): it reads a file that something else writes, the same
   shape as the licence file.
5. **A licence screen** for the administrator (status, install, lapse notice), and a read-only
   `Licence:` line in `maran status`. Both only display and install; neither decides.

## What an attacker could do with each surface, and why it is safe once the requirements hold

### 0. The adversary, restated

Same as section 1 of the verification note: the administrator who holds root and may read and patch
this source (BSL 1.1). Nothing here is a proof against that person and this note does not pretend
otherwise. Every claim below is scoped to the honest majority; the deterrent case. What the gate buys
is that copying a file, letting a licence or lease lapse, or moving a licence to another machine has
the effect the product says it has.

### 1. The window carries no secret

- `ModuleEntitlement` is a record of module id, verdict and an optional end date. A reflection test
  lists its members so a later addition of a text, key or signature member fails the build.
- The key-bearing `ILicenceLease.DecryptionKeyFor` of the loader work is NOT this window. If that
  interface and this one both answer "is this module covered", they drift, which is the failure
  `rules/architecture.md` names for shared facilities. This note assumes (D4) that the loader's lease
  interface becomes this window plus the key accessor, owned by one module, and that the key accessor
  is reachable only by the loader and never by a controller or filter.

### 2. Fail closed on a paid module, never on the core

- The gate sits only on controllers of non-Included modules. A bug, a missing licence file, a clock
  skew or an unreachable lease source can refuse a paid module. It can never refuse sign-in,
  `/health`, the licence status endpoint, the licence install endpoint, a backup, a restore, or the
  `maran` CLI. This is section 2 of the verification note applied to the gate.
- The window never throws: any failure maps to NotCovered for a non-Included module and is logged once
  at the boundary. `LicenceStartupCheck` keeps calling the verifier directly and is NOT routed through
  the cache: it is the "core never dies" probe.
- A throwing window refuses the paid test endpoint and still serves `/health` and the status endpoint,
  observed on the wire, not asserted from a mock.

### 3. Window absent

If no `ILicenceEntitlements` is registered (Licensing not composed), a paid controller refuses
(NotCovered). Included modules do not depend on the window at all.

### 4. Lease forgery, replay, clock games

- The lease is verified with Ed25519 against a pinned key. It is bound to the licence id it names and,
  when the licence is machine-bound, to the machine id, and it carries `IssuedAt` and `ValidUntil`.
- A replayed OLD lease is accepted only while its own `ValidUntil` holds. This is the exposure of any
  offline token and is bounded by issuance to 14 days.
- A lease for another licence id or another machine is refused with its own reason. A lease dated in
  the future beyond a stated skew bound is refused (a forged "fresh" lease). A clock moved backwards
  cannot extend one: the rule uses the injected clock only.
- Stated limit: a root administrator controls the clock. This is the deterrent case again.

### 5. Downgrade by deleting the lease

Deleting `lease.json` moves a leased licence to Lapsed after the grace, never to Covered. The absence
of a lease and the absence of a licence are different states, as Absent differs from Refused in the
verification note.

### 6. The write path

Whatever writes `lease.json` (D4) uses the existing atomic writer shape (temporary file, fsync,
rename, mode `0640`, owner and group as the licence file) under the existing install lock. The open
core accepts a lease only after verifying it and never writes an unverified one. Reading is bounded in
size and never throws.

### 7. Logging

The journal records state transitions (covered to lapsed and back) with licence id and module id. It
never records the lease body, a signature, a private or derived key, or the machine id raw, as in
section 4 of the verification note.

### 8. Surface (rules/security.md item 10)

This change adds no port, no daemon and no outbound call. The lease exchange is the one documented
phone-home; the panel's call is NOT part of this change. (Superseded by the fourth amendment: the call is now built and is the fifth exception of item 10.) Before any client code merges, item 10 must
name the hostname and the data sent (the licence id and nothing else is the intent), and that is the
owner's change to the rules, not this note's. Until then this change reads a file and nothing more.

### 9. Test keys

The conformance vectors and every test use TEST keys only. A test asserts the pinned production lease
key is not the test key, and the reverse. Whether the pinned production key is the cloud side's real
key cannot be known here (the build holds a test key until the owner supplies one).

## What a reviewer must check

1. The gate is on every controller of every non-Included module. The architecture test lists them.
   Because no shipped module is non-Included, that census is vacuous on today's tree; it must carry a
   planted violation and an accepting control (a test-only paid module with a controller with and
   without the attribute), so it cannot pass by finding nothing. Background jobs of paid modules call
   the window directly; there is no mechanical check for that, and it is a review item.
2. There is no path from a licence or lease failure to an exception in start-up, health, sign-in or
   the status endpoint. `LicenceStartupCheck`'s contract is unchanged and still tested.
3. `LeaseGracePolicy` uses the injected clock only. There is a unit test per transition (fresh, inside
   grace, expired, future-dated, wrong licence, wrong machine), each with an inverse control, and the
   boundary equalities (valid-until equal to now, skew bound) are exercised.
4. Test keys never replace production keys and the reverse.
5. The window's return type cannot carry secret material (read the record; the reflection test backs
   it).
6. `ModuleState`, the catalogue endpoint, the menu and the gate give the same answer for the same
   input: one resolver, no second copy of "locked".
7. Cache invalidation on install: after a successful install the next request sees the new verdict;
   a failed install leaves the old verdict.
8. The licence text pasted into the screen is rendered nowhere as HTML and appears in no log.

## What the author could not verify

- Clock behaviour across suspend and resume, and a clock stepped by an NTP correction near a lease
  boundary.
- How the closed loader will read the window; no closed code exists to read.
- Whether the pinned lease key will be the cloud side's real key.
- That the working-tree edits to the module catalogue files (`ModuleDto`, `ModuleRegistry`,
  `ModulesEndpoint`, `LicensingManifest`) are complete or compatible with this change's edits.
- That the installer polygon and the real-host licence-survival assertions (licence still verifies
  after `maran update`, after `maran rollback`, after `panel-restore`) work today; they are a later
  task of the plan and have not been run.
- Nothing was built or run by the author of this note.

## Owner decisions this note assumes

None is recorded as approved; these are the recommendations of `research_notes/plan-8.md` section 3.
If the owner answers differently, this note is amended before the affected code.

- D1: "degraded" for a lapsed paid module means refused on every request with `ModuleLicenceRequired`,
  data untouched, a plain notice in the panel, the module's data still reachable by the existing
  backup path. (Alternative: read-only.)
- D2: a lease is mandatory for every paid module, except a licence whose signed payload says it is an
  offline licence (the enterprise offline bundle), which is covered by its own expiry alone. This adds
  one optional signed field to the licence envelope (additive). Without a lease there is no
  revocation of a stolen licence before it expires.
- D3: a separate pinned Ed25519 key for leases, so a leaked lease key does not mint licences and the
  reverse.
- D4: the panel API makes the daily call through the named outbound pipeline, as part of #186's client;
  this change ships only the file seam and the read side. The loader's lease interface (#184) is this
  change's window plus the key accessor, not a parallel interface. Item 10 of `rules/security.md` is
  amended first.
- D5: until the manifest carries a required-tier field (#182), `AddOn` and `PlanGated` are both
  covered exactly when the licence's signed module list contains the module id; the licence's tier
  string is display only.
- D6: which v1 modules are paid is a business decision not made here. With every shipped module
  `Included`, this change alters no customer-visible behaviour except the catalogue's end date and the
  new screen.
- D7: Licensing joins the module registry once the screen exists, and the comment in the registry
  that says it is deliberately absent is rewritten in the same change.
- D14: the rules map edits listed in the plan (Sdk interfaces, contracts and filters; the pointer in
  `rules/architecture.md`) are approved as part of the changes that make them. This note edits no
  rule.
- Sequencing assumption: this change lands after the in-flight module-catalogue edits, and the
  licence-facing seams land before the marketplace loader work (#182-#188) uses them.

## Amendment 2026-11-09: what was built, and where it differs from the requirements above

The requirements above were written before the code. This section states what the code that landed
does, so a reviewer reads the difference and not only the intent. Owner decisions are applied as
RECOMMENDED in `research_notes/owner-decisions.md` items 64 to 74 (D1 refuse with
`ModuleLicenceRequired`; D2 lease mandatory unless the signed payload says `"offline": true`; D3 a
separate lease key; D4 and D4' one rule behind two windows; D5 module list decides; D6 every shipped
module stays `Included`; D7 Licensing joins the registry; D14 rules map edits; D15 vectors published).
None is recorded as approved by the owner; if one is answered differently this note is amended first.

**Built as required.**

- `ILicenceEntitlements`, `ModuleEntitlement` (module id, verdict, end date and nothing else; a
  reflection test lists the members), `EntitlementVerdict` in `Maran.Sdk`.
- `RequiresLicensedModuleAttribute` and its filter: 403 `ModuleLicenceRequired` for everyone, the
  administrator included; a missing window, a window that throws and any verdict but Covered refuse.
  The licence status and install endpoints carry no gate.
- One rule, `ModuleCoveragePolicy`, read by the gate, by `ModuleStateResolver` (so the catalogue and
  the boundary cannot disagree) and by a paid module's own work. `ModuleState` gained `Lapsed`;
  `ModuleDto` gained `ValidUntil`.
- The lease: `ILicenceLeaseSource` (Sdk), `FileLeaseSource` (bounded to 16 KiB, never throws, payload
  `kind` must be `lease`), `Ed25519LeaseSignatureVerifier` over a lease key type of its own
  (`LeaseVerificationKey`, embedded from `installer/keys/lease-verification.pub` when the file
  exists, otherwise none), and `LeaseGracePolicy` (injected clock only; renewal interval one day,
  skew bound five minutes; the grace is the lease's life).
- `LicenceCoverageCache`: 30 seconds, driven by the injected clock, invalidated by a successful
  install and not by a failed one. `LicenceStartupCheck` still calls the verifier directly.
- Conformance vectors with TEST keys only (`docs/licensing/`), run through the real sources in
  `ConformanceVectorTests`; an architecture test that the committed lease key is not a committed seed
  and is not the licence key.

**Differences from the requirements, each a reviewer item.**

1. `LicenceStatus.Refused` now optionally carries the authentic licence for the `Expired` reason
   (`LapsedLicence`), so an expired licence reports the modules it named as Lapsed rather than all
   modules as uncovered. The licence's signature had held when it is set. It is read for that and
   grants nothing. This touches the verification surface whose second review was recorded on
   2026-09-23; the change is additive and the existing tests are unchanged.
2. The licence payload gained one optional signed member, `offline`. Absent means false. A member
   that is present and not a boolean is Malformed. Because it is inside the signed payload, editing
   it into an installed licence breaks the signature (a test edits it and expects
   `SignatureInvalid`).
3. A missing lease is **Lapsed at once** for a non-offline licence. The requirement said "after the
   grace", but without a lease there is nothing to measure a grace against. Nothing wrote
   `lease.json` at the time (the daily renewal is built in the fourth amendment below), so on a panel that sold a paid module every
   non-offline licence would lapse; D6 keeps every shipped module `Included`, which is why this
   changes no customer-visible behaviour today.
4. **State transitions are logged and, since the second amendment below, also written to the audit
   journal.** The window is a singleton and the journals are scoped, so the first failure to read the
   licence and the recovery are logged once (error and information); a lapse and a recovery of a module
   are journalled by the watcher described in the second amendment. The audit entries for install and
   status read are unchanged.
5. Licensing is now in `ModuleRegistry`; its query and command are dispatched through Wolverine like
   every other module's (the controller no longer resolves handlers directly), and the Host's
   `AddLicensingStartupCheck` registers only the hosted service. The module's agent reach
   (`Monitor`) is now checked by `AgentCapabilityGuard` through the registry like every module's.
6. `ILicenceLease` (the module loader's window, #184) is NOT implemented by this change. D4' says its
   coverage answer must come from this rule; that is done by whoever implements it, calling
   `ILicenceEntitlements`. No class implements it today (only a test fake does).
7. **Superseded (second amendment):** `SearchModuleGate` asked `ModuleStateResolver.Of(tier)`, which
   failed closed for a paid module, so a licensed paid module's search results were hidden. It now asks
   the licence window.
8. **Superseded (second amendment):** the wire-level test of the gate is written
   (`LicensedModuleGateTests`).

**What the author could not verify (added to the list above).** Nothing in this change was built or
run: the session that wrote it was under a coding-phase lock that forbids builds and tests. Every
claim about compilation, test results, the Wolverine handler discovery after the conversion in item
5, and the OpenAPI and Sdk-surface baselines is unverified until the gate runs. The clock behaviour
and the real lease key are unverified for the reasons already listed.

**Additional reviewer items.**

9. The cache lifetime means a lapse is seen up to 30 seconds late. Confirm that is acceptable against
   a 14 day lease.
10. Confirm `FileLeaseSource` treats an unreadable file as Absent (a lapse) rather than as an error,
    and that no log line carries the lease text, a signature or the machine id (the only fields logged
    are the exception type name).
11. The `status` command line (`licence-status` verb, `Licence:` line in `maran status`) prints the
    tier, the expiry day and the lease standing, never the licence id, text, signature or machine id.

## Second amendment 2026-11-09: the audit of a lapse, the search gate and the gate on the wire

**What changed.**

- `LicenceCoverageWatcher` (Licensing, hosted by the Host in `BackgroundWorkExtensions`) reads the same
  `LicenceCoverageCache` the gate reads every five minutes (first pass one minute after start) and
  writes `LicenceModuleLapsed` and `LicenceModuleRecovered` through `LicensingAuditJournal`, as the
  panel (`SystemAuditEntry`, actor `maran-licensing`), once per change. `CoverageTransitionPolicy` is
  the one rule of which changes are journalled: into Lapsed from anything but Lapsed, and out of Lapsed
  to Covered. The subject is `module=<id>;from=<verdict>;to=<verdict>`: the module id and two verdict
  names, never the licence id, text, signature or machine id.
- `SearchModuleGate` asks `ILicenceEntitlements.ListAsync` once for the paid composed modules and
  applies `ModuleStateResolver.Of(tier, verdict)`. The tier-only overload that encoded "a paid module
  is locked" is removed. An included module consults no licence; a panel with no paid module never asks.
- `LicensedModuleGateTests` (Host integration) boots a paid probe controller behind
  `[RequiresLicensedModule]`, installs a licence signed with a test-only key through the real install
  endpoint, and observes on the wire: refused without a licence, served under one that names the
  module, refused after it expires (a moved clock), refused under a licence naming another module or
  an online licence with no lease, with `/health`, the licence status and sign-in served in every state.

**What an attacker could do, and why it is safe.**

- *Fill the audit trail by flapping the licence.* Entries are written on a change of verdict only, at
  most one per module per pass, and only for modules a licence names; the administrator who can flap
  a licence file is root, the adversary this note does not defend against.
- *Read the licence through the journal.* The subject carries module ids and verdict names. The entry
  carries no account: the watcher runs with no caller, so the journal's writer stamps none. A lapse
  noticed inside a customer's request would have been filed under that customer's account, which is
  why the write is not made from the gate.
- *Make the watcher kill the core.* Every pass runs inside one catch that logs the exception type; a
  journal failure leaves the remembered verdict unchanged so the next pass retries, and no licensing
  failure reaches the host.
- *The search stays hidden or leaks.* The search asks the window the gate asks; a covered module is
  searched and a lapsed or uncovered one is not. A module the window does not name is not covered.

**Differences and limits, each a reviewer item.**

12. The entry is written up to five minutes plus the cache's 30 seconds after the cover ended, and
    carries the time it was noticed. A lapse that began and ended between two passes is not seen. The
    refusal of the module is immediate and does not depend on the watcher.
13. The watcher's memory is in the process: after a restart a module found lapsed is journalled again
    (`from=Unobserved`). There is no persistence of the last verdict.
14. The wire test uses an offline test licence for the covered state, so the lease is not on the wire
    path there (the lease is covered by unit tests over the real sources and an online-licence-without-
    lease case). The production verification key is not exercised: a test key is injected through the
    container.

**What the author could not verify.** Nothing here was built or run (coding-phase lock). The signing
of the test licence in `SignedTestLicence` mirrors `LicenceEnvelopeBuilder`; whether the host's bearer
handler accepts the administrator's token across the moved clock (a licence life of ten minutes against a
fifteen-minute token was chosen for this) is unmeasured until the gate runs.

**Owner decision recorded.** D12: `proptest` is added to the agent as a dev-dependency (see the agent
change below); it is not part of this surface.

## Third amendment 2026-11-09: the lease leaves the module as a bearer credential

`LeaseRequestAuthorizer` (the Sdk's `ILeaseRequestAuthorizer`, answered by this module) hands the verified lease back to
the licence-bound services as `Authorization: Bearer base64url(payload).base64url(signature)` (`LeaseBearerToken`,
contract in `docs/marketplace/api-v1.md`). It reads the signed bytes through the module-internal `ILeaseEnvelopeSource`
(implemented by `FileLeaseSource`, which returns them only for a lease whose signature held), and attaches only when
`LicenceCoverageCache` reports a valid licence and a Fresh or RenewalOverdue lease, so the same judgement that gates a
paid module decides whether the panel has a credential. An offline licence presents none. It is the first code in which
a lease's signed bytes exist outside `FileLeaseSource`; `SignedLeasePayload` is never logged, journalled or kept. The
adversary of section 0 (root) can read the file anyway; against everyone else the bytes are reachable only by the
marketplace transport an administrator drives. Residual: a bearer is replayable until `validUntil`. Not built or run.

## Fourth amendment 2026-11-09: the daily renewal writes the lease (owner decision 46, option a)

Until this amendment nothing in the panel wrote `lease.json`, so on a panel that sold a paid module every online
licence lapsed at once (difference 3 above) and a marketplace grant failed with `LeaseRequired`. The renewal is now
code, and with it the one outbound call this note's section 8 said was not part of the earlier changes. Applied
from the recommended option of `research_notes/owner-decisions.md` item 46 (D4: the panel API makes the call through
the named outbound pipeline, `rules/security.md` item 10 names the host and the data, the licence id and nothing
else); not recorded as approved by the owner beyond that list, and this note is amended first if he answers
differently. `rules/security.md` item 10 now carries it as the fifth documented exception, and the contract is
`docs/marketplace/api-v1.md` ("Lease renewal") and `openapi-v1.yaml` (`POST /lease`). Written with the code, in a
session that cannot obtain a second reviewer: the requirement is OUTSTANDING below.

**What was built** (`backend/src/Maran.Modules/Licensing`):

- `Services/LeaseServiceClient` is the only type in the module that touches an HTTP client: one `POST` to
  `<LeaseServiceBaseUrl>/api/v1/lease` with `{"licence":"<id>"}`, no other header or credential, a handler with
  automatic redirects and cookies off, the answer read up to 16 KiB and no further, the deadline set by the Host's
  `OutboundHttpPipeline` on the named client `licence-lease`. The address must pass `PublicUrlRule` (an https origin)
  each time it is used; a value that fails makes the pass do nothing and log a warning, and cannot stop the panel
  starting.
- `Services/LeaseEnvelopeReader` is the one parser of the lease format, extracted from `FileLeaseSource`: the answer is
  judged by the same code, the same pinned key and the same payload rules as the stored file, so a lease is judged the
  same way going in as coming out.
- `Services/LeaseRenewer` is the pass. It does nothing for a missing, refused or expired licence and nothing for an
  offline licence (no call is made), and asks only when `LeaseRenewalPolicy.IsDue` says the stored lease is missing,
  for another licence, future-dated or at least 20 hours old. It writes only when the answer is a verified lease that
  `LeaseGracePolicy` stands as Fresh for THIS licence and THIS machine, `LeaseRenewalPolicy.Improves` says it ends later
  than the lease standing now, and the licence installed inside `LicenceInstallLock` is still the one asked about.
  `FileLeaseWriter` writes it with the licence file's atomic writer, mode 0640. Then `LicenceCoverageCache` is
  invalidated so the gate sees it.
- `Services/LeaseRenewalWorker` is the schedule (registered by the Host): a first pass 2 to 10 minutes after start, then
  22 to 24 hours after a stored lease, 6 to 7 after a failure, 2 to 2.5 when the lease was recent enough, 1 to 1.17 when
  there was nothing to renew (no licence, offline, no address). All waits are `LeaseRenewalPolicy`'s, jittered.
- Audit: `LicenceLeaseRenewed` (subject `outcome=Renewed;validUntil=<instant>`) and `LicenceLeaseRenewalFailed` (subject
  `outcome=<word>;consecutiveFailures=<n>`), as the panel, never the licence id, the lease text, the signature or the
  machine id. Alert: after three consecutive failed passes `LeaseRenewalAlerter` raises one operator alert through
  `OperatorAlertDispatcher` (journal, mail, webhook, feed), and one resolving alert after the next stored lease.
- The public demonstration replaces the client with `DemoLeaseServiceClient`, which sends nothing.

**What an attacker could do, and why it is safe.**

- *Make the panel store a lease it should not.* A forged answer fails the Ed25519 check; a genuine lease for another
  licence or machine fails `LeaseGracePolicy`; an old genuine lease replayed fails the Fresh check (issued within a day,
  not in the future); a genuine lease that would shorten cover fails `Improves`. A man in the middle on the TLS path can
  do none of these without the lease key, and can only drop or delay the answer, which leaves the old lease standing.
- *Replace a good lease with garbage, or leave a half-written file.* Nothing is written before every check passes, and
  the write is one rename of a temporary file in the same directory, so a reader sees the old file or the new one. A
  failed write returns `WriteFailed` and leaves the old lease untouched.
- *Make the panel call somewhere else.* The only address used is `Licensing__LeaseServiceBaseUrl`, which is panel
  configuration (root-owned `panel.env`), checked as an https origin on every use. No redirect is followed, and the
  request never takes an address from a response. An administrator who sets the variable is root, the adversary this note
  does not defend against.
- *Learn more about the server than item 10 allows.* The request is the licence id. The panel sets no custom user
  agent. The source address is visible, as with any HTTPS request. The log carries only the exception type of a failure;
  the address, the id and the body are never logged.
- *Use the renewal to stall or flood the core.* The pass runs on its own hosted service inside one catch, off every request
  path; the gate reads the stored lease, not the network. A failure waits hours, not seconds; there is no retry inside a
  pass. A hostile or broken service can make the panel read 16 KiB once per attempt.
- *Keep a revoked or stolen licence alive.* The renewal is how a revocation takes effect: the service stops issuing, the
  last lease runs out within fourteen days, the paid modules lapse. The panel cannot tell a revoked licence from an
  unreachable service, so it keeps the old lease until it ends, which is the grace the product promises.
- *Race an install.* The final judgement and the write run inside `LicenceInstallLock` and re-verify the installed
  licence, so a lease issued for a licence an administrator has just replaced is dropped (`LicenceChanged`).
- *Flood the audit trail or the inbox.* At most four failure entries a day while the service is down; one alert per run
  of failures and one resolution.

**Differences and limits, each a reviewer item.**

15. The run of failures and the "alert raised" flag live in the process. After a restart the count starts again, so an
    outage across a restart alerts after the new process has failed three times itself, and a recovery after a restart
    resolves no alert the new process did not raise. No persistence was added for a daily job.
16. `NotConfigured` (an address that fails the https-origin check, or the demonstration's client) is not a failure and
    raises no alert: no request was attempted, and a public demonstration must not alert. It is logged at warning on
    every pass. A typo therefore shows as a log line and, after up to fourteen days, as the watcher's lapse entry.
17. The first lease arrives up to about an hour after a licence is installed on a panel that had none (a pass with nothing to
    do waits an hour), and up to a day after one that replaces another (the wait after the previous stored lease). Until
    then a non-offline licence's paid modules are lapsed, as before this change.
18. The pass reads the machine id through the agent exactly as the verifier does. A panel whose agent is down judges
    the answer as for a host with no machine id, so a lease that names a server is refused until the agent answers; the
    old lease stands meanwhile.
19. The service decides what `issued` and `validUntil` are; the panel requires only that `issued` is within the last
    24 hours and not in the future and that `validUntil` is later than the standing lease. A service clock more than a
    day wrong makes every renewal `AnswerRejected`, which alerts.
20. The `Licensing__LeaseServiceBaseUrl` default is the documented host; whether that host serves `POST /api/v1/lease`
    as `docs/marketplace/api-v1.md` specifies is the cloud team's to build and cannot be checked from this repository.

**Tests that hold it** (written, not run): `LeaseRenewerTests` (stores a good lease; refuses, with the old lease intact, a
forged, another-licence, another-machine, expired, future-dated, day-old, non-extending, oversized, unsigned and
malformed answer; asks nothing for an offline licence, without a licence, or when the lease is recent; drops an answer
for a replaced licence; survives a failed write), `LeaseServiceClientTests` (the request carries the licence id and no
credential, no redirect is followed, the body is bounded, statuses classify), `LeaseRenewalPolicyTests` (due, improves,
delays with their jitter bounds, alert decision), `LeaseRenewalWorkerTests` (journal entries, the alert after three
failures and its resolution), `LicensingOutboundIsGatedTests` (only `LeaseServiceClient` touches the network, only the
extension registers the client, only the two named types run on their own), `OutboundHttpPipelineTests`, and the Host
registration test. Each refusing case sits beside an accepting control.

**What the author could not verify (added).** Nothing in this change was built or run (coding-phase lock). No
`lease-verification.pub` is committed, so a stock build verifies no lease and every answer would be `AnswerRejected`
until the owner supplies the cloud side's key. The service that answers is not in this repository. The Host's
composition of the new hosted service and the pipeline on the named client is covered by tests that have not been run.

**Reviewer items to add to the list above.** 15 to 20, and: the renewal sends nothing but the licence id (read
`LeaseServiceClient.RequestAsync`); nothing outside `LeaseRenewer` calls `ILeaseWriter`; the final judgement is inside
the install lock; the demonstration's client makes no call.

## Second review: OUTSTANDING (#331)
