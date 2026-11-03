# Sub-users: resending an invitation and offering membership to an existing login (issue #328)

**Second review: OUTSTANDING.** This adds a way to put a new person on the list of people who may act
for a hosting account (a consent flow addressed to a login that already exists), and a second way to
mint an invitation token, so rules/security.md "Sensitive change escalation" applies. It may land on a
branch and MUST NOT merge to `main` until a second reviewer has read it.

This note was written before the code (rules/security.md) and has been revised against the code that
landed on this branch, so that it describes what was built (a document describing code that does not
exist is a defect, rules/architecture.md). Every requirement the code meets is kept; where the code
differs from what the note first required, "Deviations found when the code landed" says so plainly.
Nothing here was run: see "What the author could not verify".

## What changes

1. **Resend.** `POST /api/v1/me/sub-users/{id}/invitation`. The owner of the account re-sends the
   invitation of a team member whose login is still `Invited`. The existing `InvitationIssuer.MintAsync`
   retires the outstanding tokens and mints a new one; the mail goes to the address already stored on
   the login, never to one typed in the request.
2. **Offer.** `POST /api/v1/me/sub-users/existing`. The owner types ONE identifier (a login name or an
   e-mail address) and the set of features to grant. If it names a login that qualifies, a row is
   stored in a new table, `identity.AccountMembershipOffers`, and a plain notification mail is queued.
   The offer stores the offered login's id, the account id, the account's name as the Accounts
   directory reads it (`AccountName`), what the owner typed (`Identifier`), the offered grants, and
   `CreatedAt`, `ExpiresAt` (14 days) and, once answered, `ResolvedAt` and an `Outcome` of
   `Accepted`, `Declined`, `Cancelled` or `Expired`. The person signs in, sees the offer on their own
   page (`GET /api/v1/me/membership-offers`) and accepts or declines it
   (`POST /api/v1/me/membership-offers/{id}/accept`, `.../{id}/decline`). Only an accept creates an
   `AccountMembership` (`AccountMembership.Admit`, called from `AccountMembershipOffer.Accept`) with
   the offered grants.
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
the team cap, and the request validator. **Open offers count toward `SubUserOptions.MaxPerAccount`**
(members with the `Member` role in any state, plus offers that are unresolved and unexpired),
checked before the target is looked at, so an owner cannot park unlimited offers and the refusal
depends only on the owner's own team.

## What an attacker could try, and why it fails (or what is left)

