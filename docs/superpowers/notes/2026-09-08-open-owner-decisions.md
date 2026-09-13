# Open owner decisions — consolidated

Written 2026-09-08 04:15 UTC, from a read-only audit of branch `dev` at HEAD `92189be` with 569
uncommitted entries in the working tree. These decisions were scattered across roughly thirty
session reports; each one below names where its evidence lives so nothing has to be re-derived.

Nothing here is a defect an implementer may close on their own judgement. Each is a question only
the owner can answer, and each is written as the question rather than as a recommendation dressed
up as one.

## 1. The S3 seam versus the spec's promise — **no longer blocking Task 11; still open**

The spec (§11) promises storage "locally and S3-compatible". The tree has the dependency
(`object_store 0.14.1`, `THIRD-PARTY-NOTICES.md:132`), the trait, two implementations and their
tests — and **no consumer**: `grep -rn ObjectStoreHost agent/crates --include=*.rs` finds the trait,
its impls, three `mod.rs` re-exports, one doc cross-reference and the tests. The agent refuses every
remote destination at its own boundary (`agent/crates/agent/src/services/backup/backup_service.rs`,
`remote_destination_refused.rs`), and the panel has no destinations table, no probe endpoint and no
screen.

The two options are argued in full at `docs/superpowers/notes/2026-09-07-backup-object-store-seam.md`
— Option A: amend the spec to local-only for v1 and delete the seam; Option B: extend the seam by
two methods and wire it. **Re-measured 2026-09-08 12:55 +04:** Task 11 has since landed WITHOUT waiting on this, in the only
shape that does not pre-empt the answer — the remote destination is *representable and refused*, in
one allow-list (`Domain/Policies/RemoteDestinationPolicy.cs`) plus a second refusal for a row that
reached the table another way (`Services/BackupDestinationResolver.cs`), five mutations all killed.
The public-bucket probe was deliberately NOT built, because the agent refuses every probe by design
and a probe today would be a check that cannot observe what it reports on. So the question is
unchanged and the cost of either answer is now lower: Option A deletes a seam and two refusals,
Option B extends the seam and turns the refusals into a code path. **Nothing further should be built
on top of this until it is answered**, and
the spec is currently a document describing something the product does not do, which
rules/architecture.md treats as a defect of equal severity to the behaviour.

## 2. Encryption at rest — an accepted gap that was never ratified

Backups are written unencrypted to `/var/backups/maran`. This was a plan decision, recorded as an
accepted gap in `docs/superpowers/notes/2026-09-05-backups-threat-note.md`, and the owner has not
ratified it. It interacts with decision 1: a local-only product stores plaintext customer data at
`root:root 0700` on the same host; a product that ships to S3 puts it on someone else's disk.

## 3. `403` versus `404` on administrator-only surfaces — the plan says one thing, nine controllers do the other

rules/security.md item 6 is correct as written and needs no change: it scopes the 404 answer to
"a resource another account owns", because a 403 there confirms the row exists. The
**backups plan's** Task 16 line overreached — it said "admin surfaces answer 404 to customers; no
403 exception exists in this plan".

Measured 2026-09-08 03:45 UTC: nine controllers across five modules answer **403** to a signed-in
customer on server-wide administrator surfaces — Identity (`SetupController`, `AuthController`,
`SecurityPolicyController`), `MonitoringController`, `BackupSchedulesController`, Firewall
(`FirewallBansController`, `FirewallRulesController`, `FirewallWhitelistController`) and
`SmtpSettingsController`. Each argues it in its own doc; `BackupSchedulesController.cs:22` puts it
best: no tenant dimension exists to hide, so 403 is the refusal and 404 would be the tenant answer.
`Maran.Sdk.Tests/Extensions/ApiResultExtensionsStatusCodeTests.cs:61` maps `ErrorType.Forbidden` to
403 deliberately, and `TasksAuthorizationTests.cs:104-106` shows the one admin surface that must
still answer 404 — a filtered feed, where a 403 would confirm the feed exists.

**The controllers are right.** What is wanted from the owner is ratification of the distinction in
one sentence, so it stops being re-litigated: *tenant resource → 404; server-wide admin surface with
no resource identified → 403; filtered admin feed → 404.* The plan line has been corrected;
rules/security.md was deliberately NOT touched, because it already says this.

## 4. `PreDeletion` is bounded by an administrator only

Task 13 refuses an account deletion whose final backup failed, and the argument for that asymmetry
is sound (`DeleteAccountCommandHandler.cs:305-315`: a wrong refusal is a retry, a wrong proceed is
permanent). The escape hatch is `SkipFinalBackup`, and **only an administrator can pass it**. So an
account whose data can never be archived — a permanently full destination, a home the agent cannot
read — is deletable by an administrator and by nobody else.

The question: is that the intended bound? It is defensible, and it is stated rather than hidden, but
it means a customer-initiated deletion can be made impossible by a server condition the customer
cannot see or fix.

## 5. The missing pre-restore copy

`BackupKind.PreRestore` still has **no producer**. Task 10 landed the whole restore path without it.
It is used only in doc comments (`Backup.cs:265`, `:307`) and in tests that seed it, which is the
shape where tests read as coverage for a state production cannot reach.

