# Threat note: `POST /api/v1/auth/refresh` answers 204 when no cookie is sent (issue #204)

`rules/security.md` ("Sensitive change escalation") requires a threat note and a second reviewer
for any change to session or token handling. This change touches the anonymous refresh endpoint.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. Branch `fix/204-upstream-refusals` MUST NOT merge to `main` until a human
second reviewer has read the change against this note and signed off. The owner is told so at
hand-off.

## What changes

Before: a request carrying no `maran_refresh` cookie (absent or empty) was answered 401
`RefreshTokenInvalidUnauthorized`. Every signed-out page load asks this endpoint once, so the
browser logged a 401 in the console on every one of them.

After: the same request is answered **204 with no body and no `Set-Cookie`**. Nothing else moves:
a cookie that is sent is handed to `RefreshSessionCommand` exactly as before, and every refusal
from it (unknown, expired, revoked, reused) still answers 401 and deletes the cookie. The SPA reads
the 204 as "no session" and clears its state the same way it cleared it on the 401.

## What an attacker could do with it, and why it is safe

1. **Learn something new from the status.** The status now depends on one fact: whether the
   caller's OWN request carried the cookie. The caller already knows that — it built the request.
   No server state is consulted on the 204 path: no database read, no user, no session, no
   account. The 401 path answers identically for every kind of invalid cookie, as before, so the
   change does not separate "unknown token" from "expired token" or "revoked family".
2. **Obtain a session without a credential.** The 204 carries no body and sets no cookie, so it
   issues nothing. An access token is still minted only after `SessionService.RotateAsync`
   accepts a presented token.
3. **Bypass reuse detection.** Reuse detection runs on a presented token; a request with no token
   never had a token to reuse. A replayed spent cookie still reaches `RotateAsync`, still revokes
   the family and still answers 401 `RefreshTokenReusedUnauthorized`
   (`SessionEndpointTests.Presenting_a_spent_refresh_cookie_a_second_time_is_refused`).
4. **CSRF.** Unchanged. `CsrfHeaderMiddleware` judges state-changing requests that carry a cookie;
   the 204 path is exactly the request that carries none, which had no effect before and has none
   now.
5. **Rate limiting and audit.** Unchanged. The endpoint keeps its policy, and the no-cookie case
   was never audited, since there is no subject to record.

## Tests

- `SessionEndpointTests.Refreshing_with_no_cookie_at_all_answers_no_content_and_sets_no_cookie`
- `SessionEndpointTests.Refreshing_with_a_present_but_invalid_cookie_is_still_refused`: the inverse
  control, a present invalid cookie is still 401 and is cleared.
- `frontend/e2e/auth/signed-out-console.spec.ts`: `/login` with no cookies logs no console error,
  and a refused cookie (401) is still seen by the same console probe.

## What the author could not verify

The suites were not run in this session; the lead runs them after all changes are in. A reviewer
should confirm the integration tests above pass, and that no proxy in front of the panel turns a
204 into another status.
