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
    │   │   ├── config/        invocation.rs (the command line) · agent_options.rs · options_error.rs
    │   │   ├── peercred/      peer_policy.rs (who may connect) · peer_guard.rs (the check)
    │   │   └── services/      one FOLDER per proto service:
    │   │       ├── system/    system_service.rs
    │   │       ├── accounts/  accounts_service.rs · account_status.rs
    │   │       ├── sites/     sites_service.rs · site_status.rs · invalid_input.rs ·
    │   │       │              validated_{site,identity,overrides}.rs — proto → validated
    │   │       │              input, one bundle per request shape, so the service file
    │   │       │              stays the three steps and nothing else ·
    │   │       │              stream_log_sink.rs · tail_terminal.rs (log-follow glue)
    │   │       ├── ssl/       ssl_service.rs · ssl_status.rs
    │   │       ├── php/       php_service.rs · php_status.rs
    │   │       ├── db/        db_service.rs · db_status.rs · validated_account.rs ·
    │   │       │              validated_creation.rs · validated_database.rs ·
    │   │       │              validated_removal.rs — proto → validated input, one
    │   │       │              bundle per request shape
    │   │       ├── files/     files_service.rs · file_status.rs ·
    │   │       │              validated_write.rs (the thin transport half:
    │   │       │              drains the stream) · write_collector.rs (the state machine that
    │   │       │              holds the header rules and the byte cap, so a unit test can drive
    │   │       │              them — a tonic Streaming cannot be constructed by one) ·
    │   │       │              validated_delete.rs
    │   │       ├── sftp/      sftp_service.rs · sftp_status.rs ·
    │   │       │              validated_creation.rs · validated_credential.rs ·
    │   │       │              validated_password_change.rs · validated_sftp_user.rs.
    │   │       │              It answers `ftp.proto`, which describes SFTP and
    │   │       │              nothing else — the folder is named after what the
    │   │       │              service IS, matching `ops/src/sftp/`. A future FTP
    │   │       │              daemon would get its own `ftp/` beside it.
    │   │       ├── cron/      cron_service.rs · cron_status.rs
    │   │       ├── firewall/  firewall_service.rs · firewall_status.rs
    │   │       ├── backup/    backup_service.rs · backup_status.rs
    │   │       └── monitor/   monitor_service.rs · monitor_status.rs
    │   ├── src/tests/         unit tests, mirroring src/ (rules/testing.md)
    │   └── tests/             integration tests (+ fixtures/)
    ├── agent-core/
    │   └── src/
    │       ├── agent_paths.rs AgentPaths — locations the agent owns and that are the SAME
    │       │                  on every family (nginx include dir, certificate dir, account
    │       │                  home root, php-fpm socket dir, SFTP jail root). A path
    │       │                  that differs between families is a distro fact and lives in
    │       │                  distro/, never here; one that does not differ lives here, never
    │       │                  as an adapter method repeated with the same literal.
    │       ├── command_outcome.rs CommandOutcome — the {status, stdout, stderr} shape of
    │       │                  having run one program. Started in one `ops` area and needed
    │       │                  by a second, which is the rule below firing: it lives here so
    │       │                  both import it instead of each keeping its own copy.
    │       ├── validation/    one folder per DOMAIN the value ends up in, so a
    │       │                  validator is found by asking "where is this written?".
    │       │                  Every kind is a `<kind>.rs` + `<kind>_error.rs` pair in
    │       │                  its group; a kind used by two groups still lives in the
    │       │                  one that CREATES the object.
    │       │   ├── system/    name · sftp_user_name — values that become OS objects
    │       │   │              (planned, same shape: cron_expression)
    │       │   ├── db/        database_name · db_user_name — values that reach MySQL
    │       │   ├── web/       domain · upstream · php_version — values written into
    │       │   │              web-server configuration (planned: port · ip_address)
    │       │   ├── fs/        path (resolve_in_home) · relative_path · file_mode
    │       │   └── secrets/   password (validated alphabet, injection-free by
    │       │                  construction) · secret (redacting wrapper, no _error)
    │       ├── privs/         the ONLY home of unsafe syscall/setuid wrappers:
    │       │                  fork_as_account.rs · account_ids.rs · priv_error.rs ·
    │       │                  directory_entry_name.rs (the shared name check, no syscall) ·
    │       │                  and one wrapper per `*at` syscall, each taking a directory the
    │       │                  caller already holds open plus a single entry name:
    │       │                  open_in_directory.rs · create_file_in_directory.rs ·
    │       │                  make_directory_in_directory.rs · rename_in_directory.rs ·
    │       │                  remove_file_in_directory.rs. One file per syscall and not one
    │       │                  "at_syscalls.rs": each carries its own SAFETY argument and its
    │       │                  own reason for the flags it forces, and a reviewer reads the
    │       │                  one that changed.
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
    │   ├── src/
    │       ├── {accounts,sites,php,db,sftp,ftp,files,cron,firewall,ssl,backup,monitor}/
    │       │                  one folder per area; anatomy below.
    │       │                  accounts = system users: useradd/userdel, homes, quotas
    │       │                  sftp/ is OpenSSH logins chrooted into a per-account,
    │       │                  root-owned jail with the real home bind-mounted inside;
    │       │                  its model/account_jail.rs is where every path and the
    │       │                  systemd mount unit's escaped NAME are derived from one
    │       │                  AccountName, because systemd refuses a mount unit whose
    │       │                  file name is not the escaping of its own mount point.
    │       │                  model/account_ownership.rs carries the uid and gid the
    │       │                  login is created WITH: an SFTP login shares its
    │       │                  account's identity, because an account home of
    │       │                  <account>:<web server group> 0750 gives an identity of
    │       │                  its own nothing at all.
    │       │                  ftp/ stays for a future FTP daemon and holds nothing.
    │       │                  php/ has both a service and an ops area. Installing a
    │       │                  PHP version is a host operation with no site to drive
    │       │                  it — it is done once and then bound by many sites — so
    │       │                  it gets `services/php/php_service.rs`. Everything about
    │       │                  a single site's PHP binding stays in `services/sites/`.
    │       │                  files/ holds, besides the two operations, the three
    │       │                  private units the privileged walk needs — they are named
    │       │                  here because they are not "one file per rpc" and would
    │       │                  otherwise have no assigned place:
    │       │                  open_parent_directory.rs (the O_NOFOLLOW descent) ·
    │       │                  write_in_home.rs · remove_in_home.rs. Each is what the
    │       │                  forked child actually runs, split from its host method so
    │       │                  a test can drive it against a temporary directory with an
    │       │                  injected uid instead of needing root — the same split
    │       │                  `sites/follow_log.rs` uses, for the same reason.
    │       ├── safe_write/    render_validate_swap.rs · remove_config.rs ·
    │       │                  rollback_guard.rs · safe_write_error.rs ·
    │       │                  config_host.rs (the injectable filesystem/reload seam) ·
    │       │                  write_config_set.rs (several files as one all-or-nothing set) ·
    │       │                  model/ (config_file.rs · reload.rs · validator.rs) — the ONE
    │                          implementation of the config-write protocol every
    │                          area calls. remove_config.rs is that same protocol
    │                          for taking a config AWAY: unlink, validate, reload,
    │                          and put the file back if either refuses. Removing a
    │                          vhost can leave the tree invalid, so removal extends
    │                          the protocol here rather than becoming an
    │                          `fs::remove_file` in the area that wanted it.
    │   └── tests/fixtures/    inert certificate/key PEMs the ssl unit tests
    │                          `include_str!`; generated for tests, never real material.
    └── templates/
        ├── src/               askama render types, one per config artifact:
        │                      nginx/{php_site,static_site,proxy_site,suspended_site,ssl_block,site_body}.rs ·
        │                      site_body.rs renders what a site SERVES — its root,
        │                      index and locations — once, so the port-80 block and
        │                      the TLS block embed the same string instead of two
        │                      hand-kept copies that drift on the half a browser reaches. ·
        │                      php_fpm/{pool,pool_override}.rs · vsftpd/user_config.rs ·
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

