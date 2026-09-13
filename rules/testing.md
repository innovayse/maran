# Testing Rules

Normative. **Order of work (owner's choice): implementation first, tests after.** Code is written and
made to build clean, then its tests are written in a dedicated pass before the work is considered
done. Test-first is welcome where it genuinely helps (a tricky algorithm, a bug reproduction), but
it is not required and no PR is rejected for lacking a red-then-green history.

What does NOT change: the Definition of Done below still gates completion — code without its tests
is unfinished work, not finished work awaiting tests.

## Definition of Done — every feature

1. Unit tests for the handler/op logic.
2. Integration test of the real surface (HTTP endpoint or agent rpc).
3. For every tenant-scoped endpoint: the IDOR test — customer A requests customer B's resource and gets **404** (not 403).
4. An audit event is written and asserted.
5. i18n keys exist in `en`, `ru`, `hy`.

A PR missing any of these is incomplete, independent of code quality.

## Where tests live

- C#: `backend/tests/<Project>.Tests` mirrors `src/`; integration tests in `<Project>.IntegrationTests` on Testcontainers-PostgreSQL; `Maran.ArchitectureTests` holds NetArchTest module-isolation rules.
- Rust: unit tests live under the crate's `src/tests/` **mirror** of its module tree, never beside or inside the unit they exercise — `src/validation/name.rs` is tested by `src/tests/validation/name_tests.rs`, the same separation the backend gets from `backend/tests/`. The unit declares them at the end of the file:

  ```rust
  // In src/validation/name.rs, after the code:
  #[cfg(test)]
  #[path = "../tests/validation/name_tests.rs"]
  mod tests;
  ```

  This is deliberately not the Rust Book's convention, which puts an inline `#[cfg(test)] mod tests { … }` in the source file. That convention loses to this repository's one-unit-per-file rule: a file holding a type plus two hundred lines of tests is not one unit, and the tests are what it mostly becomes. `#[path]` is a stable, documented module attribute, and for a `mod` declared outside an inline module block it resolves relative to the declaring file's own directory.

  The tests stay a **child module** rather than moving to the crate-level `tests/` directory, because a child module can reach its parent's private items and a crate-level test cannot. The parts most worth testing are private on purpose: `resolve_under` is a separate private function precisely so path containment can be tested against an injectable home root. `#[cfg(test)]` still keeps every line of it out of the shipped binary.

  Cargo's own `agent/crates/<crate>/tests/` directory holds integration tests only — things that exercise the crate exactly as a caller would, like the agent's handshake over a real unix socket. Fixtures live beside them; template golden files in `agent/crates/templates/tests/golden/`.

  `maran structure` rejects an inline `mod tests {` and a `*_tests.rs` outside the `src/tests/` mirror.
- Frontend: **no colocated unit tests** — the SPA is verified end-to-end. Playwright specs live in `frontend/e2e/` with fixtures in `e2e/fixtures/`; the shell's own gates are `lint`, `typecheck` and `build`.

## Naming

Test names are behavior sentences, not method references:

```csharp
[Fact]
public async Task Creating_site_with_taken_domain_returns_conflict() { ... }
```

```rust
#[test]
fn path_with_symlink_escape_is_rejected() { ... }
```

`Test1`, `TestCreateSite`, `ItWorks` are review rejects.

Test code carries doc comments like every other file: `CS1591` is on for the test projects too.
The summary restates the behavior sentence, so a reader of the generated documentation sees the
contract without opening the file. Everything else about style (formatting, one concern per file)
applies to tests too.

## What tests assert

- Behavior through the public surface, not private internals. If a test needs a private member, the design is wrong — fix the design.
- Template goldens: rendered nginx/php-fpm/vsftpd configs are compared byte-for-byte against `tests/golden/*.conf`. A template change without its golden update fails CI; the golden diff IS the review artifact.
- Failure paths are first-class: every typed error variant of a feature appears in at least one test.

## Determinism

- No sleeps; poll with timeout helpers. No `DateTime.Now`/`SystemTime::now()` in logic — inject `IClock`/`Clock`. No test order dependence, no shared mutable fixtures. A flaky test is a P1 bug: fix or delete-and-file, never retry-loop it.

## Verification is something you run, not something you are told

- A claim that the gates pass is not evidence. Whoever integrates work — a reviewer, a lead, an
  automated helper — re-runs the gates themselves against the working tree before accepting it.
  A report saying "all green" alongside a suite that finds no test files is a real failure mode we
  have already hit.
- "No tests found" is a FAILURE, never a pass: every runner in CI must exit non-zero when it
  collects nothing (`vitest --run` and `dotnet test` both do; keep it that way).
- **A verdict is named failures plus the collected total. Never the exit code.** Measured here, all
  three in one day: `dotnet test` exiting **0 with sixteen failures**; exiting **0 while a project
  whose build another agent had broken (IDE0005) never ran at all** — 1508 collected against a 1683
  baseline, and 1138 against 1725 in a second instance; and returning **143** on a run that had
  completed 13 of 15 projects because the kernel killed it under load. A broken test project does
  not fail the run, it *disappears* from it, and that is the most believable false pass there is.
  So a report quotes `N passed / N failed / N skipped` per project and the sum, names every failure,
  and compares the total against a baseline. A total that dropped is a failed run whatever the exit
  code says.
- **A count taken while another build is running is not a measurement.** Concurrent `dotnet test`
  invocations share `bin/Debug`, and a host booted against a half-written assembly produces uniform,
  plausible failures — a clean `403` everywhere — which is exactly the shape that gets believed. One
  such race was written into the ledger as a code regression before it was retracted. Under load,
  timing-sensitive results (e2e first paint, anything with a timeout) are discounted or re-run at one
  worker, and the load is stated.
- Documentation must not describe code that does not exist. A comment referencing a test, a class,
  or a middleware that was never written is a defect in its own right — delete the sentence or
  write the code.

### A check must be able to observe what it reports on

Before asserting, answer one question about the assertion itself: **if the thing I am checking were
broken, what would this line see?** If the answer is "the same as it sees now", the check is
decoration, and it is worse than none — a green gate is read as evidence, so a blind one does not
merely fail to catch a defect, it certifies its absence.

This is the most expensive defect this repository has produced. The installer's nginx gate staged
the panel vhost as `maran.conf.staging` and then ran `nginx -t`, but the distribution's
`nginx.conf` includes `conf.d/*.conf`, which `.staging` matches on neither family — so every
install validated the *previous* tree and reported `test is successful`. What that hid: the shipped
vhost carried `http2 on;`, a directive nginx gained in 1.25.1, while Ubuntu 24.04 ships 1.24.0 and
AlmaLinux 9 ships 1.20.1. **The panel's own vhost had never parsed on a single supported
platform**, for the whole life of the installer, and the check that existed to catch exactly that
was looking at a file nobody serves.

The same shape, four more times in one plan: a polygon assertion that grepped a unit file's **text**
and reported on a **runtime directory** systemd re-applies `User=`/`Group=` to on every command
invocation — the actual bug was invisible to any grep; a Playwright spec asserting
`getByRole('heading', …)).toHaveCount(0)` on a screen whose title renders through `UiEmptyState` as
a `<p>`, so it matched nothing on either side of the behaviour it claimed to distinguish; a
`to_jsonb(t)::text like '%token%'` probe for a secret at rest, run against Wolverine envelope
columns that are `bytea` and therefore render as hex; and a harness grepping a whole log for a test
name, where the PASSING line matched too.

So, concretely:

- **A gate observes the artefact the system actually uses**, on the path the system reads it from,
  under the name it reads. Not a staging copy, not a rendered string in memory, not the template.
  For nginx that means the served file plus `nginx -T` listing it — the server's own answer to "do
  you read this?" — and `cmp` against a fresh render.
- **State a check's blind spot in the check's own output.** An assertion that prints
  `UNOBSERVED HERE: this image boots no systemd` is honest; one that reads like a runtime check and
  is a grep is not.
- **A vacuity guard must be on the axis that can go blind.** `Assert.NotEmpty(tables)` proved the
  `bytea` probe had somewhere to look, which was true and irrelevant — the search could not match.
  A guard on the wrong axis is more dangerous than no guard, because it is precisely what persuades
  the next reader that the test was thought about. Guard the thing that would silently become
  unobservable, and prove it: a **positive control** — plant the value the probe hunts and assert
  the probe FINDS it — is the only guard that ages well.
- **A refusing gate needs an inverse control.** Feed it something it must ACCEPT. A gate mutated to
  refuse everything passes every test that only ever hands it broken input.
- **A SEARCH that found nothing is a check, and it owes the same proof.** "Nothing references this",
  "no workflow invokes it", "this string appears nowhere" — every one of those is an assertion whose
  failure mode is silent and whose output is identical whether the search looked or not. So a
  negative search result that anything is written on top of owes three things, together: **the exact
  command**, pasteable, flags included; **the tool that ran it**, whenever it was not
  `/usr/bin/grep`; and a **positive control** — the same pattern against something it must match, so
  the reader can see the search was capable of a hit. Without the control it is the vacuity problem
  this section already names, just spelled in a shell instead of a test.

  Every one of these has produced a false finding here, and none of them announces itself:

  - **An alternation handed to a basic-regex grep.** `grep -rn "pkill|kill_sessions|loginctl" …`
    searches for one literal 28-character string and returns `0` on every machine. That `0` was
    written into a committed threat note as *"nothing anywhere kills a live session"*, two minutes
    after `ops/src/logins/end_account_sessions.rs` landed to do exactly that. Use `-E`, or GNU BRE's
    `\|`; and prefer `-E` in a document, because the reader who re-runs it may not be using GNU grep.
  - **A `grep` that is not `/usr/bin/grep`.** A shell may define `grep` as a function or alias —
    a repository-aware wrapper that honours `.gitignore` is the common one — and it fails by
    narrowing its own scope with no message. Measured on a workstation here:
    `grep -rn 'owed' .superpowers/sdd` → `0` against `/usr/bin/grep -rn 'owed' .superpowers/sdd` →
    `587`, because that directory carries a `.gitignore` of `*`. Such a wrapper reads ignore files at
    or below the search root only, so the same pattern can answer differently depending on which
    directory the search was rooted at, and a named file is always read. `type grep` says whether you
    have one; a negative result over any ignored or generated path (`bin/`, `obj/`, `target/`,
    `node_modules/`, `dist/`, generated `*.g.cs`, session scratch) must name the tool.
  - **`grep -c` where the question was "where".** A count cannot distinguish a hit inside real work
    from a hit inside the sentence reporting it, and a document that quotes its own search term
    falsifies that search on the next read. Use `grep -n` and read the line numbers.

  A count or an absence in a rule, a threat note, a doc comment or a plan is a map of the tree, so
  the standing rule from rules/architecture.md applies: write the command beside the number, and the
  claim rots harmlessly because the next reader can regenerate it in one paste.

### Mutation harnesses — every defect in one manufactures false confidence

A mutation run is how we check that a protection is actually held up by a test:
break the protection on purpose, confirm a **named** test goes red, restore, move
on. It is the strongest evidence this repository produces about security code, and
it is worth exactly as much as the harness that produced it.

Four harnesses in one plan produced wrong answers, and every one of them failed in
the same direction — reporting a protection as tested when it was not, or a kill as
a miss. None produced a false alarm. That asymmetry is the reason this is a rule
rather than advice: a harness bug does not announce itself, it just makes the table
look finished.

A harness MUST:

- **Restore with a fresh mtime.** `cp -p` and `git checkout` of a single file put the
  ORIGINAL timestamp back, and both cargo and MSBuild key their caches on mtime — so
  the source is restored while the MUTATED binary stays in the build directory and the
  next run measures the previous experiment. `touch` the file after the mutation and
  again after the restore, and verify the restore with `cmp`.
- **Run every test target.** `cargo test` stops running later targets once one fails, so a
  mutant that kills a test in an early crate hides whether the named test in a later one
  died at all. Pass `--no-fail-fast`. Without it a clean kill reports as the wrong test
  dying.

  `dotnet test <solution>` needs no such switch — it already continues across projects, and
  **`--no-fail-fast` is not a VSTest option**: it is rejected as `MSB1001: Unknown switch`,
  which produces a run with no test-result line at all. That is the very failure this
  section forbids, so an earlier version of this rule, which told authors to pass it "(`--`
  for dotnet)", would have caused it. What `dotnet test` requires instead is the check
  below: a crashed run prints `Passed!` with a smaller total, so the per-project totals must
  be summed and compared against the baseline.
