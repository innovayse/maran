# Maran Foundation Implementation Plan (Plan 1 of 8)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A bootable skeleton of Maran: Rust root-agent serving the gRPC contract on a unix socket, C# modular-monolith host with PostgreSQL/Wolverine/module loading, Vue SPA shell, dev polygon and CI — everything later modules plug into.

**Architecture:** Three processes (Vue SPA → `maran-api` C# → `maran-agent` Rust root daemon over gRPC/unix-socket → system), PostgreSQL as the only store, modular monolith with vertical slices, proto as the single contract source. Per spec §5–§7.

**Tech Stack:** .NET 9 (ASP.NET Core, EF Core, Wolverine, Npgsql), Rust stable edition 2024 (tokio, tonic, prost, thiserror, tracing), Vue 3 + Vite + TypeScript + Tailwind + Pinia, PostgreSQL 16, protobuf.

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md`

## Roadmap (this plan is №1)

1. **Foundation** (this document) — contract, agent skeleton, host skeleton, SPA shell, dev env, CI.
2. Auth + Accounts — JWT/refresh/2FA/sessions, Account/User/Plan, system-user provisioning, quotas, suspension, IDOR fixture.
3. Sites + Multi-PHP + SSL — nginx templates, php-fpm pools, ACME.
4. Databases + FTP/SFTP + Files.
5. Cron + Firewall + Monitoring + Tasks UI.
6. Backups.
7. Provisioning API + hostpanel provider contract tests.
8. Licensing (+ cloud service) + Installer/updates/CLI + E2E hardening.

Each later plan is written via superpowers:writing-plans when its predecessor is done.

## Global Constraints (from spec — apply to every task)

- Everything approved for v1 ships in v1; maximum security and cleanliness (spec §2).
- `rules/` is normative: doc comments on ALL production code, one file = one type/unit, vertical slices, Result-not-exceptions, no shell strings, additive-only proto (rules/*.md).
- **Never `git commit`** — finish a task, report, and wait for the owner's explicit command (rules/git.md). Task "commit" steps below are checkpoints: STOP and request the owner's go-ahead.
- Identity when committing on command: `edgar2031 <edgar.poghosyan.2031@gmail.com>`; Conventional Commits; NO AI attribution trailers.
- Only three runtime processes: api (non-root), agent (root), PostgreSQL (unix socket only). No new daemons/brokers.
- Naming: `Maran.*` C# projects, `maran-*` Rust crates, package `maran.agent.v1`, services `maran-api`/`maran-agent`, socket `/run/maran/agent.sock`.
- C#: net9.0, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, central package versions.
- Rust: edition 2024, clippy `-D warnings`, `unwrap_used`/`expect_used`/`panic` denied outside tests.
- Docker is dev-only; production is native (spec §2/§14).

---

### Task 1: Repo scaffold + toolchain preflight

**Files:**
- Create: `.gitignore`, `README.md`, `CLAUDE.md`, `SECURITY.md`, `scripts/preflight.sh`
- Already present (do not touch): `rules/`, `docs/`

**Interfaces:**
- Consumes: nothing.
- Produces: repo root layout; `scripts/preflight.sh` (exit 0 = toolchain ok) used by CI and Task 13.

- [ ] **Step 1: Write `scripts/preflight.sh`** (executable, `chmod +x`)

```bash
#!/usr/bin/env bash
# Verifies the developer toolchain for Maran. Exit 0 iff everything required is present.
set -euo pipefail

fail=0
need() { # name, command, version-args, minimum-major
  local name="$1" cmd="$2" args="$3" min="$4"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "MISSING  $name ($cmd)"; fail=1; return
  fi
  local ver major
  ver="$($cmd $args 2>/dev/null | head -1)"
  major="$(echo "$ver" | grep -oE '[0-9]+' | head -1)"
  if [ "${major:-0}" -lt "$min" ]; then
    echo "TOO OLD  $name: '$ver' (need >= $min)"; fail=1
  else
    echo "OK       $name: $ver"
  fi
}

need "dotnet SDK" dotnet "--version" 9
need "cargo"      cargo  "--version" 1
need "rustc"      rustc  "--version" 1
need "node"       node   "--version" 20
need "npm"        npm    "--version" 10
need "protoc"     protoc "--version" 3
need "docker"     docker "--version" 24

exit "$fail"
```

- [ ] **Step 2: Run it**

Run: `bash scripts/preflight.sh`
Expected: all `OK` lines, exit 0. If anything is MISSING, install it before continuing (Ubuntu dev machine: `sudo apt install -y protobuf-compiler`, dotnet via dotnet.microsoft.com apt repo, rustup.rs, nodesource or nvm).

- [ ] **Step 3: Write root `.gitignore`**

```gitignore
# --- .NET
backend/**/bin/
backend/**/obj/
*.user

# --- Rust
agent/target/

# --- Node
frontend/node_modules/
frontend/dist/
e2e/node_modules/

# --- generated / local
**/*.g.cs
.env
*.local.json
/scripts/*.log
.DS_Store
```

- [ ] **Step 4: Write `README.md`**

```markdown
# Maran

Open-source (source-available) web hosting control panel: sites, multi-PHP, SSL,
databases, FTP/SFTP, files, cron, firewall, monitoring, backups — with hosting-customer
cabinets and a WHM-style provisioning API. C# modular monolith + Rust root agent +
Vue 3 SPA + PostgreSQL. Commercial modules load from the Innovayse marketplace.

- Engineering rules (normative): [`rules/`](rules/README.md)
- Design spec: [`docs/superpowers/specs/2026-08-29-maran-design.md`](docs/superpowers/specs/2026-08-29-maran-design.md)
- Current plan: [`docs/superpowers/plans/2026-08-29-maran-foundation.md`](docs/superpowers/plans/2026-08-29-maran-foundation.md)

## Dev setup

    bash scripts/preflight.sh          # verify toolchain
    docker compose -f docker/docker-compose.dev.yml up -d   # PostgreSQL for dev
    cd backend  && dotnet test
    cd agent    && cargo test
    cd frontend && npm ci && npm test

Production is installed natively via the installer (no Docker): see spec §14.
```

- [ ] **Step 5: Write `CLAUDE.md`**

```markdown
# Maran — instructions for AI sessions

1. Read `rules/README.md` first; every rule there is binding. Highlights:
   - Doc comments on ALL production code (private included). One file = one type/unit.
   - NEVER `git commit` or push unless the owner explicitly commands it, and never add
     AI attribution trailers. Identity: edgar2031 <edgar.poghosyan.2031@gmail.com>.
   - No shell-string execution anywhere; agent commands are typed proto RPCs only.
2. Spec: docs/superpowers/specs/2026-08-29-maran-design.md (Russian).
   Plans: docs/superpowers/plans/. Execute plans task-by-task with TDD.
3. Layout: proto/ (contract), backend/ (C# modular monolith), agent/ (Rust root daemon),
   frontend/ (Vue SPA), installer/, docker/ (dev only), rules/, docs/.
4. Verification commands: `bash scripts/preflight.sh`, `dotnet test` (backend/),
   `cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test` (agent/),
   `npm run lint && npm test && npm run build` (frontend/).
```

- [ ] **Step 6: Write `SECURITY.md`**

```markdown
# Security Policy

Report vulnerabilities privately to security@innovayse.com. Do not open public issues
for security problems. We acknowledge within 48 hours; critical issues target a fix or
mitigation within 14 days. Coordinated disclosure is honored and credited.
```

- [ ] **Step 7: Verify layout**

Run: `ls -la && bash scripts/preflight.sh && test -d rules && echo LAYOUT-OK`
Expected: `LAYOUT-OK`, preflight exit 0.

- [ ] **Step 8: Checkpoint** — report Task 1 done; owner decides on commit (`chore: repo scaffold and toolchain preflight`).

---

### Task 2: Proto contract v1 — `common.proto` + `system.proto`

**Files:**
- Create: `proto/agent/v1/common.proto`, `proto/agent/v1/system.proto`, `scripts/proto-lint.sh`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Tasks 5, 8): package `maran.agent.v1`; messages `AgentError{ErrorCode code, string message, string tool_output}`, `Progress{uint32 percent, string stage}`; enum `ErrorCode{…INVALID_INPUT=1, ALREADY_EXISTS=2, NOT_FOUND=3, VALIDATION_FAILED=4, SYSTEM_FAILURE=5}`; service `SystemService.GetAgentInfo(GetAgentInfoRequest) → GetAgentInfoResponse{oneof result: AgentInfo ok | AgentError error}`; `AgentInfo{string version, string distro_id, DistroFamily family, uint32 proto_version}`; enum `DistroFamily{DEBIAN=1, RHEL=2}`.

- [ ] **Step 1: Write `proto/agent/v1/common.proto`**

```proto
// Shared contract types for the Maran API <-> agent boundary.
// Evolution rules: rules/proto.md (additive only inside v1).
syntax = "proto3";

package maran.agent.v1;

// Typed failure of an agent operation. `code` drives API behavior; `message`
// is operator-facing English and is never shown raw to hosting customers.
message AgentError {
  // Machine-readable category of the failure.
  ErrorCode code = 1;
  // Operator-facing description (English, no secrets, no customer data).
  string message = 2;
  // Excerpt of the failing tool's output (e.g. `nginx -t` stderr), max 4 KiB.
  string tool_output = 3;
}

// Failure categories every operation maps into.
enum ErrorCode {
  ERROR_CODE_UNSPECIFIED = 0;
  // Input failed the agent's own validation (regex/path containment).
  ERROR_CODE_INVALID_INPUT = 1;
  // Idempotency outcome: the requested entity already exists.
  ERROR_CODE_ALREADY_EXISTS = 2;
  // Idempotency outcome: the requested entity does not exist.
  ERROR_CODE_NOT_FOUND = 3;
  // A rendered config failed its validator; previous state was restored.
  ERROR_CODE_VALIDATION_FAILED = 4;
  // Unexpected system failure (tool crashed, IO error); see tool_output.
  ERROR_CODE_SYSTEM_FAILURE = 5;
}

// Streamed progress of a long-running operation (backup, restore, install).
message Progress {
  // 0..100; monotonically non-decreasing per stream.
  uint32 percent = 1;
  // Short machine-stable stage id (e.g. "dumping_db"), i18n happens API-side.
  string stage = 2;
}
```

- [ ] **Step 2: Write `proto/agent/v1/system.proto`**

```proto
// System-level agent operations: identity/handshake.
syntax = "proto3";

package maran.agent.v1;

import "agent/v1/common.proto";

// Agent identity and environment, exchanged on every API connect.
service SystemService {
  // Returns agent version and distro info. The API refuses to operate if
  // proto_version is newer than it understands (upgrade window guard).
  rpc GetAgentInfo(GetAgentInfoRequest) returns (GetAgentInfoResponse);
}

// Empty request — present for additive evolution.
message GetAgentInfoRequest {}

// Handshake result.
message GetAgentInfoResponse {
  oneof result {
    AgentInfo ok = 1;
    AgentError error = 2;
  }
}

// Identity of a running agent.
message AgentInfo {
  // Semantic version of the agent binary, e.g. "0.1.0".
  string version = 1;
  // /etc/os-release ID, e.g. "ubuntu", "almalinux".
  string distro_id = 2;
  // Detected distro family driving the adapter choice.
  DistroFamily family = 3;
  // Highest contract revision this agent implements (starts at 1).
  uint32 proto_version = 4;
}

// Supported distro families (spec §4).
enum DistroFamily {
  DISTRO_FAMILY_UNSPECIFIED = 0;
  DISTRO_FAMILY_DEBIAN = 1;
  DISTRO_FAMILY_RHEL = 2;
}
```

- [ ] **Step 3: Write `scripts/proto-lint.sh`** (executable)

```bash
#!/usr/bin/env bash
# Validates that every proto file compiles standalone. Used by CI (cross job).
set -euo pipefail
cd "$(dirname "$0")/.."
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
protoc --proto_path=proto --descriptor_set_out="$out/all.pb" proto/agent/v1/*.proto
echo "PROTO-OK"
```

- [ ] **Step 4: Run it**

Run: `bash scripts/proto-lint.sh`
Expected: `PROTO-OK` (compile failure = fix the proto before proceeding).

- [ ] **Step 5: Checkpoint** — report; owner may commit (`feat: agent contract v1 — common + system handshake`).

---

### Task 3: Rust workspace + `maran-agent-core` (validation primitives)

**Files:**
- Create: `agent/Cargo.toml`, `agent/rustfmt.toml`, `agent/crates/agent-core/Cargo.toml`, `agent/crates/agent-core/src/lib.rs`, `agent/crates/agent-core/src/names.rs`, `agent/crates/agent-core/src/paths.rs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Tasks 4, 5 and all future ops): crate `maran-agent-core` exporting `names::AccountName` (validated newtype, `AccountName::parse(&str) -> Result<AccountName, NameError>`, `as_str()`), `paths::resolve_in_home(&AccountName, &Path) -> Result<PathBuf, PathError>`, errors `NameError{Invalid}`, `PathError{NotFound, EscapesHome}`.

- [ ] **Step 1: Workspace `agent/Cargo.toml`**

```toml
[workspace]
resolver = "2"
members = ["crates/agent-core"]

[workspace.package]
edition = "2024"
version = "0.1.0"
license = "BUSL-1.1"

[workspace.dependencies]
thiserror = "2"
regex = "1"
tokio = { version = "1", features = ["macros", "rt-multi-thread", "net"] }
tonic = "0.13"
prost = "0.13"
tonic-build = "0.13"
tracing = "0.1"
tracing-subscriber = { version = "0.3", features = ["env-filter"] }
tempfile = "3"

[workspace.lints.clippy]
unwrap_used = "deny"
expect_used = "deny"
panic = "deny"

[workspace.lints.rust]
missing_docs = "warn"
```

- [ ] **Step 2: `agent/rustfmt.toml`**

```toml
# rustfmt defaults; file exists to pin the intent (rules/rust.md).
edition = "2024"
```

- [ ] **Step 3: `agent-core` crate manifest** (`agent/crates/agent-core/Cargo.toml`)

```toml
[package]
name = "maran-agent-core"
edition.workspace = true
version.workspace = true
license.workspace = true
description = "Validation primitives and shared types for the Maran agent."

[dependencies]
thiserror.workspace = true
regex.workspace = true

[dev-dependencies]
tempfile.workspace = true

[lints]
workspace = true
```

- [ ] **Step 4: Failing tests first — `src/names.rs` tests + skeleton**

`agent/crates/agent-core/src/lib.rs`:

```rust
//! Validation primitives and shared types for the Maran agent.
//! Every command handler validates through these before touching the system
//! (defense in depth — see rules/rust.md and rules/security.md).

pub mod names;
pub mod paths;
```

`agent/crates/agent-core/src/names.rs` (tests included; implementation minimal-failing):

```rust
//! Account/site name validation: the single gate all system-facing names pass.

use std::sync::LazyLock;

use regex::Regex;

/// Matches valid account names: lowercase letter first, then lowercase
/// letters/digits/underscore, 3–30 chars total. Mirrors useradd constraints.
static NAME_RE: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^[a-z][a-z0-9_]{2,29}$").unwrap_or_else(|_| unreachable!()));

/// A validated hosting-account name, safe to embed in paths and unit names.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct AccountName(String);

/// Rejection reasons for [`AccountName::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum NameError {
    /// The candidate does not match the allowed pattern.
    #[error("invalid account name")]
    Invalid,
}

impl AccountName {
    /// Validates `candidate` and wraps it. The only way to construct the type.
    pub fn parse(candidate: &str) -> Result<Self, NameError> {
        if NAME_RE.is_match(candidate) {
            Ok(Self(candidate.to_owned()))
        } else {
            Err(NameError::Invalid)
        }
    }

    /// The validated name as a string slice.
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn valid_names_parse() {
        for ok in ["abc", "client_42", "a23456789012345678901234567890"] {
            assert!(AccountName::parse(ok).is_ok(), "{ok}");
        }
    }

    #[test]
    fn invalid_names_are_rejected() {
        for bad in ["", "ab", "1abc", "Abc", "a-b", "a b", "родной", "a".repeat(31).as_str(), "a;rm"] {
            assert_eq!(AccountName::parse(bad), Err(NameError::Invalid), "{bad}");
        }
    }
}
```

- [ ] **Step 5: `src/paths.rs` with symlink-escape tests**

```rust
//! Path containment: every customer-relative path resolves through here.

use std::path::{Path, PathBuf};

use crate::names::AccountName;

/// Rejection reasons for [`resolve_in_home`].
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum PathError {
    /// The path (or a parent) does not exist.
    #[error("path not found")]
    NotFound,
    /// After canonicalization the path leaves the account home (symlink/`..`).
    #[error("path escapes account home")]
    EscapesHome,
}

/// Base directory that contains all account homes.
const HOME_ROOT: &str = "/home";

/// Resolves `relative` inside `account`'s home, refusing traversal and
/// symlink escapes. Returns the canonical absolute path on success.
pub fn resolve_in_home(account: &AccountName, relative: &Path) -> Result<PathBuf, PathError> {
    resolve_under(&PathBuf::from(HOME_ROOT).join(account.as_str()), relative)
}

/// Testable core of [`resolve_in_home`] with an injectable home root.
fn resolve_under(home: &Path, relative: &Path) -> Result<PathBuf, PathError> {
    let joined = home.join(relative);
    let canonical = joined.canonicalize().map_err(|_| PathError::NotFound)?;
    let canonical_home = home.canonicalize().map_err(|_| PathError::NotFound)?;
    if !canonical.starts_with(&canonical_home) {
        return Err(PathError::EscapesHome);
    }
    Ok(canonical)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn path_inside_home_resolves() {
        let home = tempfile::tempdir().unwrap();
        std::fs::create_dir(home.path().join("site")).unwrap();
        let got = resolve_under(home.path(), Path::new("site")).unwrap();
        assert!(got.ends_with("site"));
    }

    #[test]
    fn dotdot_escape_is_rejected() {
        let home = tempfile::tempdir().unwrap();
        let err = resolve_under(home.path(), Path::new("../../etc/passwd")).unwrap_err();
        assert!(matches!(err, PathError::NotFound | PathError::EscapesHome));
    }

    #[test]
    fn symlink_escape_is_rejected() {
        let root = tempfile::tempdir().unwrap();
        let home = root.path().join("home");
        let outside = root.path().join("outside");
        std::fs::create_dir_all(&home).unwrap();
        std::fs::create_dir_all(&outside).unwrap();
        std::os::unix::fs::symlink(&outside, home.join("link")).unwrap();
        let err = resolve_under(&home, Path::new("link")).unwrap_err();
        assert!(matches!(err, PathError::EscapesHome));
    }
}
```

- [ ] **Step 6: Run tests**

Run: `cd agent && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test`
Expected: all green (fix warnings, they are errors). Note: tests exercise both accept and reject paths.

- [ ] **Step 7: Checkpoint** — report; possible commit `feat(agent): core validation primitives (names, path containment)`.

---

### Task 4: `maran-distro` — os-release detection + `DistroAdapter` seam

**Files:**
- Create: `agent/crates/distro/Cargo.toml`, `agent/crates/distro/src/lib.rs`, `agent/crates/distro/src/os_release.rs`, `agent/crates/distro/src/adapter.rs`
- Modify: `agent/Cargo.toml` (add member `crates/distro`)

**Interfaces:**
- Consumes: nothing.
- Produces (used by Task 5+): `DistroFamily{Debian, Rhel}`, `DistroInfo{id: String, family: DistroFamily, version_id: String}`, `detect() -> Result<DistroInfo, DetectError>`, `parse_os_release(&str) -> Result<DistroInfo, DetectError>`; trait `DistroAdapter{fn family(&self) -> DistroFamily}` (grows in later plans) + `adapter_for(family) -> &'static dyn DistroAdapter`.

- [ ] **Step 1: Crate manifest** (`agent/crates/distro/Cargo.toml`)

```toml
[package]
name = "maran-distro"
edition.workspace = true
version.workspace = true
license.workspace = true
description = "Distro family detection and the adapter seam isolating distro differences."

[dependencies]
thiserror.workspace = true

[lints]
workspace = true
```

- [ ] **Step 2: `src/os_release.rs` — tests first, then parser**

```rust
//! Parsing of /etc/os-release into a supported-distro decision.

use crate::adapter::DistroFamily;

/// Identity of the host distribution, parsed from os-release.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DistroInfo {
    /// os-release `ID` (e.g. "ubuntu", "debian", "almalinux", "rocky").
    pub id: String,
    /// Family the adapter layer keys on.
    pub family: DistroFamily,
    /// os-release `VERSION_ID` (e.g. "24.04", "9.4").
    pub version_id: String,
}

/// Why detection refused to run on this host.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DetectError {
    /// os-release is missing or unreadable.
    #[error("cannot read /etc/os-release")]
    Unreadable,
    /// The distro is not in the supported matrix (spec §4).
    #[error("unsupported distro: {id}")]
    Unsupported {
        /// The os-release ID we refused.
        id: String,
    },
}

/// Reads `/etc/os-release` and classifies the host.
pub fn detect() -> Result<DistroInfo, DetectError> {
    let content = std::fs::read_to_string("/etc/os-release").map_err(|_| DetectError::Unreadable)?;
    parse_os_release(&content)
}

/// Pure parser behind [`detect`]; fixture-testable.
pub fn parse_os_release(content: &str) -> Result<DistroInfo, DetectError> {
    let mut id = String::new();
    let mut version_id = String::new();
    for line in content.lines() {
        if let Some(v) = line.strip_prefix("ID=") {
            id = v.trim_matches('"').to_owned();
        } else if let Some(v) = line.strip_prefix("VERSION_ID=") {
            version_id = v.trim_matches('"').to_owned();
        }
    }
    let family = match id.as_str() {
        "ubuntu" | "debian" => DistroFamily::Debian,
        "almalinux" | "rocky" => DistroFamily::Rhel,
        _ => return Err(DetectError::Unsupported { id }),
    };
    Ok(DistroInfo { id, family, version_id })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ubuntu_and_debian_map_to_debian_family() {
        for (fixture, id) in [
            ("ID=ubuntu\nVERSION_ID=\"24.04\"\n", "ubuntu"),
            ("ID=debian\nVERSION_ID=\"12\"\n", "debian"),
        ] {
            let info = parse_os_release(fixture).unwrap();
            assert_eq!(info.family, DistroFamily::Debian);
            assert_eq!(info.id, id);
        }
    }

    #[test]
    fn alma_and_rocky_map_to_rhel_family() {
        for fixture in ["ID=\"almalinux\"\nVERSION_ID=\"9.4\"\n", "ID=\"rocky\"\nVERSION_ID=\"9.3\"\n"] {
            assert_eq!(parse_os_release(fixture).unwrap().family, DistroFamily::Rhel);
        }
    }

    #[test]
    fn unsupported_distro_is_refused() {
        let err = parse_os_release("ID=alpine\nVERSION_ID=\"3.20\"\n").unwrap_err();
        assert_eq!(err, DetectError::Unsupported { id: "alpine".into() });
    }
}
```

- [ ] **Step 3: `src/adapter.rs` — the seam (minimal for foundation)**

```rust
//! The DistroAdapter seam: ops code never branches on distro names,
//! it asks the adapter (rules/architecture.md). Grows in later plans.

/// Supported distro families (spec §4).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DistroFamily {
    /// Ubuntu / Debian.
    Debian,
    /// AlmaLinux / Rocky.
    Rhel,
}

/// Behavior that differs between distro families. Later plans add package
/// installation, service names, and firewall specifics here — additively.
pub trait DistroAdapter: Send + Sync {
    /// The family this adapter implements.
    fn family(&self) -> DistroFamily;
}

/// Adapter for the Debian family.
struct DebianAdapter;

/// Adapter for the RHEL family.
struct RhelAdapter;

impl DistroAdapter for DebianAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Debian
    }
}

impl DistroAdapter for RhelAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Rhel
    }
}

/// Returns the process-wide adapter for `family`.
pub fn adapter_for(family: DistroFamily) -> &'static dyn DistroAdapter {
    match family {
        DistroFamily::Debian => &DebianAdapter,
        DistroFamily::Rhel => &RhelAdapter,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn adapter_matches_requested_family() {
        assert_eq!(adapter_for(DistroFamily::Debian).family(), DistroFamily::Debian);
        assert_eq!(adapter_for(DistroFamily::Rhel).family(), DistroFamily::Rhel);
    }
}
```

- [ ] **Step 4: `src/lib.rs`**

```rust
//! Distro detection and the adapter seam for the Maran agent.

pub mod adapter;
pub mod os_release;

pub use adapter::{adapter_for, DistroAdapter, DistroFamily};
pub use os_release::{detect, parse_os_release, DetectError, DistroInfo};
```

- [ ] **Step 5: Add member + run**

Modify `agent/Cargo.toml` members: `members = ["crates/agent-core", "crates/distro"]`.
Run: `cd agent && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test`
Expected: green.

- [ ] **Step 6: Checkpoint** — possible commit `feat(agent): distro detection and adapter seam`.

---

### Task 5: `maran-agent` binary — tonic UDS server + peer-cred guard + handshake

**Files:**
- Create: `agent/crates/agent/Cargo.toml`, `agent/crates/agent/build.rs`, `agent/crates/agent/src/main.rs`, `agent/crates/agent/src/server.rs`, `agent/crates/agent/src/peercred.rs`, `agent/crates/agent/src/services/mod.rs`, `agent/crates/agent/src/services/system.rs`, `agent/tests/handshake.rs` (workspace-level integration test lives in `agent/crates/agent/tests/handshake.rs`)
- Modify: `agent/Cargo.toml` (member `crates/agent`)

**Interfaces:**
- Consumes: Task 2 protos; Task 4 `detect()`/`DistroFamily`.
- Produces: binary `maran-agent` with flags `--socket <path>` (default `/run/maran/agent.sock`) and `--allow-uid <uid>` (default: current uid; production installer passes the `panel` uid); gRPC `SystemService/GetAgentInfo` per contract. Env `MARAN_AGENT_LOG` controls tracing filter.

- [ ] **Step 1: Manifest + build script**

`agent/crates/agent/Cargo.toml`:

```toml
[package]
name = "maran-agent"
edition.workspace = true
version.workspace = true
license.workspace = true
description = "Maran root agent: typed gRPC operations over a unix socket."

[dependencies]
maran-agent-core = { path = "../agent-core" }
maran-distro = { path = "../distro" }
tokio.workspace = true
tonic.workspace = true
prost.workspace = true
tracing.workspace = true
tracing-subscriber.workspace = true
thiserror.workspace = true

[build-dependencies]
tonic-build.workspace = true

[dev-dependencies]
tempfile.workspace = true

[lints]
workspace = true
```

`agent/crates/agent/build.rs`:

```rust
//! Generates Rust types from the shared proto contract (rules/proto.md).

fn main() -> Result<(), Box<dyn std::error::Error>> {
    tonic_build::configure()
        .build_client(true) // client used by integration tests
        .compile_protos(
            &["../../../proto/agent/v1/common.proto", "../../../proto/agent/v1/system.proto"],
            &["../../../proto"],
        )?;
    println!("cargo:rerun-if-changed=../../../proto");
    Ok(())
}
```

- [ ] **Step 2: `src/peercred.rs` — the guard, unit-tested**

```rust
//! SO_PEERCRED enforcement: only the panel user's UID may talk to the agent.

/// Decides whether a peer uid is allowed to use the agent.
#[derive(Debug, Clone, Copy)]
pub struct PeerPolicy {
    /// The single uid allowed to connect (the `panel` user in production).
    allow_uid: u32,
}

impl PeerPolicy {
    /// Creates a policy allowing exactly `allow_uid`.
    pub fn new(allow_uid: u32) -> Self {
        Self { allow_uid }
    }

    /// Returns true iff `peer_uid` may use the agent.
    pub fn permits(&self, peer_uid: u32) -> bool {
        peer_uid == self.allow_uid
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_the_configured_uid_is_permitted() {
        let policy = PeerPolicy::new(1000);
        assert!(policy.permits(1000));
        assert!(!policy.permits(0));
        assert!(!policy.permits(1001));
    }
}
```

- [ ] **Step 3: `src/services/system.rs` — handshake service**

```rust
//! SystemService implementation: agent identity handshake.

use tonic::{Request, Response, Status};

use crate::proto::system_service_server::SystemService;
use crate::proto::{get_agent_info_response, AgentInfo, DistroFamily as ProtoFamily, GetAgentInfoRequest, GetAgentInfoResponse};

/// Contract revision implemented by this binary (bump per additive release).
const PROTO_VERSION: u32 = 1;

/// Serves agent identity to the API.
pub struct SystemSvc {
    /// Detected distro info captured at startup.
    distro: maran_distro::DistroInfo,
}

impl SystemSvc {
    /// Creates the service around the startup-detected `distro`.
    pub fn new(distro: maran_distro::DistroInfo) -> Self {
        Self { distro }
    }

    /// Maps the internal family enum onto the wire enum.
    fn wire_family(&self) -> ProtoFamily {
        match self.distro.family {
            maran_distro::DistroFamily::Debian => ProtoFamily::Debian,
            maran_distro::DistroFamily::Rhel => ProtoFamily::Rhel,
        }
    }
}

#[tonic::async_trait]
impl SystemService for SystemSvc {
    /// Returns version + distro identity; infallible by design.
    async fn get_agent_info(
        &self,
        _request: Request<GetAgentInfoRequest>,
    ) -> Result<Response<GetAgentInfoResponse>, Status> {
        let info = AgentInfo {
            version: env!("CARGO_PKG_VERSION").to_owned(),
            distro_id: self.distro.id.clone(),
            family: self.wire_family() as i32,
            proto_version: PROTO_VERSION,
        };
        Ok(Response::new(GetAgentInfoResponse {
            result: Some(get_agent_info_response::Result::Ok(info)),
        }))
    }
}
```

- [ ] **Step 4: `src/server.rs` + `src/main.rs` + module glue**

`src/services/mod.rs`:

```rust
//! gRPC service implementations, one file per proto service.

pub mod system;
```

`src/server.rs`:

```rust
//! UDS server assembly: socket setup, peer-cred enforcement, service registry.

use std::path::Path;

use tokio::net::UnixListener;
use tokio_stream::wrappers::UnixListenerStream;
use tonic::service::Interceptor;
use tonic::transport::server::UdsConnectInfo;
use tonic::{Request, Status};

use crate::peercred::PeerPolicy;
use crate::proto::system_service_server::SystemServiceServer;
use crate::services::system::SystemSvc;

/// Rejects requests whose unix peer uid violates the policy.
#[derive(Clone)]
pub struct PeerGuard {
    /// The allow-one-uid policy from CLI flags.
    policy: PeerPolicy,
}

impl PeerGuard {
    /// Wraps `policy` as a tonic interceptor.
    pub fn new(policy: PeerPolicy) -> Self {
        Self { policy }
    }
}

impl Interceptor for PeerGuard {
    /// Extracts SO_PEERCRED from connect info and enforces the policy.
    fn call(&mut self, request: Request<()>) -> Result<Request<()>, Status> {
        let uid = request
            .extensions()
            .get::<UdsConnectInfo>()
            .and_then(|info| info.peer_cred)
            .map(|cred| cred.uid());
        match uid {
            Some(uid) if self.policy.permits(uid) => Ok(request),
            Some(uid) => Err(Status::permission_denied(format!("uid {uid} is not permitted"))),
            None => Err(Status::permission_denied("peer credentials unavailable")),
        }
    }
}

/// Binds `socket_path` (mode 0660) and serves until shutdown.
pub async fn serve(socket_path: &Path, policy: PeerPolicy) -> Result<(), Box<dyn std::error::Error>> {
    if socket_path.exists() {
        std::fs::remove_file(socket_path)?;
    }
    if let Some(dir) = socket_path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let listener = UnixListener::bind(socket_path)?;
    std::fs::set_permissions(socket_path, std::os::unix::fs::PermissionsExt::from_mode(0o660))?;
    let distro = maran_distro::detect()?;
    tracing::info!(socket = %socket_path.display(), distro = %distro.id, "agent listening");
    tonic::transport::Server::builder()
        .add_service(SystemServiceServer::with_interceptor(SystemSvc::new(distro), PeerGuard::new(policy)))
        .serve_with_incoming(UnixListenerStream::new(listener))
        .await?;
    Ok(())
}
```

`src/main.rs`:

```rust
//! maran-agent entrypoint: flag parsing, tracing setup, server start.

mod peercred;
mod server;
mod services;

/// Generated contract types (never edited by hand — rules/proto.md).
pub mod proto {
    tonic::include_proto!("maran.agent.v1");
}

use std::path::PathBuf;

use peercred::PeerPolicy;

/// Default production socket path (spec §9).
const DEFAULT_SOCKET: &str = "/run/maran/agent.sock";

/// Parses `--socket` and `--allow-uid` flags with safe defaults.
fn parse_flags() -> (PathBuf, u32) {
    let mut socket = PathBuf::from(DEFAULT_SOCKET);
    // SAFETY-free way to read euid: rustix/libc not needed — std exposes it via metadata of /proc/self.
    let mut allow_uid = current_uid();
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--socket" => {
                if let Some(v) = args.next() {
                    socket = PathBuf::from(v);
                }
            }
            "--allow-uid" => {
                if let Some(v) = args.next().and_then(|v| v.parse().ok()) {
                    allow_uid = v;
                }
            }
            _ => {}
        }
    }
    (socket, allow_uid)
}

/// Effective uid of this process (used as the dev-mode default policy).
fn current_uid() -> u32 {
    std::os::unix::fs::MetadataExt::uid(
        &std::fs::metadata("/proc/self").unwrap_or_else(|_| std::process::exit(1)),
    )
}

/// Starts tracing and the UDS server; exits non-zero on fatal setup errors.
#[tokio::main]
async fn main() {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("MARAN_AGENT_LOG")
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info")),
        )
        .init();
    let (socket, allow_uid) = parse_flags();
    if let Err(error) = server::serve(&socket, PeerPolicy::new(allow_uid)).await {
        tracing::error!(%error, "agent failed");
        std::process::exit(1);
    }
}
```

Add `tokio-stream = { version = "0.1", features = ["net"] }` to `[workspace.dependencies]` and the agent's `[dependencies]`.

- [ ] **Step 5: Integration test `agent/crates/agent/tests/handshake.rs`** (run the server in-process on a temp socket, call it with the generated client)

```rust
//! End-to-end handshake over a real unix socket (same-uid path).

use tonic::transport::{Endpoint, Uri};
use tower::service_fn;

/// Generated contract types for the test client.
pub mod proto {
    tonic::include_proto!("maran.agent.v1");
}

#[tokio::test]
async fn handshake_returns_agent_info_over_uds() {
    let dir = tempfile::tempdir().unwrap();
    let sock = dir.path().join("agent.sock");
    let server_sock = sock.clone();
    // The test host must be a supported distro (CI containers are); skip otherwise.
    if maran_distro::detect().is_err() {
        eprintln!("skipping: unsupported host distro");
        return;
    }
    tokio::spawn(async move {
        let policy = maran_agent::peercred_policy_for_tests();
        maran_agent::serve_for_tests(&server_sock, policy).await;
    });
    tokio::time::sleep(std::time::Duration::from_millis(200)).await;

    let channel = Endpoint::try_from("http://uds.invalid")
        .unwrap()
        .connect_with_connector(service_fn(move |_: Uri| {
            let sock = sock.clone();
            async move {
                Ok::<_, std::io::Error>(hyper_util::rt::TokioIo::new(
                    tokio::net::UnixStream::connect(sock).await?,
                ))
            }
        }))
        .await
        .unwrap();

    let mut client = proto::system_service_client::SystemServiceClient::new(channel);
    let resp = client
        .get_agent_info(proto::GetAgentInfoRequest {})
        .await
        .unwrap()
        .into_inner();
    match resp.result.unwrap() {
        proto::get_agent_info_response::Result::Ok(info) => {
            assert_eq!(info.proto_version, 1);
            assert!(!info.version.is_empty());
        }
        proto::get_agent_info_response::Result::Error(e) => panic!("unexpected error: {e:?}"),
    }
}
```

Expose the two `_for_tests` helpers from `main.rs` via a small `lib.rs` (`agent/crates/agent/src/lib.rs`) so the integration test can start the server in-process:

```rust
//! Test-facing surface of the agent binary crate.

pub mod peercred;
pub mod server;
pub mod services;

/// Generated contract types (single include point for the crate).
pub mod proto {
    tonic::include_proto!("maran.agent.v1");
}

use std::path::Path;

/// Policy allowing the current (test) uid — mirrors the dev default.
pub fn peercred_policy_for_tests() -> peercred::PeerPolicy {
    peercred::PeerPolicy::new(unsafe { libc_free_current_uid() })
}

/// Serves on `socket` swallowing errors — for test harness use only.
pub async fn serve_for_tests(socket: &Path, policy: peercred::PeerPolicy) {
    let _ = server::serve(socket, policy).await;
}

/// Reads the effective uid without libc (proc metadata).
fn libc_free_current_uid() -> u32 { /* same body as main.rs current_uid */ }
```

(Adjust `main.rs` to consume the lib: `use maran_agent::…` — binary keeps only flag parsing + main. Add dev-deps `tower`, `hyper-util` to the agent crate.)

- [ ] **Step 6: Run everything**

Run: `cd agent && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test`
Expected: unit + integration green. The handshake test proves: UDS bind, 0660 perms, interceptor allow-path, proto round-trip.

- [ ] **Step 7: Deny-path check** — add to `handshake.rs`:

```rust
#[tokio::test]
async fn foreign_uid_is_rejected() {
    // PeerPolicy is pure; the full socket-level deny needs a second uid and is
    // covered by the installer E2E (Plan 8). Here we pin the policy contract:
    let policy = maran_agent::peercred::PeerPolicy::new(12345);
    assert!(!policy.permits(0), "root must NOT be implicitly allowed");
    assert!(!policy.permits(54321));
}
```

Run: `cargo test` → green.

- [ ] **Step 8: Checkpoint** — possible commit `feat(agent): UDS gRPC server with peer-cred guard and handshake`.

---

### Task 6: C# solution scaffold — Host with `/health`

**Files:**
- Create: `backend/.editorconfig` (copy of hostpanel's rules — 4sp/120/LF, naming, var, braces), `backend/Directory.Build.props`, `backend/Directory.Packages.props`, `backend/nuget.config`, `backend/Maran.sln`, `backend/src/Maran.Host/Maran.Host.csproj`, `backend/src/Maran.Host/Program.cs`, `backend/tests/Maran.Host.Tests/Maran.Host.Tests.csproj`, `backend/tests/Maran.Host.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `dotnet test` green solution; `Maran.Host` minimal API exposing `GET /health` → 200 `{"status":"ok"}`; `Program` public for `WebApplicationFactory`.

