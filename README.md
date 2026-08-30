# Maran

A modern web hosting control panel for Linux servers. Install it on a server and manage
websites, PHP versions, SSL certificates, databases, files, backups and the firewall from a
browser, with a separate cabinet for every hosting customer and an API that billing systems
drive to create accounts automatically.

Source-available and free to self-host; commercial modules are distributed through the
Innovayse marketplace.

## Why Maran

Existing panels are either expensive and closed, or free but built on architectures that keep
producing security advisories. Maran takes a different position:

- **Security by construction, not by patching.** The web application never runs as root and
  never executes shell strings. All privileged work goes through a separate root daemon that
  accepts only a fixed set of typed commands, so the whole class of remote-code-execution bugs
  that has repeatedly hit control panels is designed out rather than guarded against.
- **Multi-tenant from the first release.** Every hosting customer gets their own login,
  isolated by real Linux users and filesystem permissions, and sees only their own resources.
- **Built to be sold hosting on.** A provisioning API lets a billing system create, suspend,
  resize and terminate accounts without human involvement.
- **Readable, modular code.** Each feature is one self-contained module with its own database
  schema; module boundaries are enforced by the build, not by convention.

## Features

**Websites** — PHP, static and reverse-proxy sites; domain aliases; per-site PHP version;
access and error logs in the interface; HTTPS and www redirects; enable/disable.

**PHP** — versions 7.4 through 8.4 side by side, installed on demand. Each account gets its
own pool running under its own user, with a safe subset of settings exposed to customers.

**SSL** — free Let's Encrypt certificates with automatic renewal, custom certificate upload,
self-signed fallback.

**Databases** — MySQL/MariaDB databases and users with per-plan limits, with a web database
manager available as an optional isolated module rather than bundled into the panel.

**File access** — SFTP with chroot by default, FTPS for compatibility, plus a browser file
manager with uploads, an editor, archives, permissions and search.

**Scheduled tasks** — per-account cron with a schedule builder, environment variables and
last-run output.

**Firewall** — nftables management with sensible presets, custom rules, IP allow-lists and
automatic banning of brute-force sources.

**Monitoring** — CPU, memory, disk, network and load graphs, service status, per-account disk
usage against quota, and email alerts on thresholds.

**Backups** — scheduled file and database backups, local or S3-compatible storage, restore
from the interface, retention policies.

**Accounts and plans** — hosting accounts with quotas and limits, customer logins, suspension
as a first-class state, and a full audit log of every action.

**Security** — two-factor authentication, revocable sessions, rate limiting with automatic
firewall bans, scoped API keys, and role-aware error messages.

## Architecture

Three processes per server, with a strict separation of privilege:

    Browser (Vue SPA)  ──REST/SSE──►  maran-api   (C#, runs as an unprivileged user)
    Billing system     ──REST─────►        │            business logic, auth, modules
                                           │            PostgreSQL is reachable only here
                                           ▼ gRPC over a unix socket
                                    maran-agent  (Rust, the only root process)
                                           ▼
             nginx · php-fpm · MySQL · SSH/FTP · cron · nftables · certificates

The API holds all the intelligence but no privileges. The agent holds all the privileges but
no intelligence: it is stateless, accepts a closed set of typed commands, re-validates every
input, writes configuration through render-validate-swap with rollback, and performs customer
file operations under that customer's own user id. There is no command that runs arbitrary
programs, and paid modules never load into the root process.

Background work (certificate renewal, backups, multi-step provisioning) runs on durable
queues stored in PostgreSQL — no message broker is installed on the server.

## Technology

C# on .NET 9 (ASP.NET Core, EF Core, Wolverine) · Rust (tokio, tonic) · PostgreSQL 16 ·
Vue 3 with TypeScript, Vite, Pinia and Tailwind CSS · gRPC over unix sockets for the
API-to-agent contract.

## Supported systems

| Family | Versions |
|---|---|
| Debian | Ubuntu 22.04 LTS, Ubuntu 24.04 LTS, Debian 12, Debian 13 |
| RHEL | AlmaLinux 9, AlmaLinux 10, Rocky Linux 9, Rocky Linux 10 |

Architectures: x86_64 and aarch64. Distribution differences are isolated behind an adapter
layer in the agent, so support for further systems is additive.

## Installation

Production installs are native — no containers, no extra daemons beyond PostgreSQL and the
two panel processes:

    curl -sSL https://get.maran.com | bash

The installer verifies the system before changing anything, installs signed release
artifacts, hardens the systemd units, and prints a one-time link for creating the first
administrator in the browser. Updates are signed, taken in one click or via the `maran`
command line tool, and reversible with an automatic database dump and a rollback command.

## Repository layout

    proto/       the API-to-agent contract; both sides generate their code from it
    backend/     C# modular monolith: thin host, shared kernel, module SDK, feature modules
    agent/       Rust root daemon: gRPC server, operations, distribution adapters, templates
    frontend/    Vue 3 single-page application: administration area and customer cabinet
    installer/   native production installer, systemd units, uninstaller
    docker/      development environment only, never used in production
    rules/       normative engineering rules for contributors
    docs/        design and planning documents
    scripts/     developer helper scripts

## Development

    source scripts/dev   # toolchains and the `maran` command on PATH
    maran                       # the toolbox: every command, with what it is for
    maran check                 # verify the toolchain before anything else
    maran dev                   # the whole stack: database, API, application

`maran dev` starts the PostgreSQL container, the API and the SPA, waits until each answers,
and then streams only warnings and errors; Ctrl+C stops everything. Docker carries development
dependencies only — the API and the application run natively, exactly as they do on a server.

Verification, each runnable on its own:

    cd backend  && dotnet test         # unit, architecture and integration tests
    cd agent    && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test
    cd frontend && npm ci && npm run lint && npm run typecheck && npm run build
    cd frontend && npx playwright install --with-deps chromium && npm run test:e2e
    maran handshake      # agent and API over a real unix socket

The application has no unit-test runner by design: it is verified end to end against a running
API in `frontend/e2e/` (rules/testing.md).

`maran` is on PATH after sourcing `scripts/dev`, so it works from any directory in
the repository. It dispatches to the scripts in `scripts/lib/` — `format`, `migrate`, `module`, `structure`,
`proto`, `agent`, `handshake` — and printing it with no arguments lists them with what each is for.
The scripts remain runnable directly; CI calls them by path. `scripts/dev` is the one that must be
sourced rather than run, because a subprocess cannot put toolchains on its parent's PATH.

`maran agent` runs the Rust toolchain and falls back to a pinned container when the machine
has no C linker, so a fresh clone can build the agent before installing anything.

Requirements: .NET 9 SDK, Rust (stable), Node.js 20+, protoc, a C toolchain for Rust linking
(`build-essential`), and Docker for development only.
Contributions must follow the rules in `rules/`; they are enforced in continuous integration.

## Status

In active development toward the first release. The foundation is in place — the agent contract,
the Rust agent, the backend host and the application shell — and so is the first feature set:
first-run setup, sign-in with two-factor authentication, sessions you can see and revoke, an
append-only audit journal, and hosting accounts provisioned as real Linux users with disk quotas.

Sites, databases, FTP, cron, backups and the firewall are the modules that follow.

Mail and DNS management, reseller accounts and central management of multiple servers are
planned after the first release.

## License

Business Source License 1.1 — free to self-host and modify, source available to everyone,
with a conversion to an open source license on the date stated in `LICENSE`.

## Security

Report vulnerabilities privately as described in `SECURITY.md`. Please do not open public
issues for security problems.

---

Built by [Innovayse LLC](https://innovayse.com), Yerevan, Armenia.
