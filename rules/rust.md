# Rust Rules (agent)

Normative. The agent runs as root: these rules are security controls, not taste.

## Toolchain & lints

- Stable Rust, edition 2024. `rustfmt` defaults — never hand-format against it.
- CI runs `cargo clippy --all-targets --all-features -- -D warnings`.
- Workspace `Cargo.toml` sets:

```toml
[workspace.lints.clippy]
unwrap_used = "deny"
expect_used = "deny"
panic = "deny"
```

`unwrap`/`expect`/`panic!` are allowed only in tests and build scripts. Agent code returns errors; a root process MUST NOT panic on input.

## Errors

- Library crates (`agent-core`, `ops`, `distro`): `thiserror` enums, one per domain, marked `#[non_exhaustive]`.
- Binary boundary (`agent`): map to the proto `Error` message; `anyhow` only in `main` startup.

```rust
/// Errors returned by site operations.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SiteOpError {
    /// The rendered config failed `nginx -t`; the previous config was restored.
    #[error("nginx validation failed: {stderr}")]
    NginxValidation { stderr: String },
    /// Domain does not match the allowed pattern.
    #[error("invalid domain name")]
    InvalidDomain,
}
```

## Doc comments — mandatory for ALL items

`///` docs are REQUIRED on **every item regardless of visibility** — `pub` and private: functions, structs, enums, traits, modules, non-obvious fields. What it does, invariants, and error conditions. Test code is exempt (behavior-sentence test names). `cargo doc` warnings are errors on CI (`RUSTDOCFLAGS="-D warnings"`), and `#![warn(missing_docs)]` is set in every crate root.

## Canonical agent layout

```
agent/
├── Cargo.toml                 workspace (+ rustfmt.toml)
└── crates/
    ├── agent/                 bin: maran-agent (library + thin main, so integration
    │   │                      tests can start a real server in-process)
    │   ├── build.rs           compiles proto/agent/v1/ via tonic-build
    │   ├── src/
    │   │   ├── lib.rs · main.rs · server.rs · error.rs
    │   │   ├── config/        agent_options.rs · current_uid.rs · options_error.rs
    │   │   ├── peercred/      peer_policy.rs (who may connect) · peer_guard.rs (the check)
    │   │   └── services/      gRPC service impls, one file per proto service (system.rs)
    │   └── tests/             integration tests (+ fixtures/)
    ├── agent-core/
    │   └── src/
    │       ├── validation/    name.rs · name_error.rs · path.rs · path_error.rs
    │       └── privs/         the ONLY home of unsafe syscall/setuid wrappers
    ├── distro/
    │   └── src/
    │       ├── adapter.rs     the DistroAdapter trait, alone
    │       ├── adapter_for.rs the selector: family → adapter
    │       ├── family.rs      DistroFamily
    │       ├── detection/     detect.rs · detect_error.rs · distro_info.rs · os_release.rs
    │       ├── debian/        Ubuntu/Debian: apt, service names, paths
    │       └── rhel/          AlmaLinux/Rocky: dnf, service names, paths
    ├── ops/
    │   └── src/{accounts,sites,php,db,ftp,files,cron,firewall,ssl,backup,monitor}/
    │                          accounts = system users: useradd/userdel, homes, quotas
    └── templates/
        ├── src/               askama render types
        ├── templates/{nginx,php-fpm,vsftpd,systemd}/
        └── tests/golden/      byte-exact expected config renders
```

Crate names are kebab-case `maran-*`; module path mirrors the folder; `error.rs` is a flat crate-root file, not a folder. `agent-core/src/privs/` and the `ops`/`templates` module folders are skeleton (rules/architecture.md "Skeleton policy") — they land with the task that first needs them, in the place the map already assigns.

A crate root is always `lib.rs` (or `main.rs`), never `mod.rs`: `mod.rs` exists only for a subfolder module. So the `DistroAdapter` trait lives in `distro/src/adapter.rs` and is re-exported from `distro/src/lib.rs` — not defined in any root or `mod.rs`.

## One unit per file

**One file = exactly one public item**: one type with its impls, one trait, or one function. The file is named after that item in snake_case (`PathError` → `path_error.rs`, `resolve_in_home` → `path.rs`, `adapter_for` → `adapter_for.rs`), or after its subject when the module folder already carries the noun (`distro/src/adapter.rs` for `DistroAdapter`). An error enum is a type like any other, so it gets its OWN file next to the code that returns it — `NameError` → `name_error.rs`, never appended to `name.rs`.

A `mod.rs` or crate root holds ONLY module declarations, re-exports and the module doc comment. A definition there is a review reject. No `util.rs`/`misc.rs`/`helpers.rs`.

Reason: with one item per file the file tree IS the index of the crate — you find a type by its name without grepping, a diff names exactly what changed, and nothing accretes into a file whose name stopped describing it. Errors are the case that always erodes first, which is why they are called out.

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

## Validation first (defense in depth)

Every command handler starts by validating its inputs even though the API already did:

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

- All customer file operations go through `resolve_in_home` and run under the account's UID (fork + setuid helper in `agent-core::privs`). Direct `std::fs` on customer paths as root is forbidden.
- Config changes: render (askama) → write temp → validate → atomic `rename` → reload → typed error and rollback on failure. Partial writes are forbidden.

## unsafe

`unsafe` is forbidden except in `agent-core::privs` (syscall wrappers). Each block carries a `// SAFETY:` comment explaining the invariant. New `unsafe` outside that module fails review; CI greps for it.

## Logging

`tracing` only; one span per command carrying `correlation_id` and command name. Never log secrets, passwords, or full file contents. `println!`/`eprintln!` are forbidden outside `main` startup errors.
