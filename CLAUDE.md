# Maran — instructions for AI sessions

1. Read `rules/README.md` first; every rule there is binding. Highlights:
   - Doc comments on ALL production code (private included). One file = one type/unit.
   - NEVER `git commit` or push unless the owner explicitly commands it, and never add
     AI attribution trailers. Identity: edgar2031 <edgar.poghosyan.2031@gmail.com>.
   - No shell-string execution anywhere; agent commands are typed proto RPCs only.
2. Spec: docs/superpowers/specs/2026-08-29-maran-design.md (Russian).
   Plans: docs/superpowers/plans/. Execute plans task-by-task; implementation first, tests in a dedicated pass after (rules/testing.md).
3. Layout: proto/ (contract), backend/ (C# modular monolith), agent/ (Rust root daemon),
   frontend/ (Vue SPA), installer/, docker/ (dev only), rules/, docs/.
4. Verification commands: `maran check`, `dotnet test` (backend/),
   `cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test` (agent/),
   `npm run lint && npm run typecheck && npm run build` (frontend/ — no unit runner by design,
   rules/testing.md: the SPA is verified end-to-end via Playwright in `frontend/e2e/`).
