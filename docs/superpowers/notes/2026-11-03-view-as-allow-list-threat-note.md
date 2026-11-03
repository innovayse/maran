# View-as is an allow-list (issue #334) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
decides what an administrator holding a view-as token — a token that speaks for ANOTHER user — may
do. It is session and token handling. The branch may land on `dev` and ships only as the beta the
owner approved in writing; it must not merge to `main` for a stable release until a second reviewer
has read this note and checked every item under "What a reviewer must check". Written before the
slice was finished and refreshed with it; it extends 2026-11-02-view-as-impersonation-threat-note.md
and replaces that note's item 3 (the deny-list) with the argument below.

## The defect this closes

`ViewAsBoundaryHandler` allowed every endpoint that was not marked `[RefusedWhileViewingAs]`. The
marker list covered credentials, second factor and sessions, because that was what the author
thought of. It missed everything that creates something outliving the 15-minute token, which the
second security review of #331 found (M1):

- **SFTP logins.** `POST /api/v1/sftp-users` returns the new login's password. A view-as session
  could mint a login on the customer's host and read the secret back.
- **Databases** (`POST /api/v1/databases`, password reset), **FTP logins**, **cron entries** (a
  scheduled command that runs as the account for ever) and **running a cron job** (a command that
  runs now, as the account), **backups restore**, **certificates**, **sites**.

A deny-list is wrong in the direction that matters: every new endpoint is open to view-as until
somebody remembers to close it, and the person who adds the endpoint is the person least likely to
think of the operator holding a customer's token.

## The surface

One attribute, `Maran.Sdk.Contracts.ViewAsAccessAttribute`, carrying a closed enum,
`ViewAsAccess`:

| Value | Meaning | Honoured for |
|---|---|---|
| `Read` | the endpoint only reads | GET and HEAD only; on any other method it is refused |
| `SafeMutation` | a change that creates no credential, access path, running work or anything that outlives the look | any method; every use is pinned with a reason |
| `Refused` | not reachable from a view-as token | the reviewed form of the default |

It is on the action, or on the controller (the action's own wins: ASP.NET Core orders endpoint
metadata controller first and `GetMetadata` returns the last match). **An endpoint with no
declaration is refused.** The Sdk is the home of the attribute so a marketplace module's controller,
code this repository never reviews, falls under the same default.

Enforcement stays where it was: `ViewAsBoundaryRequirement`, carried by both panel policies, decided
by `ViewAsBoundaryHandler`. The refusal is a 403 with the panel code `HostViewAsNotAllowed`
(`ProblemDetailsAuthorizationResultWriter`; en, ru and hy in the Host's `ErrorMessages`), so the
operator is told that this action is unavailable while viewing, not that the page is forbidden to the
login they look through.

## Classification of the present endpoints

Reads are every GET a customer screen opens on. `SafeMutation` is three entries, pinned in
`ViewAsSafeMutationPin` with reasons: pin and unpin a page (a bookmark in the viewed login's menu)
and `DELETE /api/v1/view-as` (the operator's own way out). **Every other mutation is `Refused`**,
including ones that look harmless, because "what the customer can do" is no longer the test; the
test is whether the change survives the token:

- sites, certificates, backups, databases, FTP and SFTP logins, cron entries and runs, cron
  environment, switching the active account — `Refused`;
- the sessions and sub-users controllers (read and write) — `Refused`, as before;
- administrator-only endpoints — not classified: the view-as token carries `role=Customer`, so the
  administrator policy refuses it before this requirement matters;
- anonymous endpoints — not classified: they read no token.

Support work that needs a mutation is done by the administrator as the administrator, which the
journal attributes to them, not as the customer.

## What an attacker could try, and why it does not work

1. **A new endpoint nobody classified.** Refused at run time (the default), and
   `ViewAsAccessDeclarationTests` fails CI for any composed controller action a view-as token can
   reach that declares nothing, so the author is told to decide.
2. **Declare a mutation a `Read`.** The handler honours `Read` for GET and HEAD only; the census also
   names a `Read` on a POST. A GET that mutates is a defect in the endpoint, which rules/csharp.md
   already forbids; the handler cannot see it and the census cannot either.
3. **Declare a read a `SafeMutation` to skip the pin.** The census names a `SafeMutation` on a GET.
4. **Widen quietly.** A `SafeMutation` that is not in `ViewAsSafeMutationPin` fails the census, so
   widening the allow-list is a diff of a file with a reason beside each entry. A pin entry whose
   action stopped being a safe mutation fails too.
5. **Class-level shadowing.** A controller declared `Refused` at class level stays refused for an
   action that declares nothing; an action's own declaration overrides, which is how
   `DeclaredViewAsController` is held in the accept-side control.
6. **Minimal endpoints.** `/api/v1/dashboard` is not a controller action, so it declares its `Read`
   through `WithMetadata`. The census cannot see minimal endpoints; an undeclared one is refused at
   run time, so the failure is a broken dashboard under view-as, never an open door.
7. **Read as exfiltration.** A `Read` can return anything the customer can read. That is the purpose of
   viewing. What it must not return is a credential: no GET in the tree returns a password (they are
   returned only by the create and reset POSTs, all `Refused`), and a reviewer re-checks that for
   every GET declared `Read`.

## Accepted residual

- A mutation done through a GET is invisible to both the handler and the census. Rules forbid it;
  nothing here proves it.
- The two pinned page mutations alter the customer's own menu while the customer is not watching. They
  are journalled with the operator as impersonator (`DatabaseAuditWriter`), which is the control.
- The allow-list is checked per request, not per session: a `Read` that triggers background work (a
  stream) lives as long as its connection, bounded by the token's `exp` like any other request.

## What a reviewer must check

1. `ViewAsBoundaryHandler.IsAllowed` is the only place a view-as token is let through, and its default
   arm refuses.
2. Every `[ViewAsAccess(ViewAsAccess.Read)]` is on a GET.
   `grep -rn -B1 "ViewAsAccess.Read" backend/src --include=*Controller.cs` and read the line above:
   it must be `[HttpGet...]`, and the action must not write.
3. Every `SafeMutation` is in `ViewAsSafeMutationPin` and the reason holds: no credential, no access
   path, no running work, nothing that outlives the token.
   `grep -rn "ViewAsAccess.SafeMutation" backend/src --include=*.cs` must list exactly the pin.
4. No GET declared `Read` returns a secret.
5. The census has teeth: `ViewAsAccessDeclarationTests` carries a planted-violation controller and an
   accept-side one; delete a `[ViewAsAccess]` line from a real controller and the first test must go red.
6. `HostViewAsNotAllowed` is in `ExpectedErrorStatuses.AnsweredOutsideResultTranslation` and the Host
   resx triple.

## What the author could not verify here

- Nothing was built or run (the shared-tree brief forbids builds). Every test is written and
  unexecuted; the integrator's run is the first measurement, and the census list of reachable actions
  is a reflection result no one has seen printed.
- That every GET declared `Read` is free of side effects and secrets: it was judged from the
  controller signatures and the names of the queries, not by reading each handler.
- The SPA's behaviour when a view-as session meets `HostViewAsNotAllowed` (it will show the localized
  title as it does for any problem body; no screen hides its buttons while viewing).
- Marketplace modules: their controllers are refused by default; no composed example exists to try.
