# Threat note: an administrator moves a login's mail address (issue #216)

`rules/security.md` requires a threat note, written first, for a change to authentication and token
handling, and a second reviewer. This change lets an administrator change the address a customer's
login is mailed at, and that address is where the login's invitation and every password reset are
sent. It also adds a refusal to the invitation issuer. Both touch how a credential reaches a person,
so both are argued here.

**This note was written alongside the code, not before it**, which is the shape the rule forbids: a
note written after the choice tends to validate the choice. It is recorded as such rather than
back-dated. **The second reviewer requirement is OUTSTANDING.** The work was carried out by an agent
session, which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has
read it.

## What changed

- `PUT /api/v1/accounts/{id}/owner-email` (`ChangeAccountOwnerEmailCommandHandler`, Accounts) asks
  Identity, through the Sdk request `AccountLoginEmailChangeRequested`, to move the account's login
  to the new address (`AccountLoginEmailChangeRequestedHandler`, `User.ChangeEmail`), and then stores
  the address on the account row.
- `InvitationIssuer` refuses an invited login whose account is suspended
  (`InvitationAccountSuspended`), for both the mailed resend and the administrator's link.

## What an attacker could do with the address change

1. **Redirect a customer's password reset to an address they control.** That needs the
   administrator role: the action is `AdminOnly`, a customer and a reseller are answered 403
   (`AccountEditEndpointTests`, `AccountsAuthorizationTests`, `AccountResellerPrivilegeEndpointTests`). An administrator already controls the
   whole server, the customer's files and the customer's login state, so this gives an administrator
   nothing they did not have. A stolen administrator session is the real risk, and it is the same risk
   every administrator endpoint carries; the change is journalled (`AccountOwnerEmailChanged`, actor,
   address of the caller), so it is visible afterwards.
2. **Take an address another login holds, so that one reset mail names two accounts.** Refused:
   `EmailTaken`, checked before the write and backed by the unique index `IX_Users_Email`, whose
   violation is answered the same way.
3. **Inject mail headers through the address.** The validator uses `EmailAddressRule` (no display
   names, no control characters), the same rule account creation uses, capped at 254 characters.
4. **Learn which addresses exist.** Only an administrator can ask, and an administrator can already
   list every login.

What the change deliberately does not do: it does not revoke the login's sessions, and it does not
mail the old address. A customer who still knows their password keeps working; the new address only
decides where the NEXT invitation or reset goes. Whether the old address should be told is a product
decision for the reviewer.

## The suspended-invitation refusal

It only narrows what is issued, never widens it. A suspended account's invited login already could
not become usable (`User.Activate` keeps it suspended), so the refusal removes an offer rather than
closing a hole. An account with no login at all is refused too: `AccountSnapshot` now carries the
account's `State` (required, no default), and the issuer creates no login and no token for a
suspended one.

## What a reviewer must check

- That no customer-reachable route reaches `AccountLoginEmailChangeRequested` (it is invoked only by
  the Accounts handler).
- That the two writes (login first, account row second) cannot leave the login on an address the
  administrator did not ask for.
- Whether the old address should be notified of the change.

## Correction (issue #482): the reseller role reached this endpoint

The statement above, that the controller is `AdminOnly` at class level, was wrong once resellers
existed: `AccountsController` is `AdminOrReseller`, so a reseller could change the owner address of
any account in its scope, and the next password reset for that customer went to the address the
reseller chose. That is an account takeover of a customer who did not agree.

**Decision.** The route is administrator-only, enforced on the action (`[Authorize(Policy =
AuthorizationPolicies.AdminOnly)]` on `ChangeOwnerEmailAsync`), so a reseller, a reseller's API key
and a customer are answered 403 before the handler runs. The alternative the issue names, a reseller
allowed only after the CURRENT owner confirms through a link sent to the OLD address, needs a
confirmation token, its table, its mail and its expiry, none of which exists; building a credential
flow to preserve a convenience is more attack surface than the convenience is worth. A reseller who
needs a customer's address changed asks the administrator, and the change is journalled as before.
The SPA hides the action from everyone but an administrator (advice only; the endpoint refuses).

**Tests.** `AccountResellerPrivilegeEndpointTests` (a reseller is refused for its own account and
another's and the stored address is unchanged, with an administrator accepted as the control), and
`e2e/accounts/reseller-privileges.spec.ts` for the screen.

**Second review: OUTSTANDING (#331)**
