# Rust Rules (agent)

Normative. The agent runs as root: these rules are security controls, not taste.
A PR violating a MUST/MUST NOT is rejected regardless of how well it works.

## Toolchain & lints

- Stable Rust, edition 2024. `rustfmt` defaults — never hand-format against it.
- CI runs `cargo clippy --all-targets --all-features -- -D warnings`.
- Lints are declared **once**, in the workspace manifest. A crate that repeats a
  workspace lint in its own root is drift waiting to happen:

```toml
[workspace.lints.rust]
missing_docs = "warn"
unsafe_code  = "deny"

[workspace.lints.clippy]
unwrap_used = "deny"
expect_used = "deny"
panic       = "deny"
```

Every crate opts in with `[lints] workspace = true`.

`unwrap`/`expect`/`panic!` are allowed only in tests and build scripts. Agent code
returns errors; a root process MUST NOT panic on input.

`unsafe_code` is `deny` rather than `forbid` at the workspace level on purpose:
`forbid` cannot be lowered by a crate, and `agent-core::privs` needs exactly one
documented exception. Every crate root except `agent-core` therefore adds
`#![forbid(unsafe_code)]`; see "unsafe" below.

## Errors

- Library crates (`agent-core`, `ops`, `distro`, `templates`): `thiserror` enums,
  one per domain, marked `#[non_exhaustive]`.
- Binary boundary (`agent`): map to the proto `Error` message; `anyhow` only in
  `main` startup.

```rust
/// Errors returned by site operations.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SitesOpError {
    /// The rendered config failed `nginx -t`; the previous config was restored.
    #[error("nginx validation failed: {stderr}")]
    NginxValidation { stderr: String },
    /// Domain does not match the allowed pattern.
    #[error("invalid domain name")]
    InvalidDomain,
}
```

An error enum never crosses a crate boundary by being re-thrown as-is from an
unrelated domain. `ops::sites` returns `SitesOpError`; if it calls `ops::php`,
the `PhpOpError` is wrapped in a `SitesOpError` variant with `#[from]`, so the
caller reads one exhaustive list of what site work can fail on.

Error text is for operators and logs. Customer-facing wording is produced by the
C# side from the typed variant (rules/security.md, role-aware errors) — the agent
never formats a message intended for a hosting customer, and never puts a path,
a version, or tool output into a variant that will reach one.

## Doc comments — mandatory for ALL items

`///` docs are REQUIRED on **every item regardless of visibility** — `pub` and
private: functions, structs, enums, traits, modules, non-obvious fields. What it
does, invariants, and error conditions. Test code is exempt (behavior-sentence
test names). `cargo doc` warnings are errors on CI
(`RUSTDOCFLAGS="-D warnings"`), and `missing_docs` is set workspace-wide.

Every fallible `pub` function carries an `# Errors` section naming the conditions,
not just the type. "Returns `PathError`" is not documentation; "returns
`PathError::EscapesHome` when the resolved path leaves the account's home" is.

## Canonical agent layout

This tree is the map. A file goes where the map assigns it; a NEW kind of file
first gets its named place here, in the same PR that introduces it.