- [ ] **Step 1: `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: `Directory.Packages.props`** (versions live ONLY here)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.*" />
    <PackageVersion Include="xunit" Version="2.9.*" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.*" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Host project + failing test first**

`backend/tests/Maran.Host.Tests/HealthEndpointTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.Tests;

/// <summary>Boot-level smoke tests of the host pipeline.</summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 4: Run to see it fail**

Run: `cd backend && dotnet test`
Expected: FAIL (Program/endpoint missing).

- [ ] **Step 5: Minimal `Program.cs`**

```csharp
namespace Maran.Host;

/// <summary>Composition root of maran-api: builds the pipeline and maps modules.</summary>
public sealed class Program
{
    /// <summary>Builds and runs the web host.</summary>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.Run();
    }
}
```

(csproj files: Host = `Microsoft.NET.Sdk.Web`, tests reference Host + xunit set; sln includes both.)

- [ ] **Step 6: Run to green**

Run: `cd backend && dotnet test`
Expected: PASS, zero warnings.

- [ ] **Step 7: Checkpoint** — possible commit `feat(backend): host skeleton with health endpoint`.

---

### Task 7: `Maran.SharedKernel` — Result/Error/ICurrentUser/IClock

**Files:**
- Create: `backend/src/Maran.SharedKernel/Maran.SharedKernel.csproj`, `.../Results/Error.cs`, `.../Results/Result.cs`, `.../Abstractions/IClock.cs`, `.../Abstractions/ICurrentUser.cs`, `.../Abstractions/SystemClock.cs`, `backend/tests/Maran.SharedKernel.Tests/...` (`ResultTests.cs`)
- Modify: `backend/Maran.sln`

