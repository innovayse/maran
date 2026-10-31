# Threat note: `POST /api/v1/auth/change-password` (issue #232)

`rules/security.md` ("Sensitive change escalation") requires a threat note and a second reviewer
for any change to auth or session handling. This change adds a new authenticated endpoint that
replaces a password and revokes sessions. The note is written before the code.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. Branch `fix/232-change-password` MUST NOT merge to `main` until a human
second reviewer has read the change against this note and signed off. The owner is told so at
hand-off.

## What changes

Until now a signed-in person could not change their password at all; the only path was the e-mail
reset flow. The change adds:

- `ChangePasswordCommand` in the Identity module: the caller's current password, the new one. The
  caller's id, current session id, address and user agent are server-established members, stamped
  by the action from the access token and the connection, never bound from the request
  (`[JsonIgnore]` + `[BindNever]` on both targets, rules/csharp.md).
- `POST /api/v1/auth/change-password`, `[Authorize(AnyAuthenticated)]`, rate limited by the
  existing `login` policy.
- A form on `/profile` for every role.

## What an attacker could do with it, and why it is safe

1. **Brute-force the current password from a stolen access token.** Somebody holding a stolen
   15-minute access token (but not the password) could use this endpoint as a password oracle. Three
   things bound that:
   - The endpoint shares the `login` rate-limit policy: the same per-address sliding window as
     sign-in and the second-factor step, so guesses made here and guesses made at `/login` come out
     of one budget.
   - A wrong current password is counted on the user row exactly as a failed sign-in is
     (`User.RecordFailedLogin` with the operator's `MaxFailedLoginAttempts`/`LockoutMinutes`), so an
     attacker rotating addresses is still stopped by the per-user lockout.
   - While the account is locked, the endpoint refuses with the same code as a wrong password and
     does not verify the password at all, so the lockout window is not a window of free guesses.
2. **Learn whether a guess was right from the response.** The refusal is one code,
   `CurrentPasswordIncorrect` (ErrorType.Validation, 400), for a wrong password and for a locked
   account. It is a 400, not a 401, on purpose: a 401 makes the SPA renew the access token and
   replay the request, and it is the status for "your token is not good", which is not what happened.
   The caller is authenticated as the account in question, so nothing about another account can be
   learned here.
3. **Timing.** Argon2id verification runs for every attempt that is not refused by the lockout. The
   locked branch skips the hash, so a locked account answers faster; that tells the caller only that
   the account is locked — a fact about their OWN account, which the same per-user counter they are
   tripping already decides. The new password is hashed only after the current one verifies.
4. **Keep an intruder signed in after the owner changes the password (session fixation /
   other-session revocation).** A password is usually changed because it may be in somebody else's
   hands. After the new hash is committed, every session of the user is revoked with reason
   `PasswordChanged` EXCEPT the rotation family of the session making the request
   (`ISessionService.RevokeOthersAsync`). Keeping the family rather than the single row matters:
   another tab of the same browser may have rotated the refresh token since this access token was
   issued, and keeping only the stale row would sign the owner out of their own browser. Revocation
   happens after the password commit, so a failure between the two cannot leave the account with the
   old password and no sessions. No new session is issued and no cookie is set: the caller's own
   session is the one that proved the current password, and minting a fresh one here would be a
   session handed out without a sign-in.
   **Stated limit:** access tokens already issued to the revoked sessions stay valid until they
   expire (at most the access-token lifetime, 15 minutes); the panel validates access tokens by
   signature, not against the session table. This is the same limit `logout-all` and the reset flow
   have, and is not widened here.
5. **CSRF.** The endpoint requires a bearer access token in the `Authorization` header, which a
   browser never attaches on its own to a cross-site request, and the SPA holds it in memory only.
   It sets and reads no cookie, so `CsrfHeaderMiddleware` (cookie-bearing state changes) has nothing
   to judge and no cookie-only request can reach the handler: without the header the request is 401
   before model binding.
6. **Aim the command at another account.** The user id is taken from the caller's token only; the
   command carries `[BindNever]`/`[JsonIgnore]` on it, enforced by `BoundCommandGuardTests`.
7. **Policy bypass.** The new password goes through the same operator-configurable minimum length the
   reset and invitation flows use (`SecurityPolicyCache`), now one shared validator
   (`Validators/NewPasswordValidator.cs`) instead of a rule copied into each command, plus the same
   256-character ceiling bounding Argon2id work. The current password is bounded to the same ceiling
   before anything hashes it. Client-side checks are advice only and err permissive.
8. **Secrets in the audit journal or logs.** Neither password reaches the journal. A success is
   `PasswordChanged` / succeeded, a refusal `PasswordChanged` / failed, both written by
   `IdentityAuditJournal.RecordIdentifiedAsync` with the user's own id as the subject plus address
   and user agent. Neither password is in the command's `ToString` path used by any log line, and the
   command never travels a durable queue (local `InvokeAsync` only).
9. **Lockout interaction for the owner.** Five mistyped current passwords lock the account for the
   operator's lockout window, exactly as five mistyped sign-ins do. The owner's existing sessions are
   NOT ended by the lockout (lockout gates authentication, not sessions), so a mistyping owner is not
   signed out; they wait out the window or use the e-mail reset, which clears the lock. A successful
   change clears the failed-attempt counter and any lock, as the reset does.
10. **Surface.** No new port, daemon, outbound call or agent RPC. One endpoint under an existing
    controller, existing rate-limit policy.

## Tests

- Unit (`ChangePasswordCommandHandlerTests`): wrong current password refused with
  `CurrentPasswordIncorrect` and counted; locked account refused without verifying; success hashes
  the new password, keeps the caller's family and revokes the rest, clears the lockout, writes the
  audit entry; refusal writes a failed entry.
- Unit (`ChangePasswordCommandValidatorTests`): the policy minimum is enforced.
- Integration (`ChangePasswordEndpointTests`): wrong current password 400 with the code; short new
  password 400 `PasswordTooWeak`; success signs the new password in, the old one out, another session's
  refresh cookie is refused and the caller's own still refreshes; an audit row is written; repeated
  attempts are answered 429.
- Playwright (`e2e/profile/change-password.spec.ts`): the form on `/profile` for an administrator and
  a customer, reached by clicks, against stubs.

## What the author could not verify

- That no reverse proxy in front of a production panel collapses client addresses so the shared
  `login` bucket becomes one bucket for everybody (an existing property of the sign-in limit, not
  new here).
- A second reviewer should confirm the family-keeping choice in item 4 against the rotation code in
  `SessionService.RotateAsync`, and that `Session.FamilyId` is never shared between two users.
