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
phone-home; the panel's call is NOT part of this change. Before any client code merges, item 10 must
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

## Second review: OUTSTANDING (#331)
