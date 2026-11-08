# The first-password change

An account created with an initial password (issue #194) gets a login whose first password somebody
else chose. That login can sign in, and can then do nothing but replace the password. This page is the
contract the interface is written against.

## How the gate works

1. The login row carries `PasswordChangeRequired` (`identity."Users"."PasswordChangeRequired"`, default
   false). It is set when the account-creation handler makes the login with an initial password, and
   cleared by a completed password change or password reset. A sign-in that only upgrades the hash of the
   same password does not clear it.
2. Every access token issued for such a login carries the claim `pwd_change` with the value `true`. The
   claim's presence is the restriction; there is no `false` spelling.
3. Every authorization policy the panel defines (`AdminOnly`, `ResellerOnly`, `AdminOrReseller`,
   `AnyAuthenticated`, the fallback and the default) carries `PasswordChangeCompleteRequirement`. It
   refuses a token with the claim on every endpoint except `POST /api/v1/auth/change-password`, which is
   marked `[AllowDuringPasswordChange]`. Anonymous endpoints (sign-in, refresh, sign-out) are untouched.
4. A refused request answers `403` with the code `HostPasswordChangeRequired`. The two-factor steering
   (`HostTwoFactorSetupRequired`) ranks before it when both apply.

## What the interface reads

The signed-in body (`session` of the login answer, and the whole body of refresh and two-factor
verification) carries `requiresPasswordChange: true` beside `requiresTwoFactorSetup`. It is copied from
the issued token, so the flag and the claim are one decision. The interface shows the password-change
screen while it is true.

After `change-password` succeeds the interface calls refresh: the refreshed token is issued from the
login row, no longer carries the claim, and the body says `requiresPasswordChange: false`. The token that
was in the browser stays restricted until then, for at most its fifteen-minute lifetime.

## What it does not cover

- An operator viewing the panel as the customer holds a view-as token, which carries no gate: the
  obligation is the customer's, and view-as is read-only by its own allow-list.
- An API key has no password and is never a login; the key policies are outside the gate by their
  authentication scheme.
- Rolling back to the previous release (which does not know the column) lifts the gate; the column is
  expand-only and harmless to it.