- **Score against the WHOLE suite, never a subset.** `--filter` (and `cargo test -p`) is the
  same defect wearing a different hat: a mutant scored against one project's tests is scored
  blind to every other project's, and the answer it produces is "SURVIVED" — the direction
  that manufactures confidence. This has already happened here: a tenant check was reported
  as untested off a filtered run showing 24 passed, while the whole solution had three named
  tests failing on it, and the false result was then written up as a "masking pair" that did
  not exist. Narrow the run to save time only after the verdict is in, and never in the run
  the table quotes.
- **Abort when the output carries no test-result line.** A mutant that does not compile
  has measured nothing. Treating a build failure as "the test went red" is the purest
  form of the defect this section exists for.
- **Confirm the NAMED test died, not merely that something did.** "The suite went red"
  is compatible with the protection being untested and an unrelated test being brittle.
  Grep for the specific test name, and print anything else that went red beside it.
- **Verify the mutation landed.** Refuse a multi-line pattern (it silently matches and
  replaces nothing), refuse an ambiguous pattern unless the occurrence is named, and
  fail if the file did not change.
- **Choose a mutation that COMPILES.** The obvious ones here do not: `if (false)` is
  CS0162 unreachable code, which is an error under warnings-as-errors, and deleting a
  guard orphans the `using` that only it needed, which is IDE0005 — also an error. Both
  produce a run that measured nothing, and one of them produced **SURVIVED at 1138
  collected against a 1725 baseline** with four projects never built. A guard that cannot
  be removed without a compiler error is a stronger arrangement than a test, but say that
  — it is not a kill, and it is not a survivor either.
