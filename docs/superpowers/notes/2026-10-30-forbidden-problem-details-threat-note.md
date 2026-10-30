# Threat note: 403 answers carry a localized problem body (issues #201, #202)

`rules/security.md` ("Sensitive change escalation") requires a threat note, written first, and a
second reviewer for any change to authentication or authorization. This change touches two such
surfaces: the body of every 403 the authorization middleware writes, and the success body of the
anonymous `POST /api/v1/auth/accept-invitation` endpoint.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. Branch `fix/201-202-customer-area` MUST NOT merge to `main` until a human
second reviewer has read the change against this note and signed off. The owner is told so at
hand-off.

**This note was written first**, before any code of the change existed in the working tree.

## Change 1: a 403 from the authorization middleware gets an RFC 7807 body

### What changes

Before: a request refused by a policy (`AdminOnly`, `AnyAuthenticated`, the fallback policy) was
answered by `AuthorizationMiddlewareResultHandler` with status 403 and an empty body. The SPA then
displayed `response.statusText`, the bare English word "Forbidden".

After: `Maran.Host/Authorization/ProblemDetailsAuthorizationResultWriter` implements
`IAuthorizationMiddlewareResultHandler`. Every result except "forbidden" is passed unchanged to the
framework's default handler (challenge → 401, success → next middleware). On "forbidden" it writes
`application/problem+json` with status 403 and one of two codes:

- `HostTwoFactorSetupRequired` when `TwoFactorEnrolmentCompleteRequirement` is among the failed
  requirements (the caller is steered into forced enrolment);
- `HostForbidden` otherwise.

Title and detail are resolved from `Maran.Host/Resources/ErrorMessages*.resx` in the request
culture. No policy, requirement, or status changes. The decision to refuse is made exactly where it
was made before; only the body of the refusal is new.

### What an attacker could do with it, and why it is safe

1. **Learn whether a route exists.** The fallback policy already answers 403 to a signed-in
   customer for an unmatched route (`AuthorizationFallbackEndpointTests`), so a customer cannot
   tell a real admin route from a missing one by status. The new body must not reopen that: the
   text is the same generic sentence ("You do not have access to this page.") for every refusal,
   and it names no role, route, policy or requirement. A message saying "administrators only"
   would be false for an unmatched route and would distinguish the two cases. Reviewer: confirm
   the body for `/api/v1/audit` and for `/api/v1/does-not-exist` is byte-identical apart from the
   correlation id.
2. **Learn that they are steered into two-factor enrolment.** `HostTwoFactorSetupRequired`
   discloses only what the caller's own access token already says (the
   `TwoFactorSetupRequired` claim). `TwoFactorEnrolmentCompleteRequirement`'s own remarks record
   why this refusal is a 403 and not a 404. It is chosen whenever that requirement failed, even
   if the role requirement failed too, so the SPA keeps steering the person to enrolment first.
3. **Leak another tenant's resource.** Not reachable. Tenant resources answer 404 from the
   handler through the query filter (`ErrorType.NotFound`); this handler only sees policy
   refusals, which are about the caller's role, never about a row.
4. **Leak internals.** The body carries the code, a resx sentence, the status, and the
   correlation id that `ExceptionMiddleware` already returns. No exception text, no requirement
   type names, no paths.
5. **Turn a challenge into a 403.** A non-authenticated caller still gets the framework's
   challenge (401). Only `PolicyAuthorizationResult.Forbidden` is intercepted.

### What the author could not verify

- Whether any non-SPA client (an external API consumer) relied on the empty 403 body. A body on a
  403 is additive and every RFC 7807 consumer must tolerate it, but this was not surveyed.

## Change 2: accepting an invitation returns the login's username

### What changes

`AcceptInvitationCommandHandler` returned `Result<bool>`. It now returns
`Result<AcceptInvitationResultDto>` carrying `Username`, so the success screen can show the name
the person will sign in with.

### Why it is safe

- Only a caller holding a **usable** token gets the username. That caller is the invited person
  about to sign in with exactly that username, so it is not new information to them. The token is
  spent by the same request, so a replay gets nothing.
- A token that never existed, has expired, or was already spent still gets the single
  `InvitationTokenInvalid` refusal (400) with no body change, so the refusal reveals nothing more
  than before.
- The username is not a secret (it appears in the invitation mail and the account screens); the
  password still never leaves the request.

### What the author could not verify

- That an intercepted, unused invitation link was not already a risk of this size: whoever holds
  the token could already set the password and then sign in, where the login screen needs the
  username. Returning it saves such an attacker a guess. The username is in the same mail as the
  token, so the gain is judged nil; a reviewer should confirm that reading.

## What a reviewer must check

1. The handler intercepts only `Forbidden` and delegates every other result to the default
   handler.
2. The body text is generic and identical for a real admin route and an unmatched route.
3. The two-factor code is chosen whenever the enrolment requirement failed.
4. The keys exist in `en`, `ru` and `hy`, and the error-code census lists both codes as answered
   outside result translation with status 403.
5. `accept-invitation` answers a spent or unknown token exactly as before.

## Run evidence

Recorded in the hand-off report of this branch; not repeated here.