**Interfaces:**
- Produces (every module consumes): `Error(string Code, string Message)` with factory `Error.Of(code, message)`; `Result<T>` with `Ok(T)`, `Fail(Error)`, `IsSuccess`, `Value` (throws on failure access — bug guard), `Error`, `Match<TOut>(onOk, onFail)`; `IClock { DateTimeOffset UtcNow { get; } }`; `SystemClock : IClock`; `ICurrentUser { Guid UserId; Guid? AccountId; bool IsAdmin; }`.

- [ ] **Step 1: Failing tests** (`ResultTests.cs`)

```csharp
namespace Maran.SharedKernel.Tests;

using Maran.SharedKernel.Results;

/// <summary>Behavioral contract of Result&lt;T&gt;.</summary>
public sealed class ResultTests
{
    [Fact]
    public void Ok_result_carries_value()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failed_result_carries_error_and_guards_value()
    {
        var result = Result<int>.Fail(Error.Of("sites.domain_taken", "Domain already exists"));

        Assert.False(result.IsSuccess);
        Assert.Equal("sites.domain_taken", result.Error!.Code);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Match_routes_to_the_correct_branch()
    {
        var ok = Result<int>.Ok(1).Match(v => $"ok:{v}", e => $"err:{e.Code}");
        var fail = Result<int>.Fail(Error.Of("x", "y")).Match(v => $"ok:{v}", e => $"err:{e.Code}");

        Assert.Equal("ok:1", ok);
        Assert.Equal("err:x", fail);
    }
}
```

