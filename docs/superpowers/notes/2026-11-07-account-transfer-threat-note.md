# Threat note: moving an account between resellers (issue #369)

`rules/security.md` requires a threat note, written first, for a change that touches session handling
and tenant ownership. It is written BEFORE the code: every statement below is a requirement the code
must meet. Where the code lands and differs, the code is wrong or this note is amended in the same
change.

**Second review: OUTSTANDING - must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch carrying this change MUST NOT merge to `main` until a
human reviewer has read it (rules/security.md "Sensitive change escalation"). Nothing here discharges it.

## What is added

1. `PUT /api/v1/accounts/{id}/owner`, administrator only, body `{ resellerId, planId }`. A null
   `resellerId` hands the account to the administrator.
2. `TransferAccountCommandHandler` in the Accounts module, `Account.TransferTo` on the entity.
3. The Sdk event `AccountOwnerChanging`, invoked inline BEFORE the row is written, and an Identity
   subscriber that revokes the view-as sessions a reseller opened on the account.
4. `ISessionService.RevokeResellerViewAsForAccountAsync` and the revocation reason `AccountTransferred`.
5. Audit action `AccountOwnerChanged`, error code `AccountOwnerUnchanged`, operator documentation.

Not added: no schema change (so no migration), no proto change, no new agent rpc or capability, no new
port, daemon or outbound call.

## 1. What an attacker could do with this surface

- **A reseller moves its own account out, or somebody else's in.** The route is under the
  `AdminOnly` policy on top of the controller's `AdminOrReseller`; both must pass, so a reseller token
  is refused with 403 before any handler runs. Nothing else dispatches the command: the handler is
  reached only through this route, and a test pins the policy on the action.
- **A customer or API key calls it.** Refused by the same policy: the API-key scheme names no policy
  that admits this action and a customer's role is not an administrator.
- **View-as keeps the former owner inside.** A reseller's view-as session on the account carries a
  customer token for the account's owner login with an `act` claim. It is valid for up to the view-as
  lifetime and is checked against its session row on every request. If the account moved and that row
  stayed live, the former reseller would keep operating a customer it no longer owns. The subscriber
  revokes every live view-as session on the account whose OPERATOR is a reseller. An administrator's
  view-as session is left alone: the administrator still sees the account after the move.
- **The subscriber is skipped.** The event is invoked inline before the row is written, so a handler
  that throws aborts the move and the account stays with its owner (the recoverable direction). An
  event with no subscriber cannot be told from a module with nothing to revoke; the architecture test
  that every module composes the Identity handler is the existing guard for that shape, and the
  integration test of the endpoint asserts a view-as session really ends.
- **The customer's own sessions.** They are NOT revoked. A customer's login belongs to the account, not
  to the reseller; its token carries the account id and no reseller id, so the move changes nothing it
  can do. Revoking it would sign a paying customer out for a billing re-assignment.
- **Cross-tenant disclosure.** The handler runs in the administrator's context, which sees every row. It
  answers a target reseller or plan that does not exist with 404, and a plan that belongs to another
  owner than the target with 409 `PlanOwnerMismatch`. Both answer an administrator only, so they
  disclose nothing to a tenant.
- **Pool and limit bypass.** The target reseller's account limit and disk pool are admitted with the
  same guard account creation uses, including the new plan's quota, and its allocation revision is
  bumped in the same save, so a concurrent claim of the last slot loses with a concurrency failure and
  the host is put back on the account's former figures.
- **Host left on the wrong quota.** The new plan's disk quota and runtime ceilings are pushed before
  the row is written (agent first, row second, as every handler here). A save that fails after the
  push restores the former plan's figures; a restore that fails too converges when the move is repeated,
  the agent calls being replacements.
- **Replay.** The action is `[Idempotent]`; a replay with the same key returns the stored answer, and a
  repeat without a key finds the account already owned by the target and answers 409
  `AccountOwnerUnchanged` or, when the plan matches as well, the unchanged account.

## 2. What the author could not verify

- That the second reviewer agrees the customer's own sessions should survive the move.
- Behaviour against a live agent: the quota and runtime push is covered by the recording double only.
- The end-to-end view-as revocation against the composed panel is covered by the endpoint test added
  with this change; no browser run was made.
