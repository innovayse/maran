# Backups (Plan 6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Archives of an account's files plus dumps of its databases, on a schedule; storage locally and S3-compatible; restore from the UI; retention. Create, restore, list and delete as server-streaming or unary agent RPCs, every operation idempotent, every one audited. Plus the §12 promise this unblocks: `DELETE /accounts/{id}` takes a final backup before it destroys anything.

**Architecture:** Same shape as Plans 3, 4 and 5. The agent gains one area (`ops::backup`) behind the `BackupService` that already exists in `proto/agent/v1/backup.proto` as a complete stub with no implementation. Every value that reaches a path, an argv array or an object key is a validated type from `agent-core`. The backend fills the prepared `Maran.Modules/Backups/` home; it talks to other modules only through Wolverine messages and `Maran.Sdk`. Scheduling reuses the panel's existing cadence machinery (`AddPanelBackgroundWork` + a module-owned job), not the Cron module — Decision 5 argues why. The SPA gains one customer screen and two admin settings screens.

**Tech Stack:** Existing stack plus ONE new agent dependency, named here so the licence pass expects it: **`object_store`** (agent `maran-ops`, `aws` feature — S3-compatible PUT/GET/DELETE/LIST with SigV4). Rationale and the rejected alternatives are in Decision 3; the fallback if `cargo deny` refuses its tree is `aws-sdk-s3`, and hand-rolled SigV4 is refused outright (rules/security.md item 9 — no home-grown crypto). `maran licenses` runs in Task 16 and must cover whichever landed.

**Spec:** docs/superpowers/specs/2026-08-29-maran-design.md — §11 (Backups), §12 (`DELETE /accounts/{id}` = final backup → deletion), §3 (v1 scope), §10 (secrets), §16 (DoD). Issue #6.

