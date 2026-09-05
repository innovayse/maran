# Maran Rules

Normative engineering rules for this repository. They bind every contributor — human or AI. Read them before your first line of code; reviews are conducted against them.

| File | Governs |
|---|---|
| [architecture.md](architecture.md) | System boundaries, modular monolith, agent invariants |
| [csharp.md](csharp.md) | C# style, doc comments, Result-based errors, slice anatomy |
| [rust.md](rust.md) | Agent code: lints, errors, validation, unsafe policy |
| [vue.md](vue.md) | Frontend structure, components, i18n, styling |
| [proto.md](proto.md) | The API↔agent contract and its evolution |
| [testing.md](testing.md) | Order of work, Definition of Done, golden tests, CI gates |
| [security.md](security.md) | The PR security checklist and escalation rules |
| [git.md](git.md) | Commit gating, identity, branches, PRs |

The complete folder maps — where every kind of file belongs — live inside these rules: backend in csharp.md, agent in rust.md, frontend in vue.md, repository top level in architecture.md. Extend the map before inventing a location.

## What this repository never names

Rules, code comments, doc comments, commit messages and README describe our conventions **on their
own merits**. They never name other products as justification — not competitors (control panels we
compete with), not other Innovayse projects used as references, not the open-source repositories a
convention was learned from. "This is the X convention" is a review reject; state the rule and why
it is right here.

Reason: a rule that leans on an external name ages badly (the reference changes, the reader has no
access to it), and shipped source that name-drops other products is a liability in a commercial,
source-available product.

## Mechanical enforcement

Rules a machine can check are checked by a machine, not by a reviewer's memory:

- **`.editorconfig` at the repository root** governs formatting for every stack (charset, LF endings, final newline, trailing whitespace, indent width per file type, 120-column limit). Every editor and IDE honours it automatically; `dotnet format`, rustfmt and Prettier read it too. `backend/.editorconfig` adds only C#-specific style, analyzer severities and naming rules, and inherits the root file.
- ESLint (frontend), clippy (agent), Roslyn analyzers with warnings-as-errors (backend) and NetArchTest (module boundaries) enforce the rest — all as merge gates in CI.
- **Banned APIs** (BannedApiAnalyzers): reading the ambient clock and spawning processes are build errors everywhere (`backend/BannedSymbols.txt`); bypassing a tenant query filter is a build error in production code only (`backend/src/BannedSymbols.txt`, which tests deliberately do not inherit — a test bypasses the filter to prove it hides the row). Every sanctioned exception is an inline suppression carrying its reason: `SystemClock`, and twelve deliberate `IgnoreQueryFilters` calls in unattended work that has no principal.
- **Architecture tests** (`backend/tests/Maran.ArchitectureTests`) enforce what only the composed panel knows: module isolation, that every entity carrying an `AccountId` has a query filter, that every module declares the parts of the agent it reaches, and that the Host declares no message handler. Each carries a positive control, because every one of them is satisfied by an empty answer and would otherwise pass loudest when it had stopped looking.
- **`maran migrate guard`**: a migration that drops, renames or narrows what the previous release reads fails CI, so the installer's rollback promise stays keepable (rules/architecture.md).
- **`maran structure`** enforces what no compiler can express: one type per file, file name equals type name, `*Extensions` in `Extensions/`, interfaces in `Interfaces/`, no cross-module imports, no junk-drawer file names, one spelling of the caller's address, audit entries built only by a module's journal, no DI-registered type in a module's `Common/`, and member order — a type's shape before its behaviour, in the backend and in the SPA alike. It runs first in the backend CI job.

A rule that a linter, analyzer, `.editorconfig` or a check script can express MUST be configured there rather than left to review. Every claim of enforcement in these rules has been verified by deliberately writing a violation and watching the tool reject it — a rule that says "the compiler enforces this" when it does not is worse than no rule.

Rules change by PR with the owner's approval, never silently.