- [ ] **Step 2: Run → FAIL** (`dotnet test`)

- [ ] **Step 3: Implement**

`Results/Error.cs`:

```csharp
namespace Maran.SharedKernel.Results;

/// <summary>
/// A typed domain failure. <paramref name="Code"/> is machine-stable
/// ("module.reason", drives HTTP mapping and i18n); <paramref name="Message"/>
/// is operator-facing English and never shown raw to customers.
/// </summary>
public sealed record Error(string Code, string Message)
{
    /// <summary>Creates an error, guarding against empty codes.</summary>
    public static Error Of(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Error(code, message);
    }
}
```

`Results/Result.cs`:

```csharp
namespace Maran.SharedKernel.Results;

/// <summary>Outcome of a domain operation: a value or a typed error — never both.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    /// <summary>True when the operation produced a value.</summary>
    public bool IsSuccess { get; }

    /// <summary>The error of a failed result; null on success.</summary>
    public Error? Error { get; }

    /// <summary>The success value. Accessing it on a failure is a programming bug.</summary>
    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {Error!.Code}");

    /// <summary>Internal constructor; use <see cref="Ok"/> / <see cref="Fail"/>.</summary>
    private Result(bool success, T? value, Error? error)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
    }

    /// <summary>Wraps a success value.</summary>
    public static Result<T> Ok(T value) => new(true, value, null);

    /// <summary>Wraps a typed failure.</summary>
    public static Result<T> Fail(Error error) => new(false, default, error);

    /// <summary>Folds both branches into one value.</summary>
    public TOut Match<TOut>(Func<T, TOut> onOk, Func<Error, TOut> onFail) =>
        IsSuccess ? onOk(_value!) : onFail(Error!);
}
```

