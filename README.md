# Maran

[![backend](https://github.com/innovayse/maran/actions/workflows/backend.yml/badge.svg?branch=dev)](https://github.com/innovayse/maran/actions/workflows/backend.yml)
[![agent](https://github.com/innovayse/maran/actions/workflows/agent.yml/badge.svg?branch=dev)](https://github.com/innovayse/maran/actions/workflows/agent.yml)
[![frontend](https://github.com/innovayse/maran/actions/workflows/frontend.yml/badge.svg?branch=dev)](https://github.com/innovayse/maran/actions/workflows/frontend.yml)
[![cross](https://github.com/innovayse/maran/actions/workflows/cross.yml/badge.svg?branch=dev)](https://github.com/innovayse/maran/actions/workflows/cross.yml)
[![licence: BSL 1.1](https://img.shields.io/badge/licence-BSL%201.1-blue)](LICENSE)

A modern web hosting control panel for Linux servers. Install it on a server and manage
websites, PHP versions, SSL certificates, databases, files, backups and the firewall from a
browser, with a separate cabinet for every hosting customer and an API that billing systems
drive to create accounts automatically.

Source-available and free to self-host; commercial modules are distributed through the
Innovayse marketplace.

Maran is a product of **[Innovayse LLC](https://innovayse.com)**, a software company in Yerevan,
Armenia. Innovayse builds it, signs its releases, runs the licence and marketplace service behind
its commercial modules, and answers for it — the panel is not a side project or a community
volunteer effort, and the name on the signature is the name of the company you would call. Releases
are published from `releases.maran.innovayse.com` and the installer is served from
`get.maran.innovayse.com`, both under Innovayse's own domain, which is also what makes the release
signing key traceable to somebody accountable rather than to an anonymous build.

Two consequences worth stating in the same breath as the name, because a company standing behind a
product is only meaningful if it says what it does NOT promise. The licence is Business Source
License 1.1: the source is available to everyone and you may self-host and modify it, which means
Innovayse cannot and does not claim the code on your server is unmodified. And when the licence
service is unreachable, **only commercial modules degrade — the core never stops.** Customers'
servers are not held hostage to Innovayse's uptime.

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

**Databases** — MySQL/MariaDB databases and users with per-plan limits. A web database manager is
**not** part of this release: it is planned as a separate deployable with its own vhost and
authentication rather than bundled into the panel. This line used to read as though it were
available.

**File access** — SFTP with chroot by default and FTPS for compatibility, installed switched off and
turned on per server by an administrator (both shipped); a browser file manager with uploads, an
editor, archives, permissions and search (planned).

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

C# on .NET 9 (ASP.NET Core, EF Core, Wolverine) · Rust (tokio, tonic) · PostgreSQL ·
Vue 3 with TypeScript, Vite, Pinia and Tailwind CSS · gRPC over unix sockets for the
API-to-agent contract.

This line used to say "PostgreSQL 16". On a server the installer installs the distribution's own
`postgresql` package (`installer/lib/30-postgresql.sh:29,32`) and checks no version, so across the
supported matrix the major version is whatever the distribution ships — not 16. The **development**
stack is the pinned one: `postgres:16-alpine` in `docker/docker-compose.dev.yml:13`.

## Supported systems

| Family | Versions |
|---|---|
| Debian | Ubuntu 22.04 LTS, Ubuntu 24.04 LTS, Debian 12, Debian 13 |
| RHEL | AlmaLinux 9, AlmaLinux 10, Rocky Linux 9, Rocky Linux 10 |

Architectures: x86_64 and aarch64. Distribution differences are isolated behind an adapter
layer in the agent, so support for further systems is additive.

## Installation

Production installs are native — no containers. This paragraph used to add "no extra daemons
beyond PostgreSQL and the two panel processes", which was never true and is the kind of sentence an
operator reasons about their own attack surface with. The installer installs **nginx** and
**PostgreSQL** as OS packages in step 20 (`installer/lib/20-dependencies.sh`, which names both in
its own header and in the package list for each family), configures nginx at step 80, and installs
and starts **MariaDB** (85), **nftables** (87) and **cron** (88), with **vsftpd** installed switched
off (89) — `grep -n 'pkg_install' installer/lib/*.sh` names every step that installs packages. This
list previously left nftables out altogether, even though `installer/lib/87-firewall.sh:640` installs
it the same way as the rest. A correction in the other direction was attempted and reverted: a pass
moved nginx from step 20 to step 80 on the reasoning that 80 is the nginx step, which confuses
installing a package with configuring it — step 20 is where `nginx` appears in the package list, and
the claim as it originally stood was right. What
the rule in `rules/architecture.md` actually says is narrower and does hold: *Maran itself* is three
processes — `maran-api`, `maran-agent` and PostgreSQL — and the panel adds no broker and no sidecar
of its own. The rest are the services a hosting panel exists to manage.

    curl -sSL https://get.maran.innovayse.com | sudo bash

The installer verifies the system before changing anything, installs signed release
artifacts, hardens the systemd units, and prints a one-time link for creating the first
administrator in the browser. Updates are signed, taken in one click or via the `maran`
command line tool, and reversible with an automatic database dump and a rollback command.

### What this puts on your server

The sections above answer this piecemeal; here it is in one place, so an operator can see the
whole attack surface without reading the installer scripts. Every row is a fact about this tree,
not a description — cite the path if it stops being true.

| Process | Runs as | Listens on | Stores |
|---|---|---|---|
| `maran-agent` | root — the only process the panel itself runs as root (`installer/systemd/maran-agent.service`, `--allow-uid`, `agent/crates/agent/src/main.rs:94`) | a unix socket only, `/run/maran/agent.sock`, narrowed to one caller uid; no network of any kind | its own render/config/state under `/etc/maran`, `/var/lib/maran*`; installer and agent logs under `/var/log/maran` |
| `maran-api` | an unprivileged system user, `maran` (rules/security.md §8; `installer/lib/60-config.sh`) | a unix socket only, `/run/maran-api/api.sock` (`installer/panel.env.example:62`, `ASPNETCORE_URLS=http://unix:...`) — opens no TCP port itself | nothing of its own on disk; all panel state is in PostgreSQL |
| nginx — panel vhost | master: root (nginx's own model); workers: nginx's own unprivileged user | **TCP 8443, TLS only.** There is no port 80 server block for the panel and nothing to redirect from (`installer/nginx/maran.conf`, closing comment) | `/var/log/maran/nginx-{access,error}.log`, with the access log's format changed specifically so it cannot record a query string (same file, `log_format maran_no_query`) |
| nginx — customer sites | same nginx process | TCP 80, and TCP 443 with a certificate, per site (`agent/crates/templates/templates/nginx/*.j2`) | the site's own document root inside the account's home |
| PostgreSQL | its own `postgres` user, not root | **no TCP at all** — `listen_addresses=''`, enforced and then verified against the running server (`installer/lib/30-postgresql.sh:105-108,164-166`) — unix socket, reachable only from `maran-api` | every module's schema: accounts, sessions, audit journal, site and certificate metadata, plans |
| MariaDB/MySQL | its own `mysql`/`mariadb` user | the agent authenticates as `root@localhost` over the unix socket with no credential of any kind (`installer/lib/85-mysql.sh`); every account and grant the panel creates is scoped to `'<user>'@'localhost'`, never `'@'%'` (`agent/crates/ops/src/db/create_database.rs:25`) | customer databases; the panel keeps no copy of a database password once it has been shown |
| php-fpm | one pool per account, running as that account's own Linux user, never root | a unix socket per pool under `/run/maran/php` | nothing beyond the pool's own state; the account's files live in its home |
| sshd (SFTP) | root, per the operating system's own default — unmodified by this installer beyond the one config block below | TCP 22, already open on any server reachable by SSH | the one `Match Group` block making membership of the SFTP group a chroot into a jail — see "What ships with those" below for why nothing re-checks it |
| vsftpd (FTPS) | root at startup, per the daemon's own model; **installed but not running** until switched on (see below) | nothing until an administrator turns it on; then TCP 21 plus the configured passive range | account jails under `/var/lib/maran-ftps`, bind-mounting the account's real home |
| cron | root, the system service — untouched by the installer beyond scheduling per-account jobs | none | each account's own crontab |
| nftables | not a daemon — no listening socket of its own; the kernel holds the loaded ruleset | n/a | the ruleset file at `AgentPaths::nftables_ruleset_path()`, `/etc/nftables.conf` (Debian) or `/etc/sysconfig/nftables.conf` (RHEL) (`installer/lib/87-firewall.sh:109-113`) |

The only two processes root-owned **because Maran put them there** are `maran-agent` and, when an
administrator switches it on, `vsftpd`. Every other root process on this list is a Linux server's
ordinary furniture (`sshd`, `cron`, the nginx and MariaDB/PostgreSQL start-up sequence dropping to
their own users) that this installer configures rather than introduces.

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

    source scripts/dev                 # sourced, not run: toolchains and `maran` on your PATH
    maran                              # the toolbox: every command, with what it is for
    maran check                        # what this machine is missing, before anything else
    maran dev                          # the whole stack: database, API, application

Everything runs through one CLI. `maran` is on PATH after sourcing `scripts/dev`, so it works
from any directory in the repository; its help is generated from the command table, so a
command that exists is a command that is documented. It dispatches to the scripts in
`scripts/lib/`, which are implementations rather than the surface — call `maran`, not them.
`scripts/dev` is the one file that must be sourced rather than run, because a subprocess cannot
put toolchains on its parent's PATH.

`maran dev` starts the PostgreSQL container, the API and the SPA, waits until each answers, and
then streams only warnings and errors; Ctrl+C stops everything. Docker carries development
dependencies only — the API and the application run natively, exactly as they do on a server.

`source scripts/dev` also creates `.env` from `.env.example` the first time. That file is
git-ignored and is the only place local configuration lives.

Verification, each runnable on its own:

    maran structure                    # file and folder laws no compiler can express
    maran format --check               # formatting and naming, every language
    maran proto                        # the API-to-agent contract
    maran migrate check                # a model edited without a migration
    maran agent check                  # rustfmt, clippy -D warnings, cargo test
    maran handshake                    # agent and API over a real unix socket
    cd backend  && dotnet test         # unit, architecture and integration tests
    cd frontend && npm run lint && npm run typecheck && npm run build && npx playwright test
    maran website                      # the public site, when checked out beside this repository

The application has no unit-test runner by design: it is verified end to end against a running
API in `frontend/e2e/` (rules/testing.md).

The public site lives in its own repository — `gitlab.com/innovayse/maran-website`, cloned into
`website/` when you want both side by side — and this one ignores that directory. It is governed by
rules/nuxt.md all the same, and it is verified the other way round from the panel: its logic is
small, pure and directly callable — content resolution and its English fallback, sidebar shaping, structured data, locale
parity — so it is tested with Vitest in `website/tests/`, and its lint gate additionally checks the
content itself: a release published in `CHANGELOG.md` with no release page, frontmatter that does
not match its schema, an internal link that resolves to nothing, and the three locale directories
disagreeing about their keys.

`maran agent` runs the Rust toolchain natively and falls back to a pinned container when the
machine has no C linker, so a fresh clone can build the agent before installing anything.

Requirements: .NET 9 SDK, Rust (stable), Node.js 20+, protoc, a C toolchain for Rust linking
(`build-essential`), and Docker for development only.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first — it covers the setup above, the gates a pull
request has to pass, and the changes that need a second reviewer and a written threat note.

The rules in [`rules/`](rules/) are binding and mostly enforced by a command rather than by a
reviewer's memory. Work branches from `dev` and merges back into it; `main` is the release
branch and takes nothing but a reviewed pull request from `dev`.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Status

### This is a beta

**Do not put somebody else's customers on it.** A beta here means the software installs, runs and
does what the screens say — and that several of the things a paid hosting product must be trusted
about have never been exercised anywhere except this repository's own containers.

Three of those, named rather than buried in the list below, because they are the ones that decide
whether a server survives a bad day:

- **No certificate has ever been issued by a real authority.** The whole ACME path runs end to end
  against `pebble`, Let's Encrypt's test server, and that is not the same thing: not their rate
  limits, not validation from the public internet, not their chain. The renewal branch is not
  exercised at all.
- **No disk limit has ever been enforced by a real filesystem.** The panel now reports honestly
  when a host cannot enforce one, which is the fix that mattered — but a write refused at the limit
  has been observed on a loopback filesystem in a container and nowhere else.
- **No restore has been run on real hardware.** It has been run on the polygon, including one
  killed mid-swap, and that is where the evidence ends.

**The threat notes covering the privileged parts were reviewed on 2026-09-23**, and the verdict is
recorded per note in `docs/superpowers/notes/2026-09-22-second-review-packet.md` — twenty-eight of
the thirty-two, among them `privs` in the root daemon, account deletion, the setup token and the
suspension session cull. What that buys and what it does not, stated plainly: a second person, who
wrote none of them, read the arguments and accepted the risk, which is what `rules/security.md`
asks for. It is not an external audit, and the notes themselves were written by the agent sessions
that made the changes.

What is absent by design in the first release — no DNS, no mail, no web database manager — is
listed below with the rest, and none of it is a defect.

### The work itself

In active development toward the first release. The foundation is in place — the agent contract,
the Rust agent, the backend host and the application shell — and so is the first feature set:
first-run setup, sign-in with two-factor authentication, sessions you can see and revoke, an
append-only audit journal, and hosting accounts provisioned as real Linux users with disk quotas.

Sites have joined them: static, PHP and reverse-proxy vhosts rendered by the agent and validated
by the server's own `nginx -t` before anything is reloaded, domain aliases, enable and disable,
and live access and error logs in the interface. So has multi-PHP — one php-fpm pool per account
per version, running under the account's own user, with the worker budget taken from the plan and
a whitelisted subset of settings customers may change. And so has SSL: certificate material
installed into a root-only store outside every account's home, an HTTP-01 ACME client, and
renewal thirty days before expiry.

What ships with those, said here rather than left to be discovered:

- **SSL is HTTP-01 only.** No wildcards and no DNS-01 — those belong with the DNS module, which
  is not in the first release. The domain must already resolve to the server.
- **The ACME client completes a real issuance, and Let's Encrypt itself is still unproven.**
  Those are two separate facts and this bullet used to state only the first half of the first one.
  The whole path — account registration with a contact address, order, HTTP-01 challenge served and
  validated, finalise, download — now runs end to end against `pebble`, Let's Encrypt's own test
  ACME server, with the issued certificate's serial in pebble's log matching the serial read back
  out of the PEM. That exercise found a real defect no test had: the client sent no `User-Agent`,
  which pebble refuses as `malformed` on every request. Let's Encrypt is permissive about it, so
  only a stricter authority could have surfaced it.
  What is still owed is the authority, not the protocol: pebble is not Let's Encrypt's service,
  its rate limits, its validation from the public internet, or its chain, and none of that can be
  tried without a publicly resolvable domain with inbound reachability. Nor is the renewal path
  proven live — the test forces a fresh challenge every run, so the "already valid, skip the
  challenge" branch that a renewal actually takes was deliberately not exercised. Treat issuance
  against the real authority as unproven.
- **An account created by an earlier build cannot serve a site, and there is now a repair for it.**
  Creating an account group-owns its home by the web server's group so the server can reach the
  document root; homes created before that do not have it, and nginx cannot traverse to their
  document root. An administrator-only repair reports what it would change and then narrows only
  what it reported, refusing six ways rather than guessing once — a home that is not where the
  account says it is, missing, a symlink, not a directory, on a different mount, or owned by
  another uid. It runs as root, which is why it refuses rather than repairs when anything about a
  path is not what it expects. The manual `chgrp www-data /home/<account>` (`nginx` on the RHEL
  family) still works and is what the repair does.
- **`open_basedir` is not a security boundary.** Isolation between accounts comes from the pool's
  uid; the `open_basedir` line is there against accidents, not attackers.
- **An FTP daemon IS installed, and it is switched off.** This sentence used to say the opposite —
  "no FTP daemon is installed" — and it was wrong for the whole life of `installer/lib/89-ftps.sh`,
  which is worth stating rather than quietly correcting: an operator who believes there is no FTP
  server on the host does not go looking for one. What the installer actually leaves behind is
  vsftpd's package, the distribution's own `vsftpd.service` **masked** so that package cannot start
  it, a `maran-ftps` system group, the jail base `/var/lib/maran-ftps`, `/etc/pam.d/maran-ftps`, and
  Maran's own `maran-ftps.service` **installed, disabled and stopped**. Nothing is listening: no
  port 21, no data port, no configuration file even rendered, until an administrator turns FTPS on
  from the panel — which opens the ports it needs at that moment and not before. The install
  transcript says so as it happens, and `/var/log/maran/install.log` keeps it.
- **When FTPS is on, it is TLS-only and group-gated.** `force_local_logins_ssl` and
  `force_local_data_ssl` are `YES`, anonymous login is off, and every login is chrooted into a jail
  whose only entry is a bind mount of its own home. Authorization is membership of the `maran-ftps`
  group and nothing else: `root`, the panel's own service account and every other system account are
  refused by the PAM stack whether or not they hold a password. SFTP remains the default.
- **There is no web database manager yet.** The phpMyAdmin-style module described above is a
  separate deployable with its own vhost and authentication, and it is not in this release.
- **A database grant an earlier build issued keeps its wildcard pattern until an administrator
  repairs it.** A database-level `GRANT`'s name is a LIKE-style pattern, and every `_` in it matches
  any single character; a build before this fix escaped none of them, so an account whose name
  collides at the same length as another's could reach that account's database. The escape now
  written by `create_database` is forward-only — there is deliberately no migration, because one
  would rewrite live customer access with nobody watching — so a host that had already created
  databases carries unescaped rows in `mysql.db` until the repair in the panel's Databases screen is
  run against it (`RepairDatabaseGrants`, `agent/crates/ops/src/db/repair_grants.rs`). It reports
  what it would change before it changes anything, touches only rows it recognises as its own, and
  leaves everything else untouched. See
  `docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md` for the collision and
  `docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md` for the repair itself — both
  carry an outstanding second review.
- **A disk quota the filesystem cannot enforce is now reported instead of displayed as if it held.**
  A plan's disk limit is something a customer paid for, and it binds only if the filesystem was
  mounted with quota accounting and `quotaon` was actually run. Those are separate steps an operator
  can miss, and until now nothing observed them: the panel showed the plan's own figure, the kernel
  enforced nothing, and no screen, log or alert could ever contradict the other — an unenforced paid
  limit was undetectable by construction until the disk filled. The host's enforcement state is now
  read as its own fact, beside the sold figure rather than replacing it, monitoring raises an alert
  while a host cannot enforce, and the interface stops drawing a usage bar it knows means nothing.
  The panel never enables quotas by itself: mount options and `quotaon` on a live server are the
  operator's decision, and a root daemon remounting a filesystem unattended is worse than an honest
  refusal. Two limits, stated: enforcement is decided from `quotaon -p`, so an NFS home — where
  enforcement lives on the export server — is a case this does not cover; and a limit binding is
  proved on a real quota-enabled filesystem, not by any unit test.
- **A database password is shown once and never stored.** Losing it means resetting it, not
  looking it up; the same is true of an SFTP login's password.
- **Dropping a database is final.** Nothing is backed up first.
- **What bounds an SFTP login after it authenticates is the account, not this feature.** The
  session is chrooted, shell-less and cannot forward ports, but it runs as the account's uid, so
  its CPU, memory and process limits are the account's — nothing here sets them.
- **The `Match Group` block sshd needs is written once by the installer, and the panel now notices
  when it goes.** If it is removed by hand or by a package upgrade, SFTP logins become full shell
  sessions — the panel used to keep showing them as jailed, which is the worst kind of wrong,
  because nothing looks broken. Monitoring now reads the block and raises an alert when it drifts,
  resolving it when the block comes back. All four directives that do the jailing are checked
  individually, not just the `Match Group` line: a block whose header survived and whose body was
  emptied is exactly the drift worth catching.
  The check reads sshd's configuration file rather than asking `sshd -T`, and that was measured
  rather than assumed — `sshd -T` prints the effective configuration but never the contents of a
  `Match` block at all, so a check built on it would have been blind to the thing it reports on.
  What it does NOT do: follow `Include` directives, and prove that a drifted host really grants a
  shell. It reports that the configuration no longer says what the installer wrote.

Cron, backups, the firewall and monitoring have since joined them — this line used to say they
were "the modules that follow", which stopped being true when they landed. Each ships as a backend
module with its own screens: `backend/src/Maran.Modules/{Cron,Backups,Firewall,Monitoring}/` and
`frontend/src/pages/{cron,backups,firewall,monitoring}/`.

Mail and DNS management, reseller accounts and central management of multiple servers are
planned after the first release.

## License

Business Source License 1.1 — free to self-host and modify, source available to everyone,
with a conversion to an open source license on the date stated in `LICENSE`.

## Security

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Please do not open
public issues for security problems.

Probing your own installation is welcome. Probing somebody else's is not, and no finding
excuses it.

---

Built by [Innovayse LLC](https://innovayse.com), Yerevan, Armenia.
