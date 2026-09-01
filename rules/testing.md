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
- Documentation must not describe code that does not exist. A comment referencing a test, a class,
  or a middleware that was never written is a defect in its own right — delete the sentence or
  write the code.

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
  "nothing to run" is the same defect as treating "no tests found" as success.

## CI gates (all required for merge)

- C#: build warnings-as-errors, unit + integration + architecture tests.
- Rust: fmt --check, clippy -D warnings, tests, cargo-deny/audit clean, doc build clean.
- Frontend: oxlint + eslint, vue-tsc, build (unit runner intentionally absent — see above).
- Cross: proto lint, handshake E2E (built agent ↔ built API over unix socket).
- PR smoke matrix: Ubuntu 24.04 + AlmaLinux 9 polygons. Nightly: full six-OS matrix + fresh-container installer run + Playwright golden path.