| Attempt | Outcome |
|---|---|
| Use the offer form to learn whether a login name or an address exists on the server | One answer: `202 Accepted`, the same body (`MembershipOfferRequestedDto`, an echo of what the owner typed), for a login that exists, one that does not, and every no-op above. The cap and the owner guard are decided before the target is read. **Not fully closed: see "What this does NOT close".** |
| Read existence from the mail | Nothing is sent to anybody unless an offer was stored. A request that is a no-op mails nobody. |
| Aim the mail at a third party by typing an address | The mail goes to the stored address of the login the identifier resolved to, never to the typed text. Typing a stranger's address that is not a login mails nobody. The mail for a request that did not qualify is rendered to a random address under `.invalid` and dropped, never published. |
| Add someone to the team without their consent | Nothing the owner can do creates a membership. Only `AcceptMembershipOffer`, called with a token whose user IS the offered user, does. A foreign, expired, resolved or unknown offer id answers 404, indistinguishably. |
| An operator, viewing as the person, accepts for them | The invitee endpoints carry `[ViewAsAccess(ViewAsAccess.Refused)]` (and `[RefusedWhileViewingAs]`): a view-as token is refused. The owner-side actions are refused while viewing-as for the same reason (they create a credential or an access path). |
| Steal the offer by intercepting the mail | The mail carries no token and no link secret: it names the account (only when the name passes `MailHeaderTextRule`; otherwise a template that names none) and says to sign in. It holds no owner address. Interception yields nothing that acts as a credential. Consent happens inside an authenticated session. |
| Offer a role the owner should not control (an administrator, a reseller) | Refused by the `Customer + Active` filter, silently. An administrator therefore cannot be pulled onto a customer team, and the no-op is not distinguishable from "no such login". |
| Grant more than the plan allows | The offer stores a set of `AccountFeature` bits (`Grants`), and the membership is built from those stored grants, never from the accept request. Use-time gating is unchanged: the plan AND the person's grants must both include the feature (`RequiresAccountFeatureFilter`), so a plan that later loses a feature takes it from the member too. |
| Offer to a login, then keep the offer alive forever | Offers expire after 14 days. An expired offer reads as absent (404 on accept, absent from the list) and can no longer be accepted. |
| Accept after the account was suspended or deleted | Accept takes the account's team lock, then reads the account's `Owner` membership: none means the account is gone (404, `MembershipOfferNotFound`), a suspended one is refused (`MembershipOfferAccountSuspended`, 409). It also re-reads whether the caller already holds a membership, inside the same transaction. Deleting the account removes every offer of it, answered or not, in `AccountDeletingHandler`, before anything else it does (see Deviations: this adds NO `IgnoreQueryFilters` bypass). |
| Accept twice, or accept in parallel | Accepting resolves the offer and inserts the membership in one transaction. A resolved offer is no longer open, so a second accept reads as absent (404); the existing unique membership key refuses a racing insert, and the handler catches only that unique violation and answers 404. The partial unique index `UX_AccountMembershipOffers_UserId_AccountId_Open` on (login, account) where `ResolvedAt` is null is what stops two OPEN offers for one pair. |
| A member offers membership to more people, or resends | Both actions start at `SubUserOwnerGuard`: a member or an administrator gets 403 `SubUsersOwnerOnly`. |
| Owner A resends, cancels or reads account B's invitation or offer (IDOR) | The account is never a parameter; it is the caller's token. A foreign id is 404 `SubUserNotFound`, for resend and for cancel (`DELETE {id}` also cancels an open offer; the id is the OFFER's id, because an offer row listed in the owner's team carries no login id: the login it names is the owner's own typed text). The offer lookup is scoped to the caller's account in the query itself. |
| Resend to mail-bomb a stranger | The address is the one stored on the invited login, typed once by the owner at invitation time. Resend shares the per-account `RateLimitPolicies.InvitationSending` bucket (`InvitationSendMaxRequests` 20 per `InvitationSendWindowSeconds` 3600 by default, keyed by `RateLimitPartitionKey`) with invite and offer. A resend retires the previous token, so at most one link is live. |
| Resend to an `Active` login | 409 `SubUserNotPending`. This discloses only the state of the owner's own team member. |
| Resend or offer on a suspended account | Refused (`InvitationAccountSuspended` for resend, a no-op for offer). |
| Role, account or grants forged through the body | `IpAddress`, `UserAgent` and the route `UserId` are `[JsonIgnore]` plus both `[BindNever]` forms, stamped by the action; the account comes from the token (rules/csharp.md). |

## What this does NOT close (stated limits)

- **The owner's own list is an oracle with a delay, and this change makes it a wider one, not a
  narrower one.** A stored offer appears in `GET /api/v1/me/sub-users` as a row with `State = Invited` and
  `IsOffer = true` (the union leaves the login's lifecycle null; `SubUserRow.State` reads null as
  Invited); a no-op leaves no row. An owner who submits an identifier and then reads the list therefore
  learns whether it named a qualifying login. The identity of that row is only what the owner typed
  (the row's id is the offer's id, and the real login name appears after acceptance), but its existence is the fact the uniform answer hides. It is
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
  publishes nothing. The same technique as #341 narrows this, and the handler does it: the no-op path stages a
  decoy offer addressed to the owner's own login (which exists, so the foreign key holds, and never has
  an offer to its own account, so the unique index never fires), renders the mail to a random
  `.invalid` address, rolls the transaction back, and journals on its own, so the statement sequence is
  alike. An expired unresolved offer, which only a real login can have, adds one extra statement to the
  honoured path alone. What
  remains, as in #341: the mail is an in-memory enqueue on one side and a dropped render on the other
  (publishing it would mail a stranger); a commit writes more log than a rollback and is dearer in lock
  and visibility bookkeeping. These are well below network jitter on one request; a patient measurer
  could average them, at 20 samples an hour per account. This note does not claim equality.
