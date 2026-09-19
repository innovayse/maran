# Maran Quota Enforceability — Closing Issue #29

Normative for whoever executes it: task-by-task, implementation first, tests in the dedicated pass
that follows each task (rules/testing.md). Every claim below carries `path:line`; anything that
does not is marked NOT FOUND or NOT KNOWN rather than assumed.

## 0. What this plan cannot promise before it is run

This plan was written by reading, `grep`, and `git log` only — no build, no `maran test`, no
`maran mutate`, two other lanes hold the tree's write-lock. Every citation below was read in full;
nothing is paraphrased from memory. Where the plan relies on how `quotaon(8)`, `/proc/mounts`, or
NFS client quota behave, that is general Linux administration knowledge, not a repository fact —
each such point says so explicitly, because this project's own rule (rules/architecture.md,
"doc comment that describes what the code does not do") punishes confident claims that outrun
what was actually checked.

## 1. Ground truth, established by reading

**The defect as filed.** `agent/crates/ops/src/accounts/quota_blocks.rs:26-32`,
`QuotaBlocks::parse_hard_limit`, reads `quota -u -w` and returns `None` when there is no quota
line — its own doc comment: "the ordinary state of a filesystem mounted without quotas — not an
error, just no limit." The caller, `AccountOperations::usage`
(`agent/crates/ops/src/accounts/account_operations.rs:627-648`), folds that `None` straight to
`quota_bytes: 0` (`account_operations.rs:641-644`, `.unwrap_or(0)`), and the wire and C# sides
repeat the same collapse:

- `proto/agent/v1/accounts.proto:230-231` — `AccountUsage.quota_bytes`, "Quota currently in
  effect, in bytes," no third state.
- `backend/src/Maran.Agent.Client/Services/AccountsService/AccountUsageDto.cs:5` — "the quota in
  force, in bytes; zero means no quota is set." One meaning documented for a wire value that
  today already carries two: "an operator chose no limit" and "this filesystem cannot hold a
  limit at all."
- The account-facing screen that actually renders a customer's allowance is **not**
  `GetAccountQueryHandler` (`backend/src/Maran.Modules/Accounts/Queries/GetAccount/GetAccountQueryHandler.cs:29-40`,
  which projects `AccountDetailDto` with no usage or quota field at all) — it is
  `Maran.Modules.Monitoring`'s disk-usage listing:
  `backend/src/Maran.Modules/Monitoring/Common/AccountDiskUsageDto.cs:27-31` and
  `backend/src/Maran.Modules/Monitoring/Queries/ListAccountDiskUsage/ListAccountDiskUsageQueryHandler.cs:149-152`
  (`ToQuotaBytes`). **This is the exact seam the issue names**: `QuotaBytes` here is computed
  *purely* from `account.DiskQuotaMb`, the plan's stored allowance
  (`backend/src/Maran.Sdk/Contracts/AccountSnapshot.cs:52-53,91`) — it never asks whether the
  filesystem holding that account's home can enforce anything. The agent's own copy of
  `quota_bytes` on the wire is written 0 always and explicitly not projected here
  (`proto/agent/v1/monitor.proto:178-185`, `AccountDiskUsageDto.cs:8-9`), so today NOTHING in this
  path asks the enforceability question at all — the number an operator and a customer both see is
  the plan's figure, full stop, exactly as the issue states.
- `agent/crates/ops/src/accounts/account_operations.rs:656-670`, `apply_quota`, calls `setquota`
  and surfaces only success or `AccountError::CommandFailed` — it does not check mount options
  first, and its own doc comment (`account_operations.rs:651-655`) says nothing about
  enforceability either.

**Confirmed independently, in a plan already in this tree.**
`docs/superpowers/plans/2026-09-19-maran-staging-install.md:309-349` (section 3.2, "Quotas") ran
the exact grep this task specifies — `grep -rn "quotaon\|quotacheck\|usrquota\|grpquota" agent/
backend/ installer/ docs/` — and reports it "returning zero hits anywhere in the product or its
docs" before this plan existed. That plan's own conclusion, carried forward here rather than
re-derived: "a positive quota observation taken without [checking mount options] is worthless."
This plan turns that operator instruction into product behavior.

**The installer side.** `installer/lib/20-dependencies.sh:67-77,101,104,138,170` installs the
`quota` package family (`setquota`, `quotacheck`, `quota`) on both distro families — confirmed by
the staging-install plan's citation, re-checked here:
`grep -n "quota" installer/lib/20-dependencies.sh` still returns only package-list lines, no
mount/fstab/quotaon step. `installer/lib/10-preflight.sh:1-239` (read in full) has eleven checks —
`check_root`, `check_os_supported`, `check_arch_supported`, `check_ram`, `check_disk`,
`check_backup_space`, `check_existing_install_state`, `check_ports_free`,
`check_no_conflicting_panel` — and its own idiom for a warning vs. a refusal is explicit in
`10-preflight.sh:58-66`: `fail()` sets `_PREFLIGHT_FAILED` and blocks the install
(`10-preflight.sh:47-52,234-237`); `warn()` prints and does nothing else
(`10-preflight.sh:62-66`), used today for exactly one thing, `check_backup_space`
(`10-preflight.sh:85-104`), because — in that check's own words — "a preflight that refuses an
install over a condition the operator can fix afterwards teaches operators to skip preflight."
There is no quota check of either kind yet.

