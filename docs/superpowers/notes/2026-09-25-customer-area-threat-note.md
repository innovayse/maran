# Threat note — Customer area (issue #49)

`rules/security.md` requires a threat note for any change to authentication, session and token
handling, and requires a second reviewer. This change is authentication and token handling: it
creates a new class of panel login, a new credential-bearing mail, and a new anonymous endpoint
that sets a first password.

**The second reviewer requirement is OUTSTANDING.** The work was carried out by agent sessions,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it.

**This note was written LATE, and that weakens it.** The rule says the note is written first,
before the change, because a note written afterwards is a justification for choices already made
rather than an argument that shaped them. The reviewer should read every claim below as an
assertion by the author of the change, and should verify rather than accept the ones marked so.

## What the change adds

A hosting account's owner can now hold a panel login. Concretely: the account creation flow
announces `AccountCreated`, the Identity module issues a `Customer` login in a new `Invited` state
holding no password, publishes an invitation mail carrying a single-use token, and a new anonymous
endpoint exchanges that token for a first password. The login then follows its account's life —
suspended, resumed, deleted. A new read endpoint answers "my account", and the module catalogue
now answers per role so a customer is not offered server-wide screens.

The tenant isolation this all relies on — query filters on every tenant entity, `IgnoreQueryFilters`
as a build error, `AccountDirectory` scoping the OS-backed resources — is NOT new and was not
changed. What is new is that a principal finally exists to exercise it.

## The invitation token

**Steal it from the database.** Only its SHA-256 digest is stored (`InvitationToken.TokenHash`),
drawn from 256 bits of CSPRNG output, so a dump of the table yields nothing usable. The plaintext
exists in the mail body and nowhere else — not in a log, not in an audit entry, not in a response
body. Asserted as an absence, not assumed: a test extracts the token from the published mail,
hashes it, matches it to the stored digest, and then asserts the journal entry contains neither the
token nor its hash.

**Replay it.** Single use is recorded by stamping `UsedAt`, never by deleting the row, so a second
presentation is visible rather than indistinguishable from a token that never existed. Accepting an
invitation also retires every other outstanding token for that user, so two live invitations cannot
coexist.

**Learn which tokens exist.** Expired, spent and unknown tokens all receive the identical refusal.
Tested by asserting the two results are EQUAL, not merely that both failed.

**Intercept it in transit.** The mail is published on a deliberately non-durable local queue, so a
live token never rests in an envelope table where it would survive a database dump and outlive its
own lifetime. The accepted cost is a lost mail when the process dies between publish and send; the
recovery is the administrator's resend.

**Have the panel mail a token to an address of the attacker's choosing.** The recipient is read off
the stored row, never echoed from the request. `OwnerEmail` is validated by the panel's own
`EmailAddressRule.IsAddress`, not FluentValidation's built-in rule — this was a review finding, and
it matters: MailKit's `MailboxAddress.Parse` was read and is lenient, accepting a display-name form,
so the panel's own rule is the only rejection layer. **Reviewer: verify this claim.**

## The new anonymous endpoint

`POST /api/v1/auth/accept-invitation` is anonymous by necessity — its caller has no credential yet.
It carries its OWN rate-limiting policy (`RateLimitPolicies.Invitation`, enforced by
`InvitationRateLimitPolicy`), a separate bucket from password reset's, partitioned by the caller's
address exactly the way the login and password-reset limiters are. `[AllowAnonymous]` is on the
ACTION rather than the controller class, because a class-level attribute would outrank action-level
`[Authorize]` on that controller's other actions. The new password is validated by the EXISTING
password policy; no second password policy was introduced.