```
agent/
├── Cargo.toml                 workspace: members, shared versions, lints
├── Cargo.lock
├── rustfmt.toml
└── crates/
    ├── agent/                 bin: maran-agent (library + thin main, so integration
    │   │                      tests can start a real server in-process)
    │   ├── build.rs           compiles proto/agent/v1/ via tonic-build
    │   ├── src/
    │   │   ├── lib.rs · main.rs · server.rs · error.rs
    │   │   ├── config/        agent_options.rs · current_uid.rs · options_error.rs
    │   │   ├── peercred/      peer_policy.rs (who may connect) · peer_guard.rs (the check)
    │   │   └── services/      one FOLDER per proto service:
    │   │       ├── system/    system_service.rs
    │   │       ├── accounts/  accounts_service.rs · account_status.rs
    │   │       ├── sites/     sites_service.rs · site_status.rs
    │   │       ├── db/        db_service.rs · db_status.rs
    │   │       ├── files/     files_service.rs · file_status.rs
    │   │       ├── ftp/       ftp_service.rs · ftp_status.rs
    │   │       ├── cron/      cron_service.rs · cron_status.rs
    │   │       ├── firewall/  firewall_service.rs · firewall_status.rs
    │   │       ├── ssl/       ssl_service.rs · ssl_status.rs
    │   │       ├── backup/    backup_service.rs · backup_status.rs
    │   │       └── monitor/   monitor_service.rs · monitor_status.rs
    │   ├── src/tests/         unit tests, mirroring src/ (rules/testing.md)
    │   └── tests/             integration tests (+ fixtures/)
    ├── agent-core/
    │   └── src/
    │       ├── validation/    name.rs · name_error.rs · path.rs · path_error.rs ·
    │       │                  domain.rs · domain_error.rs · port.rs · port_error.rs ·
    │       │                  ip_address.rs · ip_address_error.rs ·
    │       │                  cron_expression.rs · cron_expression_error.rs
    │       ├── privs/         the ONLY home of unsafe syscall/setuid wrappers:
    │       │                  fork_as_account.rs · account_ids.rs · priv_error.rs
    │       └── utils/         helpers that carry no domain knowledge, one file per
    │                          subject: directory.rs · current_uid.rs. A helper earns a
    │                          place here when a SECOND crate needs it; until then it
    │                          stays private beside its only caller. The banned shape is
    │                          the catch-all (util.rs, helpers.rs, misc.rs), not the folder.
    ├── distro/
    │   └── src/
    │       ├── adapter.rs     the DistroAdapter trait, alone
    │       ├── adapter_for.rs the selector: family → adapter
    │       ├── family.rs      DistroFamily
    │       ├── detection/     detect.rs · detect_error.rs · distro_info.rs · os_release.rs
    │       ├── debian/        debian_adapter.rs · debian_paths.rs · debian_packages.rs ·
    │       │                  debian_services.rs
    │       └── rhel/          rhel_adapter.rs · rhel_paths.rs · rhel_packages.rs ·
    │                          rhel_services.rs
    ├── ops/
    │   └── src/
    │       ├── {accounts,sites,php,db,ftp,files,cron,firewall,ssl,backup,monitor}/
    │       │                  one folder per area; anatomy below.
    │       │                  accounts = system users: useradd/userdel, homes, quotas
    │       │                  php has no proto service of its own — it is driven by
    │       │                  sites and accounts, and still gets its own area
    │       └── safe_write/    render_validate_swap.rs · rollback_guard.rs ·
    │                          safe_write_error.rs — the ONE implementation of the
    │                          config-write protocol every area calls
    └── templates/
        ├── src/               askama render types, one per config artifact:
        │                      nginx/{php_site,static_site,proxy_site,ssl_block}.rs ·
        │                      php_fpm/pool.rs · vsftpd/user_config.rs ·
        │                      systemd/unit.rs · render_error.rs
        ├── templates/{nginx,php-fpm,vsftpd,systemd}/
        └── tests/golden/      byte-exact expected config renders
```

Crate names are kebab-case `maran-*`; module path mirrors the folder; `error.rs`
is a flat crate-root file, not a folder.

A crate root is always `lib.rs` (or `main.rs`), never `mod.rs`: `mod.rs` exists
only for a subfolder module. So the `DistroAdapter` trait lives in
`distro/src/adapter.rs` and is re-exported from `distro/src/lib.rs` — not defined
in any root or `mod.rs`.

Folders in the tree above that hold no code yet are skeleton
(rules/architecture.md "Skeleton policy"): they exist from day one, held by a
zero-byte `.gitkeep`, and land their first real file with the task that needs it —
in the place the map already assigns.

## One unit per file

**One file = exactly one public item**: one type with its impls, one trait, or one
function. The file is named after that item in snake_case:

| Item                      | File                                  |
| ------------------------- | ------------------------------------- |
| `PathError`               | `path_error.rs`                       |
| `fn resolve_in_home`      | `path.rs` — subject naming, see below |
| `fn adapter_for`          | `adapter_for.rs`                      |
| `struct SitesServiceImpl` | `sites_service.rs`                    |
| `async fn create_site`    | `create_site.rs`                      |

Two naming forms are allowed and nothing else:

1. **Item naming** — the file is the snake_case of its single public item. This is
   the default and the enforced case.