- **A SURVIVED verdict owes a witness.** A survivor is a claim about the tests only once
  the mutation is shown to have changed BEHAVIOUR: a mutant that is a semantic no-op
  (`filter_map(|f| Some(x))` for `map`) leaves the suite green and the "did the file
  change" guard sees nothing wrong. So a survivor is followed by a measurement — a probe,
  a scoped witness test — that exhibits the difference, and either that becomes the missing
  test or the survivor is retracted. This pays: the witness for a control-character sweep
  that looked like decoration showed a quoted local part carrying a control character both
  parsing and round-tripping, so it smuggled that character past every layer downstream.
  The gap was in the tests, not the code, and only the witness could say so.
- **Not live in a shared scratchpad two agents can write.** One harness in this plan was
  overwritten mid-run by another agent's script of the same name.

And two rules about reading the results, which cost as much as the harness bugs did:

- **A protection with no possible mutant is a claim that needs proving, not asserting.**
  "There is no way to break this" has been wrong every time it has been said here —
  `renameat`'s replace-not-follow was declared unmutatable and dies to a one-identifier
  `renameat2(…, RENAME_NOREPLACE)`. Look harder before writing it down; if it is
  genuinely true, say why in the code, next to the thing.
- **Two checks that mask each other are one check and one piece of decoration.** Mutate
  each independently and say which died alone. When neither dies alone, mutate both
  together: if that survives too, the guarantee is coming from somewhere else entirely
  (a downstream `ENOTDIR`, a permission bit) and the comments claiming otherwise are
  wrong. A defensive call that cannot fail is deleted, not labelled — a label ages into
  staleness while the next reader still reasons about the call as protection.

