# Databases and SFTP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hosting account can hold MySQL/MariaDB databases and SFTP users, both within its plan's limits, both provisioned by the root agent and both isolated from every other tenant by the operating system rather than by a check in the panel.

**Architecture:** The shape Plan 3 established, unchanged. The Rust agent owns every system mutation and every path; `agent-core` validates input into types that cannot hold an invalid value; `distro` is the only crate that may name a distribution; the C# panel holds the rows, the plan limits and the audit trail, and reaches the agent only through typed clients behind their resilience pipelines. Two new panel modules — `Databases` and `Ftp` — sit beside `Sites` and `Ssl` with their own schemas and their own tenant query filters.

**Tech Stack:** Rust (tonic), C# .NET 9 (EF Core, Wolverine), PostgreSQL for the panel's own rows, MySQL/MariaDB as the managed service, OpenSSH `internal-sftp` for SFTP, Vue 3 + TypeScript for the screens.

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md` — §8 (tenancy, roles, limits), §11 (Databases, FTP/SFTP), §15 (audit).

**Tracked as:** GitHub issue #4, reduced to two of its three subsystems. The file manager is deliberately **not** in this plan: it is nine RPCs over arbitrary customer-controlled paths with chunked upload and archive extraction, and it gets its own plan and its own privileged-code review. That split was the owner's decision, recorded here so nobody re-merges the scope.

## Global Constraints

Copied from the spec and from `rules/`. Every task's requirements implicitly include this section.

- **FTP means SFTP in this plan, and only SFTP.** OpenSSH `internal-sftp` chrooted into a **per-account bind-mount jail**, which the spec names as the default. vsftpd and FTPS are **removed from this plan** and deferred (issue #20). The chroot is NOT the account's home directly: OpenSSH refuses to chroot into a directory that is not root-owned, and plan 3 ships the home as `<account>:web_server_group 0750`. Rather than change that home ownership — an invariant Sites, nginx and php-fpm all depend on — the jail is a root-owned per-account directory with the real home **bind-mounted** inside it. The home is never modified, cross-tenant isolation is unchanged, and nginx serving is untouched. This is a deliberate architecture decision (recorded in the ledger) to keep SFTP's complexity inside SFTP and avoid a change that would ripple across three shipped subsystems.
- **No shell strings, anywhere.** Processes are spawned with argv arrays against an allow-list of absolute paths supplied by the distro adapter. This plan runs `mysql` and edits sshd configuration; none of it may be assembled into a command line.
- **Credentials are shown once and never stored.** The panel generates a password, hands it to the agent, shows it to the operator once, and keeps **no copy** — not plaintext, not a hash. MySQL holds its own. A panel that can reveal a customer's database password later is a panel that can leak it.
- **A password is validated, not escaped.** MySQL DDL cannot parameterise `IDENTIFIED BY`, so the password reaches a root-run statement. It is therefore not a free string but a `Password` validated type whose constructor accepts only an injection-safe alphabet — ASCII letters, digits and a fixed set of symbols that excludes the quote, backtick, backslash, newline and space. The generator draws from exactly that alphabet. This is the same "validated, not escaped" rule the names follow, applied to the one input the reviewer found reaching root SQL unchecked. `Secret` (non-printing) wraps the value for carriage; `Password` (validated) is what guarantees it cannot inject. They are two types with two jobs.
- **Every name is prefixed with the owning account, and the panel's own rows are the tenant boundary.** A database is `<account>_<name>` and a database user is `<account>_<name>`; the suffix forbids the separator, so the last-underscore split back to (account, name) is unique even though `AccountName` permits underscores. But the prefix isolates **creation** only: a `starts_with("acme_")` scan aliases account `acme` onto `acme_dev`'s databases. So listing, dropping and sizing are authorised by the panel's own tenant-filtered `Database` rows — never by a prefix scan on the agent — exactly as `Sites` is. The agent's `ListDatabases` is diagnostic only and matches the full account by last-underscore decode, never `starts_with`.
- **Counted limits are checked in the panel; the disk is enforced by the agent.** `Plan` gains `MaxDatabases` and `MaxSftpUsers` beside the existing `MaxSites` and `MaxPhpWorkersPerPool`.
- **Deleting an account destroys its databases and SFTP access first.** This is the plan-3 pool-leak lesson: `userdel` does not touch MySQL or sshd, so without an explicit cascade a deleted account's `<account>_*` databases and its SFTP membership survive on the host, and a re-created account of the same name inherits the prior tenant's live data and logins. The agent's `delete_account` drops them where pool cleanup already lives; the panel emits an `AccountDeleting` event so the Databases and Sftp modules delete their rows; a cleanup failure aborts the deletion.
- **Tenant isolation answers 404, never 403.** A 403 confirms the resource exists. Every account-scoped route gets its IDOR test, and the fixture reads routes off the controller by reflection so a new route fails naming itself.
- **Errors are values.** `Result<T>` and a typed `Error` in C#; typed error enums in Rust. `AgentErrorTranslator` remains the single wire-error boundary, and the agent's text — which can carry paths and quoted credentials — is logged, never returned.
- **Doc comments on every item, private included.** `CS1591` is an error; there are no `<NoWarn>` suppressions and none may be added. One file, one type or unit.
- **`unsafe` only inside `agent-core/src/privs/`**, under the existing scoped allow.
- **All repository text in English.** Locale files carry en, ru and hy in parity; `maran structure` checks it.
- **Gates:** `dotnet test Maran.sln`, `dotnet build Maran.sln -warnaserror`, `maran agent check` (fmt, clippy `-D warnings`, tests, rustdoc), `maran structure`, `maran proto`, `maran migrate check`, `maran migrate status`, `maran licenses --check`, `maran handshake`, `npm run lint && npm run typecheck && npm run build && npx playwright test`, and both container polygons.

## What Plan 3 learned, and what this plan must not repeat

These are not slogans. Each cost a review round or a production defect in the previous plan, and every one of them applies here.

- **A test that reads a registration is not a test that resolves it.** 690 backend tests passed while the panel could not boot, because a `BackgroundService` singleton took a scoped dependency and no test built the real container.
- **A fake proves a call was made, never that the real implementation defends anything.** Mutation kills that land on a fake's ordering assertions are hollow.
- **A protection whose removal changes nothing is decoration.** Delete it or make it observable; do not label it and move on.
- **"This cannot be tested" is a claim requiring evidence.** It was asserted once about a private key that was in fact reaching a log.
- **Two checks that mask each other are one check plus decoration.** Mutate each alone, then both together.
- **Bound every wait.** Four hangs in Plan 3 were each first blamed on the runner.
- **Mutation harnesses lie in the direction of confidence.** Seven harness defects occurred; six reported a protection as tested when it was not. `rules/testing.md` now carries the rules — follow that section exactly.
- **The installer is part of the product.** Plan 3 shipped a panel that could not create a site on a fresh server because `install.sh` never made a directory the polygon images made for themselves. Anything this plan needs on a host goes into the installer, and the polygon proves the installer does it rather than doing it itself.
- **A resource created on the host outlives the account unless something deletes it.** Plan 3's worst defect was a php-fpm pool that survived account deletion and stopped php-fpm for every tenant. A MySQL database and an SFTP login survive `userdel` the same way, and the twist is worse: a re-created account name inherits them. Account-deletion cleanup is a task in this plan, not an afterthought (Task 12).
- **A validated type guards names; it does not guard the password.** The password is the one customer-influenced value that reaches root-run SQL, and a `Secret` that cannot be printed also cannot be inspected. The password needs its own validated type, or the injection the names are immune to walks straight in through the one field nobody typed a constructor for.

---

## File structure

Where each new unit lives, decided before the tasks so the decomposition is locked in rather than discovered.

### Agent — `agent/crates/`

| Path | Responsibility |
|---|---|
| `agent-core/src/validation/database_name.rs` | `DatabaseName` — the account prefix is applied here and nowhere else. |
| `agent-core/src/validation/database_name_error.rs` | Its refusals, one variant per reason. |
| `agent-core/src/validation/db_user_name.rs` | `DbUserName`, same prefix rule, MySQL's 32-byte user limit. |
| `agent-core/src/validation/db_user_name_error.rs` | Its refusals. |
| `agent-core/src/validation/sftp_user_name.rs` | `SftpUserName`, same prefix rule, 32-byte useradd limit. |
| `agent-core/src/validation/sftp_user_name_error.rs` | Its refusals. |
| `agent-core/src/validation/password.rs`, `password_error.rs` | `Password` — the injection-safe validated type. Constructor refuses the quote, backtick, backslash, newline and space; `as_str` returns the value; `Debug` writes `<password>`; no `Display`, no `Serialize`. This is the type that makes interpolation into `IDENTIFIED BY` safe. |
| `agent-core/src/validation/secret.rs` | `Secret` — a wrapper that cannot be printed, for any value carried but not validated here. `Debug` writes `<secret>`, no `Display`, no `Serialize`. |
| `distro/src/adapter.rs` | Three new trait methods: `mysql_client_binary`, `mysql_service`, `sftp_group`. |
| `distro/src/debian/debian_adapter.rs`, `distro/src/rhel/rhel_adapter.rs` | Their per-family answers. |
| `ops/src/db/mod.rs` | Declarations only. |
| `ops/src/db/db_error.rs` | One error enum for the area. |
| `ops/src/db/db_host.rs` | The trait that keeps process spawning injectable. |
| `ops/src/db/process_db_host.rs` | Its real implementation. |
| `ops/src/db/create_database.rs`, `drop_database.rs`, `list_databases.rs`, `database_size.rs` | One file per RPC. |
| `ops/src/db/model/` | Inputs and outputs for the above. |
| `ops/src/sftp/mod.rs`, `sftp_error.rs`, `sftp_host.rs`, `process_sftp_host.rs` | The SFTP area, same shape as `db`. No template: an SFTP user is a system account in the `sftp_group`, and the chroot is one sshd `Match Group` block written once by the installer, not a per-user file. |
| `ops/src/sftp/create_sftp_user.rs`, `set_sftp_password.rs`, `delete_sftp_user.rs` | One file per RPC. `create` adds the user to `sftp_group` with a nologin shell; `set_sftp_password` sets it via `chpasswd` over stdin (no shell, argv array, the password never on a command line); `delete` removes the user. |
| `agent/src/services/db/db_service.rs`, `db_status.rs` | Proto → ops → response, and the error → gRPC code map. |
| `agent/src/services/sftp/sftp_service.rs`, `sftp_status.rs` | Same. |
| `scripts/lib/check-structure.sh` | The new agent files are added to its subject-name list in the same task, or `maran structure` fails on them. |

### Panel — `backend/src/`

| Path | Responsibility |
|---|---|
| `Maran.Agent.Client/Interfaces/IAgentDbClient.cs`, `IDbServiceInvoker.cs` | The typed client and its transport seam. |
| `Maran.Agent.Client/Services/DbService/` | `AgentDbClient`, `GrpcDbServiceInvoker`, the DTOs. |
| `Maran.Agent.Client/Interfaces/IAgentSftpClient.cs`, `ISftpServiceInvoker.cs` | Same for SFTP. |
| `Maran.Agent.Client/Services/SftpService/` | Same. |
| `Maran.Host/Resilience/ResilientAgentDbClient.cs`, `ResilientAgentSftpClient.cs` | The pipeline decorators. |
| `Maran.Modules/Databases/` | The module: `Domain/Database.cs`, persistence with its tenant filter, `Commands/{CreateDatabase,DropDatabase,ResetDatabasePassword}/`, `Queries/{ListDatabases,GetDatabase}/`, controller, resx triple. |
| `Maran.Modules/Sftp/` | The module: `Domain/SftpUser.cs`, persistence, `Commands/{CreateSftpUser,ResetSftpPassword,DeleteSftpUser}/`, `Queries/ListSftpUsers/`, controller, resx triple. Cross-module cleanup handler for `AccountDeleting`. |
| `Maran.Modules/Accounts/Domain/Plan.cs` | Gains `MaxDatabases` and `MaxSftpUsers`, with a migration that backfills real values. |
| `Maran.Sdk/Events/AccountDeleting.cs` | The Sdk event Accounts publishes and the two new modules handle to drop their resources. |

### SPA — `frontend/src/`

| Path | Responsibility |
|---|---|
| `types/database.ts`, `types/sftpUser.ts` | The shapes the API returns. |
| `composables/apis/useDatabasesApi.ts`, `useSftpApi.ts` | The calls, used from stores only. |
| `stores/databases.ts`, `stores/sftp.ts` | State and actions. |
| `pages/databases/DatabasesPage.vue`, `components/databases/DatabaseCreatedDialog.vue` | The list, the create form, and the one-time credential reveal. |
| `pages/sftp/SftpUsersPage.vue`, `components/sftp/SftpUserCreatedDialog.vue` | The list, the create form, and the one-time credential reveal (SFTP passwords are shown once too). |
| `locales/{en,ru,hy}/databases.json`, `sftp.json` | Copy, in parity. |

### Installer — `installer/lib/`

| Path | Responsibility |
|---|---|
| `installer/lib/85-mysql.sh` | Ensures MariaDB exists and runs, that `root@localhost` authenticates over the unix socket, and — if it does not — **fails loudly** rather than storing a password. Leaves the panel's own PostgreSQL untouched. |
| `installer/lib/86-sftp.sh` | Creates the `sftp_group`, and adds one idempotent `Match Group` block to sshd_config giving `ChrootDirectory %h`, `ForceCommand internal-sftp`, `AllowTcpForwarding no`, `X11Forwarding no`. Reloads sshd. A re-run must not duplicate the block. **The chroot-ownership reconciliation (below) is proven here.** |

---

## Phase A — the agent

### Task 1: Validated names, and the account prefix

**Files:**
- Create: `agent/crates/agent-core/src/validation/database_name.rs`, `database_name_error.rs`, `db_user_name.rs`, `db_user_name_error.rs`, `sftp_user_name.rs`, `sftp_user_name_error.rs`, `password.rs`, `password_error.rs`, `secret.rs`
- Modify: `agent/crates/agent-core/src/validation/mod.rs`, `agent/crates/agent-core/src/lib.rs`, `rules/rust.md` (canonical layout rows for the new files), `scripts/lib/check-structure.sh` (the new files' subject names)
- Test: `agent/crates/agent-core/src/tests/validation/database_name_tests.rs`, `db_user_name_tests.rs`, `sftp_user_name_tests.rs`, `password_tests.rs`, `secret_tests.rs`

**Interfaces:**
- Consumes: `AccountName` from `agent-core/src/validation/name.rs`.
- Produces:
  - `DatabaseName::for_account(account: &AccountName, requested: &str) -> Result<DatabaseName, DatabaseNameError>`; `fn as_str(&self) -> &str` returns the **prefixed** name.
  - `DbUserName::for_account(account: &AccountName, requested: &str) -> Result<DbUserName, DbUserNameError>`; `as_str`.
  - `SftpUserName::for_account(account: &AccountName, requested: &str) -> Result<SftpUserName, SftpUserNameError>`; `as_str`.
  - `Password::parse(value: &str) -> Result<Password, PasswordError>`; `fn as_str(&self) -> &str`. Accepts only `[A-Za-z0-9]` plus a fixed safe symbol set that **excludes** `'` `` ` `` `"` `\` newline and space; `Debug` prints `<password>`; no `Display`, no `serde`. This is the type whose existence makes interpolation into `IDENTIFIED BY '<value>'` and a `user:pass` chpasswd line injection-free.
  - `Secret::new(value: String) -> Secret`; `fn expose(&self) -> &str`. `Debug` prints `<secret>`; no `Display`, no `serde`. `Secret` hides; `Password` validates — a value that must be safe in root SQL is a `Password`, not merely a `Secret`.

