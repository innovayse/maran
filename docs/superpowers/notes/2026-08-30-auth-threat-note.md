# Threat note — Plan 2, Auth and Accounts

`rules/security.md` requires a threat note for any change to authentication, session and token
handling, the agent's privileges, licence verification, or the installer's privileged steps. This
change is the first three of those. It also requires a second reviewer; this note is what that
reviewer reads first, and it belongs in the pull request description.

The question each section answers is the one the rule asks: **what could an attacker do with this
surface, and why is it safe now.**

## What the change adds

Panel authentication, end to end: first-administrator setup, sign-in, JWT access tokens, refresh
rotation in a cookie, TOTP with recovery codes, session listing and revocation, an append-only
audit journal with an administrators-only screen, and the account lifecycle (create, suspend,
reactivate, delete) reaching the agent over typed RPCs.

Before it, the panel had no authentication at all and `UnauthenticatedCurrentUser` made every
authorization check fail closed. Every surface below is therefore new attack surface.

## Sign-in

**Steal a password from the database.** Passwords are stored only as Argon2id hashes in PHC
format, with the parameters inside the hash so raising them later does not invalidate what is
stored (`Argon2idPasswordHasher`, `NeedsRehash`). Nothing else in the codebase hashes a password.
Proven by `Argon2idPasswordHasherTests`, and by an integration test asserting the stored value
starts `$argon2id$` and does not contain the plaintext.

**Guess a password.** `POST /api/v1/auth/login` is rate limited per client address, 5 attempts per
300 seconds in production `appsettings.json` (deliberately wide in development so e2e runs are not
throttled). Every attempt from one address shares one budget whatever account it names, so working
through a list of usernames is bounded exactly as hammering one is.

This is a **fix made during the review that produced this note**, and the reviewer should look at
it. The key used to be (address, username) with the username read from the `username` QUERY string,
while the endpoint authenticates the username in the request BODY. An attacker controls both: a
body naming the real account plus a random query value on every request put each attempt in a fresh
partition — unlimited guesses against one account from one address, with the limiter reporting
itself as working. A regression test now asserts that changing the request cannot buy a fresh
budget; the test that previously stood there asserted the opposite and passed.

Callers behind one shared address share a budget; the production numbers leave room for mistyping.

The address the panel reads is the caller's, not nginx's. That is worth stating because it was not
true until this change: nothing read `X-Forwarded-For`, so behind the installer's own reverse proxy
every request arrived from `127.0.0.1` — one budget for the entire internet, which is no protection
against an attacker and a denial of service against everyone else, five wrong passwords locking out
the whole panel. The header is honoured for one hop and **only from loopback**: it is written by the
client, so trusting it from anywhere would hand the limiter a key the caller picks — the same defect
in a new place. A regression test asserts a forwarded address from an untrusted peer is ignored.

**Guess a password from many addresses.** The address limiter cannot see an attacker who rotates
addresses, so the account carries its own counter: `User.FailedLoginAttempts` is incremented on
every wrong password and the account locks for `User.LockoutDuration` (15 minutes) once it reaches
`User.MaxFailedLoginAttempts` (10). A successful sign-in clears both. The threshold is deliberately
higher than the per-address budget — this is the last line, for an attacker the first line cannot
see, and it must not fire on somebody who mistyped.

A locked account is refused **before its password is checked** and with the **same** error a wrong
password gets. A distinct "locked" answer would be an oracle twice over: it confirms the account
exists, and it tells an attacker their guessing was working well enough to trip the lock. The
person genuinely locked out learns nothing from the response either — that is the cost, paid
deliberately, and the reason the window is short. A test asserts the two answers are identical.

Persistent attackers are still not pushed down to nftables: that needs the agent's
`FirewallService` and is named in the out-of-scope list.

**Learn whether a username exists.** A wrong username and a wrong password produce the same typed
error, `InvalidCredentialsUnauthorized`, from the same code path.

**Get in with a weak password.** The policy (at least 12 characters, not equal to the username) is
enforced by a FluentValidation validator that runs as Wolverine middleware. This is worth naming
because it once did *not* run: the validators were registered but never invoked, and a weak
password reached the database. `SetupEndpointTests` now exercises the real HTTP path specifically
so a dead validator cannot pass again.

## Tokens and sessions

**Replay a stolen access token.** Access tokens live 15 minutes and are validated with
`ClockSkew = TimeSpan.Zero`, so an expired token is expired at the second it says, not five
minutes later.

**Steal the refresh token from JavaScript.** It is in an httpOnly, `SameSite=Strict`, Secure cookie
(`RefreshCookie`), so page script cannot read it and a cross-site request does not carry it.

