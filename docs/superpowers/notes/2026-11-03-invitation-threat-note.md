# Sub-user invitation: uniform answer, cap, own rate-limit bucket (issue #341)

**Second review: OUTSTANDING.** This changes how an authenticated customer can probe for other
tenants' logins and how the panel sends mail to addresses a customer typed, so rules/security.md
"Sensitive change escalation" applies. It may land on a branch and MUST NOT merge to `main` until a
second reviewer has read it.

## The surface and the attack it had

`POST /api/v1/me/sub-users` created a login for a typed name and address. A name or address already
held by any login on the server was refused with `SubUserLoginTaken` or `SubUserEmailTaken` (409), a
free one with 201. Any account owner could therefore test any name or mailbox against every tenant of
the server: a cross-tenant enumeration. The same endpoint shared the `invitation` rate-limit bucket
(keyed by client address) with the anonymous acceptance endpoint, so it was bounded by the wrong key
and could starve, or be starved by, people accepting their own invitations. And there was no cap on
how many logins one account could create.

## What changed

1. **One answer.** The handler answers `202 Accepted` with `SubUserInvitationDto { username, email }`,
   an echo of the owner's own input, for a free request and for a taken one. A taken request creates
   nothing and mails nobody. A unique-index violation (a race between the pre-check and the insert) is
   answered the same way. The taken check is one query over both columns, so the work does not depend
   on which column matched.
2. **A cap.** `SubUserOptions.MaxPerAccount` (section `SubUsers`, default 25, `[Range(1, 1000)]`,
   `ValidateOnStart`) counts members of the account in any state (pending included). It is checked
   BEFORE the taken check and depends only on the owner's own team, so refusing on it says nothing
   about the target. Refused with `SubUserLimitReached` (409, en/ru/hy, in `ExpectedErrorStatuses`).
3. **Own bucket.** `RateLimitPolicies.InvitationSending` (`invitation-sending`), keyed by
   `RateLimitPartitionKey` (account, then user, then address), 20 per 3600 s by default
   (`RateLimiting:InvitationSendMaxRequests` / `InvitationSendWindowSeconds`). Acceptance keeps
   `invitation`, keyed by address. The sub-user controller's POST uses the new one.
4. The removed codes `SubUserLoginTaken` and `SubUserEmailTaken` are gone from the resx tables and the
   census: nothing raises them any more.

## What an attacker could try, and why it fails (or what is left)

| Attempt | Outcome |
|---|---|
| Read existence from the status or body | Same status (202), same body shape, same fields (the owner's own input). `A_taken_name_or_address_is_answered_exactly_like_a_free_one`. |
| Read existence from the cap | The cap depends on the owner's own count only; a full team is refused whatever was asked (`A_full_account_is_refused_even_for_a_taken_name`). |
| Read existence from mail | Nothing is mailed to the existing holder of a taken address or name. The invitee receives mail only when the request was honoured. |
| Read existence from timing | Both paths run the same reads and write one journal line; the honoured path also writes the login, membership and token in one transaction and publishes the mail. The difference is one database transaction. NOT closed, see below. |
| Flood invitations to a stranger's mailbox | Per-account `invitation-sending` bucket (default 20/h), then the cap. A free request mails once, to the address the owner typed. |
| Exhaust the acceptance bucket by sending, or the reverse | Separate policies with separate keys. `Sending_invitations_has_its_own_rate_limit_separate_from_acceptance`. |
| Create unlimited logins | Cap, counted including pending invitations. |

## What this does NOT close (stated limits)

- **The owner's own list is an oracle with a delay.** An owner who invites a name and then reads
  `GET /api/v1/me/sub-users` sees whether the row appeared. The brief for this change allows the real
  outcome to travel "to the invited e-mail or the owner's later view", so this is accepted, and it is
  bounded rather than removed: by the sending bucket (names testable per hour), the cap (rows that can
  be left pending) and by cleanup — a tester must also remove each probe row. Removing the channel
  needs a consenting join flow for existing logins, which does not exist.
- **Timing.** The honoured path costs one more transaction than the refused one. A measuring owner
  behind the bucket's allowance (20/h) gets 20 samples an hour per account. A real fix equalises the
  work (for example doing the write on a queue for both paths); it was not done here because the
  Wolverine local queue is not durable and a durable one would persist the token-bearing envelope
  (rules/csharp.md "a message carrying a secret is never durable").
- **The audit log records the real outcome** (`SubUserInvited`, `succeeded: false`). It is
  administrator-only (`AuditController`, `AdminOnly`); if a customer-facing audit view is ever added it
  must not show this field.
- The admin `ResendInvitation` and `IssueInvitationLink` actions are unchanged: they take an account
  id the administrator already holds, so they disclose nothing across tenants. They stay on the `api`
  bucket.

## What a reviewer must check

- That no code path in `InviteSubUserCommandHandler` returns anything other than the one DTO (or an
  owner-account refusal) for a taken request, including exceptions: only the unique-violation
  `DbUpdateException` is caught; any other failure is a 500 for both paths alike.
- That `RateLimitPartitionKey` really yields the account for a customer token (the policy file relies
  on it, `RateLimitPartitionKeyTests`).
- That the SPA never branches on the response beyond success/failure
  (`SubUsersPage.invite`, `useSubUsersApi.invite`).

## What the author could not verify

Nothing was built or run (the working tree is shared and the owner's rule is code first, one gate
later): the new unit and integration tests, `maran structure`, the api-contract baseline and the test
baseline counts (`scripts/test-baseline.txt`, which gained tests in `Maran.Modules.Identity.Tests` and
`Maran.Host.IntegrationTests` and lost none) are all unrun. The Playwright spec was edited for the
202 answer but not executed, and no click-through of the invite form was done.