**Why the prefix lives in the type.** MySQL's database and user namespaces are global to the server. If the panel passes a bare name, tenant A creates `wordpress` and tenant B cannot — or worse, B is handed A's. A check in a handler is one refactor from being skipped; a constructor that cannot produce an unprefixed name is not.

- [ ] **Step 1: Write the failing tests for `DatabaseName`**

```rust
#[test]
fn a_name_is_prefixed_with_the_account_that_owns_it() {
    let account = AccountName::parse("alice").expect("valid account");
    let name = DatabaseName::for_account(&account, "shop").expect("valid name");
    assert_eq!(name.as_str(), "alice_shop");
}

#[test]
fn a_requested_name_that_already_contains_the_separator_is_refused() {
    // Otherwise "bob_secrets" requested by alice becomes "alice_bob_secrets", which reads as
    // bob's database in every listing and log the operator will ever see.
    let account = AccountName::parse("alice").expect("valid account");
    assert!(matches!(
        DatabaseName::for_account(&account, "bob_secrets"),
        Err(DatabaseNameError::UnexpectedCharacter { .. })
    ));
}

#[test]
fn a_name_that_would_exceed_mysqls_sixty_four_byte_limit_is_refused() {
    let account = AccountName::parse("alice").expect("valid account");
    let requested = "a".repeat(64);
    assert!(matches!(
        DatabaseName::for_account(&account, &requested),
        Err(DatabaseNameError::TooLong { .. })
    ));
}

#[test]
fn every_refused_character_is_named_by_the_error_it_produces() {
    let account = AccountName::parse("alice").expect("valid account");
    for requested in ["shop; DROP", "shop`", "shop'", "shop\\", "shop ", "shop\n", ""] {
        assert!(
            DatabaseName::for_account(&account, requested).is_err(),
            "{requested:?} must be refused"
        );
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-agent-core database_name`
Expected: FAIL — `DatabaseName` does not exist.

- [ ] **Step 3: Implement `DatabaseName`**

```rust
/// The separator between an account's name and the name it chose. Underscore because MySQL
/// accepts it unquoted and because cPanel has used it for two decades, so operators read
/// `alice_shop` without being told what it means.
const SEPARATOR: char = '_';