**This was not always true, and the correction matters.** An earlier draft of this endpoint shared
`PasswordReset`'s bucket, on the reasoning that both are anonymous and both spend a token. That
reasoning does not survive contact with either failure direction: a customer behind shared NAT — an
office, a campus, a carrier-grade NAT on a mobile network — whose neighbours are requesting password
resets would find their one-time invitation link refused with no self-service recovery, because
resending an invitation is an administrator-only action, unlike asking for another reset mail. And
the other way round, an attacker probing invitation tokens from one address would burn the reset
budget of every genuine customer sharing it. `RateLimitPolicies.Invitation`'s own remarks record
both directions in full; the fix was to give the endpoint its own meter before this branch merges,
not after.

## The invited login itself

**Sign in as a login that has not been claimed.** An invited login holds an empty password hash,
which `Argon2idPasswordHasher.Verify` rejects for every candidate, and the login handler refuses any
state other than `Active`. The refusal is byte-identical to a wrong password, so the endpoint does
not reveal which accounts have been set up. **Known limitation, stated rather than hidden:** because
the empty hash already fails verification, the test proving indistinguishability would also pass if
the state check did not exist. The state check is what makes a SUSPENDED login refuse, and that case
is tested directly.

**Keep working after the account is suspended.** Suspension revokes live sessions as well as closing
sign-in — a suspension that only blocked the next attempt would leave whoever is already inside
working until their access token expired. Tested against the session row, not the login state.

**Survive the account's deletion.** Deleting an account removes the login, its invitation tokens,
its reset tokens and its sessions in one transaction. An orphan session would be a live credential
for an account that no longer exists. **Reviewer: the residue AUDITOR — the mechanism that makes a
deletion complete on observed absence — is exercised by direct per-table queries in the module's
tests, not through the auditor itself, because the auditor lives in the Host and is unreachable from
a module test without breaking isolation. A host-level test is the honest home for it.**

## One account, one login

A partial unique index on `User.AccountId` enforces it, filtered so administrators (who hold none)
remain unconstrained. Both handlers that can create a login catch the duplicate-key violation
narrowly — `DbUpdateException` whose inner exception is a Postgres unique violation, nothing wider —
so a concurrent second delivery loses the race quietly rather than surfacing a 500.

A consequence the reviewer should know: an existing test that proved the log-stream budget is keyed
by ACCOUNT rather than by USER could no longer be expressed, because two logins on one account are
now impossible. It was rewritten to two sessions of one login, which is a weaker claim. A
replacement test asserting the partition key directly is owed.

## Two holes a final review found, and closed

A whole-branch review before merge found two authentication holes in the seams between the thirteen
implementation tasks — the kind of defect no single task's own tests could see, because each side of
the seam was correct on its own. Both are now closed; a human reviewer should still read them rather
than take this note's word for it, per the outstanding-review requirement above.

**Two-factor sign-in had no state gate, so suspension was decorative for any customer with 2FA.**
`LoginCommandHandler` refused a login whose `User.State` was not `Active`; `VerifyTwoFactorCommandHandler`
did not check `State` at all, and it is reachable on its own (`POST /api/v1/auth/two-factor` re-checks
the password precisely because it is). An administrator suspending an account revokes its live
sessions (`AccountSuspendingHandler`), but a customer with two-factor enrolled could mint a fresh one
through that endpoint anyway, using their still-correct password and a still-valid TOTP code. Fixed
by introducing `AuthenticationCompleter`, the one place that now answers "may this login sign in" and
issues the session and access token that follow from a yes — both handlers route every session they
issue through it, so a third handler wanting to sign somebody in has no way to obtain a session
without also going through the gate. Closed by
`VerifyTwoFactorCommandHandlerTests.A_suspended_login_is_refused_identically_to_a_wrong_password` and
`.A_suspended_login_issues_no_session_at_all`.

