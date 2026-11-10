# Maran Rules

Normative engineering rules for this repository. They bind every contributor — human or AI. Read them before your first line of code; reviews are conducted against them.

| File | Governs |
|---|---|
| [architecture.md](architecture.md) | System boundaries, modular monolith, agent invariants |
| [csharp.md](csharp.md) | C# style, doc comments, Result-based errors, slice anatomy |
| [rust.md](rust.md) | Agent code: lints, errors, validation, unsafe policy |
| [vue.md](vue.md) | Frontend structure, components, i18n, styling |
| [nuxt.md](nuxt.md) | Public website: rendering, content, locales, SEO |
| [proto.md](proto.md) | The API↔agent contract and its evolution |
| [testing.md](testing.md) | Order of work, Definition of Done, golden tests, CI gates |
| [security.md](security.md) | The PR security checklist and escalation rules |
| [git.md](git.md) | Commit gating, identity, branches, PRs |

The complete folder maps — where every kind of file belongs — live inside these rules: backend in csharp.md, agent in rust.md, frontend in vue.md, the public website in nuxt.md, repository top level in architecture.md. Extend the map before inventing a location.

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
- **Banned APIs** (BannedApiAnalyzers): reading the ambient clock and spawning processes are build errors everywhere (`backend/BannedSymbols.txt`); bypassing a tenant query filter is a build error in production code only (`backend/src/BannedSymbols.txt`, which tests deliberately do not inherit — a test bypasses the filter to prove it hides the row). Every sanctioned exception is an inline suppression carrying its reason: `SystemClock`, and 36 deliberate `IgnoreQueryFilters` calls in unattended work that has no principal — `grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l`, which is what this sentence is FOR: a reviewer reads it to decide whether the list of bypasses has grown, so the count carries the command that regenerates it and a stale number can be caught in one line instead of believed. It has been stale twice: at twelve while the tree held 23, and at 23 while the tree held 24 (re-measured 2026-09-12). Raised to 25 on 2026-10-29 for `Maran.Modules/Sites/Services/StartupPhpSiteReconciler.cs` (issue #125), unattended start-up work with no principal. Raised to 26 on 2026-10-30 for `Maran.Modules/Tasks/IntegrationEvents/Handlers/AccountDeletingHandler.cs` (issue #201), which releases a deleted account's task rows whoever asked for the deletion. Raised to 27 on 2026-11-01 for `Maran.Modules/Cron/IntegrationEvents/Handlers/AccountDeletingHandler.cs` (issue #158), which releases a deleted account's runs started by hand. Raised to 30 on 2026-11-03 for `Maran.Modules/Accounts/Services/AccountNamespaceClaims.cs` (issue #179), three calls that read account names, reseller names and primary domains across every owner, because a reseller's own context hides the rows it must not collide with. Raised to 34 on 2026-11-04 for the fail-closed Accounts context (issue #371), four calls in three files that run with no principal or read rows the active context hides: `Maran.Modules/Accounts/Seeders/PlanSeeder.cs` (the start-up check that any plan exists), `Maran.Modules/Accounts/Services/AccountDirectory.cs` (`ListAsync`, two calls, accounts and plans, for the backup run, retention, backup reclamation and the PHP reconcile run, all with an anonymous caller) and `Maran.Modules/Accounts/Services/AccountNameDirectory.cs` (the switcher of a customer with several accounts reads the names of accounts other than the active one, by ids from the caller's own memberships). Raised to 35 on 2026-11-05 for `Maran.Modules/Accounts/Services/AccountDirectory.cs` (issue #379), one more call in `FindSystemUserAsync`, which reads one account's system user name by id for certificate renewal, unattended work whose anonymous caller the fail-closed Accounts context shows nothing. Raised to 36 on 2026-11-09 for `Maran.Modules/Accounts/Services/AccountOwnership.cs` (issue #460), one call that answers the view-as bearer check whether a reseller still owns an account, before the request has a principal. The count was 35 at HEAD before the #460 change and is 36 in the working tree, measured with the command above. Raised by three on 2026-11-09 for the certificate module's DNS-01 door (issue #161): `Maran.Modules/Dns/Services/DnsZoneEditor.cs` (`OpenForAccountAsync`, a zone read scoped by the account the unattended caller names), `Maran.Modules/Dns/Services/DnsChallengeRecords.cs` (`FindZoneAsync`, the same scope) and `Maran.Modules/Dns/Services/DnsChallengeSweeper.cs` (the hourly removal of challenge records no order is waiting on, with no principal). The running figure above had also fallen behind the tree before this change (`git grep -n '\.IgnoreQueryFilters()' HEAD -- backend/src '*.cs' | grep -c '^HEAD:backend/src'` printed 58 at HEAD against the 36 named here, the Dns module's reconciler, name directory and event handlers having been added without an entry), so trust the command and not the number: it printed 62 in the working tree when this entry was written. Raised by one on 2026-11-09 for `Maran.Modules/Backups/Services/MigrationArtifactService.cs` (issue #127), `DiscardExportAsync`, which reads the backup row of an expired account export by id with no principal, because the hourly expiry pass that calls it has none; it removes only a backup of the export kind. That count was not re-measured here: run the command above.
- **Architecture tests** (`backend/tests/Maran.ArchitectureTests`) enforce what only the composed panel knows: module isolation, that every entity carrying an `AccountId` has a query filter, that every module declares the parts of the agent it reaches, that the Host declares no message handler, and that the marketplace's only way to the network is its one gated transport and the module starts no work of its own (`MarketplaceOutboundIsGatedTests`). Each carries a positive control, because every one of them is satisfied by an empty answer and would otherwise pass loudest when it had stopped looking.
- **`maran migrate guard`**: a migration that drops, renames or narrows what the previous release reads fails CI, so the installer's rollback promise stays keepable (rules/architecture.md).
- **`maran structure`** enforces what no compiler can express: one type per file, file name equals type name, `*Extensions` in `Extensions/`, interfaces in `Interfaces/`, no cross-module imports, no junk-drawer file names, no `Controllers/Requests/` folder, one spelling of the caller's address, sentence case in the SPA (no CSS `uppercase`), audit entries built only by a module's journal, no DI-registered type in a module's `Common/`, and member order — a type's shape before its behaviour, in the backend and in the SPA alike. It runs first in the backend CI job.

A rule that a linter, analyzer, `.editorconfig` or a check script can express MUST be configured there rather than left to review. Every claim of enforcement in these rules has been verified by deliberately writing a violation and watching the tool reject it — a rule that says "the compiler enforces this" when it does not is worse than no rule.

Rules change by PR with the owner's approval, never silently.