## Toolchain prerequisites — a gate you cannot run is not a gate that passed

- The Rust gates need more than `rustup`: `cargo test`/`clippy`/`build` link test binaries with the
  system C linker, so without a C toolchain (`sudo apt install -y build-essential`) cargo stops at
  ``linker `cc` not found`` and NO agent test runs. `protoc` is required too — the agent's `build.rs`
  generates the proto contract at compile time.
- `maran check` reports exactly which of these are missing. Run it before claiming any
  agent gate is green: a toolchain error is a failure to verify, never a pass, and reading it as
  "nothing to run" is the same defect as treating "no tests found" as success. It checks eight
  things, which is the whole list any gate here needs: the dotnet SDK (major 9), cargo, rustc,
  node, npm, protoc, docker, and the C linker `cc`.
- **`source scripts/dev` guarantees three of those eight** — the dotnet SDK, cargo/rustc, and
  protoc — and does not guarantee `node`, `npm`, `docker` or `cc`. node and npm are deliberately
  absent from it: they are installed per-version (a version manager locally, `actions/setup-node`
  in CI), so naming them would mean hard-coding one machine's version, which works on that machine
  and breaks invisibly on a runner by putting a non-existent directory ahead of the real one. A
  path that is true in only one place is worse than no path. Nor does every `maran` command source
  that file — eight scripts under `scripts/lib/` do, and the counting gates deliberately do NOT,
  because `scripts/dev` also exports `DOTNET_ROOT="$HOME/.dotnet"`, which on a runner would point
  .NET at a directory `setup-dotnet` never created. Sourcing has to be right in two environments;
  proving has to be right in one. So the gates prove.
