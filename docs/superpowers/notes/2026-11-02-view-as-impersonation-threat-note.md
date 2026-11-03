# View as an account (issues #317, #314) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
lets one signed-in user obtain an access token that speaks for ANOTHER user. It is session and token
handling in the strictest sense. Written before the code, per that rule; the branch may land on `dev`
but must not merge to `main` until a second reviewer has read this note and checked every item under
"What a reviewer must check".

## The surface

Three endpoints, all in the Identity module (`Controllers/ViewAsController.cs`):

```
POST   /api/v1/view-as   {accountId}   administrators only; issues a view-as session + access token
GET    /api/v1/view-as                 any signed-in caller; reports whether THIS token is a view-as one
DELETE /api/v1/view-as                 only meaningful with a view-as token; ends that session
```

What `POST` produces:

- A new `identity.Sessions` row with `Kind = ViewAs`, `UserId` = the viewed account's customer
  login, `ActorUserId` = the administrator, `ActorSessionId` = the `sid` of the administrator's own
  access token, and `ExpiresAt = now + 15 minutes` (`ViewAsLifetime.Duration`, a constant, not an
  option).
- An access token for the viewed login — `sub`, `name`, `role=Customer`, `account` exactly as the
  customer's own token would carry — plus `sk=ViewAs` and an RFC 8693-style `act` claim, a JSON
  object `{"sub": <admin id>, "name": <admin login>, "sid": <admin session id>}`. Its `exp` is the
  session's `ExpiresAt`, not the usual access-token lifetime.
- **No refresh token.** The row needs a token hash (the column is required and unique), so one is
  generated, hashed and thrown away; the plaintext never leaves the method. The refresh cookie is
  NOT touched.

### The refresh cookie, and why the operator is never stranded

The refresh cookie (`maran_refresh`, `Path=/api/v1/auth`, HttpOnly, SameSite=Strict) holds the
administrator's own rotation family before, during and after viewing. Nothing in this change writes
or deletes it. While viewing, the SPA holds the view-as access token in memory instead of the
administrator's. The SPA renews only on a 401 (there is no refresh timer), so while the view-as
token is accepted nothing calls `/auth/refresh`.

Every way out lands back on the administrator's own session through that untouched cookie:

- **Back** — `DELETE /api/v1/view-as` with the view-as token revokes the view-as row
  (`SessionRevocationReason.ViewAsEnded`) and journals the end; the SPA then calls `/auth/refresh`,
  which rotates the administrator's family as any refresh does.
- **Expiry** — after 15 minutes the token's `exp` passes; the next call is 401, the SPA's ordinary
  401 path calls `/auth/refresh`, and it is the administrator who comes back. The banner then
  disappears because `GET /view-as` answers "not viewing" for the administrator's token.
- **Revocation from the other side** — the customer ending the view-as row from their own sessions
  screen, the account being suspended (`RevokeAllAsync`), or the administrator signing out
  everywhere: see item 6 below — the next request is 401 and the same refresh brings back whatever
  the cookie still holds (nothing, after a sign-out-everywhere, which is the right answer).

A view-as session can never be ROTATED: `SessionService.RotateAsync` refuses a `ViewAs` row, and its
token is never handed out anyway. So the one credential that outlives 15 minutes is the
administrator's own, and it never left the administrator's cookie.

## What an attacker could try, and why it does not work

1. **A customer asks to view as somebody.** `POST` is `AdminOnly`; a customer token is refused with
   403 by the policy before any handler runs. The handler re-reads the actor from the database and
   requires `Role == Admin` and `MayAuthenticate`, so a token issued before a demotion or a
   suspension cannot start one either.
2. **An administrator names an account that does not exist, or one with no customer login.** 404
   (`ViewAsAccountNotFound`), indistinguishable between the two, and journalled as a refused
   `ViewAsStarted`. An administrator's scope is every account. **Reseller rule (implemented in #179):**
   a reseller may view as only accounts it owns. The handler resolves the target through
   `IAccountDirectory.FindAsync`, which runs under the actor's own request caller and answers `null`
   for an account outside it, so a reseller naming another reseller's account gets the same 404 from
   the same line. The endpoint's policy is `AdminOrReseller`, and an administrator may also start a
   view-as into a reseller through `POST /api/v1/view-as/reseller`. The edges, the token role and
   the no-chaining rule are in `2026-11-03-reseller-role-threat-note.md`.