**The typed-reason idiom to follow.** `agent/crates/ops/src/db/model/grant_repair_refusal.rs:1-81`
is a closed set of refusal reasons (`GrantRepairRefusal`, `#[non_exhaustive]` at line 17) with a
`Display` impl that renders operator-facing English per variant
(`grant_repair_refusal.rs:61-80`) — every variant means "the row was not touched," stated once at
the type's own doc comment (lines 7-15) rather than repeated per variant. `#[non_exhaustive]` is
the idiom's answer to "an explicit unspecified member": it is not a named `Unspecified` variant,
it is the attribute that forces every `match` in this crate to carry a wildcard arm, so a future
variant this plan's author did not anticipate cannot silently fall through unhandled — the same
protection a named catch-all would give, without inventing a state that means nothing. Section 2
below follows this exact shape.

**The polygon's declared blind spot.** `docker/polygon/setquota-stand-in.sh:1-21` states, in its
own comment, that a container's overlay filesystem "has no quota support to apply one to" and that
the stand-in "accepts and does nothing" — "quota behaviour is NOT exercised by the polygon."
Section 8 below is built on this fact, not around it.

**The analogous, already-shipped pattern this plan copies.** `SftpJailDrifted`
(`backend/src/Maran.Modules/Monitoring/Domain/Enums/AlertKind.cs:18-26`) is a continuous,
agent-observed drift check added after the fact for a similar reason (issue #28 item E, same
file's remarks, lines 5-11). Its shape, read in full and reused below:
- Agent op: `agent/crates/ops/src/monitor/get_sftp_jail_status.rs:1-50`, a pure read plus a pure
  classification (`agent/crates/ops/src/monitor/model/sftp_jail_status.rs:1-181`,
  `SftpJailStatus::{Intact, Drifted { missing }}`), with its own "what this cannot see" section
  (lines 76-88).
- Wire: `proto/agent/v1/monitor.proto:1-30` (service doc), `GetSftpJailStatus` rpc added beside
  the read-only `MonitorService`.
- Evaluator: `backend/src/Maran.Modules/Monitoring/Services/AlertEvaluator.cs:1-230` —
  `EvaluateAsync` takes `sftpJailStatus: AgentSftpJailStatus?` (null = "the call did not succeed,"
  doc comment lines 121-125), calls `ObserveAsync` (lines 233-249) which upserts one
  `AlertState` row keyed by `(Kind, Subject)` and returns an `AlertTransition`
  (`AlertState.Observe`, `backend/src/Maran.Modules/Monitoring/Domain/Entities/AlertState.cs:113-138`
  — a saturating consecutive-breach counter, `BreachesBeforeAlert` before it fires, reset to 0 on
  any healthy reading), then `AnnounceAsync` (lines 251-273) journals unconditionally and publishes
  `SendMailRequested` only when there is a recipient.
- Resx: four keys per alert kind, `Alert<Kind><Transition><Subject|Body>`
  (`backend/src/Maran.Modules/Monitoring/Resources/NotificationMessages.resx:39-51`), present in
  `.resx`, `.ru.resx`, and `.hy.resx` (confirmed: `grep -c AlertSftpJailDrifted
  backend/src/Maran.Modules/Monitoring/Resources/NotificationMessages*.resx` — three files, four
  keys each).

## 2. The three-state outcome

**Problem restated precisely.** Today one `Option<QuotaBlocks>` (`quota_blocks.rs:33`) answers
two unrelated questions with one bit: "did `quota -u -w` print a hard-limit line for this user"
collapses "no limit is configured" and "this filesystem cannot hold a limit at all" into the same
`None`. A third, currently-invisible case makes it worse: `quota -u -w` itself can fail to run
(`account_operations.rs:640`, `self.host.run(...)`), and today that failure is not even reached by
`usage()` because `?` on `self.host.run(...)` (line 640) already returns `Err` before
`parse_hard_limit` runs — so "the tool could not be asked" is already distinguished from "the tool
answered no limit" at the `Result` level, and it is only the SUCCESSFUL answer that conflates two
meanings. That is the bug to fix — not the `Result`, the `Option` inside it.

**The new type**, `agent/crates/ops/src/accounts/model/quota_state.rs` (one type, one file, per
rules/architecture.md "One file = exactly one public unit"):

```rust
//! What the panel can honestly say about one account's disk quota.

/// The reason a filesystem cannot hold an enforceable disk quota, as far as
/// this agent can tell from where it is standing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum QuotaUnenforceableReason {
    /// The filesystem holding the account's home is not mounted with a quota
    /// accounting option (`usrquota`/`uquota`/`usrjquota`), so no per-user
    /// limit can exist on it no matter what `setquota` is asked to do.
    MountedWithoutQuotaAccounting,

    /// The filesystem is mounted with a quota accounting option, but
    /// `quotaon -p` reports user quota accounting is OFF for it — mounted
    /// correctly but never turned on (or turned off since).
    AccountingNotEnabled,
}

/// What this agent found about one account's disk quota, resolved to exactly
/// one of three states — never a bare `Option`, because an `Option` here has
/// carried two different facts about the host as the same `None` since this
/// type was `QuotaBlocks::parse_hard_limit`'s return value, and a caller
/// reading `None` could not tell which one it was looking at.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum QuotaState {
    /// The filesystem can enforce a quota, and this account has one.
    Enforced(super::quota_blocks::QuotaBlocks),

    /// The filesystem can enforce a quota, and no limit is configured for
    /// this account — `setquota`'s own "zero means unlimited"
    /// (`account_operations.rs:616`), reached honestly rather than by
    /// default.
    EnforceableButUnset,

    /// The filesystem cannot enforce anything, for the stated reason. Any
    /// figure the panel stores for this account's plan is fiction until this
    /// changes.
    NotEnforceable(QuotaUnenforceableReason),
}
```

`#[non_exhaustive]` on both enums, matching `GrantRepairRefusal`'s idiom exactly (Section 1): a
reason this plan's author did not foresee — a third quota mechanism, a filesystem type quota tools
do not recognize — must be added as a new variant and every `match` in the crate fails to compile
until handled, rather than silently falling into a `_ =>` this plan wrote before the case existed.

**What changes downstream.** `AccountOperations::usage` (`account_operations.rs:627-648`) is
rewritten to determine enforceability FIRST (Section 3), and only when enforceable, parse
`quota -u -w` and fold its `Option<QuotaBlocks>` to `Enforced`/`EnforceableButUnset` rather than to
a byte count. `AccountUsage`'s `quota_bytes: u64` field becomes `quota: QuotaState`, and every
caller of it (`account_operations_tests.rs`, the gRPC service layer, `AccountUsageDto.cs`,
`accounts.proto`'s `AccountUsage` message) is a task below rather than a detail here — each is a
type the compiler will refuse to build until touched, which is the whole point of replacing an
`Option` with a closed enum.

## 3. Where the truth actually lives — the decisive observation

Three places could answer "can this filesystem enforce a quota," and they answer three different
questions (Linux administration knowledge, not a repository fact — none of this is cited to this
codebase because none of it exists in this codebase yet, confirmed by Section 1's grep):

1. **`/proc/mounts`'s option list** (`usrquota`/`grpquota` for ext-family, `uquota`/`usrjquota` for
   XFS). Answers: "was this filesystem MOUNTED asking for quota accounting." It is necessary — a
   filesystem mounted without the option cannot enforce a quota under any circumstances — but not
   sufficient: ext4 requires a separate `quotacheck` + `quotaon` after the mount before the kernel
   actually tracks anything, so a filesystem can show `usrquota` in its mount options for months
   after an operator ran `quotaon` once, then ran `quotaoff` by hand, or after `quotacheck` was
   interrupted and never re-run — the option stays in `/proc/mounts` either way, because it
   describes what the MOUNT requested, not what the kernel is doing right now.

2. **`quotaon -p <mountpoint>`** (`quota-tools`' own query mode). Answers: "is the kernel tracking
   and enforcing user/group quota accounting on this filesystem RIGHT NOW." This is the live,
   authoritative answer to the actual question the panel needs — it is what `setquota` itself
   consults before it will do anything, so it is one step closer to "will `setquota` actually
   bind" than a static mount option is. XFS quotas are commonly turned on at mount time via the
   same option that appears in `/proc/mounts`, so for XFS this observation and observation 1 mostly
   agree; for ext4, where `quotaon` is a separate, forgettable step, they can and do disagree, and
   this observation is the one that is right when they do.

3. **What `setquota` itself returns.** Answers only: "did THIS specific invocation, for THIS user,
   on THIS filesystem, succeed just now." It is the narrowest and latest of the three, and it is
   already surfaced today as `AccountError::CommandFailed` (`account_operations.rs:656-670`) — it
   is not silent, it is just not distinguished from every OTHER reason `setquota` can fail (a bad
   argument, a permissions problem, the binary missing). Using it as the enforceability signal
   would mean re-deriving enforceability from a command's stderr text, which is exactly the
   locale-fragile, tool-version-fragile parsing this repository's own `LOCKED_PASSWORD_PREFIX`
   comment (`account_operations.rs:34-49`) shows the cost of getting wrong once already.

**Decision: observation 2, `quotaon -p`, is decisive.** It is the only one of the three that asks
the kernel's own live state rather than a mount-time request (1) or a single past attempt (3), and
it is checked BEFORE `setquota` is called, which is what lets Section 6 show an honest "cannot
enforce" screen instead of waiting for a customer's write to fail. Observation 1 is retained as a
SECONDARY signal only for the `MountedWithoutQuotaAccounting` reason (Section 2) — a `quotaon -p`
that reports off on a filesystem whose mount options never asked for quotas at all gets the more
specific, more actionable reason ("remount with the option") rather than the generic "turn it on."

**The case this decisive observation would still miss, named as required.** A network filesystem
mounted for `/home` — NFS, most concretely — can report a LOCAL `quotaon -p` state that is entirely
disconnected from enforcement, because NFS quota accounting (where it exists at all, via
`rpc.rquotad`) is enforced on the SERVER the export lives on, not on the client this agent runs on.
`quotaon -p` on an NFS client mount typically has nothing meaningful to say, or reports a state
that describes the client's cache rather than the server's enforcement. This agent has no rpc path
to a remote NFS server's quota daemon and the product's supported layout does not use NFS for
`/home` in v1 (`rules/architecture.md`'s system-boundaries section names three processes and no
network filesystem), so this is named as a known gap rather than solved: `QuotaUnenforceableReason`
is `#[non_exhaustive]` precisely so a future NFS-aware variant can be added without a breaking
change, and the plan does not pretend observation 2 covers a case v1 does not support.

## 4. Installer preflight

**Decision: warn, not fail — argued, not assumed.** Following the installer's own recorded reason
for its one existing warning (`10-preflight.sh:58-66`, quoted in Section 1): refusing to install
Maran because `/home`'s filesystem lacks `usrquota` would be wrong for an operator who has no plans
to sell disk-limited plans at all — Section 1's own reading of `AccountSnapshot.DiskQuotaMb`
(`AccountSnapshot.cs:52-53`) shows a plan's allowance is a number the OPERATOR configures; nothing
in `rules/architecture.md` or the spec makes a disk quota mandatory for every deployment. A `fail`
here would make Maran uninstallable on a great many otherwise-perfectly-good hosts (any managed
VPS image that ships ext4 without `usrquota` by default, which is most of them) over a feature the
operator may never turn on. This mirrors `check_backup_space` exactly: a real, disk-and-plan-dependent
fact the operator should see and can act on later, not a gate on the install itself.

**New check, `installer/lib/10-preflight.sh`, added to `step_preflight`'s list
(`10-preflight.sh:220-233`) after `check_disk`:**

```sh
# check_quota_capable: WARNS (never fails) when the filesystem holding /home lacks a
# quota-accounting mount option. See rules/architecture.md and this plan's own Section 3/4 for
# why this is a warning and not a refusal: disk quotas are an OPTIONAL, plan-driven feature, and
# an operator who never sells disk-limited plans should not be blocked from installing Maran at
# all over a filesystem property that never matters to them.
#
# What this reads, and what it cannot see: /proc/mounts' OPTIONS field for the filesystem holding
# /home, at install time only. It is a MOUNT-TIME signal (Section 3, observation 1) — it does not
# run quotaon -p, because at this point in the install no account, and therefore no quota, has
# ever been requested; the continuous check that matters once accounts exist is Section 5's
# Monitoring alert, not this one-time gate.
check_quota_capable() {
  local target_fs opts
  target_fs="$(findmnt -no SOURCE /home 2>/dev/null || findmnt -no SOURCE / )"
  opts="$(findmnt -no OPTIONS /home 2>/dev/null || findmnt -no OPTIONS / )"
  case "$opts" in
    *usrquota*|*uquota*|*usrjquota*)
      ok "the filesystem holding /home (${target_fs}) is mounted with quota accounting"
      ;;
    *)
      warn "the filesystem holding /home (${target_fs}) is not mounted with a quota accounting option (usrquota/uquota)" \
        "Disk quotas will not be enforceable on any account created before this is fixed. If you sell disk-limited plans, remount with the option (e.g. 'mount -o remount,usrquota <fs>' or an /etc/fstab edit plus reboot) and run 'quotacheck -cum <mountpoint> && quotaon <mountpoint>' before creating accounts. If you never plan to enforce disk limits, this can be ignored — the panel will show every account's quota as unenforceable rather than silently pretending one is in force."
      ;;
  esac
}
```

This mirrors `check_backup_space`'s shape (measure something real, `warn` with the exact number/path
measured, never touch `_PREFLIGHT_FAILED`) rather than inventing a new idiom.

## 5. Surfacing continuously — the Monitoring module

A remount changes enforceability without touching any account, so a one-time installer check
(Section 4) is necessary but not sufficient — the same argument this plan already made about
`SftpJailDrifted` existing at all (Section 1: "nothing re-checked it afterwards").

**New alert kind**, added to `AlertKind` (`backend/src/Maran.Modules/Monitoring/Domain/Enums/AlertKind.cs`),
following its own documented rule that "the kind is half of a row's identity... adding a value here
is adding a new row per subject, never a new meaning for an existing one" (lines 8-11):

```csharp
/// <summary>
/// The filesystem holding hosting accounts' home directories cannot enforce a per-user disk
/// quota — mounted without quota accounting, or mounted correctly but quota accounting is off.
/// </summary>
QuotaNotEnforceable = 4,
```

**Subject**: one row, the filesystem path — mirroring `RootFilesystemSubject`/`SftpJailSubject`
(`AlertEvaluator.cs:47-59`), a new `public const string AccountHomeFilesystemSubject = "/home";`
(matching `AgentPaths::ACCOUNT_HOME_ROOT`, `agent/crates/agent-core/src/agent_paths.rs:41`, cited
by name rather than duplicated as a literal, exactly the reasoning `home_directory`
(`account_operations.rs:135-142`) already uses for the same constant).

**Agent side, following the `SftpJailStatus`/`get_sftp_jail_status` shape exactly**
(Section 1's citations): a new pure model `agent/crates/ops/src/monitor/model/quota_enforceability.rs`
re-exporting `QuotaUnenforceableReason` from Section 2 (one canonical type, not a second copy —
`ops::accounts` and `ops::monitor` both depend on `ops`'s own crate root, so the type is defined
once in whichever of the two is lower in the dependency direction; `ops::accounts` already is the
one `usage()` lives in, so the type stays there and `ops::monitor` imports it) plus a new op
`get_quota_enforceability.rs` that reads `/proc/mounts` (new `MonitorHost::read_mounts`, a verbatim
read, same shape as `read_sshd_config`) and runs `quotaon -p <mountpoint>` (new
`MonitorHost::run`-style seam, or reuse of an existing one — a task decides which, since
`MonitorHost` today has no `run`; `SystemHost::run` in `ops::accounts` already exists and doing the
observation there and exposing it to `ops::monitor` avoids adding a second process-spawning seam
for one command). New distro adapter method `quotaon_binary()` beside `quota_binary()`/
`setquota_binary()` (`agent/crates/distro/src/adapter.rs:372-383`), implemented on both families
(`quota-tools`' `quotaon` ships at the same path pattern as `quota`/`setquota` per the staging
plan's citation of `20-dependencies.sh`).

**Proto**: a new rpc on `MonitorService` (`proto/agent/v1/monitor.proto:1-30`),
`GetQuotaEnforceability`, following `GetSftpJailStatus`'s exact request/response shape (empty
request, `oneof result { Ok ok; AgentError error; }`), `Ok` carrying an enum mirroring
`QuotaState`'s two enforceability variants (`ENFORCEABLE`, `NOT_ENFORCEABLE` with a reason enum
mirroring `QuotaUnenforceableReason`) — additive per rules/architecture.md's proto evolution rule,
never touching `AccountUsage` or `AccountDiskUsage`'s existing fields.

**`AlertEvaluator.EvaluateAsync`** (`AlertEvaluator.cs:118-181`) gains a fourth parameter,
`QuotaEnforceabilityStatus? quotaStatus`, null meaning exactly what it means for
`sftpJailStatus` (the call did not succeed — advances nothing, resets nothing,
`AlertEvaluator.cs:121-125`'s own doc comment restated for the new parameter), and a fourth
`ObserveAsync`/`pending.Add` block copying the `SftpJailDrifted` block (`AlertEvaluator.cs:166-181`)
verbatim in shape. Four new resx keys, `AlertQuotaNotEnforceable{Raised,Resolved}{Subject,Body}`,
added to all three locale files (`.resx`, `.ru.resx`, `.hy.resx`) — English wording drafted per
Section 6, Russian and Armenian left for the owner's review per this task's own instruction not to
invent final wording in a language nobody here is asked to author natively.

## 6. What the panel shows instead of a number it cannot back up

**Decision**: `AccountDiskUsageDto.QuotaBytes` (`AccountDiskUsageDto.cs:27-31`) stops being a bare
`long` and becomes a small DTO carrying both the figure and whether it means anything —
`AccountQuotaDto(long QuotaBytes, bool Enforceable)` — because the issue's own commercial framing
(a paid limit the filesystem cannot back up) is exactly a per-account, per-listing-row fact, not a
host-wide banner: an operator with two accounts, one on an enforceable disk and one recovering from
a remount, needs the distinction on the SAME screen. `ListAccountDiskUsageQueryHandler.ToRow`
(`ListAccountDiskUsageQueryHandler.cs:126-138`) reads the new `QuotaEnforceabilityStatus` (cached
per listing call from the same agent round-trip Section 5 already makes for the Monitoring
sampler — a task decides whether `ListAccountDiskUsage` makes its own agent call or reads the
`AlertState` row Section 5 already wrote, and the latter is cheaper and already fresh to at most one
sampler interval) and sets `Enforceable: false` for every row when the shared `/home` filesystem is
`NotEnforceable`, `true` otherwise.

**Operator-facing text — meaning only, wording is the owner's to approve, in
`backend/src/Maran.Modules/Accounts/Resources/DisplayNames.resx` (+`.ru.resx`, `.hy.resx`)**,
because the backend owns user-facing text (rules/architecture.md, "The backend owns the data, the
SPA renders it") and this module already owns account-facing display strings
(`git status` shows `DisplayNames.resx` already touched on this branch by concurrent work — this
plan adds keys, it does not reopen files mid-edit by another lane):

- `en`: a short label replacing the bare figure — meaning: "this account's plan allows N, but the
  server cannot currently enforce any disk limit; the account may use more." Not "unlimited" (which
  reads as a feature) and not the bare number with no caveat (today's defect) — the meaning must
  say the limit exists on paper and is not backed by the filesystem right now.
- `ru`/`hy`: same meaning, left for the owner to phrase naturally — not translated mechanically
  from the English draft above.

## 7. Why quotas are never enabled automatically

**Argued, not assumed, per this task's own instruction.** `mount -o remount,usrquota` and
`quotaon` are filesystem-state changes an operator can be running other workloads against; a root
daemon deciding on its own to remount `/home` — the filesystem holding every hosting account's live
files — the moment it notices the option is missing is strictly worse than the honest refusal this
plan proposes, for a reason this codebase already states about a different remount-adjacent risk:
`rules/architecture.md`'s config-write section requires render→swap→validate specifically because
an unattended write to live infrastructure needs a human-inspectable rollback path, and a kernel
mount-option change has none inside this agent — there is no "un-remount" the agent could perform
if `quotacheck` then found the filesystem in a state the operator did not expect. `Maran.Agent`'s
own stated invariant (rules/architecture.md, "Agent") is that every command is a closed, typed,
idempotent operation the C# side explicitly drives — "mutate `/etc/fstab` and remount `/home`" is
not a hosting-account operation at all, it is a change to the HOST's own storage configuration that
outlives every account on it, and the spec's three-process boundary was never asked to cover that
class of change. The check stays observe-only end to end: Section 4 warns, Section 5 alerts,
Section 6 tells the truth on screen, and the fix stays a documented manual command in the warning
and alert text themselves (Section 4/5's message bodies), exactly where the staging-install plan
already told an operator to run it by hand (`2026-09-19-maran-staging-install.md:341-344`).

## Tasks

### Task 1 — `QuotaState`/`QuotaUnenforceableReason` (agent, `ops::accounts`)
New file `agent/crates/ops/src/accounts/model/quota_state.rs` per Section 2. `QuotaBlocks::to_bytes`
(`quota_blocks.rs:54-58`) stays; `parse_hard_limit`'s signature is unchanged (it still answers "did
this SPECIFIC quota line exist," which is a real, narrower question `QuotaState` composes from) —
only its CALLER changes.

### Task 2 — `MonitorHost` seam for `/proc/mounts` and `quotaon -p`
Extend `agent/crates/ops/src/monitor/monitor_host.rs`'s trait with a mounts read and a `run`
capability (or thread the existing `ops::accounts::SystemHost::run` through, per Section 5's
open question — resolve it by reading whichever crate boundary `ops::monitor` already crosses for
`MonitorHost::read_sshd_config`'s file access, and match that shape rather than inventing a second
one). New `quotaon_binary()` on `DistroAdapter` (`agent/crates/distro/src/adapter.rs`), implemented
identically on both families per the staging plan's confirmation that `quota-tools` ships the same
binary set on both.

### Task 3 — `get_quota_enforceability` op
`agent/crates/ops/src/monitor/get_quota_enforceability.rs`, doc-commented with the same "what it
checks, how, what it cannot see" structure as `get_sftp_jail_status.rs:10-42`, citing Section 3's
NFS gap verbatim rather than re-deriving it.

### Task 4 — Rewire `AccountOperations::usage`
`account_operations.rs:627-648` calls the enforceability check first, folds to `QuotaState`.
`AccountUsage`'s `quota_bytes: u64` field becomes `quota: QuotaState`
(`agent/crates/ops/src/accounts/mod.rs`, wherever `AccountUsage` is actually declared — confirmed
by this plan's own grep to be re-located by whoever runs it, since the declaring file was not
opened in this reading pass; the task must open it before editing, per this plan's own honesty
rule). Every other `AccountOperations` method is untouched.

### Task 5 — Proto: `AccountUsage` and `GetQuotaEnforceability`
`proto/agent/v1/accounts.proto:220-232`: `AccountUsage`'s `quota_bytes` field is additive-only
(rules/architecture.md forbids renumbering) — add a new `QuotaState quota_state = 3;` message
field carrying the three-state enum, leave `quota_bytes` as a deprecated mirror of the byte figure
for `Enforced`/`EnforceableButUnset` and 0 for `NotEnforceable`, so existing wire consumers do not
break before they are migrated. `proto/agent/v1/monitor.proto`: new `GetQuotaEnforceability` rpc
per Section 5.

### Task 6 — C# clients: `AccountUsageDto`, `IAgentAccountsClient`, `IAgentMonitorClient`
`AccountUsageDto.cs:5` doc comment is rewritten (it is exactly the kind of doc comment
rules/architecture.md flags — stating one meaning for a field that now carries three); add the new
enforceability field. New `IAgentMonitorClient.GetQuotaEnforceabilityAsync`, mirroring
`GetSftpJailStatusAsync`'s existing shape (not opened in this pass — a task must read it before
copying it, same rule as Task 4).

### Task 7 — `installer/lib/10-preflight.sh`: `check_quota_capable`
Exactly the function in Section 4, added to `step_preflight`'s call list
(`10-preflight.sh:220-233`) after `check_disk`.

### Task 8 — `AlertKind.QuotaNotEnforceable` + `AlertEvaluator` fourth block
Per Section 5. Touches `AlertKind.cs`, `AlertEvaluator.cs` (new parameter, new `pending` block,
mirroring the `SftpJailDrifted` block at `AlertEvaluator.cs:166-181` in shape), and the caller that
builds `EvaluateAsync`'s arguments from the agent's readings (not opened in this pass — locate via
`grep -rn "EvaluateAsync(" backend/src/Maran.Modules/Monitoring`, since the sampler that calls it
was not read and this plan does not guess its shape).

### Task 9 — Resx: four `AlertQuotaNotEnforceable*` keys, three locales
Per Section 5, mirrored on `AlertSftpJailDrifted*`'s four keys (`NotificationMessages.resx:39-51`
and its two locale twins).

### Task 10 — `AccountDiskUsageDto`/`ListAccountDiskUsageQueryHandler`: the enforceability field
Per Section 6. `AccountDiskUsageDto.cs:27-31`'s doc comment is rewritten alongside the field it
describes — the same rule Task 6 applies to `AccountUsageDto`.

### Task 11 — Resx: account-screen operator text, meaning only
Per Section 6, `Maran.Modules.Accounts`' `DisplayNames.resx` + `.ru.resx` + `.hy.resx`, English
meaning drafted, `ru`/`hy` left for the owner.

### Task 12 — Frontend: render the enforceability flag
Wherever the disk-usage listing is rendered in `frontend/` (not opened in this pass — locate by
searching for the component consuming `ListAccountDiskUsage`'s response shape). Per
rules/architecture.md, "The backend owns the data, the SPA renders it": the SPA renders the
backend's already-localized flag, invents no wording of its own.

## Proof, per task — the mutant and its inverse control

Per rules/testing.md: a check that cannot observe what it reports on is not a check. Every task
below states the mutant that must be caught, the test that catches it, and the REQUIRED inverse
control.

- **Task 1 (`QuotaState`)**: mutant — swap `Enforced`/`EnforceableButUnset` in the fold logic (an
  enforceable-but-unset account reported as if it held a limit of zero bytes, which downstream
  code could read as "quota is zero bytes," the opposite of unlimited). Test: unit test over the
  fold function with a fabricated `quota -u -w` output holding no hard-limit line, asserting
  `EnforceableButUnset` specifically, not merely "not `Enforced`." **Provable on this machine**:
  pure function over a string, no host needed.

- **Tasks 2/3 (mount + `quotaon -p` observation)**: mutant — treat `AccountingNotEnabled` as
  `MountedWithoutQuotaAccounting` or vice versa (the wrong fix instruction reaches the operator).
  Test: unit tests over fabricated `/proc/mounts` text and fabricated `quotaon -p` output, one per
  reason. **Positive control (quotas enforceable) — NOT provable on this machine or the polygon**:
  `docker/polygon/setquota-stand-in.sh:12-16` states the overlay filesystem has no quota support at
  all; this needs the real VPS from issue #28 section B, mounted with `usrquota` and `quotaon` run,
  observed exactly as `2026-09-19-maran-staging-install.md`'s section 3.2 describes. **Negative
  control (not enforceable) — provable everywhere, including the polygon and this machine**: the
  polygon's own overlay filesystem already IS the negative case; a unit test over a real
  `/proc/mounts` read on any dev machine without `usrquota` set is a second, cheaper negative
  control.

- **Task 4 (`AccountOperations::usage` rewire)**: mutant — call `apply_quota`'s parse path before
  checking enforceability (regressing to today's collapse). Test: fake `SystemHost`/`MonitorHost`
  reporting `NotEnforceable`, asserting `usage()` never reaches `parse_hard_limit` and returns
  `NotEnforceable` regardless of what the fake `quota -u -w` output says. **Provable on this
  machine**: fakes only, the exact shape `AccountOperations`' existing tests already use
  (`agent/crates/ops/src/tests/accounts/account_operations_tests.rs`, read in Section 1's grep for
  `setquota`).

- **Task 7 (`check_quota_capable`)**: mutant — invert the `case` match so a filesystem WITH
  `usrquota` warns and one without does not. Test: run the function against two fabricated
  `findmnt` outputs (shell function, testable by stubbing `findmnt` on `PATH` in a test harness, or
  by refactoring the option-string match into a pure function the installer's own test style
  already favors — not opened in this pass; a task must find whether `installer/lib` has any
  existing shell-level test harness before choosing). **Provable on this machine**: string match
  only. **Full positive/negative control still needs the real VPS**: confirming the check's OWN
  `ok`/`warn` line prints correctly against `installer/install.sh`'s actual `findmnt /home` on a
  real disk layout is the staging-install plan's job, not this plan's unit test's.

- **Task 8 (`AlertEvaluator` fourth block)**: mutant — swap the `Raised`/`Resolved` transition
  (mail says "restored" when it just broke). Test: mirror
  `AlertEvaluatorTests`'s existing `SftpJailDrifted` assertions
  (`backend/tests/Maran.Modules.Monitoring.Tests/Services/AlertEvaluatorTests.cs:201,230`, cited
  from Section 1's grep) with the new kind substituted, asserting the exact
  `Subject == $"QuotaNotEnforceable:{AlertEvaluator.AccountHomeFilesystemSubject}"` string and the
  transition direction. **Provable on this machine**: `MonitoringDbContext` over
  Testcontainers-PostgreSQL, the module's existing integration-test pattern
  (rules/testing.md, "Where tests live").

- **Task 10 (`AccountDiskUsageDto` enforceability field)**: mutant — hardcode `Enforceable = true`
  (silently reverting to today's defect, the exact bug this whole plan exists to fix). Test:
  `ListAccountDiskUsageQueryHandlerTests` (cited in `git status` as already under concurrent edit
  on this branch — this task's test file addition must be coordinated with that lane, not written
  blind against a file mid-change) asserting a fake `IAgentMonitorClient` reporting
  `NotEnforceable` produces `Enforceable: false` on every row regardless of each account's
  `DiskQuotaMb`. **Provable on this machine**: fakes only, no real filesystem needed — this is the
  one part of the whole feature that is fully provable without a VPS, because it is a pure
  data-join test, same shape `ListAccountDiskUsageQueryHandlerTests` presumably already uses for
  the existing "agent measured nothing → null, not zero" case (`ListAccountDiskUsageQueryHandler.cs:128-135`'s
  own remarks describe that exact prior test).

## Summary — what is provable here, and what needs the real VPS

**Provable on this machine (unit/integration, fakes and Testcontainers only):** Tasks 1, 4, 8, 10
in full; Tasks 2/3's classification logic and Task 7's string-matching logic (the NEGATIVE
enforceability case, since the polygon's own overlay filesystem already demonstrates it, and any
dev machine without `usrquota` set demonstrates it a second way).

**NOT provable here or in the polygon — needs the real VPS from issue #28 section B:** the POSITIVE
case throughout — a filesystem actually mounted with `usrquota`, `quotacheck`+`quotaon` actually
run, `setquota` actually binding, and a customer's write actually refused past the limit. This is
stated by the polygon's own stand-in (`docker/polygon/setquota-stand-in.sh:12-16`) and by this
plan's Section 8; the staging-install plan's section 3.2
(`2026-09-19-maran-staging-install.md:309-361`) already contains the exact commands for that
observation and is the document to extend, not duplicate, when that VPS run happens.

## What makes issue #29 harder than it states

- The issue frames the defect as living in `quota_blocks.rs`'s `Option`. Reading further shows the
  SAME collapse is repeated independently at four more layers that do not share code with it —
  the proto message, the C# DTO's doc comment, and, worst of all, the Monitoring listing that
  actually reaches a screen NEVER EVEN CALLS into `quota_blocks.rs` at all: it computes its
  `QuotaBytes` purely from the plan's stored megabytes (`ListAccountDiskUsageQueryHandler.cs:149-152`)
  and ignores the agent's `AccountUsage`/`quota_bytes` entirely (confirmed: the agent's own copy is
  hardcoded to 0 on the wire and explicitly not projected, `monitor.proto:178-185`,
  `AccountDiskUsageDto.cs:8-9`). Fixing `quota_blocks.rs` alone would leave the actual customer- and
  operator-facing screen exactly as wrong as it is today. This plan's Task 10 is therefore not
  optional polish on top of Task 1 — it is the fix for the half of the bug the issue's own title
  does not point at.
- There is no single "the filesystem's quota state" reading anywhere in the tree today — this plan
  adds the very first one, which means Tasks 2/3 are new capability, not a refactor, and the "read
  first" discipline this plan followed found nothing to build on for the mount/quotaon check
  specifically (confirmed by Section 1's repeated grep returning zero hits before this plan).