**Replay a refresh token that was already used.** Refresh rotates: each use issues a new token and
retires the old one. Presenting a retired token revokes the entire family, which turns a successful
theft into an immediate, visible sign-out of the real user rather than silent shared access.
Refresh tokens are stored as SHA-256 hashes — full-entropy values, so a fast hash is right;
recovery codes, which a human types, use Argon2id instead.

**Keep a session after being locked out.** Sessions are rows in PostgreSQL, listed and revocable by
their owner and by an administrator; "sign out everywhere" ends all of them. Revocation is checked
server-side, so it takes effect within the access token's 15-minute life at worst.

**Read or kill somebody else's session.** The endpoint takes no user parameter and scopes by the
caller's identity; another user's session id answers 404, not 403, so it is not an existence
oracle. Proven by `SessionEndpointTests`.

## Two-factor

**Reuse an observed TOTP code.** The verification window is `(previous: 1, future: 0)` and the last
accepted window is recorded on the user (`LastTotpWindow`), so a code cannot be used twice even
inside its own validity.

**Brute-force a recovery code.** Recovery codes are hashed with Argon2id and single-use; using one
records `RecoveryCodeUsed` in the audit journal.

## First-run setup

**Create the first administrator on somebody else's server.** `/setup` requires the installer's
one-time token, compared against configuration; an empty configured token is never satisfied by an
empty supplied one, so a misconfigured server is not handed to the first stranger to post an empty
string. Setup is refused outright once any user exists, whatever the token says. Both are tested.

## Authorization and IDOR

**Reach another tenant's data.** Account endpoints are `[Authorize(AdminOnly)]` as a whole. The
IDOR fixture (`AccountsAuthorizationTests`) enumerates every account-scoped route once and asks
three questions of each: anonymous is refused (401), a signed-in customer is refused (403) *for
their own account id* — because the rule is role-based, and a check that only rejected other
people's identifiers would leave the module open — and an unknown identifier answers 404 rather
than failing. The fixture was verified to fail: weakening the policy to `AnyAuthenticated` turns it
red on all six routes.

**Escalate by forging a role claim.** `HttpContextCurrentUser` compares the role claim against
exactly one name, ordinally: an unknown role, or `admin` in the wrong case, is not an
administrator. A request with no claims, or with no `HttpContext` at all, is nobody rather than an
exception. `HttpContextCurrentUserTests` covers each of those directions.

**Forget an `[Authorize]` and open an endpoint.** The authorization fallback policy requires an
authenticated user, so an endpoint with no attribute denies rather than opens. Anonymity is marked
per action, never on a class — a class-level `[AllowAnonymous]` outranks an action's `[Authorize]`,
which had already left `logout-all` open once.

**Ride a logged-in browser (CSRF).** `SameSite=Strict` plus a mandatory custom header
(`X-Maran-Request`) on cookie-bearing state changes: a cross-site form cannot set a custom header.

## The audit journal

**Amend or forge history.** Entries are written by `IAuditWriter` from inside the handlers that
perform an action; there is no HTTP route that writes one, and `DatabaseAuditWriter` has no update
or delete. The absence of those methods is the enforcement.

**Learn about other tenants by reading it.** The journal names every actor and the address they
came from, so `GET /api/v1/audit` is `AdminOnly` — not the authenticated default. A customer gets
403, an anonymous caller 401, and the SPA renders the panel's own refusal rather than deciding for
itself. Tested in `AuditEndpointTests`.

**Find a secret in it.** A test asserts the journal's response never contains the password that
produced the entry, and the setup handler's audit test asserts neither the token nor the password
appears in the subject.

## The agent boundary

**Run a command through the panel.** There is no "execute a shell string" RPC and no shell
invocation anywhere: account operations are typed proto calls, and the agent spawns processes with
argv arrays against absolute paths that come from the distro adapter. The `ops` crate contains no
platform literal at all — `maran structure` fails the build if one appears — so the paths cannot be
smuggled in as data either.

**Have the panel act as root on the wrong account.** The agent verifies its caller below the RPC
layer with `SO_PEERCRED` (only the `panel` uid), and re-validates every input rather than trusting
the C# side.

## What deliberately did not ship

Named so their absence is not read as an oversight: API keys for hostpanel, nftables auto-banning,
forced 2FA for administrators (the enrolment machinery ships, the policy switch does not), the
optional administrator IP allowlist, and password reset by email. Each is listed in the plan with
the roadmap item it belongs to.

## Residual risk

The password policy and the lockout numbers are enforced but not yet operator-configurable; both
arrive with the settings module, and until then they are constants on `User` with their reasoning
written beside them. The lockout is a fixed window rather than the escalating one the spec
describes, and firewall-level banning of a persistent attacker waits on the agent's
`FirewallService`. An attacker with a very large supply of addresses can therefore still cost a
legitimate user 15-minute lockouts; that denial of service is the accepted trade against the
account takeover the lock prevents.