/// MySQL's identifier ceiling. Sixty-four **bytes**, not characters — but the allow-list below
/// is ASCII-only, so here the two are the same number.
const MAXIMUM_LENGTH: usize = 64;

impl DatabaseName {
    /// Builds the name a database will actually have, from the account that owns it and the
    /// name its customer asked for.
    ///
    /// The prefix is applied here and in no other place. A caller cannot obtain an unprefixed
    /// `DatabaseName`, which is the point: MySQL's namespace is global to the server, so an
    /// unprefixed name lets one tenant occupy — or reach — another tenant's database.
    ///
    /// # Errors
    ///
    /// [`DatabaseNameError::Empty`] when nothing was requested;
    /// [`DatabaseNameError::UnexpectedCharacter`] for anything outside `[a-z0-9]`, which
    /// includes the separator itself so a request cannot forge another account's prefix;
    /// [`DatabaseNameError::TooLong`] when the prefixed result exceeds MySQL's limit.
    pub fn for_account(account: &AccountName, requested: &str) -> Result<Self, DatabaseNameError> {
        if requested.is_empty() {
            return Err(DatabaseNameError::Empty);
        }

        if let Some(character) = requested
            .chars()
            .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
        {
            return Err(DatabaseNameError::UnexpectedCharacter { character });
        }

        let full = format!("{}{SEPARATOR}{requested}", account.as_str());
        if full.len() > MAXIMUM_LENGTH {
            return Err(DatabaseNameError::TooLong { length: full.len() });
        }

        Ok(Self(full))
    }

    /// The name as MySQL will hold it, prefix included.
    pub fn as_str(&self) -> &str {
        &self.0
    }
}
```

- [ ] **Step 4: Run and watch them pass**

Run: `source scripts/dev && cd agent && cargo test -p maran-agent-core database_name`
Expected: PASS.

- [ ] **Step 5: Do the same for `DbUserName` and `SftpUserName`**

`DbUserName` is identical except `MAXIMUM_LENGTH` is **32**, which is MySQL's user ceiling and is silently truncated by older servers rather than refused — truncation is how two tenants end up sharing one account. `SftpUserName` is identical with a ceiling of **32**, matching the `useradd` name limit, and its suffix alphabet must also satisfy `useradd`'s NAME_REGEX (`[a-z_][a-z0-9_-]*`); since the prefix already starts with a letter, the suffix `[a-z0-9]` is safe.

- [ ] **Step 6: Write `Password` and prove it refuses what would inject**

```rust
#[test]
fn a_password_accepts_the_generator_alphabet_and_nothing_dangerous() {
    assert!(Password::parse("Aa0-_.=+").is_ok());
    for injecting in ["pass'word", "pass`word", "pass\"word", "pass\\word", "pass word", "pass\nword", ""] {
        assert!(Password::parse(injecting).is_err(), "{injecting:?} must be refused");
    }
}

#[test]
fn a_password_does_not_print_its_value() {
    let password = Password::parse("Aa0-_.=+").expect("valid");
    assert_eq!(format!("{password:?}"), "<password>");
}
```

The alphabet is the point: `IDENTIFIED BY '<value>'` and `chpasswd`'s `user:pass` line both become injection-free because a `Password` cannot hold a quote, a backslash or a newline — not because anything is escaped. The doc comment must say exactly that, because the next reader sees a value interpolated next to SQL and will otherwise "fix" it into something that accepts more.

- [ ] **Step 7: Write `Secret` and prove it cannot be printed**

```rust
#[test]
fn a_secret_does_not_print_its_value() {
    let secret = Secret::new("hunter2".to_owned());
    assert_eq!(format!("{secret:?}"), "<secret>");
    assert!(!format!("{secret:?}").contains("hunter2"));
}

#[test]
fn a_secret_inside_a_struct_does_not_leak_through_the_derived_debug() {
    // The realistic leak is not `{secret:?}` written on purpose. It is a request struct with
    // `#[derive(Debug)]` reaching a tracing macro, which is how a password ends up in a log
    // that somebody later pastes into an issue.
    #[derive(Debug)]
    struct Request {
        user: String,
        password: Secret,
    }
    let printed = format!("{:?}", Request { user: "alice".into(), password: Secret::new("hunter2".into()) });
    assert!(printed.contains("alice"));
    assert!(!printed.contains("hunter2"));
}
```

- [ ] **Step 8: Mutation pass**

For each protection — every name's character allow-list, the separator refusal, each length ceiling, `Password`'s alphabet, `Password`'s and `Secret`'s `Debug` — comment it out, run the whole workspace suite with `--no-fail-fast`, confirm a **named** test goes red, restore with a fresh mtime and `cmp`. Paste the table. State which mutants failed alone, because the length check and the character check can mask each other on an input that trips both.

- [ ] **Step 9: Record the new files in `rules/rust.md` and `check-structure.sh`**

A new kind of file gets its named place in `rules/rust.md`'s canonical layout **and** its subject name in `scripts/lib/check-structure.sh` in the same change, per `agent/CLAUDE.md`. Prove the gate sees them: run `maran structure` and confirm STRUCTURE-OK. The reviewer found that a template file absent from the subject list is a guaranteed `maran structure` failure — that is why this is a step, not an assumption.

---

### Task 2: The distro adapter learns about MySQL and SFTP

**Files:**
- Modify: `agent/crates/distro/src/adapter.rs`, `debian/debian_adapter.rs`, `rhel/rhel_adapter.rs`, `src/tests/adapter_for_tests.rs`
- Test: `agent/crates/distro/src/tests/debian_adapter_tests.rs`, `rhel_adapter_tests.rs`

**Interfaces:**
- Produces, on `DistroAdapter`:
  - `fn mysql_client_binary(&self) -> &'static str` — the absolute path to `mysql`.
  - `fn mysql_service(&self) -> &'static str` — the service to restart.
  - `fn sftp_group(&self) -> &'static str` — the group whose members sshd's `Match Group` block chroots.

There is no vsftpd method: SFTP is served by the OpenSSH daemon that is already running, so this plan installs no FTP daemon and names no FTP binary. That is the concrete simplification the SFTP-only ruling buys.

- [ ] **Step 1: Write the failing tests**

```rust
#[test]
fn the_mysql_client_is_an_absolute_path_on_every_family() {
    for adapter in every_adapter() {
        assert!(
            adapter.mysql_client_binary().starts_with('/'),
            "{:?} must name an absolute path: argv spawning has no PATH to fall back on",
            adapter.family()
        );
    }
}

#[test]
fn the_sftp_group_is_the_same_name_on_every_family_so_the_sshd_block_is_portable() {
    // The Match Group block the installer writes names one group. If the two families answered
    // different group names, the block written on one would chroot nobody on the other, and an
    // SFTP user created there would get a full shell login instead of a jail — the opposite of
    // the isolation this exists for. So here the invariant is SAMENESS, asserted deliberately,
    // not difference.
    assert_eq!(DebianAdapter.sftp_group(), RhelAdapter.sftp_group());
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-distro`
Expected: FAIL — the methods do not exist.

- [ ] **Step 3: Add the methods and their per-family answers**

Debian: `/usr/bin/mysql`, service `mariadb`, group `maran-sftp`.
RHEL: `/usr/bin/mysql`, service `mariadb`, group `maran-sftp`.

The two families genuinely agree on all three today; the doc comment says so, and the sameness of the group is load-bearing (the sshd block references it), so it is asserted rather than left to coincidence. `mysql_service` returns `mariadb` on both because both ship MariaDB; if a family ever ships MySQL proper, this is the one method that changes.

- [ ] **Step 4: Run and watch them pass, then prove the polygon agrees**

The polygon test in Task 5 asserts `mysql_client_binary` exists on the real image and that a member of `sftp_group` is actually chrooted by the running sshd. A path that is right in a unit test and absent on AlmaLinux is exactly the defect Plan 3 found in `rhel_services.rs`.

- [ ] **Step 5: Mutation pass**

Swap one family's answer for the other's and confirm the polygon test for that family goes red while the other stays green — the shape that caught the broken RHEL php-fpm path.

---

### Task 3: `ops::db` — create, drop, list, size

**Files:**
- Create: `agent/crates/ops/src/db/{mod,db_error,db_host,process_db_host,create_database,drop_database,list_databases,database_size}.rs`, `ops/src/db/model/{database_request,database_summary,database_size_report}.rs`
- Modify: `agent/crates/ops/src/lib.rs`
- Test: `agent/crates/ops/src/tests/db/{fake_db_host,create_database_tests,drop_database_tests,list_databases_tests,database_size_tests}.rs`