2. **Subject naming** — the file is named after its subject when the containing
   folder already carries the noun: `distro/src/adapter.rs` for `DistroAdapter`,
   `validation/path.rs` for the path functions. Subject naming is only valid where
   this document names the file explicitly in the canonical layout. Inventing a
   new subject-named file is a review reject; extend the map first.

An error enum is a type like any other, so it gets its OWN file next to the code
that returns it — `NameError` → `name_error.rs`, never appended to `name.rs`.

A `mod.rs` or crate root holds ONLY module declarations, re-exports and the module
doc comment. A definition there is a review reject. No `util.rs`/`misc.rs`/
`helpers.rs`/`common.rs`.

Reason: with one item per file the file tree IS the index of the crate — you find
a type by its name without grepping, a diff names exactly what changed, and
nothing accretes into a file whose name stopped describing it. Errors are the case
that always erodes first, which is why they are called out.

```rust
// WRONG — validation/name.rs
pub struct AccountName(String);
pub enum NameError { TooShort, BadCharacter }   // second public type in the file

// RIGHT — validation/name.rs
pub struct AccountName(String);

// RIGHT — validation/name_error.rs
pub enum NameError { TooShort, BadCharacter }

// RIGHT — validation/mod.rs
pub mod name;
pub mod name_error;
```

Files stay small and single-purpose; target < 300 lines, hard review trigger at 400. A file approaching the trigger is almost always holding more than one unit.

## Service anatomy (`crates/agent/src/services/<service>/`)

One folder per proto service, two kinds of file inside:

- `<service>_service.rs` — the `#[tonic::async_trait]` impl. Each rpc method does
  exactly three things: turn the proto request into a validated input type, call
  one `ops` function, turn the result into a response. **No branching on business
  conditions, no filesystem access, no process spawning.** If a method needs a
  fourth thing, that thing belongs in `ops`.
- `<area>_status.rs` — the single `From<XOpError> for tonic::Status` mapping for
  the area. It exists so the match never grows inside the service file, and so
  one error variant maps to one gRPC code in one place.

Streaming rpcs (`TailSiteLog`, `CreateBackup`, `ReadFile`, `WriteFile`) still
follow this: the stream is produced by `ops` and the service only wraps it.

## Operation anatomy (`crates/ops/src/<area>/`)

```
ops/src/sites/
├── mod.rs                  module declarations and re-exports only
├── sites_op_error.rs       one error enum for the whole area
├── create_site.rs          one rpc = one file = one `pub async fn`
├── update_site_php_version.rs
├── enable_site.rs
├── disable_site.rs
├── delete_site.rs
├── tail_site_log.rs
├── reload_web_server.rs
└── model/                  input and output types, one per file
    ├── create_site_input.rs
    ├── site_kind.rs
    └── log_kind.rs
```

Rules for an operation function:

- Its name matches the proto rpc in snake_case. The mapping rpc → file is 1:1 and
  mechanical; a reader finds the code for `CreateSite` without searching.
- It takes an already-typed input from `model/` (never loose `String` parameters
  that a caller could pass in the wrong order) plus `&dyn DistroAdapter` where
  platform facts are needed.
- It returns `Result<_, <Area>OpError>`.
- It re-validates its inputs (see "Validation first") even though the API and the
  service layer already did.
- It is idempotent (see "Idempotency").

`model/` holds inputs the operation accepts and values it returns. A type used by
two areas moves to `agent-core`, it does not get imported across `ops` areas.

## Template anatomy (`crates/templates/`)

- One render type per config artifact, in a folder named after the target system:
  `src/nginx/php_site.rs`, `src/php_fpm/pool.rs`.
- The template source file mirrors the type: `templates/nginx/php_site.conf.j2`.
- The golden file mirrors it again: `tests/golden/nginx/php_site.conf`.
  Golden names are derived, never invented — `site_new.conf`, `site_fixed.conf`
  and friends are review rejects.
- A template change without its golden update fails CI; the golden diff IS the
  review artifact (rules/testing.md).

## Distro adapter

- Distro differences live only in the `distro` crate behind `DistroAdapter`.
  `ops` code MUST NOT branch on distro names, package managers, or paths — and MUST NOT
  write a platform literal either, which is the same violation without an `if`:

```rust
// WRONG — ops/src/accounts/account_operations.rs
const ACTIVE_SHELL: &str = "/usr/sbin/nologin";   // correct on Debian, absent on RHEL

// RIGHT
self.distro.nologin_shell()
```

  A literal is worse than a branch, because a branch at least names the thing it is
  guessing about. `useradd` does not verify that the shell exists, so the wrong path
  creates the account successfully and surfaces months later as "SFTP refuses me".
  `scripts/lib/check-structure.sh` rejects platform literals under `ops/`.
- The branch on family happens exactly once, in `adapter_for`.
- The trait is grown additively, and when it passes roughly a dozen methods it is
  split by concern (paths, packages, services) into sub-traits returned from the
  adapter rather than accumulating a flat interface.
- Adding a family is a new implementation folder plus one arm in `adapter_for` —
  never a new arm anywhere else.

## Validation first (defense in depth)

Every command handler starts by validating its inputs even though the API already
did:

```rust
/// Matches account and site names: lowercase, digits, `_`, 3–30 chars, letter first.
pub static NAME_RE: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"^[a-z][a-z0-9_]{2,29}$").unwrap());

/// Resolves `relative` inside the account home, rejecting traversal and symlink escapes.
pub fn resolve_in_home(account: &AccountName, relative: &Path) -> Result<PathBuf, PathError> {
    let home = PathBuf::from("/home").join(account.as_str());
    let joined = home.join(relative);
    let canonical = joined.canonicalize().map_err(|_| PathError::NotFound)?;
    if !canonical.starts_with(&home) {
        return Err(PathError::EscapesHome); // symlink or ../ escape — refused
    }
    Ok(canonical)
}
```

- All customer file operations go through `resolve_in_home` and run under the
  account's UID. Direct `std::fs` on customer paths as root is forbidden.
- Any caller-supplied value written into a line-oriented or structured config file
  — a crontab entry, an nginx directive, an env file, a systemd unit — MUST reject
  newlines, carriage returns and control characters before it is written
  (rules/security.md §4). Rendering through a template does not make it safe: the
  value is **validated, not escaped**.
- Validation types are constructors that can fail (`AccountName::parse`), not
  free-standing checks a caller may forget. Once a `AccountName` exists, it is
  valid; downstream code does not re-check it, it just cannot be constructed from
  anything else.

## Privileges (`agent-core::privs`)

The agent runs as root. Dropping privileges correctly is the single highest-risk
piece of code in the repository, and its shape is fixed here rather than decided
under deadline.

- `fork_as_account` is the **only** entry point for doing work as a customer.
  Every customer file operation calls it; nothing else drops privileges.
- **Fork, then drop.** `setuid` and friends apply to the whole process, not to the
  calling thread, so calling them inside the multi-threaded tokio runtime is
  forbidden. The work happens in a forked child that drops first and reports its
  result back to the parent.
- **Order is `setgroups` → `setgid` → `setuid`, and never any other.** Dropping
  the user id first removes the capability needed to drop the groups, so the
  supplementary groups silently survive and the "unprivileged" child is not.
- After dropping, the child **re-reads its own uid, gid and group list and
  verifies them** before touching a single file. A failed verification aborts the
  child; it never continues on the assumption that the syscalls worked.
- The child does the narrowest possible unit of work and exits. It does not hold
  the tokio runtime, does not open a socket, and does not run arbitrary code.
- Changes to this module require a second reviewer and a threat note
  (rules/security.md "Sensitive change escalation").

## Process execution

- No shell, ever. No `sh -c`, no string-built command lines, no `format!` into a
  command.
- Processes are spawned with argv arrays only, against an **allow-list of absolute
  binary paths** supplied by the `distro` adapter. A binary not on the list cannot
  be run.
- No rpc, of any kind, executes a caller-supplied program, shell string, or
  template. Anything resembling "run this for me" is rejected on sight.
- stdout/stderr of a spawned tool are captured into a typed error variant for the
  operator log — never returned verbatim to a hosting customer.

## Config writes: render → validate → swap

Every system config the agent writes goes through `ops::safe_write`, which is the
one implementation of this protocol:

1. Render the template (askama) into memory.
2. Write to a temporary file **in the same directory** as the target, so the later
   rename is atomic on the same filesystem.