3. **Escalate from inside a view-as session.** The token's role is the VIEWED login's (`Customer`, or
   `Reseller` since #179), so every administrator endpoint refuses it exactly as it refuses that login. On top of that,
   `ViewAsBoundaryRequirement` is attached to BOTH panel policies (like the two-factor steering) and
   refuses, for a view-as token, every endpoint not declared reachable with `[ViewAsAccess]` (a deny-list when this note was
   written, an allow-list since issue #334 — see 2026-11-03-view-as-allow-list-threat-note.md): starting another
   view-as (so no chaining and no switching without stopping), changing the password, enrolling,
   confirming or disabling two-factor, signing out everywhere, and the whole sessions controller
   (list and revoke), the sub-users controller (invite, change grants, remove, list) and switching
   the session's active account (`POST /api/v1/me/active-account`). Profile is read-only today and
   stays reachable. Invitations and the issuing of links are `AdminOnly` already.
4. **Forge the actor.** `act`, `sk` and `sid` live inside the HMAC-signed token; only
   `JwtAccessTokenIssuer.IssueViewAsAsync` writes them. A view-as token is additionally validated
   against its row on EVERY request (item 6), and the row's `UserId` and `ActorUserId` must equal the
   token's `sub` and `act.sub`, so a token that verified but disagreed with its row is refused.
5. **Hide what was done while viewing.** `DatabaseAuditWriter` — the one writer every module's
   journal reaches — stamps `ImpersonatorUserId` and `ImpersonatorUsername` from
   `ICurrentUser.ActorUserId`/`ActorUsername` on every entry written during a view-as request. The
   entry's own actor stays the viewed customer, because that is whose authority the action ran
   under; the impersonator column says who was really at the keyboard. Start and end are journalled
   explicitly (`ViewAsStarted`, `ViewAsEnded`) with the administrator as actor and the account and
   login as subject.
6. **Keep using a view-as token after Back, after the customer revoked it, or after the
   administrator signed out everywhere.** Ordinary access tokens are stateless for their 15 minutes;
   a view-as token is not. `ViewAsSessionValidation` runs in the JWT bearer handler's
   `OnTokenValidated` for every token carrying `sk=ViewAs` or `act`, and fails authentication (401)
   unless: the `sid` row exists, is `Kind = ViewAs`, is active, matches `sub` and `act.sub`; AND the
   administrator's own rotation family (found from `ActorSessionId`) still has an active session.
   Checking the FAMILY rather than the row is deliberate: the administrator's row rotates whenever
   any other tab refreshes, and checking the row would end viewing at every rotation.
7. **Replay the view-as token from another machine.** Same as any bearer token for at most 15
   minutes, bounded further by item 6. No refresh exists to extend it.
8. **Smuggle the view-as state into the admin's own session.** The SPA's banner is drawn from
   `GET /view-as`, which reads the CURRENT TOKEN's claims and re-checks the row; the store's own
   flag is never the source of the banner. A tampered SPA can show or hide a banner; it cannot make
   a token carry `act`.
9. **Sign out while viewing.** `POST /auth/logout` is anonymous and reads the refresh cookie, which
   is the administrator's: it signs the ADMINISTRATOR out, which is what a person pressing "sign
   out" means. The view-as row is not revoked by it, and does not need to be: item 6 refuses the
   view-as token on its next request, because the actor's family is now revoked. The row then
   lapses at its `ExpiresAt` with no `ViewAsEnded` entry (see "Accepted residual").

## Why 15 minutes

Long enough to look at an account's screens and follow one support thread; short enough that a tab
left open in a shared office ends itself. It equals the default access-token lifetime, so a view-as
token never outlives what an ordinary token could, and it is a constant rather than an option on
purpose: an operator who raises it to a day has made a bearer credential for another person that
lasts a day, with no refresh step to re-check anything. A longer look is a second Back-and-start,
and each start is journalled.

## Accepted residual

- The view-as session appears on the customer's own sessions screen, with the administrator's
  address and user agent. That is deliberate transparency: the customer can see that the panel was
  viewed as them, and can end it.
- Expiry is not journalled as an event: nothing runs at the 15th minute. The journal shows the start
  and, when the operator pressed Back, the end; an expired session is a start with no end, and the
  row's `ExpiresAt` says when it lapsed.
- Work handed to a DURABLE Wolverine queue and journalled later runs with no HTTP context, so its
  entry carries no impersonator. Every journal write on the endpoints reachable while viewing is
  local `InvokeAsync`, which keeps the context; this is a limit for future handlers, not a gap today.
- `ICurrentUser.ActorUserId`/`ActorUsername` are default interface members returning "no actor", so
  the twenty test doubles and design-time stand-ins need no edit. The one production implementation
  that can see a token (`HttpContextCurrentUser`) overrides both.

## What a reviewer must check

1. `ViewAsBoundaryRequirement` is on both policies in `Maran.Host/Authorization/RolePolicies.cs`, and
   every endpoint in item 3 carries `[ViewAsAccess(ViewAsAccess.Refused)]` or is undeclared. The
   architecture test `ViewAsAccessDeclarationTests` fails for any endpoint that is not classified.
2. `ViewAsSessionValidation` is wired in `AuthenticationExtensions` and fails CLOSED: a malformed
   `act`, a missing row, a database error, or a missing `sid` is a failed authentication, never a
   pass.
3. `IssueViewAsAsync` cannot be reached except from `StartViewAsCommandHandler`, and that handler
   re-reads the actor's role from the database.
4. `SessionService.RotateAsync` refuses `Kind = ViewAs`, and `StartViewAsAsync` never returns the
   refresh token.
5. The integration tests in `ViewAsEndpointTests` actually observe each refusal (each test asserts the
   status AND that nothing changed), including the expiry test, which moves the panel's clock rather
   than sleeping.
6. Nothing in the SPA decides anything: every refusal above holds with the SPA replaced by curl.

## What the author could not verify here

- Nothing was built or run (the shared-tree brief forbids builds). Every test is written and
  unexecuted; the integrator's run is the first measurement.
- The exact claim shape `Microsoft.IdentityModel.JsonWebTokens` produces for a dictionary-valued
  claim on the way back in (expected: one `act` claim whose value is the raw JSON object and whose
  value type is `JSON`). `HttpContextCurrentUser` and `ViewAsSessionValidation` parse it with
  `System.Text.Json` and fail closed when it is not an object; the integration test decoding the
  issued token is the check.
- That `OnTokenValidated` runs before authorization for SignalR/SSE endpoints the same way it does
  for controllers — expected, since they share the bearer scheme, but not observed.
- The migration (`Sessions.Kind`, `ActorUserId`, `ActorSessionId`; `AuditEvents.ImpersonatorUserId`,
  `ImpersonatorUsername`) is generated by the integrator; it is additive only.

## Not in this slice

- Reseller → account and administrator → reseller: implemented by #179; see `2026-11-03-reseller-role-threat-note.md`.
- Showing the impersonator column on the audit screen (the DTO carries it; the table does not yet).