**Interfaces:**
- Consumes: `DatabaseName`, `DbUserName`, `Password`, `AccountName` from Task 1; `DistroAdapter::mysql_client_binary` from Task 2.
- Produces:
  - `create_database(host: &dyn DbHost, request: &CreateDatabaseRequest) -> Result<(), DbError>` — `CreateDatabaseRequest { database: DatabaseName, user: DbUserName, password: Password }`.
  - `drop_database(host: &dyn DbHost, name: &DatabaseName, user: &DbUserName) -> Result<(), DbError>` — takes the **validated** `DatabaseName`, which the service builds by re-running `for_account` on the account the panel authorised, never a raw wire string. A fully-qualified name off the wire that skipped `for_account` is the drop-injection the reviewer found; the type is how it cannot arrive.
  - `list_databases(host: &dyn DbHost, account: &AccountName) -> Result<Vec<DatabaseSummary>, DbError>` — **diagnostic only.** See the isolation note below; the panel's own rows, not this call, decide what a tenant may see.
  - `database_size(host: &dyn DbHost, name: &DatabaseName) -> Result<DatabaseSizeReport, DbError>` — validated name, as `drop`.
  - `DbError` variants: `AlreadyExists`, `NotFound`, `ClientFailed { code: i32 }`, `Unparsable`, `AccessDenied`.

**How the agent authenticates to MySQL, and why it is not a stored password.** The agent runs as root. MySQL and MariaDB on both families ship `unix_socket` (`auth_socket`) authentication for `root@localhost`, so a root process connects over the socket with no password at all. That is the design: a password the agent stores is a password that can be stolen from the agent. The installer's job (Task 11) is to ensure that plugin is enabled and to fail loudly if it is not.

**How SQL is built without building a string — names AND the password.** `mysql --execute` takes one statement, and neither identifiers nor the `IDENTIFIED BY` literal can be parameterised in DDL. The protection is the validated type, not escaping, and it must cover **both** interpolated values: a `DatabaseName`/`DbUserName` holds only `[a-z0-9_]`, and a `Password` holds only its injection-safe alphabet, so `CREATE DATABASE \`name\`` and `CREATE USER 'user'@'localhost' IDENTIFIED BY '<password>'` cannot inject. The reviewer found the earlier draft protected the names and left the password a free string reaching root SQL — that is why `Password` exists and why this step tests it directly. The doc comment must say "validated, not escaped" for both, because the next reader sees interpolation next to SQL and will otherwise "fix" it into something that accepts more.