`Abstractions/IClock.cs` / `SystemClock.cs` / `ICurrentUser.cs`:

```csharp
namespace Maran.SharedKernel.Abstractions;

/// <summary>Injectable time source; DateTime.Now is forbidden (rules/csharp.md).</summary>
public interface IClock
{
    /// <summary>Current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
```

```csharp
namespace Maran.SharedKernel.Abstractions;

/// <summary>Production clock backed by the OS.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

```csharp
namespace Maran.SharedKernel.Abstractions;

/// <summary>The authenticated principal of the current request/message.</summary>
public interface ICurrentUser
{
    /// <summary>Panel user id.</summary>
    Guid UserId { get; }

    /// <summary>Owning account for Customer contexts; null for Admin.</summary>
    Guid? AccountId { get; }

    /// <summary>True for server administrators.</summary>
    bool IsAdmin { get; }
}
```

- [ ] **Step 4: Run → PASS** (`dotnet test`, zero warnings)

- [ ] **Step 5: Checkpoint** — possible commit `feat(backend): shared kernel result/error/clock/current-user`.

---

### Task 8: `Maran.Agent.Client` — codegen + typed wrapper

**Files:**
- Create: `backend/src/Maran.Agent.Client/Maran.Agent.Client.csproj`, `.../AgentChannel.cs`, `.../AgentInfoDto.cs`, `.../IAgentSystemClient.cs`, `.../AgentSystemClient.cs`, `backend/tests/Maran.Agent.Client.Tests/AgentSystemClientTests.cs`
- Modify: `backend/Directory.Packages.props` (add `Grpc.Tools`, `Grpc.Net.Client`, `Google.Protobuf`), `backend/Maran.sln`

**Interfaces:**
- Consumes: Task 2 protos, Task 7 `Result<T>`.
- Produces (Host and modules consume): `IAgentSystemClient { Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct); }`; `AgentInfoDto(string Version, string DistroId, string Family, uint ProtoVersion)`; `AgentChannel.CreateUnixSocket(string socketPath) -> GrpcChannel`.

- [ ] **Step 1: csproj with proto codegen**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Google.Protobuf" />
    <PackageReference Include="Grpc.Net.Client" />
    <PackageReference Include="Grpc.Tools" PrivateAssets="all" />
    <Protobuf Include="../../../proto/agent/v1/*.proto" ProtoRoot="../../../proto" GrpcServices="Client" />
    <ProjectReference Include="../Maran.SharedKernel/Maran.SharedKernel.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Failing test — wrapper maps oneof to Result** (in-process fake server via `Grpc.Core.Testing`-free route: implement the generated *client* against a `CallInvoker` stub)

```csharp
namespace Maran.Agent.Client.Tests;

