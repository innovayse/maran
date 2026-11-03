# Sub-users: resending an invitation and offering membership to an existing login (issue #328)

**Second review: OUTSTANDING.** This adds a way to put a new person on the list of people who may act
for a hosting account (a consent flow addressed to a login that already exists), and a second way to
mint an invitation token, so rules/security.md "Sensitive change escalation" applies. It may land on a
branch and MUST NOT merge to `main` until a second reviewer has read it.

This note is written before the code (rules/security.md): it states what the change MUST do. Where
the text below says "the handler does X" it is a requirement the reviewer checks against the code
that follows, not a description of code that already exists.

## What changes

1. **Resend.** `POST /api/v1/me/sub-users/{id}/invitation`. The owner of the account re-sends the
   invitation of a team member whose login is still `Invited`. The existing `InvitationIssuer.MintAsync`
   retires the outstanding tokens and mints a new one; the mail goes to the address already stored on
   the login, never to one typed in the request.
2. **Offer.** `POST /api/v1/me/sub-users/existing`. The owner types ONE identifier (a login name or an
   e-mail address) and the set of features to grant. If it names a login that qualifies, a row is
   stored in a new table, `identity.AccountMembershipOffers`, and a plain notification mail is queued.
   The person signs in, sees the offer on their own page (`GET /api/v1/me/membership-offers`) and
   accepts or declines it (`POST .../{id}/accept`, `.../{id}/decline`). Only an accept creates an
   `AccountMembership` (`AccountMembership.Admit`) with the offered grants.
3. **Why a table and not a pending membership.** `MembershipState` stays `Active | Suspended`.
   `Account.Resume` resumes every membership of the account, so a `Pending` state would silently
   activate an unconsented row, and every reader of the state (sign-in, refresh, switcher, the central
   request guard) would need re-auditing. A separate table is read only by the offer endpoints, so a
   mistake in it cannot grant access.

## Who may be offered, and the rules the handler applies

An offer is created only when ALL hold, and otherwise the request is a silent no-op that is answered
exactly like a success:

