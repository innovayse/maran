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
- Inside a module, code is organized per operation: `Commands/<Operation>/` holds the command, its handler and its validator together; `Queries/<Operation>/` likewise (rules/csharp.md). No `Managers/`, `Helpers/` or `Misc/` grab-bags.
- `Maran.Host` composes modules and holds no business logic. `SharedKernel` holds primitives only (`Result`, errors, `ICurrentUser`, `IClock`) — if it grows domain concepts, that's a smell.

## Agent

- The command set is closed and typed. **MUST NOT** add any RPC that executes caller-supplied programs, shell strings, or templates.
- Every command is idempotent: repeating it converges to the same state and reports `AlreadyExists`/`NotFound` instead of failing.
- Every input is re-validated inside the agent (names against allow-list regexes, paths canonicalized and required to stay under `/home/<account>/`) even though the API validated already.
- File operations on customer data run under the account's UID (fork + setuid), never as root.
- Config writes follow: render template → temp file → validate (`nginx -t` etc.) → atomic rename → reload → typed error + rollback on failure.
- Distro differences live only in the `distro` crate behind the `DistroAdapter` trait. `ops` code **MUST NOT** branch on distro names.
- The agent never loads external/paid code. New operations ship compiled into the open agent and stay inert until a C# module drives them.

## General

- **One file = exactly one public unit** — one type, trait or function, and its error type is a separate unit in its own file (`NameError` → `name_error.rs`). File name and path always match what's inside (namespace ⇔ folder, type ⇔ file); language specifics in rules/csharp.md, rules/rust.md, rules/vue.md. Multi-type files and dumping grounds (`Utils`, `Helpers`, `misc`) are rejected.
- **Every file has one correct place, defined per stack:** backend in rules/csharp.md ("Canonical backend layout"), agent in rules/rust.md ("Canonical agent layout"), frontend in rules/vue.md ("Structure"). Filing a file "wherever it fits", inventing an ad-hoc folder, or leaving a workaround to be tidied later are all review rejects — extend the documented map instead, in the same PR.
- **Doc comments are mandatory for all production code in every language** — every type, member, and function, private included (tests exempt; exact form per language rules).
- Files stay small and single-purpose; target < 300 lines, hard review trigger at 400.
- DRY and YAGNI: no speculative abstractions, no "for later" flags. Fleet mode exists only as the transport abstraction already in the design.
- Truth lives in PostgreSQL; anything on disk is derivable and rebuildable from it.
- Additive evolution: proto files and the Provisioning API only gain fields/endpoints inside a major version; renaming or renumbering is forbidden.

## Repository top level

```
proto/agent/v1/        the API↔agent contract; both sides generate code from it
backend/               C# modular monolith (rules/csharp.md)
agent/                 Rust root daemon (rules/rust.md)
frontend/              Vue 3 SPA (rules/vue.md)
installer/             native production install: lib/ (numbered steps), systemd/, nginx/
docker/                DEVELOPMENT ONLY: compose + polygon/ distro images
scripts/               developer helpers (dev-env, preflight, proto-lint, new-module, e2e)
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
- A NEW backend module's anatomy is never hand-made: run `scripts/new-module.sh <Name>` — it creates the canonical folder set exactly as documented, keeping every module identical in shape.
- The skeleton is not a license to file things loosely: a file goes into the folder the rules assign it, and a new KIND of file first gets its named place in the stack rules, then the code lands.
