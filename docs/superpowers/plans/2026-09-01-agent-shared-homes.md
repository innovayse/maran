# Agent Shared Homes (deduplication) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the ~600 duplicated lines in the agent by giving each copied block ONE named home, and fix the two correctness defects the duplication audit uncovered (accounts RPCs blocking the tokio worker; spawn failures mislabeled as reload failures).

**Architecture:** No new crates and no "global utils" layer. Three precisely-named homes — `agent/src/services/wire/` for the proto↔domain boundary shared by every service, `agent-core::utils::spawn_argv` for the one process-spawn body (the cargo `ProcessBuilder` move, scaled to our size), and `validation::prefixed_name` + `decode()` methods on the prefixed-name types. Two test fakes share one composed `RecordingCommands` struct. Everything else (per-area error enums, per-area `*_status.rs` mappers, per-area host traits, db's bounded-read spawn, ssl's delegating host, ssl/monitor's answer-matching fakes) stays exactly as it is — either it matches community best practice (Sabrina Jewson "Modular Errors", nrc error-docs) or it is deliberately different, not duplicated.

**Tech Stack:** Rust (edition 2024), tokio `spawn_blocking`, tonic; no new dependencies.

**Spec:** This plan argues from the audit in "Findings" below. Every finding was re-verified by an independent adversarial audit on 2026-09-01 (10 assumption checks against the working tree, file:line evidence); the corrections it produced are folded into the tasks. Rules ground truth: `rules/rust.md`, `rules/architecture.md`.

## Global Constraints

- NEVER `git commit` or push — the owner commits personally (CLAUDE.md). Every "verify" step replaces the usual commit step.
- No `unwrap`/`expect`/`panic!` outside tests (workspace lints deny them).
- Doc comments on EVERY item, private included; fallible `pub` fns carry `# Errors` naming conditions (rules/rust.md). Moved files get their docs re-read: a doc that names its old, narrower context is a defect (rules/testing.md "documentation must not describe code that does not exist").
- One file = one unit; `mod.rs` declares and re-exports, never defines.
- No shell-string execution; processes are argv arrays only.
- Every task ends green: `source scripts/dev && maran agent check && maran structure`.
- rules/rust.md map and agent/CLAUDE.md tables are updated in the same change that moves a file (rules/architecture.md). Note: `maran structure` does NOT check the map — keeping it honest is review-enforced, so Task 7 is mandatory, not cosmetic.
- All repo text is English.

## Findings (audited evidence this plan is built on)

1. `async fn run<T, F>` exists in 9 services; bodies are identical except the error type and the message's noun phrase. The phrases are NOT all "<area> operation": ssl says "certificate operation", files says "file operation", monitor says "monitoring reading". The shared wrapper must take the noun phrase, not an area word, to keep every message byte-identical.
2. `accounts_service.rs` has a **synchronous** `fn run<T>`: `useradd`/`setquota`/`quota` are awaited on the async worker — violates rules/rust.md "Blocking work goes through spawn_blocking" and stalls all in-flight RPCs on that worker. The service is `AccountsServiceImpl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost>` with FOUR fields (`operations`, `php_host`, `db_host`, `sftp_host` — `delete_account` drives the cross-area cascade through the last three); `server.rs:111-116` constructs it with four arguments.
3. The `Command::new … .output() … status.code().unwrap_or(-1)` spawn body is copied in **6** hosts: accounts, firewall, monitor, php, sftp (its `ConfigHost` impl), sites. In php/sftp/sites a spawn failure is wrapped as `SafeWriteError::ReloadFailed`, so "binary not found" reads as "reload failed" in operator logs. NOT copies, and NOT to be touched: `process_db_host.rs` (`.spawn()` + bounded 1 MiB read with kill-on-overflow — a deliberate memory ceiling), `process_ssl_host.rs` (`ConfigHost::run` delegates to the sites host; its own `spawn_with_input` pipes stdin for openssl), and sftp's second `SftpHost::run` (stdin-piped `chpasswd`).
4. `validated_account` is a verbatim-in-code duplicate across `services/cron/` and `services/db/` (their DOC paragraphs differ and are area-specific); 9 files import one of them. `invalid_input` lives in `services/sites/` but is imported by exactly 23 files across all services, all via full `crate::services::…` paths.
5. The `for_account` constructors of `DbUserName`, `DatabaseName`, `SftpUserName` are executably identical (same checks, same order, same error shapes `Empty` / `UnexpectedCharacter { character }` / `TooLong { length }`), differing only in the length limit (32/64/32) and docs. Each file's `SEPARATOR` const is used only inside `for_account`.
6. The rsplit-decode of "does this prefixed name belong to this account" is copied 3×: `sftp/process_sftp_host.rs` (decodes `SftpUserName`), `db/list_databases.rs` (decodes `DatabaseName`), `db/drop_account_databases.rs` (decodes **`DbUserName`** — the users pass). All three need the decoded value, not a boolean.
7. Of the recording fakes, exactly TWO share the record-argv + configured-outcome core: `fake_php_host.rs` and accounts' `RecordingHost`. `fake_ssl_host.rs` (answers per-argv via `match`, records program-less argv, panics on unexpected) and `fake_monitor_host.rs` (per-unit stdout map, `run` can return `Err`) are structurally different ON PURPOSE and stay as they are.
8. External reference points: cargo centralizes spawning in one `ProcessBuilder` (crates/cargo-util); rust-analyzer keeps `stdx` deliberately tiny and promotes helpers only on a second caller; per-module error enums are the endorsed pattern — so the 10 `*_status.rs` mappers and 11 `*_error.rs` enums are NOT dedup targets. In-crate matches on `SafeWriteError` are protected by their `other =>` arms (the `#[non_exhaustive]` attribute does nothing in-crate), and no test pins the spawn-failure text — verified.

## File Structure (end state)

```
agent/crates/agent/src/services/
├── wire/                      NEW — the proto↔domain boundary, shared by every service
│   ├── mod.rs                 declarations only
│   ├── invalid_input.rs       MOVED from services/sites/ (content unchanged)
│   ├── system_failure.rs      NEW — the one SystemFailure AgentError constructor
│   ├── validated_account.rs   MOVED from services/cron/, docs generalized (db copy deleted)
│   └── run_blocking.rs        NEW — the one spawn_blocking wrapper
agent/crates/agent-core/src/
├── utils/spawn_argv.rs        NEW — the one Command spawn body
└── validation/
    ├── prefixed_name.rs       NEW — crate-internal shared constructor core (fn + SEPARATOR)
    └── prefix_problem.rs      NEW — its rejection enum, one unit per file as everywhere
agent/crates/ops/src/tests/support/
├── mod.rs                     NEW — declarations only
└── recording_commands.rs      NEW — shared recording core for the two compatible fakes
```

---

### Task 1: `services/wire/` — the shared boundary module

**Files:**
- Create: `agent/crates/agent/src/services/wire/mod.rs`
- Create: `agent/crates/agent/src/services/wire/system_failure.rs`
- Move: `agent/crates/agent/src/services/sites/invalid_input.rs` → `agent/crates/agent/src/services/wire/invalid_input.rs`
- Move: `agent/crates/agent/src/services/cron/validated_account.rs` → `agent/crates/agent/src/services/wire/validated_account.rs`
- Delete: `agent/crates/agent/src/services/db/validated_account.rs`
- Modify: `agent/crates/agent/src/services/mod.rs` (add `pub mod wire;`), `agent/crates/agent/src/services/sites/mod.rs`, `agent/crates/agent/src/services/cron/mod.rs`, `agent/crates/agent/src/services/db/mod.rs` (drop moved/deleted declarations), and every importer (23 files for `invalid_input`, 9 for `validated_account`).

**Interfaces:**
- Produces: `crate::services::wire::invalid_input::invalid_input(message: String) -> AgentError` (signature unchanged); `crate::services::wire::validated_account::validated_account(account_username: &str) -> Result<AccountName, AgentError>`; `crate::services::wire::system_failure::system_failure(message: String) -> AgentError`.
- Consumed by: every current importer; Task 2 consumes `system_failure`.

- [x] **Step 1: Create the module skeleton**

`wire/mod.rs` (NOTE: `run_blocking` is deliberately NOT declared here — its file arrives in Task 2, and this task must end green on its own):
```rust
//! The proto ↔ domain boundary, shared by every service.
//!
//! One home for the things every service repeats at its edge: turning a
//! rejected input into the wire error (`invalid_input`), revalidating the
//! account name an rpc carries (`validated_account`), and reporting an
//! agent-side breakdown (`system_failure`). Services import from here and
//! never from each other's folders.

pub mod invalid_input;
pub mod system_failure;
pub mod validated_account;
```

`wire/system_failure.rs`:
```rust
//! The one constructor for the wire's SystemFailure error.

use crate::proto::{AgentError, ErrorCode};

/// Wraps an agent-side breakdown — a panic in a blocking task, a subsystem
/// that did not answer — as the wire error the panel reports as a fault.
///
/// One constructor rather than a literal at each call site, so the code, the
/// empty `tool_output` and the shape cannot drift apart between services.
#[must_use]
pub fn system_failure(message: String) -> AgentError {
    AgentError {
        code: ErrorCode::SystemFailure as i32,
        message,
        tool_output: String::new(),
    }
}
```

- [x] **Step 2: Move the two existing files**

```bash
cd agent/crates/agent/src/services
mv sites/invalid_input.rs wire/invalid_input.rs
mv cron/validated_account.rs wire/validated_account.rs
rm db/validated_account.rs
```

- [x] **Step 3: Generalize `wire/validated_account.rs`'s docs**

The cron original's docs are crontab-specific and the deleted db copy's were decode-specific; the shared file must speak for every caller. Replace the module doc and the fn doc's first/why paragraphs with (keep the `# Errors` section as it is):
```rust
//! Revalidating the account name an rpc carries.

/// Revalidates the account name an rpc carries.
///
/// The API validated it already. This is the agent's own check, and it exists
/// because the agent runs as root and the API does not (rules/security.md item
/// 1, which requires revalidation in the agent and not only at the API
/// boundary). What the name goes on to decide differs per service — whose
/// crontab is edited, which home is written under, which prefix database rows
/// are decoded against — but every one of those is an argument handed to a
/// root process, which is why the gate is shared and unconditional.
```
Update its `invalid_input` import to `use crate::services::wire::invalid_input::invalid_input;`.

- [x] **Step 4: Rewrite the imports workspace-wide**

All importers use full `crate::services::…` paths (verified — no `super::` forms to miss):
```bash
grep -rl "services::sites::invalid_input" agent/crates/agent/src | \
  xargs sed -i 's|services::sites::invalid_input|services::wire::invalid_input|g'
grep -rl "services::cron::validated_account\|services::db::validated_account" agent/crates/agent/src | \
  xargs sed -i 's|services::cron::validated_account|services::wire::validated_account|g; s|services::db::validated_account|services::wire::validated_account|g'
```
Remove the `mod invalid_input;` line from `sites/mod.rs` and the `mod validated_account;` lines from `cron/mod.rs` and `db/mod.rs`; add `pub mod wire;` to `services/mod.rs`.

- [x] **Step 5: Verify**

Run: `source scripts/dev && maran agent check && maran structure`
Expected: exit 0 (all current tests green), STRUCTURE-OK.

---

### Task 2: `run_blocking` — one wrapper, and the accounts blocking fix

**Files:**
- Create: `agent/crates/agent/src/services/wire/run_blocking.rs`
- Modify: `agent/crates/agent/src/services/wire/mod.rs` (add `pub mod run_blocking;`)
- Modify: the 9 services `cron_service.rs`, `db_service.rs`, `files_service.rs`, `firewall_service.rs`, `monitor_service.rs`, `php_service.rs`, `sftp_service.rs`, `sites_service.rs`, `ssl_service.rs` — delete each private `async fn run<T, F>` and call the shared one.
- Modify: `agent/crates/agent/src/services/accounts/accounts_service.rs` — replace the synchronous `fn run<T>` with the shared async wrapper; the four fields become `Arc`s.
- NOT modified: `agent/crates/agent/src/server.rs` — `AccountsServiceImpl::new` keeps its four-argument signature and wraps internally.

**Interfaces:**
- Produces:
```rust
pub async fn run_blocking<T, E>(
    what: &'static str,
    map_error: impl FnOnce(&E) -> AgentError,
    operation: impl FnOnce() -> Result<T, E> + Send + 'static,
) -> Result<T, AgentError>
where
    T: Send + 'static,
    E: Send + 'static,
```
`what` is the message's noun phrase, chosen per service so every existing message stays byte-identical (see the table in Step 2).
- Consumes: `wire::system_failure::system_failure` from Task 1.

- [x] **Step 1: Write `run_blocking.rs`**

```rust
//! The one place a service hands a blocking operation to the runtime.

use crate::proto::AgentError;
use crate::services::wire::system_failure::system_failure;

/// Runs one blocking operation off the runtime's workers and maps its failure
/// onto the wire error.
///
/// Every operation in `ops` spawns processes and waits on them; rules/rust.md
/// requires that off the async workers, since a process wait on a worker
/// stalls every other in-flight command. This wrapper is written once so a
/// service cannot forget the `spawn_blocking` (the accounts service once did)
/// or map a panic differently from its neighbours.
///
/// `what` is the noun phrase of the failure message — "cron operation",
/// "certificate operation", "monitoring reading" — kept caller-chosen so the
/// messages operators already know did not change when the wrapper unified.
///
/// # Errors
///
/// Returns `map_error`'s mapping of whatever the operation failed on, or a
/// system failure when the blocking task did not finish — a panic inside the
/// agent has no domain answer to give, and rules/proto.md reserves gRPC
/// statuses for transport problems, which it is not.
pub async fn run_blocking<T, E>(
    what: &'static str,
    map_error: impl FnOnce(&E) -> AgentError,
    operation: impl FnOnce() -> Result<T, E> + Send + 'static,
) -> Result<T, AgentError>
where
    T: Send + 'static,
    E: Send + 'static,
{
    match tokio::task::spawn_blocking(operation).await {
        Ok(outcome) => outcome.map_err(|error| map_error(&error)),
        Err(error) => Err(system_failure(format!("the {what} did not finish: {error}"))),
    }
}
```
Add `pub mod run_blocking;` to `wire/mod.rs` (alphabetical position: after `invalid_input`).

- [x] **Step 2: Migrate the nine async services (mechanical, message-preserving)**

In each file: delete the private `async fn run<T, F> … }` block, add `use crate::services::wire::run_blocking::run_blocking;`, change every `Self::run(move || …)` call to `run_blocking(WHAT, |error| to_agent_error(error), move || …)`, then remove imports the deleted block was the only user of — typically `ErrorCode` and sometimes `AgentError` (the build's `-D warnings` + IDE0005-equivalent will name them; `maran agent lint` fails on any leftover).

`WHAT` per service — copied from each file's current message so the wire text does not change:

| service | `WHAT` |
|---|---|
| cron | `"cron operation"` |
| db | `"database operation"` |
| files | `"file operation"` |
| firewall | `"firewall operation"` |
| monitor | `"monitoring reading"` |
| php | `"PHP operation"` |
| sftp | `"sftp operation"` |
| sites | `"site operation"` |
| ssl | `"certificate operation"` |

FIREWALL INTERACTION (added 2026-09-04): the firewall area carries a STATIC
call-site assertion (introduced with the `firewall_lock.rs` async-guard fix —
no runtime check can tell the two call shapes apart on a multi-threaded
runtime, so a test walks the source and requires every `firewall::<op>(` to be
reached through `Self::run(move || …)`). Migrating `firewall_service.rs` to
`run_blocking` changes that shape, so the SAME step must update the
assertion's expected pattern to `run_blocking(` — find it under
`agent/crates/agent/src/tests/services/firewall/` (or beside
`ops/src/firewall/firewall_lock.rs`) and adjust it, or the gate fails on a
correct migration.

Worked example — `php_service.rs`, whose ONLY `Self::run` call site is in `list_php_versions` (its `install_php_version` rpc is a raw streaming `spawn_blocking` with a progress channel and is NOT migrated — leave it untouched):
```rust
let outcome = run_blocking("PHP operation", |error| to_agent_error(error), move || {
    php::list_php_versions(host.as_ref(), distro)
})
.await;
```

- [x] **Step 3: Fix the accounts service (four fields, not one)**

`AccountsServiceImpl` is generic over four hosts because `delete_account` drives the cross-area cascade. The rewrite wraps each field in `Arc` and keeps `new`'s signature, so `server.rs` does not change:
```rust
use std::sync::Arc;

use crate::services::wire::run_blocking::run_blocking;

pub struct AccountsServiceImpl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost> {
    /// The account operations, shared with the blocking tasks that run them.
    operations: Arc<AccountOperations<H>>,
    /// The php host `delete_account`'s cascade removes pools through.
    php_host: Arc<P>,
    /// The db host the cascade drops databases through.
    db_host: Arc<D>,
    /// The sftp host the cascade removes logins through.
    sftp_host: Arc<S>,
}

impl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost> AccountsServiceImpl<H, P, D, S> {
    /// Creates the service around the operations and the cascade's hosts.
    #[must_use]
    pub fn new(operations: AccountOperations<H>, php_host: P, db_host: D, sftp_host: S) -> Self {
        Self {
            operations: Arc::new(operations),
            php_host: Arc::new(php_host),
            db_host: Arc::new(db_host),
            sftp_host: Arc::new(sftp_host),
        }
    }
}
```
Delete the private sync `fn run<T>`. `H: SystemHost + 'static` (and `P/D/S: …Host + 'static`) suffice for `spawn_blocking`: `SystemHost`, `PhpHost` (via `ConfigHost`), `DbHost` and `SftpHost` all carry `Send + Sync` as supertraits — do NOT add redundant bounds.

Worked example, `create_account` (the simple shape — apply it to `suspend`, `unsuspend`, `set_quota`, `usage` with their own ops calls):
```rust
let request = request.into_inner();
let result = match Self::validated(&request.username) {
    Ok(name) => {
        let operations = Arc::clone(&self.operations);
        let quota_bytes = request.quota_bytes;
        match run_blocking("account operation", |error| to_agent_error(error), move || {
            operations.create(&name, quota_bytes)
        })
        .await
        {
            Ok(created) => create_account_response::Result::Ok(CreateAccountOk {
                home_directory: created.home_directory,
                uid: created.uid,
            }),
            Err(error) => create_account_response::Result::Error(error),
        }
    }
    Err(invalid) => create_account_response::Result::Error(invalid),
};
```
Worked example, `delete_account` (the cascade shape — all four Arcs move into the closure; keep the argument order `operations.delete` already uses today):
```rust
let operations = Arc::clone(&self.operations);
let php_host = Arc::clone(&self.php_host);
let db_host = Arc::clone(&self.db_host);
let sftp_host = Arc::clone(&self.sftp_host);
match run_blocking("account operation", |error| to_agent_error(error), move || {
    operations.delete(php_host.as_ref(), db_host.as_ref(), sftp_host.as_ref(), &name)
})
.await
```
(Check the current call at `accounts_service.rs` `delete_account` and preserve its exact parameter order and any extra arguments.)

- [x] **Step 4: Verify**

Run: `source scripts/dev && maran agent check && maran handshake`
Expected: exit 0; all tests green (no agent unit test constructs `AccountsServiceImpl` — only `server.rs` does, and its call did not change); handshake proves the boot path.

---

### Task 3: `spawn_argv` — one spawn body for the six copies, honest spawn errors

**Files:**
- Create: `agent/crates/agent-core/src/utils/spawn_argv.rs`
- Modify: `agent/crates/agent-core/src/utils/mod.rs` (declare it)
- Modify: exactly these six hosts — `ops/src/accounts/process_system_host.rs`, `ops/src/firewall/process_firewall_host.rs`, `ops/src/monitor/process_monitor_host.rs`, `ops/src/php/process_php_host.rs`, `ops/src/sftp/process_sftp_host.rs` (its `ConfigHost::run` ONLY), `ops/src/sites/process_site_host.rs`
- Modify: `ops/src/safe_write/safe_write_error.rs` (new variant)
- NOT modified (verified deliberate, do not touch): `ops/src/db/process_db_host.rs` (bounded 1 MiB read with kill-on-overflow — replacing it with `.output()` would delete a memory-safety ceiling), `ops/src/ssl/process_ssl_host.rs` (its `ConfigHost::run` delegates to the sites host; `spawn_with_input` pipes stdin), sftp's `SftpHost::run` (stdin-piped `chpasswd` — so sftp KEEPS its `use std::process::Command`).

**Interfaces:**
- Produces:
```rust
/// in maran_agent_core::utils::spawn_argv
pub fn spawn_argv(program: &str, arguments: &[&str]) -> std::io::Result<CommandOutcome>
```
- Consumes: `maran_agent_core::command_outcome::CommandOutcome`.

- [x] **Step 1: Write `spawn_argv.rs`**

```rust
//! The one body that turns an argv array into a finished [`CommandOutcome`].

use std::process::Command;

use crate::command_outcome::CommandOutcome;

/// Spawns `program` with `arguments` as an argv array and waits for it.
///
/// No shell is involved, at any point (rules/security.md item 3): the
/// arguments reach `execve` one by one, so there is no command line for
/// anything to re-parse. `program` must come from the `DistroAdapter`'s
/// allow-list and never from a request — that contract belongs to the caller
/// and is restated here because this is the function that spawns.
///
/// Callers whose child needs stdin (chpasswd, openssl) or whose output must
/// be read bounded (the database client) do NOT belong here — those spawns
/// are deliberately different and stay beside their owners.
///
/// # Errors
///
/// Returns the `io::Error` of failing to START the program — not found, not
/// executable, fork refused. A program that started and exited non-zero is
/// NOT an error here: its status is the caller's domain decision, so it comes
/// back as a [`CommandOutcome`] like any success.
pub fn spawn_argv(program: &str, arguments: &[&str]) -> std::io::Result<CommandOutcome> {
    let output = Command::new(program).args(arguments).output()?;

    Ok(CommandOutcome {
        // -1 for a process killed by a signal: it did not exit, and reporting
        // 0 would read as success to every caller.
        status: output.status.code().unwrap_or(-1),
        stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
        stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
    })
}
```

- [x] **Step 2: Add the honest variant to `SafeWriteError`**

In `safe_write_error.rs`:
```rust
    /// The program could not be started at all — missing binary, not
    /// executable. Distinct from a validator or reload that RAN and refused:
    /// an operator fixing "nginx -t failed" and one installing a missing
    /// package are doing different work, and the error name should say which.
    #[error("could not run {program}: {reason}")]
    SpawnFailed {
        /// The program that could not be started.
        program: String,
        /// The operating system's reason.
        reason: String,
    },
```
This compiles everywhere without further edits — verified: the only matches on `SafeWriteError` are the three `From` impls in `sites_op_error.rs` / `php_op_error.rs` / `ssl_op_error.rs`, and each ends with an `other => … { reason: other.to_string() }` arm that absorbs the new variant (it is those arms, not `#[non_exhaustive]`, that protect in-crate matches). No test asserts on the spawn-failure text — verified against the firewall fixtures (fake-fabricated), `render_validate_swap_tests.rs` (a reload that really ran), and the golden files.

- [x] **Step 3: Migrate the six hosts**

php, sites, and sftp's `ConfigHost` impl — the three whose spawn failure currently lies as `ReloadFailed` — become:
```rust
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        spawn_argv(program, arguments).map_err(|error| SafeWriteError::SpawnFailed {
            program: program.to_owned(),
            reason: error.to_string(),
        })
    }
```
accounts, firewall, monitor keep their existing mapping targets, only the body is replaced (worked example, accounts):
```rust
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, AccountError> {
        spawn_argv(program, arguments).map_err(|error| AccountError::CommandUnavailable {
            program: program.to_owned(),
            reason: error.to_string(),
        })
    }
```
(Match each file's actual variant field names — firewall's is `NftFailed`, monitor's is `MonitorError::program_unavailable()` which takes no fields; read the current `map_err` and keep its target exactly.) Remove `use std::process::Command;` where the file no longer spawns — that is accounts, firewall, monitor, php, sites, but NOT sftp.

- [x] **Step 4: Verify**

Run: `source scripts/dev && maran agent check`
Expected: exit 0; all tests green (the fakes implement the host traits and never touch these bodies).

---

### Task 4: `prefixed_name` — one constructor core for the three prefixed names

**Files:**
- Create: `agent/crates/agent-core/src/validation/prefixed_name.rs`
- Create: `agent/crates/agent-core/src/validation/prefix_problem.rs`
- Modify: `agent/crates/agent-core/src/validation/mod.rs` (add `mod prefix_problem;` and `mod prefixed_name;` — crate-internal, not `pub`)
- Modify: `validation/db/db_user_name.rs`, `validation/db/database_name.rs`, `validation/system/sftp_user_name.rs`

**Interfaces:**
- Produces (crate-internal):
```rust
pub(crate) const SEPARATOR: char;                    // in prefixed_name
pub(crate) enum PrefixProblem { Empty, UnexpectedCharacter { character: char }, TooLong { length: usize } }
pub(crate) fn prefixed(account: &AccountName, requested: &str, maximum_length: usize)
    -> Result<String, PrefixProblem>
```
- Consumed by: the three `for_account`s here; Task 5 consumes `SEPARATOR` from the `decode` methods.

- [x] **Step 1: Write `prefix_problem.rs`**

```rust
//! Why a requested name could not be prefixed.

/// Why a requested name could not be prefixed. The public types map each case
/// onto their own domain error, so this stays crate-internal vocabulary.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum PrefixProblem {
    /// Nothing was requested.
    Empty,
    /// A character outside `[a-z0-9]` — the separator included, which is what
    /// stops account `alice` requesting `bob_admin` and being handed a name
    /// that reads as `bob`'s.
    UnexpectedCharacter {
        /// The offending character.
        character: char,
    },
    /// The prefixed result exceeds the caller's limit.
    TooLong {
        /// The length the prefixed result would have had.
        length: usize,
    },
}
```

- [x] **Step 2: Write `prefixed_name.rs`**

```rust
//! The shared core of every "account-prefixed" name.

use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::system::name::AccountName;

/// The separator between the owning account and the requested half.
///
/// One constant for all three prefixed names, because the DECODERS on those
/// types split at this character and a disagreement would decode silently
/// wrong (see `SftpUserName::decode`).
pub(crate) const SEPARATOR: char = '_';

/// Builds `<account>_<requested>`, applying the shared alphabet and length
/// rules all three prefixed names agree on.
///
/// # Errors
///
/// - [`PrefixProblem::Empty`] when `requested` is empty.
/// - [`PrefixProblem::UnexpectedCharacter`] for anything outside `[a-z0-9]`.
/// - [`PrefixProblem::TooLong`] when the prefixed result exceeds
///   `maximum_length` bytes.
pub(crate) fn prefixed(
    account: &AccountName,
    requested: &str,
    maximum_length: usize,
) -> Result<String, PrefixProblem> {
    if requested.is_empty() {
        return Err(PrefixProblem::Empty);
    }

    if let Some(character) = requested
        .chars()
        .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
    {
        return Err(PrefixProblem::UnexpectedCharacter { character });
    }

    let full = format!("{}{SEPARATOR}{requested}", account.as_str());
    if full.len() > maximum_length {
        return Err(PrefixProblem::TooLong { length: full.len() });
    }

    Ok(full)
}
```

- [x] **Step 3: Rewrite the three `for_account`s as mappings**

Method docs stay EXACTLY as they are (they carry the per-domain reasoning); only bodies change. Keep each file's `MAXIMUM_LENGTH`; delete each file's `SEPARATOR` const (verified used only by `for_account`). Worked example, `db_user_name.rs`:
```rust
use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::prefixed_name::prefixed;

    pub fn for_account(account: &AccountName, requested: &str) -> Result<Self, DbUserNameError> {
        prefixed(account, requested, MAXIMUM_LENGTH)
            .map(Self)
            .map_err(|problem| match problem {
                PrefixProblem::Empty => DbUserNameError::Empty,
                PrefixProblem::UnexpectedCharacter { character } => {
                    DbUserNameError::UnexpectedCharacter { character }
                }
                PrefixProblem::TooLong { length } => DbUserNameError::TooLong { length },
            })
    }
```
Apply the same shape in `database_name.rs` (limit 64, `DatabaseNameError`) and `sftp_user_name.rs` (limit 32, `SftpUserNameError`).

- [x] **Step 4: Verify**

Run: `source scripts/dev && maran agent check`
Expected: exit 0 — the existing `*_tests.rs` for all three types pin the exact behavior (including `TooLong { length: 33 }` / `{ length: 65 }`) and must pass unchanged.

---

### Task 5: `decode()` on the prefixed types — one home for the inverse

**Files:**
- Modify: `validation/system/sftp_user_name.rs`, `validation/db/database_name.rs`, `validation/db/db_user_name.rs` (add `decode`)
- Modify: `ops/src/sftp/process_sftp_host.rs` (delete `NAME_SEPARATOR` + `decode_login`; call `SftpUserName::decode` at the `filter_map` site), `ops/src/db/list_databases.rs` (delete its `SEPARATOR`; call `DatabaseName::decode`), `ops/src/db/drop_account_databases.rs` (delete its `SEPARATOR`; call **`DbUserName::decode`** — this file decodes database USERS, not databases)
- Test: `agent-core/src/tests/validation/system/sftp_user_name_tests.rs`, `agent-core/src/tests/validation/db/database_name_tests.rs`, `agent-core/src/tests/validation/db/db_user_name_tests.rs` (add decode cases)

**Interfaces:**
- Produces (same shape on all three types): `pub fn decode(account: &AccountName, candidate: &str) -> Option<Self>` — all three ops call sites need the decoded VALUE (verified), which this returns.

- [x] **Step 1: Add `decode` to the three types**

Worked example, `sftp_user_name.rs`:
```rust
    /// Decodes a full system login back into the name, only when it belongs
    /// to `account`.
    ///
    /// The inverse of [`SftpUserName::for_account`], kept on the same type so
    /// the separator cannot drift between the builder and the decoder. The
    /// WHOLE account is compared, not a prefix of it: account names may
    /// contain the separator, so `alice_` is a prefix of `alice_bob_deploy`,
    /// which belongs to account `alice_bob`. Splitting at the LAST separator
    /// recovers the halves, because `for_account` forbids the separator in
    /// the requested half.
    #[must_use]
    pub fn decode(account: &AccountName, candidate: &str) -> Option<Self> {
        let (owner, requested) =
            candidate.rsplit_once(crate::validation::prefixed_name::SEPARATOR)?;
        if owner != account.as_str() {
            return None;
        }

        // Rebuilt rather than wrapped: `for_account` is the only constructor,
        // which keeps every value in the process one this agent could create.
        Self::for_account(account, requested).ok()
    }
```
Same method on `DatabaseName` ("Decodes a database name back…") and `DbUserName` ("Decodes a database user name back…").

- [x] **Step 2: Point the three ops call sites at the methods**

- `process_sftp_host.rs`: delete the file-level `NAME_SEPARATOR` const and the `decode_login` fn (with its doc); at the listing site replace `decode_login(account, name)` with `SftpUserName::decode(account, name)`.
- `list_databases.rs`: delete its `SEPARATOR` const; replace the inline rsplit/compare/rebuild with `DatabaseName::decode(account, row_name)`.
- `drop_account_databases.rs`: delete its `SEPARATOR` const; replace the users-pass inline decode with `DbUserName::decode(account, user_name)`.
Preserve each site's surrounding filtering semantics exactly (they iterate rows and keep the `Some`s).

- [x] **Step 3: Add decode tests (behavior sentences)**

In `sftp_user_name_tests.rs`:
```rust
#[test]
fn a_login_decodes_back_to_the_name_that_built_it() {
    let name = SftpUserName::for_account(&account(), "deploy").expect("a valid request");

    let decoded = SftpUserName::decode(&account(), name.as_str());

    assert_eq!(decoded.expect("the login round-trips").as_str(), name.as_str());
}

#[test]
fn another_accounts_login_does_not_decode() {
    let other = AccountName::parse("alice_bob").expect("a valid account name");
    let login = SftpUserName::for_account(&other, "deploy").expect("a valid request");

    assert!(SftpUserName::decode(&account(), login.as_str()).is_none());
}
```
Mirror the same two tests in `database_name_tests.rs` (with `DatabaseName`, request `"shop"`) and `db_user_name_tests.rs` (with `DbUserName`, request `"shop"`).

- [x] **Step 4: Verify**

Run: `source scripts/dev && maran agent check`
Expected: exit 0; sftp/db area tests unchanged and green; 6 new decode tests pass.

---

### Task 6: `RecordingCommands` — one recording core for the TWO compatible fakes

Scope note (audited): only `fake_php_host.rs` and accounts' `RecordingHost` share the record-argv + configured-outcome core. `fake_ssl_host.rs` (per-argv `match` answers, program-less recording, panics on unexpected argv) and `fake_monitor_host.rs` (per-unit stdout map, fallible `run`) are different by design and are NOT touched.

**Files:**
- Create: `ops/src/tests/support/mod.rs`, `ops/src/tests/support/recording_commands.rs`
- Modify: `ops/src/lib.rs` — mount once: `#[cfg(test)] #[path = "tests/support/mod.rs"] mod test_support;` (the same `#[path]` mounting the areas already use for fakes, e.g. `ops/src/php/mod.rs`)
- Modify: `ops/src/tests/php/fake_php_host.rs`, `ops/src/tests/accounts/account_operations_tests.rs` (its `RecordingHost`)

**Interfaces:**
- Produces:
```rust
pub(crate) struct RecordingCommands { /* private */ }
impl RecordingCommands {
    pub(crate) fn new() -> Self
    pub(crate) fn record(&self, program: &str, arguments: &[&str]) -> CommandOutcome
    pub(crate) fn set_next(&self, status: i32, stdout: &str, stderr: &str)
    pub(crate) fn calls(&self) -> Vec<Vec<String>>
    pub(crate) fn calls_to(&self, program: &str) -> Vec<Vec<String>>
}
```

- [x] **Step 1: Write the support module**

`tests/support/mod.rs`:
```rust
//! Shared test support, mounted from `lib.rs` under `#[cfg(test)]`.

pub(crate) mod recording_commands;
```

`tests/support/recording_commands.rs`:
```rust
//! The recording core a fake host composes: what was run, what to answer.

// A fake's lock can only be poisoned by a failing test, and a failing
// assertion IS the reporting mechanism there.
#![allow(clippy::unwrap_used)]

use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;

/// Records every argv it is handed and answers with a configured outcome.
///
/// Not a mock with expectations: tests assert on the recorded argv afterwards,
/// which is the thing worth pinning (`useradd --create-home` and `useradd -m`
/// differ by nothing a type system can see and by everything a customer's
/// data can). Fakes COMPOSE this — hold it in a field and delegate — so each
/// area's fake keeps its own trait impls and area-specific fixtures. Fakes
/// that answer per-argv (ssl) or per-unit (monitor) are a different kind on
/// purpose and do not use this.
pub(crate) struct RecordingCommands {
    /// Every argv handed to [`RecordingCommands::record`], in order.
    calls: Mutex<Vec<Vec<String>>>,
    /// The outcome the following `record` calls answer with.
    next: Mutex<(i32, String, String)>,
}

impl RecordingCommands {
    /// Creates a recorder that answers success with empty output.
    pub(crate) fn new() -> Self {
        Self {
            calls: Mutex::new(Vec::new()),
            next: Mutex::new((0, String::new(), String::new())),
        }
    }

    /// Records the argv and answers with the configured outcome.
    pub(crate) fn record(&self, program: &str, arguments: &[&str]) -> CommandOutcome {
        let mut command = vec![program.to_owned()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        let (status, stdout, stderr) = self.next.lock().unwrap().clone();
        CommandOutcome { status, stdout, stderr }
    }

    /// Configures what every following `record` answers.
    pub(crate) fn set_next(&self, status: i32, stdout: &str, stderr: &str) {
        *self.next.lock().unwrap() = (status, stdout.to_owned(), stderr.to_owned());
    }

    /// Every recorded argv, in order.
    pub(crate) fn calls(&self) -> Vec<Vec<String>> {
        self.calls.lock().unwrap().clone()
    }

    /// The recorded argvs whose program equals `program`.
    pub(crate) fn calls_to(&self, program: &str) -> Vec<Vec<String>> {
        self.calls()
            .into_iter()
            .filter(|call| call.first().is_some_and(|first| first == program))
            .collect()
    }
}
```

- [x] **Step 2: Compose it into the two fakes**

`fake_php_host.rs`: replace the `commands: Mutex<Vec<Vec<String>>>` and `command: Mutex<(i32, String)>` fields with one `recording: crate::test_support::recording_commands::RecordingCommands`; `ConfigHost::run` becomes `Ok(self.recording.record(program, arguments))`; the `reject_commands`-style configurator calls `self.recording.set_next(status, "", stderr)`; assertion helpers delegate to `self.recording.calls()` / `calls_to`, keeping their public signatures so the area tests do not change.

Accounts' `RecordingHost`: keep its `statuses: Mutex<Vec<i32>>` queue and stdout field (queue semantics are area behavior); its `run` becomes: pop the next status as today, then `self.recording.set_next(status, &self.stdout(), Self::stderr_for(status))` followed by `Ok(self.recording.record(program, arguments))` — and its calls-accessors delegate to `self.recording`. If the accounts helpers read simpler with the queue folded differently, keep the EXTERNAL helper signatures identical; the tests' assertions are the contract.

- [x] **Step 3: Verify**

Run: `source scripts/dev && maran agent check`
Expected: exit 0; php and accounts test files pass with zero assertion changes.

---

### Task 7: Rules and maps — same-change bookkeeping + the rule of two

**Files:**
- Modify: `rules/rust.md` (canonical map + the "rule of two" paragraph)
- Modify: `agent/CLAUDE.md` (crate tables)

- [x] **Step 1: Map entries (rules/rust.md)**

Services block gains:
```
    │   │       ├── wire/      the proto↔domain boundary shared by EVERY service:
    │   │       │              invalid_input.rs · validated_account.rs ·
    │   │       │              system_failure.rs · run_blocking.rs. Services import
    │   │       │              from here and never from each other's folders.
```
agent-core `utils/` line gains: `spawn_argv.rs — the ONE plain argv spawn body; hosts whose spawn is deliberately different (stdin-piped, bounded-read) keep their own and say why`.
`validation/` block gains: `prefixed_name.rs + prefix_problem.rs — crate-internal shared core of the three account-prefixed names (the types keep their own errors and docs)`.
ops block gains: `src/tests/support/ — recording_commands.rs, the recording core the compatible fakes compose (mounted from lib.rs under #[cfg(test)])`.

- [x] **Step 2: The rule of two (rules/rust.md, under "One unit per file")**

```markdown
**The rule of two.** The SECOND copy of a block is the moment it moves to a
named home — a shared module with a purpose-name, never a `util.rs`. A third
copy is a review reject. This is how `safe_write/` and `CommandOutcome`
happened, and how nine copies of a spawn wrapper must not happen again.
```

- [x] **Step 3: agent/CLAUDE.md tables**

Services table gains a `wire/` row; agent-core table gains `spawn_argv.rs` and the `prefixed_name`/`prefix_problem` pair on the validation row; ops table gains the `tests/support/` row. Wording mirrors Step 1.

- [x] **Step 4: Final verification, whole workspace**

Run: `source scripts/dev && maran agent check && maran structure && maran handshake && maran proto`
Expected: all green; test count = pre-plan count + 6 new decode tests, zero changed assertions.

## Self-Review

- Findings coverage: 1→Task 2, 2→Task 2 (four-field amendment), 3→Task 3 (six hosts; db/ssl/sftp-chpasswd excluded by name), 4→Task 1, 5→Task 4, 6→Task 5 (DbUserName in drop_account_databases), 7→Task 6 (two fakes only), 8→Task 7 + non-goals. ✓
- Adversarial audit (2026-09-01) amendments folded: message-noun table incl. "monitoring reading"; php's only migratable call site named and the streaming rpc excluded; unused-import pruning step; four-Arc accounts rewrite with unchanged `new` arity; `#[non_exhaustive]` reasoning corrected to the `other =>` arms; `prefix_problem.rs` split out to keep one-unit-per-file; support module mounted once from `lib.rs`; `run_blocking` declared in Task 2, so Task 1 is green standalone. ✓
- Type consistency: `system_failure(String)` (Task 1) consumed by `run_blocking` (Task 2); `spawn_argv -> io::Result<CommandOutcome>` mapped per host (Task 3); `PrefixProblem` fields identical between Task 4's enum and mappings; `decode` consumes Task 4's `SEPARATOR`. ✓
- No commit steps by design (owner commits); each task's gate is the maran command set. ✓

---

## Outcome (all seven tasks complete, 2026-09-05)

Every step above is done and its gate ran green. Per-task reports:
`.superpowers/sdd/2026-09-01-agent-shared-homes/task-{1,2,3,4-5,6-7}-report.md`.

Final gate, `agent/`: `cargo fmt --check`, `cargo clippy --all-targets -D warnings`,
`RUSTDOCFLAGS="-D warnings" cargo doc --no-deps --workspace` all clean;
`cargo test` = **1114 passed / 0 failed / 60 ignored across 23 targets**
(pre-plan count + the 6 new decode tests, zero changed assertions), `maran structure`
STRUCTURE-OK.

### Where the tree differed from what this plan predicted

These are corrections to the Findings above, recorded so the next reader inherits
the measurement and not the estimate:

1. **Finding 4 — `invalid_input` had 24 importers, not 23.** The extra file was
   `db/validated_account.rs`, which Task 1 deleted; after the move exactly 23 files
   import `wire::invalid_input`, which is the number the plan quoted. Not tree
   drift, and no work was missed. The two `validated_account.rs` files were verified
   identical in code (a comment-stripped `diff` was empty) before one was deleted.
2. **Finding 3 — a FOURTH spawn candidate exists, and was deliberately not folded.**
   `ops/src/cron/crontab_spool.rs`'s `fn run` has the same
   `.args().env().output()` outline as the six, but its signal-killed status is
   `PROGRAM_UNAVAILABLE`, a negative cron-specific sentinel that
   `CronError::CrontabRefused` carries as a code, and it is paired with the bounded
   `run_bounded` beside it. Folding it would have lost the sentinel or pushed a cron
   constant into `agent-core`. The plan named six and excluded this one by silence;
   it is now excluded by name, and it is one of the three worked examples in the
   rule of two (Task 7) precisely because it looks like a copy and is not.
3. **Finding 3 — `LC_ALL=C` was pinned on ONE host, not assumed on all six.** Only
   the accounts host set it. Rather than let the pin evaporate into the shared body,
   `spawn_argv` sets it on every spawn and owns the constants. That is a stated
   behaviour change for firewall, monitor, php, sftp and sites, which now spawn
   under `LC_ALL=C` where they did not: monitor parses a service manager's own words
   and firewall reads `nft`'s diagnostics, so both carried the same exposure that
   once made `quota` report "unlimited" and made a crontab-less account undeletable.
   Nothing in those areas matches translated text today, so nothing breaks; what
   changes is that nothing can.
4. **Task 7's map was already partly done when it started.** Tasks 4 and 5 had
   entered `prefixed_name.rs`/`prefix_problem.rs` in both maps and `spawn_argv.rs`
   in `agent/CLAUDE.md`, because the Global Constraints require the map to move with
   the file. Task 7 therefore audited the whole map against the tree rather than
   applying the list in Step 1, and that audit found one entry the list did not name:
   `services/db/` had never gained `validated_password_change.rs` (a plan-4 gap).
   It is now listed.
5. **Task 7 found and removed one live violation of the rule it was writing.**
   Task 3 noted that cron's private `LOCALE_VARIABLE`/`LOCALE_VALUE` had become a
   second copy of `spawn_argv`'s public pair, and left it as out of scope. Cron's
   spawns stay their own (stdin, sentinel); its two constants now read their VALUES
   from `spawn_argv` and keep only their own local reasoning as prose.