using Maran.Agent.V1;

/// <summary>Mapping contract of AgentSystemClient (proto oneof → Result).</summary>
public sealed class AgentSystemClientTests
{
    [Fact]
    public async Task Ok_payload_maps_to_success_result()
    {
        var response = new GetAgentInfoResponse
        {
            Ok = new AgentInfo { Version = "0.1.0", DistroId = "ubuntu", Family = DistroFamily.Debian, ProtoVersion = 1 },
        };
        var client = new AgentSystemClient(new StubSystemService(response));

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ubuntu", result.Value.DistroId);
    }

    [Fact]
    public async Task Error_payload_maps_to_failed_result_with_agent_code()
    {
        var response = new GetAgentInfoResponse
        {
            Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "boom" },
        };
        var client = new AgentSystemClient(new StubSystemService(response));

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("agent.system_failure", result.Error!.Code);
    }
}
```

(`StubSystemService` implements the small `ISystemServiceInvoker` seam defined in Step 3 — one file, returns the canned response.)

- [ ] **Step 3: Implement wrapper**

`IAgentSystemClient.cs`:

```csharp
namespace Maran.Agent.Client;

using Maran.SharedKernel.Results;

/// <summary>Typed access to the agent's SystemService.</summary>
public interface IAgentSystemClient
{
    /// <summary>Performs the identity handshake with the local agent.</summary>
    Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct);
}
```

`AgentInfoDto.cs`:

```csharp
namespace Maran.Agent.Client;

