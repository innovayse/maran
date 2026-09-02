# Maran agent (Rust) — instructions for AI sessions

Binding rules: `rules/rust.md` (layout, naming, privileges), `rules/security.md`
(the reviewer's checklist), `rules/testing.md` (where tests live).
Read `rules/rust.md` before writing any Rust in this tree.

## The one-line summary

`agent` translates, `ops` decides, `agent-core` validates, `distro` knows the
platform, `templates` renders. A file that does two of these is in the wrong
crate.

## Where a new file goes

Answer these in order; the first "yes" is the destination.

1. Does it turn a proto message into a call, or an error into a `tonic::Status`?
   → `crates/agent/src/services/<service>/`
2. Does it change the system (users, configs, services, files)?
   → `crates/ops/src/<area>/`
3. Does it answer "is this input safe?" or "run this as that user"?
   → `crates/agent-core/src/`
4. Does it differ between Debian and RHEL? → `crates/distro/src/`
5. Does it produce config file text? → `crates/templates/`

If none fit, it is a NEW kind of file: add its named place to
`rules/rust.md` first, in the same PR. Never file it "wherever it fits".

Tables below mark planned homes as *(planned)*. A planned row is the address a
future file must use — it is not a claim that the file exists today.

## Crate map

### `crates/agent/` — the daemon, binary + library

The only crate that knows gRPC exists. Library plus a thin `main`, so integration
tests can start a real server in-process on a temporary socket.

| Path | Purpose |
|---|---|
| `src/main.rs` | tracing setup, flag parsing, start. Nothing else. |
| `src/lib.rs` | module declarations; includes generated proto once for the crate. |
| `src/server.rs` | socket preparation, permissions, service registry. |
| `src/error.rs` | `StartupError` — fatal startup failures only. |
| `src/config/` | command-line and environment parsing: `invocation.rs` (what the command line asked for — run, or print usage; refuses an argument this binary does not define), `agent_options.rs` (the answer it carries), `options_error.rs`. |
| `src/peercred/` | who may connect at all. `peer_policy.rs` = the rule, `peer_guard.rs` = the check. Authorisation starts below the RPC layer. |
| `src/services/<service>/` | one folder per proto service. `<service>_service.rs` = the tonic trait impl, `<area>_status.rs` = the error → gRPC code mapping, `validated_*.rs` = one proto-to-input bundle per request shape. Today: `system/`, `accounts/`, `sites/`, `ssl/`, `php/`, `files/`, `db/`, `sftp/`. |
| `src/tests/` | unit tests, mirroring `src/`. |
| `tests/` | integration tests over a real unix socket (`handshake.rs`), plus `fixtures/`. |
| `build.rs` | compiles `proto/agent/v1/` via tonic-build. Generated code is never committed. |

A service method does exactly three things: proto → validated input, one `ops`
call, result → response. Business branching, filesystem access and process
spawning in a service file are review rejects.

### `crates/agent-core/` — validation, privileges, and what is global

The security primitives every other crate depends on. No gRPC, no distro
knowledge, no system mutation.

| Path | Purpose |
|---|---|
| `src/agent_paths.rs` | `AgentPaths`: directories the agent owns that are identical on every family (nginx include dir, certificate dir, account home root, php-fpm socket dir, SFTP jail root). A path that differs per family is a `distro` fact instead. |
| `src/validation/` | grouped by the domain the value ends up in: `system/` (name, sftp_user_name), `db/` (database_name, db_user_name), `web/` (domain, upstream, php_version), `fs/` (path — `resolve_in_home` — relative_path, file_mode), `secrets/` (password, secret). One type per input kind, each with its own `*_error.rs` beside it. A constructed value is a valid value. |
| `src/utils/` | helpers that belong to no single area and answer a question about the host, not about a feature: `directory.rs` (recursive size), `current_uid.rs`. A helper only one area calls belongs to that area, not here. |
| `src/privs/` | the ONLY place `unsafe` is allowed. `fork_as_account.rs` is the single entry point for doing work as a customer; `account_ids.rs` resolves uid/gid via `getpwnam_r`; `priv_error.rs` types the failures. Threat note: `docs/superpowers/notes/2026-08-30-privs-threat-note.md`. |

`privs` rules that are easy to get wrong: fork first, then drop (setuid is
process-wide, not thread-scoped, so it must not be called inside the tokio
runtime); order is `setgroups` → `setgid` → `setuid`; the child re-reads and
verifies its ids before touching anything. Changes here need a second reviewer.

### `crates/distro/` — the only crate that may name a distribution

Every `if debian` in the codebase belongs here and nowhere else. No other crate
may contain a platform literal — a path, a package name, a shell — and
`maran structure` fails the build when one appears in `ops`.

| Path | Purpose |
|---|---|
| `src/detection/` | what host is this? `os_release.rs` parses, `detect.rs` decides, `distro_info.rs` carries the answer, `detect_error.rs` refuses unsupported hosts. |
| `src/adapter.rs` | the `DistroAdapter` trait, alone in its file. |
| `src/adapter_for.rs` | family → adapter. The branch on family happens exactly once, here. |
| `src/family.rs` | `DistroFamily`. |
| `src/debian/debian_adapter.rs`, `src/rhel/rhel_adapter.rs` | paths, package names, service names per family. |
| `src/tests/` | unit tests, mirroring `src/`. |

Adding a family = one new folder + one arm in `adapter_for`. Nothing else changes.

### `crates/ops/` — what the agent actually does

One folder per area, one file per proto RPC, named as the RPC in snake_case:
`CreateSite` → `sites/create_site.rs`. The mapping is mechanical so the code for
an RPC is found without searching.

| Path | Purpose |
|---|---|
| `src/accounts/` | system users: useradd/userdel, homes, quotas, usage. |
| `src/sites/` *(planned)* | nginx vhosts: create, enable/disable, delete, php version, log tail, reload. |
| `src/php/` *(planned)* | pools and versions. No proto service of its own — driven by sites and accounts. |
| `src/db/` | MySQL/MariaDB databases and the dedicated user each one is created with. The agent holds no database credential: it connects over the local socket as root, authenticated by the connecting uid. |
| `src/files/` *(planned)* | customer file operations. Every one goes through `resolve_in_home` and runs under the account's uid. |
| `src/sftp/` | OpenSSH SFTP logins, each chrooted into a per-account root-owned jail with the account's real home bind-mounted inside. `model/account_jail.rs` derives every jail path AND the systemd mount unit's escaped name from one `AccountName`. The login is created with the ACCOUNT's own uid and gid (`model/account_ownership.rs`): a home of `<account>:<web server group> 0750` gives a separate identity nothing at all. |
| `src/ftp/` *(planned)* | FTP users, if an FTP daemon is ever shipped. SFTP is `src/sftp/`. |
| `src/cron/` *(planned)* | per-account crontab entries. |
| `src/firewall/` *(planned)* | nftables rules and bans. |
| `src/ssl/` *(planned)* | certificate install, removal, self-signed. |
| `src/backup/` *(planned)* | create, restore, list, delete. |
| `src/monitor/` *(planned)* | host metrics, service statuses, per-account disk usage. |
| `src/safe_write/` *(planned)* | the ONE implementation of render → temp → fsync → validate → atomic rename → reload → rollback. Areas call it; they never write their own copy. |

Inside an area: `mod.rs` (declarations only), `<area>_error.rs` (one error enum
for the area), one file per operation, and `model/` for its input and output
types — `accounts/` is the worked example, down to `system_host.rs` (the trait
that keeps process spawning injectable) and `process_system_host.rs` (its real
implementation). A type needed by two areas moves to `agent-core`; areas never
import each other.

Every operation is idempotent: repeating it converges, and it reports
`AlreadyExists`/`NotFound` rather than failing. This is why retries are safe and
why no cleanup scripts exist.

### `crates/templates/` — config text

| Path | Purpose |
|---|---|
| `src/nginx/`, `src/php_fpm/`, `src/systemd/`, `src/vsftpd/` *(vsftpd planned)* | one askama render type per config artifact. |
| `templates/<target>/` | the template sources, mirroring the render types. |
| `tests/golden/<target>/` | byte-exact expected renders. Names are derived from the render type, never invented. |

A template change without its golden update fails CI; the golden diff is the
review artifact.

## Standing constraints

- Never `unwrap`, `expect` or `panic!` outside tests and build scripts. A root
  process returns typed errors.
- No shell, ever. Processes are spawned with argv arrays against an allow-list of
  absolute paths from the distro adapter. No RPC runs caller-supplied code.
- Doc comments on every item, private included. `# Errors` names the conditions,
  not just the type.
- One file = one public unit, and the file is named after it (`adapter.rs` holds
  `DistroAdapter`, `debian_adapter.rs` holds `DebianAdapter`). Errors get their
  own `*_error.rs`. `mod.rs` and crate roots declare and re-export, never define.
- Blocking work goes through `spawn_blocking`. Streams stay bounded — never read a
  customer file into memory whole.
- Never log secrets or customer file contents, at any level.

## Verification — everything runs through `maran`

There is one entry point to this repository's tooling. Source `scripts/dev`
once per shell: it puts the toolchains AND `scripts/` on PATH, so `maran` is
typed as a global command rather than as a path.

```bash
source scripts/dev            # must be SOURCED — a subprocess cannot set your PATH
maran                         # the whole toolbox, with what each command is for
```

Then, for this tree:

```bash
maran check                   # toolchain preflight: can this machine build at all
maran agent check             # fmt --check, clippy -D warnings, cargo test, and cargo doc
maran structure               # the file and folder laws above, as a merge gate
maran proto                   # lint the API-to-agent contract
maran handshake               # agent and API over a real unix socket
```

`maran agent` also takes `build`, `test`, `lint` and `fmt` separately when a
full `check` is more than the moment needs. It uses a native `cargo` when one is
installed and falls back to the pinned `rust:<version>-slim` container
otherwise, so the same command works on a machine with no Rust toolchain.

Never call the scripts under `scripts/lib/` directly: they are implementations,
and `maran` is the documented surface. A toolchain error is a failure to verify,
never a pass. "No tests found" is a failure too.
