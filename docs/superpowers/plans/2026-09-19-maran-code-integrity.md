# Maran Code Integrity — the open-repo half

Normative for whoever executes it: task-by-task, implementation first, tests in the dedicated pass
that follows each task (rules/testing.md). Every claim below carries `path:line`; anything that does
not is marked NOT FOUND or NOT KNOWN rather than assumed. Extends
`docs/superpowers/specs/2026-09-19-maran-code-integrity.md` (the addendum this plan implements) and
the base spec §9/§10/§13/§14/§15/§18.

## 0. What this plan cannot promise before it is run

Written by reading, `grep`, and `git log` only — no build, no `maran test`, no `maran mutate`. Three
other lanes hold the tree's write-lock (`agent/crates/ops/src/accounts/` +
`backend/src/Maran.Modules/{Monitoring,Accounts}/`; `installer/` + `scripts/`; `docker/`), so every
file path this plan names under those trees is a citation of what exists TODAY, and the task that
implements it must re-read the file immediately before editing — another lane may have moved it.

## 1. Ground truth, established by reading

**No prior art for this feature exists in the tree.** `grep` for
`целостн|подмен|модифиц|tamper|integrity` in
`docs/superpowers/specs/2026-08-29-maran-design.md` returns nothing. `grep -rln
"PluginLoader|ILicenceVerifier|AssemblyLoadContext|LicenceLease|PhoneHome"` across `backend/src`
returns nothing. `grep "licen" proto/agent/v1/common.proto` returns exactly one hit, an unrelated
comment at line 76 about retry semantics. This confirms §13/§18's own description: the licence
lease exchange, `PluginLoader`, and the cloud service are ALL outside this monorepo today, and
nothing here can be extended incrementally — the seam this plan adds has no predecessor to match.

**The installer's transport-integrity artifact, read in full.** `installer/lib/50-artifacts.sh`
verifies a manifest's Ed25519 signature (`verify_manifest_signature`, lines 70-88) against a
baked-in public key (`MARAN_RELEASE_PUBLIC_KEY_PEM`, line 30, resolved next to the installer
package, never fetched), then each of exactly three components' sha256
(`verify_artifact_checksum`, lines 126-140, called once per component in
`download_and_verify_online`/`use_offline_tarball`, lines 143-206, and AGAIN in `unpack_artifacts`,
lines 179-206, immediately before that component's own extraction — the file's own comment at
209-217 explains why: the second check is what turns "nothing should touch the staging dir" into an
observation). `manifest_field()` (91-104) is a bare `awk`/`grep`/`sed` reader over a fixed
`{"artifacts": {"<component>-<arch>": {"url": ..., "sha256": ...}}}` shape — explicitly not using
jq, "not guaranteed present at this point in the install" (comment at 91-93).