3. `fsync` the temporary file **and** its containing directory, so a crash cannot
   leave a rename pointing at unflushed bytes.
4. Validate (`nginx -t`, `php-fpm -t`, `crontab -T`, as the area requires).
5. Atomically `rename` over the target.
6. Reload the service.
7. On any failure at 4–6: restore the previous content and return a typed error.

Partial writes are forbidden. An area that needs a variation on this protocol
extends `safe_write` — it does not write its own copy. Two implementations of a
write-and-rollback path is how the first unrecoverable config corruption happens.

## Idempotency

Every command converges to the same state when repeated, and reports
`AlreadyExists`/`NotFound` instead of failing. This is what makes a retry safe
after a network blip, a job requeue, or an agent restart mid-operation — and it is
what removes the need for cleanup scripts, which is where control panels
accumulate their worst code.

Concretely: creating an account that exists is success, deleting a site that is
gone is success, and an operation interrupted halfway leaves the system in a state
the same command can complete.

## Async and blocking

- One tokio multi-threaded runtime, started in `main`. No nested runtimes, no
  `block_on` inside async code.
- Blocking work — filesystem walks, process waits, archive extraction, database
  dumps — goes through `tokio::task::spawn_blocking`. A blocking call on a runtime
  worker stalls every other in-flight command.
- No unbounded buffering of streamed data. `ReadFile`, `TailSiteLog`,
  `CreateBackup` and `RestoreBackup` stream in bounded chunks; reading a
  customer's 4 GB file into a `Vec<u8>` is a denial of service against the panel.
- Long-running operations respect cancellation: when the client drops the stream,
  the work stops.

## unsafe

`unsafe` is forbidden except in `agent-core::privs` (syscall wrappers). Every
crate root except `agent-core` carries `#![forbid(unsafe_code)]`, so the compiler
is the gate rather than a grep. Inside `agent-core`, the exception is scoped to
the module:

```rust
// agent-core/src/privs/mod.rs
#[allow(unsafe_code)]
mod fork_as_account;
```

Each `unsafe` block carries a `// SAFETY:` comment explaining the invariant it
relies on. New `unsafe` outside that module fails to compile, and adding an
`#[allow]` to make it compile is a review reject.

## Logging

- `tracing` only; one span per command carrying `correlation_id` and the command
  name, opened at the service layer so every `ops` call inherits it.
- Never log secrets, passwords, private keys, tokens, or full file contents.
- Never log a customer's file contents at any level, including `debug`.
- `println!`/`eprintln!` are forbidden outside `main` startup errors.
- An error is logged once, at the boundary that handles it. Logging at every level
  on the way up turns one failure into five lines and hides the real one.

## Tests

Full rules in rules/testing.md. The Rust-specific shape:

- Unit tests live under the crate's `src/tests/` **mirror** of its module tree,
  never inline and never beside the unit:

```rust
// In src/validation/name.rs, after the code:
#[cfg(test)]
#[path = "../tests/validation/name_tests.rs"]
mod tests;
```

- `crates/<crate>/tests/` holds integration tests only — things that exercise the
  crate as a caller would, like the handshake over a real unix socket.
- Test names are behavior sentences: `path_with_symlink_escape_is_rejected`.
- Every typed error variant appears in at least one test.
- Template goldens are compared byte-for-byte.
- Test code is exempt from the doc-comment rule; everything else applies.

## Enforcement

`maran structure` runs in CI as a merge gate and rejects:

- more than one public unit in a file;
- a definition in a crate root or `mod.rs`;
- a file whose name does not match its single public item, in snake_case. The
  subject-named exceptions are LISTED in the script rather than inferred — an inferred
  exception is a rule that quietly stops applying — plus the two families the service
  anatomy mandates by name, `*_service.rs` and `*_status.rs`;
- an inline `#[cfg(test)] mod tests { … }`;
- a `*_tests.rs` outside the `src/tests/` mirror;
- junk-drawer file names (`util`, `utils`, `helpers`, `misc`, `common`, `shared`).

The compiler and clippy enforce the rest: `unsafe_code`, `missing_docs`,
`unwrap_used`, `expect_used`, `panic`. A rule that can be a lint is a lint; a rule
that can be a script check is a script check; only what neither can express is
left to review.