It is now honestly disclosed to the customer — `frontend/src/locales/{en,ru,hy}/backups.json`,
`restore.noSafetyCopy`: "No copy is taken of the account as it stands now. The panel does not back
up before it restores, so today's state is not recoverable once this starts." That is the right
thing to have done, and it is not the same as deciding.

The question: build the pre-restore copy, or delete the enum value and the doc comments that promise
it? Leaving a third state — the value present, unwritten, and explained — is the option that ages
worst, because the next reader has to re-derive all of this.

## 6. Committing the refreshed baseline, and the one legitimate downward row

`scripts/test-baseline.txt` is **untracked** (`git status --porcelain`), so the numbers every
counting gate scores against are not committed. Summed from the file, **re-measured 2026-09-08 12:55 +04**: **rust 1477 passed / 79 ignored across
25 rows** (the rust rows have been refreshed since this note was written, which said 1472/77) and
**backend 2541 across 18 projects**, which is the row still behind the tree. The polygon lane's per-family
count follows from the same file by arithmetic — the eleven `*_on_a_real_host` rows carry **78**
`#[ignore]`d tests between them, which is what a polygon family executes — and the SPA still has no
committed baseline at all. A `maran mutate` run
already prints baseline FINDINGs that are noise against the stale committed state
(`task-10-13-report.md:720`), and the report's own note there is "Owner: refresh the baseline".

One row moves DOWNWARD legitimately, which is the case rules/testing.md tells everyone to treat as
a failed run — so it needs the owner's signature rather than an implementer's. Commit the refreshed
file, with that row named in the commit message and the reason it fell.

Related, and worth deciding at the same time: **`maran test` is not wired into CI.**
`backend.yml:67` runs bare `dotnet test` and `agent.yml:59` bare `cargo test`, so the baseline
comparison — the whole "a total that dropped is a failed run" protection — is enforced only in the
polygon lane and the mutation harness. This is now stated as a known gap in rules/testing.md's
CI-gates section rather than left implied.

## 7. FIVE threat notes carry the second reviewer as OUTSTANDING — this branch cannot merge to `main`

Under rules/security.md ("Sensitive change escalation"), a privileged change with no second reviewer
is **not compliant**; it may live on a branch and **MUST NOT merge to `main`**. Measured
2026-09-08 04:08 UTC, `grep -in OUTSTANDING docs/superpowers/notes/*.md`:

| Note | Where it says OUTSTANDING |
|---|---|
| `2026-09-05-backups-threat-note.md` | lines 7, 410 |
| `2026-09-07-installer-privileged-steps-threat-note.md` | lines 8, 223, 326 |
| `2026-09-08-account-password-state-attestation-threat-note.md` | line 16 |
| `2026-09-08-account-unlock-threat-note.md` | line 12 |
| `2026-09-08-agent-shutdown-threat-note.md` | line 5 (**added 2026-09-08 12:55 +04**; it did not exist when the 04:08 sweep ran) |

Five notes, all privileged surfaces (the agent's backup/restore path, the installer's privileged
steps, an account password-state attestation, account unlock, and the agent's shutdown path and its
systemd unit). **Re-measured 2026-09-08 12:55 +04** — the count was four at 04:08 and is five now,
which is the point: this list is a dated measurement, not a live status, and it grows whenever a
privileged change lands. A further set of older notes
(`2026-08-30-auth`, `2026-08-30-privs`, `2026-08-31-log-tail-openat`,
`2026-09-04-cron-firewall-monitoring`) demand a second reviewer and record no outcome either way,
which is not evidence of discharge; `2026-09-02-databases-sftp` says the same thing in prose without
the label. `2026-09-03-panel-socket-threat-note.md:132` records a measurement made BY a second
reviewer and appears discharged.

**This is the item that gates everything else.** A human has to read five documents. Nothing an
agent session can do substitutes for it, and the rule was deliberately not weakened to say otherwise.

## 8. Two promises the type system makes that the code does not keep

Found by a product-wide enum sweep (2026-09-08 04:05 UTC: every enum member in `backend/src`,
grepped for a non-doc use outside its defining file — 7 of 131 have no producer).

- **`UserRole.Customer` has no producer anywhere.** The only `new User(` in `backend/src` is
  `CompleteSetupCommandHandler.cs:75`, with `UserRole.Admin`. Creating a hosting account creates no
  panel login, and `AuthController` publishes no registration route. **No customer can sign in to
  this panel today.** Every customer that exists is constructed in a test fixture — so the whole
  tenant-scoped surface, and every IDOR test proving it, is exercised only against state no
  production path can produce. This is not a Backups defect and it is far larger than this plan; it
  is filed here because this is where it was found.
- **`LicenceTier.AddOn` and `LicenceTier.PlanGated` have no producer.** All ten module manifests
  declare `LicenceTier.Included`. The paid-tier half of the product's model exists as two enum
  values and nothing else.
- Adjacent, smaller: **`SessionRevocationReason.RevokedByAdmin` has no writer** — five siblings do,
  and there is no administrator session-revocation surface, so the enum asserts a capability the
  panel does not have.

`AgentCapability.System` and `ChartRange.LastDay` were the two false positives in that sweep and are
explained in the backups plan's status section; both would benefit from one sentence in their own
doc comment so the next sweep does not re-open them.
