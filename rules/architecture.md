# Architecture Rules

Normative. A PR violating a MUST/MUST NOT is rejected regardless of how well it works.
Spec: `docs/superpowers/specs/2026-08-29-maran-design.md`.

## System boundaries

- The system is exactly three processes: `maran-api` (C#, runs as user `panel`), `maran-agent` (Rust, the ONLY root process), PostgreSQL (unix socket only). **MUST NOT** add daemons, brokers, or sidecars.
- Only `maran-api` talks to PostgreSQL. The agent **MUST** stay stateless: no DB, no config files of its own beyond what the installer writes.
- Only `maran-agent` mutates the system (users, configs, services). The API **MUST NOT** shell out, write system configs, or require root — ever.
- API ↔ agent traffic goes through the gRPC contract in `proto/agent/v1/` and nothing else.

## Backend: modular monolith

- A feature lives in exactly one module project `Maran.Modules.<Name>`.
- A module **MUST** reference only `Maran.Sdk` and `Maran.SharedKernel`. Referencing another module is forbidden and fails the NetArchTest suite (`backend/tests/Maran.ArchitectureTests`). Cross-module needs go through Wolverine messages or Sdk abstractions.
- Each module owns one PostgreSQL schema named after it (`accounts.*`, `sites.*`). **MUST NOT** query another module's schema.
- **A facility more than one module needs is its own module, never a lodger in the module that happened to need it first.** Outgoing mail is the worked example: the SMTP settings, the sender and the `SendMailRequested` handler lived in Monitoring because the alert evaluator was the first thing that wanted to send. The consequence was invisible and security-relevant — Identity's password reset silently depended on Monitoring being loaded, and with that module disabled the event reached a queue with no handler and nothing told the operator. So the test is not "who wrote it first" but "who would still need it if that module were removed": if the answer is anybody, it is a module of its own (`Maran.Modules.Notifications`), and every consumer reaches it the same way.
- **A shared facility's contract lives in `Maran.Sdk`; its implementation and its internals do not.** Two shapes, and the choice between them is the question of who may hold the secret. A message (`Sdk/Contracts/SendMailRequested.cs`) when the consumer only needs the thing done and does not wait for it. A read-only window in `Sdk/Interfaces/` — `IAccountDirectory`, `ISiteDirectory`, `IAlertRecipientDirectory` — implemented by the owning module, when the consumer must read one fact it cannot know. What NEVER moves to the Sdk is the seam that holds the credential: `IMailer` stays internal to the sending module, so no consumer can reach the mail server's password, and no consumer is privileged over another. A window exposes the narrowest value that answers the question — an address, not the settings record it was read from.
- **A module declares which parts of the agent it may drive, and the panel enforces it.** The
  `Manifest` carries `AgentCapabilities`; `AgentCapabilityGuard` refuses at composition time to load
  a module whose declared dependencies reach past that list. One door to a root process, shared by
  every module in one process, is a door that has to be accounted for per module — most of all when
  the module was bought rather than written here (rules/security.md item 13).
- **Handlers come from the module registry, not from a scan — and that is enforced by there being
  nothing to find.** `MessagingExtensions` names each module assembly explicitly. Wolverine still
  scans the entry assembly on its own and cannot be stopped from doing so
  (`DisableConventionalDiscovery` disables the explicit list along with the default, measured: 201
  of 233 integration tests fail), so `HandlerLocationTests` asserts `Maran.Host` declares no handler
  — which makes that scan a scan over nothing. A handler belongs in the module that owns its
  operation.
- Inside a module, code is organized per operation: `Commands/<Operation>/` holds the command, its handler and its validator together; `Queries/<Operation>/` likewise (rules/csharp.md). No `Managers/`, `Helpers/` or `Misc/` grab-bags.
- `Maran.Host` composes modules and holds no business logic. `SharedKernel` holds primitives only (`Result`, errors, `ICurrentUser`, `IClock`) plus the general-purpose helpers in `Utilities/<Subject>/` that more than one module asks (rules/csharp.md) — if it grows domain concepts, that's a smell.

## Agent

- The command set is closed and typed. **MUST NOT** add any RPC that executes caller-supplied programs, shell strings, or templates.
- Every command is idempotent: repeating it converges to the same state and reports `AlreadyExists`/`NotFound` instead of failing.
- Every input is re-validated inside the agent (names against allow-list regexes, paths canonicalized and required to stay under `/home/<account>/`) even though the API validated already.
- File operations on customer data run under the account's UID (fork + setuid), never as root.
- Config writes follow: render template → temp file → validate (`nginx -t` etc.) → atomic rename → reload → typed error + rollback on failure.
- Distro differences live only in the `distro` crate behind the `DistroAdapter` trait. `ops` code **MUST NOT** branch on distro names.
- The agent never loads external/paid code. New operations ship compiled into the open agent and stay inert until a C# module drives them.

## Supported systems, and what "supported" obliges

Two families, and every one of these is a system the product must actually work on — not a
best-effort target:

| Family | Versions |
|---|---|
| Debian | Ubuntu 22.04 LTS, Ubuntu 24.04 LTS, Debian 12, Debian 13 |
| RHEL | AlmaLinux 9, AlmaLinux 10, Rocky Linux 9, Rocky Linux 10 |

Architectures: `x86_64` and `aarch64`.

What that obliges, concretely:

- **No platform fact appears as a literal outside the `distro` crate.** Not a binary path, not a
  package name, not a service name, not a config directory. `ops` asks the adapter; it never
  writes `/usr/sbin/nologin`, `apt-get`, `dnf` or `/etc/nginx/sites-available` itself.
- A difference is discovered by **reading both distributions' documentation**, not by testing on
  the one machine at hand. Code that works on Ubuntu because the developer runs Ubuntu is not
  supported on AlmaLinux; it is untested there.
- Adding a family is a new adapter folder plus one arm in `adapter_for` — never a new branch
  anywhere else. If a change needs a second branch, the adapter is missing a method.

The reason this is a rule and not a preference: the failure mode is silent and per-customer. An
account created with a shell path that does not exist on RHEL is created successfully — `useradd`
does not verify the shell — and the customer discovers it when SFTP refuses them, on a server the
developer never ran.

## General

- **One file = exactly one public unit** — one type, trait or function, and its error type is a separate unit in its own file (`NameError` → `name_error.rs`). File name and path always match what's inside (namespace ⇔ folder, type ⇔ file); language specifics in rules/csharp.md, rules/rust.md, rules/vue.md. Multi-type files and dumping grounds (`Utils`, `Helpers`, `misc`) are rejected. The one folder whose NAME is in that family, `Maran.SharedKernel/Utilities/`, is mapped in rules/csharp.md and holds no file of its own — only subject folders of singly-named types, which is what makes it judgeable and not a bag.
- **Every file has one correct place, defined per stack:** backend in rules/csharp.md ("Canonical backend layout"), agent in rules/rust.md ("Canonical agent layout"), frontend in rules/vue.md ("Structure"). Filing a file "wherever it fits", inventing an ad-hoc folder, or leaving a workaround to be tidied later are all review rejects — extend the documented map instead, in the same PR.
- **Doc comments are mandatory for all production code in every language** — every type, member, and function, private included (tests exempt; exact form per language rules).
- **A doc comment that describes what the code does not do is a defect of the same severity as the
  behaviour it misdescribes**, and it is fixed in the same change or the change is not done. It is
  the comment that stops the next reader looking: the wrong behaviour is now covered by a sentence
  saying it is right, so nobody re-derives it. Seven instances in one plan — a field documented at
  length as the boundary of a query it was never read by, so a reconciler closed tasks the running
  process had started; a normaliser promising `fe80::1%eth0` was "refused rather than stripped"
  while returning `fe80::1`; a disk figure whose justification argued for `f_bavail` when the
  question was what a customer can write into; two in one file in consecutive review rounds; and one
  citing a rule **by line number** while the code applied that rule to the wrong file. The mechanism
  is always the same, and the implementer who found it in their own code named it best: *a
  justification written after the choice validates the choice; only one written forward from the
  QUESTION can contradict it.* So write the doc from the question, and when a fix changes the
  mechanism, say which test holds up each half — and, if an earlier version credited the wrong
  mechanism, say that too.
- Files stay small and single-purpose; target < 300 lines, hard review trigger at 400.
- DRY and YAGNI: no speculative abstractions, no "for later" flags. Fleet mode exists only as the transport abstraction already in the design.
- Truth lives in PostgreSQL; anything on disk is derivable and rebuildable from it.
- Additive evolution: proto files and the Provisioning API only gain fields/endpoints inside a major version; renaming or renumbering is forbidden.
- **The database evolves the same way: expand, then contract, a release apart.** The installer
  promises an update is "reversible with an automatic database dump and a rollback command", and
  that promise is only keepable while the PREVIOUS release still runs against the NEW schema —
  rolling the schema back instead means restoring a dump, which discards every message in flight in
  the `wolverine` schema and every row written since. So a migration never drops, renames or narrows
  what the last release reads; the removal ships a release later, after the code that read it is
  gone. A rename is a removal by another name and is refused with the rest. `maran migrate guard`
  reads the `Up` methods of the migrations a branch adds and fails CI, and the only way past it is a
  `// contract-phase:` line in the migration saying why no release reads what it destroys.

## Repository top level

```
proto/agent/v1/        the API↔agent contract; both sides generate code from it
backend/               C# modular monolith (rules/csharp.md)
agent/                 Rust root daemon (rules/rust.md)
frontend/              Vue 3 SPA (rules/vue.md)
installer/             native production install: lib/ (numbered steps), systemd/, nginx/
docker/                DEVELOPMENT ONLY: compose + polygon/ distro images
scripts/               maran (the toolbox entry point), dev (sourced: PATH and keys), lib/
rules/                 these rules — the single source of structure and law
docs/                  design specs and implementation plans
.github/workflows/     CI: backend, agent, frontend, cross-stack
```

## Where a module's UI lives

- A module is one shippable unit on the **backend** (its own project, own PostgreSQL schema, own
  `Manifest.cs` carrying id/version/licence tier). Its **frontend** is not a separate unit: every
  module's interface is written in the core SPA's flat structure and compiled into the single
  bundle (rules/vue.md "Modules in the frontend").
- Licence tiers are enforced **server-side on every request**. The SPA only hides what the licence
  does not include; it is never the boundary.
- The agent never loads module code of any kind — paid operations ship compiled into the open agent
  and stay inert until a licensed C# module drives them.
- Consequence to keep in mind when designing a module: its API must be complete enough that the UI
  needs nothing beyond it, because there is no module-specific frontend runtime to fall back on.

## The backend owns the data, the SPA renders it

- Every domain value the interface shows — module names, plan names and limits, statuses, tiers,
  error messages — is produced and localized by the backend. The SPA holds no domain constants and
  no hardcoded identifiers.
- Consequence for module design: a module's API must expose everything its screens need, including
  the reference data its forms select from. A UI reduced to asking the user to type an identifier
  means the module's contract is incomplete — that is a backend gap to close, never a UI workaround.

## Skeleton policy

- The full canonical folder skeleton exists in the repository from day one (owner's decision): all v1 module anatomies, Host/SharedKernel/Sdk sections, frontend feature folders, agent crate layout. The tree in the IDE always shows where everything will live.
- An empty folder is held in git by a **zero-byte `.gitkeep`** — no content, ever. Delete the `.gitkeep` when the folder gains its first real file.
- A NEW backend module's anatomy is never hand-made: run `maran module <Name>` — it creates the canonical folder set exactly as documented, keeping every module identical in shape.
- The skeleton is not a license to file things loosely: a file goes into the folder the rules assign it, and a new KIND of file first gets its named place in the stack rules, then the code lands.
