# Suspension per membership and the retirement of `Users.AccountId`: threat note (issues #326, #325)

**Second review: OUTSTANDING.** This changes sign-in, refresh and account switching (who may obtain a
token for which account) and stops reading the column that used to say which account a login owned, so
rules/security.md "Sensitive change escalation" applies. It may land on a branch and MUST NOT merge to
`main` until a second reviewer has read it.

## What changes

- `identity.AccountMemberships."State"` (`MembershipState`: `Active`, `Suspended`) is the only stored
  fact about whether a login may act for an account right now. `AccountSuspendingHandler` and
  `AccountResumingHandler` change the state of EVERY membership of the account (the owner's and each
  added member's); the login row is not touched, and `UserLifecycle` has no suspended value any more.
- A fourth reader, added for issue #331, is the central request guard: `AccountMembershipActiveRequirement`
  (handled by `AccountMembershipActiveHandler` in Identity, attached to the `AnyAuthenticated` policy in
  the Host's `RolePolicies`) reads the caller's membership of the account their token names on EVERY
  authorized customer request and leaves the requirement unmet when it is suspended; the Host's
  `ProblemDetailsAuthorizationResultWriter` then answers 409 `AccountMembershipSuspended`, the code the
  other doors use. It needs no Sdk window because the fact is Identity's own. Exempt, on purpose: an
  administrator (acts for no account), a view-as token (its per-request session check ends it when the
  suspension revokes its session, and `StartViewAs` refuses a suspended membership), a customer token
  naming no account. (A login with no membership of the named account at all, a removal, was exempt
  here until issue #340 and is now refused by the same handler; see
  `2026-11-03-subuser-grants-threat-note.md`.)
- Three doors read the state: `ActiveAccountResolver` (sign-in and refresh, through
  `AuthenticationCompleter` and `RefreshSessionCommandHandler`), `SwitchActiveAccountCommandHandler`, and
  `AccountMembership.Allows` (the feature gate). A person whose every membership is suspended is refused at
  sign-in with the same `InvalidCredentialsUnauthorized` a wrong password gets, before a second factor is
  asked for.
- `Users.AccountId` and `UX_Users_AccountId` are RETIRED, NOT DROPPED. This release neither reads nor writes
  the column (it is a shadow property named `LegacyAccountId` in `UserConfiguration`, so no entity member
  exposes it) and a new login stores NULL there; it stays in the schema, nullable, with its partial unique
  index, so that `1.0.0-beta.4` can still run against the schema (rules/architecture.md, expand then
  contract). **The drop is held to the release after beta.5.** The "one owner per account" guarantee the
  index gave is re-stated as `UX_AccountMemberships_AccountId_Owner`, a partial unique index created by the
  expand migration `AddMembershipState`.
- Account deletion now deletes every login left with no membership (members who had only that account
  included); before, only the login the dropped column named was deleted.

## What an attacker could try, and why it fails

| Attempt | Why it fails |
|---|---|
| A suspended account's member keeps working because only the owner's login was found | The handlers act on the account's memberships, not on a login; a member's membership is suspended with the owner's. Covered by `AccountSuspendingHandlerTests`. |
| A token issued before the suspension keeps working | Sessions speaking for the account are revoked in the same handler (`ISessionService.RevokeSpeakingForAccountAsync`), and the central guard above refuses every customer request whose token names the suspended account, so the remaining fifteen minutes of an already-issued access token are closed on every tenant endpoint, not only the feature-gated ones. Sessions of the same person speaking for another, still active, account are deliberately kept. Covered by `SuspendedAccountMemberEndpointTests`. |
| An invited member who never accepted survives the suspension | Their membership exists from the moment of the invitation and is suspended with the rest; accepting later stores the password and makes the login `Active`, but sign-in still refuses it. Covered by `SuspendedAccountMemberEndpointTests`. |
| Refresh keeps issuing tokens for the suspended account if the revocation step failed | The resolver only offers active memberships, so a refresh moves to another active account or is refused (`RefreshTokenInvalidUnauthorized`). |
| Switching to the suspended account | 409 `AccountMembershipSuspended`, journalled with `succeeded: false`. The account is the caller's own, so naming its state discloses nothing. |
| Probing other tenants' account ids through the new 409 | A stranger's account id and a non-existent one still answer 404 `AccountMembershipNotFound`; the 409 is reachable only for a membership the caller holds. Covered by `MembershipSuspensionEndpointTests`. |
| Accepting an old invitation to reopen a suspended account | Accepting stores the password and makes the login `Active`, but the membership stays suspended, so sign-in refuses it until the account is resumed. |
| Fresh invitation for a suspended account's owner | Still refused (`InvitationAccountSuspended`), now decided by the owner membership's state. |
| Two owners for one account after the login-side index is gone | `UX_AccountMemberships_AccountId_Owner` refuses the second; asserted against PostgreSQL. |

## What this does NOT cover (stated limits)

- The central guard costs one indexed membership read per authorized customer request, and it covers only
  endpoints that carry the `AnyAuthenticated` policy (every customer endpoint, held by
  `EndpointAuthorizationPolicyTests`); an endpoint a module marks `[AllowAnonymous]` is outside it by
  design. A token whose role is Customer but which names no account is not refused by it.
- `1.0.0-beta.4` reads `Users.AccountId`, so the column is kept and rollback to beta.4 stays possible at
  the schema level. What a rolled-back beta.4 would see is still limited: a login created by this release
  holds NULL there, so beta.4 does not find an account for it. For a login that owns an account, beta.4
  refuses the sign-in from the legacy `State` / `IsAccountSuspended` pair of the owner's row. This release
  keeps that pair in step with the owner membership: `AccountSuspendingHandler` and `AccountResumingHandler`
  restate it through `LegacyOwnerSuspensionPolicy` and `User.SyncLegacyColumns` (suspended while ANY owner
  membership of that login is suspended; an invited owner stays `Invited` with `IsAccountSuspended` true), and
  `AddMembershipState` no longer rewrites the pair when it collapses the two suspended lifecycles (an earlier
  draft blanked it to Active / false, which would have let every suspended account's owner sign in again after a
  rollback; that was wrong and is corrected). Covered by `AccountSuspendingHandlerTests`,
  `AccountResumingHandlerTests` and `UserTests`. Limits: this protects only the OWNER's login (beta.4 had no
  members), a rollback does not undo suspensions made in beta.4 itself, and a login that owns several accounts
  is shown as suspended while any is, because beta.4 could represent one account per login.
  `AddMembershipState.Down` restores only the OWNER's membership suspension onto the login, with the same
  pair values; an added member's suspension has no earlier shape to go back to.
- The architecture census no longer sees `User`, `Session`, `PasswordResetToken`, `InvitationToken`,
  `RecoveryCode` and `PinnedPage` as tenant rows (a login names no account now), so their exemptions were
  removed from `TenantScopeTests`; the account-deletion cascade over them is asserted by
  `AccountDeletionCascadeTests` instead of the census.

## What the author could not verify

Nothing was built or run (the working tree is shared and the gate pass belongs to the coordinator). The
migrations, their designers and the model snapshot were written by hand. PENDING in the gate pass, and the
coordinator will update this note once they are done: the rollback claim above (never exercised against a
real beta.4 build), `maran migrate check` (the snapshot equals the model),
`maran migrate guard` (`AddMembershipState` adds an index and a column and drops nothing the previous release
reads), and a built, run suite (issue #331 adds `SuspendedAccountMemberEndpointTests`, also not run yet). The second review stays OUTSTANDING.
The reviewer should confirm that no other reader of `Users.AccountId` survived (`grep -rn "\.AccountId"`
over `backend/src/Maran.Modules/Identity`) and that `AccountMembership.Allows` is still the only reader of
grants.
