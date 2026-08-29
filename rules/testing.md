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
- Rust: unit tests inline `#[cfg(test)]`; integration in `agent/tests/`; template golden files in `agent/crates/templates/tests/golden/`.
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

Test code is exempt from the mandatory doc-comment rule — the behavior-sentence name *is* the documentation. Everything else about style (formatting, one concern per file) applies to tests too.

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

## CI gates (all required for merge)

- C#: build warnings-as-errors, unit + integration + architecture tests.
- Rust: fmt --check, clippy -D warnings, tests, cargo-deny/audit clean, doc build clean.
- Frontend: oxlint + eslint, vue-tsc, build (unit runner intentionally absent — see above).
- Cross: proto lint, handshake E2E (built agent ↔ built API over unix socket).
- PR smoke matrix: Ubuntu 24.04 + AlmaLinux 9 polygons. Nightly: full six-OS matrix + fresh-container installer run + Playwright golden path.
