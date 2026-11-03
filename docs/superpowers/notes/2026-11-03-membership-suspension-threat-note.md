# Suspension per membership and the end of `Users.AccountId`: threat note (issues #326, #325)

**Second review: OUTSTANDING.** This changes sign-in, refresh and account switching (who may obtain a
token for which account) and drops the column that used to say which account a login owned, so
rules/security.md "Sensitive change escalation" applies. It may land on a branch and MUST NOT merge to
`main` until a second reviewer has read it.

## What changes

- `identity.AccountMemberships."State"` (`MembershipState`: `Active`, `Suspended`) is the only stored
  fact about whether a login may act for an account right now. `AccountSuspendingHandler` and
  `AccountResumingHandler` change the state of EVERY membership of the account (the owner's and each
  added member's); the login row is not touched, and `UserLifecycle` has no suspended value any more.
- Three doors read the state: `ActiveAccountResolver` (sign-in and refresh, through
  `AuthenticationCompleter` and `RefreshSessionCommandHandler`), `SwitchActiveAccountCommandHandler`, and
  `AccountMembership.Allows` (the feature gate). A person whose every membership is suspended is refused at
  sign-in with the same `InvalidCredentialsUnauthorized` a wrong password gets, before a second factor is
  asked for.
- `Users.AccountId` and `UX_Users_AccountId` are dropped. The "one owner per account" guarantee the index
  gave is re-stated as `UX_AccountMemberships_AccountId_Owner`, a partial unique index.
- Account deletion now deletes every login left with no membership (members who had only that account
  included); before, only the login the dropped column named was deleted.

## What an attacker could try, and why it fails

| Attempt | Why it fails |
|---|---|
| A suspended account's member keeps working because only the owner's login was found | The handlers act on the account's memberships, not on a login; a member's membership is suspended with the owner's. Covered by `AccountSuspendingHandlerTests`. |
| A token issued before the suspension keeps working | Sessions speaking for the account are revoked in the same handler (`ISessionService.RevokeSpeakingForAccountAsync`); the feature gate reads the membership on every call, so gated areas close at once. Sessions of the same person speaking for another, still active, account are deliberately kept. |
| Refresh keeps issuing tokens for the suspended account if the revocation step failed | The resolver only offers active memberships, so a refresh moves to another active account or is refused (`RefreshTokenInvalidUnauthorized`). |
| Switching to the suspended account | 409 `AccountMembershipSuspended`, journalled with `succeeded: false`. The account is the caller's own, so naming its state discloses nothing. |
| Probing other tenants' account ids through the new 409 | A stranger's account id and a non-existent one still answer 404 `AccountMembershipNotFound`; the 409 is reachable only for a membership the caller holds. Covered by `MembershipSuspensionEndpointTests`. |
| Accepting an old invitation to reopen a suspended account | Accepting stores the password and makes the login `Active`, but the membership stays suspended, so sign-in refuses it until the account is resumed. |
| Fresh invitation for a suspended account's owner | Still refused (`InvitationAccountSuspended`), now decided by the owner membership's state. |
| Two owners for one account after the login-side index is gone | `UX_AccountMemberships_AccountId_Owner` refuses the second; asserted against PostgreSQL. |

## What this does NOT cover (stated limits)

- An access token issued before the suspension and not tied to a revoked session is honoured by endpoints
  that do not ask the feature gate until its fifteen-minute lifetime ends; unchanged from member removal.
- `1.0.0-beta.4` reads `Users.AccountId`. Dropping it means that release cannot be rolled back onto this
  schema (the `// contract-phase:` line in `DropUserAccountId` says so). The owner decides whether a
  pre-release is a rollback target.
- The architecture census no longer sees `User`, `Session`, `PasswordResetToken`, `InvitationToken`,
  `RecoveryCode` and `PinnedPage` as tenant rows (a login names no account now), so their exemptions were
  removed from `TenantScopeTests`; the account-deletion cascade over them is asserted by
  `AccountDeletionCascadeTests` instead of the census.

## What the author could not verify

Nothing was built or run (the working tree is shared and the gate pass belongs to the coordinator). The
migration, its designer and the model snapshot were written by hand: `maran migrate check` must confirm the
snapshot equals the model, and `maran migrate guard` must accept `DropUserAccountId` through its marker.
The reviewer should confirm that no other reader of `Users.AccountId` survived (`grep -rn "\.AccountId"`
over `backend/src/Maran.Modules/Identity`) and that `AccountMembership.Allows` is still the only reader of
grants.