- **Which gates prove it, and which still do not.** Every entry below was measured by removing the
  tool from `PATH` and running the command; a refusal means the verdict line says `DID NOT RUN` and
  the status is **2**, this harness's word for a run that measured nothing (`scripts/lib/suite.sh`).

  | command | proves | when the tool is absent |
  |---|---|---|
  | `maran test` (rust/backend/spa) | `suite_require_toolchain` | refuses — `TEST VERDICT: DID NOT RUN`, 2 |
  | `maran mutate` | `suite_require_toolchain` | refuses, 2 |
  | `maran format` | per requested stack, before any stack is read | refuses — `FORMAT VERDICT: DID NOT RUN`, 2 |
  | `maran agent` | cargo + protoc natively, docker on the container path | refuses — `AGENT VERDICT: DID NOT RUN`, 2 |
  | `maran proto` | protoc | refuses — `PROTO VERDICT: DID NOT RUN`, 2 |
  | `maran licenses` | cargo, dotnet, npm, jq | refuses — `LICENSES VERDICT: DID NOT RUN`, 2 |
  | `maran api` | node | refuses with a one-line message and 2, but prints no verdict line |
  | `maran structure`, `maran lock selftest` | nothing to prove — pure shell, no toolchain | n/a |
  | `maran migrate check`, `maran migrate status` | `suite_require_toolchain backend`, above `dotnet tool restore` | refuses — `MIGRATIONS VERDICT: DID NOT RUN`, 2 |
  | `maran drift` | docker on PATH, the container present AND running, and the database answering `select 1` | refuses — `DRIFT VERDICT: DID NOT RUN`, 2 |
  | `maran handshake` | `suite_require_toolchain rust` + protoc + `suite_require_toolchain backend` + curl + a free port 5081, all above the build | refuses — `HANDSHAKE VERDICT: DID NOT RUN`, 2 |

  Those last two rows were the remaining gap in this table and are now closed; what they said before
  is kept here, because the shape is the lesson. `maran drift` with no `docker` printed
  `DRIFT-UNKNOWN — the maran-postgres container is not running, so nothing was checked.` on
  **stderr** and exited **1**, so a caller keeping stdout got ZERO BYTES plus the status of a real
  schema drift — and the sentence was untrue about its own cause, since the container was running and
  `docker` was what was missing. `maran handshake` with no `cargo` printed `building the agent` on
  stdout, `HANDSHAKE-FAILED: the agent did not build` on **stderr**, and exited **1**: the word
  FAILED about source it never read, on a gate `cross.yml` scored by status.

  Neither named defect was the worst member of its file, which is now three for three on this branch.
  `maran drift` died under `set -e` on the bare assignment
  `probe="$(psql_query 'select 1')"` — a command substitution that fails ends the script — so
  against an unreachable database it printed **zero bytes on stdout AND zero bytes on stderr** (psql's
  own message goes into the variable) and returned psql's status, and the `DRIFT-UNKNOWN — could not
  query` guard written for exactly that case was DEAD CODE that had never once executed. The two
  history queries below it carried the same hazard with a worse consequence: an empty answer there is
  indistinguishable from "this database holds no migration at all", which this command would have
  reported as every module BEHIND — a confident finding manufactured by a query that did not run.
  Each is now asked for its status, and a failure is a refusal rather than an empty set.
  `maran handshake` hid the same class behind two more sentences: a missing `protoc` arrived as "the
  agent did not build" (the agent's `build.rs` generates the contract at compile time, so the one
  confusion this command must never produce is a toolchain gap wearing the contract's clothes), and a
  missing .NET SDK, an absent `curl`, a port 5081 a peer was already listening on, or a database
  container that never became healthy all arrived — ninety seconds later — as "the api never
  answered".

  **Which outcomes are findings and which are facts about the machine** is the judgement each of
  these two needed, and it differs between them.
  `maran drift` asks an OPS question — is this deployment behind the migrations on disk — so on a
  workstation with nothing running, "I could not check" is the ORDINARY answer and must not be red: a
  gate whose everyday answer is red is a gate people learn to ignore. `DID NOT RUN` is exactly that
  answer and is neither a pass nor a finding by construction. Facts about the machine, all refusals:
  docker absent, container absent, container not running, database unreachable, a history query that
  failed, and `--apply` unable to start the SDK. Findings: `SCHEMA-BEHIND` (measured against a real
  history table, and `--apply` is the fix), and a tree in which no module offered a migration file —
  that last one is FAILED and not DID NOT RUN, because the database answered and the TREE was what
  had nothing to check. It is also this command's VACUITY GUARD, on the axis that can actually go
  blind: the whole input is a glob over `backend/src/Maran.Modules/*/Persistence/Migrations`, and a
  layout that moves would otherwise produce `SCHEMA-CURRENT — 0 module(s) compared`, a clean gate over
  nothing at all.
  `maran handshake` is the opposite case: it is a WIRED MERGE GATE and the only check that observes
  the contract on the wire, so its FAILED must mean the branch — the agent's source, the API's, or the
  proto between them. Everything the machine owes it refuses instead.

  `maran migrate check` was the shape those two still have, and closing it took three changes rather
  than one, because the named defect was not the worst member. With `dotnet` absent from PATH
  entirely it did not reach its own check loop at all: it died inside
  `(cd backend && dotnet tool restore)`, printing **zero bytes on stdout** and returning **127** —
  the same shape as `maran agent check` and `maran proto` below. So the guard is placed after
  `scripts/dev` has supplied the pinned SDK and before the first command that uses it, the tool
  restore is itself a refusal rather than a `set -e` death, and `COULD NOT CHECK` now counts into a
  separate counter from `MODEL CHANGED`. Where both occur in one run the FINDING wins — a module that
  reported drift was measured, and what it found is real — and the unmeasured modules are named
  alongside it as INCOMPLETE rather than folded into it. `backend.yml` scores the printed line, not
  the status, exactly as it does for `maran format --check`.

  Neither belongs in `maran lock selftest`, and the reason is that file's own rule. A case asserting
  "with no SDK, `migrate check` refuses" needs no toolchain and would fit; its mandatory INVERSE
  CONTROL — the same command, a working SDK, the refusal ABSENT — needs a real .NET SDK and a
  buildable tree. Adding the refusal without the control would make it precisely the guard that
  refuses everything; adding both would cost the selftest the first-step position in `backend.yml`
  that rests on it needing no toolchain. So it stays out, and this paragraph is where the shape is
  written down instead.
- The three shapes this section was written against were all measured here on one day, and all
  three were the *reporting* half rather than the missing tool: `maran agent check` printed
  `AGENT-CHECK FAILED — stages that failed: fmt clippy test doc`, naming four stages as failing
  about source not one of them had read; the same command on a PATH without `rustc` died inside
  `rust_version="$(rustc --version | awk …)"` — `pipefail` made 127 the pipeline's status and
  `set -e` ended the script there, with **zero bytes of output and a status of 127**; and
  `maran proto` produced 62 bytes (`protoc: command not found`) and also exited 127. 127 is none of
  this harness's three answers, and a caller keeping only stdout was handed an empty file. A
  toolchain guard is therefore not only a message: it must run **before** the first stage, and
  nothing above it may be allowed to kill the script first.

## CI gates (all required for merge)

Each row names the workflow that runs it, so this list can be checked against
`.github/workflows/` rather than believed. A gate not named in a workflow is not a gate.

**Per stack**

- C#: build warnings-as-errors, unit + integration + architecture tests, and
  `maran format --check backend` — the .editorconfig style, gated on the printed
  `FORMAT VERDICT: OK` line rather than on a status (`backend.yml`). That gate ran here
  unnamed by this list for as long as it has existed, which is the drift this section's
  own first sentence exists to prevent.
- Rust: fmt --check, clippy -D warnings, tests, doc build clean (`agent.yml`).
  **Not** cargo-deny or cargo-audit: this row claimed both for months and no workflow has
  ever invoked either — measured against every step of every file in `.github/workflows/`.
  A gate not named in a workflow is not a gate, and a gate named here and nowhere else is
  worse than none, because it stops the next reader from looking. Wanting them back means
  adding the step, not the sentence.
- Frontend: oxlint + eslint, vue-tsc, build, and `maran test spa` — the Playwright suite scored
  against its per-spec-file baseline and gated on the printed verdict line (`frontend.yml`) — no
  unit runner, by design (see above).
- Cross: proto lint, and the handshake E2E (built agent ↔ built API over a real unix socket),
  gated on the printed `HANDSHAKE VERDICT: OK` line rather than on a status (`cross.yml`) — the same
  shape the format and migration steps in `backend.yml` settled, and for the same measured reason: the
  step used to read `$?` alone, so a runner with no `cargo` recorded `HANDSHAKE-FAILED: the agent did
  not build` as a contract that does not hold on the wire. `HANDSHAKE VERDICT: DID NOT RUN` now fails
  the step under its own message, saying that neither process was started and no rpc crossed the
  socket.
  **`maran drift` is deliberately NOT wired, and this is the argument.** It needs a database holding a
  DEPLOYMENT's migration history, and a CI service container is created empty for the job — so in a
  workflow it would report every migration of every module missing on every run, a gate that is
  structurally always red, which is the exact failure mode the ruling above warns about. The branch
  question it resembles is already answered by two wired gates that need no live deployment:
  `maran migrate check` (the model against the migration files) and `maran migrate guard` (nothing
  destructive). `drift` stays an ops-and-workstation tool, like `maran mutate` and `maran dev`.
- Two gates that are not about a stack at all, and are listed so this table matches the
  directory: CodeQL analysis of the backend (`codeql.yml`), and the branch policy that
  refuses any pull request into `main` whose head is not this repository's `dev`
  (`branch-policy.yml`, on `pull_request_target` so it cannot be edited by the branch it
  judges).

**Structure, contract and schema — the checks no compiler can express**

- `maran structure` — one type per file, file name equals type name, the folder maps, member order
  (`backend.yml`, first in the job).
- `maran proto` — contract lint plus the additive law; `--accept` records a deliberate change
  (`cross.yml`).
- `maran api` — **the request contract.** It reads every SPA request body and the command each
  endpoint binds it to, and fails when they have drifted apart; `--accept` records a deliberate
  change against `scripts/api-contract-baseline.txt` (`api-contract.yml`). It exists because
  endpoints now bind their command object directly — there is no request type left in between to
  hold the two shapes together — so nothing but this gate observes the seam. It prints its own blind
  spot (`UNOBSERVED HERE — query-string and route binding, and every response body`), which is the
  form every gate here owes.
- `maran migrate check` and `maran migrate guard` — the expand-then-contract law: a migration that
  drops, renames or narrows what the previous release reads fails the build (`backend.yml`, with
  full history checked out so the branch can be diffed against its merge base). The guard reads
  **untracked** migration files too, because a migration that has not been committed yet is exactly
  the one about to land, and it scans `migrationBuilder.Sql(...)` for destructive verbs as well as
  the typed `Drop*`/`Rename*`/`AlterColumn` calls. It prints what it cannot see.
- `maran licenses --check` — third-party notices cover every declared dependency (`notices.yml`).

**Counting, and the guards against a run that measured nothing**

- `maran test` — **the verdict on the backend and the agent suites** (`backend.yml`, `agent.yml`).
  It runs the whole solution and the whole workspace — neither command can express a subset, because
  `--filter` and `-p` are refused by the command itself — parses one row per test dll and per cargo
  target, names every failing test, reconciles those names against the totals, and compares both
  against `scripts/test-baseline.txt`. Both lanes pass on the harness's printed `TEST VERDICT: OK`
  line and on nothing else: a run that exits 0 without printing it fails, and a run that prints it
  and then exits non-zero fails too, because the two disagree and neither is then evidence. It
  self-tests its own log parsers against planted fixtures before it measures anything. `--accept`
  RECORDS the baseline and is never passed in CI — a check that refreshes its own baseline enforces
  nothing.
- The **SDK pin** beside it (`backend.yml`). `setup-dotnet` names the exact patch, not a `9.0.x`
  band, and the next step asserts `dotnet --version` before anything is built. A band is resolved by
  the action at run time, so a release would be built by whichever SDK was current that morning and
  nothing would record which; the committed baseline is a count taken on one compiler, and a total
  that moved for that reason would be read as a change on the branch. The assertion is there because
  the failure it catches is silent: a bare-PATH `dotnet` that is an 8.0 snap, asked to build net9.0,
  prints `NETSDK1045`, builds nothing, and the run around it reports success.
- `maran polygon verify` — **scores an already-executed polygon run from its logs, and runs
  nothing itself** (`agent.yml`). Per suite it requires a `Running tests/<suite>.rs` line, a
  `test result:` line, and `passed + failed` equal to the count `scripts/test-baseline.txt` declares
  for that suite; over the run it requires at least one test executed, and it reconciles every named
  failure against the totals. Its suite list is **discovered** from `agent/crates/*/tests`, never
  written down, so a new `*_on_a_real_host.rs` that no `--test` list names fails this job by name.
- **The image currency axis, asked before any count is believed** (`polygon_currency_from_log` in
  `scripts/lib/polygon.sh`, wired into `maran polygon verify`). Every other axis asks whether the
  container said enough; this one asks whether it was the right container. Measured 2026-09-11: a
  shared `maran-polygon-alma9:latest` built on the 9th against a Dockerfile step added on the 10th,
  with no build in between, put **twelve tests red over correct code** while all five axes below
  held — every suite started, every suite finished, every count reconciled. It was a perfect
  measurement of a tree that no longer existed. The lesson is the one this section is about: **an
  assertion inside a build is blind to the absence of the build** — the Dockerfile's own mode
  assertion was present and correct, and nothing ran it. So the check is not in the image. Every
  polygon build records the sha256 of `docker/polygon/**` + `installer/**` in the image
  (`maran.polygon.fingerprint`); `maran polygon stamp` reads it back before a container starts and
  writes the answer into the run's log; `maran polygon verify` re-checks that line against the tree
  and answers `POLYGON VERDICT: ABORTED` — not `DID NOT RUN`, because the tree was read and the
  logs were parsed, and the answer is about the artefacts scored rather than about the lane. A log
  carrying no stamp is refused the same way: "produced before this check existed" and "produced
  against an image nobody checked" are indistinguishable, and a check must not guess. In CI the
  image is built in the same job from the same checkout, so this can only fire on a workstation,
  which is where a floating `:latest` is the default failure rather than the impossible one.
  The other consumer of that floating pair is `maran dev` (`scripts/lib/dev-stages.sh`), and it
  asked only whether the image EXISTED — printing its build date and returning 0 for the very
  images `polygon_stamp` refuses as carrying no label at all. It now asks the same content question
  through the same two functions, and answers it in three ways: CURRENT, NOT CURRENT (naming which
  of "no label" and "other sources", with both hashes), and REFUSED for an absent image. NOT CURRENT
  does not stop the bring-up, and that is a ruling rather than an omission: a dev stack scores
  nothing and gates nothing, the two things that must not be stale — the agent binary and its
  contract — are verified by sha256 against the running process in the same stage, refusing would
  block every `maran dev` on a host whose hand-built images predate the label, and rebuilding is the
  silent 1.9GB build into a log file that the same file already refuses as indistinguishable from a
  hang. What it may never do is look current, so the stage's LAST line is the staleness rather than
  the `binary ... sha256` line that covers the agent alone.
- `polygon_verify_ran`'s **fifth axis** (`scripts/lib/polygon.sh`). The first four axes are all axes
  of PRESENCE — did the suite appear, did it print a result line, did the container exit, did
  anything run. All four are satisfied by a suite that ran a fraction of itself. The fifth axis is
  the DECLARED TOTAL: the count the committed baseline states for that suite, which is the same
  thing `suite_compare` enforces in the other lanes and which the polygon lane alone used to lack.
- `suite_compare` (`scripts/lib/suite.sh`), used by `maran test`, `maran mutate` and
  `maran polygon verify` — compares parsed per-target rows against `scripts/test-baseline.txt`,
  names every target that VANISHED from a run, and names a target that is new since the baseline
  rather than silently accepting it. This is the mechanism behind "a total that dropped is a failed
  run whatever the exit code says".
- `maran lock selftest` — **the refusal contract itself** (`backend.yml`, the first step of the
  job, before `setup-dotnet`, because it needs no toolchain and no container and everything below
  it inherits its answer). The tree lock is what lets `maran format`, `maran test`, `maran mutate`,
  `maran agent` and `maran polygon verify` answer "I did not run" instead of inventing a verdict
  about somebody else's mutant; those three answers are only worth something while every refusing
  command still prints `VERDICT: DID NOT RUN` on **stdout** and returns **2**, and this is the only
  thing that asserts it. Seven cases, each pairing a refusal with an INVERSE CONTROL — the same
  command, the same arguments, the lock free, asserting the refusal is ABSENT — because a gate
  mutated to refuse everything passes every test that only ever hands it broken input. Case 3 is
  the 855-second wedge in a test: hold the lock, SIGKILL the holder, require the lock free with
  nothing cleared by hand. It prints its own blind spots and they are real: it drives
  `scripts/lib/polygon-ci.sh verify` as the representative refusing command, so it proves the LOCK
  and the three-answer contract, **not** that `format.sh` or `test-verdict.sh` individually still
  honour them; its inverse controls prove a command got past the lock, not that the expensive half
  then worked; and it kills a plain shell, not a build daemon, so a descriptor leaking specifically
  into MSBuild is the same defect seen through a different child. Until it was wired it was named
  in one `agent.yml` comment and invoked by nothing — a check that runs nowhere protects nothing.
- `maran mutate --polygon` — scores a mutation in three lanes instead of one: the local workspace
  and every `#[ignore]`d suite inside each polygon family. It exists because the two lanes disagree
  in both directions, and each disagreement is information: killed locally but surviving on the
  polygon means the local test asserts a rendered string rather than the system's answer; surviving
  locally but killed on the polygon means the protection has **no local test at all**. In
  `--polygon` mode no repository file is written — the mutation is applied to an rsync snapshot.

**PR and nightly matrices**

- PR smoke matrix: Ubuntu 24.04 + AlmaLinux 9 polygons. Nightly: full six-OS matrix +
  fresh-container installer run + Playwright golden path.

### Where this list is enforced, and the one place it is not

This section used to say that `maran test` was not wired into CI at all — that `backend.yml:67` ran
bare `dotnet test` and `agent.yml:59` ran bare `cargo test`, so on those two lanes the committed
baseline was compared against nothing. **That is no longer true, and it is written down here because
a rule describing a gate the repository has since closed stops the next reader from looking.**

What those two lanes do now: `backend.yml` runs `bash scripts/maran test backend` and `agent.yml`
runs `bash scripts/maran test rust`, each gated on the harness's printed `TEST VERDICT: OK` line
rather than on a status — and each failing when the line and the status disagree, which is the shape
every measured false pass in this file had. `backend.yml` also pins `setup-dotnet` to the exact
patch the baseline was recorded on and refuses any other, because the baseline is a count taken on
one compiler. Both are listed above with the rest of the counting gates.

**The SPA is closed too, and it was the last one.** The floor exists:
`maran test spa` runs the Playwright suite and scores it through the same `suite_compare` as the
other two stacks, keyed **one row per spec file** in `scripts/test-baseline.txt`. Per file, not per
total, because the failure this exists to catch is a spec file that quietly stops being collected —
a rename, a bad import, a `testMatch` that no longer matches — and a run whose collected total held
steady because another file grew by as much would report nothing. A vanished file is named. The
count is 71 spec files, 362 passed and 1 skipped against 363 collected, and it is a measurement, not
a number quoted from memory between readers, which is what it had been. It has now gone stale three
times, which is the point of writing down **how** it was re-derived rather than only what it is: it
read `64 spec files, 318 passed` in the morning of 2026-09-10, was corrected to `70 spec files, 354
passed` at midday against a run printing `70 targets: 354 passed / 0 failed / 1 ignored`, the suite
grew again the same afternoon (`71` rows summing to `357`, confirmed by a run printing
`71 targets: 357 passed / 0 failed / 1 ignored`), and it grew a fourth time on 2026-09-12. So do not
read the digits — re-derive them, with the one command that cannot disagree with the gate, because it
reads the gate's own input:

```
awk -F'\t' '$1=="spa"{n++; p+=$3; f+=$4; i+=$5} END{print n, p, f, i}' scripts/test-baseline.txt
```

which prints `71 362 0 1` as this sentence is written. The lane that raised the row recorded a
`maran test spa` run printing `71 targets: 362 passed / 0 failed / 1 ignored` beside its edit, so the
baseline and a run agree; the `awk` is the half a reader can re-run in a second, and is what this
paragraph is pointing at. A count in this file is a map of the gate, so a
stale one is the defect rules/architecture.md names: it stops the next reader re-deriving it. Four
staleness events in three days is also the evidence for a narrower claim — the SPA row total is the
fastest-moving number in these rules, so a reader who needs it exact runs the `awk` and a reader who
needs only the shape reads "seventy-odd spec files, one skipped".

The wiring landed with it: `.github/workflows/frontend.yml` runs `maran test spa` and gates on the
printed verdict line, the same shape as the other two lanes, and its path filters gained `scripts/**`
so an edit to the harness or the baseline actually runs the job it governs. That lane sets no
`E2E_PORT`: `suite_run` asks the kernel for a free port and exports it, which removes at the source
the failure where a run exits 0 having executed nothing because something else held 5173 — a real
measured failure here, not a hypothetical. Playwright's browsers are installed uncached on purpose:
a cache keyed on anything looser than the exact version can serve a chromium that will not launch,
and the check below cannot see that.

Two smaller blind spots, stated so they are not mistaken for coverage. `maran mutate` cannot score
the SPA: it chooses its stack from the mutated file's path, `agent/` or `backend/`, and refuses
anything else — so no frontend protection has ever been mutation-scored. And the SPA lane's browser
check is a directory probe, not a launch; a chromium that is present and broken is caught only
downstream, by the run collecting nothing and the harness reporting ABORTED rather than a pass.
