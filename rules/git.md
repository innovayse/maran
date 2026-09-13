# Git Rules

Normative for everyone (humans and AI agents) working in this repository.

## Commits

- **Owner-gated. Nothing is committed without the project owner's explicit go-ahead.** Finish the work, report it, wait for the word. This applies to every contributor session and especially to AI agents.
- Author identity for owner-side commits: `edgar2031 <edgar.poghosyan.2031@gmail.com>`.
- Commit messages: Conventional Commits — `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `ci:`; imperative mood; subject ≤ 72 chars; body explains *why* when the diff doesn't.
- Commit messages carry **no AI attribution trailers** (no `Co-Authored-By: Claude …`) and no tool advertisements.
- One logical change per commit; a commit builds and passes tests on its own.

```
feat: add site suspension state to nginx template

Suspended sites must keep their vhost (SEO, SSL renewal) but serve the
suspension page. Toggling is a template variable, not a config rewrite.
```

## Working alongside others in this tree

- More than one person or session may have this working tree open. Before concluding that a file
  "vanished" or that someone broke something, check the facts (`ls`, timestamps) and ask — do not
  accuse, and do not silently recreate what someone deliberately removed.
- **Never `git checkout --` a file in this tree.** With hundreds of uncommitted files and several
  sessions open, it is a destructive command aimed at work that exists nowhere else: one use of
  `git checkout -- proto/agent/v1/firewall.proto` to undo a test mutation reverted another session's
  uncommitted edits. To undo your own edit, restore from a copy **you** took and verify it with
  `cmp` — the same discipline mutation harnesses already owe (rules/testing.md). Better still, run
  experiments against a copy of the tree, or a read-only mount: nothing to restore is stronger than
  a restore that worked.
- **A red test in someone else's area is not evidence of a regression.** An in-flight mutant looks
  exactly like one — named tests red, a comment contradicting its code — because that is what a
  mutation IS. The two are told apart by the coordination fact, not by the diff: the file's mtime
  against that session's activity, and whether they are running mutations. Ask before you fix. A
  correct-looking fix applied to someone else's experiment destroys the measurement and reports
  itself as a repair; it has happened here twice, both times to the person who had told everyone
  else not to do it.
- Deleting or moving a file that holds real code is never a side effect of a structural change:
  say what will be removed, get the go-ahead, then do it. Without commits there is no history to
  recover from.

## Branches

- `main` is protected: PRs only, CI green required, no force-push.
- Branch names: `feature/<module>-<slug>`, `fix/<module>-<slug>`, `docs/<slug>`, `ci/<slug>` — e.g. `feature/sites-php-version-switch`.
- Rebase on main before merge; merges to main are squash or fast-forward — no merge-commit noise.

## Pull requests

- PR description: what + why, spec/plan reference, the security checklist result (rules/security.md), and test evidence (what ran, what it showed).
- Small PRs — one task from the plan per PR is the ideal size. A PR touching several modules at once needs a stated reason.
- Sensitive surfaces (see rules/security.md) need a second reviewer.

## Hygiene

- Never commit: secrets, `.env`, generated code (proto codegen, `dist/`, `bin/`, `obj/`, `target/`, `node_modules/`), golden-test *outputs* (goldens themselves ARE committed), editor droppings. `.gitignore` is maintained per top-level component.
- History is never rewritten on `main`. Force-push is allowed only on your own feature branches.
