# Reseller API keys and provisioning access (issue #368) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
extends a credential class (the API key of issue #144) with an OWNER: a key can now belong to a
reseller and reach that reseller's own accounts and plans. It is authentication, token handling and a
tenant boundary at once. The branch **may land on a branch and MUST NOT merge to `main`** until a
second reviewer has read this note and checked every item under "What a reviewer must check". Written
BEFORE the code, so it can contradict a choice instead of validating one.

## What is being added, in one paragraph

A signed-in reseller creates, lists and revokes API keys that belong to it
(`api/v1/reseller/api-keys`). Such a key carries the reseller's id and may hold exactly two scopes,
`plans.read` and `accounts.read`. With them an integration reads the reseller's OWN plans and
accounts through three new GET endpoints under `api/v1/reseller-api/` (the accounts list, the plans list and one plan). There is deliberately no read of a single account: its detail carries the owner's e-mail address, which a key has no business holding. Nothing else changes for the
existing administrator keys, which keep `server.read` and `self.read`, still cannot reach tenant data,
and are still managed only at `api/v1/api-keys`. No endpoint a key reaches writes anything. The
write half of provisioning (create, suspend, delete an account by key) is deliberately NOT here: it
belongs to the Provisioning module (issue #7), which does not exist yet, and a write by a
long-lived bearer secret needs its own note.

## What changes about the threat model

#144 argued that no key reaches tenant data, so a stolen key leaked server status and nothing else.
That argument is what this change gives up for ONE kind of key, so the new argument has to stand on its
own legs.

- A reseller key reads tenant-adjacent data: the names, primary domains, plans and statuses of every
  account the reseller owns, and the reseller's plans with their limits. It never reads an owner's
  e-mail, a login, a site, a certificate or a file: `AccountDto` and `PlanDto` carry none of them, and
  the Accounts module is the only one that learns the key is a reseller's.
- The data is what the reseller's own login reads on the same two screens. The key adds no data; it
  removes the second factor and the 15-minute lifetime. A stolen reseller key therefore equals a stolen
  reseller password plus second factor, for READ access to two lists, until it is revoked, expires, or
  the reseller is suspended.

## The argument, by concern

### 1. Ownership is established by the server, from the token, and nowhere else

- The owner of a new key is the reseller id on the signed-in reseller's access token, stamped by the
  controller action into a `[BindNever][JsonIgnore]` member of the command (rules/csharp.md
  "Server-established members of a command"). No request field, route value or query value names an
  owner. `BoundCommandGuardTests` holds the guard.
- The administrator endpoint (`api/v1/api-keys`) stamps NO owner, so it can only create
  administrator-owned keys. A reseller endpoint stamps its own reseller. There is no path by which
  one party creates a key for the other.
- One field, one meaning: `OwnerResellerId` is the id of the reseller that owns the key, or `null`
  for a key the administrator owns. It is not a filter, not a permission and not an account id.

### 2. Reach: the key sees what its reseller owns, through one existing mechanism

- The key's reseller id travels in a claim (`key_reseller`) written only by the API key handler and
  read into `ApiKeyCaller.ResellerId` by `HttpContextCurrentUser` for a principal the `ApiKey` scheme
  authenticated, as the key id is. A JWT carrying that claim is refused exactly as one carrying a key
  id already is.
- `ApiKeyCaller.ResellerId` is read by ONE place: the Accounts module's `AccountsDbContext` query
  filters, which already narrow a `ResellerCaller` to `ResellerId`. A key bound to a reseller is narrowed
  the same way, and an administrator-owned key (`ResellerId` null) is narrowed to nothing, as before.
  Every other module's context, directory and journal keeps `ApiKeyCaller => nothing`; the compiler
  forces each `switch` to decide because the closed `Caller` set did not change shape.
- `AccountDirectory`, `ResellerDirectory`, `AccountFeatureDirectory` (the cross-module windows) and
  every Accounts WRITE path still answer a key "no": a reseller key cannot start a view-as, cannot read
  another module's data through a window, and cannot reach a command, because the four endpoints it
  reaches dispatch queries only and no command exists that a key policy admits.
- Another reseller's account answers a reseller key with 404, as it does the reseller's login: the
  query filter hides the row.
- Policy census: the two new policies are `ApiKeyPlansRead` and `ApiKeyAccountsRead`. Both require the
  `ApiKey` scheme, the key id claim, the scope claim AND the reseller claim; `ApiKeyServerRead`
  additionally requires that the reseller claim be ABSENT. So even a key somehow holding a scope that
  belongs to the other kind is refused at the door.

### 3. Scopes are split by who may hold them

`server.read` is administrator-grade (it reads server-wide data that belongs to no tenant);
`plans.read` and `accounts.read` are reseller-grade. The two sets are disjoint and `self.read` is
implicit for both.

- The entity refuses to issue a key whose scopes are outside its owner's set (an administrator key
  with `accounts.read`; a reseller key with `server.read`). The validator refuses the same with the
  existing `ApiKeyScopeUnknown` code, so the API answers 400; the entity is the floor under a command
  dispatched on the bus without the validator.
- The scope list endpoint of each controller offers only that owner's set, localized by the backend.
- A stored scope outside the current vocabulary grants nothing (unchanged from #144).

### 4. The key lives and dies with the reseller

- The authenticator, for a key that has an owner, reads the reseller's LOGIN in the same schema
  (`Users.ResellerId`) and denies the key when the login is absent or suspended
  (`User.IsSuspended`). Suspending a reseller therefore stops its keys on their next call without
  touching them, and resuming the reseller lets the still-valid, un-revoked, unexpired keys work again.
  The denial is the same bodyless 401 as every other refusal (#144 section 2); the reason is logged for
  the operator with the key id.
- Deleting a reseller deletes its keys (`ResellerDeletingHandler`), with the login and its sessions:
  a key is a way in, and a way in must not outlive its owner.
- The check is made on every call; there is no cache, so a suspension takes effect on the next request.
- Equal work: the owner lookup runs after the digest comparison, only for a key that matched, so it
  adds no timing difference between an unknown prefix and a known one.

### 5. Management: a reseller sees and revokes only its own keys

- `GET`, `POST`, `DELETE` under `api/v1/reseller/api-keys` are `ResellerOnly` (JWT) and
  `ViewAsAccess.Refused`: an operator viewing as a reseller cannot mint a credential that outlives the
  look, and a key can never manage keys (no key policy guards them).
- The list, the scope list and the revoke are narrowed to the caller's reseller. A key of another
  reseller, or an administrator's, answers **404** to revoke (`ApiKeyNotFound`), never 403.
- The administrator endpoints `api/v1/api-keys` list and revoke ADMINISTRATOR-owned keys only (issue #435 adds a separate, reseller-scoped oversight surface; see the addendum at the end). They do not list a
  reseller's keys: the Identity module cannot name a reseller (that data is Accounts'), and listing
  keys with an unnamed owner is a screen nobody can act on. The administrator's levers over a
  reseller's keys are suspending or deleting the reseller. **This is a stated limit, not a claim of
  completeness**: an administrator cannot revoke ONE reseller key while leaving the reseller active.
  See "Owner decisions".
- Ceilings: an administrator's keys are counted against the panel's 100 working keys, a reseller's
  against 20 working keys PER reseller. A reseller can therefore neither exhaust the administrator's
  ceiling nor another reseller's. Count and insert stay one serialised step under the existing
  panel-wide advisory lock (`IApiKeyCreationLock`).

### 6. What is unchanged from #144 and still holds

Entropy, SHA-256 at rest, constant-time compare, one 401 for every failure, header-only transport,
the per-address failure limiter, per-key rate limit, address binding, expiry, the per-call audit entry
(the new endpoints are journalled by the same global filter, the route TEMPLATE only), revocation
effective on the next call. The audit entries for create and revoke of a reseller key are written by
the same journal; the actor is the reseller's login. They name the prefix, name and scopes and never
the key or its digest.

## What a reviewer must check

1. `ApiKeyAuthenticator`: an owned key is denied when the login is suspended or missing, after the
   digest compare, with the identical `ApiKeyDenied`.
2. `AccountsDbContext`: the three `switch` expressions read `ApiKeyCaller.ResellerId` only in the
   reseller scope, `SeesEveryRow` stays `false` for a key, and a key with a null reseller id sees
   nothing. Re-run `grep -rn "ApiKeyCaller" backend/src --include=*.cs` and read every module's arm
   still says "nothing" (that grep is `/usr/bin/grep`; the command is the control, the hit list is the
   census).
3. `ApiKeyPolicies`: the reseller claim is required by the two new policies and forbidden by
   `ApiKeyServerRead`.
4. The controllers' `[Authorize]` lines, and that no action under `reseller-api/` dispatches a command or returns an owner's e-mail.
5. `CreateApiKeyCommandHandler`: the owner comes from the command member the controller stamped, the
   count is per owner, and the owner is inside the lock.
6. The migration adds ONE nullable column and an index, drops and renames nothing, and the previous
   release (which does not read the column) keeps working against it.
7. Cross-reseller IDOR: reseller A's key listing accounts never shows B's; A cannot revoke B's key.

## What the author could not verify

- **No integration run.** The integration tests for the new endpoints are written, not executed, by
  this session (no Docker; rules/testing.md "Verification is something you run"). The reviewer runs
  them.
- That `key_reseller` cannot be forged through any authentication scheme other than the key handler.
  Argued from the code (only `ApiKeyAuthenticationHandler.BuildTicket` writes it; the JWT issuer has no
  such claim; `HttpContextCurrentUser` reads it only from a principal whose authentication type is the
  `ApiKey` scheme), not exhaustively tested against a malformed-token corpus.
- The installer's nginx `log_format` omitting `X-Api-Key` (carried over from #144, still unverified).
- Behaviour under a suspended-then-resumed reseller with a key used during the suspension window: the
  key is refused during it; after resumption it works. Whether resumption should instead require new
  keys is an owner decision.

## Owner decisions this note raises

1. **Writes by key.** Create/suspend/delete of an account by a reseller key waits for the Provisioning
   module (issue #7) and its own note.
2. **Administrator oversight of reseller keys.** Listing and revoking one reseller key from the
   administrator's screen needs the reseller's name next to the key, a read window from Accounts into
   Identity's screen, and a UI. Not built here.
3. **Resumption.** Whether a resumed reseller's old keys come back (built) or are dead (safer, needs a
   revoke on suspend).
4. **Reseller UI.** The reseller's keys screen in the SPA is not in this change.

## Addendum (issue #435): the reseller's keys screen and the administrator's lever over one key

Status: **Second review: OUTSTANDING**, as the note above. This addendum answers "Owner decisions" 2
and 4 and replaces the "stated limit" of section 5. Written before the code of #435, so it can
contradict a choice instead of validating one.

### What is added

1. **A reseller's keys screen in the SPA** (Settings, API keys). It is the administrator's existing API
   keys page asking the reseller's existing endpoint (`api/v1/reseller/api-keys`), not a second copy:
   the same table, filter sheet, dialogs and store, parameterised by an owner. No backend change
   serves it; it adds no capability the reseller did not already have at the API.
2. **An administrator's oversight of one reseller's keys**, two endpoints and a screen:
   `GET api/v1/resellers/{resellerId}/api-keys` (the existing list query: page, search, status, sort)
   and `DELETE api/v1/resellers/{resellerId}/api-keys/{id}` (the existing revoke command). The screen
   is the same API keys page reached from the reseller's page, with no create button.

### Threat argument

- **It only ever lowers capability.** The one new lever revokes a key; it cannot create one and cannot
  widen a scope, an address binding or an expiry. No new credential exists after this change that
  did not before. The worst a stolen administrator session does with it is revoke reseller keys,
  which the same session could already do by suspending or deleting the reseller, and which is a
  denial of service on an integration, not a disclosure.
- **The owner comes from the route, and only on an administrator-only controller.** The new controller
  carries the class-level `AdminOnly` policy and `ViewAsAccess.Refused`. The route's reseller id is
  stamped into the same server-established `OwnerResellerId` member the reseller's own surface
  fills from its token. A reseller signed in as itself, as another reseller, or by view-as is refused
  (403), an anonymous caller 401, and an API key has no policy that admits it here, so a key cannot
  revoke a key. The route id is a parameter the administrator may set freely, which is the point of
  the endpoint: an administrator may already read any reseller.
- **Cross-owner confusion is a 404, not a different answer.** The handler looks a key up by key id AND
  owner. Revoking through the route of reseller B a key that reseller A owns, a key the administrator
  owns, or a random id, are the same `ApiKeyNotFound` 404, so the route cannot be used to confirm
  that a key id exists under another owner. The administrator's own `api/v1/api-keys` still answers
  404 to a reseller's key id and lists none of them: the two surfaces stay disjoint.
- **A reseller that does not exist lists nothing and is not an error.** Identity cannot read the
  Accounts schema, and a reseller's keys are deleted with it (`ResellerDeletingHandler`), so there is
  nothing to disclose; the alternative, a cross-module read per list request, was weighed and rejected
  as a new coupling for a case with no information in it.
- **No secret crosses.** The list carries `ApiKeyDto` (prefix, scopes, use), which has no member the
  key or its digest could occupy.
- **Audit.** The revoke writes the existing `ApiKeyRevoked` journal entry through
  `IdentityAuditJournal.RecordApiKeyAsync`, with the **administrator's login as actor**, the key's
  prefix, name and scopes as subject, and the caller's address and agent. An integration test asserts
  the actor is the administrator and the subject carries neither the secret nor the digest. A revoke of
  an already revoked key succeeds and writes nothing, as before. The entry does not name the reseller:
  the prefix identifies the key and the key list under the reseller identifies the owner.
- **The SPA screen adds no authority.** Every owner-dependent call goes to an endpoint whose policy is
  role-exact; the SPA's choice of endpoint is advice, and a wrong choice costs a refused request
  (rules/vue.md, rules/security.md item 6). The secret of a created key is held in memory for one
  showing and cleared on close and on leaving the page, as for the administrator.

### What a reviewer must check (in addition to the list above)

1. `ResellerApiKeyOversightController`: class-level `AdminOnly`, `ViewAsAccess.Refused`, no action
   dispatches a create, and the revoke stamps `OwnerResellerId` from the ROUTE, not from the body or
   the query (`RevokeApiKeyCommand` binds nothing).
2. `ListApiKeysQuery.OwnerResellerId` is `[BindNever]` and is overwritten by the action, so a
   `?ownerResellerId=` query parameter cannot name another owner (`BoundCommandGuardTests`).
3. `ResellerApiKeyOversightEndpointTests`: reseller A, reseller B and an anonymous caller are refused
   both routes, each beside the administrator's accepting control, and a wrong-reseller route answers
   the same 404 as a random id.
4. The audit entry of an oversight revoke names the administrator as actor.

### What the author could not verify

- **No run.** The new integration and end-to-end specs are written, not executed, by this session
  (the lead's gate runs every build and test once). The reviewer runs them.
- That no other consumer reads `ApiKeyRevoked` entries assuming the actor is the key's owner (searched
  in the tree for the action name only; the audit retention and export read it as opaque text).
- That an operator wants the reseller named on the entry. Not built: it would need the reseller's
  name from Accounts or the id carried in the subject. Owner decision.

### Owner decisions this addendum closes or raises

- Closed: "Administrator oversight of reseller keys" (built, revoke only) and "Reseller UI" (built).
- Raised: whether the oversight should also let the administrator revoke ALL of a reseller's keys in
  one action (not built; suspending the reseller does that today without deleting the keys).
- Still open from above: writes by key. Answered by issue #551: a resumed reseller's keys come back
  (the creator rule judges the login on every call, and a suspension is reversible), a deleted
  reseller's do not (see the #551 addendum in `2026-11-05-api-keys-threat-note.md`).