A subject-named **concern file** is allowed to hold more than one public function,
where the concern named by the file is itself the unit — not any one function
inside it. The canonical layout names exactly this shape for `distro/src/debian/`
and `distro/src/rhel/`: `debian_paths.rs` / `rhel_paths.rs`,
`debian_packages.rs` / `rhel_packages.rs` and `debian_services.rs` /
`rhel_services.rs` each answer every path, package or service question their
family's `DistroAdapter` methods ask. `validation/path.rs` is the same pattern
already, one file short of needing to be said out loud. Splitting these into one
file per function would produce `debian_nginx_include_directory.rs` next to
`debian_certificate_directory.rs` — a file per fact instead of a file per concern,
which does not make the tree easier to read, only longer. The exception is this
short, named list, not a licence to group functions by convenience elsewhere:
a new concern file earns a place here the same way a new subject-named file does,
by being added to the map first.

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

One folder per proto service, three kinds of file inside:

- `<service>_service.rs` — the `#[tonic::async_trait]` impl. Each rpc method does
  exactly three things: turn the proto request into a validated input type, call
  one `ops` function, turn the result into a response. **No branching on business
  conditions, no filesystem access, no process spawning.** If a method needs a
  fourth thing, that thing belongs in `ops`.
- `<area>_status.rs` — the single `From<XOpError> for tonic::Status` mapping for
  the area. It exists so the match never grows inside the service file, and so
  one error variant maps to one gRPC code in one place.