- **The audit log records the real outcome** (`SubUserMembershipOffered`, succeeded or not; the
  constants `SubUserMembershipOffered`, `SubUserMembershipOfferAccepted` and
  `SubUserMembershipOfferDeclined` live in `IdentityAuditActions`, `Maran.Sdk/Contracts`). It is
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
  `Accept` compares the caller's user id with the offer's user id (it does so in the query, so a
  foreign offer is absent), and that the membership is created from the offer's stored grants and never
  from the request. `AccountMembershipOffer.Accept` is the only code that does both.
- That the mail contains no token, no link secret and no owner address, and that the account name
  written into it passes the mail-header text rule (`MailHeaderTextRule`); that it is published after
  the commit (it is: `PublishAsync` follows `CommitAsync`), and that the message carries nothing that
  acts as a secret (it carries none; the code does not choose a non-durable route, so a durable queue
  would keep a mail with no credential in it). The resend mail is different and pre-existing: it
  carries an invitation token for one hop.
- That every new action on `SubUsersController` and `MembershipOffersController` declares its view-as
  access (`ViewAsAccessDeclarationTests`) and that the invitee endpoints are refused for a view-as
  token and answer an accountless customer (every membership removed) sensibly rather than 500.
- That resend and offer use `RateLimitPolicies.InvitationSending`. The working plan names
  `RateLimitPolicies.Invitation`, which after #341 is the anonymous acceptance bucket keyed by address;
  using it would put the owner back on the wrong key.
- That `AccountMembershipOffer` is covered by the tenant census: it is a named exemption in
  `TenantScopeTests` (no query filter, because it is also the invitee's row and an account filter would
  hide it from the invitee) with the reason, every invitee read compares the caller's own user id, and
  the owner-side reads are scoped to the token's account. Check that an IDOR test exists for each
  endpoint.
- That the account-deletion path (`AccountDeletingHandler` and `ModuleAccountResidueAuditor`) leaves no
  offer behind. The handler removes the account's offers before its early return and saves them; check
  that the early return still saves them.
- That the migration is expand-only (a new table, nothing dropped, nothing narrowed), `maran migrate
  guard` passes, and table, column, index and constraint names are PascalCase. **At the time of this
  revision the migration and the model snapshot are not part of the branch** (the entity and its
  configuration are; the integrator generates the migration), so this item cannot be checked from the
  diff yet. Check the indexes `UX_AccountMembershipOffers_UserId_AccountId_Open` (filtered on
  `"ResolvedAt" IS NULL`), `IX_AccountMembershipOffers_UserId`, `IX_AccountMembershipOffers_AccountId`
  and the cascading foreign key `FK_AccountMembershipOffers_Users_UserId` are all in it.
- That `ListSubUsers` returns a correct total and page boundary for a mixed set of members and offers
  (it is `members.Concat(offers)` in one LINQ query, searched with `ILike` and paged by
  `ToPagedAsync`), and that an offer row carries only what the owner typed: its username and email
  are both the identifier, its id is the offer's id.
- That the resx triples (en, ru, hy) hold every new code and every `EmailTemplates` key, that the error
  kinds are right (`SubUserNotPending` Conflict, `MembershipOfferNotFound` NotFound,
  `MembershipOfferAccountSuspended` Conflict) and that an expired offer answers NotFound, not Conflict.

## Deviations found when the code landed

Written against the code, after it. Each item is a place where the code differs from what this note
first said, or where the first text was wrong.

