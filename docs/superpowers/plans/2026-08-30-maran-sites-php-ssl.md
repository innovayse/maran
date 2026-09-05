# Sites, Multi-PHP and SSL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A customer can create a site, choose its PHP version, and serve it over HTTPS — with nginx and php-fpm configured by the agent, never by a shell string.

**Architecture:** The agent owns everything that touches the host: it renders nginx vhosts and php-fpm pools from embedded askama templates through one write path (render → temp → fsync → validate → atomic rename → reload → rollback), and it writes only its own files under `/etc/maran/`, included by the distribution's nginx. The panel owns everything that decides: plan limits, ACME ordering and renewal, the audit trail, and the database of record. ACME is entirely C# — the agent only places certificate files and reloads.

**Tech Stack:** Rust (tonic, askama), C# .NET 9 (Wolverine, EF Core, PostgreSQL), Vue 3 + TypeScript + Tailwind, nginx, php-fpm.

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md` — §4 (OS matrix), §8 (tenancy and limits), §9 (agent contract), §11 (module behaviour), §12 (provisioning), §15 (observability), §16 (testing), §17 (UI).

**Issue:** https://github.com/innovayse/maran/issues/3

## Global Constraints

Copied from the spec and `rules/`; every task inherits them.

- **No shell strings, anywhere.** Processes are spawned with argv arrays against absolute paths supplied by `DistroAdapter`. There is no "run this command" RPC and there will not be one (spec §9).
- **The agent writes only its own files:** `/etc/maran/nginx/sites/*.conf`, included by the distribution's nginx. It never edits a file it does not own (spec §9).
- **One config write path:** render from an embedded askama template → temp file → validate (`nginx -t`) → atomic rename → reload → on failure roll back and return a typed error (spec §9).
- **Every operation is idempotent**, and every one emits an audit event (spec §9, §10).
- **The agent distrusts the caller:** every input is re-validated inside the agent — strict name regexes, canonicalised paths, nothing outside `/home/<account>/` (spec §9).
- **Customer file operations run under the account's uid** (fork + setuid), never as root (spec §9).
- **Document root is exactly** `/home/<account>/sites/<domain>/` (spec §11).
- **One php-fpm pool per account × version**, running under the account's uid, with `pm.max_children` from the plan's worker limit (spec §8, §11).
- **PHP 7.4–8.4**, from Sury on the Debian family and Remi on the RHEL family (spec §4, §11).
- **Customers get a whitelisted subset of PHP settings** through pool overrides, never raw ini text — and the agent re-validates the whitelist itself (spec §11 with §9).
- **Let's Encrypt HTTP-01 over webroot only.** Wildcard and DNS-01 wait for the DNS module (spec §11).
- **Renewal runs 30 days before expiry**, as a Wolverine scheduled job in C# (spec §11).
- **MaxSites is enforced in the application layer at creation time**; the worker limit is materialised into the rendered pool (spec §8).
- **Every tenant table carries `AccountId` and an EF Core global query filter**, so another tenant's row is never physically returned; each account-scoped endpoint carries an IDOR test (spec §8).
- **No platform literal outside `crates/distro`.** `maran structure` fails the build on one (rules/rust.md).
- **Modules reference only `Maran.Sdk` and `Maran.SharedKernel`**, never each other; NetArchTest enforces it (spec §6).
- **All three locales from day one** — en, ru, hy — with key parity checked by `maran structure` (spec §17, rules/vue.md).
- **The correlation id travels UI → HTTP → Wolverine → gRPC metadata → agent operation** (spec §15).
- **Error detail is role-dependent:** an administrator sees the `nginx -t` output that caused a failure; a customer sees a safe message (spec §15).
- **Proto changes are additive only** (spec §5).

---
## Two decisions this plan makes before any task

**1. PHP gets its own proto service, and `rules/rust.md` is extended in the same task.**

The spec requires the agent to install PHP versions on demand (§11) as a long-running, streaming
operation (§9). `rules/rust.md` currently says "php has no proto service of its own — it is driven
by sites and accounts". Installing a system-wide PHP version is not a site operation and no site
drives it, so putting `InstallPhpVersion` on `SitesService` would model it wrongly to avoid editing
a document. The rules anticipate exactly this: *"If none fit, it is a NEW kind of file: add its
named place to `rules/rust.md` first, in the same PR."* Task 1 does that.

**2. The SPA stays flat; the spec's `frontend/src/modules/sites/` is not followed.**

Spec §17 sketches `frontend/src/modules/sites/`. `rules/vue.md` forbids `src/modules/` and mandates
the flat layout the SPA already uses — `pages/sites/`, `stores/sites.ts`, `types/site.ts`. The rules
are the newer and more specific document, the existing code follows them, and mixing two layouts
would be worse than either. This plan follows the rules and records the divergence here so the next
reader does not think it was missed.

---

## File structure — the agent

Every path below is named in `rules/rust.md`'s canonical layout. Nothing here invents a file name.

### `proto/agent/v1/`

| File | Responsibility |
|---|---|
| `sites.proto` | Exists. Gains nothing; its seven rpcs are implemented as written. |
| `ssl.proto` | Exists. Its three rpcs are implemented as written. |
| `php.proto` | **New.** `PhpService`: `ListPhpVersions` (unary) and `InstallPhpVersion` (server-streaming `Progress`). |

### `agent/crates/distro/` — the only crate that may name a distribution

| File | Responsibility |
|---|---|
| `src/adapter.rs` | `DistroAdapter` grows the web-server and PHP facts: binary paths, package names, service names, config directories, the user nginx runs as, the php-fpm pool directory per version, the PHP package repository. |
| `src/debian/debian_paths.rs`, `debian_packages.rs`, `debian_services.rs` | Debian-family answers (Sury). |
| `src/rhel/rhel_paths.rs`, `rhel_packages.rs`, `rhel_services.rs` | RHEL-family answers (Remi). |

### `agent/crates/templates/`

| File | Responsibility |
|---|---|
| `src/nginx/static_site.rs`, `php_site.rs`, `proxy_site.rs`, `ssl_block.rs`, `suspended_site.rs` | One askama render type per vhost artifact. |
| `src/php_fpm/pool.rs` | The per-account-per-version pool. |
| `src/render_error.rs` | `RenderError`. |
| `templates/nginx/*.conf.j2`, `templates/php-fpm/pool.conf.j2` | Template sources, mirroring the render types. |
| `tests/golden/nginx/*.conf`, `tests/golden/php_fpm/pool.conf` | Byte-exact expected renders. Names derived from the render type. |

### `agent/crates/ops/`

| File | Responsibility |
|---|---|
| `src/safe_write/render_validate_swap.rs`, `rollback_guard.rs`, `safe_write_error.rs` | The one write path. Every config in this plan goes through it. |
| `src/sites/create_site.rs`, `update_site_php_version.rs`, `enable_site.rs`, `disable_site.rs`, `delete_site.rs`, `tail_site_log.rs`, `reload_web_server.rs` | One file per rpc, named as the rpc in snake_case. |
| `src/sites/sites_op_error.rs`, `src/sites/model/*.rs` | The area's error enum and its typed inputs. |
| `src/php/list_php_versions.rs`, `install_php_version.rs`, `write_pool.rs`, `php_op_error.rs` | The PHP area. |
| `src/ssl/install_certificate.rs`, `remove_certificate.rs`, `generate_self_signed.rs`, `ssl_op_error.rs` | The SSL area. |

### `agent/crates/agent-core/`

| File | Responsibility |
|---|---|
| `src/validation/domain.rs`, `domain_error.rs` | `Domain::parse` — a hostname that is safe to write into a config line. Rejects newlines, carriage returns and control characters. |
| `src/validation/port.rs`, `port_error.rs` | The reverse-proxy upstream's port. |
| `src/privs/fork_as_account.rs`, `account_ids.rs`, `priv_error.rs` | **New, and the reason this work needs a second reviewer.** Creating a document root and writing an ACME challenge file happen as the account, never as root. |

### `agent/crates/agent/src/services/`

| File | Responsibility |
|---|---|
| `sites/sites_service.rs`, `sites/site_status.rs` | Proto ↔ ops for sites; the one error-to-gRPC-code mapping. |
| `ssl/ssl_service.rs`, `ssl/ssl_status.rs` | The same for SSL. |
| `php/php_service.rs`, `php/php_status.rs` | The same for PHP. |

### `docker/polygon/`

| File | Responsibility |
|---|---|
| `ubuntu24.Dockerfile`, `alma9.Dockerfile` | **Modified.** They carry `ca-certificates` and nothing else today, so `nginx -t` — the validation the whole write path exists for — cannot run even once. They gain nginx and php-fpm from the family's own repository. |

## File structure — the panel

Two modules, because they own different data and different schedules: a site is a row the customer
edits, a certificate is a row a background job renews. `rules/architecture.md` gives each module one
PostgreSQL schema and forbids one module referencing another, so a shared `Sites` module holding
certificates would be the wrong shape the day renewal needs its own inbox.

Both are scaffolded with `maran module <Name>`, never assembled by hand.

### `backend/src/Maran.Modules/Sites/` — schema `sites`

| File | Responsibility |
|---|---|
| `Domain/Site.cs` | The entity. Every property `private set`; `ChangePhpVersion`, `Enable`, `Disable`, `Rename` are methods on it. Carries `AccountId`. |
| `Domain/Enums/SiteBackendType.cs`, `SiteStatus.cs` | Closed value sets. |
| `Persistence/SitesDbContext.cs` | **Applies the global query filter on `AccountId`.** This is the first tenant-scoped table in the product; the filter is why another tenant's row is never physically returned. |
| `Persistence/Configurations/SiteConfiguration.cs` | Table `Sites`, the unique index on `Domain`. |
| `Commands/CreateSite/…Command.cs`, `…CommandHandler.cs`, `…CommandValidator.cs` | Checks the plan's `MaxSites` **before** calling the agent, then provisions and records. |
| `Commands/ChangeSitePhpVersion/`, `EnableSite/`, `DisableSite/`, `DeleteSite/` | One folder per operation, three files each. |
| `Queries/ListSites/`, `GetSite/`, `ListPhpVersions/` | Reads. |
| `Common/SiteDto.cs`, `SiteDetailDto.cs`, `PhpVersionDto.cs` | Outward shapes. |
| `Resources/ErrorMessages.resx` + `.ru.resx` + `.hy.resx` | `SiteDomainTaken`, `SiteLimitReached`, `PhpVersionNotInstalled`, `SiteNotFound`, `WebServerValidationFailed`. Error codes are defined by these files; there is no hand-written errors class. |
| `Controllers/SitesController.cs` + `Requests/` | `api/v1/sites`, thin: bind, dispatch, translate `Result`. |

### `backend/src/Maran.Modules/Ssl/` — schema `ssl`

| File | Responsibility |
|---|---|
| `Domain/Certificate.cs` | Issuer, subject, `NotAfter`, `AccountId`. |
| `Domain/Enums/CertificateSource.cs` | `LetsEncrypt`, `Custom`, `SelfSigned`. |
| `Persistence/SslDbContext.cs` | Global query filter on `AccountId`. |
| `Commands/IssueCertificate/`, `InstallCustomCertificate/`, `RemoveCertificate/` | ACME ordering lives here, in C#, exactly as the spec requires. |
| `Services/AcmeClient.cs` | HTTP-01 over webroot. The webroot file is written **through the agent**, as the account. |
| `Jobs/CertificateRenewalJob.cs` | Wolverine scheduled job: renew at 30 days before expiry. Uses `IClock`, never the ambient clock, so the window is testable. |
| `Resources/ErrorMessages.resx` ×3 | `CertificateIssuanceFailed`, `CertificateNotFound`, `DomainNotServedHere`. |
| `Controllers/CertificatesController.cs` | `api/v1/certificates`. |

### `backend/src/Maran.Agent.Client/`

| File | Responsibility |
|---|---|
| `Interfaces/IAgentSitesClient.cs`, `IAgentSslClient.cs`, `IAgentPhpClient.cs` | The typed wrappers the modules consume. Nothing outside this project touches generated gRPC code. |
| `Services/SitesService/`, `SslService/`, `PhpService/` | One folder per proto service: client, invoker seam, DTOs. |

### `backend/src/Maran.Host/Resilience/`

| File | Responsibility |
|---|---|
| `ResilientAgentSitesClient.cs`, `ResilientAgentSslClient.cs`, `ResilientAgentPhpClient.cs` | Decorators putting each client through the named operation pipeline, as the accounts client already is. |
| `AcmePipeline.cs` | The outbound ACME calls get their own named pipeline — a certificate authority is not the agent and does not share its timeout. |

## File structure — the application

Flat, as `rules/vue.md` requires.

| File | Responsibility |
|---|---|
| `types/site.ts`, `types/certificate.ts`, `types/phpVersion.ts` | One domain per file. |
| `composables/apis/useSitesApi.ts`, `useCertificatesApi.ts` | Called from stores only. |
| `stores/sites.ts`, `stores/certificates.ts` | The state the pages read. |
| `pages/sites/SitesListPage.vue`, `SiteFormPage.vue`, `SiteDetailPage.vue`, `SiteLogsTab.vue`, `SiteSslTab.vue` | Screens. |
| `components/sites/SiteStatusBadge.vue`, `PhpVersionSelect.vue` | Feature components, built from the UI kit. |
| `components/ui/UiConfirm.vue` | Used for deleting a site and revoking a certificate. |
| `locales/{en,ru,hy}/sites.json`, `certificates.json` | All three from day one; `maran structure` checks parity. |
| `e2e/sites/*.spec.ts` | The golden path the spec names: account → site → SSL. |

---
## Reading the task list

Nineteen tasks in four phases. They are ordered by dependency, not by preference: the agent's write
path has to exist before anything can be written through it, and a certificate has nothing to
install onto until a site exists.

| Phase | Tasks | Deliverable |
|---|---|---|
| A — the agent's foundations | 1–5 | The contract compiles, the platform facts have a home, a config can be rendered and swapped safely. |
| B — the agent's operations | 6–11 | Sites, PHP and certificates work on a real host, proved on both distribution families. |
| C — the panel | 12–15 | The panel decides: limits, tenancy, ACME, renewal. |
| D — the application and the proof | 16–19 | Screens, the five-part Definition of Done, and the golden path in a browser. |

`rules/testing.md` orders the work inside a task: implementation first, then its tests in a dedicated
pass, before the task is done. Steps below follow that order rather than strict test-first, except
where a golden file or a rollback test is the only way to see the behaviour at all.

---

### Task 1: Compile the contract, and give PHP a service

**Files:**
- Create: `proto/agent/v1/php.proto`
- Modify: `agent/crates/agent/build.rs`
- Modify: `rules/rust.md` (the canonical layout's `services/` and `ops/` entries)

**Interfaces:**
- Consumes: nothing.
- Produces: the Rust types `maran.agent.v1.SitesService`, `SslService`, `PhpService` and their
  messages, generated into `agent/crates/agent/src/proto`; every later agent task depends on them.

**Why this is first:** `build.rs` today lists `common.proto`, `system.proto` and `accounts.proto` and
nothing else, so `sites.proto` and `ssl.proto` — complete, documented, reviewed contracts — generate
no Rust at all. Until this task, no agent code can name a single site type.

- [ ] **Step 1: Add the PHP contract**

Create `proto/agent/v1/php.proto`. `ListPhpVersions` is unary; `InstallPhpVersion` is server-streaming
because installing a PHP version pulls packages from Sury or Remi and takes minutes
(`rules/proto.md`: "Long-running rpcs … are server-streaming and emit `Progress`").

```proto
// Installed PHP runtimes, and installing new ones. Multi-PHP is a host-level
// concern: a version is installed once and then bound to any number of sites,
// so it is not driven by a single site's lifecycle.
syntax = "proto3";

package maran.agent.v1;

option csharp_namespace = "Maran.Agent.V1";

import "agent/v1/common.proto";

service PhpService {
  // Lists the PHP versions installed on this host, newest first. Read-only.
  // The panel needs this before it can offer a version for a site: binding a
  // site to a version that is not installed fails VALIDATION_FAILED, so the
  // choice must be made from what exists rather than from a hardcoded list.
  rpc ListPhpVersions(ListPhpVersionsRequest) returns (ListPhpVersionsResponse);

  // Installs a PHP version and its FPM runtime from the family's repository
  // (Sury on the Debian family, Remi on the RHEL family), streaming progress.
  // Idempotent: installing a version that is already present completes
  // immediately with a success terminal message.
  rpc InstallPhpVersion(InstallPhpVersionRequest) returns (stream InstallPhpVersionResponse);
}

message ListPhpVersionsRequest {}

message ListPhpVersionsResponse {
  oneof result {
    ListPhpVersionsOk ok = 1;
    AgentError error = 2;
  }
}

message ListPhpVersionsOk {
  // The installed versions, newest first.
  repeated PhpVersion versions = 1;
}

message PhpVersion {
  // Two-component version as the packages name it, e.g. "8.3".
  string version = 1;
  // Absolute path to the FPM service's unix socket directory for this
  // version, so a site's vhost can be pointed at the right pool.
  string fpm_socket_directory = 2;
  // True when this version is the host's default CLI PHP.
  bool is_default = 3;
}

message InstallPhpVersionRequest {
  // Two-component version to install, e.g. "8.4". Validated against the
  // versions this agent supports (7.4 through 8.4); anything else is
  // INVALID_INPUT rather than a package manager error.
  string version = 1;
}

message InstallPhpVersionResponse {
  oneof result {
    // Emitted repeatedly while the installation runs.
    Progress progress = 1;
    // Terminal success.
    InstallPhpVersionOk ok = 2;
    // Terminal failure.
    AgentError error = 3;
  }
}

message InstallPhpVersionOk {
  // The version now installed, echoed back so a caller that retried a
  // cancelled stream can confirm what it got.
  string version = 1;
}
```

- [ ] **Step 2: Compile the three contracts into the agent**

`agent/crates/agent/build.rs` lists its protos explicitly. Add the three:

```rust
    tonic_build::configure()
        .build_server(true)
        .build_client(false)
        .compile_protos(
            &[
                "agent/v1/common.proto",
                "agent/v1/system.proto",
                "agent/v1/accounts.proto",
                "agent/v1/sites.proto",
                "agent/v1/ssl.proto",
                "agent/v1/php.proto",
            ],
            &[proto_root],
        )?;
```

- [ ] **Step 3: Extend the canonical layout, in this task**

`rules/rust.md` says "php has no proto service of its own — it is driven by sites and accounts". That
was true when nothing installed PHP. Replace that sentence and add the service directory, because the
rules also say: *"If none fit, it is a NEW kind of file: add its named place to `rules/rust.md` first,
in the same PR."*

In the canonical layout's `services/` list add `php/` beside `sites/` and `ssl/`, and replace the
parenthetical with:

```
php/ has both a service and an ops area. Installing a PHP version is a host
operation with no site to drive it — it is done once and then bound by many
sites — so it gets `services/php/php_service.rs`. Everything about a single
site's PHP binding stays in `services/sites/`.
```

- [ ] **Step 4: Verify the contract and the build**

```bash
source scripts/dev
maran proto
cd agent && cargo build -p maran-agent
```

Expected: `PROTO-OK`, and a clean build. The generated types exist but nothing uses them yet, which is
correct at this point.

- [ ] **Step 5: Commit**

```bash
git add proto/agent/v1/php.proto agent/crates/agent/build.rs rules/rust.md
git commit -m "feat(proto): compile the sites and ssl contracts, and add the PHP service

sites.proto and ssl.proto have been complete and documented since Plan 1 and
generated no Rust at all: build.rs lists its protos by hand and named only
common, system and accounts. Until now no agent code could name a site type.

PHP gets a service of its own. rules/rust.md said it had none because nothing
drove it; installing a version is a host operation no single site drives, and
it is long enough to need streaming progress. The layout is extended in this
commit rather than bent around."
```

---

### Task 2: Teach the adapter about nginx and php-fpm

**Files:**
- Modify: `agent/crates/distro/src/adapter.rs`
- Create: `agent/crates/distro/src/debian/debian_paths.rs`, `debian_packages.rs`, `debian_services.rs`
- Create: `agent/crates/distro/src/rhel/rhel_paths.rs`, `rhel_packages.rs`, `rhel_services.rs`
- Modify: `agent/crates/distro/src/debian/debian_adapter.rs`, `rhel/rhel_adapter.rs`, and both `mod.rs`
- Test: `agent/crates/distro/src/tests/adapter_for_tests.rs`

**Interfaces:**
- Consumes: `DistroAdapter` as it is — `family()`, `nologin_shell()`.
- Produces, on the same trait:
  ```rust
  fn nginx_binary(&self) -> &'static str;
  fn nginx_include_directory(&self) -> &'static str;
  fn nginx_service(&self) -> &'static str;
  fn web_server_user(&self) -> &'static str;
  fn php_fpm_pool_directory(&self, version: &str) -> String;
  fn php_fpm_service(&self, version: &str) -> String;
  fn php_fpm_binary(&self, version: &str) -> String;
  fn php_package(&self, version: &str) -> String;
  fn package_manager(&self) -> &'static str;
  fn certificate_directory(&self) -> &'static str;
  ```
  Every site, pool and certificate operation asks these; no other crate may know the answers.

**Why the answers differ, and where they come from:** the spec fixes the two families and the two
package repositories (§4) but names no paths — those are read from each distribution's own
documentation, which `rules/rust.md` requires ("differences are found by reading both distributions'
documentation"). The Debian family installs Sury's `php8.3-fpm` with pools in
`/etc/php/8.3/fpm/pool.d` and nginx running as `www-data`; the RHEL family installs Remi's
`php83-php-fpm` with pools in `/etc/opt/remi/php83/php-fpm.d` and nginx running as `nginx`.

- [ ] **Step 1: Grow the trait**

In `adapter.rs`, add the methods above to `DistroAdapter`, each with a doc comment saying what the
value is for and why it differs. For example:

```rust
    /// The user the web server runs as, which must own nothing and be able to
    /// read a site's document root.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family. The
    /// document root's group is set to this so the server can read files the
    /// account owns without either of them being able to write the other's.
    fn web_server_user(&self) -> &'static str;

    /// Directory the php-fpm pool files for `version` live in.
    ///
    /// The families disagree twice over: the path shape, and how the version
    /// appears in it — `/etc/php/8.3/fpm/pool.d` against
    /// `/etc/opt/remi/php83/php-fpm.d`, where the dot is dropped.
    fn php_fpm_pool_directory(&self, version: &str) -> String;
```

- [ ] **Step 2: Answer them per family, in files named for the concern**

`rules/rust.md` names `debian_paths.rs`, `debian_packages.rs`, `debian_services.rs` — the adapter
delegates rather than growing into one long file. Each holds one public function.

`agent/crates/distro/src/debian/debian_paths.rs`:

```rust
//! Filesystem locations on the Debian family.

/// The directory the agent's own vhost includes are written to.
///
/// The agent writes only here; the distribution's `nginx.conf` includes it.
/// It is never `sites-available`/`sites-enabled`, which belong to the
/// distribution's own packaging.
#[must_use]
pub fn nginx_include_directory() -> &'static str {
    "/etc/maran/nginx/sites"
}

/// Pool directory for a PHP version, e.g. `/etc/php/8.3/fpm/pool.d`.
#[must_use]
pub fn php_fpm_pool_directory(version: &str) -> String {
    format!("/etc/php/{version}/fpm/pool.d")
}

/// Where the agent keeps certificate material, outside every account's home.
#[must_use]
pub fn certificate_directory() -> &'static str {
    "/etc/maran/certificates"
}
```

`agent/crates/distro/src/rhel/rhel_paths.rs` answers the same three questions:

```rust
//! Filesystem locations on the RHEL family.

/// The directory the agent's own vhost includes are written to.
#[must_use]
pub fn nginx_include_directory() -> &'static str {
    "/etc/maran/nginx/sites"
}

/// Pool directory for a PHP version, e.g. `/etc/opt/remi/php83/php-fpm.d`.
///
/// Remi drops the dot from the version and roots its packages under
/// `/etc/opt/remi`, so neither half of the Debian path survives.
#[must_use]
pub fn php_fpm_pool_directory(version: &str) -> String {
    format!("/etc/opt/remi/php{}/php-fpm.d", version.replace('.', ""))
}

/// Where the agent keeps certificate material, outside every account's home.
#[must_use]
pub fn certificate_directory() -> &'static str {
    "/etc/maran/certificates"
}
```

Write `debian_packages.rs` / `rhel_packages.rs` (`php_package("8.3")` → `"php8.3-fpm"` against
`"php83-php-fpm"`; `package_manager()` → `"/usr/bin/apt-get"` against `"/usr/bin/dnf"`) and
`debian_services.rs` / `rhel_services.rs` (`php_fpm_service("8.3")` → `"php8.3-fpm"` against
`"php83-php-fpm"`; `nginx_service()` → `"nginx"` on both, and the doc comment says the agreement is a
coincidence worth stating rather than a shared rule).

- [ ] **Step 3: Delegate from the adapters**

`debian_adapter.rs` gains one line per method:

```rust
    fn php_fpm_pool_directory(&self, version: &str) -> String {
        debian_paths::php_fpm_pool_directory(version)
    }
```

- [ ] **Step 4: Test both families against each other**

Append to `agent/crates/distro/src/tests/adapter_for_tests.rs`:

```rust
#[test]
fn the_families_disagree_about_where_a_php_pool_lives() {
    let debian = adapter_for(DistroFamily::Debian);
    let rhel = adapter_for(DistroFamily::Rhel);

    assert_eq!(debian.php_fpm_pool_directory("8.3"), "/etc/php/8.3/fpm/pool.d");
    assert_eq!(rhel.php_fpm_pool_directory("8.3"), "/etc/opt/remi/php83/php-fpm.d");
}

#[test]
fn the_rhel_family_drops_the_dot_from_a_php_version() {
    // The one difference a reader is most likely to get wrong, because the
    // package name and the path disagree with the version the caller passes.
    let rhel = adapter_for(DistroFamily::Rhel);

    assert_eq!(rhel.php_package("8.4"), "php84-php-fpm");
    assert_eq!(rhel.php_fpm_service("8.4"), "php84-php-fpm");
}

#[test]
fn the_web_server_runs_as_a_different_user_on_each_family() {
    assert_eq!(adapter_for(DistroFamily::Debian).web_server_user(), "www-data");
    assert_eq!(adapter_for(DistroFamily::Rhel).web_server_user(), "nginx");
}
```

- [ ] **Step 5: Verify**

```bash
source scripts/dev
maran agent check
maran structure
```

Expected: clippy clean at `-D warnings`, the three tests pass, `STRUCTURE-OK`.

- [ ] **Step 6: Commit**

```bash
git add agent/crates/distro
git commit -m "feat(distro): teach the adapter about nginx and php-fpm

The trait knew two things: the family, and the nologin shell. Everything a
site or a pool needs is a platform fact, and rules/rust.md allows none of them
outside this crate — not a path, not a package name, not a service name.

The RHEL family drops the dot from a PHP version in both the package name and
the pool path, which is the difference a reader is most likely to get wrong,
so it has a test of its own."
```

---

### Task 3: A domain name that is safe to put in a config file

**Files:**
- Create: `agent/crates/agent-core/src/validation/domain.rs`, `domain_error.rs`
- Create: `agent/crates/agent-core/src/validation/upstream.rs`, `upstream_error.rs`
- Modify: `agent/crates/agent-core/src/validation/mod.rs`
- Test: `agent/crates/agent-core/src/tests/validation/domain_tests.rs`, `upstream_tests.rs`

**Interfaces:**
- Consumes: the existing `AccountName::parse` shape.
- Produces:
  ```rust
  pub struct Domain(String);
  impl Domain { pub fn parse(candidate: &str) -> Result<Self, DomainError>; pub fn as_str(&self) -> &str; }

  pub struct Upstream(String);
  impl Upstream { pub fn parse(candidate: &str) -> Result<Self, UpstreamError>; pub fn as_str(&self) -> &str; }
  ```
  Every sites and ssl operation takes a `Domain`, never a `&str`.

**Why this exists:** `rules/security.md` states the rule this type enforces — *"Any caller-supplied
value written into a line-oriented or structured config file … MUST reject newlines, carriage
returns and control characters before it is written. This is the panel's equivalent of SQL
injection … Rendering through a template does not make it safe: the value is validated, not
escaped."* A `server_name` comes straight from a customer. A domain containing a newline followed by
`}` closes the server block and opens an attacker's own.

- [ ] **Step 1: Write the error, in its own file**

`domain_error.rs`:

```rust
//! Why a candidate domain was refused.

use thiserror::Error;

/// Reasons [`super::Domain::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DomainError {
    /// The candidate was empty.
    #[error("a domain cannot be empty")]
    Empty,

    /// Longer than the 253 characters DNS permits.
    #[error("a domain cannot exceed 253 characters")]
    TooLong,

    /// A label was empty, over 63 characters, or began or ended with a hyphen.
    #[error("`{label}` is not a valid domain label")]
    InvalidLabel {
        /// The offending label.
        label: String,
    },

    /// A character that has no place in a hostname — including, and most
    /// importantly, a newline, a carriage return or any other control
    /// character, which would end the config line this value is written into.
    #[error("a domain cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
```

- [ ] **Step 2: Write the type**

`domain.rs`:

```rust
//! A hostname that is safe to write into a web-server configuration.

use crate::validation::domain_error::DomainError;

/// The longest a domain may be, from DNS.
const MAX_LENGTH: usize = 253;

/// The longest a single label may be, from DNS.
const MAX_LABEL_LENGTH: usize = 63;

/// A syntactically valid hostname, checked once at the boundary and then
/// carried as a type so no later caller has to remember to check it again.
///
/// Construction is the only way to obtain one, so a `Domain` in a signature is
/// a promise that the value has been through [`Domain::parse`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Domain(String);

impl Domain {
    /// Parses `candidate` into a domain.
    ///
    /// Lowercased on the way in, because DNS is case-insensitive while a
    /// config file and a filesystem path are not: two sites differing only in
    /// case must not become two different document roots.
    ///
    /// # Errors
    ///
    /// - [`DomainError::Empty`] when `candidate` is empty.
    /// - [`DomainError::TooLong`] beyond 253 characters.
    /// - [`DomainError::IllegalCharacter`] for anything but ASCII letters,
    ///   digits, `-` and `.` — which is what rejects the newline that would
    ///   otherwise end the `server_name` line and start a directive of the
    ///   caller's choosing.
    /// - [`DomainError::InvalidLabel`] for an empty or over-long label, or one
    ///   starting or ending with a hyphen.
    pub fn parse(candidate: &str) -> Result<Self, DomainError> {
        if candidate.is_empty() {
            return Err(DomainError::Empty);
        }

        if candidate.len() > MAX_LENGTH {
            return Err(DomainError::TooLong);
        }

        if let Some(character) = candidate
            .chars()
            .find(|c| !c.is_ascii_alphanumeric() && *c != '-' && *c != '.')
        {
            return Err(DomainError::IllegalCharacter { character });
        }

        for label in candidate.split('.') {
            if label.is_empty()
                || label.len() > MAX_LABEL_LENGTH
                || label.starts_with('-')
                || label.ends_with('-')
            {
                return Err(DomainError::InvalidLabel { label: label.to_owned() });
            }
        }

        Ok(Self(candidate.to_ascii_lowercase()))
    }

    /// The validated domain.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}
```

- [ ] **Step 3: Write the upstream type**

`sites.proto` says of `proxy_upstream`: *"Host must be loopback or a private address; validated by the
agent, not passed through to the config verbatim."* `upstream.rs` parses `host:port`, requires the
host to be `127.0.0.0/8`, `::1`, `10/8`, `172.16/12` or `192.168/16`, and the port to be 1–65535.
A public address is refused with `UpstreamError::NotPrivate`: a reverse proxy pointing at the
internet turns the panel into an open proxy for whoever asked for the site.

- [ ] **Step 4: Test the attack, not only the happy path**

`agent/crates/agent-core/src/tests/validation/domain_tests.rs`:

```rust
use super::super::domain::Domain;
use super::super::domain_error::DomainError;

#[test]
fn an_ordinary_domain_parses() {
    assert_eq!(Domain::parse("example.com").unwrap().as_str(), "example.com");
}

#[test]
fn a_domain_is_lowercased_so_two_cases_are_not_two_sites() {
    assert_eq!(Domain::parse("Example.COM").unwrap().as_str(), "example.com");
}

#[test]
fn a_domain_containing_a_newline_is_rejected() {
    // The attack this type exists for: written into `server_name example.com;`
    // verbatim, the rest of the value becomes directives of the caller's
    // choosing. rules/security.md calls this the panel's SQL injection.
    let refused = Domain::parse("example.com;\n}\nserver {\n  listen 80");

    assert!(matches!(refused, Err(DomainError::IllegalCharacter { .. })));
}

#[test]
fn a_domain_containing_a_carriage_return_is_rejected() {
    assert!(matches!(
        Domain::parse("example.com\r"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn a_domain_containing_a_null_byte_is_rejected() {
    assert!(matches!(
        Domain::parse("example.com\0"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn a_path_traversal_dressed_as_a_domain_is_rejected() {
    // The document root is built from the domain, so `..` must never reach it.
    assert!(matches!(
        Domain::parse("../../etc/nginx"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn an_empty_label_is_rejected() {
    assert!(matches!(
        Domain::parse("example..com"),
        Err(DomainError::InvalidLabel { .. })
    ));
}

#[test]
fn a_label_starting_with_a_hyphen_is_rejected() {
    assert!(matches!(
        Domain::parse("-example.com"),
        Err(DomainError::InvalidLabel { .. })
    ));
}
```

Write `upstream_tests.rs` in the same shape, with a test named
`an_upstream_pointing_at_a_public_address_is_rejected` that asserts `Upstream::parse("8.8.8.8:80")`
fails, and one asserting `127.0.0.1:3000` and `192.168.1.10:8080` succeed.

- [ ] **Step 5: Declare the modules and the test mirrors**

In `validation/mod.rs` add `pub mod domain; pub mod domain_error; pub mod upstream; pub mod
upstream_error;` and the re-exports. At the end of `domain.rs`:

```rust
#[cfg(test)]
#[path = "../tests/validation/domain_tests.rs"]
mod tests;
```

- [ ] **Step 6: Verify**

```bash
source scripts/dev
maran agent check
maran structure
```

Expected: all tests pass; `STRUCTURE-OK` (the test mirror is where check #9 requires).

- [ ] **Step 7: Commit**

```bash
git add agent/crates/agent-core
git commit -m "feat(agent-core): a domain that is safe to write into a config file

A server_name comes from a customer. Written verbatim, a domain containing a
newline closes the server block and opens one of the caller's choosing —
rules/security.md calls this the panel's equivalent of SQL injection, and says
plainly that rendering through a template does not make it safe: the value is
validated, not escaped.

So the value is a type. A Domain in a signature is a promise that parse ran,
which no later caller can forget. It is lowercased on the way in because DNS
is case-insensitive and a document root is not.

Upstream does the same for a reverse-proxy target, and refuses a public
address: a proxy pointing at the internet is an open proxy."
```

---

### Task 4: The templates crate, and the goldens that guard it

**Files:**
- Modify: `agent/crates/templates/Cargo.toml`, `src/lib.rs`
- Create: `src/nginx/{static_site,php_site,proxy_site,suspended_site,ssl_block}.rs`, `src/php_fpm/pool.rs`, `src/render_error.rs`, and both `mod.rs`
- Create: `templates/nginx/{static_site,php_site,proxy_site,suspended_site}.conf.j2`, `templates/nginx/ssl_block.conf.j2`, `templates/php-fpm/pool.conf.j2`
- Test: `agent/crates/templates/tests/golden_test.rs` and `tests/golden/nginx/*.conf`, `tests/golden/php_fpm/pool.conf`

**Interfaces:**
- Consumes: `Domain` and `Upstream` from Task 3.
- Produces:
  ```rust
  pub struct PhpSite<'a> { pub domain: &'a str, pub aliases: &'a [String], pub document_root: &'a str,
                           pub fpm_socket: &'a str, pub access_log: &'a str, pub error_log: &'a str,
                           pub ssl: Option<SslBlock<'a>> }
  impl PhpSite<'_> { pub fn render(&self) -> Result<String, RenderError>; }
  ```
  and the same shape for `StaticSite`, `ProxySite`, `SuspendedSite`, `Pool`. `ops::sites` renders
  through these and nowhere else.

**Why goldens:** `rules/testing.md` — *"rendered nginx/php-fpm/vsftpd configs are compared
byte-for-byte against `tests/golden/*.conf`. A template change without its golden update fails CI;
the golden diff IS the review artifact."* A vhost is the most security-sensitive text this product
writes, and a diff a human reads is the only review that catches a wrong `fastcgi_pass`.

- [ ] **Step 1: Give the crate its dependency**

`agent/crates/templates/Cargo.toml` has no dependencies at all. Add askama, pinned centrally like
every other dependency in the workspace:

```toml
[dependencies]
askama = { workspace = true }
thiserror = { workspace = true }
```

and add `askama = "0.12"` to the workspace `[workspace.dependencies]` if it is not there.

- [ ] **Step 2: Write the PHP vhost template**

`agent/crates/templates/templates/nginx/php_site.conf.j2`. Every value in it is already validated;
none is escaped, because the rule is that unsafe values never arrive here at all.

```jinja
# Rendered by Maran. Do not edit: this file is replaced whenever the site changes.
server {
    listen 80;
    listen [::]:80;
    server_name {{ domain }}{% for alias in aliases %} {{ alias }}{% endfor %};

    root {{ document_root }};
    index index.php index.html;

    access_log {{ access_log }};
    error_log {{ error_log }};

    # The ACME challenge is served over plain HTTP before a certificate exists,
    # so this location stays reachable even when everything else redirects.
    location ^~ /.well-known/acme-challenge/ {
        root {{ document_root }};
        default_type "text/plain";
    }
{% if ssl.is_some() %}
    location / {
        return 301 https://$host$request_uri;
    }
}
{{ ssl.as_ref().unwrap().render_into_server() }}
{% else %}
    location / {
        try_files $uri $uri/ /index.php?$query_string;
    }

    location ~ \.php$ {
        include fastcgi_params;
        fastcgi_pass unix:{{ fpm_socket }};
        fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
        fastcgi_hide_header X-Powered-By;
    }

    # Everything under a dot is denied, including .env, .git and the challenge
    # directory's neighbours. The ACME location above is matched first by ^~.
    location ~ /\. {
        deny all;
    }
}
{% endif %}
```

- [ ] **Step 3: Write the render type**

`src/nginx/php_site.rs`, one public item, named for the file:

```rust
//! The vhost for a PHP-backed site.

use askama::Template;

use crate::nginx::ssl_block::SslBlock;
use crate::render_error::RenderError;

/// Renders the nginx server block for a site served by php-fpm.
///
/// Every field is a value that has already been validated by the caller —
/// `agent-core`'s `Domain`, `Upstream` and `resolve_in_home` — because a
/// template escapes nothing (rules/security.md).
#[derive(Template)]
#[template(path = "nginx/php_site.conf.j2")]
pub struct PhpSite<'a> {
    /// The primary domain, as `server_name`'s first value.
    pub domain: &'a str,
    /// Additional hostnames served by the same block.
    pub aliases: &'a [String],
    /// Absolute document root under the account's home.
    pub document_root: &'a str,
    /// Absolute path of the php-fpm pool's unix socket.
    pub fpm_socket: &'a str,
    /// Absolute path of the access log.
    pub access_log: &'a str,
    /// Absolute path of the error log.
    pub error_log: &'a str,
    /// The TLS half, when a certificate is installed.
    pub ssl: Option<SslBlock<'a>>,
}

impl PhpSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
```

- [ ] **Step 4: Write the pool template**

`templates/php-fpm/pool.conf.j2`. One pool per account per version, running as the account, with
`pm.max_children` from the plan — this is where the spec's "CPU/process limits via php-fpm pool
sizes" (§8) becomes a real number:

```jinja
; Rendered by Maran. Do not edit: this file is replaced whenever the plan changes.
[{{ pool_name }}]
user = {{ account }}
group = {{ account }}

listen = {{ socket_path }}
listen.owner = {{ web_server_user }}
listen.group = {{ web_server_user }}
listen.mode = 0660

pm = dynamic
pm.max_children = {{ max_children }}
pm.start_servers = {{ start_servers }}
pm.min_spare_servers = {{ min_spare_servers }}
pm.max_spare_servers = {{ max_spare_servers }}

; The account can read its own home and nothing else. open_basedir is not a
; security boundary on its own — the uid is — but it turns a mistake in one
; site into an error instead of a read of a neighbour's file.
php_admin_value[open_basedir] = {{ home_directory }}:/tmp
php_admin_value[disable_functions] = exec,passthru,shell_exec,system,proc_open,popen
php_admin_flag[allow_url_fopen] = off
{% for override in overrides %}
php_value[{{ override.name }}] = {{ override.value }}
{% endfor %}
```

The `overrides` list is the "safe subset" the spec grants customers (§11): the render type accepts
only names from a fixed whitelist, and Task 8 re-validates it in `ops` rather than trusting the panel.

- [ ] **Step 5: Write the goldens and the test that compares them**

`agent/crates/templates/tests/golden_test.rs`:

```rust
//! Byte-for-byte comparison of every rendered artifact against its golden.
//!
//! The golden diff is the review artifact for a template change: a reviewer
//! reads what the web server will actually be told, not a template's
//! intention (rules/testing.md).

use maran_templates::nginx::php_site::PhpSite;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

#[test]
fn a_php_site_renders_its_golden() {
    let aliases = vec!["www.example.com".to_owned()];
    let site = PhpSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: "/home/acme/sites/example.com",
        fpm_socket: "/run/php/maran-acme-8.3.sock",
        access_log: "/home/acme/logs/example.com.access.log",
        error_log: "/home/acme/logs/example.com.error.log",
        ssl: None,
    };

    assert_eq!(site.render_config().unwrap(), golden("nginx/php_site.conf"));
}
```

Generate each golden once by rendering, read it end to end, and commit it only after checking that
the `fastcgi_pass` points at the pool socket and the dotfile deny is present.

- [ ] **Step 6: Verify**

```bash
source scripts/dev
maran agent check
maran structure
```

Expected: goldens match; `STRUCTURE-OK`.

- [ ] **Step 7: Prove the golden can fail**

Change one character in `php_site.conf.j2` — for instance `fastcgi_hide_header` to
`fastcgi_pass_header` — run `cargo test -p maran-templates`, watch the golden test fail and read the
diff. Put the character back. A golden nobody has seen fail is a file, not a test.

- [ ] **Step 8: Commit**

```bash
git add agent/crates/templates
git commit -m "feat(templates): the render types, and the goldens that review them

The crate was a doc comment and four empty directories. It now renders every
config this plan writes: the four vhost shapes, the TLS block, and the
per-account-per-version pool.

Nothing in a template is escaped, and that is deliberate: unsafe values never
reach it. Domain and Upstream refuse a newline at the boundary, so the
template can be read as the file it produces rather than as a puzzle about
quoting.

Each render has a byte-exact golden, because a vhost is the most
security-sensitive text this product writes and the diff is what a human
actually reviews. The goldens were checked by breaking one on purpose first."
```

---

### Task 5: `safe_write` — the one path a config may take

**Files:**
- Create: `agent/crates/ops/src/safe_write/render_validate_swap.rs`, `rollback_guard.rs`, `safe_write_error.rs`, `mod.rs`
- Modify: `agent/crates/ops/src/lib.rs`
- Test: `agent/crates/ops/src/tests/safe_write/render_validate_swap_tests.rs`

**Interfaces:**
- Consumes: `DistroAdapter` (Task 2), the render types (Task 4).
- Produces:
  ```rust
  pub trait ConfigHost: Send + Sync {
      fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError>;
  }

  pub fn write_config(
      host: &dyn ConfigHost,
      target: &Path,
      contents: &str,
      validator: &Validator<'_>,
      reload: &Reload<'_>,
  ) -> Result<(), SafeWriteError>;
  ```
  Every site, pool and certificate write in Tasks 6–9 calls this and never touches `std::fs` itself.

**The protocol, verbatim from `rules/rust.md`:** render → write a temporary file **in the same
directory** as the target → `fsync` the file **and its directory** → validate → atomically `rename`
over the target → reload → and on any failure from validation onwards, restore the previous content
and return a typed error. *"Partial writes are forbidden. An area that needs a variation on this
protocol extends `safe_write` — it does not write its own copy. Two implementations of a
write-and-rollback path is how the first unrecoverable config corruption happens."*

Each step earns its place: the temporary file shares the directory so the rename is atomic on one
filesystem; the directory `fsync` is what stops a crash leaving a rename pointing at unflushed bytes;
validation runs on the file in its final directory because `nginx -t` reads includes by path.

- [ ] **Step 1: Write the error**

`safe_write_error.rs` — variants `Render`, `TemporaryWrite`, `Sync`, `Rename`, `ValidationFailed
{ stderr }`, `ReloadFailed { stderr }`, `RollbackFailed { original_error, rollback_error }`. The last
one matters: a failure to undo is not the same event as a failure to do, and an operator paged at
04:00 needs to know which happened.

- [ ] **Step 2: Write the rollback guard**

`rollback_guard.rs` holds the previous bytes (or the knowledge that the target did not exist) and
restores them on `Drop` unless it has been disarmed:

```rust
//! Restores a configuration file if the operation that replaced it does not finish.

/// Holds what was there before, and puts it back unless [`RollbackGuard::commit`]
/// is called.
///
/// A guard rather than an `if` at each error path: there are five ways out of
/// the write sequence after the rename, and the one that gets forgotten is the
/// one that leaves a server unable to start.
pub struct RollbackGuard {
    target: PathBuf,
    previous: Option<Vec<u8>>,
    armed: bool,
}
```

- [ ] **Step 3: Write the sequence**

`render_validate_swap.rs` performs the seven steps in order, with a comment on each saying what
failure it prevents. Validation and reload run through `ConfigHost::run` — argv arrays against
absolute paths from the adapter, never a shell.

- [ ] **Step 4: Test what the protocol is for**

The happy path is the least interesting test here. `render_validate_swap_tests.rs` uses a fake
`ConfigHost` whose validator can be told to fail:

```rust
#[test]
fn a_config_that_fails_validation_leaves_the_previous_one_in_place() {
    // The whole reason this module exists: nginx must never be left holding a
    // file it cannot parse, because the next reload — by us or by logrotate —
    // takes the site down.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("site.conf");
    std::fs::write(&target, b"server { listen 80; }\n").unwrap();
    let host = FakeConfigHost::failing_validation("nginx: [emerg] unknown directive");

    let refused = write_config(&host, &target, "not a config", &validator(), &reload());

    assert!(matches!(refused, Err(SafeWriteError::ValidationFailed { .. })));
    assert_eq!(std::fs::read(&target).unwrap(), b"server { listen 80; }\n");
}

#[test]
fn a_failed_reload_also_restores_the_previous_config() {
    // Validation passing and the reload failing is the harder case: the file
    // is syntactically fine and still wrong, so the guard must run on a path
    // that looks like success until the last step.
    ...
}

#[test]
fn writing_a_config_where_none_existed_removes_the_file_when_validation_fails() {
    // There is nothing to restore, and leaving a rejected file behind would
    // break the NEXT unrelated reload.
    ...
}

#[test]
fn the_temporary_file_is_created_in_the_targets_own_directory() {
    // A temporary file in /tmp cannot be renamed atomically onto /etc: the
    // rename becomes a copy, and a copy can be read half-written.
    ...
}

#[test]
fn a_config_that_validates_replaces_the_previous_one_and_reloads_once() {
    ...
}
```

- [ ] **Step 5: Verify**

```bash
source scripts/dev
maran agent check
```

Expected: five tests pass, clippy clean.

- [ ] **Step 6: Commit**

```bash
git add agent/crates/ops
git commit -m "feat(ops): the one path a configuration file may take

Render, write a temporary beside the target, fsync the file and its directory,
validate, rename atomically, reload, and put the old bytes back if anything
after the rename fails.

Each step is there for a failure: the temporary shares the directory because a
rename across filesystems is a copy and a copy can be read half-written; the
directory fsync is what stops a crash leaving a rename pointing at bytes that
were never flushed; the rollback is a guard rather than an if, because there
are five ways out of the sequence and the forgotten one leaves a web server
unable to start.

The tests are about those failures, not the happy path."
```

---

### Task 6: Creating, deleting, enabling and disabling a site

**Files:**
- Create: `agent/crates/ops/src/sites/{mod,sites_op_error,site_host,process_site_host,create_site,delete_site,enable_site,disable_site}.rs`
- Create: `agent/crates/ops/src/sites/model/{create_site_input,site_kind,mod}.rs`
- Test: `agent/crates/ops/src/tests/sites/{create_site_tests,disable_site_tests}.rs`

**Interfaces:**
- Consumes: `Domain` (Task 3), the render types (Task 4), `write_config` (Task 5), `DistroAdapter`
  (Task 2), and `AccountName`/`resolve_in_home` as they already exist.
- Produces:
  ```rust
  pub fn create_site(host: &dyn SiteHost, distro: &dyn DistroAdapter, input: &CreateSiteInput)
      -> Result<CreatedSite, SitesOpError>;
  pub fn delete_site(...) -> Result<(), SitesOpError>;
  pub fn enable_site(...) -> Result<(), SitesOpError>;
  pub fn disable_site(...) -> Result<(), SitesOpError>;
  ```
  Task 10's service layer calls exactly these.

**One file per rpc, named as the rpc in snake_case** — `rules/rust.md` makes the mapping mechanical so
the code for an rpc is found without searching.

- [ ] **Step 1: The typed input**

`model/create_site_input.rs` carries validated values only, so no caller can pass a domain and an
account in the wrong order:

```rust
/// Everything `create_site` needs, already validated.
pub struct CreateSiteInput {
    /// The owning account.
    pub account: AccountName,
    /// The primary domain.
    pub domain: Domain,
    /// Additional hostnames served by the same site.
    pub aliases: Vec<Domain>,
    /// What serves the content.
    pub kind: SiteKind,
}

/// What serves a site's content, with the data each shape needs.
pub enum SiteKind {
    /// Files only.
    Static,
    /// php-fpm, bound to an installed version.
    Php {
        /// Two-component version, e.g. "8.3".
        version: String,
    },
    /// Forwarded to a private upstream.
    ReverseProxy {
        /// The validated `host:port`.
        upstream: Upstream,
    },
}
```

- [ ] **Step 2: Create the document root as the account, not as root**

The document root lives at `/home/<account>/sites/<domain>/`, inside a customer's home.
`rules/security.md`: *"Direct `std::fs` on customer paths as root is forbidden"*, and
*"`fork_as_account` is the only entry point for doing work as a customer"*. So `create_site` resolves
the path with `resolve_in_home` and creates it through `fork_as_account`, which Task 7 writes. Until
then this step is blocked — which is why Task 7 comes before the service layer.

- [ ] **Step 3: Render and write the vhost**

Choose the render type from `SiteKind`, render, and hand it to `write_config` with the adapter's
`nginx_binary()` plus `["-t"]` as the validator and its `nginx_service()` reload as the reload. The
operation touches no filesystem itself.

- [ ] **Step 4: Make each operation idempotent**

- `create_site` on an existing site returns `SitesOpError::AlreadyExists`.
- `delete_site` on a missing one returns `SitesOpError::NotFound`.
- `enable_site` on an enabled site is a no-op success, and the same for `disable_site` — the spec
  requires both (§9), and the panel retries after a timeout.

`disable_site` re-renders the vhost from `SuspendedSite` rather than deleting it: `sites.proto` says
the vhost is kept *"so SSL renewal and SEO are not disrupted"*. A suspended site that stops answering
`/.well-known/acme-challenge/` cannot renew, and comes back with an expired certificate.

- [ ] **Step 5: Tests**

`create_site_tests.rs` covers: an ordinary PHP site writes a vhost containing its `fastcgi_pass`; a
second create for the same domain returns `AlreadyExists` and does not rewrite the file; a failing
`nginx -t` leaves no vhost behind and returns `NginxValidation` carrying the tool output.
`disable_site_tests.rs` covers: disabling replaces the vhost with the suspended one and **keeps the
ACME location**; disabling twice succeeds.

- [ ] **Step 6: Verify and commit**

```bash
source scripts/dev && maran agent check && maran structure
git add agent/crates/ops
git commit -m "feat(ops): create, delete, enable and disable a site"
```

---

### Task 7: `fork_as_account` — doing work as the customer

**Files:**
- Create: `agent/crates/agent-core/src/privs/{mod,fork_as_account,account_ids,priv_error}.rs`
- Test: `agent/crates/agent-core/src/tests/privs/account_ids_tests.rs`
- Create: `docs/superpowers/notes/2026-XX-XX-privs-threat-note.md`

**Interfaces:**
- Produces:
  ```rust
  pub fn fork_as_account<F>(ids: &AccountIds, work: F) -> Result<(), PrivError>
      where F: FnOnce() -> Result<(), PrivError>;
  pub fn account_ids(username: &AccountName) -> Result<AccountIds, PrivError>;
  ```

**This task needs a second reviewer and a threat note.** `rules/security.md` names the agent's `privs`
module as one of four areas that do, and this is the module. It is also the only place `unsafe` is
allowed.

**The three rules that are easy to get wrong, and fatal when they are:**

1. **Fork, then drop.** `setuid` is process-wide, not thread-scoped, so it must never be called inside
   the tokio runtime — a dropped privilege in one thread is a dropped privilege for the daemon.
2. **Order is `setgroups` → `setgid` → `setuid`, and never any other.** Dropping the uid first removes
   the privilege needed to drop the groups, leaving the child in the account's uid *and* root's
   supplementary groups.
3. **The child verifies before it acts.** It re-reads its own uid, gid and group list and aborts if
   any of them is not what it asked for, because a partially applied drop looks exactly like a
   successful one from the parent.

- [ ] **Step 1: Resolve the ids without a shell**

`account_ids.rs` reads uid, gid and the primary group by calling `getpwnam_r`, not by parsing
`/etc/passwd` and not by running `id`.

- [ ] **Step 2: Write the fork**

`fork_as_account.rs` forks, and in the child: `setgroups(&[gid])`, `setgid(gid)`, `setuid(uid)`, then
re-reads all three and `_exit(EX_NOPERM)` on any mismatch; runs `work`; `_exit`s with its outcome. The
parent waits and maps the exit status to `Ok(())` or a typed `PrivError`. The child does the narrowest
unit of work possible — create one directory, write one file — and exits.

- [ ] **Step 3: Test what can be tested without root**

`account_ids_tests.rs` asserts that resolving an unknown user returns `PrivError::NoSuchAccount`
rather than a panic, and that resolving the current user returns this process's own ids. The fork
itself is exercised in Task 11's container test, where the agent runs as root against a real account.

- [ ] **Step 4: Write the threat note**

`rules/security.md` requires it in the pull request description: what an attacker could do with this
surface and why it is safe now. Cover at least: a symlink in the account's home pointing at
`/etc/shadow` (defeated by dropping to the account's uid before touching anything, and by
`resolve_in_home` refusing a path that escapes); a race between resolving the path and using it; a
`setuid` that partially applies; and what happens if the child is killed mid-write.

- [ ] **Step 5: Verify and commit**

```bash
source scripts/dev && maran agent check && maran structure
git add agent/crates/agent-core docs/superpowers/notes
git commit -m "feat(agent-core): fork_as_account, the only way to act as a customer

The document root, the ACME challenge file and every other write inside a
customer's home happen as that customer, never as root, so a symlink pointing
at /etc/shadow reaches a process that cannot read it.

Fork first: setuid is process-wide, so dropping inside the tokio runtime drops
it for the daemon. The order is setgroups, setgid, setuid and never any other,
because dropping the uid first removes the privilege needed to drop the
groups. The child re-reads all three and aborts on a mismatch, because a
partial drop looks like a successful one from the parent.

Threat note in the pull request; this needs a second reviewer."
```

---

### Task 8: Multi-PHP — listing, installing, and one pool per account per version

**Files:**
- Create: `agent/crates/ops/src/php/{mod,php_op_error,php_host,process_php_host,list_php_versions,install_php_version,write_pool}.rs`
- Create: `agent/crates/ops/src/php/model/{pool_input,php_override,mod}.rs`
- Create: `agent/crates/ops/src/sites/update_site_php_version.rs`
- Test: `agent/crates/ops/src/tests/php/{write_pool_tests,php_override_tests}.rs`

**Interfaces:**
- Produces:
  ```rust
  pub fn list_php_versions(host: &dyn PhpHost, distro: &dyn DistroAdapter)
      -> Result<Vec<InstalledPhpVersion>, PhpOpError>;
  pub fn install_php_version<P>(host: &dyn PhpHost, distro: &dyn DistroAdapter, version: &str, progress: P)
      -> Result<(), PhpOpError> where P: FnMut(u32, &str);
  pub fn write_pool(host: &dyn ConfigHost, distro: &dyn DistroAdapter, input: &PoolInput)
      -> Result<(), PhpOpError>;
  ```

**The supported versions are a closed set.** The spec fixes 7.4 through 8.4 (§11). A version outside it
is `INVALID_INPUT` from the agent, not a package-manager error: `php9.9-fpm` reaching `apt-get` would
be a caller choosing what the agent installs, and the agent distrusts the caller (§9).

- [ ] **Step 1: List what is installed**

`list_php_versions` asks the adapter for each supported version's pool directory and reports the ones
that exist, newest first, with the socket directory a vhost will point at. It runs no package manager:
listing must be cheap enough for the panel to call on every page load.

- [ ] **Step 2: Install, with progress**

`install_php_version` runs the family's package manager through argv arrays — `["/usr/bin/apt-get",
"install", "-y", "php8.3-fpm"]` — with the package name from the adapter and never a shell. It reports
progress at named stages (`"repository"`, `"download"`, `"install"`, `"enable"`) through the callback,
which Task 10's service turns into `Progress` messages. Idempotent: an already-installed version
completes immediately at 100%.

- [ ] **Step 3: The customer's safe subset, re-validated here**

`model/php_override.rs` holds the whitelist. The spec grants customers *"a safe subset of settings via
pool-overrides"* (§11), and `rules/security.md` requires the agent to re-validate rather than trust the
panel:

```rust
/// The PHP settings a customer may change, and the bounds they may change them within.
///
/// A whitelist, not a filter: a name that is not here is refused rather than
/// sanitised. `disable_functions` and `open_basedir` are deliberately absent —
/// they are the pool's own protection and are set with `php_admin_value`,
/// which a `php_value` override cannot reach.
const ALLOWED: &[(&str, OverrideKind)] = &[
    ("memory_limit", OverrideKind::Bytes { max: 512 * 1024 * 1024 }),
    ("upload_max_filesize", OverrideKind::Bytes { max: 512 * 1024 * 1024 }),
    ("post_max_size", OverrideKind::Bytes { max: 512 * 1024 * 1024 }),
    ("max_execution_time", OverrideKind::Seconds { max: 300 }),
    ("max_input_vars", OverrideKind::Count { max: 10_000 }),
    ("date.timezone", OverrideKind::Timezone),
];
```

- [ ] **Step 4: Write the pool through `safe_write`**

`write_pool` renders `Pool` and writes it with `php-fpm -t` as the validator and the version's fpm
service as the reload — the same protocol as a vhost, because it is the same class of file.
`pm.max_children` comes from the plan's worker limit, which the panel passes in.

- [ ] **Step 5: Switch a site's version**

`sites/update_site_php_version.rs`: refuse with `PhpVersionNotInstalled` if the version is absent
(the contract says `VALIDATION_FAILED`), ensure the pool for that account and version exists, re-render
the vhost with the new socket, and reload. Setting the same version twice is a no-op success.

- [ ] **Step 6: Tests**

`php_override_tests.rs` is the important one: a name outside the whitelist is refused; `memory_limit`
above the maximum is refused; a value containing a newline is refused (the config-injection rule again,
in a second file format); `date.timezone` accepts `Europe/Yerevan` and refuses `../../etc/passwd`.
`write_pool_tests.rs` asserts the rendered pool runs as the account and that `disable_functions`
survives an override attempting to unset it.

- [ ] **Step 7: Verify and commit**

```bash
source scripts/dev && maran agent check && maran structure
git add agent/crates/ops
git commit -m "feat(ops): multi-PHP — list, install, and one pool per account per version

The supported versions are a closed set, so a version outside 7.4 to 8.4 is
refused by the agent rather than handed to a package manager: what the agent
installs is not the caller's choice.

The customer's settings are a whitelist with bounds, re-validated here rather
than trusted from the panel, and php_admin_value keeps disable_functions and
open_basedir out of reach of a php_value override."
```

---

### Task 9: Certificates — install, remove, self-signed

**Files:**
- Create: `agent/crates/ops/src/ssl/{mod,ssl_op_error,install_certificate,remove_certificate,generate_self_signed,certificate_expiry}.rs`
- Test: `agent/crates/ops/src/tests/ssl/{install_certificate_tests,certificate_expiry_tests}.rs`

**Interfaces:**
- Produces:
  ```rust
  pub fn install_certificate(host: &dyn ConfigHost, distro: &dyn DistroAdapter,
      account: &AccountName, domain: &Domain, certificate_pem: &str, private_key_pem: &str)
      -> Result<i64, SslOpError>;
  pub fn remove_certificate(...) -> Result<(), SslOpError>;
  pub fn generate_self_signed(...) -> Result<i64, SslOpError>;
  ```
  Each returns the expiry as Unix seconds, which is what `InstallCertificateOk.expires_at_unix` carries
  and what the panel schedules renewal from.

**The agent does not know what ACME is.** The spec is explicit (§9): ACME logic is in C#, and the agent
*"only places certificate files and does a reload"*. There is no HTTP client here, no account key, no
order. A reviewer seeing one in this crate should reject the change.

- [ ] **Step 1: Refuse a key that does not match its certificate**

Before anything is written, check that the private key belongs to the certificate. A mismatched pair
passes `nginx -t` and fails at the first TLS handshake — the site goes down at the moment it was
supposed to become secure, and the rollback has already been disarmed.

- [ ] **Step 2: Write both files into the agent's own store**

`ssl.proto` requires the material to live *"under the agent's own cert store (never inside the
account's home)"*. The key is written mode `0600`, owned by root, before the certificate — so a
readable key never exists even briefly. Neither file goes near `fork_as_account`: they are the agent's,
not the customer's.

- [ ] **Step 3: Rewire the vhost and reload**

Re-render the site with `SslBlock`, and hand it to `write_config`. The certificate write and the vhost
write are two `safe_write` calls, and the second one's rollback restores the plain-HTTP vhost, so a
certificate that nginx rejects leaves a working site rather than a broken one.

- [ ] **Step 4: Never log the key**

`rules/security.md` names private keys as secrets. `SslOpError` has no variant carrying key material,
and the `tool_output` from a failed validation is checked for the key's first line before it is
attached — `openssl` has been known to echo input.

- [ ] **Step 5: Tests**

`install_certificate_tests.rs`: a matched pair installs and returns the certificate's real expiry; a
mismatched pair is refused before any file is written; installing byte-identical material twice is a
no-op success (the contract says so); a failed reload leaves the previous vhost. Fixtures are
self-signed certificates generated once and committed under `tests/fixtures/`, with a far-future
expiry so the suite does not start failing on a date.

- [ ] **Step 6: Verify and commit**

```bash
source scripts/dev && maran agent check
git add agent/crates/ops
git commit -m "feat(ops): install, remove and self-sign certificates

The agent places material and reloads; it does not know what ACME is, and the
absence of an HTTP client in this crate is the enforcement.

The key is checked against the certificate before anything is written, because
a mismatched pair passes nginx -t and fails at the first handshake — the site
goes down exactly when it was meant to become secure."
```

---

### Task 10: The service layer, and registering it

**Files:**
- Create: `agent/crates/agent/src/services/sites/{mod,sites_service,site_status}.rs`
- Create: `agent/crates/agent/src/services/ssl/{mod,ssl_service,ssl_status}.rs`
- Create: `agent/crates/agent/src/services/php/{mod,php_service,php_status}.rs`
- Modify: `agent/crates/agent/src/services/mod.rs`, `agent/crates/agent/src/server.rs`

**Interfaces:**
- Consumes: every `ops` function from Tasks 6, 8 and 9.
- Produces: three tonic services registered on the socket.

**A service method does exactly three things** — proto to validated input, one `ops` call, result to
response — and `rules/rust.md` adds: *"No branching on business conditions, no filesystem access, no
process spawning."* Copy the shape of `accounts_service.rs` exactly; a reviewer should be able to read
the two side by side and see no new ideas.

- [ ] **Step 1: The unary services**

`sites_service.rs` and `ssl_service.rs` follow `accounts_service.rs` line for line: `validated()`
turns each string field into its type, one `ops` call, and a `oneof` result. A domain outcome is never
`Err(Status)`.

- [ ] **Step 2: The error mappings, one file each**

`site_status.rs`, `ssl_status.rs`, `php_status.rs` each hold the single
`to_agent_error(&XOpError) -> AgentError`. `SitesOpError::NginxValidation { stderr }` maps to
`ERROR_CODE_VALIDATION_FAILED` with the stderr in `tool_output` — which `rules/proto.md` defines as
exactly this case: *"rendered config failed its validator; state rolled back"*. Every enum ends with a
`_ =>` arm because they are all `#[non_exhaustive]`.

- [ ] **Step 3: The streaming services**

`TailSiteLog` and `InstallPhpVersion` return streams. The stream is produced by `ops` and the service
only wraps it (`rules/rust.md`). Both are bounded: the log tail caps history at 1000 lines as the
contract states, and both stop when the client drops the stream rather than reading a growing file
into a channel nobody is draining.

- [ ] **Step 4: Register, each with its own guard**

In `server.rs`, add three `add_service` calls. Every one carries its own `PeerGuard::new(policy)` —
the interceptor is per-service, and a service registered without one is reachable by any local process:

```rust
        .add_service(SitesServiceServer::with_interceptor(
            SitesServiceImpl::new(ProcessSiteHost::new(), adapter),
            PeerGuard::new(policy)))
        .add_service(SslServiceServer::with_interceptor(
            SslServiceImpl::new(ProcessConfigHost::new(), adapter),
            PeerGuard::new(policy)))
        .add_service(PhpServiceServer::with_interceptor(
            PhpServiceImpl::new(ProcessPhpHost::new(), adapter),
            PeerGuard::new(policy)))
```

- [ ] **Step 5: Test that the guard is on all of them**

Add to `agent/crates/agent/tests/handshake.rs` a test connecting from a uid the policy does not allow
and asserting each of the three new services refuses it. A guard omitted from one service is invisible
until someone finds it.

- [ ] **Step 6: Verify and commit**

```bash
source scripts/dev && maran agent check && maran structure && maran handshake
git add agent/crates/agent
git commit -m "feat(agent): serve the sites, ssl and php services

Three services, each a translation layer and nothing else, and each with its
own PeerGuard — the interceptor is per-service, so one registered without it
would be reachable by any local process. A test connects as a disallowed uid
and asserts all three refuse."
```

---

### Task 11: A polygon that can actually run nginx

**Files:**
- Modify: `docker/polygon/ubuntu24.Dockerfile`, `docker/polygon/alma9.Dockerfile`
- Create: `agent/crates/agent/tests/sites_on_a_real_host.rs`
- Modify: `.github/workflows/agent.yml`
- Modify: `docker/README.md`

**Why this task exists:** the whole point of `safe_write` is that validation is real — `nginx -t`
actually parses the file the agent wrote. The polygon images carry `ca-certificates` and nothing else,
so every test so far has used a fake `ConfigHost`. Without this task the plan ships a validator that
has never validated anything.

- [ ] **Step 1: Give each image its web server and PHP**

Ubuntu 24.04 with Sury; AlmaLinux 9 with Remi. Both keep the comment saying production never uses
Docker, and both gain a line explaining that these packages exist so the agent's own validation can be
exercised — not to make the container a server.

- [ ] **Step 2: An integration test that writes a real vhost**

`tests/sites_on_a_real_host.rs`, ignored by default and run in the container: create an account,
create a PHP site, assert the vhost exists and `nginx -t` passes; then write a deliberately broken
template and assert the previous config survives. That second assertion is the one that has never been
tested for real.

- [ ] **Step 3: Run it on both families in CI**

`agent.yml` gains a job matrix over the two images, as the spec requires for a pull request
(§16: Ubuntu 24 and Alma 9 on PR, the full six-OS matrix nightly).

- [ ] **Step 4: Verify and commit**

```bash
docker build -f docker/polygon/ubuntu24.Dockerfile -t maran-polygon-ubuntu24 docker/polygon
docker build -f docker/polygon/alma9.Dockerfile   -t maran-polygon-alma9    docker/polygon
git add docker .github/workflows/agent.yml agent/crates/agent/tests
git commit -m "test(agent): a polygon that can run nginx, and a test that proves rollback

safe_write exists so that nginx never holds a file it cannot parse, and until
now nothing had ever run nginx -t: the polygon images carried ca-certificates
and every test used a fake host.

Both images now carry nginx and php-fpm from their own family's repository —
Sury on Ubuntu, Remi on Alma — and an integration test writes a real vhost,
breaks it on purpose, and asserts the previous config survived."
```

---

### Task 12: The typed agent clients, and their pipelines

**Files:**
- Create: `backend/src/Maran.Agent.Client/Interfaces/{IAgentSitesClient,ISitesServiceInvoker,IAgentSslClient,ISslServiceInvoker,IAgentPhpClient,IPhpServiceInvoker}.cs`
- Create: `backend/src/Maran.Agent.Client/Services/SitesService/{AgentSitesClient,GrpcSitesServiceInvoker,CreatedSiteDto}.cs`
- Create: `backend/src/Maran.Agent.Client/Services/SslService/{AgentSslClient,GrpcSslServiceInvoker,InstalledCertificateDto}.cs`
- Create: `backend/src/Maran.Agent.Client/Services/PhpService/{AgentPhpClient,GrpcPhpServiceInvoker,PhpVersionDto}.cs`
- Modify: `backend/src/Maran.Agent.Client/DependencyInjection.cs`
- Create: `backend/src/Maran.Host/Resilience/{ResilientAgentSitesClient,ResilientAgentSslClient,ResilientAgentPhpClient}.cs`
- Modify: `backend/src/Maran.Host/Extensions/ResilienceExtensions.cs`
- Test: `backend/tests/Maran.Agent.Client.Tests/SitesService/AgentSitesClientTests.cs`

**Interfaces:**
- Consumes: the generated C# clients, which already exist — `Maran.Agent.Client.csproj` globs every
  proto, so only the wrappers are missing.
- Produces:
  ```csharp
  Task<Result<CreatedSiteDto>> CreateAsync(string accountUsername, string domain,
      IReadOnlyList<string> aliases, SiteBackendKind kind, string phpVersion,
      string proxyUpstream, CancellationToken cancellationToken);
  Task<Result<bool>> ChangePhpVersionAsync(string accountUsername, string domain, string phpVersion, CancellationToken ct);
  Task<Result<bool>> EnableAsync(...); Task<Result<bool>> DisableAsync(...); Task<Result<bool>> DeleteAsync(...);
  Task<Result<InstalledCertificateDto>> InstallCertificateAsync(string accountUsername, string domain,
      string certificatePem, string privateKeyPem, CancellationToken ct);
  Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken ct);
  ```

Copy `AgentAccountsClient` exactly: the two-constructor shape (an internal one taking the invoker seam
for tests, a public one taking the channel), and the `ResultCase` switch mapping `Ok`, `Error` and a
missing `oneof` to `AgentInvalidResponse`. The seven shared `Agent*` resx keys already exist and cover
these clients too; **no new keys are needed here**.

- [ ] **Step 1: Write the three clients and their invoker seams**
- [ ] **Step 2: Register them in `AddAgentClient`**
- [ ] **Step 3: Decorate all three in `AddPanelResilience`**

`ResilienceExtensions` hand-decorates `IAgentAccountsClient` today. A client registered without its
decorator has no timeout at all — the defect this repository already found once, when the pipeline was
registered and resolved by nobody. Extend `DecorateAgentAccountsClient` into a small generic helper and
call it for all four clients, so the next one cannot be forgotten quietly.

- [ ] **Step 4: Test the mapping, and that the decoration happened**

`AgentSitesClientTests` asserts each `ResultCase` maps as expected and that a validation failure's
`tool_output` is **logged and not returned** — a customer must not see `nginx: [emerg]`. Add to
`ContainerResolutionTests` the assertion that all four agent clients resolve as their `Resilient…`
wrapper.

- [ ] **Step 5: Verify and commit**

```bash
cd backend && dotnet test tests/Maran.Agent.Client.Tests tests/Maran.Host.Tests
git commit -m "feat(agent-client): typed clients for sites, ssl and php, each behind its pipeline"
```

---

### Task 13: The Sites module

**Files:**
- Create the module with `maran module Sites`, then fill: `Domain/Site.cs`, `Domain/Enums/{SiteBackendType,SiteStatus}.cs`, `Persistence/{SitesDbContext,Configurations/SiteConfiguration}.cs`, `Commands/{CreateSite,ChangeSitePhpVersion,EnableSite,DisableSite,DeleteSite}/`, `Queries/{ListSites,GetSite,ListPhpVersions}/`, `Common/{SiteDto,SiteDetailDto,PhpVersionDto}.cs`, `Controllers/SitesController.cs`, `Resources/ErrorMessages{,.ru,.hy}.resx`
- Modify: `backend/src/Maran.Host/Modules/ModuleRegistry.cs`, `backend/Maran.sln`
- Test: `backend/tests/Maran.Modules.Sites.Tests/`

**Interfaces:**
- Consumes: `IAgentSitesClient`, `IAgentPhpClient` (Task 12), `IClock`, `IAuditWriter`.
- Produces: `api/v1/sites` and the `sites` schema.

- [ ] **Step 1: Scaffold, never assemble by hand**

```bash
source scripts/dev
maran module Sites
```

Then add the project to `Maran.sln` and `ModuleRegistry.All`. A module absent from the registry is a
module the host never loads — and its controllers still compile, which is what makes the omission
quiet.

- [ ] **Step 2: The entity, with no public setters**

`Site` carries `AccountId`, `Domain`, `Aliases`, `BackendType`, `PhpVersion`, `Status`, `CreatedAt`,
every one `private set`, and changes only through `ChangePhpVersion(string version)`, `Enable()`,
`Disable()`. `maran structure` check #14 fails the build on a public setter.

- [ ] **Step 3: The global query filter — the first one in the product**

```csharp
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        // Spec §8: a tenant row is not returned to another tenant PHYSICALLY, not
        // by a handler remembering to filter. An administrator sees everything;
        // a customer's context carries their account id and the filter closes
        // over it, so a query that forgets a Where clause still cannot leak.
        modelBuilder.Entity<Site>().HasQueryFilter(site =>
            _currentUser.IsAdmin || site.AccountId == _currentUser.AccountId);
    }
```

The context takes `ICurrentUser` in its constructor. Every later tenant-scoped module copies this.

- [ ] **Step 4: `MaxSites`, checked in the application layer**

Spec §8 puts countable limits in the application at creation time. `CreateSiteCommandHandler` counts
the account's sites, compares against `Plan.MaxSites`, and returns `Error.Of(nameof(ErrorMessages
.SiteLimitReached))` before it calls the agent — a site refused by the plan must never reach the host.

- [ ] **Step 5: Agent first, row second**

The same ordering as accounts, for the same reason: a vhost with no row is invisible and harmless, and
the operation converges on retry; a row with no vhost is a site the customer was told they have.

- [ ] **Step 6: The error codes are the resx keys**

`SiteDomainTaken`, `SiteLimitReached`, `SiteNotFound`, `PhpVersionNotInstalled`,
`WebServerValidationFailed` — in all three locales, with the parity test covering them. There is no
hand-written errors class; `Error.Of(nameof(ErrorMessages.SiteNotFound))` is the whole mechanism.

- [ ] **Step 7: Audit every mutation**

Create, change-version, enable, disable and delete each write an `AuditEvent`. Definition of Done
item 4, and the subject carries the domain — never the tool output.

- [ ] **Step 8: Migration**

```bash
maran migrate add Sites InitialSitesSchema
maran migrate check
```

- [ ] **Step 9: Verify and commit**

```bash
cd backend && dotnet test
source ../scripts/dev && maran structure && maran format --check
git commit -m "feat(sites): the Sites module, and the product's first tenant query filter"
```

---

### Task 14: The Ssl module, ACME, and renewal

**Files:**
- Create with `maran module Ssl`, then fill: `Domain/Certificate.cs`, `Domain/Enums/CertificateSource.cs`, `Persistence/SslDbContext.cs`, `Commands/{IssueCertificate,InstallCustomCertificate,RemoveCertificate}/`, `Queries/ListCertificates/`, `Services/{AcmeClient,AcmeChallengeWriter}.cs`, `Jobs/CertificateRenewalJob.cs`, `Common/Options/AcmeOptions.cs`, `Controllers/CertificatesController.cs`, `Resources/ErrorMessages{,.ru,.hy}.resx`
- Modify: `backend/src/Maran.Host/Resilience/AcmePipeline.cs`, `.env.example`, `docker/.env.example`, `installer/panel.env.example`
- Test: `backend/tests/Maran.Modules.Ssl.Tests/`

**Interfaces:**
- Consumes: `IAgentSitesClient` (to write the challenge file), `IAgentSslClient` (to install the
  result), `IClock`.
- Produces: `api/v1/certificates`, the `ssl` schema, and a scheduled job.

- [ ] **Step 1: HTTP-01 over webroot, and nothing else**

Spec §11 allows exactly this: wildcard and DNS-01 wait for the DNS module, which is not in v1 at all.
`AcmeClient` orders, writes the challenge token to `/.well-known/acme-challenge/` **through the agent,
as the account**, waits for validation, and hands the issued material to `IAgentSslClient
.InstallCertificateAsync`.

- [ ] **Step 2: The challenge file is a customer file**

It lives inside the document root, so it is written by the agent under the account's uid — not by the
API, which `rules/security.md` says "spawns nothing at all" and never touches a customer's disk.

- [ ] **Step 3: ACME is an outbound call, so it gets its own pipeline**

`AcmePipeline.cs`, separate from the agent's: a certificate authority is not a unix socket and does not
share its timeout. Rate limits matter too — Let's Encrypt counts failures, so the retry is bounded and
does not retry a rejected order.

- [ ] **Step 4: Configuration, documented in all three env files**

`AcmeOptions`: directory URL (staging by default in development), contact email, and the certificate
store path. `rules/security.md` item 7 requires every new variable in `.env.example`,
`docker/.env.example` and `installer/panel.env.example`. Defaulting to Let's Encrypt **staging** in
development is deliberate: a developer looping on a bug must not burn the production rate limit.

- [ ] **Step 5: Renewal, at thirty days, on an injected clock**

`CertificateRenewalJob` is a Wolverine scheduled job — not a daemon, because `rules/security.md` item
10 forbids a new process without a spec change. It selects certificates whose `NotAfter` is within
thirty days of `IClock.UtcNow` and re-issues them. The clock is injected so the window is testable
without waiting sixty days.

- [ ] **Step 6: Tests**

Handler tests with a fake ACME client: issuance stores the certificate and its expiry; a failed order
leaves no row; renewal selects a certificate at 29 days and ignores one at 31. Integration tests for
the endpoints, including the IDOR test — customer A asking for customer B's certificate gets **404**.

- [ ] **Step 7: Verify and commit**

```bash
maran migrate add Ssl InitialSslSchema && maran migrate check
cd backend && dotnet test
git commit -m "feat(ssl): ACME over HTTP-01, custom certificates, and renewal at thirty days"
```

---

### Task 15: Wiring the modules into the host

**Files:**
- Modify: `backend/src/Maran.Host/Modules/ModuleRegistry.cs`, `backend/Maran.sln`
- Modify: `backend/tests/Maran.ArchitectureTests/`
- Test: `backend/tests/Maran.Host.IntegrationTests/{SitesEndpointTests,CertificatesEndpointTests,SitesAuthorizationTests}.cs`

- [ ] **Step 1: Register both modules and both test projects**
- [ ] **Step 2: The IDOR fixture, enumerated**

`SitesAuthorizationTests` copies `AccountsAuthorizationTests`: every site-scoped route named once in a
`TheoryData`, then asked three questions — anonymous is refused, a customer reaching another
customer's site gets **404 and not 403**, and an unknown id answers 404 rather than failing. Spec §8
requires this test on every account-scoped endpoint.

- [ ] **Step 3: Prove the fixture fails**

Temporarily remove the query filter from `SitesDbContext`, watch the cross-tenant test go red, put it
back. A fixture nobody has seen fail is a fixture that proves nothing — this repository has already
shipped two tests that were protecting the defect they were supposed to catch.

- [ ] **Step 4: Verify and commit**

```bash
cd backend && dotnet test
source ../scripts/dev && maran structure && maran migrate check
git commit -m "feat(host): serve the sites and ssl modules, with their IDOR fixtures"
```

---

### Task 16: The application's data layer

**Files:**
- Create: `frontend/src/types/{site,certificate,phpVersion}.ts`
- Create: `frontend/src/composables/apis/{useSitesApi,useCertificatesApi}.ts`
- Create: `frontend/src/stores/{sites,certificates}.ts`
- Create: `frontend/src/locales/{en,ru,hy}/{sites,certificates}.json`

**Interfaces:**
- Produces: `useSitesStore()` with `sites`, `selected`, `phpVersions`, `loading`, `acting`,
  `errorMessage`, and the actions `load`, `loadOne`, `create`, `changePhpVersion`, `enable`,
  `disable`, `remove`, `loadPhpVersions`; `useCertificatesStore()` with `issue`, `installCustom`,
  `remove`.

Follow `useAccountsApi.ts` and `stores/accounts.ts` exactly: every function a `const` arrow with an
explicit return type and JSDoc; the API composable called from the store and from nowhere else; the
backend's already-localized message stored verbatim on failure.

- [ ] **Step 1: The types, one domain per file**

`types/site.ts` holds `SiteBackendType`, `SiteStatus`, `Site`, `SiteDetail`, `CreateSiteRequest` and
the `SitesApi` interface. **No hardcoded list of PHP versions anywhere in the SPA** — `rules/vue.md`
forbids client-side domain data, and the versions are whatever the host has installed, which only the
agent knows.

- [ ] **Step 2: The composables**
- [ ] **Step 3: The stores**
- [ ] **Step 4: The locale files, all three, from the start**

Namespaces `sites.list.*`, `sites.form.*`, `sites.detail.*`, `certificates.*`. Only the SPA's own
chrome: server error text is rendered as it arrives and never keyed.

- [ ] **Step 5: Verify**

```bash
cd frontend && npm run lint && npm run typecheck
cd .. && source scripts/dev && maran structure
```

Expected: `STRUCTURE-OK`, which now includes the locale parity check across the three new files.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(frontend): the sites and certificates data layer"
```

---

### Task 17: The screens

**Files:**
- Create: `frontend/src/pages/sites/{SitesListPage,SiteFormPage,SiteDetailPage,SiteLogsTab,SiteSslTab}.vue`
- Create: `frontend/src/components/sites/{SiteStatusBadge,PhpVersionSelect,SiteBackendFields}.vue`
- Modify: `frontend/src/router/index.ts`, `frontend/src/composables/useNavigation.ts`

- [ ] **Step 1: The list, and the way in**

The site's domain is a link to its detail page, and `useNavigation` gains a Sites entry gated on
`meta.module`. A screen nothing links to is a screen nobody has — this repository has already shipped
three of them.

- [ ] **Step 2: The form**

`UiForm` (always `novalidate`), the UI kit for every control, and the backend's validator mirrored
rather than reimplemented: the field turns red on the server's answer, and the client's own hints are
advice. The backend type selector shows the PHP version field only for a PHP site and the upstream
field only for a reverse proxy.

- [ ] **Step 3: The detail page, with its tabs**

Overview, Logs and SSL. Deleting a site uses `UiConfirm` and names the consequence — the vhost goes,
the files stay, which is what the contract does and what the customer needs to know.

- [ ] **Step 4: The log tab streams**

`TailSiteLog` is a stream; the SPA reads it over SSE (spec §17: live data over SSE) and stops when the
component unmounts. A log line is customer-supplied text rendered into the DOM — it goes through
interpolation, never `v-html`, which `rules/vue.md` calls an XSS hole in a panel that renders exactly
this kind of content.

- [ ] **Step 5: Verify and commit**

```bash
cd frontend && npm run lint && npm run typecheck && npm run build
git commit -m "feat(frontend): the site screens, and a link that reaches them"
```

---

### Task 18: The Definition of Done, in a dedicated pass

**Files:**
- Create/complete: `backend/tests/Maran.Modules.Sites.Tests/**`, `Maran.Modules.Ssl.Tests/**`
- Create: `frontend/e2e/sites/{list,form,detail,php-version,ssl}.spec.ts`, `frontend/e2e/fixtures/stub-sites-route.ts`

`rules/testing.md` puts the tests in their own pass after the implementation, and names the five things
a feature needs before it is done. This task is that pass, and it is not optional polish: *"code
without its tests is unfinished work, not finished work awaiting tests."*

- [ ] **Step 1: Unit tests for every handler**

Including the failure paths — *"every typed error variant of a feature appears in at least one test"*.
That means a test for each of `SitesOpError`, `PhpOpError` and `SslOpError`'s variants, including the
`nginx -t` rollback.

- [ ] **Step 2: Integration tests of the real surface**

Over HTTP against Testcontainers-PostgreSQL, as `SetupEndpointTests` does.

- [ ] **Step 3: The IDOR tests**

Already begun in Task 15; complete them for certificates too.

- [ ] **Step 4: Assert the audit events**

Each mutation writes one, and a test reads it back. Also assert the journal never carries a private
key or tool output.

- [ ] **Step 5: The i18n keys exist in all three locales**

`maran structure` proves it, and the resx parity test proves the backend half.

- [ ] **Step 6: Playwright specs**

Stubbed routes, as the accounts specs do: the list renders, the form refuses what the server refuses,
the PHP selector offers what the API returned rather than a hardcoded list, deleting asks first.

- [ ] **Step 7: Verify and commit**

```bash
cd backend && dotnet test
cd ../frontend && npx playwright test
git commit -m "test: the five parts of Done for sites, PHP and certificates"
```

---

### Task 19: The golden path, in a real browser

**Files:**
- Create: `frontend/e2e/golden-path/account-to-ssl.spec.ts`
- Modify: `docs/superpowers/notes/` (the threat note from Task 7, completed)
- Modify: `CHANGELOG.md`, `README.md`

Spec §16 names this path explicitly: install → account → site → SSL → file → database → cron →
suspend. This plan owns the first four; the rest arrive with later plans, and the spec is unstaged
here.

- [ ] **Step 1: Run the whole stack and drive it**

```bash
source scripts/dev
maran dev
```

Then, in a browser through Playwright, against the **real** API and a **real** agent — not stubs:
sign in, create an account, create a site on it, watch the vhost appear, switch its PHP version, issue
a self-signed certificate, and load the site.

- [ ] **Step 2: Verify by looking**

Take a screenshot at each step and read it. A passing assertion says an element existed; a screenshot
says the operator can use the screen. This is the step that catches a form that validates correctly
and is unusable — the kind of defect no unit test has ever found in this repository.

- [ ] **Step 3: Complete the threat note**

Task 7 wrote it about `privs`. Extend it to the whole feature: config injection through a domain, an
upstream pointing at the internet, a customer's PHP override reaching `disable_functions`, a private
key in a log, a certificate installed for a domain the account does not own.

- [ ] **Step 4: Update the changelog and the readme**

The README's status section lists what works. Sites, multi-PHP and SSL join it — and if a limitation
ships with them (only HTTP-01, no wildcards), it is named there rather than discovered.

- [ ] **Step 5: The full gate run**

```bash
source scripts/dev
maran check && maran structure && maran format --check && maran proto && maran migrate check && maran licenses --check
cd backend && dotnet test
cd ../agent && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test
cd ../frontend && npm run lint && npm run typecheck && npm run build && npx playwright test
cd .. && maran handshake
```

Every one green, with the numbers written into the pull request. A claim that the gates pass is not
evidence; the output is.

- [ ] **Step 6: Open the pull request into `dev`**

With the threat note in the description, because this feature changed `privs` and
`rules/security.md` requires a second reviewer for it.

---

## Deliberately out of scope

Named so their absence is not read as an oversight:

- **Wildcard certificates and DNS-01** — spec §11 puts them with the DNS module, which is not in v1.
- **Custom nginx snippets for administrators** — spec §11 allows them, and they are a config-injection
  surface that deserves its own design and its own threat note rather than a paragraph in this plan.
- **phpMyAdmin as an isolated vhost** — spec §3 makes it optional and later.
- **The panel's own certificate** — the installer's self-signed cert becoming a Let's Encrypt one is
  installer work (spec §10), and shares this plan's ACME client rather than preceding it.
- **Per-site resource metering** — arrives with Monitoring, roadmap item 5.
- **`open_basedir` as a security boundary** — it is not one, and the pool relies on the uid. Said
  plainly here so nobody later treats the template line as protection.

## Self-review

**Spec coverage.** §9's config pipeline → Task 5. §9's fork-and-drop → Task 7. §9's streaming for long
operations → Tasks 8 and 10. §11 sites (domains, aliases, docroot, three types, per-site PHP, logs,
enable/disable) → Tasks 6, 8, 17. §11 multi-PHP (7.4–8.4, Sury/Remi, install on demand, pool per
account × version, safe subset) → Tasks 2, 8. §11 SSL (HTTP-01 webroot, renewal at 30 days, custom
certificates, self-signed) → Tasks 9, 14. §8 MaxSites in the application → Task 13. §8 pool workers
from the plan → Task 8. §8 global query filters → Task 13. §4 both families → Tasks 2, 11. §15
correlation id → Tasks 10, 12. §16 goldens → Task 4; container matrix → Task 11; DoD → Task 18;
Playwright golden path → Task 19. §17 three locales → Tasks 16, 18.

**Gaps found while reviewing, and closed:** the contract was never compiled into the agent (Task 1);
there was no way to discover installed PHP versions, though the panel must choose from them (Task 1);
the polygon could not run `nginx -t`, so the validator central to the whole design had never validated
anything (Task 11).

**Type consistency.** `Domain` and `Upstream` are the parameter types from Task 3 onwards, never
`&str`. `write_config` has one signature, used by Tasks 6, 8 and 9. `SiteBackendType` is the proto
enum's name in C# and TypeScript alike; the Rust side calls its own type `SiteKind` because it carries
data per variant and a name that differs from the wire type is honest about that.