/// <summary>Agent identity as seen by the backend (decoupled from wire types).</summary>
public sealed record AgentInfoDto(string Version, string DistroId, string Family, uint ProtoVersion);
```

`AgentSystemClient.cs` — maps oneof → `Result`, error codes as `"agent." + code.ToString().ToSnakeCase()`; constructor takes the `ISystemServiceInvoker` seam; a second constructor takes `GrpcChannel` for production. `AgentChannel.cs`:

```csharp
namespace Maran.Agent.Client;

using System.Net.Sockets;
using Grpc.Net.Client;

/// <summary>Builds gRPC channels over the agent's unix socket.</summary>
public static class AgentChannel
{
    /// <summary>Creates a channel connected to <paramref name="socketPath"/>.</summary>
    public static GrpcChannel CreateUnixSocket(string socketPath)
    {
        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(endpoint, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            },
        });
    }
}
```

- [ ] **Step 4: Run → PASS** (`dotnet test`)

- [ ] **Step 5: Checkpoint** — possible commit `feat(backend): typed agent client with unix-socket channel`.

---

### Task 9: Sdk + module discovery + PgSQL/Wolverine wiring + architecture tests

**Files:**
- Create: `backend/src/Maran.Sdk/Maran.Sdk.csproj`, `.../IPanelModule.cs`, `backend/src/Maran.Host/ModuleRegistry.cs`, `backend/tests/Maran.ArchitectureTests/{Maran.ArchitectureTests.csproj,ModuleIsolationTests.cs}`, `backend/tests/Maran.Host.IntegrationTests/{…csproj,HostBootTests.cs}`
- Modify: `backend/src/Maran.Host/Program.cs`, `backend/Directory.Packages.props` (add `Npgsql.EntityFrameworkCore.PostgreSQL`, `WolverineFx`, `WolverineFx.Postgresql`, `NetArchTest.Rules`, `Testcontainers.PostgreSql`), `backend/Maran.sln`

**Interfaces:**
- Consumes: Tasks 6–8.
- Produces: `IPanelModule { string Name { get; } void ConfigureServices(IServiceCollection services, IConfiguration configuration); void MapEndpoints(IEndpointRouteBuilder endpoints); }`; `ModuleRegistry.All` (explicit list — no reflection magic); Host boots with `ConnectionStrings:Panel` + Wolverine durable PgSQL; DI exposes `IAgentSystemClient`, `IClock`.

- [ ] **Step 1: `IPanelModule.cs`** (Sdk)

```csharp
namespace Maran.Sdk;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The contract every panel module implements — internal v1 modules and
/// marketplace modules share this exact shape (spec §13). Grows additively.
/// </summary>
public interface IPanelModule
{
    /// <summary>Stable machine name; equals the PostgreSQL schema name.</summary>
    string Name { get; }

    /// <summary>Registers the module's services and options.</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's HTTP endpoints.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
```

- [ ] **Step 2: `ModuleRegistry.cs`** (Host) — empty explicit list for now:

```csharp
namespace Maran.Host;

using Maran.Sdk;

/// <summary>Explicit registry of compiled-in modules (plans 2+ add entries).</summary>
public static class ModuleRegistry
{
    /// <summary>All modules in load order. Deliberately explicit — no assembly scanning.</summary>
    public static IReadOnlyList<IPanelModule> All { get; } = [];
}
```

- [ ] **Step 3: Wire Program.cs** — Npgsql connection (from `ConnectionStrings:Panel`), `builder.Host.UseWolverine(opts => opts.PersistMessagesWithPostgresql(connectionString))`, register `IClock/SystemClock`, `IAgentSystemClient` (socket path from `Agent:SocketPath`, default `/run/maran/agent.sock`), loop modules: `ConfigureServices` then after `Build()` → `MapEndpoints`. Keep `/health` returning also `agent: connected|unavailable` (non-fatal at boot).

- [ ] **Step 4: Failing integration test** (`HostBootTests.cs`, Testcontainers):

```csharp
namespace Maran.Host.IntegrationTests;

using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

/// <summary>Boots the real host against a disposable PostgreSQL.</summary>
public sealed class HostBootTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <inheritdoc />
    public Task InitializeAsync() => _pg.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Host_boots_with_postgres_and_serves_health()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Panel", _pg.GetConnectionString()));

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }
}
```

- [ ] **Step 5: Architecture tests** (`ModuleIsolationTests.cs`):

```csharp
namespace Maran.ArchitectureTests;

using NetArchTest.Rules;