**The build/sign/verify tooling that produces that manifest, read in full.**
`scripts/lib/release-bundle.sh` stages `api`/`agent`/`frontend` output directories
(`build_api`/`build_agent`/`build_frontend`, lines ~100-135), tars each (`tar_component`,
~137-141), writes `manifest.json` with one `sha256`/`url` pair per component
(`cmd_build`'s manifest-writing block, ~155-180), and separately signs
(`cmd_sign`, requires a key path OUTSIDE the repo, refuses one inside it —
`require_signing_key`, ~195-210) and verifies (`verify_bundle`, ~215-245, the SAME two checks
`50-artifacts.sh` performs, by the file's own comment "so a bundle this script accepts is a bundle
the installer accepts"). `cmd_selftest` (~275-330) is the inverse-control precedent this plan's
proof obligations follow: build a throwaway-signed bundle, prove it verifies, then prove a
one-byte tamper is refused and a stripped signature is refused — three outcomes, each printed with
a `RELEASE VERDICT` line.

**The Monitoring alert idiom this plan extends, read in full.**
`backend/src/Maran.Modules/Monitoring/Domain/Enums/AlertKind.cs` — a closed, documented enum;
`SftpJailDrifted = 3`'s own doc comment (lines 8-15) states the exact precedent this plan follows:
"a package upgrade or a hand edit... silently turned every SFTP login into a full shell session
while the panel kept reporting every account as jailed" — a drift check added after the fact, one
new enum member, no change to the machinery around it.
`backend/src/Maran.Modules/Monitoring/Domain/Enums/AlertTransition.cs` (Raised/Resolved/None) and
`Domain/Entities/AlertState.cs` (`Observe(bool breaching, DateTimeOffset)`, `BreachesBeforeAlert =
10` consecutive samples, lines 51-58, 156-186) are the debounce; `Services/AlertEvaluator.cs` is
the orchestrator — its `EvaluateAsync` (lines 121-186) reads three independent optional inputs
(`diskUsedPercent`, `services`, `sftpJailStatus`), and for each, a `null`/`Unknown` observation
"advances nothing and resets nothing" (doc comment at 45-51, and the `Unknown` skip at 152-155) —
the exact "unanswered check raises nothing" idiom this plan's contract must preserve.
`AnnounceAsync` (243-267) journals via the EXISTING generic constants
`AuditActions.AlertRaised`/`AlertResolved` (confirmed present in
`backend/src/Maran.Sdk/Contracts/AuditActions.cs`, not re-numbered here) with subject
`$"{kind}:{subject}"` — no kind-specific audit constant exists today for any of the three current
kinds, which is the basis for this plan's decision (§5 below) not to add one either.

**The `key=value` subject precedent and the incident it fixes, read in full.**
`backend/src/Maran.Modules/Databases/Commands/RepairDatabaseGrants/RepairDatabaseGrantsCommandHandler.cs`
lines 54-61: "The first version wrote `narrowed 1 of 1 grants, refused 0`, and a live check found it
sitting in the audit screen's subject column in english on a russian page... The `key=value;key=value`
shape... is the resolution; what must NEVER be done instead is to localize it, since the journal
outlives the locale the writer happened to be using." This plan's every subject follows that shape.

**The Sdk seam idiom, read in full.**
`backend/src/Maran.Sdk/Interfaces/IAccountResidueAuditor.cs` — a narrow read-only interface, owned
by the Sdk, implemented by whichever module can answer, with an explicit `AccountResidue.Unchecked`
case (doc comment 24-29) so "not asked" is never confused with "nothing found" — the direct
precedent for this plan's `Unavailable` outcome. `IAlertRecipientDirectory.cs` and `IPanelModule.cs`
confirm the same shape (one method, one narrow question, implementation lives in the module that
owns the data, never the reverse).

## 2. What this repository owns, and what it explicitly does not (addendum §7)

This repo owns: the hash-list artifact's production at build time; the typed contract the closed
`PluginLoader` calls into when it has an answer; the Monitoring alert extension that turns that
answer into an operator-visible signal; making the finding available to whatever already performs
the licence-lease phone-home. This repo does NOT own: the comparison algorithm (closed, private
repo, §13); the cloud service's handling of a received finding (§18, separate service); the
licence-lease wire format itself (NOT FOUND in this tree today — §13/§18's cloud side).

## 3. Tasks

### Task 1 — the hash-list artifact at build time

**Files:** `scripts/lib/release-bundle.sh` (edit — owned by the `installer/`+`scripts/` lane; this
task is a SPECIFICATION for that lane to execute, not something this write-only design lane may
edit itself).

Add, inside `cmd_build`, after the three `tar_component` calls and before `manifest.json` is
written (currently ~155): a new function `write_integrity_manifest out_dir` that walks
`${out_dir}/api/`, `${out_dir}/agent/`, `${out_dir}/frontend/` (the SAME staged directories
`build_api`/`build_agent`/`build_frontend` populate, and the same ones `tar_component` reads —
never the installed filesystem) with `find <dir> -type f` and writes
`${out_dir}/integrity-manifest.json`:

```json
{
  "version": "<version>",
  "files": {
    "api/Maran.Host": "<sha256>",
    "frontend/index.html": "<sha256>",
    "...": "..."
  }
}
```

Keys are paths relative to `${MARAN_INSTALL_ROOT}/<component>/` (matching
`installer/lib/50-artifacts.sh`'s own `${MARAN_INSTALL_ROOT}/${component}` extraction targets,
lines 179-186), prefixed with the component name so `api/`, `agent/`, `frontend/` entries cannot
collide.

**The vacuity-floor gate (kills the empty-list mutant):** immediately after writing the file, the
same function counts entries and calls `release_failed` (the existing hard-refusal helper, already
used throughout this script) if the count is below a floor constant, e.g.
`INTEGRITY_MANIFEST_MIN_FILES=50` — a number small enough not to be a maintenance burden and large
enough that "the frontend bundle alone produced zero files" or "the walk pointed at an empty
directory" cannot pass silently. **Open question for the owner** (also logged in the working log):
whether this floor should instead be a percentage of the previous release's count; this plan drafts
a fixed floor because the build script has no access to "the previous release" today.

`cmd_sign` gains one more `openssl pkeyutl -sign` call, same key, same invocation shape, producing
`integrity-manifest.json.sig` beside `manifest.json.sig`. `cmd_package` gains
`integrity-manifest.json integrity-manifest.json.sig` to its `tar -czf` file list (alongside the
existing `manifest.json manifest.json.sig api.tar.gz agent.tar.gz frontend.tar.gz`).

**Proof this task needs:**
- Mutant: `write_integrity_manifest` walks the wrong directory (e.g. only `frontend/`) and silently
  omits `api`/`agent` entries. Test: `cmd_selftest` gains a fourth outcome — build the real staged
  dirs (or, matching the existing selftest's own honesty note about `cargo build` not compiling
  right now, synthetic per-component payload files exactly as outcomes 1-3 already use) and assert
  the resulting `integrity-manifest.json` has one entry per synthetic file across ALL three
  components, not just one.
- Mutant: the floor check is inverted or removed. Test: selftest builds a bundle with fewer than
  `INTEGRITY_MANIFEST_MIN_FILES` synthetic files and asserts `cmd_build` exits non-zero via
  `release_failed`, mirroring outcome 2/3's "prove the refusal, not just the acceptance" shape.
- Inverse control: (a) an untouched bundle's `integrity-manifest.json` verifies against
  `integrity-manifest.json.sig` with the same `openssl pkeyutl -verify` invocation `verify_bundle`
  already uses for `manifest.json` — prove ACCEPT; (b) flip one hex character in one entry's sha256
  inside `integrity-manifest.json` AFTER signing, and prove the signature check on the file (not a
  content re-derivation — the signature covers the file's bytes as signed) still verifies, because
  that is the honest boundary: signature verification proves the FILE was not altered after signing,
  it does not re-derive hashes; a corrupted per-entry hash inside an otherwise correctly-signed file
  is a build-time defect the floor/count check does not catch either, and this inverse control
  documents that gap rather than hiding it.

### Task 2 — the seam: types the closed `PluginLoader` calls into

**Files (new):**
- `backend/src/Maran.Sdk/Contracts/CodeIntegrityOutcome.cs`
- `backend/src/Maran.Sdk/Contracts/CodeIntegrityReport.cs`
- `backend/src/Maran.Sdk/Interfaces/ICodeIntegrityReportSink.cs`

`CodeIntegrityOutcome` — a closed enum, following `AlertTransition`'s shape (small, closed,
doc-commented per member): `Clean = 0`, `Drifted = 1`, `Unavailable = 2`. Never a `bool`: the task
brief and `IAccountResidueAuditor.AccountResidue.Unchecked` both establish that "did not check" and
"checked and found nothing" must be different values a caller cannot collapse by accident.

`CodeIntegrityReport` — a sealed record, following `SendMailRequested`'s shape (an Sdk contract
record, immutable, doc-commented on every parameter):

```csharp
public sealed record CodeIntegrityReport(
    CodeIntegrityOutcome Outcome,
    string InstalledVersion,
    IReadOnlyList<string> DifferingPaths,
    string? UnavailableReason,
    DateTimeOffset ObservedAt);
```

`DifferingPaths` is empty unless `Outcome == Drifted`. `UnavailableReason` is non-null only when
`Outcome == Unavailable` (missing hash list, unreadable, signature failure, vacuity floor tripped at
runtime — the closed side's own concern which reason string it uses; this repo only carries it).
Doc comment states explicitly: this type is a REPORT OF FACT, never a verdict — matching addendum
§5's "never an accusation" requirement, so nobody wiring this into a UI string later invents
accusatory language the type itself never implied.

`ICodeIntegrityReportSink` — one method, following `IAccountResidueAuditor`'s and
`IAlertRecipientDirectory`'s shape exactly:

```csharp
public interface ICodeIntegrityReportSink
{
    Task ReportAsync(CodeIntegrityReport report, CancellationToken cancellationToken);
}
```

Doc comment states the calling convention explicitly, because it is the one thing genuinely
different from every existing Sdk interface: EVERY other interface in `Maran.Sdk.Interfaces` is
called BY this repo's own code (a handler asks a directory a question). This one is called FROM
the closed `PluginLoader`, loaded into the same process via `AssemblyLoadContext` per §13, so the
doc comment must say plainly: implementations must be idempotent per `(InstalledVersion,
ObservedAt)` (a `PluginLoader` retrying a report after a transient DB failure must not double-raise
an alert), must never throw for a caller that cannot itself retry meaningfully, and must return
promptly (the caller is a foreign, closed component on an unknown schedule — this repo's
implementation must not become a bottleneck it cannot see into).

**Not decided here, and stated as such:** whether `PluginLoader` calls this once per polling cycle
or only on transition — the contract accepts either (it is not itself the debounce; see Task 3) —
and the polling cadence itself is `PluginLoader`'s decision, out of this repo (open question also in
the working log, because `AlertState.BreachesBeforeAlert = 10`'s usefulness as "about ten minutes"
assumes a roughly sixty-second cadence, and nothing enforces that assumption across the closed
boundary).

**Proof this task needs:** these are plain data/interface types with no behavior — the proof is
that `Maran.ArchitectureTests`' existing module-isolation and Sdk-shape suites (NOT re-read in
detail here; NOT FOUND confirmed only for the feature-specific pieces above, general suite presence
inferred from `rules/architecture.md`'s description of `Maran.ArchitectureTests`) pass unchanged —
a new Sdk contract and a new Sdk interface are additive and referencing nothing outside
`Maran.Sdk`/`Maran.SharedKernel`, so no new violation should be possible. The task that implements
this must actually run that suite and report the real result rather than asserting it.

### Task 3 — Monitoring implements the sink, extends the alert machine

**Files:**
- `backend/src/Maran.Modules/Monitoring/Domain/Enums/AlertKind.cs` (edit — add
  `CodeIntegrityDrifted = 4`, doc comment following the `SftpJailDrifted` precedent's style: state
  what real-world event this row answers for, and cite this plan).
- `backend/src/Maran.Modules/Monitoring/Services/AlertEvaluator.cs` (edit): two new subject
  constants, `InstalledFilesSubject = "installed-files"` and `HashListSubject = "hash-list"`
  (following `RootFilesystemSubject`/`SftpJailSubject`'s existing constant style), and one new
  parameter to `EvaluateAsync`: `CodeIntegrityReport? codeIntegrityReport` (nullable — "unanswered
  raises nothing", matching every other optional input this method already takes). When non-null:
  - `Outcome == Drifted` → `ObserveAsync(CodeIntegrityDrifted, InstalledFilesSubject, breaching:
    true, ...)`, detail = the differing paths, capped (see below) and joined.
  - `Outcome == Unavailable` → `ObserveAsync(CodeIntegrityDrifted, HashListSubject, breaching: true,
    ...)`, detail = `report.UnavailableReason`.
  - `Outcome == Clean` → both subjects observed with `breaching: false`, resolving whichever was
    open (mirrors how a healthy disk reading resolves `DiskUsage` even though only one subject
    exists for that kind — here there are two, and both must be told "healthy" every time a clean
    report arrives, or a hash-list-availability alert that has nothing to do with drift would never
    resolve once drift also clears).
  - `null` (never called this round) → neither subject is touched at all, matching the existing
    `sftpJailStatus is null` skip.
- `backend/src/Maran.Modules/Monitoring/MonitoringModule.cs` (edit, exact registration line to be
  found at implementation time — `grep -n "AddScoped\|AddSingleton"` in that file first): register
  a new service, e.g. `CodeIntegrityReportHandler`, as the module's
  `ICodeIntegrityReportSink` implementation. That handler's only job is to hold a reference to
  `AlertEvaluator` (or the narrower piece of it Task 3 exposes) and call it with the incoming
  report — it does not re-implement debouncing itself, `AlertState` already owns that.
- `backend/src/Maran.Modules/Monitoring/Resources/NotificationMessages.resx` +
  `.ru.resx` + `.hy.resx` (edit): four new keys following the existing `Alert{Kind}{Transition}
  Subject`/`Body` naming (`AlertCodeIntegrityDriftedRaisedSubject/Body`,
  `AlertCodeIntegrityDriftedResolvedSubject/Body` — ONE pair of keys per transition, shared across
  both subjects, since the body text differentiates "which files differ" vs. "the hash list itself
  could not be read" by INTERPOLATING `detail`, exactly as `SftpJailDrifted`'s body already
  interpolates `FormatMissing`). **Wording is not invented here** per the assignment's explicit
  instruction — placeholder English strings only, marked `TODO(owner): review naturalness`, Russian
  and Armenian left as the same placeholder marked for translation, never machine-translated into
  the resx as if final.

**The cap on how many differing paths appear where:** the mail body may list every path up to a
fixed cap (proposed: 50, matching this plan's own vacuity-floor order-of-magnitude reasoning — an
operator reading a mail about 3,000 files needs a count and "see the panel", not a wall of text);
the audit subject is `key=value` per §5's precedent — e.g.
`version=1.4.2;count=7;paths=api/Maran.Host,agent/maran-agent,...` capped separately and shorter
(the journal is not the detailed report; `count` is the figure that matters most and is never
truncated, `paths` may be). This mirrors `RepairDatabaseGrantsCommandHandler`'s own "the subject is
a count, never [everything]" doctrine, adapted: here the identity of what changed is useful (unlike
a grant repair, nothing here can leak another tenant's data — every path is the panel's own code),
so a bounded list, not just a count, is included.

**No new `AuditActions` constant.** `AnnounceAsync`'s existing call already uses
`AuditActions.AlertRaised`/`AlertResolved` generically for every kind (verified at
`AlertEvaluator.cs`'s `AnnounceAsync`, action selection `transition == Raised ? AlertRaised :
AlertResolved`). Adding a fourth `AlertKind` costs nothing here — the generic constants already
cover it. **This is stated explicitly because the assignment says to argue either way**: the
argument for NOT adding one is that the existing three kinds (`DiskUsage`, `ServiceStopped`,
`SftpJailDrifted`) already share these two constants without any kind-specific one, so introducing
`CodeIntegrityRaised`/`CodeIntegrityResolved` would be inconsistent with the module's own established
pattern and would be the one that needs Identity's three `DisplayNames` files edited AND
`AuditActionDisplayNameTests` re-run — extra surface for zero gained information, since `subject`
already carries `CodeIntegrityDrifted:installed-files` or `:hash-list` (the existing
`$"{kind}:{subject}"` format at `AnnounceAsync`'s call site) and that is exactly what an operator
reading the journal needs to distinguish this from a disk alert.

**Proof this task needs:**
- Mutant: the `Clean` branch resolves only `InstalledFilesSubject`, leaving a stale `HashList`
  alarm open forever once the underlying availability problem is fixed. Test: unit test seeds an
  `AlertState` row firing on `HashListSubject`, feeds one `Clean` report, asserts BOTH subjects'
  rows resolve (mirrors `AlertEvaluator`'s existing per-subject test style — the concurrent lane
  owns the actual test file location; this plan specifies the case, not the file, per the lane
  boundary in the assignment).
- Mutant: the `null` branch is removed (i.e. an absent report is silently treated as `Clean`).
  Test: call `EvaluateAsync` with `codeIntegrityReport: null` when a `CodeIntegrityDrifted` row is
  currently firing, assert the row's `ConsecutiveBreaches`/`IsFiring` are UNCHANGED — an unanswered
  round must not resolve an open alert either, which is the same "unanswered raises AND resolves
  nothing" symmetry the existing `sftpJailStatus is null` case already has to satisfy and is worth
  restating for this new input rather than assuming it transfers.
- **Inverse control, the two states the assignment names explicitly:** (a) an unmodified install —
  feed a `Clean` report, assert no mail is queued and no new `AlertRaised` entry is written; (b) a
  modified install — feed a `Drifted` report naming three specific paths, assert the journal's
  subject contains exactly those three paths (or their count, per the cap) and the raised mail body
  names them, not a generic "something changed" sentence — a report that cannot name what differs
  provides no more information than a boolean and defeats the entire feature's purpose.
- **The vacuity trap at this layer:** feed a `Drifted` report whose `DifferingPaths` is EMPTY (a
  malformed input `PluginLoader` should never send, but this layer must not trust that). Test:
  assert the handler either treats an empty-paths `Drifted` as a contract violation it logs and
  refuses to journal as a clean drift (never silently downgrading to `Clean`), because an empty
  `DifferingPaths` on a `Drifted` outcome is internally inconsistent and must not be interpreted
  charitably.

### Task 4 — reaching the cloud phone-home without a new outbound call

**Files:** NOT FOUND — the licence-lease exchange this must ride on does not exist in this
repository (Task 0's grep result). This task is therefore a SPECIFICATION for the closed
`PluginLoader`/licence-client work when it lands, not code this plan can place a path against
today.

What this repo's obligation IS, stated precisely: `ICodeIntegrityReportSink`'s implementation
(Task 3) is the only place a `CodeIntegrityReport` becomes available inside the panel process.
Whatever component eventually performs the daily licence-lease exchange (§13/§18, closed, not yet
built per this grep) must read the LATEST report the same way — either by also implementing (or
composing with) `ICodeIntegrityReportSink`, or by this repo exposing a second, read-only Sdk window
(`ICodeIntegrityLastReportDirectory`, mirroring `IAlertRecipientDirectory`'s "one narrow question"
shape) that the lease client reads from when it is ready to attach the finding to its existing
outbound call. This plan recommends the SECOND shape — a read-only window Monitoring implements
alongside the sink — because it keeps the lease client from needing to know anything about the
alert machine's debounce state, and because `rules/security.md` item 10's "no new outbound calls"
gate is satisfied only if the phone-home client is the one INITIATING contact; a sink pattern where
`PluginLoader` pushes INTO the lease client risks that client depending on a component it does not
own being available first.

**Not implementable now; flagged, not guessed.** No file path is given here because none exists to
give. When the licence-lease client lands (a future lane's work, likely also closed-adjacent per
§13), it must cite THIS section before wiring anything, and this plan's Task 3 types are what it
reads from.

### Task 5 — installer awareness (documentation only, no installer code change proposed)

**Files:** none new. This task is deliberately a non-task, recorded so nobody assumes it was
missed: `installer/lib/50-artifacts.sh` unpacks `integrity-manifest.json`/`.sig` as ordinary
members of the bundle it already extracts (Task 1 adds them to `cmd_package`'s tar list, which is
the SAME tarball `use_offline_tarball`/`download_and_verify_online` already handle generically per
component — but `integrity-manifest.json` is NOT a component, it is a top-level file alongside
`manifest.json`). **Open question the `installer/`+`scripts/` lane must resolve, not this plan**:
whether `50-artifacts.sh` needs a new step to COPY `integrity-manifest.json`/`.sig` to a fixed
runtime path (e.g. `/usr/local/maran/integrity/`) so `PluginLoader` can find the list for the
CURRENTLY installed version without re-deriving it from the blue/green update symlink structure
that §14 describes but this repo's read of `installer/lib/*.sh` (only `50-artifacts.sh` was read in
full for this plan; the blue/green switch step was NOT read) does not confirm exists yet under that
name. Stated as NOT KNOWN rather than assumed.

## 4. Definition of Done, mapped to rules/testing.md's DoD (unit + integration + IDOR + audit event
   + i18n strings)

- Unit: `AlertState`/`AlertEvaluator` new-subject behavior (Task 3's proof list).
- Integration: NOT proposed as a new integration test class beyond what Task 3 specifies — this
  plan explicitly does NOT own `backend/src/Maran.Modules/Monitoring/` writes (owned by the
  concurrent lane per the assignment's boundary), so this section hands over test CASES, not test
  FILES.
- IDOR: not applicable — this feature has no per-tenant data; every finding is about the panel's
  own code, which is why Task 3 argued a bounded path list is safe to journal in full.
- Audit event: Task 3, reusing existing constants — argued, not assumed.
- i18n strings: Task 3's four resx keys across en/ru/hy, wording deferred to the owner per the
  assignment's explicit instruction.

## 5. Summary of what remains open to the owner

1. Fixed vs. percentage vacuity floor for `integrity-manifest.json`'s file count (Task 1).
2. `PluginLoader`'s polling cadence, and whether `AlertState.BreachesBeforeAlert = 10`'s ten-minute
   assumption should become a documented contract across the closed boundary (Task 2).
3. Final en/ru/hy wording for the four new resource keys (Task 3) — meaning specified, sentences
   not invented.
4. Sink-vs-window shape for reaching the phone-home exchange, and which future lane owns building
   the licence-lease client at all, since it does not exist yet (Task 4).
5. Whether `50-artifacts.sh` needs a dedicated runtime path for the two new files, or whether
   `PluginLoader` is expected to resolve them relative to the blue/green install root itself
   (Task 5) — NOT KNOWN, the blue/green switch script was not read for this plan.