1. **No new `IgnoreQueryFilters` bypass.** The first text said the `AccountDeleting` handler would be
   counted among the sanctioned bypasses. `AccountMembershipOffer` has no query filter (it is exempt in
   `TenantScopeTests`), so the handler reads it plainly and the count in rules/README.md does not
   move. The only `IgnoreQueryFilters` calls the branch adds are in a test fixture
   (`SubUserTeamWorld`), which tests are permitted.
2. **The cancel and remove id is the offer id.** The first text said an offer is addressed "by the
   offered login's id". An offer row never carries that id, so `DELETE {id}` falls back to an offer of
   the caller's account whose id is `{id}` when `{id}` is not a member. The offer cancelled this way is
   recorded under the existing `SubUserRemoved` action with the typed identifier as subject; there is no
   separate cancel action. A cancel does not check the expiry, so an expired unresolved offer can also
   be cancelled (harmless: it is closed either way).
3. **Accept's account check reads the owner's membership, not the account.** The first text said accept
   re-reads the account. It takes the team lock and reads the `Owner` membership of the offer's
   account: absent is 404, suspended is 409. A refused accept, decline or cancel is journaled with
   the id the caller named.
4. **Accept does not mark an expired offer `Expired`.** An expired unresolved offer is simply absent to
   accept, decline and the list (404 or not listed). Only the offer handler closes one (`Expire`),
   when it would otherwise collide with a fresh offer for the same pair. The outcome set is
   `Accepted`, `Declined`, `Cancelled`, `Expired`; the entity guards each transition.
5. **No foreign key to the account.** Accounts live in another module's schema, so the table has a
   cascading foreign key to the offered login only; `AccountDeletingHandler` is what removes an
   account's offers.
6. **The offer stores two texts the first text did not name:** `AccountName` (the account's name as the
   directory reads it, shown to the invitee, fitted to 256) and `Identifier` (what the owner typed,
   fitted to 254). An account that cannot be read is stored with an empty name and its mail uses the
   template that names no account.
7. **Statement order, as built.** Owner guard; account read from the directory; the team lock; member
   count; open-offer count; the cap check (refusal rolls back and journals); one query over login name
   and email; membership check; open-offer check (using the empty id when there is no login); expiry of
   a stale offer; stage an offer (real or decoy) and save; render the mail; then commit and publish, or
   roll back and journal. The account read is outside the transaction. Equality of this sequence in the
   two paths is by construction and has not been measured.
8. **Rate limit policy.** Resend, invite and offer use `RateLimitPolicies.InvitationSending` (confirmed
   in `SubUsersController`), not `RateLimitPolicies.Invitation`.

## What the author could not verify

Nothing was built or run (the working tree is shared and the owner's rule is code first, one gate
later), for the code as it now stands. Specifically unverified:

- That the union in `ListSubUsers` (a members query concatenated with an offers query whose lifecycle
  is a nullable constant `null`, then `ILike` search and the shared sort map) translates to SQL under
  EF Core and PostgreSQL, and that its total and page boundaries are right. It has unit tests that
  have not been run.
- That the migration and snapshot exist and match the entity (they are not in the branch yet).

- How large the remaining timing gap of the decoy-and-rollback technique is. It is applied (see
  Deviations item 7), but no measurement was taken.
- That the unique index plus the expired-offer rule behave as described under real PostgreSQL
  (including 13, the panel's floor) under parallel offers and parallel accepts.
- That the number of statements in the honoured and no-op paths is equal at run time (the handler's
  code makes them equal by construction, apart from the expired-offer statement; no statement log
  was compared).
- The behaviour of the invitee endpoints for a login whose token names an account it has just left, or
  that has no membership at all.
- The Russian and Armenian mail and screen text (written against the existing vocabulary, not reviewed
  by a speaker), and the three-language Playwright run of the add-an-existing-login flow.
- `maran structure`, the Sdk and Architecture censuses (including the `TenantScopeTests` exemption),
  the api-contract baseline and the test baseline counts are all unrun for this change.

The owner is told at hand-off that this surface carries an outstanding second review.