**An invitation accepted on a suspended account activated the login.** `User.Suspend()` is
deliberately a no-op on an `Invited` login, so an unused invitation is never silently consumed — but
nothing retired outstanding tokens on suspension, and `AcceptInvitationCommandHandler` called
`user.Activate(...)` with no check of the account's status. An account created, invited, suspended,
and then opened from the week-old link became a working `Active` login on a suspended hosting
account. Fixed on the domain: `User` now tracks `IsAccountSuspended` independently of `State` (so it
survives an `Invited` login where `State` itself cannot record a suspension), and `Activate` reads it
to come out `Suspended` rather than `Active` when the account was suspended in the meantime — the
password is still stored, so `Resume()` alone returns the owner to a working login with no second
invitation needed. Closed by
`AcceptInvitationCommandHandlerTests.Accepting_an_invitation_on_a_suspended_account_does_not_activate_the_login`
and `.Resuming_the_account_makes_the_already_accepted_login_active`.

## A reset token can now complete an invitation, and what that tells an unauthenticated caller

Added after the rest of this note, closing a deferred finding: `ResetPasswordCommandHandler` did not
look at `User.State` at all. An invited login's owner whose invitation mail was lost — spam-filtered,
an unread inbox past the week the token lives for, simply never delivered — had exactly one
self-service path in front of them, "forgot password", and it led nowhere: the handler happily set a
new password hash on a login stuck in `Invited`, which the login handler refuses regardless of the
password. The account was reachable by no one, permanently, with nothing in the product telling the
customer or the administrator why.

**The fix: a valid reset token completes the invitation.** `ResetPasswordCommandHandler` now branches
on `User.State` after the token is matched — `Invited` calls `User.Activate(...)`, exactly what
`AcceptInvitationCommandHandler` calls, instead of `User.ChangePassword(...)`. The justification is
that presenting a token whose digest matches a live row in `PasswordResetTokens` proves the same
fact an invitation token proves: control of the mailbox the token was mailed to. Nothing about the
CSPRNG draw, the digest comparison, or the single-use enforcement differs between the two token
tables, so accepting the proof here is not a weaker check than the one `AcceptInvitationCommandHandler`
already trusts — it is the identical check.

**What this tells an unauthenticated caller: nothing new.** The rejected alternative was refusing the
reset outright for an `Invited` login, which was rejected specifically because it would have been an
oracle: `RequestPasswordResetCommandHandler` deliberately does the same work and returns the same
answer whether or not an address belongs to anyone (see that type's remarks), so that a caller who
only knows an address cannot learn whether an account exists behind it. A refusal keyed on `State`
inside `ResetPasswordCommandHandler` would not, by itself, have reached that unauthenticated caller —
reaching this branch already requires holding a token that came from a mailbox the request handler
mailed to — but a divergent behavior at completion time is exactly the kind of seam a later change
could widen into a real oracle without anyone noticing, so it was avoided instead of merely justified.
Both branches — `Invited` and every other state — return the identical `Result<bool>.Ok(true)`.
**Reviewer: verify this claim** by reading `ResetPasswordCommandHandlerTests` directly rather than
this paragraph.

**Every outstanding invitation token is retired in the same call.** Without this, an invitation mail
that arrives after the customer has already recovered access through a reset — or one an attacker
intercepted earlier and sat on — would still be live, and `AcceptInvitationCommandHandler` would
accept it and silently overwrite the password the customer just chose. Closed by mirroring the
retire-outstanding-tokens loop `RequestPasswordResetCommandHandler` already runs for its own table,
against `InvitationTokens` instead.

**Audit trail:** completing an invitation this way now writes both a `PasswordChanged` entry (true of
every reset) and an `InvitationAccepted` entry (true of this one), so an operator scanning for who has
accepted their invitation is not told a customer who came in through "forgot password" never did.

Closed by `ResetPasswordCommandHandlerTests.A_reset_for_an_invited_login_activates_it_instead_of_leaving_it_invited`,
`.A_reset_that_completes_an_invitation_retires_its_outstanding_invitation_tokens`, and
`.A_reset_that_completes_an_invitation_is_also_journalled_as_an_invitation_accepted`.

## What the author could not verify

- The completeness of the audit-writer double used in tests: if the production audit entry carries
  free-text fields the double does not model, a token leak into one of them would be invisible to
  the absence test above.
- Any of this under real concurrency against PostgreSQL beyond the integration suite's coverage.