- the identifier matches a login (one query over login name and e-mail, so the work does not depend on
  which column matched, as in #341);
- that login is `UserRole.Customer` and `UserLifecycle.Active` (an administrator, a reseller, an
  `Invited` login and a deactivated one are all no-ops);
- it is not already an owner or member of this account (any `MembershipState`);
- the account exists and is not suspended;
- no open offer exists for this (login, account) pair.

The caller's own checks come first and say nothing about the target: the owner guard
(`SubUserOwnerGuard`: the caller holds an `Owner` membership of the account their token speaks for),
the team cap, and the request validator. **Open offers count toward `SubUserOptions.MaxPerAccount`**,
checked before the target is looked at, so an owner cannot park unlimited offers and the refusal
depends only on the owner's own team.

## What an attacker could try, and why it fails (or what is left)

| Attempt | Outcome |
|---|---|
| Use the offer form to learn whether a login name or an address exists on the server | One answer: `202 Accepted`, the same body (an echo of what the owner typed), for a login that exists, one that does not, and every no-op above. The SPA says "If that login exists, an offer was sent". The cap and the owner guard are decided before the target is read. **Not fully closed: see "What this does NOT close".** |
| Read existence from the mail | Nothing is sent to anybody unless an offer was stored. A request that is a no-op mails nobody. |
| Aim the mail at a third party by typing an address | The mail goes to the stored address of the login the identifier resolved to, never to the typed text. Typing a stranger's address that is not a login mails nobody. |
| Add someone to the team without their consent | Nothing the owner can do creates a membership. Only `AcceptMembershipOffer`, called with a token whose user IS the offered user, does. A foreign, expired, resolved or unknown offer id answers 404, indistinguishably. |
| An operator, viewing as the person, accepts for them | The invitee endpoints carry `[ViewAsAccess(ViewAsAccess.Refused)]` (and `[RefusedWhileViewingAs]`): a view-as token is refused. The owner-side actions are refused while viewing-as for the same reason (they create a credential or an access path). |
| Steal the offer by intercepting the mail | The mail carries no token and no link secret: it names the account and says to sign in. Interception yields nothing that acts as a credential. Consent happens inside an authenticated session. |
| Offer a role the owner should not control (an administrator, a reseller) | Refused by the `Customer + Active` filter, silently. An administrator therefore cannot be pulled onto a customer team, and the no-op is not distinguishable from "no such login". |
| Grant more than the plan allows | The offer stores a set of `AccountFeature` bits only. Use-time gating is unchanged: the plan AND the person's grants must both include the feature (`RequiresAccountFeatureFilter`), so a plan that later loses a feature takes it from the member too. |
| Offer to a login, then keep the offer alive forever | Offers expire after 14 days. An expired offer reads as absent (404 on accept, absent from the list) and can no longer be accepted. |
| Accept after the account was suspended or deleted | Accept refuses a suspended account (`MembershipOfferAccountSuspended`, 409) and re-reads the account and the existing memberships inside the same transaction. Deleting the account releases its open offers (an `AccountDeleting` handler, counted among the sanctioned `IgnoreQueryFilters` bypasses). |
| Accept twice, or accept in parallel | Accepting resolves the offer and inserts the membership in one transaction. A partial unique index on (login, account) where `ResolvedAt` is null, and the existing unique membership key, make the second attempt fail; it answers 404, not a second membership. |
| A member offers membership to more people, or resends | Both actions start at `SubUserOwnerGuard`: a member or an administrator gets 403 `SubUsersOwnerOnly`. |
| Owner A resends, cancels or reads account B's invitation or offer (IDOR) | The account is never a parameter; it is the caller's token. A foreign id is 404 `SubUserNotFound`, for resend and for cancel (`DELETE {id}` also cancels an open offer, addressed by the offered login's id). |
| Resend to mail-bomb a stranger | The address is the one stored on the invited login, typed once by the owner at invitation time. Resend shares the per-account `invitation-sending` bucket (20 per hour by default, keyed by `RateLimitPartitionKey`) with invite and offer. A resend retires the previous token, so at most one link is live. |
| Resend to an `Active` login | 409 `SubUserNotPending`. This discloses only the state of the owner's own team member. |
| Resend or offer on a suspended account | Refused (`InvitationAccountSuspended` for resend, a no-op for offer). |
| Role, account or grants forged through the body | `IpAddress`, `UserAgent` and the route `UserId` are `[JsonIgnore]` plus both `[BindNever]` forms, stamped by the action; the account comes from the token (rules/csharp.md). |

## What this does NOT close (stated limits)

- **The owner's own list is an oracle with a delay, and this change makes it a wider one, not a
  narrower one.** A stored offer appears in `GET /api/v1/me/sub-users` as a row with `State = Invited`;
  a no-op leaves no row. An owner who submits an identifier and then reads the list therefore learns
  whether it named a qualifying login. The identity of that row is only what the owner typed (the real
  login name appears after acceptance), but its existence is the fact the uniform answer hides. It is
  bounded, not removed: by the sending bucket (20 identifiers an hour per account), by the cap (every
  probe that hits parks an offer, and a tester must also cancel each one to keep probing), and by the
  fact that it only reveals qualifying logins (`Customer` and `Active`), not every name on the server.
  Closing it would mean showing a row for EVERY submission and storing a void offer for a typed string
  that matches nobody; that stores strangers' text and changes what "pending" means, so it is an owner
  decision and is not made here.
- **The login holder learns that an owner of account X typed their name.** That is the point of the
  flow (the offer names the account), and it is limited to qualifying logins. An owner can therefore
  send unsolicited mail to a real person who has a login on the same server. Bounds: one open offer per
  (login, account); the hourly bucket; the cap; an expiry; and the recipient can decline. There is no
  per-recipient limit across different accounts: several owners can each mail the same person once.
- **Timing, the residual.** The honoured path and the no-op path must perform the same reads in the
  same order (login lookup by one query over both columns, membership check, account load, open-offer
  check) and end with one journal row. They are not equal: the honoured path then inserts an offer
  row, commits it and hands a rendered mail to the bus, while the no-op path writes nothing and
  publishes nothing. The same technique as #341 narrows this: the no-op path stages a decoy offer in
  the same transaction, rolls it back, and journals on its own, so the statement sequence is alike. What
  remains, as in #341: the mail is an in-memory enqueue on one side and a dropped render on the other
  (publishing it would mail a stranger); a commit writes more log than a rollback and is dearer in lock
  and visibility bookkeeping. These are well below network jitter on one request; a patient measurer
  could average them, at 20 samples an hour per account. This note does not claim equality.
- **The audit log records the real outcome** (`SubUserMembershipOffered`, succeeded or not). It is
  administrator-only. The entry holds what the owner typed, which may be a stranger's address; the
  journal decides what to keep (`IdentityAuditJournal`), and a customer-facing audit view, if one is
  ever added, must not show the outcome.
- **Sites, SSL, PHP and tasks are not restrictable per member** (a product decision, unchanged by this
  issue). An accepted member reaches them whatever the grants say.
- **A member added by acceptance is not suspended with the owner's login** unless the account is
  suspended, which suspends every membership (`2026-11-03-membership-suspension-threat-note.md`).

## What a reviewer must check

- That every path out of `OfferMembershipCommandHandler` that is not a refusal by the owner's own state
  (not an owner, cap reached, invalid input) returns the one DTO, including: a unique-index race on
  the offer insert, an expired unresolved offer for the same pair, and an exception in rendering the
  mail. Only the unique violation may be caught; anything else is a 500 on both paths alike.
- **The expired-offer collision.** The partial unique index is on (login, account) where `ResolvedAt`
  is null. An offer that expired and was never resolved still has `ResolvedAt` null, so a fresh offer
  to the same login would collide. The handler (or the index) must treat an expired offer as absent;
  check that an expired row is resolved before a new one is inserted, and that this does not change
  the answer.
- That no code can set `Accepted` without `AccountMembership.Admit` in the same transaction, that
  `Accept` compares the caller's user id with the offer's user id, and that the membership is created
  from the offer's stored grants and never from the request.
- That the mail contains no token, no link secret and no owner address, and that the account name
  written into it passes the mail-header text rule (`MailHeaderTextRule`); that it is published after
  the commit, and by a non-durable message or one that carries nothing that acts as a secret.
- That every new action on `SubUsersController` and `MembershipOffersController` declares its view-as
  access (`ViewAsAccessDeclarationTests`) and that the invitee endpoints are refused for a view-as
  token and answer an accountless customer (every membership removed) sensibly rather than 500.
- That resend and offer use `RateLimitPolicies.InvitationSending`. The working plan names
  `RateLimitPolicies.Invitation`, which after #341 is the anonymous acceptance bucket keyed by address;
  using it would put the owner back on the wrong key.
- That `AccountMembershipOffer` is covered by the tenant census: either a query filter on the account
  (it is also the invitee's row, so an account filter must not hide it from the invitee) or a named
  exemption in `TenantScopeTests` with the reason, and an IDOR test for each endpoint.
- That the account-deletion path (`AccountDeletingHandler` and `ModuleAccountResidueAuditor`) leaves no
  offer behind, and that the bypass count in rules/README.md is regenerated with its command if a new
  `IgnoreQueryFilters` is added.
- That the migration is expand-only (a new table, nothing dropped, nothing narrowed), `maran migrate
  guard` passes, and table, column, index and constraint names are PascalCase.
- That `ListSubUsers` returns a correct total and page boundary for a mixed set of members and offers,
  and that an offer row carries only what the owner typed.
- That the resx triples (en, ru, hy) hold every new code and every `EmailTemplates` key, that the error
  kinds are right (`SubUserNotPending` Conflict, `MembershipOfferNotFound` NotFound,
  `MembershipOfferAccountSuspended` Conflict) and that an expired offer answers NotFound, not Conflict.

## What the author could not verify

Nothing was built or run when this was written (the working tree is shared and the owner's rule is code
first, one gate later), and at the time of writing the code described does not exist yet. Specifically
unverified:

- That the decoy-and-rollback technique of #341 can be applied to an offer insert without a visible
  difference in statement order, and how large the remaining timing gap is. No measurement was taken.
- That the unique index plus the expired-offer rule behave as described under real PostgreSQL
  (including 13, the panel's floor) under parallel offers and parallel accepts.
- That the number of reads in the honoured and no-op paths is equal in the finished handler.
- The behaviour of the invitee endpoints for a login whose token names an account it has just left, or
  that has no membership at all.
- The Russian and Armenian mail and screen text (written against the existing vocabulary, not reviewed
  by a speaker), and the three-language Playwright run of the add-an-existing-login flow.
- `maran structure`, the Sdk and Architecture censuses, the api-contract baseline and the test baseline
  counts are all unrun for this change.

The owner is told at hand-off that this surface carries an outstanding second review.
