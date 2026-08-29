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
    ├── agent/                 bin: maran-agent
    │   ├── src/
    │   │   ├── main.rs · lib.rs · server.rs · peercred.rs · error.rs
    │   │   ├── config/        flag/env parsing
    │   │   └── services/      gRPC service impls, one file per proto service
    │   └── tests/             integration tests (+ fixtures/)
    ├── agent-core/
    │   └── src/
    │       ├── validation/    name regexes, path containment (resolve_in_home)
    │       └── privs/         the ONLY home of unsafe syscall/setuid wrappers
    ├── distro/
    │   └── src/               DistroAdapter trait (mod.rs) + one folder per family:
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

Crate names are kebab-case `maran-*`; module path mirrors the folder; `error.rs` is a flat crate-root file, not a folder.

## One unit per file

One file = one logical unit: a type with its impls, or one cohesive group of free functions with a single purpose. File name says what it is (`paths.rs` = path containment, `peercred.rs` = socket peer checks). No `util.rs`/`misc.rs`/`helpers.rs`.

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