/// <summary>CI-enforced module isolation (rules/architecture.md).</summary>
public sealed class ModuleIsolationTests
{
    [Fact]
    public void Modules_reference_only_sdk_and_shared_kernel()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.Modules")
            .ShouldNot().HaveDependencyOnAny("Maran.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Shared_kernel_depends_on_nothing_of_ours()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.SharedKernel")
            .ShouldNot().HaveDependencyOnAny("Maran.Host", "Maran.Sdk", "Maran.Modules", "Maran.Agent.Client")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
```

(Cross-module isolation rule activates with the first two modules in Plan 2; the harness exists now.)

- [ ] **Step 6: Run** — `cd backend && dotnet test` (needs Docker for Testcontainers). Expected: green.

- [ ] **Step 7: Checkpoint** — possible commit `feat(backend): sdk module contract, pgsql+wolverine wiring, architecture tests`.

---

### Task 10: Frontend scaffold — SPA shell

**Files:**
- Create: `frontend/package.json`, `frontend/vite.config.ts`, `frontend/tsconfig.json`, `frontend/tsconfig.app.json`, `frontend/tsconfig.node.json`, `frontend/eslint.config.ts`, `frontend/.oxlintrc.json`, `frontend/tailwind.config.ts`, `frontend/index.html`, `frontend/src/main.ts`, `frontend/src/App.vue`, `frontend/src/pinia.ts`, `frontend/src/router/index.ts`, `frontend/src/utils/http.ts`, `frontend/src/locales/{en,ru,hy}/app.json`, `frontend/src/i18n.ts`, `frontend/src/views/SystemStatusView.vue`, `frontend/src/App.spec.ts`

**Interfaces:**
- Consumes: backend `GET /health` (dev proxy → `http://localhost:5000`).
- Produces: `npm run dev|build|test|lint` all working; `http.get<T>(path): Promise<T>` typed helper; router with `/` → `SystemStatusView`; i18n initialized with `en/ru/hy`, key `app.title = "Maran"`.

- [ ] **Step 1: Scaffold with Vite** — `npm create vite@latest frontend -- --template vue-ts`, then add deps: `npm i vue-router@4 pinia vue-i18n@11 && npm i -D tailwindcss @tailwindcss/vite oxlint eslint eslint-plugin-vue vue-tsc vitest @vue/test-utils jsdom` (pin exact versions in package.json).
- [ ] **Step 2: Wire Tailwind (vite plugin), router, pinia, i18n** — `App.vue` carries a doc comment header, renders `<RouterView/>` inside a minimal layout with `{{ t('app.title') }}`. `SystemStatusView.vue` calls `http.get<{status: string}>('/health')` on mount and renders the status + agent state.
- [ ] **Step 3: `src/utils/http.ts`**

```ts
/**
 * Typed HTTP client for the panel API. All module api.ts files use this;
 * components never call fetch directly (rules/vue.md). RFC 7807 errors are
 * parsed into typed ApiError instances.
 */
export class ApiError extends Error {
  /** Machine-stable problem code used for i18n lookup. */
  readonly code: string
  /** HTTP status of the failed call. */
  readonly status: number

  /** Builds an error from a problem+json payload. */
  constructor(status: number, code: string, message: string) {
    super(message)
    this.code = code
    this.status = status
  }
}

/** Performs a GET returning the parsed JSON body of type T. */
export async function get<T>(path: string): Promise<T> {
  const response = await fetch(path, { headers: { Accept: 'application/json' } })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}))
    throw new ApiError(response.status, problem.code ?? 'unknown', problem.title ?? response.statusText)
  }
  return (await response.json()) as T
}
```

- [ ] **Step 4: Smoke test `App.spec.ts`** (vitest + jsdom): mounts App with router/i18n/pinia test harness, asserts `app.title` renders in all three locales (loop `en/ru/hy` setting `i18n.global.locale`).
- [ ] **Step 5: Run all gates** — `npm run lint && npm run test -- --run && npm run build` → green; `vue-tsc` clean.
- [ ] **Step 6: Checkpoint** — possible commit `feat(frontend): spa shell with router, pinia, i18n, tailwind`.

---

### Task 11: Dev environment — docker compose + polygon images

**Files:**
- Create: `docker/docker-compose.dev.yml`, `docker/polygon/ubuntu24.Dockerfile`, `docker/polygon/alma9.Dockerfile`, `docker/README.md`

**Interfaces:**
- Consumes: nothing. Produces: `docker compose -f docker/docker-compose.dev.yml up -d` → PostgreSQL 16 on `localhost:5432` (user/pass/db `maran_dev`); polygon images build and can run the agent binary for manual/CI testing.

- [ ] **Step 1: compose file**

```yaml
# Dev-only services (production never uses Docker — spec §2).
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: maran_dev
      POSTGRES_PASSWORD: maran_dev
      POSTGRES_DB: maran_dev
    ports: ["5432:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
volumes:
  pgdata:
```

- [ ] **Step 2: polygon Dockerfiles** — `ubuntu24.Dockerfile`: `FROM ubuntu:24.04`, install `ca-certificates`, create `/run/maran`, copy nothing (binary mounted at runtime: `docker run -v $PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent …`). Same for `alma9.Dockerfile` from `almalinux:9`. Each ends with a comment documenting the intended `docker run` invocation.
- [ ] **Step 3: Verify** — `docker compose -f docker/docker-compose.dev.yml up -d && docker compose -f docker/docker-compose.dev.yml ps` (postgres healthy), `docker build -f docker/polygon/ubuntu24.Dockerfile docker/polygon` and alma9 build OK.
- [ ] **Step 4: Checkpoint** — possible commit `chore: dev compose and distro polygon images`.

---

### Task 12: CI — four workflows + cross handshake job

**Files:**
- Create: `.github/workflows/backend.yml`, `.github/workflows/agent.yml`, `.github/workflows/frontend.yml`, `.github/workflows/cross.yml`

**Interfaces:**
- Consumes: every prior task's commands (they are the CI steps).
- Produces: PR gates per rules/testing.md.

- [ ] **Step 1: `agent.yml`** — on PR paths `agent/**`, `proto/**`: ubuntu-24.04; steps: checkout, `dtolnay/rust-toolchain@stable`, `sudo apt-get install -y protobuf-compiler`, `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test`, `RUSTDOCFLAGS="-D warnings" cargo doc --no-deps`.
- [ ] **Step 2: `backend.yml`** — on PR paths `backend/**`, `proto/**`: `actions/setup-dotnet@v4` (9.0.x), `sudo apt-get install -y protobuf-compiler`, `dotnet test backend` (Testcontainers uses the runner's Docker).
- [ ] **Step 3: `frontend.yml`** — `actions/setup-node@v4` (node 20), `npm ci`, `npm run lint`, `npx vue-tsc --noEmit`, `npm run test -- --run`, `npm run build` in `frontend/`.
- [ ] **Step 4: `cross.yml`** — proto lint (`bash scripts/proto-lint.sh`) + handshake E2E: build agent (`cargo build -p maran-agent`), start it on a temp socket with `--allow-uid $(id -u)`, then `dotnet run --project backend/src/Maran.Host` with `Agent:SocketPath` pointing at it and curl `/health` asserting `"agent":"connected"`; kill agent. Script the sequence in `scripts/e2e-handshake.sh` (created here) so it runs identically locally.
- [ ] **Step 5: Validate workflow YAML** — `python3 -c "import yaml,glob; [yaml.safe_load(open(f)) for f in glob.glob('.github/workflows/*.yml')]; print('YAML-OK')"` and, if available, `actionlint`.
- [ ] **Step 6: Checkpoint** — possible commit `ci: backend, agent, frontend and cross handshake workflows`.

---

### Task 13: Foundation verification sweep

**Files:** none new — this is the gate.

- [ ] **Step 1:** `bash scripts/preflight.sh` → exit 0.
- [ ] **Step 2:** `bash scripts/proto-lint.sh` → `PROTO-OK`.
- [ ] **Step 3:** `cd agent && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test` → green.
- [ ] **Step 4:** `cd backend && dotnet test` → green, zero warnings.
- [ ] **Step 5:** `cd frontend && npm run lint && npm run test -- --run && npm run build` → green.
- [ ] **Step 6:** `bash scripts/e2e-handshake.sh` → `HANDSHAKE-OK` (agent+api over UDS).
- [ ] **Step 7:** Review sweep against `rules/`: every new file one-type-per-file, doc comments everywhere, no forbidden constructs (`grep -rn "sh -c\|DateTime.Now\|unwrap()" backend/src agent/crates --include="*.cs" --include="*.rs"` — expect zero hits in production code).
- [ ] **Step 8:** Report results to the owner with the full command outputs; request permission for the foundation commit(s). **Do not commit without it.**

## Plan self-review (done at authoring)

- Spec coverage: foundation covers spec §5 (processes/transport), §6 (repo/module skeleton), §7 (style via rules/ + configs), §9 (socket+peercred+handshake slice of the agent), start of §15 (health), §16 (test harnesses, CI). Modules, auth, tenancy, licensing, installer intentionally live in Plans 2–8 (roadmap).
- No placeholders: every step carries runnable content or exact commands.
- Type consistency: `Result<T>`/`Error` (Task 7) match usage in Task 8; proto names (Task 2) match Rust (Task 5) and C# (Task 8) usage; `IPanelModule` (Task 9) matches ModuleRegistry.