- one item-named file per decision the handler would otherwise inline — the
  request-to-input checks (`validated_site.rs`, `validated_identity.rs`,
  `validated_overrides.rs`, `invalid_input.rs`), a sink or adapter the rpc needs
  (`stream_log_sink.rs`), and the terminal-outcome choices of a streaming rpc
  (`tail_terminal.rs`). A handler is a translation layer, so anything with more
  than one case gets a name, and it gets one for a reason a reviewer can check:
  a decision inlined in a handler can be deleted without a single test going
  red. Same rule as everywhere else — one unit per file, named after the unit.

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

## Config writes: render → swap → validate

Every system config the agent writes goes through `ops::safe_write`, which is the
one implementation of this protocol:

1. Render the template (askama) into memory.
2. Write to a temporary file **in the same directory** as the target, so the later
   rename is atomic on the same filesystem.
3. `fsync` the temporary file **and** its containing directory, so a crash cannot
   leave a rename pointing at unflushed bytes.
4. Atomically `rename` over the target.
5. Validate (`nginx -t`, `php-fpm -t`, `crontab -T`, as the area requires).
6. Reload the service.
7. On any failure at 5–6: restore the previous content and return a typed error.

The rename precedes validation, not the other way round, because the validating
tool reads the real config tree by path — `nginx -t` parses `nginx.conf` and
everything its includes glob in, and a temporary file named `.tmpXXXXXX` matches
no glob and is invisible to it. Validating before the rename would parse the OLD
tree and tell us nothing about the new content. Renaming first is safe because
nginx does not read a file until it is asked to: between the rename and the
reload the file on disk has changed and the running server has not, so a failed
validation is still fully recoverable by restoring the previous content — nothing
in the running process needs to be undone. It is also strictly more useful this
way: validating against the real tree catches conflicts with OTHER files, such as
a duplicate `server_name` in a different site's config, which validating one file
in isolation never could.

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

- more than one public unit in a file, except the named concern files
  (`debian_paths.rs`, `debian_packages.rs`, `debian_services.rs`,
  `rhel_paths.rs`, `rhel_packages.rs`, `rhel_services.rs` — see "One unit per
  file"), where the concern is the unit and several functions are expected;
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