**Plan v1.** It is written after reading `.superpowers/sdd/2026-09-02-maran-cron-firewall-monitoring/progress.md` end to end — sixty rulings and the defects a live browser run found. The five traps that ledger records are the five this plan is shaped against, and each is named where it bites: a check that cannot observe what it reports on (Decision 3's bucket probe, Task 6); a doc comment describing what the code does not do (Task 4's restore doc is written FORWARD from the question, per rules/architecture.md); a warm build hiding an analyser error (Task 16 measures `--no-incremental`, Ruling 57); a fixture that omits the thing that leaks (Task 3's archive fixtures carry a symlink to `/etc/shadow` and a `../` member, because a fixture of ordinary files cannot see either defect); and completion meaning "nothing threw" (Task 4 and Task 10 — the account-deletion cascade reported COMPLETED/100 over rows it had not touched, and a restore is the same operation pointed at a live account).

---

## Implementation status — every row carries the time it was measured (latest pass: **2026-09-08 12:50–13:40 +04**)

The checkboxes below were three days behind the tree. This section is the measured state; the
checkboxes were then corrected to match it, because a plan whose ticks disagree with the code is
the same defect as a doc comment describing what the code does not do (rules/architecture.md),
one level up.

**This table has now been overtaken twice in one day.** The 21:39 +04 reading was taken while
Tasks 10, 12 and 13 were being written under it, and every "not started" it recorded for those
three is wrong on the tree that exists now. Two consequences worth keeping:

1. **`RestoreBackupCommand` is not dead code, and this is the fourth time that has been asked.**
   Every report that called it unconsumed described the tree BEFORE Task 10. Measured 2026-09-08
   03:50 UTC: `BackupsController.cs:127` publishes `[HttpPost("{id:guid}/restore")]`. Stop chasing it.
2. **The reading below was again taken on a NON-QUIET tree** — eleven MSBuild worker nodes and a
   concurrent truncation sweep writing `backend/src/**` during the pass — so it quotes only static,
   lock-free gates. No `dotnet test` and no `cargo test` were run, deliberately (rules/testing.md).

**Read every row as a dated pointer, not as a live status.** This table has now been overtaken
four times in one day, three of them within four hours, because work lands in parallel faster than
a prose row is edited — and a doc describing what the code does not do is a defect of the same
severity as the behaviour (rules/architecture.md). Fixing the rows again would only reset the
clock, so the shape is changed instead: **every row carries the time its claim was measured, and
names the report file that holds the measurement.** A row is therefore a claim about the tree AT
THAT MOMENT, and a reader can see at a glance that a row is four hours old and go to the named
report rather than believing the prose. A row whose stamp is older than the work it describes is
expected to be wrong; re-measure it, do not trust it. Stamps written by earlier passes are quoted
as their authors wrote them (some say UTC where the host clock is +04); stamps added on
2026-09-08 at 12:50 are local +04 and say so.

| Task | State | **Measured at** | Evidence — and the report that holds the measurement |
|---|---|---|---|
| 1 — validated inputs, `SecretString`, paths | **Landed** | 2026-09-07 (task report) | `agent-core/src/validation/system/{backup_id,local_backup_root}.rs`; report `task-1-report.md` |
| 2 — distro facts | **Landed** | 2026-09-07 (task report) | `tar`/`gzip`/`database_dump` in `EXPECTED_BINARIES` (`distro/src/tests/debian/debian_adapter_tests.rs:79-94`); **deviation:** the methods live in `*_services.rs`, not the `*_paths.rs` the plan named — `*_paths.rs` holds locations, not spawnables (`task-2-report.md`) |
| 3 — `ops::backup` create | **Landed** | 2026-09-07 (task report) | `ops/src/backup/create_backup.rs` + `archive/`; `task-3-report.md` |
| 4 — restore | **Landed** | 2026-09-07 (task report) | `ops/src/backup/restore_backup.rs`; `task-4-report.md` |
| 5 — list, delete | **Landed** | 2026-09-07 (task report) | `ops/src/backup/{list_backups,delete_backup}.rs`; `task-5-report.md` |
| 6 — object store + bucket probe | **Landed as code, WIRED TO NOTHING** | 2026-09-08 04:05 | `s3_object_store_host.rs`, `local_object_store_host.rs`, `object_store_host.rs` exist and are tested; `grep -rn ObjectStoreHost --include=*.rs agent/crates/` finds only the trait, its impls, three `mod.rs` re-exports and the tests. See `docs/superpowers/notes/2026-09-07-backup-object-store-seam.md` — an OWNER DECISION, not a task |
| 7 — proto delta + `BackupService` | **Landed** | 2026-09-07 (task report) | `proto/agent/v1/backup.proto`; `agent/src/services/backup/` (11 files, incl. `remote_destination_refused.rs`, which is where the S3 refusal ended up) |
| 8 — the C# agent client | **Landed** | 2026-09-08 03:28–04:10 | `backend/src/Maran.Agent.Client/Services/BackupService/` (16 files); `task-8-report.md`. **CORRECTED:** the AlmaLinux polygon run HAS happened — `task-15-report.md:100-104` records eleven suites per family, **71 passed / 0 failed on Ubuntu 24.04 AND on AlmaLinux 9**, per-suite `test result:` lines summed. The Task 8 "Ubuntu only" caveat is retired |
| 9 — Backups module: create, list, delete | **Landed** | 2026-09-08 03:28–04:10 | 40 files under `backend/src/Maran.Modules/Backups/`, migration `20260907105730_InitialBackupsSchema`, 43 module tests green. **Deviations recorded below.** |
| 10 — restore, panel half | **LANDED** (measured 2026-09-08 03:50 UTC; the 21:50 row above was taken mid-implementation and is retracted) | 2026-09-08 03:50 | Route `Controllers/BackupsController.cs:127` `[HttpPost("{id:guid}/restore")]`, rate-limited at :128 with `RateLimitPolicies.BackupRestore`; `Commands/RestoreBackup/{RestoreBackupCommand,…Handler,…Validator}.cs`; `Services/RestoreRunner.cs`; `Mappers/RestoreAgentErrorTranslator.cs`; `Models/RestoreRunOutcome.cs`; `Common/RestoreOutcomeDto.cs`; panel task kind written at `RestoreBackupCommandHandler.cs:175` (`TaskKinds.BackupRestore`); audit `AuditActions.BackupRestored`, 3 writers. IDOR: the controller's route-coverage test picks the new route up by construction. **`RestoreBackupCommand` is NOT dead — that finding described the pre-Task-10 tree and has now been chased three times.** |
| 11 — destinations, credentials, probe | **LANDED — the "Not started" reading below was overturned by measurement; Task 11 landed after that reading was taken and this row was hours behind the tree** | 2026-09-08 12:50 +04 | `find backend/src/Maran.Modules/Backups -iname "*estination*"` returns the entity and its whole slice: `Domain/Entities/BackupDestination.cs`, `Domain/Enums/BackupDestinationKind.cs`, `Domain/Policies/RemoteDestinationPolicy.cs` (the ONE refusal, an allow-list), `Commands/SaveBackupDestination/` (3 files), `Queries/ListBackupDestinations/` (2), `Controllers/BackupDestinationsController.cs`, `Services/BackupDestinationResolver.cs`, `Mappers/{BackupDestinationMapper,BackupDestinationViewMapper}.cs`, `Persistence/Configurations/BackupDestinationConfiguration.cs`, `Seeders/DefaultBackupDestinationSeeder.cs`, `Models/ResolvedBackupDestination.cs`, `Common/BackupDestinationDto.cs`, and migration `Persistence/Migrations/20260908043519_BackupDestinations.cs` (+ its Designer). **The task also found and fixed a live defect no panel-side test could see**: the mapper was putting the configured root into `AgentBackupDestination.Path`, a field the agent refuses for a local destination ("a local destination carries no path"), so **every create, restore, delete and retention call this panel made was refusable by a real agent** — `task-11-report.md:105-120`. Mutations: **five run, five KILLED**, whole-solution scored (`task-11-report.md:293-299`); M5 first printed SURVIVED and that was a harness defect, written up as such. The public-bucket probe is still NOT built, deliberately: the agent refuses every probe by design, so a probe today would be a check that cannot observe what it reports on. Report: `task-11-report.md` |
| 12 — schedules, cadence, retention | **LANDED, and its mutation ledger is CLOSED — the "the one piece with NO verdict" reading is retracted** | 2026-09-08 12:50 +04 | Code as the previous reading listed it (`Domain/Entities/BackupSchedule.cs`, `Commands/SaveBackupSchedule/`, `Queries/GetBackupSchedule/`, `Jobs/{BackupRunRequested,BackupRunHandler,RetentionRequested,RetentionHandler}.cs`, `Controllers/BackupSchedulesController.cs`, the 5-minute cadence in `Maran.Host/BackgroundServices/BackupScheduleScheduler.cs`, migration `20260908010810_BackupSchedulesAndOrphanedAccounts`), **plus the four owed mutations, run on the whole 18-project solution and ALL FOUR KILLED** — `task-12-report.md:457,469,477,486` with the table at `:496-501`, each naming the test that died and the collected total for that run (2550/2/0, 2550/2/0, 2551/1/0, 2550/2/0). No survivor, so no witness is owed. Report: `task-12-report.md`, the section appended 2026-09-08 |
| 13 — final backup before account deletion | **LANDED, and §12 is kept** (measured 2026-09-08 03:55 UTC; the 21:50 "half-built" row is retracted) | 2026-09-08 03:55 | Producer `Services/AccountBackupService.cs:85`; **registered** `BackupsModule.cs:74` (`AddScoped<IAccountBackupService, AccountBackupService>`); **called** from `Accounts/Commands/DeleteAccount/DeleteAccountCommandHandler.cs:109` as `IEnumerable<IAccountBackupService>`, reduced to a nullable field at :122 with `SingleOrDefault` (a collection because Wolverine's generated code emits `GetRequiredService<T>` per constructor parameter, so a nullable parameter would make "no Backups module" mean "accounts cannot be deleted"). **The backup is step 0, before the `AccountDeleting` cascade** (`:129-137`), and **a failed final backup REFUSES the deletion** — nothing is destroyed at the point of refusal (`:363-366`). Three named exits, each of them code and each audited under its own action: no Backups module composed (`:338`, `FinalBackupSkippedNoModule`), an administrator passing `SkipFinalBackup` (`:348`, `FinalBackupSkipped`), and a typed machine-readable failure on the response and the task so the operator can fix and retry (`:367`, failure recorded against `FinalBackupTaken`). Proved by `Maran.Modules.Accounts.Tests/Commands/DeleteAccount/DeleteAccountFinalBackupTests.cs` and `Maran.Host.IntegrationTests/AccountDeletionCascadeTests.cs:155` |
| 14 — the screens | **LANDED, and the restore dialog with it** (re-measured 2026-09-08 03:57 UTC) | 2026-09-08 03:57 | `pages/backups/BackupsPage.vue`, `components/backups/{BackupCreateForm,BackupStatusBadge}.vue`, `stores/backups.ts`, `composables/apis/useBackupsApi.ts`, `types/backup.ts`, `locales/{en,ru,hy}/backups.json` (36 keys, parity exact), route at `router/index.ts:102`, e2e `frontend/e2e/backups/list.spec.ts`. Playwright **243 passed / 0 failed / 1 skipped** (`task-14-report.md:117`). **`BackupRestoreDialog.vue` NOW EXISTS** (`frontend/src/components/backups/`), and `backups.json` has grown from 36 keys to **70 in each of en/ru/hy**, parity exact (measured by parsing all three). Five stale comments were corrected in that pass; one of them was a **user-visible string in three locales** that told the customer restore was unavailable. The string that replaced it states the real gap instead — `en/backups.json:68` `"noSafetyCopy": "No copy is taken of the account as it stands now. The panel does not back up before it restores, so today's state is not recoverable once this starts."` — which is `BackupKind.PreRestore`'s absence said out loud to the customer. **Still NOT built, deliberately:** `BackupDestinationsPage.vue` and `BackupSchedulePage.vue` (Task 11 has no endpoints; Task 12's schedule surface is admin-only and unscreened) |
| 15 — installer, polygon, free space | **CORRECTED: landed, with two argued deviations** | 2026-09-08 03:28–04:10 | `task-15-report.md`. Polygon **71/0 per family**, `STRUCTURE-OK`. Deviations: no `installer/lib/89-backups.sh` (step 40 already creates and asserts `/var/backups/maran`; a second authority for one directory's mode was refused), and `Backups__MaxArchiveBytes`/`Backups__MaxDumpBytes` were NOT written to `panel.env.example` because nothing reads them — the ceilings are `MAXIMUM_ARCHIVE_BYTES`/`MAXIMUM_DUMP_BYTES` in `ops::backup`. Only `Backups__LocalRoot=/var/backups/maran` is written (`installer/panel.env.example:174`) |
| 16 — Definition of Done | **Seven of eight met; ONE remains open — the backend half of the quiet-tree count, and nothing else** | 2026-09-08 13:40 +04 | Re-walked line by line below the table. **Met since the previous reading:** the consolidated mutation ledger (`progress.md`, 167 measurements) and **the live browser run** (`live-run-report.md`, 318 lines, four flows including the refusal against a real agent, five defects raised as prose diffs). **Open:** the backend suite has still never been counted on an idle tree. Fifth attempt this session: **49 minutes waited, 147 samples, 144 BUSY**, three distinct peer sessions running `dotnet test Maran.sln` / `dotnet build` / `cargo test --workspace` back to back; the one gap was ~40 s. Log `quiet-tree-wait.log`, re-runnable script `quiet-tree-measure.sh`, both beside this plan's reports. The other three lanes are closed: rust **1477 / 0 / 79 across 25 targets**, polygon **78 per family**, SPA **270 / 0 / 1 across 54 spec files**. Report: `closing-report.md` |

### Promises whose only evidence is a type, a constant or an enum value (Task 16 sweep, 21:50)

Measured the way dead code is hunted: for each name, grep for a *consumer*, not a definition.

| Name | Where it is defined | Real consumer? |
|---|---|---|
| `ObjectStoreHost` / `S3ObjectStoreHost` / `LocalObjectStoreHost` | `ops/src/backup/` | **NONE.** `grep -rn ObjectStoreHost agent/crates --include=*.rs` finds the trait, its two impls, three `mod.rs` re-exports, one doc cross-reference in `agent/src/services/backup/validated_destination.rs:15`, and the tests. Owner decision, `docs/superpowers/notes/2026-09-07-backup-object-store-seam.md` |
| `BackupKind.Scheduled` | `Domain/Enums/BackupKind.cs:21` | **CLOSED 2026-09-08.** Exactly one writer, `Jobs/BackupRunHandler.cs:213`, and the file says so in its own doc at :19. Gained its first writer today |
| `BackupKind.PreRestore` | `BackupKind.cs:33` | **STILL NONE, and now the most valuable open item in this plan.** Task 10 landed WITHOUT it: a product-wide enum sweep (04:05 UTC — every enum member in `backend/src` grepped for a non-doc use outside its defining file) finds `BackupKind.PreRestore` used only in doc comments (`Backup.cs:265`, `:307`) and in tests that SEED it (`RetentionHandlerTests.cs:79`, `BackupTests.cs:130`, `AccountDeletingHandlerTests.cs:184`). Tests constructing a state no production code can produce is exactly the shape that reads as coverage. It is at least now DISCLOSED: `frontend/src/locales/{en,ru,hy}/backups.json` `restore.noSafetyCopy` tells the customer plainly that no copy is taken. Owner decision, not a defect to fix silently |
| `BackupKind.PreDeletion` | `BackupKind.cs:27` | **CLOSED 2026-09-08.** Writer `Services/AccountBackupService.cs:85`, registered `BackupsModule.cs:74`, reached from `DeleteAccountCommandHandler.cs:109`. Open owner question, not a code gap: the only escape from a refused deletion is `SkipFinalBackup`, which is **bounded by an administrator only** — no operator-facing override exists for a customer-initiated deletion |
| `IAgentBackupClient.RestoreAsync`, `AgentRestoreOutcome`, `BackupRestoreEvent` | `Maran.Agent.Client` | **CLOSED 2026-09-08.** Consumed by `Services/RestoreRunner.cs` under `Commands/RestoreBackup/RestoreBackupCommandHandler.cs`, published at `BackupsController.cs:127` |
| `IAgentBackupClient.ProbeAsync`, `AgentPublicReadVerdict` | `Maran.Agent.Client` | **No consumer above the client**, and the agent itself refuses every probe by design (`backup_service.rs:316-334`). Task 11 |
| `IAccountBackupService` | `Maran.Sdk/Interfaces/` | **CLOSED 2026-09-08.** `DeleteAccountCommandHandler.cs:109` resolves it as a collection; `BackupsModule.cs:74` supplies the one registration |
| `Backups__MaxArchiveBytes` / `Backups__MaxDumpBytes` | nowhere | Correctly ABSENT — `installer/panel.env.example:173` says so in words rather than shipping a key nothing reads |

Counter-examples, checked and found to be genuinely wired: `TaskKinds.BackupCreate`
(`CreateBackupCommandHandler.cs:129`), `AgentCapability.Backup` (`BackupsManifest.cs:31`),
`Backup.SurvivesAccountDeletion()` (`Maran.Host/Modules/ModuleAccountResidueAuditor.cs:236` **and**
`AccountDeletingHandler.cs:97` — two consumers, which is what the doc claims).

### The same sweep, run product-wide (2026-09-08 04:05 UTC)

The Task 16 sweep only looked at Backups. Run over **every enum member in `backend/src`** — 131
members, each grepped for a non-doc-comment use outside its own defining file — seven have no
producer anywhere in production code. Three are real gaps, one of them large:

| Value | Verdict |
|---|---|
| **`UserRole.Customer`** | **NO PRODUCER ANYWHERE.** The only `new User(` in `backend/src` is `Identity/Commands/CompleteSetup/CompleteSetupCommandHandler.cs:75`, and it passes `UserRole.Admin`. Creating a hosting account creates no panel login (`Accounts/Commands/CreateAccount/` never touches Identity), and `Identity/Controllers/AuthController.cs` publishes no registration route. So **no customer can sign in to this panel** — the entire tenant-scoped surface, and every IDOR test proving it, is reachable today by an administrator only. `UserRole.Customer` appears in production code exactly once, as a doc cross-reference at `Identity/Domain/Entities/User.cs:26`, and every customer that exists is constructed inside a test fixture. This is the largest promise-with-no-producer in the repository and it is not a Backups defect; it is filed here because this sweep is what found it |
| **`LicenceTier.AddOn`, `LicenceTier.PlanGated`** | **NO PRODUCER.** All ten module manifests declare `LicenceTier.Included` (`grep -rn "LicenceTier\." backend/src --include=*.cs`). The paid-tier half of the product's business model exists as two enum values and nothing else. Not a defect — but it is a promise the type makes and the code does not keep, so it belongs on the owner list rather than in a type |
| **`SessionRevocationReason.RevokedByAdmin`** | **NO WRITER.** Five siblings are written (`ReuseDetected`, `Rotated`, `PasswordChanged`, `Logout`, `LogoutAll`); this one is not, and there is no administrator session-revocation surface. The enum asserts a capability the panel does not have |
| `AgentCapability.System` | **Explained, not a gap.** Its consumer is `Maran.Host/HealthChecks/AgentHealthProbe.cs:22`, and the Host declares no manifest by design (`HandlerLocationTests`). No module reaches `IAgentSystemClient`, so nothing should declare it. Worth a sentence in `AgentCapability.cs` so the next sweep does not re-open it |
| `ChartRange.LastDay` | **Explained, not a gap.** It is model-bound from a customer's query string, so its producer is the request, not the code. Referenced in `Maran.Modules.Monitoring.Tests` |

### The `df` control moved, and its baseline is now stale

`monitor_on_a_real_host.rs:197`'s precedent has been followed: the `statvfs`-vs-`df` control is now
`agent/crates/agent/tests/backup_on_a_real_host.rs:1048`, a crate-level suite the polygon discovers
(`scripts/lib/polygon.sh:56`), and `available_bytes_tests.rs:14` records that it deliberately does
not live in the unit mirror. **But both files were written at 21:42-21:43 on 2026-09-07, after Task
15's polygon run (report mtime 19:33), so the control has never executed inside a polygon image.**
The 71/0 per-family baseline predates it; the next run will report more and must be re-baselined,
not treated as a discrepancy.

### The Task 9 / Task 11 boundary moved, deliberately

Two pieces the plan assigned to Task 11 landed in Task 9, with argument. **Task 11's implementer
should expect the tree to differ from Task 11's text as written:**

1. **`Options/BackupOptions.cs` exists already, with `LocalRoot` ONLY.** Create cannot name a
   destination path without it, so the setting could not wait: `BackupDestinationMapper.Local`
   reads `options.LocalRoot` and can produce no other kind of destination. `MaxArchiveBytes`,
   `MaxDumpBytes` and the **startup refusal of a `LocalRoot` that fails Task 1's rules** are
   still Task 11's, and the type was deliberately NOT given empty properties to grow into. The
   default is the installer's own literal `/var/backups/maran`; the agent re-validates it at its
   own boundary regardless, so this value is a request and not a trust.
2. **`Backup.DestinationId` is `Guid?`, not `Guid`.** The destination table arrives in Task 11; a
   non-null foreign key to a table that does not exist would be a lie in the schema. Task 11 owns
   the narrowing — and note that narrowing a column is exactly what `maran migrate guard` refuses
   without a `// contract-phase:` line (rules/architecture.md, expand-then-contract), so Task 11's
   migration must be written for that, not against it.

### Other deviations already taken, recorded so they are not re-litigated

- **Two audit actions, not four.** The plan named `BackupCreated`/`BackupCreationFailed`/
  `BackupDeleted`/`BackupDeletionFailed`. The tree's idiom is one action name plus `AuditEntry`'s
  `succeeded` flag (Ssl records a refused issuance as `CertificateIssued`, succeeded=false), and
  rules/csharp.md says match the existing module. Two shipped.
- **`TaskKinds.BackupRestore` was NOT added.** Nothing names it until Task 10 lands; a constant no
  code reads is documentation of code that does not exist. Task 10 adds it.

---

## Global Constraints

- NEVER `git commit`, `git add` or push. The owner commits. No AI attribution anywhere. Never `git checkout --` in this tree (progress.md Ruling 47).
- No shell-string execution by the agent. Processes are argv arrays against absolute paths from the distro adapter; `maran structure` rule 17b refuses bare-name spawns. `tar`, `gzip` and the dump client are spawned, never piped through `sh`.
- `ops` never names a platform literal (rule 17); facts come from `maran_distro` or `AgentPaths`.
- Doc comments on every item, private included. One file = one public unit, named after it. Errors in `*_error.rs`.
- Every agent operation is idempotent; `AlreadyExists`/`NotFound` are outcomes, not failures.
- IDOR answers 404, never 403. Admin-only surfaces answer 404 to customers.
- DoD per feature: unit + integration + IDOR test + audit success AND failure events + i18n en/ru/hy.
- Frontend laws: const arrows, UI kit only, `use<Feature>Api.ts` composables called from stores only.
- Verification: `source scripts/dev` FIRST, always. Then `maran agent check`, `maran structure`, `maran proto`, `maran handshake`, backend `dotnet test`, frontend lint+typecheck+build+`npx playwright test`.
- **Rules changes need the owner.** This plan proposes four, listed below; approving the plan approves those diffs verbatim and no others.

### Exact values (no task may invent a different one)

| Thing | Value | Why this value |
|---|---|---|
| Local backup root | `/var/backups/maran`, `root:root 0700` | Outside every home and every document root (Decision 3). `/var/backups` is the FHS location for exactly this and exists on both families. |
| Per-account directory | `<root>/<account>/`, `root:root 0700` | Enumeration of one account's artifacts is a directory read, not a scan. |
| Artifact | `<root>/<account>/<backup-id>.tar.gz`, `0600` | One file per backup; gzip because both families ship it and an operator can open it with `tar` (Decision 1). |
| Sidecar | `<root>/<account>/<backup-id>.meta.json`, `0600` | Listing reads sidecars; RESTORE reads the copy inside the archive and refuses if the two disagree. |
| Manifest version | `1` | An unknown version is refused, never guessed. |
| Checksum | SHA-256, hex lowercase, over the finished `.tar.gz`, and per-dump over each `.sql` | rules/security.md item 9 forbids MD5/SHA1 for anything security-relevant, and a restore's integrity check IS security-relevant. |
| Agent scratch | `AgentPaths::backup_scratch_dir(<backup-id>)` = `/var/lib/maran/scratch/backup/<backup-id>/`, `root:root 0700` | Database dumps never touch account-writable space (Task 3's TOCTOU argument). **CORRECTED after the plan was written:** this line said `agent_scratch_dir()`, which is `/run/maran/scratch` — a tmpfs sized at 10% of RAM (measured: 1.5 GiB on a 16 GiB host), while `MAXIMUM_DUMP_BYTES` is 8 GiB per database and its doc comment argued about filling a *disk*. A dump there is resident kernel memory, and a restore holds the dump and its rollback dump at once. The scratch moved to the new `BULK_SCRATCH_ROOT = /var/lib/maran/scratch`; `agent_scratch_dir()` remains for small, reboot-clean files. Measurement: `.superpowers/sdd/2026-09-05-maran-backups/scratch-ceiling-report.md`. |
| Restore staging root | `/home/.maran-restore`, `root:root 0700` | Same filesystem as the homes, so the swap is two `rename`s and not a copy. |
| Progress stages, create | `dumping_databases` 0→40, `archiving_files` 40→80, `uploading` 80→99, terminal 100 | Fixed spans so a stall is visible as a stalled span, not as a number that means nothing. Percentages inside a span are computed from bytes/rows done, never emitted as literals (the deletion cascade's 10/50/90 were constants and the lie was invisible). |
| Progress stages, restore | `verifying` 0→10, `downloading` 10→30, `restoring_databases` 30→70, `restoring_files` 70→95, `finalising` 95→99 | Order argued in Decision 2. |
| Stream channel capacity | 16 | Matches `INSTALL_CHANNEL_CAPACITY` in `php_service.rs`; bounded per rules/rust.md "Streams stay bounded". |
| Default retention | keep the **7** most recent successful backups per account per destination | A week of dailies. Configurable 1..=365. Retention prunes only AFTER a new backup has completed successfully. |
| Default schedule | daily at **03:00 UTC**, disabled until an operator enables it | A schedule the operator did not ask for that starts writing gigabytes is a surprise; off-by-default is the honest ship state. |
| Schedule evaluation cadence | every **5 minutes** (`BackupScheduleScheduler` hosted service) | Fine enough that an hour-of-day schedule fires within its hour; coarse enough to cost nothing. |
| Dump ceiling | 8 GiB per database, 64 GiB per archive | A ceiling that exists is a refusal an operator can read; no ceiling is a full disk at 03:00. Both configurable. |
| Concurrency | at most **one** backup or restore operation per account, agent-side, enforced by a keyed async mutex; a second returns `AlreadyExists` | Two restores into one home interleave two archives; a mutex is the honest fix (the `ops::firewall` precedent, Task 5 of plan 5). |
| S3 object key | `<prefix>/<account>/<backup-id>.tar.gz`, private ACL | Prefix is operator-chosen and validated; the rest is agent-derived. |
| Restore confirmation | the request body must carry the account's exact system username | A destructive operation whose confirmation is a boolean is a checkbox nobody reads. |

### Rules changes this plan carries (owner approval = plan approval)

1. `rules/rust.md` + `agent/CLAUDE.md` validation map: the `system/` row gains `backup_id · local_backup_root`; the `web/` row gains `s3_bucket · s3_region · s3_object_prefix`. No new folder — S3 endpoint values are remote-web values and `web/` already holds `domain · upstream · port · source_cidr`. (Plan 5 invented a `net/` folder and was overruled; this does not repeat it.)
2. `rules/rust.md` + `agent/CLAUDE.md` agent-core map: a new top-level unit `secret_string.rs` beside `command_outcome.rs` — a string whose `Debug` and `Display` print `«redacted»` (Task 1). It is not validation, so it does not go under `validation/`.
3. `agent/CLAUDE.md` crate map: `ops` gains its `backup/` row as shipped (it is `(planned)` today), including the `backup/archive/` subfolder.
4. `rules/csharp.md` module map: `Maran.Modules/<Name>/Options/` is already used by Firewall and Tasks but is not in the map; this plan adds the row, because Backups adds `BackupOptions`.

### Rulings (R1–R12). Each exists so executors do not re-litigate.

- **R1 — a backup is a gzip-compressed tar of the account's home plus one SQL dump per database, with a manifest.** Not a content-addressed repository. Decision 1 argues it and states what it costs.
- **R2 — the archive's internal layout is fixed and is part of the contract.** `manifest.json` at the root; `home/…` holding the account's home directory contents; `databases/<database>.sql` holding one dump each. Nothing else. The restore refuses a member outside those three prefixes rather than skipping it — a member it cannot place is an archive it does not understand.
- **R3 — creation reads the home as root; extraction writes it as the account.** The asymmetry is deliberate and both halves are argued in Task 3 and Task 4. Reading root-side is forced by `fork_as_account` returning `Result<(), PrivError>` with `close_inherited_descriptors()` shutting every descriptor ≥3, so a child cannot hand an archive back (plan 5's Ruling 15, corrected). Writing account-side is possible because extraction returns only success or failure, and it converts "root writes wherever the archive says" into "the account writes where the account already could".
- **R4 — the `databases/` members are extracted ROOT-side into the root-only scratch; only `home/` is extracted as the account.** Two extractions of one archive, for one reason: a dump file sitting in account-writable staging can be replaced between extraction and load, and the loader connects as `root@localhost`. That is arbitrary SQL as the database superuser — `FILE` privilege reads `/etc/shadow` into a table. Each dump's SHA-256 is checked against the manifest after extraction and before loading.
- **R5 — restore is REPLACE within a defined scope, never merge.** Decision 2.
- **R6 — the point of no return is the first `DROP DATABASE`.** Everything before it is undone by two renames; everything after it is undone by the pre-restore rollback dumps, which are taken per database immediately before that database is dropped. A restore that fails after the point of no return reports FAILED and names which databases were rolled back and which were not.
- **R7 — a restore reports ok only when every stage's terminal step succeeded, and the terminal message carries the counts.** Files swapped: yes/no. Databases restored: N of M. "Nothing threw" is not a completion (the account-deletion cascade defect, progress.md line 4850).
- **R8 — credentials never rest on the agent's disk and never appear in a log, an error message or `tool_output`.** They travel per-call on the root-only unix socket in fields typed `SecretString`. The stub's comment saying credentials are "provisioned to the agent out of band" is superseded by Task 7 Step 0, with the reason written into the proto comment.
- **R9 — the panel refuses to save an S3 destination whose bucket serves an unauthenticated GET.** A probe that writes a canary object and then fetches it with no credentials, and refuses on 200. Not a bucket-policy read: a policy read reports on a document, and what matters is what the internet gets. (rules/testing.md "a check must be able to observe what it reports on".)
- **R10 — destinations, schedules and retention are ADMIN-only; create, list, restore and delete of one's own account are customer-capable.** Decision 4.
- **R11 — the final backup before account deletion is fail-closed with an audited override.** If the Backups module is loaded and the final backup fails, the deletion refuses and the account is left exactly as it was. An administrator (and the Provisioning API key, which billing uses) may pass `skipFinalBackup=true`, which is audited as `FinalBackupSkipped` with the caller on it. If the Backups module is NOT loaded, deletion proceeds and audits `FinalBackupSkippedNoModule` — a panel with the module removed must still be able to delete an account.
- **R12 — retention deletes only after a success, oldest first, and never the backup a restore is reading.** Deleting before means one failed run costs the last good copy.

---

## The five decisions, argued

### Decision 1 — what a backup IS: `tar` + `gzip` + a dump per database

**Chosen.** One artifact per backup: `tar` of `home/` plus `databases/*.sql` plus `manifest.json`, gzip-compressed, SHA-256 recorded. Dumps come from the family's dump client (`mysqldump`/`mariadb-dump`) with `--single-transaction --quick --routines --triggers --events --hex-blob`.

**The alternative, taken seriously.** A content-addressed store (restic, borg) gives deduplication, incremental cost proportional to change, and encryption at rest for free. For a hosting panel keeping seven dailies of a 20 GiB account, that is the difference between 140 GiB and roughly 25 GiB, and it is a real difference, not a rounding one.

**Why it loses here, in order of weight:**

1. **The installer must ship it on both families and four+ distributions.** `restic` is in Debian's main archive but reaches AlmaLinux/Rocky only through EPEL, which the installer does not enable today; borg's situation is worse. Adding a third-party repository to a customer's server to make backups work is a change to the installer's threat surface, and §14's preflight promise ("honest refusal") turns into "honest refusal on half the supported matrix".
2. **The repository is stateful, and the agent is not.** rules/architecture.md: "The agent MUST stay stateless: no DB, no config files of its own beyond what the installer writes." A restic repository is a mutable index with locks; a lock left by a killed process makes the next backup fail in a way an operator cannot diagnose without learning restic. Our chosen format has no state at all — a `.tar.gz` and a sidecar.
3. **The repository password is a secret whose loss destroys every backup.** Encryption at rest is a genuine gain (Decision 3 has no equivalent), but the key becomes a thing the panel must store, rotate, and survive a rebuild of. A panel that loses the key has backups that are indistinguishable from noise. That is a worse failure than the one it prevents.
4. **The operator cannot open it with ordinary tools.** The most valuable property of a backup is that it works when the panel does not. `tar xzf` works from a rescue shell on a machine with no panel and no network; `restic restore` needs the binary, the repo, and the password.

**What losing it costs, stated plainly so nobody thinks it was free:** every backup is a full backup. Seven dailies of a 20 GiB account cost ~140 GiB before compression. Retention (7 by default), the per-archive ceiling (64 GiB), and the preflight free-space check in Task 15 are the mitigations, and they are mitigations, not a fix. Incremental backup is a v2 candidate and is named as such in the threat note. It is also why the local root is a documented, operator-changeable path: putting it on a second volume is the operator's lever.

**Two details that are decisions, not defaults:**
- `--one-file-system` on create. Without it, `tar` descends into the SFTP jail's bind mount (`/var/lib/maran/sftp/<acc>` is bind-mounted under the home in this product) and archives every file twice, one of the copies through a mount the account does not own.
- No `-h`/`--dereference`, ever. A symlink is stored as a symlink. With `-h`, an account replacing a home subdirectory with a symlink to `/etc` gets `/etc/shadow` into an archive an administrator can download. Task 3's fixture contains exactly that symlink and a named test asserts the archive stores the LINK.

### Decision 2 — restore: what it does to what is already there

This is the operation that can destroy a working account while claiming to help it, so this section says exactly what happens, in what order, and what is recoverable at each moment.

**Scope, stated before behaviour.** A restore replaces (a) the contents of `/home/<account>/` and (b) the contents of each database named in the manifest that the panel still knows about. It does NOT re-create nginx vhosts, certificates, SFTP logins, cron entries' panel rows, or the account itself — those live in the panel's database and on other parts of the disk, and a files+database backup does not carry them. This is a genuine gap between the spec's promise and what the spec's own definition of a backup contains; it is recorded in the report as a spec ambiguity, and the UI says it in words on the confirmation dialog rather than letting a customer infer that "restore" means "undo".

**Replace, not merge, and not refuse.**
- *Merge* is refused because the result matches no point in time. If the reason for the restore is a compromise, a merge keeps the attacker's files. If the reason is a bad deploy, a merge keeps the bad deploy's new files. A customer who wanted merge wanted a file manager, and they have one.
- *Refuse-if-not-empty* is refused because a restore into an empty home is the case that never happens.
- So: replace. The old home is not deleted at the moment of replacement — it is renamed aside and kept until the whole restore has succeeded (see below), which is what makes the destructive step reversible.

**The sequence, and the recoverability at each step.**

| # | Step | If it fails here |
|---|---|---|
| 1 | `verifying` — fetch the artifact's bytes (S3) or open them (local); verify SHA-256 against the panel's recorded checksum; read `manifest.json` from inside the archive and compare against the sidecar; refuse an unknown manifest version, an account name that is not this account, or a member outside `manifest`/`home/`/`databases/` (R2) | Nothing has been touched. Typed error, account untouched. |
| 2 | Take a **pre-restore backup** of the account — the same `create` operation, kind `PreRestore`, to the same destination, retained outside the retention count | Restore refuses. The customer's current state is still the only state. |
| 3 | Extract `databases/*` root-side into the root-only scratch; verify each dump's SHA-256 against the manifest (R4) | Nothing has been touched. |
| 4 | Extract `home/*` as the account into `/home/.maran-restore/<acc>.<id>/` (R3) | Remove the staging tree. Account untouched. |
| 5 | **`restoring_databases`.** For each database, in manifest order: dump it to `<scratch>/rollback/<db>.sql`; then `DROP DATABASE`; `CREATE DATABASE`; load the archive's dump. **The first `DROP DATABASE` is the point of no return** (R6) | Reload the rollback dump for every database already replaced, in reverse order. Then rename nothing (files are untouched at this point). Report FAILED naming which databases were rolled back and which could not be. The account's files are exactly as they were. |
| 6 | **`restoring_files`.** `rename` `/home/<acc>` → `/home/.maran-restore/<acc>.previous.<id>`; `rename` staging → `/home/<acc>`. Two renames on one filesystem | Between the two renames the account has no home for microseconds; if the second rename fails, the first is reversed immediately and the account is back. If reversing also fails, the operation reports FAILED with the exact path the home is parked at, because a message an operator can act on beats a rollback that lies. |
| 7 | `finalising` — re-apply ownership and mode on the home ROOT and remove the `previous` tree and the scratch. **Measured, not assumed:** `AccountOperations` creates a home as `0750` owned `<acc>` but group-owned by the **web server's group** (`chgrp` then `chmod 0750`, `account_operations.rs:495` and the doc paragraph above it — the web server must traverse the home to serve the site). A restore that puts the account's own group back therefore breaks every site the account owns, silently, with a 403 from nginx. So finalising re-applies exactly that arrangement, reading the group from the distro adapter's `web_server_group()`, and a named test pins it | The restore has succeeded; a failure here is logged and reported as a warning on the terminal message, not as a failure of the restore. The `previous` tree remaining is a disk-space problem, not a correctness one, and the next restore's staging cleanup removes it. |

**Why databases before files.** The reverse order was considered and rejected. The file swap is atomic and reversible by one rename; the database load is neither. Doing the irreversible half first means the reversible half never has to be reversed after the irreversible half has committed — the only genuinely bad interleaving is "files swapped, then databases fail", which leaves the customer's application looking at new code and old data. With this order the bad interleaving is "databases restored, files fail", which leaves old code and new data — also wrong, but the files half is one rename away from being correct, and the operation says so.

**Half-success is impossible to report as success.** R7: the terminal `RestoreBackupOk` carries `files_restored: bool` and `databases_restored`/`databases_total`, and the panel refuses to mark the restore Completed unless files are restored and the two counts are equal. The named test `a_restore_that_loses_a_database_reports_failed_not_completed` exists precisely because plan 5's deletion cascade reported COMPLETED/100 over work it had not done, and its mutation (write `Completed` unconditionally) must go red.

### Decision 3 — where the bytes live and who can read them

A backup contains the customer's files and their database, and the database contains whatever their application put in it: API keys, password hashes, other people's personal data. It is the single most sensitive object this product produces.

**Local.** `/var/backups/maran`, `root:root 0700`, per-account subdirectories `0700`, artifacts `0600`. What the operator's backup directory MUST NOT be, enforced by validation in `LocalBackupRoot::parse` at BOTH boundaries (API validator and agent revalidation) and by a startup check:

- **Not under `/home`.** Under a home it counts against the customer's quota, is included in the next backup of that account (recursively), and — the real defect — is *writable by the customer*, so the artifact a restore reads is an artifact the customer chose. That is a root-side extraction of attacker-supplied content, and the two-extraction split (R4) exists partly because of it.
- **Not under any site document root**, for the obvious reason: `curl https://example.com/backups/<uuid>.tar.gz`.
- **Not a symlink**, at any component. Resolved with the canonicalisation the repository already uses; a path that resolves elsewhere is refused, not followed.
- **Not `/tmp`, `/var/tmp`, `/dev/shm`** — world-writable directories where another local user pre-plants the name root is about to write.
- **Must exist, be a directory, be owned by root, and be mode 0700 or stricter.** Checked at startup and again before every write; a directory that has become group-readable is refused with a message naming the mode. This check can observe what it reports on: it `stat`s the path the write goes to, not a configured string.

**S3.** Two questions: where the credentials live, and what the bucket does.

*Credentials.* The panel's database is the one secret store (`EncryptedStringConverter`, the SMTP password's precedent). The agent is stateless by rule, so credentials travel **per call**, on the unix socket that is `root:panel 0660` with `SO_PEERCRED` — a channel already trusted with every root operation the panel drives. They are typed `SecretString` in the agent so `Debug`, `Display` and every `tracing` field print `«redacted»`, they are never written to disk, and `AgentError.tool_output` is scrubbed of them before it crosses back (the S3 client's error text can quote a signed URL). The alternative — the installer writing `/etc/maran/backup-s3.env` — was rejected because it makes the agent stateful in configuration, makes credential rotation an installer action instead of a UI action, and puts the secret in a second place without removing it from the first. This SUPERSEDES the stub comment in `backup.proto` that says credentials are provisioned out of band; Task 7 Step 0 rewrites that comment with the reason, which is a legal additive change (comments and new fields both are).

*The bucket.* The panel does not control the bucket's policy, and a bucket whose policy is wrong is a public archive of a customer's database. So R9: saving an S3 destination runs a probe — PUT a small canary object under the configured prefix, then GET it over plain HTTPS with **no credentials at all**, then DELETE it. A 200 on the unauthenticated GET refuses the destination with a distinct error the operator can act on. A 403/404 accepts it. A network failure on the anonymous GET is reported as *unproven*, not as safe, and the destination is refused — the honest answer when a check could not observe. Objects are written with a private ACL and TLS is required (an `http://` endpoint is refused; `https://` custom endpoints for S3-compatible providers are allowed).

*Wrong credentials.* A typed `S3AccessDenied` on save (the probe fails at PUT) and on every later operation; the schedule marks the destination `Unhealthy` after a failed run and raises the existing alert path rather than retrying in a loop that could trip a provider's lockout. Never a retry storm, never a credential in the error.

**Not in v1, deliberately:** client-side encryption of the artifact, and a panel endpoint that downloads an artifact to a browser. The first is real key management and rules/security.md forbids improvising crypto; the second means streaming a root-owned archive containing a customer's database through the API process, which the spec does not ask for. Both are named in the threat note as accepted gaps rather than omitted silently.

### Decision 4 — who may do what

| Surface | Customer | Administrator |
|---|---|---|
| List / create / delete backups of **their own** account | yes | yes, for any account |
| **Restore** their own account from their own backup | yes, with typed confirmation, rate-limited to 3/hour, audited | yes, any account |
| Configure destinations (local path, S3 endpoint + credentials) | **no** — 404 | yes |
| Configure schedules and retention | **no** — 404 | yes |
| See another account's backups | 404 | yes |

**Why a customer may restore their own account.** §11 says "restoration from the UI" and §8 says customer cabinets are v1; a restore feature only an administrator can run is a support ticket, not a feature. The risk it carries — a customer destroying their own working site — is addressed by the typed confirmation, the automatic pre-restore backup (step 2 above), and the dialog naming what is replaced and what is not.

**Why a customer may NOT configure destinations.** Choosing where the bytes go is choosing to exfiltrate a copy of their own data to a bucket the operator cannot see — which is their right for their own data, but the same field is how a customer aims the panel's root process at a path Decision 3 spends a section constraining, and one form cannot be safe for the local half and unsafe for the S3 half. Admin-only, whole.

**Against the IDOR rule.** Tenant-scoped surfaces (`/api/backups/…` for one's own account) answer **404** for another account's row. Host-wide admin surfaces (`/api/admin/backup-destinations`, `/api/admin/backup-schedules`) answer **404** to a customer, matching the existing admin-gating idiom in Firewall and Monitoring — not 403, because a 403 confirms the surface exists. There is no 403 exception in this plan.

### Decision 5 — scheduling: the panel's cadence, not the Cron module

**Decided: backups do NOT use the Cron module.** They use the mechanism the panel already uses for its own recurring work — a hosted service registered in `Maran.Host/Extensions/BackgroundWorkExtensions.cs` (`BackupScheduleScheduler`, cadence 5 minutes) that publishes a module-owned message (`BackupRunRequested`) handled inside `Maran.Modules.Backups`. This is file-for-file the shape of `CertificateRenewalScheduler` → `CertificateRenewalRequested` → `CertificateRenewalHandler`, and of `MetricsRetentionScheduler`.

**Why not the Cron module, in the order the reasons bind:**

1. **The Cron module is the customer's crontab, and its truth is the crontab file.** It has no persistence at all (plan 5, Task 9: "no Persistence — crontab is truth"). A schedule stored there is a schedule the panel cannot query, cannot report on, and cannot reconcile after a crash.
2. **A cron entry is owned and editable by the account.** Its `.cmd` file lives under the customer's home and is writable by them (plan 5's threat note says so in as many words). A backup schedule the customer can delete is not a backup schedule, and a backup command line the customer can rewrite runs as… whatever they wrote.
3. **A cron entry runs as the account's UID through `/bin/sh`.** A backup runs as root, reads the whole home, dumps databases as the database superuser, and writes to a root-only directory. There is no version of this that belongs on a customer's crontab.
4. **The panel needs the run recorded.** A scheduled backup is a `PanelTask` with progress, an audit entry, a `Backup` row and a retention consequence. Cron gives an exit code in a file whose mtime the account controls.

**Is a second scheduler right?** There is no second scheduler. `AddPanelBackgroundWork` is *the* panel scheduler and already runs five cadences; this adds a sixth line to it. The Host owns the cadence and the module owns the job, which rules/architecture.md's own comment on that file calls the right split. The schedule shape is deliberately not a cron expression — `Frequency { Daily, Weekly }` + `HourUtc` + `DayOfWeek?` — because a cron expression is a parsing surface we would have to validate again for a UI whose actual requirement is "daily at 03:00".

---

## File Structure (decided before the tasks)

New files unless marked `(+)`. Prepared empty homes are filled, not created.

```
proto/agent/v1/backup.proto (+)   ADDITIVE ONLY (Task 7 Step 0 is the law):
                                  +S3 credential fields on BackupDestination, +endpoint,
                                  +expected_checksum/manifest fields on Restore, +allowed
                                  database names on Restore, +counts on RestoreBackupOk,
                                  +ProbeDestination rpc; comments rewritten where they
                                  describe behaviour this plan changes (R8)

agent-core:
  validation/system/backup_id.rs (+_error)          lowercase hyphenated uuid, mirrors CronEntryId
  validation/system/local_backup_root.rs (+_error)  absolute, canonical, not under /home, not a
                                                    site root, not /tmp|/var/tmp|/dev/shm, no
                                                    symlink component
  validation/web/s3_bucket.rs (+_error)             3..=63, DNS-style, no dots-with-uppercase
  validation/web/s3_region.rs (+_error)             [a-z0-9-]{1,32}
  validation/web/s3_object_prefix.rs (+_error)      0..=256, [A-Za-z0-9/_-], no "..", no leading /
  secret_string.rs                                  NEW top-level unit: redacting Debug/Display
  agent_paths.rs (+)                                BACKUP_ROOT, RESTORE_STAGING_ROOT,
                                                    account_backup_dir, backup_artifact_path,
                                                    backup_sidecar_path, backup_scratch_dir,
                                                    restore_staging_dir, restore_previous_dir

distro (+): tar_binary, gzip_binary, database_dump_binary  (VERIFIED on both polygons, Task 2)

ops/src/backup/
  mod.rs · backup_error.rs
  create_backup.rs · restore_backup.rs · list_backups.rs · delete_backup.rs
  backup_host.rs · process_backup_host.rs          (spawn seam; dump-to-file and load-from-file)
  database_catalog.rs                               (seam: which databases does this account own)
  object_store_host.rs · s3_object_store_host.rs · local_object_store_host.rs
                                                    (one seam, two implementations, and NO CALLER —
                                                     the four operations take a LocalBackupRoot and
                                                     touch the filesystem directly. CORRECTED: this
                                                     said "the local destination is not a branch
                                                     through the area"; there is no branch because
                                                     there is no remote arm. See the Task 6
                                                     correction and seam-wiring-report.md)
  backup_lock.rs                                    (per-account keyed async mutex)
  archive/{mod.rs, archive_home.rs, extract_home_as_account.rs, extract_databases_as_root.rs,
           dump_database.rs, load_dump.rs, checksum_file.rs}
  model/{backup_manifest.rs, manifest_database.rs, backup_summary.rs, restore_outcome.rs,
         backup_stage.rs, progress_sink.rs, archive_spec.rs, extract_spec.rs,
         object_summary.rs, public_read_verdict.rs}
  tests mirrored under ops/src/tests/backup/ ; archive fixtures under ops/tests/fixtures/backup/

agent: services/backup/{mod.rs, backup_service.rs, backup_status.rs, validated_destination.rs}
       server.rs (+) ; tests/backup_on_a_real_host.rs (+fixtures)
       docker/polygon (+): tar/gzip/dump client present; README run lists the new suite

backend Maran.Agent.Client/Services/BackupService/
  AgentBackupClient.cs · GrpcBackupServiceInvoker.cs · ResilientAgentBackupClient.cs
  BackupCreateEvent.cs · BackupCreateEventKind.cs · BackupRestoreEvent.cs
  BackupRestoreEventKind.cs · AgentBackupSummary.cs · AgentBackupDestination.cs
  AgentBackupDestinationKind.cs · AgentRestoreOutcome.cs
  Interfaces/IAgentBackupClient.cs · Interfaces/IBackupServiceInvoker.cs

backend Maran.Modules/Backups/
  BackupsModule.cs · BackupsManifest.cs · GlobalUsings.cs · Maran.Modules.Backups.csproj
  Domain/Entities/{Backup.cs, BackupDestination.cs, BackupSchedule.cs}
  Domain/Enums/{BackupStatus.cs, BackupKind.cs, BackupDestinationKind.cs, BackupFrequency.cs}
  Commands/{CreateBackup,DeleteBackup,RestoreBackup,SaveBackupDestination,
            DeleteBackupDestination,SaveBackupSchedule}/  (command+handler+validator each)
  Queries/{ListBackups,GetBackup,ListBackupDestinations,GetBackupSchedule}/
  Common/{BackupDto.cs, BackupDestinationDto.cs, BackupScheduleDto.cs, RestoreOutcomeDto.cs}
  Controllers/{BackupsController.cs, BackupDestinationsController.cs, BackupSchedulesController.cs}
  Controllers/Requests/{...}   SUPERSEDED — this folder was abolished product-wide after this
                        plan was written. An action binds its Command or Query directly; see
                        rules/csharp.md, "An endpoint binds its command directly". Do not create it.
  Services/{BackupAuditJournal.cs, BackupRunner.cs, RetentionPruner.cs,
            S3DestinationProbe.cs, AccountBackupService.cs}
  Jobs/{BackupRunRequested.cs, BackupRunHandler.cs, RetentionRequested.cs, RetentionHandler.cs}
  Options/BackupOptions.cs
  Persistence/{BackupsDbContext.cs, Configurations/×3, Migrations/, DesignTime*}
  Mappers/{BackupAgentErrorTranslator.cs, BackupMapper.cs}
  Validators/{LocalBackupRootRule.cs, S3DestinationValidator.cs}
  Resources/{DisplayNames,ErrorMessages}{,.ru,.hy}.resx
  Seeders/DefaultBackupDestinationSeeder.cs

backend Sdk (+): Contracts/AgentCapability.cs (+Backup), Contracts/TaskKinds.cs
  (+BackupCreate, +BackupRestore), Contracts/AuditActions.cs (+11 actions),
  Interfaces/IAccountBackupService.cs (NEW — the act-window Accounts uses for the final backup,
    Task 13), Interfaces/IAccountDatabaseDirectory.cs (NEW — the read-only window Backups uses to
    learn which databases an account still owns, implemented by Databases, Task 10)
backend Databases (+): Services/AccountDatabaseDirectory.cs (the implementation; Databases stays
  the data's owner) + the sibling test doubles that stub it
backend Accounts (+): DeleteAccountCommandHandler.cs, DeleteAccountCommand.cs (+SkipFinalBackup)
backend Host (+): BackgroundWorkExtensions.cs (+BackupScheduleScheduler),
  BackgroundServices/BackupScheduleScheduler.cs
backend tests: Maran.Modules.Backups.Tests (+ solution entry), Host.IntegrationTests (+)

frontend: pages/backups/{BackupsPage.vue, BackupRestoreDialog.vue, BackupCreateDialog.vue},
  pages/settings/{BackupDestinationsPage.vue, BackupSchedulePage.vue},
  components/backups/{BackupList.vue, BackupStatusBadge.vue, RestoreScopeNotice.vue},
  stores/backups.ts, composables/apis/useBackupsApi.ts, types/backups.ts,
  locales/{en,ru,hy}/backups.json, router (+), e2e/backups.spec.ts + stubs

installer: lib/89-backups.sh, install.sh (+), uninstall.sh (+, NEVER deletes artifacts),
  lib/10-preflight.sh (+free space), panel.env.example (+Backups__ keys),
  assert-installer-steps.sh (+)
```

---

## Phase A — the agent

### Task 1: The validated inputs, the redacting secret, and the paths

**Files:** the five type+error pairs above; `agent-core/src/secret_string.rs`; `validation/{system,web}/mod.rs` (+); `agent_paths.rs` (+); rules-map amendments §1 and §2 applied to `rules/rust.md` + `agent/CLAUDE.md`; tests mirrored under `src/tests/`.

**Interfaces:**
- `BackupId::parse(&str) -> Result<Self, BackupIdError>` — a plain lowercase hyphenated uuid and nothing else, mirroring `CronEntryId` (read it first and match its idiom). It is the only thing between a caller-supplied id and a filesystem path and an object key, and its doc says so in those words (plan 5's Ruling 6 is the precedent, and it exists because the first version guarded the string at each use instead).
- `LocalBackupRoot::parse(&str) -> Result<Self, LocalBackupRootError>`; error variants, each its own: `NotAbsolute`, `NotCanonical`, `UnderAccountHomes`, `UnderSiteRoot`, `WorldWritableAncestor`, `SymlinkComponent`, `TooLong`. The refusal list is Decision 3's, and the doc comment states the check is a *string* check and that ownership/mode are checked separately at write time against the real inode — the two are different questions and only one of them can be answered from a string.
- `S3Bucket::parse`, `S3Region::parse`, `S3ObjectPrefix::parse` per the File Structure grammars. `S3ObjectPrefix` refuses `..` as a path segment and any leading `/`.
- `SecretString::new(String) -> Self`, `expose(&self) -> &str`. `impl Debug` and `impl Display` both write `«redacted»`. `expose` is the only reader and is named to be greppable — a review can find every place a secret is unwrapped by searching one word.
- `AgentPaths` additions exactly as in File Structure. `backup_artifact_path(&AccountName, &BackupId)` = `/var/backups/maran/<account>/<id>.tar.gz`; `restore_staging_dir(&AccountName, &BackupId)` = `/home/.maran-restore/<account>.<id>`; `restore_previous_dir(&AccountName, &BackupId)` = `/home/.maran-restore/<account>.previous.<id>`. Both staging paths are under one root so the swap's two renames are same-filesystem, and the doc says that is the reason.

- [x] **Step 1 — failing tests** (line comments, not block comments):
```rust
// backup_id: a_plain_uuid_parses; an_uppercase_uuid_is_refused; a_path_separator_is_refused;
//   a_traversal_segment_is_refused; the_empty_string_is_refused
// local_backup_root: a_path_under_home_is_refused; a_path_under_a_site_root_is_refused;
//   tmp_and_var_tmp_and_dev_shm_are_refused; a_relative_path_is_refused;
//   a_component_that_is_a_symlink_is_refused; the_default_root_parses
// s3_bucket: the_dns_grammar_is_enforced; an_uppercase_bucket_is_refused; bounds_are_enforced
// s3_object_prefix: a_traversal_segment_is_refused; a_leading_slash_is_refused; the_empty_prefix_is_legal
// secret_string: debug_and_display_both_redact; expose_returns_the_value;
//   a_secret_inside_a_derived_struct_still_redacts   <- the one that matters: a field of a
//   #[derive(Debug)] struct is the way a secret actually reaches a log line
// agent_paths: the_two_restore_paths_share_one_parent_so_the_swap_is_a_rename
```
- [x] **Step 2:** red (types absent) → implement in the idiom of `validation/system/cron_entry_id.rs` and `validation/web/source_cidr.rs` (read both first; no regex crate).
- [x] **Step 3:** `maran agent check` + `maran structure` green; the two map amendments applied in this same change (rules/architecture.md requires the map to move with the file).
- [x] **Step 4:** mutation per type — admit an uppercase uuid; admit a path under `/home`; make `SecretString`'s `Debug` derive; admit `..` in a prefix. Each must kill its NAMED test; restore `cmp`-verified with a fresh mtime; score against the whole workspace, never `-p`-filtered (plan 5, lesson 11).

**Gate:** `source scripts/dev && maran agent check && maran structure` → exit 0, totals stated as `N passed / 0 failed` against the pre-task baseline.

### Task 2: Distro facts — verified on both images, not guessed

**Files:** `distro/src/adapter.rs` (+), both `*_paths.rs` (+), both family test tables (+), `EXPECTED_BINARIES` (+).

**Interfaces and the facts to VERIFY on both polygon images** (the transcript goes in the task report, the answers into the doc comments):
- `tar_binary()` — expected `/usr/bin/tar` on both; verify with `command -v tar`.
- `gzip_binary()` — expected `/usr/bin/gzip`; verify. (We spawn `tar --gzip`, which execs gzip from `PATH`; naming the binary lets the preflight assert it exists rather than discovering it missing at 03:00.)
- `database_dump_binary()` — **this is the one that differs and the one to actually measure.** Debian's mariadb-client and AlmaLinux 9's mariadb both historically ship `mysqldump`, but AlmaLinux 9's mariadb 10.5 ships `mariadb-dump` with `mysqldump` as a compatibility symlink, and a symlink that a future package drops is exactly the silent per-family failure rules/architecture.md warns about. Verify `command -v` for BOTH names on BOTH images, record which is real and which is a symlink, and return the **real** name per family.
- Also verify, and record in the doc comment, whether the dump client accepts `--no-tablespaces` on each family: MySQL 8 requires it without `PROCESS`, MariaDB's client rejects the flag outright. The answer decides whether the flag is in the argv at all; do not add it "just in case" — an unknown flag is a non-zero exit and a backup that never runs.
- [x] **Steps:** extend `EXPECTED_BINARIES` and the per-family literal tests (red) → implement → verify on both images with a pasted transcript → green → mutation: swap one family's dump binary for the other's; the family test must go red.

**Gate:** `maran agent check && maran structure`; both polygon images consulted (`docker/polygon`), transcript pasted.

### Task 3: `ops::backup` — create

**Files:** `ops/src/backup/{mod.rs, backup_error.rs, create_backup.rs, backup_host.rs, process_backup_host.rs, database_catalog.rs, backup_lock.rs}`, `archive/{mod.rs, archive_home.rs, dump_database.rs, checksum_file.rs}`, `model/{backup_manifest.rs, manifest_database.rs, backup_summary.rs, backup_stage.rs, progress_sink.rs}`; tests mirrored; fixtures; `ops/src/lib.rs` (+); rules amendment §3.

**Interfaces:**
- `BackupHost` — the spawn seam, one method per *shape* of spawn, because the shapes genuinely differ (the `DbHost` doc's argument for a single method does not hold here: dumping writes a file, archiving does not, loading reads stdin):
  - `dump_database(&self, database: &str, into: &Path) -> Result<u64, BackupError>` — spawns the dump binary with argv `[--single-transaction, --quick, --routines, --triggers, --events, --hex-blob, --result-file=<into>, --databases, <database>]`, stdout captured for diagnostics only. `--result-file` rather than a redirect, so there is no shell and no descriptor plumbing. Returns bytes written.
  - `create_archive(&self, spec: &ArchiveSpec) -> Result<u64, BackupError>` — argv `[--create, --gzip, --numeric-owner, --one-file-system, --sparse, --file, <tmp>, -C, /home, <account>, -C, <scratch>, databases, manifest.json]`. No `-h`. Decision 1 names both flags and why.
  - `load_dump(&self, database: &str, from: &Path) -> Result<(), BackupError>` — the client with stdin piped from the file (the `chpasswd` precedent in `process_sftp_host.rs` — read it first).
  - `extract(&self, spec: &ExtractSpec) -> Result<(), BackupError>` — used by Task 4.
- `DatabaseCatalog` — `databases_of(&self, account: &AccountName) -> Result<Vec<String>, BackupError>`. **`ops::backup` does NOT call `ops::db`**: a cross-area import inside `ops` is the violation plan 5's Task 6 caught (`QuotaBlocks`). The composition happens one layer up, in `agent/src/services/backup/`, exactly as `AccountsServiceImpl` composes four hosts for the deletion cascade — Task 7 wires `ops::db::list_databases` into this seam. The doc comment names that precedent.
- `create_backup(host, catalog, account, backup_id, root, sink) -> Result<BackupSummary, BackupError>`
  (**CORRECTED**: no `store` and no `destination` parameter — see the Task 6 correction below; the lock is taken internally by `backup_lock.rs`, not passed in):
  1. take the per-account lock (`backup_lock.rs`, a `Mutex<HashMap<AccountName, Arc<tokio::sync::Mutex<()>>>>`; a second operation on the same account is `BackupError::AlreadyRunning`);
  2. if the artifact already exists at the destination with this id → `BackupError::AlreadyExists` **before** any work (idempotency, as `backup.proto` already promises);
  3. `dumping_databases`: create the root-only scratch `0700`; for each database from the catalog, dump it and hash it; percent = 40 × (done / total);
  4. `archiving_files`: write `manifest.json` into the scratch; archive; percent interpolated from the artifact's growing size against the home's measured size;
  5. hash the finished artifact; `rename` it from `<id>.tar.gz.partial` onto `<id>.tar.gz` (atomic publish — a reader never sees a half-written artifact, and a crashed run leaves a `.partial` that the next run removes);
  6. write the sidecar; remove the scratch, always, including on every error path;
  7. `uploading` when the destination is S3 — **NOT BUILT**; the operation takes a `LocalBackupRoot` and has no remote arm, so the local path is the only path and it goes straight to 100 (see the Task 6 correction).
- `BackupManifest { version: u32, account: String, backup_id: String, created_at_unix: i64, home_bytes: u64, databases: Vec<ManifestDatabase>, agent_version: String }`; `ManifestDatabase { name: String, bytes: u64, sha256: String }`. Serialised with `serde_json` (already a dependency, added in plan 5).

- [x] **Step 1 — failing tests**, the load-bearing ones by name:
  `a_symlink_in_the_home_is_archived_as_a_link_and_not_followed` (fixture home contains `secrets -> /etc/shadow`; assert the argv carries no `-h` AND, on the polygon in Task 7, that the extracted member is a link),
  `the_archive_argv_contains_one_file_system_and_never_dereference`,
  `every_database_the_catalog_names_is_dumped_and_hashed`,
  `the_manifest_lists_every_dump_with_its_checksum`,
  `a_dump_failure_removes_the_scratch_and_publishes_no_artifact`,
  `the_artifact_is_published_by_rename_so_a_reader_never_sees_a_partial`,
  `a_second_backup_of_the_same_account_reports_already_running`,
  `an_existing_artifact_with_the_same_id_reports_already_exists_before_any_dump`,
  `no_caller_supplied_byte_reaches_an_argv_position_that_is_not_a_validated_type` (walks the built argv and asserts provenance — the `the_installed_line_contains_no_caller_supplied_byte` idea from plan 5's cron task, which was that plan's strongest single test),
  `progress_percent_is_computed_and_never_a_literal` (two runs with different database counts must emit different intermediate percents — the deletion cascade's 10/50/90 were constants and nothing could see it).
- [x] **Step 2:** implement over a `RecordingBackupHost` composed from `tests/support/recording_commands.rs`.
- [x] **Step 3:** gates green.
- [x] **Step 4:** mutations — add `-h` to the archive argv; drop `--one-file-system`; skip the scratch cleanup; publish without the rename; emit `50` as a literal. Each kills its named test; whole-workspace scoring; a SURVIVED verdict owes a witness (plan 5, lesson 9).

### Task 4: `ops::backup` — restore, the operation that can destroy a working account

**Files:** `ops/src/backup/restore_backup.rs`, `archive/{extract_home_as_account.rs, extract_databases_as_root.rs, load_dump.rs}`, `model/restore_outcome.rs`; tests mirrored; fixtures including a hostile archive.

**Interfaces:**
- `restore_backup(host, account, backup_id, root, allowed_databases: &[DatabaseName], expected_sha256: &str, home_group, sink) -> Result<RestoreOutcome, BackupError>`, implementing Decision 2's seven steps in that order. (**CORRECTED**: no `store`/`destination`; `allowed_databases` is typed `DatabaseName`, not `String`; `home_group` was added for the finalising step's `web_server_group()` re-apply.)
- `RestoreOutcome { files_restored: bool, databases_restored: u32, databases_total: u32, rolled_back: Vec<String>, not_rolled_back: Vec<String> }`. R7: the caller cannot report success without reading these, because there is no boolean "ok" in the type.
- `allowed_databases` is the panel's decision, carried on the request: a database in the manifest that the panel no longer knows about is **refused, never created**. Creating one would resurrect a database the panel has forgotten, with a user nothing points at — the orphan the deletion cascade's comment describes.
- Error variants, each its own and each in one test: `ChecksumMismatch`, `ManifestVersionUnknown`, `ManifestAccountMismatch`, `ManifestDisagreesWithSidecar`, `UnexpectedArchiveMember { name }`, `UnknownDatabase { name }`, `DumpChecksumMismatch { database }`, `RolledBack { failed: String }`, `RolledBackPartially { failed: String, not_rolled_back: Vec<String> }`, `HomeParkedAt { path }`.

**The doc comment on `restore_backup` is written FORWARD from the question**, per rules/architecture.md's paragraph on justifications written after the choice. The question it answers, first sentence: *"If this fails halfway, what state is the account in, and can the customer still work?"* Then the table from Decision 2, then the point of no return, then the two things this does NOT restore (vhosts, certificates) — because a doc that lets a reader believe restore means undo is the defect that rule exists for.

**R3/R4, concretely:**
- `extract_databases_as_root(host, artifact, scratch)` — `tar --extract --file <artifact> -C <scratch> databases` with `--no-same-owner`; then each dump's SHA-256 is verified against the manifest. Root-side because the loader connects as the database superuser (R4).
- `extract_home_as_account(ids, artifact, staging)` — staging created root-side `0700` then chowned to the account; the extraction itself runs inside `fork_as_account`, so `tar` is the account and every created file is owned by them by construction. No `-P`; a member with an absolute path or a `..` segment is refused by the pre-scan in step 1 before `tar` ever runs, and `tar`'s own stripping is the belt behind that brace. The doc names both and says which is the brace.

- [x] **Step 1 — failing tests**, by name:
  `a_hostile_archive_member_outside_home_and_databases_is_refused_before_anything_is_touched` (fixture archive contains `../../etc/cron.d/pwn`),
  `an_archive_whose_checksum_does_not_match_is_refused_before_the_pre_restore_backup`,
  `a_manifest_that_disagrees_with_the_sidecar_is_refused`,
  `a_database_the_panel_does_not_know_is_refused_and_never_created`,
  `a_dump_whose_checksum_does_not_match_is_refused_before_any_drop`,
  `a_failure_before_the_first_drop_leaves_the_home_and_every_database_untouched`,
  `a_failure_after_the_first_drop_reloads_every_rollback_dump_in_reverse_order`,
  `a_rollback_that_itself_fails_names_the_databases_it_could_not_restore`,
  `a_failed_second_rename_reverses_the_first_and_the_account_has_its_home_back`,
  `a_restore_that_loses_a_database_reports_failed_not_completed` (R7 — the plan-5 defect, pinned),
  `the_home_is_extracted_inside_fork_as_account_and_the_dumps_are_not` (asserts on the recording host's call log which extraction ran in which context).
- [x] **Step 2:** implement. **Step 3:** gates.
- [x] **Step 4:** mutations — skip the member pre-scan; skip the dump checksum; drop the rollback dump before the `DROP`; make `RestoreOutcome` report success when `databases_restored < databases_total`; reverse the two renames' order. Each kills its named test.

### Task 5: `ops::backup` — list, delete, and what retention needs

**Files:** `ops/src/backup/{list_backups.rs, delete_backup.rs}`, `model/backup_summary.rs` (+); tests.

**Interfaces:**
- `list_backups(root, account) -> Result<Vec<BackupSummary>, BackupError>` — reads sidecars from the local root. (**CORRECTED**: there is no `host`, no `store`, and no `LIST` arm; an S3 listing was never built.) A sidecar that does not parse is reported as a summary in the `Unreadable(reason)` state rather than skipped in silence: a skipped-in-silence entry is invisible to retention and accumulates forever (plan 5, lesson 14 — the readable set beside the unreadable one is the distinguishing case, and it is the test).
- `delete_backup(host, store, account, backup_id) -> Result<(), BackupError>` — removes artifact and sidecar; a missing artifact is `NotFound`, not an error, per the proto's own promise; a missing sidecar beside a present artifact still removes the artifact.
- [x] **Steps:** failing tests (`an_unreadable_sidecar_is_listed_as_unreadable_beside_a_readable_one`, `deleting_an_absent_backup_reports_not_found`, `deleting_removes_both_the_artifact_and_its_sidecar`, `a_partial_artifact_is_never_listed`) → implement → gates → mutations (skip the sidecar removal → red).

### Task 6: The object store — S3-compatible transport and the bucket probe

> **Landed, and connected to nothing.** The transport, the probe and their tests exist; no
> operation calls `ObjectStoreHost`, so an S3 destination cannot be created, restored, listed or
> deleted. Whether to wire the seam or withdraw it is an owner decision, laid out with its costs in
> `docs/superpowers/notes/2026-09-07-backup-object-store-seam.md`. Do not treat the ticks below as
> "S3 works".

**Files:** `ops/src/backup/{object_store_host.rs, s3_object_store_host.rs}`; `maran-ops` `Cargo.toml` (+ `object_store` with the `aws` feature); tests.

**Interfaces:**
- `ObjectStoreHost` — **CORRECTED: the seam is landed but has NO CONSUMER, deliberately.** This line said it was "the seam every other task has been written against"; it is not, and the four operations above take a `LocalBackupRoot` and touch the filesystem directly. Wiring was attempted and refused on three measured obstacles: `list` answers `Vec<ObjectSummary>` (`{key, bytes}`), which cannot express the two inode facts a local listing reports (`UnmintedArtifactName`, `NotARegularFile`), so routing through it would DELETE four named tests; a listing would additionally become N downloads because the trait can only `get(key, into: &Path)`; and creation and restore would each gain a full extra copy of the artifact. An S3 destination therefore cannot be created, restored, listed or deleted, and the refusal is structural rather than a check that can be forgotten. Measurement: `.superpowers/sdd/2026-09-05-maran-backups/seam-wiring-report.md`; the trait's own doc comment now leads with "NOTHING CALLS THIS TRAIT YET". Signature as landed: `put(&self, key: &str, from: &Path, sink: &dyn ProgressSink) -> Result<u64, BackupError>`, `get(&self, key: &str, into: &Path, sink) -> Result<u64, BackupError>`, `delete(&self, key: &str)`, `list(&self, prefix: &str) -> Result<Vec<ObjectSummary>, BackupError>`, `probe_public_read(&self, key: &str) -> Result<PublicReadVerdict, BackupError>`.
- `PublicReadVerdict { Private, PubliclyReadable, Unproven { reason: String } }` — three states, not a boolean, because "the anonymous GET could not be made" is not "private" and must not be reported as one (R9). The panel refuses the destination on `PubliclyReadable` **and** on `Unproven`.
- `LocalObjectStoreHost` also exists — **but the local destination does NOT go through it.** The intended one-code-path property was not achieved; `create`/`restore`/`list`/`delete` open, rename and unlink files themselves. Same measurement as above.
- Credentials arrive as `SecretString` and are handed to the client at construction; the host's `Debug` is hand-written to omit them, and there is a test that formats the host and greps for the key.
- Every error crossing back is scrubbed: `BackupError::ObjectStoreFailed { message }` runs the message through a redactor that removes anything matching the configured access key id and any `X-Amz-Signature=…` query parameter, with a named test feeding a message that contains both.

- [x] **Step 1 — failing tests:** `a_public_bucket_probe_reports_publicly_readable`, `a_probe_that_cannot_reach_the_endpoint_reports_unproven_and_never_private`, `an_http_endpoint_is_refused`, `the_hosts_debug_output_contains_no_credential`, `an_error_message_quoting_a_signature_is_redacted`, `a_key_is_built_from_the_prefix_the_account_and_the_id_and_nothing_else`. Tests run against a local HTTP stub, not a real provider; the stub's limits are stated in the test module's doc — it proves our request shape and our redaction, and it proves NOTHING about a real provider's policy semantics, which Task 16's live pass covers.
- [x] **Step 2:** implement; if `cargo deny` refuses `object_store`'s tree, fall back to `aws-sdk-s3` and record the licence decision in the report — do not hand-roll SigV4 (rules/security.md item 9).
- [x] **Step 3:** gates + `maran licenses` diff pasted (final regeneration is Task 16, per plan 5's Ruling 39).
- [x] **Step 4:** mutations — make `Unproven` map to `Private`; delete the redactor. Each kills its named test.

### Task 7: Proto delta, the `BackupService` implementation, and a polygon that can say no

**Files:** `proto/agent/v1/backup.proto` (+) per Step 0; `agent/src/services/backup/{mod.rs, backup_service.rs, backup_status.rs, validated_destination.rs}`; `server.rs` (+); `agent/tests/backup_on_a_real_host.rs` + fixtures; `docker/polygon/*.Dockerfile` (+); `docker/README.md` (+ the new suite in the run command — `maran structure` now walks `*_on_a_real_host.rs` and fails when one is not listed); `.github/workflows/agent.yml` (+).

- [x] **Step 0 — the proto delta obeys rules/proto.md's additive law, and `maran proto` checks it against `proto/agent/v1/contract-baseline.txt`.** No field is deleted, renumbered or retyped. Concretely, all additions:
  - `BackupDestination` gains `string s3_endpoint = 5` (S3-compatible providers; empty = AWS), `string s3_access_key_id = 6`, `string s3_secret_access_key = 7`, `bool s3_path_style = 8`. The two credential fields carry a comment saying they are secrets, are never logged, and are never persisted by the agent (R8) — and the message's existing comment claiming credentials are provisioned out of band is REWRITTEN, with the reason, because a comment that describes what the code does not do is a defect of the same severity as the behaviour (rules/architecture.md).
  - `RestoreBackupRequest` gains `string expected_sha256 = 4` and `repeated string allowed_databases = 5`.
  - `RestoreBackupOk` gains `bool files_restored = 1`, `uint32 databases_restored = 2`, `uint32 databases_total = 3`, `repeated string not_rolled_back = 4` (it is an empty message today, so every number is free).
  - `CreateBackupOk` gains `string sha256 = 2`, `uint64 database_count = 3`.
  - `BackupInfo` gains `string sha256 = 4` and a `oneof state { ReadableBackup readable = 5; UnreadableReason unreadable = 6; }`, NOT a `bool readable` flag. Task 6 made the Rust `BackupSummary` structural — `Readable(ReadableBackup{manifest, artifact_bytes, artifact_sha256}) | Unreadable(UnreadableReason)` — so serde cannot build a summary that claims to be readable with no manifest. A flag on the wire would re-admit exactly that contradiction one layer down. Both arms are new field numbers, so this stays additive.
    **Stated limit:** proto3's `oneof` makes the arm exactly-one, but does NOT make `ReadableBackup.manifest` present — the guarantee is total in Rust and partial on the wire. The service therefore converts at the boundary and refuses a `readable` arm whose manifest is missing, rather than assuming the wire carries Rust's invariant. C# and the SPA switch on the case; neither reads a flag.
  - New rpc `ProbeDestination(ProbeDestinationRequest) returns (ProbeDestinationResponse)` with `PublicReadVerdict` as a new enum — new rpcs and new messages are always additive.
  - `maran proto` must pass; if it names a baseline diff, the diff is read line by line before `maran proto --accept` is typed, and the accepted baseline lands in its own change (rules/proto.md).
- **The service:** three-line rule — validate, call `ops`, map. `validated_destination.rs` revalidates every destination field with Task 1's types (rules/security.md item 1: the API validated already; the agent validates because it is root and the API is not). The streaming rpcs mirror `php_service.rs`'s `install_php_version` exactly: bounded `mpsc` channel of 16, `ReceiverStream`, terminal ok/error as the last item. The `DatabaseCatalog` seam is implemented here by delegating to `ops::db::list_databases` (Task 3's composition argument).
- **Polygon propositions** (`#[ignore]`, serial, both images):
  - create: a real account with a real home and two real databases → artifact exists `0600` in a `0700` directory, `tar tzf` lists `manifest.json`, `home/`, `databases/`; the recorded SHA-256 matches `sha256sum`;
  - **the symlink proposition**: the home contains `link -> /etc/shadow`; `tar tvf` shows a symlink and the archive does not contain shadow's bytes (grep the decompressed stream for a `root:` hash prefix and assert absent — a positive control first plants a known string in a real file under the home and asserts the grep FINDS it, so the probe is proved able to see);
  - restore: mutate the home and a database, restore, assert both are back, the files are owned by the ACCOUNT (`stat -c %U`), and the home root reads exactly `<acc>:<web-group>:750` (`stat -c %U:%G:%a`) — the arrangement `AccountOperations` creates and the one a restore is most likely to silently undo;
  - **the hostile-archive proposition**: hand-built archive with `../../etc/cron.d/pwn` → refused, and `/etc/cron.d/pwn` does not exist afterwards;
  - **the halfway proposition**: a restore whose second database dump is corrupt → reports FAILED, the first database is back to its pre-restore contents, and the home is untouched;
  - idempotency: creating twice with one id reports `AlreadyExists` and does not rewrite the artifact (compare mtime);
  - delete: removes both files; deleting again reports `NotFound`.
- [x] **Steps:** proto per Step 0 → regenerate → service → mapping unit tests per rpc arm → polygon on BOTH images with pasted totals → `maran agent check` / `structure` / `proto` / `handshake` green.

---

## Phase B — the panel

### Task 8: The agent client

**Files:** `Maran.Agent.Client/Services/BackupService/…` and the two interfaces, per File Structure. Mirrors `Services/PhpService/` for the streaming shape and `Services/CronService/` for the unary shape — read both first; `WireTypeContainmentTests` fails a leaked `Maran.Agent.V1` type.

**Surface:**
```csharp
IAsyncEnumerable<BackupCreateEvent> CreateAsync(
    string accountUsername, string backupId, AgentBackupDestination destination,
    CancellationToken cancellationToken);
IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
    string accountUsername, string backupId, AgentBackupDestination destination,
    string expectedSha256, IReadOnlyList<string> allowedDatabases, CancellationToken cancellationToken);
Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(string accountUsername, AgentBackupDestination destination, CancellationToken cancellationToken);
Task<Result<bool>> DeleteAsync(string accountUsername, string backupId, AgentBackupDestination destination, CancellationToken cancellationToken);
Task<Result<PublicReadVerdict>> ProbeAsync(AgentBackupDestination destination, CancellationToken cancellationToken);
```
`AgentBackupDestination` carries the S3 secret as a `string` the record's `ToString()` is overridden to omit — the C# mirror of `SecretString`, with a test that formats the record and asserts the secret is absent. `ResilientAgentBackupClient` follows the existing resilience decorators; a streaming call is NOT retried (a half-streamed restore replayed from the start is a second restore).
- [x] **Steps:** mapping tests per rpc arm and per terminal kind → implement → `AgentCapability.Backup` added to the Sdk enum (rules/security.md item 13: a new agent service must add a value or `AgentCapabilityGuard` throws) → gates.

### Task 9: The Backups module — entities, create, list, delete

**Files:** the module tree in File Structure minus restore/destinations/schedule; `Maran.Modules.Backups.Tests` added to the solution the way `Maran.Modules.Sftp.Tests` was; `AuditActions.cs` (+); `TaskKinds.cs` (+ `BackupCreate`, `BackupRestore`).

Concretely:
- `Backup` entity: `Id` (Guid, and it IS the agent's `backup_id`, so one identifier crosses the whole system), `AccountId`, `DestinationId`, `Status` (`Running|Completed|Failed`), `Kind` (`Manual|Scheduled|PreDeletion|PreRestore`), `SizeBytes`, `Sha256`, `DatabaseCount`, `StartedAt`, `FinishedAt`, `FailureCode`. `AccountId` means the global query filter applies — `TenantScopeTests` walks the model and fails an unfiltered entity, so this is not a thing anyone has to remember.
- `BackupRunner` (module service): opens a `PanelTask` (`TaskKinds.BackupCreate`), writes the `Running` row, consumes the agent's event stream, reports progress onto the task, and writes the terminal row and the audit entry. **The task and the row are written from the same terminal event**, so a completed task over a failed row is not representable.
- Create is admin-or-owner; list/get/delete are tenant-scoped with the 404 answer; delete refuses a backup whose row is `Running`.
- Audit: `BackupCreated` / `BackupCreationFailed` / `BackupDeleted` / `BackupDeletionFailed`, success and failure both, through `BackupAuditJournal` mirroring `CronAuditJournal`.
- [x] **Steps:** failing handler tests (create writes Running then Completed; a failed stream writes Failed and audits failure; delete of a Running backup refuses; IDOR 404 on all four surfaces) → implement → Testcontainers integration → i18n ×3 → mutations (write `Completed` regardless of the terminal event → the named test red; drop the failure audit → red).

### Task 10: Restore — the panel half

**Files:** `Commands/RestoreBackup/…`, `Controllers` (+), `Common/RestoreOutcomeDto.cs`, resources (+).

Concretely:
- The request carries `confirmAccountUsername`; the validator refuses anything but an exact match against the target account's system username. FluentValidation, and its message is one of the few that names the value the caller must type.
- The handler: refuse if a backup or restore is already running for the account (a panel-side check *and* the agent's lock — two layers, and the test asserts the panel's refusal is the one the customer sees); compute `allowedDatabases` from the Databases module's rows for this account, reached through an Sdk window (`IAccountDatabaseDirectory` — a new read-only window in `Maran.Sdk/Interfaces/`, implemented by Databases, returning names only, with its tenant semantics stated in its doc comment the way `IAccountDirectory.ListAsync`'s are); open a `PanelTask` (`TaskKinds.BackupRestore`); stream; and **map `RestoreOutcome` to the row exactly once**: `Completed` requires `files_restored && databases_restored == databases_total` (R7).
- Rate limit: 3 restores per account per hour, mirroring `LoginRateLimitPolicy`'s machinery with its own bucket.
- Audit: `BackupRestored` / `BackupRestoreFailed`, the latter carrying the failure code and the rolled-back/not-rolled-back lists in its detail, because that is the record an operator needs at 3 a.m.
- [x] **Steps:** failing tests (wrong confirmation refuses; a concurrent restore refuses; a partial outcome is Failed; the allowed-database list excludes a database the panel does not own; IDOR 404; rate limit) → implement → integration → mutations (accept any confirmation → red; map partial to Completed → red). — **ticked 2026-09-08 12:50 +04 on the evidence of task-10-13-report.md and the status row for Task 10 (route, handler, runner, translator all measured on the tree)**, not on a re-run in this session; the task's status row above carries the measurement and its date.

### Task 11: Destinations, credentials and the public-bucket probe

> **Read "The Task 9 / Task 11 boundary moved" above first.** `BackupOptions` already exists with
> `LocalRoot` only, and `Backup.DestinationId` is already `Guid?`. This task narrows that column,
> which `maran migrate guard` refuses without a `// contract-phase:` line.

**Files:** `Commands/{SaveBackupDestination,DeleteBackupDestination}/…`, `Services/S3DestinationProbe.cs`, `Validators/…`, `Options/BackupOptions.cs`, `Controllers/BackupDestinationsController.cs`, `Seeders/DefaultBackupDestinationSeeder.cs`, `Persistence/Configurations/BackupDestinationConfiguration.cs`.

Concretely:
- `BackupDestination` entity has **no `AccountId`** — it is host-wide configuration, so it is one of the entities `TenantScopeTests` must be told about *by name, with its reason*, in the exemption list (an exemption for an entity that no longer exists fails that test, which is why it is written where it is).
- The S3 secret column uses `EncryptedStringConverter` (the SMTP password's precedent). The GET returns `HasSecret: bool` and never the value — the named test `the_destination_response_never_carries_the_secret` and its mutation must go red.
- Saving an S3 destination runs `S3DestinationProbe` (R9) through `IAgentBackupClient.ProbeAsync`; `PubliclyReadable` and `Unproven` both refuse, with different error codes and different resx messages, because the operator's next action differs.
- `BackupOptions`: `LocalRoot` (bound from `Backups__LocalRoot`), `MaxArchiveBytes`, `MaxDumpBytes`. Startup validation refuses the module coming up with a `LocalRoot` that fails Task 1's rules, naming the env key — a silently-defaulted backup root is Decision 3's whole failure mode.
- The seeder creates the local destination on first run from `BackupOptions.LocalRoot`, once, and never re-seeds when the list is empty (plan 5 shipped a `WhitelistSeeder` that re-seeds on an empty list and the ledger records it as a deliberate non-fix; do not repeat the shape here — key the seeding on a marker row, and say so in the doc).
- Admin-only, 404 to customers, matching the Firewall controllers' idiom (read one and name it in the report).
- [x] **Steps:** failing tests (secret never in GET; public bucket refuses; unproven refuses; a local root under `/home` refuses at the validator AND at startup; the seeder does not re-seed after the operator deletes the row) → implement → integration → mutations (map `Unproven` to accept → red; drop the converter → the at-rest test red). — **ticked 2026-09-08 12:50 +04**: Task 11 landed (`task-11-report.md`), five mutations, five KILLED. Deviation recorded there: the public-bucket probe was NOT built, because the agent refuses every probe by design.

### Task 12: Schedules, the cadence, and retention

**Files:** `Commands/SaveBackupSchedule/…`, `Queries/GetBackupSchedule/…`, `Jobs/{BackupRunRequested,BackupRunHandler,RetentionRequested,RetentionHandler}.cs`, `Services/RetentionPruner.cs`, `Maran.Host/BackgroundServices/BackupScheduleScheduler.cs`, `Maran.Host/Extensions/BackgroundWorkExtensions.cs` (+).

Concretely (Decision 5):
- `BackupSchedule`: `AccountId` (nullable — null means "every account on the host", the operator's default policy), `DestinationId`, `Frequency`, `HourUtc`, `DayOfWeek?`, `RetainCount`, `Enabled` (false by default), `LastRunAt`.
- `BackupScheduleScheduler` (Host, 5-minute cadence) publishes `BackupRunRequested`; `BackupRunHandler` (module) selects due schedules with `LastRunAt` older than the frequency window, and **writes `LastRunAt` before starting the run**, so a restart mid-run does not start a second one an hour later on top of the first. `IClock` throughout; `DateTime.UtcNow` is a banned symbol.
- Retention (R12): after a run reports Completed, `RetentionPruner` deletes the oldest successful backups beyond `RetainCount` for that account+destination, skipping `PreRestore` and `PreDeletion` kinds (a safety copy that retention eats is not a safety copy) and skipping any backup a restore is currently reading. `BackupRetentionPruned` audit per deletion.
- A failed scheduled run: `Failed` row, `BackupCreationFailed` audit, destination marked unhealthy after two consecutive failures, and **no pruning** — R12.
- [x] **Steps:** failing tests (a due schedule fires once and only once across two cadence ticks; `LastRunAt` is written before the run; retention keeps exactly `RetainCount`; retention never removes a `PreRestore`; a failed run prunes nothing; a disabled schedule never fires) → implement → Testcontainers integration for the selection SQL → mutations (write `LastRunAt` after the run → the "once and only once" test red; prune on failure → red). — **ticked 2026-09-08 12:50 +04**: Task 12 landed and its four owed mutations were run whole-solution and ALL KILLED (`task-12-report.md:496-501`).

### Task 13: The final backup before account deletion (spec §12)

**Files:** `Maran.Sdk/Interfaces/IAccountBackupService.cs` (new), `Maran.Modules/Backups/Services/AccountBackupService.cs`, `Maran.Modules/Accounts/Commands/DeleteAccount/{DeleteAccountCommand.cs, DeleteAccountCommandHandler.cs}` (+), `Maran.Modules/Accounts/Controllers/AccountsController.cs` (+), the four sibling test doubles that construct the handler.

**The seam, and why this shape.** `Maran.Sdk/Interfaces/IAccountBackupService.cs`:
```csharp
Task<Result<Guid>> TakeFinalBackupAsync(Guid accountId, string accountName, CancellationToken cancellationToken);
```
An *act*-window rather than a message, and the interface's doc says why in the terms rules/architecture.md sets: the S3 credential and the local root never leave the Backups module, only the act crosses; and the caller must be able to *wait for the answer and refuse on it*, which a published message cannot give. It is **injected as `IAccountBackupService?`** — null when the Backups module is not composed. That nullability is the whole of R11 and the doc says so: a message with no subscriber raises nothing and reads exactly like success, which is the mechanism behind plan 5's cascade defect (progress.md, "no handler exists and there was nothing to release were literally the same observation"); a null reference is a fact the code can branch on and a test can set.

Behaviour, in `DeleteAccountCommandHandler` before the `AccountDeleting` cascade — before, because a final backup taken after the databases have been dropped is a backup of nothing:
- `_backups is null` → proceed, audit `FinalBackupSkippedNoModule`, and say it on the task's stage line.
- `command.SkipFinalBackup` (admin permission or a Provisioning API key with the scope; never a customer) → proceed, audit `FinalBackupSkipped` with the caller.
- otherwise → take it; failure refuses the deletion with `Error.Of(nameof(ErrorMessages.FinalBackupFailed), ErrorType.Failure)` and the account is left exactly as it was (the handler's existing "a cleanup failure aborts the deletion" doctrine, extended to this step, and the doc paragraph is amended in the same change rather than left describing three steps when there are four).
- The `Backup` row is kind `PreDeletion` and is **exempt from retention** and from the cascade that removes the account's rows — a final backup deleted by the deletion it was taken for is the joke this whole task exists to avoid. Its `AccountId` therefore names an account that no longer exists, which is deliberate; the entity's doc says so, and `ModuleAccountResidueAuditor` gets an explicit, dated exemption for `Backup` rows of kind `PreDeletion` — otherwise the residue audit refuses every deletion it is meant to protect. **That exemption is the single most dangerous line in this plan**, because it is a hole in the check that caught the cascade defect: it is narrowed to that one kind, it is tested (`a_residue_of_any_other_kind_still_refuses_the_deletion`), and the mutation that widens it to all kinds must go red.
- [x] **Steps:** failing tests (no module → proceeds and audits the right action; failure → refuses and the account survives; skip flag → proceeds and audits with the caller; a customer cannot set the skip flag; the `PreDeletion` row survives the cascade; any other residue still refuses) → implement → integration (a real deletion end to end with the module loaded) → mutations (treat a failed final backup as success → red; widen the residue exemption → red). — **ticked 2026-09-08 12:50 +04 on the evidence of task-10-13-report.md and the status row for Task 13, plus the live run's FLOW 4, which drove the refusal against a real agent**, not on a re-run in this session; the task's status row above carries the measurement and its date.

---

## Phase C — the panel's screens

### Task 14: The backups screens

**Files:** per File Structure; `useBackupsApi.ts`, `stores/backups.ts`, `locales/{en,ru,hy}/backups.json`, router, e2e.

The screens, concretely:
- `BackupsPage.vue` — the customer's and the administrator's list in one page, differing by what the store was allowed to fetch: id, kind, status badge, size, database count, created-at, and per-row Restore/Delete. A `Running` row shows live progress from the Tasks stream via the existing store (no second transport — R9 of plan 5).
- `BackupRestoreDialog.vue` — the plan's most important piece of UI. It states, in words the backend supplies (rules/architecture.md: the backend owns the domain text), exactly three things: what is replaced (files under the home; these named databases), what is NOT (vhosts, certificates, SFTP logins, the account itself), and that a pre-restore backup is taken first. Then it requires the account username typed exactly. The confirm button is disabled until it matches, and the typed value is what the request carries — the SPA does not "verify" it and then send a boolean.
- `BackupDestinationsPage.vue` / `BackupSchedulePage.vue` under settings, admin-only, hidden by the licence/permission machinery and enforced server-side. The S3 secret field is `UiPasswordInput` **without** generate (an existing provider secret is entered, not minted — the SMTP page's precedent) and shows the `HasSecret` hint.
- e2e propositions: the restore dialog's confirm stays disabled for a wrong username and the network stub asserts **zero** calls; a `Running` backup's progress advances from the stubbed task stream; the destinations form never renders a stored secret; a customer navigating to `/settings/backup-destinations` gets the 404 screen; the restore dialog lists the not-restored items (a test that reads the list, so a UI that quietly drops the caveat fails).
- [x] **Steps:** stubs → screens → lint/typecheck/build → `npx playwright test` with totals pasted. Before trusting any e2e verdict, CONFIRM THE MUTANT IS SERVED (plan 4's lesson 1, re-learned twice in plan 5). — **ticked 2026-09-08 12:50 +04 on the evidence of task-14-report.md and `backup-restore-screen-report.md`; `BackupRestoreDialog.vue` and 70 i18n keys per locale are on the tree**, not on a re-run in this session; the task's status row above carries the measurement and its date.

---

## Phase D — proving it, and closing

### Task 15: The installer, the polygon and the free-space truth

**Files:** `installer/lib/89-backups.sh`, `install.sh` (+), `uninstall.sh` (+), `installer/lib/10-preflight.sh` (+), `installer/panel.env.example` (+ `Backups__LocalRoot`, `Backups__MaxArchiveBytes`, `Backups__MaxDumpBytes` — rules/security.md item 7: every variable the product reads has an entry), `assert-installer-steps.sh` (+), polygon Dockerfiles (+ `tar`, `gzip`, the dump client).

- 89: create `/var/backups/maran` `root:root 0700` idempotently; write the three `Backups__` keys; assert afterwards that the directory exists with that owner and mode by `stat`ing it (an installer step that creates a directory and does not look at it is plan 5's lesson 7).
- preflight: warn when the filesystem holding the backup root has less free space than a configured floor, and say the number. A warning, not a refusal — an operator backing up to a mounted volume added later must still be able to install.
- **uninstall NEVER deletes `/var/backups/maran`.** It prints the path and the artifact count and says they were left. An uninstaller that removes a customer's only copy of their data is the worst defect this product could ship, and it would be one line.
- `assert-installer-steps.sh`: the step is invoked, the directory's mode is asserted, and the uninstaller is asserted to leave a planted artifact in place. Run the shell gate **to completion inside the real image** — `bash -n` cannot see an unbound variable (plan 5, lesson 15).
- [x] **Steps:** write → rebuild both images → run every polygon suite on both families with totals pasted → uninstall assertion with a planted artifact. — **ticked 2026-09-08 12:50 +04 on the evidence of task-15-report.md (polygon per family, `STRUCTURE-OK`, two argued deviations)**, not on a re-run in this session; the task's status row above carries the measurement and its date.

### Task 16: The Definition of Done pass

- [x] `maran licenses` — the object-store dependency lands in the third-party notices; paste the diff. This is the ONE regeneration (plan 5's Ruling 39).
- [x] i18n parity: `maran structure` + `ResourceKeyParityTests` (it auto-discovers the new resx family — name it in the report).
- [x] IDOR sweep: every new endpoint listed with its proving test; **no 403 on a tenant-scoped resource**, and the restore route is covered by construction because `BackupsAuthorizationTests`' route-coverage test enumerates routes off the controller. **CORRECTION to this line's own wording, 2026-09-08:** "admin surfaces answer 404 to customers" is wrong as written, and `BackupSchedulesController` is the finding this line asked for — it answers **403** (`:66`, `:89`), and it is right to. rules/security.md item 6 scopes the 404 rule to "a resource another account owns": a 403 there confirms the row exists. A server-wide administrator-only surface identifies no tenant resource, so there is nothing for a 403 to confirm, and nine controllers across five modules give that answer (Identity ×3, Monitoring, Backups schedules, Firewall ×3, Notifications), each saying so in its own doc — `BackupSchedulesController.cs:22`: "403 is the right refusal here and 404 would be the tenant answer". `Maran.Sdk.Tests/Extensions/ApiResultExtensionsStatusCodeTests.cs:61` maps `ErrorType.Forbidden` to 403 deliberately. The one place a 403 WOULD leak on an admin surface is a filtered feed, and `TasksAuthorizationTests.cs:104-106` argues exactly that and answers 404. **The rule is right and is not weakened; this plan's line overreached.** Owner ratification wanted — see the decision list.
- [x] Audit sweep: grep-verified against `Maran.Sdk/Contracts/AuditActions.cs`, **2026-09-08 04:02 UTC**. Eight actions, not the two the 21:50 pass found and not the four this plan named. Every one has at least one writer outside the constants file: `BackupCreated` (:286), `BackupDeleted` (:294), `BackupRestored` (:74, 3 writers), `BackupScheduleSaved` (:303, 2), `BackupRetentionPruned` (:312, 2), `FinalBackupTaken` (:81, 3), `FinalBackupSkipped` (:92, 2), `FinalBackupSkippedNoModule` (:103, 1). The two-actions-plus-`succeeded` idiom recorded below still holds; the deletion path adds three because "the operator chose to skip" and "this panel has no Backups module" are different facts (`AuditActions.cs:98`).
- [x] Threat note `docs/superpowers/notes/2026-09-05-backups-threat-note.md`: the artifact's contents and who can read them; Decision 3's refusal list and what each refusal prevents; the two-extraction split (R4) and the escalation it closes; what `fork_as_account` buys on the write side and what it cannot buy on the read side (R3, citing plan 5's Ruling 15); the S3 credential path end to end, including the redactor and what it does not cover; the public-bucket probe's `Unproven` state; the residue exemption of Task 13 and why it is narrow; and the accepted gaps, stated as gaps — no client-side encryption, no artifact download endpoint, full backups only (Decision 1's cost), a restore does not restore vhosts or certificates, and a running application may lose writes made during a restore.
- [~] Quiet-tree measurement — **STILL PARTLY MET; fifth attempt, 2026-09-08 12:51–13:40 +04, and the backend count is refused for the fifth time rather than faked.** Waited **49 minutes, 147 process samples 20 s apart, 144 of them BUSY**; the sample log is `.superpowers/sdd/2026-09-05-maran-backups/quiet-tree-wait.log` and the re-runnable script beside it as `quiet-tree-measure.sh`. **Three distinct peer sessions** (process groups 1728418, 1779844, 1790688) ran `dotnet test Maran.sln`, `dotnet build Maran.sln` and `cargo test --workspace` back to back for the whole window; the only gap was **about 40 seconds** at 12:51, which cannot hold a `--no-incremental` build plus an 18-project suite, and the six-consecutive-clear-samples requirement was deliberately NOT relaxed to fit it. The idleness protocol is five independent signals — six clear samples 20 s apart, CPU busy from `/proc/stat` deltas over 90 s, load average, files newer than 10 min under `backend/**/{bin,obj}` and the four source trees, `docker ps`, and a process check keyed on `comm` that **excludes the checker's own process group** (the first version of that check matched a `bash -c` wrapper whose command line merely mentioned `dotnet test`, which is the phantom-builder trap; it was reproduced and fixed within one sample). **No backend figure is quoted from this pass.** The figures on disk disagree by 58 tests and are all contended: **2608** across 18 projects (`task-11-report.md:343`), **2552** (`truncation-sweep-report.md:275`), **2551/2550** (`task-12-report.md:496-499`); `scripts/test-baseline.txt` declares **2541 across 18 projects**. Rust, polygon and SPA lanes are closed and are quoted from the baseline and from named reports: rust **1477 / 0 / 79 across 25 targets** (the committed baseline, refreshed since this plan's 1472/78), polygon **78 per family** (the eleven `*_on_a_real_host` rows' `ignored` column, which is what a family executes), SPA **270 passed / 0 failed / 1 skipped, 271 collected across 54 spec files** (`destinations-screen-report.md:198`; the 54 spec files are confirmed against today's tree).
  Superseded reading from the fourth pass, kept because it holds the evidence for the Rust lane and the build:
  > *(fourth pass, verbatim)* Quiet-tree measurement, `dotnet build --no-incremental` first (Ruling 57 — a warm total is not a measurement), all suites, per-project totals against the baseline, every failure named. A total that dropped is a failed run whatever the exit code says.
  **PARTLY MET, fourth pass, 2026-09-08 08:13-08:35 +04. A quiet tree was finally obtained and the idleness is documented, not asserted** — five independent signals in `.superpowers/sdd/2026-09-05-maran-backups/progress.md`: CPU busy **3.90%** from `/proc/stat` deltas over a 90 s window; **zero** files newer than 10 minutes under `backend/**/{bin,obj}`, `agent/target` or the four source trees; load average falling (0.39/0.50/1.45 → 0.88/0.63/1.35); a process check **excluding this session's own PIDs** finding no `dotnet test`/`build`, no `cargo`/`rustc`, no test host (the four surviving `MSBuild.dll /nodemode:1 /nodeReuse:true` entries are 5-hour-old node-reuse daemons, which is what makes a bare process count read ~28 on a quiet tree); and an EMPTY `docker ps`.
  Measured inside that window: **`dotnet build --no-incremental Maran.sln` → Build succeeded, 0 Warning(s), 0 Error(s)** (SDK 9.0.317 after `source scripts/dev`), and **`maran test rust` → 24 targets, 1472 passed / 0 failed / 78 ignored, `TEST VERDICT: OK`**, every baselined target reporting, exactly the committed baseline.
  **The backend count is still owed, and refused rather than faked for the fourth time.** Between the build and the test invocation the peer agent's live-stack run came up — `dotnet run` with cwd in `backend/src/Maran.Host`, containers `maran-live-agent` and `maran-postgres`, `vite --port 5173` and a Playwright-driven Chrome — and `maran test backend` REFUSED, naming the `dotnet run` pid. `dotnet run` writes the same `bin/Debug` a test host boots from and that host has those assemblies loaded, so a run here would corrupt the live pass as well as itself. Polled for a window every 30 s and did not get one. **Backend 2552 / 0 / 0 across 18 projects stands as `task-12-report.md`'s figure** (2550+2, 2550+2, 2551+1, 2550+2 on four whole-solution mutation runs), attributed and provisional. The SPA's **250 / 0 / 1, 251 collected across 51 spec files** is `backup-restore-screen-report.md:128`'s, not re-run (it would contend for port 5173 and the browser); the 51 spec files ARE confirmed against today's tree. The polygon's **77 / 0 per family** (`polygon-lane-count-gate-report.md:30-31`, images rebuilt at the current fingerprint) is confirmed here by arithmetic rather than by a container: this session's Rust per-target `ignored` column sums to exactly 77 and matches its per-suite rows one for one.
  **Baseline finding, reported and not fixed (`scripts/` is out of scope):** `scripts/test-baseline.txt` has rust at 1472 (exact) and backend at **2541, eleven behind** the truncation sweep's 2552, so `maran test backend` will report a count finding until `maran test backend --accept` is run on a quiet tree; the polygon baseline needs the same at 77. The file is also still **untracked**.
- [x] Mutation ledger: every task's score, whole-suite scored, every SURVIVED verdict carrying its witness or withdrawn.
  **MET, 2026-09-08.** `.superpowers/sdd/2026-09-05-maran-backups/progress.md` now exists — the file the Execution notes require and which this plan had never had — and it carries the consolidated ledger, built by reading every report under `.superpowers/sdd/` and this plan's directory. **167 measurements: 132 KILLED, 20 SURVIVED, 15 with no verdict.** Every kill names the test that made it; every survivor carries its witness and its disposition; every unscorable measurement says why it measured nothing (seven mutants that did not compile, four aborted lanes, one that proved a positive control instead of its claim, one never run, and Task 12's originally-blocked four). Nothing was re-run and no verdict was invented. **Task 12's hole is closed**: a follow-up session ran all four of its recorded commands on the whole 18-target solution and killed all four (`task-12-report.md`, the section appended 2026-09-08). Eight survivors remain open and are listed as owner items in the ledger's §5; the disagreements between reports — the polygon count's 71 → 73 → 77 sequence, the four-times-repeated `RestoreBackupCommand` finding, the `available_bytes` delete-then-restore, the backend total's climb from 2264 to 2552, and one report that says it applied two mutations and writes up one — are recorded in §4 with both figures cited.
- [x] **The live browser run against a real host.** **MET, measured 2026-09-08 12:52 +04 by reading the report on disk — the "NOT MET / blocked" wording below is retracted.** `.superpowers/sdd/2026-09-05-maran-backups/live-run-report.md`, 318 lines, records the run: a real stack stood up (PostgreSQL from the repo compose file, `maran-agent` as root inside the `maran-polygon-ubuntu24` image on a host bind-mounted socket, the API and the Vite SPA), Ruling 60 honoured (`:46` — a symbol the change introduced was confirmed in the RUNNING binary before anything was measured), and four numbered flows driven in a browser: FLOW 1 take a backup (`:78`), FLOW 2 restore, with both failure shapes driven as well (`:106`), FLOW 3 delete a backup (`:158`), and **FLOW 4, the refusal — the half nothing on this tree drives against a real agent** (`:167`): a broken destination, a deletion attempted, the account still there afterwards and the audit carrying the failed `FinalBackupTaken`. Audit actions actually written are listed at `:194`. **The run is evidence, and it is also the largest single source of findings in this plan**: five defects, each written as a prose diff for the owner (`:206-274`) — the schedule screen nothing can reach (HIGH), a typed agent refusal answering HTTP 500 (MEDIUM), a failed restore burning the operator's hourly restore budget (MEDIUM), a deletion dialog that never mentions the final backup (LOW), and raw identifiers on `/tasks` (LOW) — plus the finding that **no composition in this repo stands the product up with an agent** (`maran dev` starts db+api+SPA and no agent), and the declared collision that a first `maran migrate apply` re-exported `.env` and applied migrations to the peer session's `maran_live` database (additive, 0 destructive statements, but the peer should know). **Stated blind spot, and it is by construction:** no `UserRole.Customer` is produced anywhere in `backend/src`, so every flow was driven as an administrator and the customer-facing halves are UNPROVEN rather than untested (`:287`).

**Task 16 re-walked 2026-09-08 03:28–04:10 UTC (third pass)** — evidence per item is in
`.superpowers/sdd/third-reconciliation-report.md`, which extends
`.superpowers/sdd/2026-09-05-maran-backups/task-16-report.md` and
`.superpowers/sdd/rules-second-reconciliation-report.md` rather than redoing them. **Superseded in part on 2026-09-08 08:13-08:35 +04**: the mutation ledger is now MET
(`.superpowers/sdd/2026-09-05-maran-backups/progress.md`), the quiet-tree item moved from refused to
partly met on a documented quiet window (Rust confirmed, backend still owed), and the live browser
run is in flight.

**The open owner decisions this plan raises are consolidated in
`docs/superpowers/notes/2026-09-08-open-owner-decisions.md`** rather than scattered across thirty
reports. The one that gates merging: **five threat notes carry the second-reviewer requirement as
OUTSTANDING** (four at 04:08, five re-measured 2026-09-08 12:55 +04 — `2026-09-08-agent-shutdown-threat-note.md` has landed since), which under rules/security.md makes this work unmergeable to protected `main` until a
human reads them.

---

## Execution notes for the controller

- Order: 1 → 2 serial (Task 2 consumes nothing of Task 1 but both touch the rules maps; serialise the map edits). 3, 5, 6 after 1–2 (disjoint files); 4 after 3 (shares `backup_error.rs` and the host trait); 7 after 3–6. 8 after 7. 9 after 8; 10, 11, 12 after 9 (they share `AuditActions.cs` and the module's `DbContext` — serialise the appends or coordinate); 13 after 9 and 11 (it needs the module and a destination to exist); 14 after 10–12; 15 after 7; 16 last, on a quiet tree.
- **Task 10 carries an Sdk widening and therefore never runs concurrently with 11, 12 or 13.** `IAccountDatabaseDirectory` adds an interface the Databases module implements and every sibling module's test doubles must satisfy; plan 5 shipped exactly this shape as `AccountSnapshot`'s `DiskQuotaMb` and it recompiled 29 files, not the 7 the controller predicted. The widening lands atomically inside Task 10, with the implementation and every test double named in the brief before the task starts.
- Fresh implementer per task with a brief; adversarial review told to attack; every ruling recorded in `.superpowers/sdd/2026-09-05-maran-backups/progress.md`.
- The four rules amendments are applied by Tasks 1 (§1, §2), 3 (§3) and 11 (§4) exactly as written above. Any further rules change an implementer wants is a BLOCKED escalation, not an edit.
- Standing requirements carried from plan 5, binding here: every mutation harness cross-checks the named failures against the failed count and aborts when the output has no test-result line; restore with a fresh mtime and `cmp`; score against the whole workspace, never filtered; confirm a dev server serves the mutant before believing an e2e verdict; and sweep the docs for identifiers that no longer exist by resolving every backticked name, not by grepping for what you remember removing.

## Self-review

- **Spec coverage.** §11 "archives of an account's files" → Tasks 3, 7; "dumps of its databases" → Task 3; "on a schedule" → Task 12; "locally and S3-compatible" → Tasks 3, 6, 11; "restore from the UI" → Tasks 4, 10, 14; "retention" → Task 12. §12 "`DELETE /accounts/{id}` (final backup → deletion)" → Task 13. Issue #6: create/restore/list/delete as streaming RPCs with progress → Tasks 3–7 (list and delete are unary and stay unary — the stub proto already made them unary, they complete in milliseconds, and changing an rpc's streaming shape is a BREAKING proto change, so making them streaming would need a v2 directory; recorded as a deliberate deviation from the issue's wording, with its reason). Idempotent → R-numbered per operation and tested per operation. Audited → Tasks 9–13 and the Task 16 sweep.
- **Type consistency, checked name by name against where each is defined.** `BackupId`, `LocalBackupRoot`, `S3Bucket`, `S3Region`, `S3ObjectPrefix`, `SecretString` (Task 1) → consumed in Tasks 3, 4, 6, 7. `BackupHost`, `DatabaseCatalog`, `ProgressSink`, `BackupManifest`, `BackupSummary` (Task 3) → consumed in 4, 5, 6, 7. `ObjectStoreHost` (Task 6) → **consumed nowhere**, by the measured decision recorded at Task 6. `RestoreOutcome` (Task 4) → mapped in 7, carried on the wire by Task 7's `RestoreBackupOk` fields, consumed in Task 10. `PublicReadVerdict` (Task 6) → proto enum in 7, client in 8, refusal in 11. `IAgentBackupClient` (Task 8) → 9, 10, 11. `IAccountBackupService` (Task 13) → the only Sdk widening in this plan besides `IAccountDatabaseDirectory` (Task 10) — both named at definition and at every consumption site. `AgentCapability.Backup` (Task 8), `TaskKinds.BackupCreate`/`BackupRestore` (Task 9).
- **Placeholder scan.** No step says "add appropriate error handling", "handle errors", "etc.", or "as needed". Every refusal list is enumerated; every value is in the Global Constraints table; the two facts this plan does not know — which dump binary is real on each family, and whether `--no-tablespaces` is accepted — are assigned to Task 2 as measurements with a pasted transcript, not left as assumptions.
- **Every task ends in an independently testable deliverable** with its own gates: Tasks 1–7 end green on `maran agent check` + `maran structure` (+ `proto`/`handshake` where the contract moved); 8–13 on `dotnet test` with per-project totals; 14 on lint/typecheck/build/Playwright; 15 on the polygon and the installer assertions; 16 on the whole set plus the live run.
- **Sixteen tasks, and why that many.** The agent's four operations are four tasks because create and restore have different threat models and a reviewer must be able to reject one without the other; the object store is its own task because it is the only new dependency and the only network egress; the proto+service+polygon is one task because a contract change and its implementation must land together. The panel splits along the authorization boundary — tenant-scoped operations (9, 10) apart from host-wide configuration (11, 12) — because that is the boundary the IDOR sweep checks. Task 13 is separate because it edits another module's most dangerous handler. Fourteen would mean folding restore into create or destinations into schedules, and both folds hide the seam that matters; eighteen would mean splitting list/delete apart, which nothing would review differently.

## Residuals

Work identified after the plan's sixteen tasks closed, written to be picked up as-is.

### R1 — Prove the child `PATH` allow-list entry, on both families

**The gap.** `agent/crates/agent-core/src/utils/apply_child_environment.rs` gives every spawned
child a cleared environment plus exactly two variables: `LC_ALL=C` and
`PATH=/usr/sbin:/usr/bin:/sbin:/bin`. The doc comment on `PATH_VALUE` argues the entry exists for
the package managers — the agent's own argv are all absolute paths from the `DistroAdapter`, but
`apt-get`/`dnf` run packaged helpers and maintainer scripts by BARE NAME. The argument is sound and
was never measured: no polygon suite installs a package, so nothing proves a maintainer script
resolves a bare name under that `PATH`, and nothing would go red if the allow-list were later
trimmed to `LC_ALL` alone or if `PATH_VALUE` lost a directory.

**What has already been measured (Ubuntu 24.04 host, dpkg 1.22.6, non-root).** A probe `.deb` whose
`postinst` prints its `PATH` and calls `update-alternatives` by bare name, installed with
`dpkg --root=<tmp> --force-not-root,script-chrootless -i`:

| Child environment | Result |
|---|---|
| `PATH=/usr/sbin:/usr/bin:/sbin:/bin` (our `PATH_VALUE`) | `MAINTSCRIPT PATH=[/usr/sbin:/usr/bin:/sbin:/bin]`, `BARE NAME RESOLVED: update-alternatives`, rc 0 |
| `env -i LC_ALL=C` (the allow-list trimmed to one entry) | `dpkg: error: PATH is not set`, **rc 2** |
| `env -i LC_ALL=C PATH=/usr/local/bin` (a `PATH` without the distro dirs) | `dpkg: warning: 'sh' not found in PATH or not executable` (and the same for `rm`, `tar`, `diff`, `dpkg-deb`, `ldconfig`, `start-stop-daemon`), then `dpkg: error: 7 expected programs not found in PATH or not executable` |

Two conclusions the doc comment did not have. First, dpkg does **not** fall back to a built-in
default when `PATH` is unset on the install path — it refuses outright, so a trimmed allow-list is
not a subtle maintainer-script failure on the Debian family, it is every `apt-get install` failing
at once. (`dpkg -l` with `PATH` unset still succeeds; the query path never calls dpkg's
`setup_path()`, so a query-based probe would measure nothing — do not write one.) Second, dpkg
names the four directories it expects, so `PATH_VALUE`'s *contents* are load-bearing and not just
its presence.

**Status: the RHEL half is UNPROVEN.** No `rpm` exists on the measuring host, so nothing here says
whether `rpm` supplies scriptlets a `PATH` of its own (which would make the RHEL family immune and
leave `dnf`'s own Python subprocess calls as the real exposure) or passes the parent's through. The
task below must determine that by measurement and record the answer, not assume symmetry with dpkg.

**The task.** Add a polygon suite `agent/crates/agent/tests/package_install_on_a_real_host.rs`,
running on both images, that spawns the package manager through the agent's own code path — not a
hand-built `Command` — so the assertion is about `apply_child_environment` and not about a
restatement of it. Concretely: drive `ops::php::install_php_version`'s host (`ProcessPhpHost`, which
routes through `spawn_argv` → `apply_child_environment`), or, if a real PHP install is too heavy for
the PR smoke matrix, add the cheapest package below through the same host type.

Cheapest package that exercises a maintainer script, per family:

- **Debian family: `netbase`.** 13,112 bytes, `Depends: <none>`, ships a `postinst`
  (`/var/lib/dpkg/info/netbase.postinst`), and the `ubuntu24.Dockerfile` already installs it — so
  `apt-get install -y --reinstall netbase` re-runs the maintainer script with no new dependency
  graph. If the image has no package cache at test time, build the probe `.deb` in the test instead
  (`dpkg-deb --build`, no network at all) and install it with `dpkg -i`; that is strictly cheaper and
  is what produced the table above.
- **RHEL family: to be selected by measurement, first candidate `crontabs`** — tiny, `noarch`, and
  the cron packages are already in the `alma9` image. Select it with
  `rpm -q --scripts <pkg> | grep -q '^post'` over the image's installed set and pin the winner in the
  test with its measured size, rather than trusting this line.

**The exact assertions.**

1. The install exits 0 through the agent's spawn path.
2. The maintainer script's own view of the environment is captured and asserted, not inferred. Give
   the probe package a `postinst`/`%post` that writes `PATH=$PATH` to a file under the test's
   temporary directory, and assert that file contains exactly
   `agent_core::utils::apply_child_environment::PATH_VALUE` — by referring to the constant, never by
   restating the string, so a change to `PATH_VALUE` moves the assertion with it.
3. A bare-name program the script invokes actually resolved: the script runs
   `update-alternatives --version` (Debian) / the RHEL equivalent chosen above and writes its rc to
   the same file; assert `0`.
4. Print an explicit `UNOBSERVED HERE:` line for anything the container cannot see, per
   rules/testing.md.

**The inverse control (required — a refusing gate that never sees good input proves nothing).** In
the same suite, run the identical install once more through a spawn whose environment is
`CHILD_ENVIRONMENT` **with the `PATH` entry filtered out**, and assert it FAILS. Build that
environment by filtering the real constant —

```rust
let trimmed: Vec<_> = CHILD_ENVIRONMENT
    .iter()
    .filter(|(name, _)| *name != PATH_VARIABLE)
    .collect();
```

— so the control cannot silently drift into re-adding `PATH`. On the Debian family the measured
expectation is a non-zero exit with `PATH is not set` on stderr; on the RHEL family the expected
failure mode is whatever the measurement above establishes, and **if the RHEL install SUCCEEDS
without `PATH`, that is the finding**: say so in the test's own output and in the doc comment on
`PATH_VALUE`, because it would mean the entry is carried for one family only and the comment
currently claims both.

A third case is worth the two lines it costs, given the dpkg table: run with
`PATH=/usr/local/bin` and assert the install still fails. That is what proves the four directories
in `PATH_VALUE` are the load-bearing part, and it is the mutation a future editor is most likely to
make (adding `/usr/local/*` back, which the doc comment argues against).

**Blocked on.** Nothing in the agent. The suite needs the two polygon images to install a package
at test time, so it lands with whoever owns `docker/polygon/`.
