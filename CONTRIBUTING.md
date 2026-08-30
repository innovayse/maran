# Contributing to Maran

Maran is source-available and commercial. You can read every line, run it, change it for
yourself, and send changes back — the licence (BSL 1.1) is about selling it as a service,
not about who may work on it.

The rules below are not style preferences. They are the reason a root daemon written by
several people is safe to run on somebody else's server.

## Before you write code

Read [`rules/README.md`](rules/README.md). Every rule there is binding, and most of them are
enforced by a command rather than by a reviewer's memory. The ones that surprise people most:

- **Doc comments on all production code**, private members included. `CS1591` is an error.
- **One file, one type.** The file name is the type name.
- **No shell strings anywhere.** The agent runs as root; every operation is a typed proto RPC
  against an allow-list of absolute paths. There is no "run this command" call and there will
  not be one.
- **Errors are values.** `Result<T>` and a typed `Error`, not exceptions, for anything a caller
  can act on.
- **The backend owns all user-facing text.** The SPA renders what the panel sends; it never
  invents an error message.

## Getting set up

```bash
git clone https://github.com/innovayse/maran.git
cd maran
source scripts/dev     # sourced, not run: it puts the toolchains and `maran` on your PATH
maran check            # tells you what your machine is missing
maran dev              # database, API and SPA together
```

`source scripts/dev` also creates `.env` from `.env.example` on first use. That file is
git-ignored and is the only place local configuration lives — edit it, not the tracked
`appsettings.*.json`.

Everything else runs through the one CLI. Type `maran` to see the whole toolbox. Do not call
the scripts under `scripts/lib/` directly: they are implementations, `maran` is the surface.

## Branches

```
feature branch  →  dev  →  main
```

- **`main`** is the release branch. It is protected: no direct pushes, ever, including for
  maintainers. It only ever receives a reviewed pull request from `dev`.
- **`dev`** is where work integrates, and is the default branch — a new pull request targets it
  without anyone having to remember to.
- Branch from `dev`, name it for what it does (`feat/backup-restore`, `fix/quota-rounding`),
  and open a pull request back into `dev`.

## Before you open a pull request

Run the gates. All of them are fast except the integration tests, and CI runs the same ones,
so finding out here is cheaper:

```bash
maran check                                   # is this machine able to build at all
maran structure                               # file and folder laws no compiler can express
maran format --check                          # formatting and naming
maran proto                                   # the API-to-agent contract
maran migrate check                           # a model edited without a migration
cd backend  && dotnet test
cd frontend && npm run lint && npm run typecheck && npm run build && npx playwright test
maran agent check                             # fmt, clippy -D warnings, cargo test
maran handshake                               # agent and API over a real unix socket
```

A toolchain error is a failure to verify, never a pass. "No tests found" is a failure too.

## Tests

[`rules/testing.md`](rules/testing.md) is the authority. In short:

- Test names are behaviour sentences: `Revoking_another_users_session_answers_404_rather_than_403`.
  `Test1` and `ItWorks` are review rejects.
- Assert behaviour through the public surface. If a test needs a private member, the design is
  wrong — fix the design.
- Every typed error variant of a feature appears in at least one test.
- **Never change production code to make a test pass.** If a test is failing because the code is
  wrong, fix the code and say so. If it is failing because the test asserted the wrong thing,
  say that too — we have found four tests that were protecting defects, and each one was worth
  more as a report than as a green tick.
- A new gate must be proved able to fail before you trust it. Break the thing on purpose, watch
  it go red, put it back.

## Changes that need more than a review

[`rules/security.md`](rules/security.md) requires a **second reviewer and a threat note in the
pull request description** for changes to:

- authentication, sessions or token handling
- the agent's `privs` module — the only place `unsafe` is allowed
- licence verification
- the installer's privileged steps

The note answers one question: *what could an attacker do with this surface, and why is it safe
now.* There is a worked example at
[`docs/superpowers/notes/`](docs/superpowers/notes/). A note that lists only what is safe is not
a threat note — name what you found and what you left open.

## Commits

- English, in the imperative, explaining **why** rather than restating the diff.
- One coherent change per commit. A 200-file commit called "updates" is a review reject.
- No AI attribution trailers.

## Reporting a security problem

Do not open an issue. See [`SECURITY.md`](SECURITY.md).

## Questions

Open an issue. A question that turned out to be a missing line in this file is a useful issue,
not a waste of anyone's time.