**Why `list_databases` is not the tenant boundary (the reviewer's B1).** A prefix scan — `SHOW DATABASES` filtered by `starts_with("alice_")` — aliases account `alice` onto `alice_bob`'s databases, because `alice_bob_shop` starts with `alice_`. The MySQL name is collision-free (the suffix forbids the separator, so the last-underscore split is injective), but a `starts_with` filter is not the same predicate as "decodes to this account". So authorisation lives in the panel: the `Databases` module lists, drops and sizes only its own tenant-filtered rows (Task 7). This agent call exists for a reconciliation/diagnostic view an admin runs, and even here it decodes each name by its last underscore and matches the **whole** account, never `starts_with`.

- [ ] **Step 1: Write the failing tests against a fake host**

```rust
#[test]
fn creating_a_database_asks_the_client_for_the_prefixed_name_and_never_the_requested_one() {
    let host = FakeDbHost::new();
    let account = AccountName::parse("alice").expect("valid");
    let request = CreateDatabaseRequest {
        database: DatabaseName::for_account(&account, "shop").expect("valid"),
        user: DbUserName::for_account(&account, "shop").expect("valid"),
        password: Password::parse("Gen3rated-pw").expect("valid"),
    };

    create_database(&host, &request).expect("created");

    let statements = host.statements();
    assert!(statements.iter().any(|s| s.contains("alice_shop")));
    assert!(
        !statements.iter().any(|s| s.contains("`shop`")),
        "the bare requested name must never reach MySQL"
    );
}

#[test]
fn a_password_the_type_forbids_cannot_be_constructed_so_it_cannot_reach_the_statement() {
    // The injection this closes is a quote in the password breaking out of IDENTIFIED BY '…'.
    // The defence is that such a password has no Password value, so there is nothing to pass to
    // create_database. This is the "validated, not escaped" guarantee, at the type boundary.
    assert!(Password::parse("pw' OR '1'='1").is_err());
}

#[test]
fn an_error_from_the_client_carries_a_typed_variant_and_not_the_raw_stderr() {
    // The realistic leak is the client quoting the credential back. The error must be a typed
    // DbError, never the client's stdout/stderr verbatim.
    let host = FakeDbHost::failing_with(1045, "Access denied for user 'alice_shop'@'localhost'");
    let account = AccountName::parse("alice").expect("valid");
    let request = CreateDatabaseRequest {
        database: DatabaseName::for_account(&account, "shop").expect("valid"),
        user: DbUserName::for_account(&account, "shop").expect("valid"),
        password: Password::parse("Gen3rated-pw").expect("valid"),
    };
    let error = create_database(&host, &request).expect_err("must fail");
    assert!(matches!(error, DbError::AccessDenied));
    assert!(!format!("{error:?}").contains("Gen3rated-pw"));
}

#[test]
fn creating_a_database_that_already_exists_reports_already_exists_rather_than_failing() {
    // Idempotency, per the standing rule: repeating an operation converges.
    let host = FakeDbHost::with_existing("alice_shop");
    let account = AccountName::parse("alice").expect("valid");
    let request = CreateDatabaseRequest {
        database: DatabaseName::for_account(&account, "shop").expect("valid"),
        user: DbUserName::for_account(&account, "shop").expect("valid"),
        password: Password::parse("Gen3rated-pw").expect("valid"),
    };
    assert!(matches!(create_database(&host, &request), Err(DbError::AlreadyExists)));
}

#[test]
fn the_diagnostic_list_decodes_by_last_underscore_and_does_not_alias_a_sub_account() {
    // This is B1 as a test. `alice_bob_shop` belongs to account `alice_bob`, not `alice`. A
    // starts_with("alice_") filter would leak it. The decode-by-last-underscore + whole-account
    // match must NOT return it for `alice`.
    let host = FakeDbHost::with_existing_many(&["alice_shop", "alice_blog", "alice_bob_shop", "bob_shop", "mysql"]);
    let account = AccountName::parse("alice").expect("valid");
    let listed = list_databases(&host, &account).expect("listed");
    let names: Vec<&str> = listed.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(names, vec!["alice_blog", "alice_shop"]); // alice_bob_shop is NOT alice's
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-ops db`
Expected: FAIL — the module does not exist.

- [ ] **Step 3: Implement the four operations and the host trait**

`DbHost::execute(&self, statement: &str) -> Result<String, DbError>` is the whole seam. `ProcessDbHost` spawns `[mysql_client_binary, "--batch", "--skip-column-names", "--execute", statement]` with `Stdio::null()` for stdin and captured stdout — no shell, argv array, absolute path from the adapter.

`list_databases` filters on the prefix rather than asking MySQL for everything and trusting the caller to filter, because a listing that returns another tenant's names has already leaked them even if the UI hides them.

- [ ] **Step 4: Run and watch them pass**

- [ ] **Step 5: Mutation pass**

At minimum: drop the prefix filter in `list_databases` (a named test must go red); return the raw client stderr in `DbError` instead of a typed variant (the password test must go red); map `AlreadyExists` to a generic failure (the idempotency test must go red). Score against the whole workspace, never a subset.

---

### Task 4: `ops::sftp` — an OpenSSH SFTP user, chrooted

**Files:**
- Create: `agent/crates/ops/src/sftp/{mod,sftp_error,sftp_host,process_sftp_host,create_sftp_user,set_sftp_password,delete_sftp_user}.rs`, `ops/src/sftp/model/sftp_user_request.rs`
- Modify: `agent/crates/ops/src/lib.rs`
- Test: `agent/crates/ops/src/tests/sftp/{fake_sftp_host,create_sftp_user_tests,set_sftp_password_tests,delete_sftp_user_tests}.rs`

**Interfaces:**
- Consumes: `SftpUserName`, `Password`, `AccountName` from Task 1; `DistroAdapter::sftp_group` from Task 2; the jail base path `/var/lib/maran/sftp` as an `AgentPaths` constant in `agent-core/src/agent_paths.rs` (it is identical on every family, so it is an `AgentPaths` value, not a distro fact — per `agent/CLAUDE.md`).
- Produces:
  - `create_sftp_user(host: &dyn SftpHost, request: &SftpUserRequest) -> Result<(), SftpError>` — `SftpUserRequest { account, user: SftpUserName, password: Password }`. **Ensures the account's jail exists** (idempotent — see below), then creates the system user with a `nologin` shell, supplementary group `sftp_group`, **passwd home set to the jail** `/var/lib/maran/sftp/<account>`, then sets the password.
  - `set_sftp_password(host: &dyn SftpHost, user: &SftpUserName, password: &Password) -> Result<(), SftpError>` — via `chpasswd` reading `user:password` from **stdin**, argv array, no shell. The `Password` type guarantees the line cannot be broken by a newline and the value cannot inject.
  - `delete_sftp_user(host: &dyn SftpHost, user: &SftpUserName) -> Result<(), SftpError>` — `userdel` without `-r`. The jail and its mount are account-level and are removed by the account-deletion cascade (Task 12), not here.
  - `SftpError`: `AlreadyExists`, `NotFound`, `SpawnFailed { code: i32 }`, `PasswordRejected`, `JailFailed`.

**The jail, and why the home is never touched (the reviewer's I4, resolved as Option B).** OpenSSH refuses to chroot into a directory that is not root-owned or is group/world-writable, and plan 3 ships the account home as `<account>:web_server_group 0750`. Rather than change that home — an invariant Sites, nginx and php-fpm all depend on — each account gets a root-owned **jail** with its real home bind-mounted inside:

```
/var/lib/maran/sftp/<account>/        root:root 0755   ← the chroot; sshd accepts it, the user can list it
/var/lib/maran/sftp/<account>/home/   ← a bind mount of /home/<account>, with the home's own 0750 perms
```

sshd's `Match Group <sftp_group>` block (written once by the installer, Task 11) uses `ChrootDirectory %h`; the SFTP users' passwd home is the jail, so the user lands in the jail, enters `home`, and it is their real home — full access for the account, unchanged perms, unchanged nginx path. There is **no customer-supplied chroot path** and **no `resolve_in_home`**: the jail is fixed and root-owned, so the whole chroot-escape class is gone by construction, and the home model is untouched.

**The jail lifecycle is declarative and account-scoped, not imperative and per-user.** `create_sftp_user` ensures, idempotently, that the jail directory exists and that a per-account `systemd` bind-mount unit (`maran-sftp-<account>.mount`, `/home/<account>` → the jail's `home`) is installed and started. A `systemd .mount` unit is chosen over a bare `mount` call because it survives reboot by construction — an imperative mount would silently vanish on the next boot and break every SFTP login for that account. The jail and the mount are removed by the `AccountDeleting` cascade (Task 12), the same cascade that drops the databases: the mount is an account resource with account lifecycle. A mount left behind after deletion is a leak, so the cascade's unmount is a tested step.

- [ ] **Step 0 (polygon proof, not a design spike): confirm the jail works end to end**

The design above is decided, not open. This step **proves** it on the Ubuntu polygon before the code depends on it: create an account home exactly as plan 3 leaves it, build the jail + bind mount, add the `Match Group` block, and assert three things on the real host — an SFTP login lands in the jail and can read/write the real home; the same account's nginx site still serves (the home was not touched); and a chrooted user cannot reach `/etc` or another account's files. Record the transcript in the ledger. If any of the three fails, that is a finding that stops the task, not a guess to paper over.

- [ ] **Step 1: Write the failing tests against a fake host**

```rust
#[test]
fn creating_an_sftp_user_puts_it_in_the_chroot_group_with_a_nologin_shell() {
    let host = FakeSftpHost::new();
    let account = AccountName::parse("alice").expect("valid");
    let request = SftpUserRequest {
        account: account.clone(),
        home: account.clone(),
        user: SftpUserName::for_account(&account, "web").expect("valid"),
        password: Password::parse("Gen3rated-pw").expect("valid"),
    };

    create_sftp_user(&host, &request).expect("created");

    let created = host.created_user().expect("a user was created");
    assert_eq!(created.name, "alice_web");
    assert!(created.groups.contains(&"maran-sftp".to_owned()), "must be in the chroot group");
    assert!(created.shell.ends_with("nologin"), "an SFTP user must not get a shell");
}

#[test]
fn the_password_is_set_over_stdin_and_never_appears_in_an_argument_vector() {
    // The leak this closes: a password on a command line is visible in `ps` to every local
    // user. chpasswd reads it from stdin; the argv array carries only ["chpasswd"].
    let host = FakeSftpHost::new();
    /* create, then inspect */
    let spawn = host.last_spawn().expect("a process was spawned to set the password");
    assert_eq!(spawn.argv, vec!["/usr/sbin/chpasswd"]);
    assert!(spawn.stdin.contains("alice_web:"));
    assert!(!spawn.argv.iter().any(|a| a.contains("Gen3rated-pw")));
}

#[test]
fn a_password_the_type_forbids_cannot_break_the_chpasswd_line() {
    // A newline in the password would inject a second `user:password` line into chpasswd's
    // stdin — i.e. set another account's password. Password::parse forbids the newline, so
    // there is no such value to pass.
    assert!(Password::parse("pw\nroot:owned").is_err());
}

#[test]
fn creating_a_user_that_already_exists_reports_already_exists() {
    let host = FakeSftpHost::with_existing("alice_web");
    /* … */
    assert!(matches!(create_sftp_user(&host, &request), Err(SftpError::AlreadyExists)));
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `source scripts/dev && cd agent && cargo test -p maran-ops sftp`
Expected: FAIL — the module does not exist.

- [ ] **Step 3: Implement, and keep the fork/drop discipline where it belongs**

`create_sftp_user` runs `useradd`/`usermod`/`chpasswd` as root (managing system accounts is root's job and is not done as the customer). There is no `fork_as_account` here because nothing writes into the customer's home in this task — the home already exists, made by account creation. `SftpHost::spawn(argv, stdin)` is the seam; `ProcessSftpHost` uses absolute paths from the adapter, `Stdio::piped()` for stdin, and never a shell.

- [ ] **Step 4: Run and watch them pass**

- [ ] **Step 5: Mutation pass**

Drop the `sftp_group` membership and confirm the group test goes red. Put the password on the argv instead of stdin and confirm the stdin test goes red. Map `AlreadyExists` to a generic failure and confirm idempotency goes red. Each alone, scored against the whole workspace.

---

### Task 5: The agent's services, and the polygon that proves them

**Files:**
- Create: `agent/crates/agent/src/services/db/{db_service,db_status}.rs`, `agent/src/services/sftp/{sftp_service,sftp_status}.rs`
- Create: `agent/crates/agent/tests/databases_on_a_real_host.rs`, `agent/crates/agent/tests/ftp_on_a_real_host.rs`
- Modify: `agent/crates/agent/build.rs` (compile `db.proto` and `ftp.proto`), `src/server.rs`, `src/services/mod.rs`, `tests/handshake.rs`
- Modify: `docker/polygon/ubuntu24.Dockerfile`, `alma9.Dockerfile` (mariadb-server and openssh-server, started in the test's fixture rather than baked as running)

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces: `DbService` answering the RPCs in `proto/agent/v1/db.proto`, and `SftpService` answering `proto/agent/v1/ftp.proto`. `ftp.proto` needs an **additive** edit: its create message loses the FTPS/virtual-user framing (no `chroot_path` field — the jail is the home) and the reason is recorded; `db.proto`'s header comment is corrected (I1) so it no longer says to persist the password.

- [ ] **Step 1: Wire both services and extend the handshake test**

`handshake.rs` already drives a real socket; it gains one call per new service asserting the service is reachable and answers a typed error rather than `UNIMPLEMENTED`.

- [ ] **Step 2: Write the polygon tests**

```rust
#[test]
#[ignore = "creates a real database on a real MariaDB: polygon only"]
fn a_database_created_by_the_agent_is_visible_to_the_real_mysql_client() { /* … */ }

#[test]
#[ignore = "connects with the created credentials: polygon only"]
fn the_created_db_user_can_connect_with_the_generated_password_and_see_only_its_own_database() {
    // Proves the password reached MySQL intact (no truncation, no injection-mangling) AND that
    // grants are scoped: the user connects, sees alice_shop, and cannot USE bob_shop.
}

#[test]
#[ignore = "creates a real system account and drops it: polygon only"]
fn an_sftp_user_logs_in_and_is_jailed_in_its_own_home_and_cannot_reach_another_accounts() {
    // The test that matters. A real sftp session against the real sshd: the user lands in its
    // home, and `cd /etc` / `cd /home/otheraccount` fail. Assert the refusal, never that a
    // config file contains the word ChrootDirectory.
}

#[test]
#[ignore = "creates a real system account: polygon only"]
fn an_sftp_user_gets_no_shell_even_over_ssh_exec() {
    // ForceCommand internal-sftp + nologin: `ssh alice_web whoami` must fail, not run.
}
```

- [ ] **Step 3: Prove the ignored tests fail loudly outside the polygon**

They must refuse, not skip. Plan 3 found one of twelve unguarded and passing quietly outside any container.

- [ ] **Step 4: Mutation pass, including the images**

Remove the `Match Group` block the installer writes and confirm the jail test goes red **on the real host** (the user gets a full session). Confirm the images create nothing the installer is supposed to create — the images `COPY` and run the installer's `85-mysql.sh`/`86-sftp.sh`, exactly as `80-nginx.sh` is now driven, so if the installer stops doing it the image build fails. This is the direct answer to Plan 3's worst gap.

---

## Phase B — the panel

### Task 6: Agent clients for Db and Sftp

**Files:**
- Create: `backend/src/Maran.Agent.Client/Interfaces/{IAgentDbClient,IDbServiceInvoker,IAgentSftpClient,ISftpServiceInvoker}.cs`
- Create: `backend/src/Maran.Agent.Client/Services/DbService/{AgentDbClient,GrpcDbServiceInvoker,CreatedDatabaseDto,DatabaseSummaryDto}.cs`
- Create: `backend/src/Maran.Agent.Client/Services/SftpService/{AgentSftpClient,GrpcSftpServiceInvoker}.cs`
- Create: `backend/src/Maran.Host/Resilience/{ResilientAgentDbClient,ResilientAgentSftpClient}.cs`
- Modify: `Maran.Agent.Client/DependencyInjection.cs`, `Maran.Host/Extensions/ResilienceExtensions.cs`, `Maran.Host.Tests/Composition/ContainerResolutionTests.cs`, `HostedServiceResolutionTests.cs`
- Test: `backend/tests/Maran.Agent.Client.Tests/Services/{DbService,SftpService}/`

**Interfaces:**
- Consumes: `AgentErrorTranslator` — the single wire-error boundary. Do not add a second path around it.
- Produces: `IAgentDbClient` with `CreateAsync`, `DropAsync`, `ListAsync`, `GetSizeAsync`; `IAgentSftpClient` with `CreateAsync`, `SetPasswordAsync`, `DeleteAsync`. Both resolved already wrapped in their resilience pipelines.

**The C# password carrier must not leak either (the reviewer's B2/M1, C# side).** The Rust `Password` type does not cross the wire as a type — it is a plain `string` in the proto. So on the C# side the generated password flows as a `string` through `CreateAsync`, the request DTO and the command. A C# `record` auto-generates a `ToString()` over every property, so a request record carrying a `Password` property that reaches a log leaks it — the plan-3 private-key-in-log shape. Therefore: the C# type that carries a password to the agent is **not** a plain record with a `Password` property; it either omits the password from its generated `ToString()` (a hand-written `ToString`, or a dedicated non-printing `SensitiveString` wrapper) and a test proves `ToString()`/`ToString()`-via-interpolation does not contain the value. The reviewer's M1 also applies: the realistic leak vector is the agent's `tool_output`, not a `using password: YES` line, so `AgentErrorTranslator`'s redaction must be verified against a value the panel actually sent, and the redaction test is expected to fail first and drive the widening (a value the panel just generated is one it can recognise and strip).

- [ ] **Step 1: Write the failing tests, driving the production entry point**

Assert the **request the stub received**, field by field, for every RPC. Plan 3's review found sixteen surviving mutations because request mapping was untested for every RPC but one, and the stub even exposed a captured request that no test read.

```csharp
[Fact]
public async Task Create_sends_the_account_the_database_the_user_and_the_password()
{
    var stub = new StubDbService();
    var client = new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance);

    await client.CreateAsync("alice", "shop", "shop", "generated", CancellationToken.None);

    Assert.Equal("alice", stub.LastCreateRequest!.AccountUsername);
    Assert.Equal("shop", stub.LastCreateRequest.DatabaseName);
    Assert.Equal("shop", stub.LastCreateRequest.DbUsername);
    Assert.Equal("generated", stub.LastCreateRequest.Password);
}

[Fact]
public async Task The_generated_password_is_passed_to_the_agent_and_never_written_to_the_log()
{
    // The realistic leak is the agent quoting the password back in an error. AgentErrorTranslator
    // strips PEM blocks and long base64 runs; a database password is neither, so this test is
    // the one that says whether the redaction covers what this client actually sends.
    var recorder = new RecordingLogger<AgentDbClient>();
    var stub = StubDbService.FailingWith(ErrorCode.SystemFailure, "Access denied for 'alice_shop' using password 'generated'");
    var client = new AgentDbClient(stub, recorder);

    await client.CreateAsync("alice", "shop", "shop", "generated", CancellationToken.None);

    Assert.DoesNotContain("generated", recorder.Text);
}
```

**That second test is expected to FAIL on first run,** and its failure is a finding, not a nuisance: `AgentErrorTranslator` redacts PEM and long base64 runs, and a generated database password matches neither. Widen the redaction to cover it — the natural rule is that a value the panel just sent is a value it can recognise and strip — and prove the widening with its own mutation.

- [ ] **Step 2: Run, watch the mapping tests fail and the redaction test fail for its own reason**

- [ ] **Step 3: Implement both clients and their decorators**

- [ ] **Step 4: Add both to `ContainerResolutionTests` and assert the pipeline does something**

Not that it is wired — what it **does**. Plan 3 found `DeleteAsync` bypassing its pipeline entirely, with no timeout, passing 59 of 59 tests.

- [ ] **Step 5: Mutation pass**

Every field of every request, swapped and dropped; the decorator bypassed; the redaction removed.

---

### Task 7: The Databases module

**Files:**
- Create: `backend/src/Maran.Modules/Databases/` — `DatabasesModule.cs`, `DatabasesManifest.cs`, `Domain/Database.cs`, `Persistence/{DatabasesDbContext,DesignTimeDbContextFactory}.cs`, `Persistence/Configurations/DatabaseConfiguration.cs`, a migration, `Commands/{CreateDatabase,DropDatabase,ResetDatabasePassword}/`, `Queries/{ListDatabases,GetDatabase}/`, `Controllers/DatabasesController.cs`, `Resources/ErrorMessages{,.ru,.hy}.resx`
- Modify: `Maran.Modules/Accounts/Domain/Plan.cs` (+`MaxDatabases`), its configuration, seeder and migration; `Maran.Host/Modules/ModuleRegistry.cs`; `Maran.sln`; `Maran.Host.csproj`; `Maran.ArchitectureTests.csproj`; `Maran.Sdk/Contracts/AuditActions.cs`
- Test: `backend/tests/Maran.Modules.Databases.Tests/`, `backend/tests/Maran.Host.IntegrationTests/DatabasesAuthorizationTests.cs`

**Interfaces:**
- Consumes: `IAccountDirectory` (`AccountSnapshot(Guid Id, string Username, int MaxSites, int MaxPhpWorkersPerPool)` — gains `MaxDatabases`), `IAgentDbClient`, `IAuditWriter`.
- Produces: `Database` with `Id`, `AccountId`, `Name` (the **requested** name, unprefixed), `FullName` (what MySQL holds), `DbUserName`, `CreatedAt`. No password column, of any kind.

**Ordering, and the compensation the reviewer's I3 requires.** Agent first, row second — the shape Plan 3 settled on. A refused creation leaves no row. But the reverse gap is real: if the agent creates the database and then the **row insert** fails for anything other than a duplicate (a transient DB error), the customer has a live database with a lost password and no row — and a retry hits the agent's `AlreadyExists`, which does not reset the password. So a row-insert failure after a successful agent create must **compensate** by dropping the just-created database (best effort, logged) so the retry is clean, or the create flow must be able to reconcile an orphan on the `AlreadyExists` path by resetting the password and writing the row. Pick one, state which, and test it — a test that kills the row insert and asserts no orphan survives. And do not repeat plan 3's Ssl wholesale `catch (DbUpdateException)`: catch **SqlState 23505 only** for the duplicate path.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task A_database_beyond_the_plans_allowance_is_refused_before_the_agent_is_called_at_all()

[Fact]
public async Task A_name_another_tenant_already_uses_is_still_available_because_names_are_prefixed()
// alice_shop and bob_shop coexist. This is the test that says the prefix works end to end.

[Fact]
public async Task Reading_another_tenants_database_answers_not_found_rather_than_forbidden()

[Fact]
public async Task A_refused_provisioning_leaves_no_database_row_behind()

[Fact]
public async Task A_row_insert_that_fails_after_the_agent_created_the_database_leaves_no_orphan()
// I3: kill the row insert with a non-23505 error; assert the database was dropped (or reconciled)
// so a retry does not hit a live database with a lost password and no row.

[Fact]
public async Task The_generated_password_is_returned_once_and_is_absent_from_every_later_read()
// Create returns it. GetDatabase and ListDatabases must not carry it, and no column holds it.

[Fact]
public async Task A_database_failure_that_is_not_a_duplicate_is_not_reported_as_a_name_already_taken()

[Fact]
public async Task Listing_databases_returns_the_panels_own_tenant_rows_and_never_asks_the_agent_to_enumerate()
// B1 at the panel layer: authorisation is the tenant query filter over the module's rows, not a
// prefix scan on the host. Assert the agent's ListDatabases is not called on a list request.
```

- [ ] **Step 2: Run and watch them fail**

- [ ] **Step 3: Implement the module**

Password generation lives in the panel, not the agent: the panel is what shows it to the operator, and a value generated where it is displayed has one fewer hop to leak from. Generate from the **same injection-safe alphabet** the agent's `Password` type accepts, with `RandomNumberGenerator`, not `Random` — a password the agent would reject is a create that fails after the UI already promised one.

- [ ] **Step 4: Run and watch them pass**

- [ ] **Step 5: `ResetDatabasePassword` (the reviewer's I2)**

With "shown once, never stored", reset is the **only** recovery for a lost password, so it is a first-class command, not a footnote. It generates a new password, calls the agent to `ALTER USER … IDENTIFIED BY`, returns the new password once, and writes no copy. Its own IDOR test (another tenant's database → 404), its own audit entry, its own mutation. Tenant-scoped like every other command.

- [ ] **Step 6: `Plan.MaxDatabases`, with a migration that backfills real values**

Plan 3 shipped a migration that backfilled `0` and a seeder that only inserted absent plans, so every existing installation got a limit of zero. Backfill 2 / 10 / 50 for Starter / Business / Unlimited, pin the numbers with a test, and make the constructor refuse a negative allowance.

- [ ] **Step 7: Mutation pass**

The tenant query filter; the limit check; the ordering AND the compensation; the 23505 narrowing; the prefix; the absence of a password column; the list-from-rows guarantee; the reset's IDOR.

---

### Task 8: The Sftp module

**Files:**
- Create: `backend/src/Maran.Modules/Sftp/` — the same shape as Task 7, with `Domain/SftpUser.cs` carrying `Id`, `AccountId`, `Name` (requested, unprefixed), `FullName` (the system user), `CreatedAt`, and no password column
- Modify: `Plan.cs` (+`MaxSftpUsers`), `ModuleRegistry.cs`, `Maran.sln`, `AuditActions.cs`
- Test: `backend/tests/Maran.Modules.Sftp.Tests/`, `backend/tests/Maran.Host.IntegrationTests/SftpAuthorizationTests.cs`

**Interfaces:**
- Consumes: `IAccountDirectory` (gains `MaxSftpUsers`), `IAgentSftpClient`, `IAuditWriter`.
- Produces: `SftpUser` and its DTOs.

**No chroot field, and therefore no `ISiteDirectory` (the reviewer's F5).** The earlier draft made the panel validate an FTP chroot against a site's document root via `ISiteDirectory`, which was both more restrictive than the agent (which allows any in-home path) and coupled this module to Sites for no reason. With SFTP the jail is always the account's own home — OpenSSH's `ChrootDirectory %h` — so there is no chroot to choose, no path from the customer, and no cross-module dependency. The commands are create (name + generated password), reset password, delete. This is strictly simpler and closes the mismatch the reviewer flagged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task An_sftp_user_beyond_the_plans_allowance_is_refused_before_the_agent_is_called()

[Fact]
public async Task A_name_another_tenant_uses_is_still_available_because_names_are_prefixed()

[Fact]
public async Task Reading_another_tenants_sftp_user_answers_not_found_rather_than_forbidden()

[Fact]
public async Task The_generated_password_is_returned_once_and_is_absent_from_every_later_read()

[Fact]
public async Task Resetting_a_password_returns_a_new_one_once_and_stores_no_copy()

[Fact]
public async Task Deleting_an_sftp_user_removes_the_row_only_after_the_agent_confirms()
```

- [ ] **Step 2–4: Run, implement, run**

Password generation and the shown-once flow are identical to Task 7 (same injection-safe alphabet, `RandomNumberGenerator`, no stored copy). Agent-first/row-second with the same I3 compensation: a row-insert failure after the agent created the user drops the user so a retry is clean.

- [ ] **Step 5: Mutation pass**

The tenant query filter; the limit check; the ordering and the compensation; the prefix; the absence of a password column; the reset's IDOR. No masking pair here — removing `ISiteDirectory` removed the two-checks-that-mask-each-other the earlier draft had, which is itself the cleaner outcome.

---

### Task 9: Wiring, and the IDOR fixture extended

**Files:**
- Modify: `Maran.Host/Modules/ModuleRegistry.cs`, `Maran.Host.IntegrationTests/Fixtures/ControllerRoutes.cs`
- Test: `Maran.Host.IntegrationTests/{DatabasesAuthorizationTests,SftpAuthorizationTests}.cs`, `Maran.Host.Tests/Composition/ControllerActivationTests.cs`

- [ ] **Step 1: Register both modules and both test projects**

Without the architecture-test project reference, NetArchTest never sees the module and `ModuleCoverageTests` fails — the gap Plan 3's brief also missed.

- [ ] **Step 2: Enumerate every route in both fixtures**

`Every_site_scoped_route_on_the_controller_is_covered_by_the_idor_fixture` already reads routes by reflection; the two new controllers get the same treatment, so a route added later fails naming itself.

- [ ] **Step 3: Prove the fixture fails**

Temporarily remove the query filter from each new `DbContext`, watch the cross-tenant test go red, put it back. This step is in the plan because Plan 3's equivalent step is the only reason its fixture was ever proved.

- [ ] **Step 4: Assert both new contexts are scoped, and every controller activates**

A Singleton `DbContext` passed all 452 tests in Plan 3 until a test resolved it twice from one scope and once from another.

---

## Phase C — the panel's screens

### Task 10: Data layer and screens

**Files:**
- Create: `frontend/src/types/{database,sftpUser}.ts`, `composables/apis/{useDatabasesApi,useSftpApi}.ts`, `stores/{databases,sftp}.ts`, `pages/databases/DatabasesPage.vue`, `components/databases/DatabaseCreatedDialog.vue`, `pages/sftp/SftpUsersPage.vue`, `components/sftp/SftpUserCreatedDialog.vue`, `locales/{en,ru,hy}/{databases,sftp}.json`
- Create: `frontend/e2e/databases/{list,create,credentials}.spec.ts`, `frontend/e2e/sftp/{list,create,credentials}.spec.ts`
- Modify: `frontend/src/router/index.ts`, `frontend/src/utils/moduleNavigationIcon.ts`

**Interfaces:**
- Consumes: the API from Tasks 7–9.
- Produces: two routes and two stores. `useApi` and the `apis` composables are called **from stores only**, never from a component.

**The one-time credential is the whole design problem of this task.** The password exists for exactly one render. It must be shown clearly, be copyable, and be gone on any navigation or reload — and the screen has to say so before the operator closes it, because there is no second chance and no support path that recovers it.

- [ ] **Step 1: Write the failing Playwright specs**

```ts
test('the generated password is shown once and is gone after a reload', async ({ page }) => { … })
test('the dialog says plainly that the password cannot be shown again', async ({ page }) => { … })
test('a database another tenant owns is not listed', async ({ page }) => { … })
test('the create form refuses a name the panel can reject without asking the server', async ({ page }) => { … })
test('the panel shows the prefixed name so the operator can find it in mysql', async ({ page }) => { … })
```

That last one matters more than it looks: an operator who reads `shop` in the panel and types `shop` in a mysql client gets "unknown database". The screen shows `alice_shop` and says the prefix is the account's.

- [ ] **Step 2: Run and watch them fail**

Run: `cd frontend && npx playwright test e2e/databases`

- [ ] **Step 3: Build the stores, then the screens**

Icons come from `UiIcon`'s named scale (`sm` 14 / `md` 18 / `lg` 22); the numeric prop was removed deliberately. Controls inherit the kit's sizing — do not add per-page padding overrides, which is how the header ended up 34px against the form's 42px.

- [ ] **Step 4: Run, and check both themes and a narrow viewport**

- [ ] **Step 5: Add navigation entries**

`moduleNavigationIcon.ts` states the glyph per module — `databases` and `sftp` get their own rather than falling back to the neutral `grid`, which is what made three entries identical before. The SFTP screen shows the prefixed system-user name for the same reason the database screen does: an operator connecting with an SFTP client types `alice_web`, not `web`.

- [ ] **Step 6: Mutation pass, such as the SPA allows**

Say plainly which protections no check can catch. `rules/testing.md` records that the SPA has no unit runner by design; an honest "no check catches this" is worth more than a green tick, and Plan 3's frontend tasks were the first to state it.

---

## Phase D — proving it on a real host

### Task 11: The installer, and the polygon that proves the installer

**Files:**
- Create: `installer/lib/85-mysql.sh`, `installer/lib/86-sftp.sh`
- Modify: `installer/install.sh`, `installer/uninstall.sh`, `docker/polygon/{ubuntu24,alma9}.Dockerfile`, `docker/README.md`
- Test: the polygon suites from Task 5

**What the installer must do, because nothing else will.**
- Ensure MariaDB is installed and running, and that `root@localhost` authenticates over the unix socket. If it does not — an operator who set a root password by hand — **fail loudly with what to do**, rather than storing a password to work around it.
- Create the `maran-sftp` group and the jail base directory `/var/lib/maran/sftp` at `root:root 0755`.
- Add **one** idempotent `Match Group maran-sftp` block to sshd_config with `ChrootDirectory %h`, `ForceCommand internal-sftp`, `AllowTcpForwarding no`, `X11Forwarding no`. Reload sshd. A re-run must not duplicate the block (guard on a marker comment). The home the SFTP users chroot into is their **jail** (`/var/lib/maran/sftp/<account>`, set as their passwd home), not `/home/<account>` — so the installer does **not** touch home ownership, and plan 3's home model is untouched. The jail's `home/` mountpoint and the per-account `systemd` bind-mount unit are created by the agent's first `create_sftp_user` (Task 4), not the installer; the installer only lays the base directory and the sshd block.

- [ ] **Step 1: Write the installer steps, idempotent by construction**

- [ ] **Step 2: Make the polygon prove the installer, not replace it**

The images `COPY` these scripts and run their functions, exactly as `80-nginx.sh` is now driven, then assert the result. If the installer stops doing any of it, both image builds fail and no polygon suite runs. This is the direct answer to Plan 3's worst gap, where the images manufactured the precondition the installer forgot and the golden path inherited it.

- [ ] **Step 3: Mutation pass on the installer**

Remove each step and confirm the image build fails at its assertion.

---

### Task 12: Account-deletion cascade (the reviewer's B3)

**Files:**
- Create: `backend/src/Maran.Sdk/Events/AccountDeleting.cs`
- Modify: `Maran.Modules/Accounts/Commands/DeleteAccount/DeleteAccountCommandHandler.cs` (publish `AccountDeleting` and abort on a cleanup failure); `Maran.Modules/Databases/` and `Maran.Modules/Sftp/` (a handler each); `agent/crates/ops/src/accounts/delete_account.rs` (drop the account's databases and remove its SFTP membership, where pool cleanup already lives)
- Test: `backend/tests/Maran.Host.IntegrationTests/AccountDeletionCascadeTests.cs`, the polygon suites

**Why this is a task and not a footnote.** `userdel` does not touch MySQL or sshd. Today `DeleteAccountCommandHandler` removes only the system account (via the agent) and the panel's `Account` row — so deleting `alice` orphans every `alice_*` database and her SFTP membership on the host. A re-created `alice` then **inherits the prior tenant's live data and logins**. This is plan 3's pool-leak class with a cross-tenant-credential twist, and it is exactly the kind of defect that passes every unit test and is found only by driving a real host.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Deleting_an_account_removes_its_database_rows_and_its_sftp_rows()

[Fact]
public async Task A_cleanup_failure_aborts_the_deletion_and_leaves_the_account_recoverable()
// A half-deleted account is recoverable; an orphaned database is not. So if dropping the
// databases fails, the account is NOT deleted, and the operator sees why.
```

```rust
#[test]
#[ignore = "creates and deletes a real account with a real database: polygon only"]
fn a_deleted_account_leaves_no_database_and_a_recreated_account_of_the_same_name_inherits_nothing() {
    // The test that matters. Create alice + alice_shop, delete alice, re-create alice, and assert
    // `mysql` shows no alice_shop and the new alice cannot log in with the old SFTP credentials.
}
```

- [ ] **Step 2: Run, implement, run**

The agent's `delete_account` drops the databases, removes the account's SFTP users, and **stops and removes the per-account bind-mount unit and the jail directory** (`/var/lib/maran/sftp/<account>`) in the same operation that removes the pools — one host-side cleanup, ordered before `userdel`. The unmount is not best-effort: a bind mount left after deletion is a mount of a now-`userdel`'d home into a lingering jail, so it is a tested step, and a re-created account of the same name gets a fresh jail, never the old mount. The panel publishes `AccountDeleting`; the Databases and Sftp modules handle it to delete their rows; the Accounts handler treats a cleanup failure as a hard stop. Modules communicate only through the Sdk event — no `Accounts → Databases` reference, which `ModuleIsolationTests` forbids.

- [ ] **Step 3: Mutation pass**

Remove the database drop from `delete_account` and confirm the polygon test goes red (a re-created account inherits the old database). Make the cleanup failure non-fatal and confirm the "abort" test goes red.

---

### Task 13: The Definition of Done pass

**Files:**
- Modify: whatever the gaps turn out to need
- Test: across all three stacks

- [ ] **Step 1: Every typed error variant of both features appears in at least one test**

`DbError` (`AlreadyExists`, `NotFound`, `ClientFailed`, `Unparsable`, `AccessDenied`) and `SftpError` (`AlreadyExists`, `NotFound`, `SpawnFailed`, `PasswordRejected`) — count them and cover them, or state why a variant is unreachable, and remember that "unreachable" is a claim requiring evidence.

- [ ] **Step 2: Idempotency, proved**

Create a database twice → `AlreadyExists`. Drop twice → `NotFound`. Create an SFTP user twice → `AlreadyExists`; delete twice → `NotFound`; reset a password twice → success. This is the standing rule that makes retries safe.

- [ ] **Step 3: Extend the golden path**

Spec §16's path is install → account → site → SSL → **file → database** → cron → suspend. This plan owns the database step: `frontend/e2e/golden-path/account-to-ssl.spec.ts` gains database creation against the real stack, with the one-time credential asserted and then a real `mysql` connection made with it inside the polygon. A credential the panel shows and the server rejects is the defect this step exists to catch. It also gains an SFTP login with the shown-once password, jailed in the home.

- [ ] **Step 4: The threat note**

`docs/superpowers/notes/` gains this plan's note, and `rules/security.md` requires it to name **what is left open**, not only what is safe. Candidates already visible: the agent's MySQL access is root-over-socket and anything that can run as root can use it; a dropped database is not backed up first; an SFTP user is a real system account, so the account's `systemd` slice and process limits, not this plan, are what bound what it can do once logged in; and phpMyAdmin and FTPS are deferred, so operators who need them have neither yet.

- [ ] **Step 5: Every gate, on this tree**

All of them, with the per-project totals summed against the baseline — `dotnet test` prints `Passed!` for a crashed run with a smaller total.

---

## Self-review

**1. Spec coverage.** §11 Databases — Tasks 3, 6, 7, 10, 13. §11 SFTP (the spec's default; FTPS deferred) — Tasks 4, 6, 8, 10, 11, 13. §8 limits — Tasks 7 and 8 (`MaxDatabases`, `MaxSftpUsers`). §15 audit — every mutating command in Tasks 7, 8 and 12. §16 golden path — Task 13. Account-deletion cleanup, which the reviewer found missing — Task 12.

**Gaps I am recording rather than hiding.** Two subsystems the spec's §11 lists are **deferred** to their own plans, and both deferrals are decisions, not omissions: **phpMyAdmin** ("an optional module on an isolated vhost with a one-time SSO token") is a separate deployable with its own vhost, authentication and licence question; and **FTPS** (vsftpd, "TLS обязателен") is a different provisioning path from the SFTP this plan ships, with its own certificate story. SFTP is the spec's default, so shipping it first is correct; an operator who specifically needs FTPS does not have it yet, and the threat note (Task 13) says so. The one architectural risk I am carrying openly is the SFTP chroot-vs-home-ownership reconciliation (Task 4 Step 0 spike, applied in Task 11): it may require changing plan-3's home ownership, and if it does, that change ships here with a test that nginx still serves.

**2. Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N". Every test block names its assertion. Three steps deliberately say the test is **expected to fail first** and why — Task 4 Step 0's chroot spike, Task 6's redaction test, and Task 9's fixture proof — because a step that only ever passes proves nothing.

**3. Type consistency.** `DatabaseName::for_account` / `DbUserName::for_account` / `SftpUserName::for_account` are the same shape throughout, and `Password::parse` / `Secret::new` are used consistently (a value in root SQL or a chpasswd line is a `Password`; a value merely carried is a `Secret`). `as_str()` everywhere returns the prefixed value. `AccountSnapshot` gains `MaxDatabases` and `MaxSftpUsers` in Tasks 7 and 8 and is referenced with those names in both. `DbHost::spawn`/`execute` and `SftpHost::spawn` are named consistently in Tasks 3, 4 and 5. The FTPS-modelled-as-a-field paragraph an earlier draft had here is gone with the SFTP-only ruling — there is no `Protocol` field and no `ChrootPath` anywhere in the plan.

**Revision note.** This plan was revised after a pre-execution adversarial review (recorded in `.superpowers/sdd/2026-09-01-maran-databases-sftp/progress.md`). Four blocking findings were fixed before any subagent trusts it: the list/drop tenant boundary moved onto the panel's rows (B1); a validated `Password` type now guards the one input that reaches root SQL (B2); an account-deletion cascade was added as Task 12 (B3); and the FTP scope was reduced to SFTP-only to match the spec's default and the committed contract (B4). Six important findings and two self-contradictions in the original draft were fixed in the same pass.
