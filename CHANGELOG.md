# Changelog

Notable changes, newest first. Dates are the merge date. Until 1.0 the format is prose rather
than a strict category list: what changed and why it matters to somebody running a server.

## Unreleased

### Added

- **Panel authentication.** First-run setup from the installer's one-time token, sign-in with
  Argon2id, JWT access tokens of fifteen minutes, refresh rotation in an httpOnly SameSite=Strict
  cookie where a reused token revokes the whole family, TOTP with recovery codes, and sessions you
  can list and revoke — including "sign out everywhere".
- **Account lockout.** Ten consecutive failures lock an account for fifteen minutes. A locked
  account is refused before its password is checked and with the same error a wrong password
  gets, so the response is not an oracle.
- **Hosting accounts as real Linux users.** Creating an account provisions its system user, home
  directory and the plan's disk quota through the agent; suspend, reactivate and delete reach the
  host too. The agent acts first and the database row follows, so the panel never claims an
  account the server does not have.
- **An append-only audit journal** of every sign-in and every mutation, with an
  administrators-only screen.
- **The `maran` CLI** — one entry point to the whole toolbox (`maran` lists it).

### Fixed

- The login rate limiter partitioned on a `username` **query** value while the endpoint
  authenticates the request **body**, so an attacker could get a fresh partition per request and
  guess passwords without limit. It now keys on the caller's address alone.
- Nothing read `X-Forwarded-For`, so behind the installer's own nginx every request appeared to
  come from `127.0.0.1`: one rate-limit budget for the entire internet, and an audit journal
  whose "from where" was always loopback. The header is now honoured for one hop, and only from
  loopback — it is written by the client, and trusting it from anywhere would hand the limiter a
  key the caller picks.
- Agent operations ran with no timeout at all: the resilience pipeline was registered and
  resolved by nobody. The health probe's timeout was hard-coded, which made
  `Agent:ProbeTimeoutSeconds` configuration nothing read.
- Command validators were registered but never invoked, so a weak password reached the database.
- `.env.example` told developers to copy it to `.env`, and nothing read the result.

## 0.1.0 — foundation

The contract, the Rust agent skeleton with `SO_PEERCRED` on its unix socket, the C# modular
monolith, the Vue shell, the development environment and CI.
