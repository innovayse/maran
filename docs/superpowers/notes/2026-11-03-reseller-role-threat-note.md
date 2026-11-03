# Reseller role and the admin, reseller, account view-as edges (issues #179, #314, #317) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
adds a fifth kind of caller, puts a new role and a new claim into the access token, and lets two more
kinds of signed-in user obtain a token that speaks for ANOTHER user. It touches token claims,
session handling and impersonation, so it needs a second reviewer, and no agent session can dispatch
one. Written BEFORE any code exists, per that rule, so the reviewer reads an argument instead of a
diff. Every sentence below about what the code does is a **requirement the code must meet**, not a
description of code already written; a reviewer's first job is to confirm each one against the diff.

The branch may land on its issue branch and on `dev` through a pull request; it **MUST NOT merge to
`main`** until a human reviewer has discharged this note, or the owner has recorded the beta
exception for a named pre-release in writing (rules/security.md "The one recorded exception"). The
owner is told at hand-off that these surfaces carry an outstanding review: the access-token claim
set, the `Caller` hierarchy, the view-as start endpoints, the Accounts query filters.

## Owner decisions this note rests on

These are final (the owner, 2026-11-03). Code that departs from one is a defect, not a design choice.

- **D1 — view-as is ONE hop per token.** The allowed edges are exactly: administrator to reseller,
  administrator to any account, reseller to one of its OWN accounts. There is no chaining and no
  nesting: no token produced by a view-as can start another view-as. `[RefusedWhileViewingAs]` stays
  on every view-as start endpoint. An administrator who is viewing as a reseller presses Back and
  then views the account directly; an administrator can already see every account, so a second hop
  would add no authority and would cost a parent-session stack the verifier would have to walk.
- **D2 — suspending a reseller does NOT suspend its customers' accounts.** It suspends the reseller's
  LOGIN and refuses the reseller's mutations. Accounts are suspended separately by an administrator.
- **D4 — two-factor is mandatory for resellers.** A reseller can start view-as into customers, so its
  login is a privileged one.

Other plan decisions stand as recommended: D3 (a reseller uses only its own plans), D5 (envelope is
per-plan ceilings plus two aggregates), D7 (the Accounts query filters are narrowing-only), D8
(reseller credentials never reach other tenant modules).

## The surface

- **A new role**, `UserRole.Reseller`, and a new principal case, `ResellerCaller`
  (`Maran.SharedKernel/Callers/ResellerCaller.cs`), carrying the reseller id. A member exists only on
  the case that has it, which is why a reseller is not a `CustomerCaller` with a made-up account id.
- **A new access-token claim**, `reseller`, beside role `Reseller`. `HttpContextCurrentUser` builds
  `ResellerCaller` from them.
- **Two new authorization policies**, `ResellerOnly` and `AdminOrReseller`, and a new module
  audience value `AdminOrReseller`.
- **Accounts module**: `accounts.Resellers` (name, contact, status, envelope), `Account.ResellerId` and
  `Plan.ResellerId` (null means administrator-owned), `ResellersController` (admin only),
  `ResellerController` (`GET me`, reseller only), and `AccountsController` / `PlansController`
  widened from `AdminOnly` to `AdminOrReseller` with narrowing done by the query filters, not by each
  handler.
- **Identity module**: the reseller's login (`Users.Role = Reseller`, `Users.ResellerId`), four
  integration-event handlers that create, suspend, resume and delete that login, and
  `ForceTwoFactorForResellers` on the security policy.
- **View-as**: `POST /api/v1/view-as` (account) widens from `AdminOnly` to `AdminOrReseller`; a new
  `POST /api/v1/view-as/reseller` is `AdminOnly`. Both carry `[RefusedWhileViewingAs]`. `GET` and
  `DELETE /api/v1/view-as` stay as they are. `ViewingAsDto` gains a `kind` (`account` or `reseller`)
  and a subject id, and loses the account-only `accountId`, so one field has one meaning.
- **Migrations**, expand-only: `accounts.Resellers`, nullable `ResellerId` on `Accounts` and `Plans`,
  per-owner unique plan names, `Users.ResellerId`, `SecurityPolicies.ForceTwoFactorForResellers` with a
  default. No backfill: every existing row reads back as administrator-owned, which is today's meaning.

This note extends, and does not replace, `2026-11-02-view-as-impersonation-threat-note.md` and
`2026-11-03-view-as-allow-list-threat-note.md`; every property those notes establish must still hold.

## Assets

1. **The reseller login.** It can create accounts and plans, and can start a view-as into every
   account it owns. Taking it over takes every account the reseller owns.
2. **View-as sessions** of both kinds. A token that speaks for another login is the strongest bearer
   credential the panel mints.
3. **The isolation between resellers.** Reseller A must not read, change, enumerate or impersonate
   anything of reseller B, nor learn that it exists beyond what a server-global namespace forces.
4. **The envelope.** What the administrator sold a reseller (accounts, disk pool, per-plan ceilings,
   features) is the commercial limit; a reseller exceeding it is a billing and capacity breach.
5. **The administrator's authority.** An administrator who is viewing as a reseller must not keep
   administrator power, and a reseller must never gain it.

## What an attacker could try, and the requirement that stops it

Each item states the attack, then the requirement the code MUST meet, then the evidence the reviewer
should demand.

1. **Reseller A names reseller B's account, plan or id on any endpoint.** Requirement: the Accounts
   context carries narrowing query filters for `Account`, `Plan` and `Reseller` keyed on the caller's
   reseller id, so a handler that forgets a `Where` still cannot return B's row; `AccountDirectory`
   (the window other modules and view-as use) narrows the same way. The answer is **404**, identical to
   a random GUID, never 403 (rules/security.md item 6). 403 is used only for a ROLE refusal of an
   endpoint (a reseller calling `POST /resellers`), which discloses nothing about a tenant. Evidence:
   `ResellerIsolationTests` covers get, list, suspend, reactivate, change-plan, change-owner-email,
   delete, resource-summary and view-as against B's account, and get, put and delete on B's plan;
   creating an account on B's plan answers 404 `PlanNotFound`; every list excludes B's rows, with a
   positive control that A sees its own.
2. **Forge the `reseller` claim.** Requirement: the claim lives inside the HMAC-signed token, so it
   cannot be edited. A reseller token with the role but no claim, or with a claim that is not a GUID,
   is `UnrecognisedPrincipalException` (fail closed), exactly as the account claim is today. On a
   view-as start the actor and the target are re-read from the database, never taken from the token.
3. **Role escalation through view-as.** Requirement, in three parts. (a) A view-as token carries the
   VIEWED login's role: viewing as a reseller yields role `Reseller`, so every `AdminOnly` endpoint
   refuses it, and **an administrator viewing as a reseller keeps no administrator power**. (b) A
   reseller viewing as one of its customers yields role `Customer` and gains nothing it did not have
   over that account. (c) **No chaining (D1):** `[RefusedWhileViewingAs]` on both start endpoints, and
   `ViewAsBoundaryRequirement` on both panel policies, refuse any further start from a view-as token.
   Evidence: `ViewAsChainEndpointTests` exercises every allowed edge AND every non-edge (a customer, an
   anonymous caller, a reseller starting a reseller view, a view-as token starting either kind).
4. **The token issuer produces a token that disagrees with its row.** Requirement:
   `JwtAccessTokenIssuer.IssueViewAs` branches on the viewed login's role. Customer: requires the
   session's active account and writes `account`. Reseller: requires NO active account and a
   `ResellerId` on the viewed user, and writes role `Reseller` plus `reseller`. Any mismatch THROWS: a
   token is the one thing that must not be produced on a bug. `ViewAsSessionVerifier` additionally
   asserts the same target-kind consistency on every request. No forced-two-factor steering on a
   view-as token, as today.
5. **A reseller starts a view-as into an account it lost or never owned.** Requirement: the account is
   resolved through `IAccountDirectory.FindAsync` under the actor's own request caller, which for a
   reseller is narrowed to its accounts, on EVERY start; not found, not owned and no-owner-login are
   one 404 (`ViewAsAccountNotFound`). The one distinguishable refusal is 409 `ViewAsLoginUnavailable`
   (the owner login may not sign in), and it is reachable only for an account the caller already owns.
6. **A demoted or suspended operator starts a view-as with an old token.** Requirement: the start
   handlers re-read the actor from the database and require `Role` in `{Admin, Reseller}` and
   `MayAuthenticate`; for a reseller actor also that the reseller is `Active` (through
   `IResellerDirectory`, because Identity may not read `accounts.*`).
7. **Envelope bypass by a race.** Two account creates with one slot left must not both succeed.
   Requirement: `Reseller` carries an explicit `AllocationRevision` counter mapped as a concurrency token and every allocation-changing
   handler touches the reseller row inside its transaction (`NoteAllocationChanged`); the loser's
   `DbUpdateConcurrencyException` maps to the same 409 code. Evidence: an integration test with two
   concurrent creates and exactly one 201.
8. **Envelope bypass by editing a plan after accounts exist.** Requirement: the disk pool is recomputed
   on `UpdatePlan` with the new value, as well as on account create and `ChangeAccountPlan`. A plan
   over any ceiling, or with a feature outside the envelope's set, is refused with the offending field
   names. The arithmetic lives in one pure policy (`ResellerEnvelopePolicy`), not in each handler.
9. **A suspended reseller with a still-valid access token.** An access token lives up to 15 minutes.
   Requirement (D2): the mutating handlers call a shared `RequireActive` check, so a suspended
   reseller's live token can read and cannot write (409 `ResellerSuspended`). Suspension also
   suspends the LOGIN through the `ResellerSuspending` event (existing lifecycle), and it does not
   touch any customer account.
10. **A reseller login with no second factor (D4).** Requirement:
    `SecurityPolicy.ForceTwoFactorForResellers` defaults to true, and sign-in steers a reseller who has
    not enrolled to enrolment exactly as it steers an administrator. The migration column is
    `NOT NULL DEFAULT true`, so rows that exist before the migration are forced too. The two-factor
    endpoints stay `[RefusedWhileViewingAs]`, so a view-as session cannot enrol or disable for the
    login it is looking through.
11. **Hide an impersonation from the audit.** Requirement: entries written during a view-as request
    carry the impersonator through the existing `PrincipalCaller.ViewedBy` and the one audit writer, for
    BOTH kinds; start, refusal and end are journalled through the module's own journal (never a
    hand-built `AuditEntry`), with a subject that says `reseller <name> as <login>` or
    `account <name> as <login>`. Every reseller mutation journals success AND failure through
    `AccountAuditJournal`, with the reseller as actor.
12. **A reseller reads another tenant's audit.** Requirement: no audit endpoint is reachable by a
    reseller in this change. A reseller-visible audit screen is a separate follow-up issue.
13. **A reseller reaches customer data directly.** Requirement (D8): `ResellerCaller` is "sees no rows"
    in Sites, Ssl, Tasks, Cron, Ftp and Sftp, the same as `AccountlessCustomerCaller`
    (`SeesEveryAccount` false, `OwnAccountId` null), and `RequiresAccountFeatureFilter` refuses it as it
    refuses a customer without the feature. A reseller reaches a customer's resources only through
    view-as, which is journalled, time-boxed and revocable.
14. **A forgotten `switch` arm.** Requirement: every `switch` over `Caller` in the tree names
    `ResellerCaller` EXPLICITLY, with no discard arm that would hide a missing case, and the
    `UnreachableException` text says "five cases". A missed arm falls through to a runtime exception on
    the first reseller request to that module, which is why the reviewer's grep below exists and why an
    integration test per module with a reseller token is required.
15. **The SPA lets a reseller onto administrator screens.** Today the SPA's `isAdmin` means "not a
    customer" and the route flag `adminOnly` only turns a customer away, so a reseller would pass as an
    administrator. Requirement: both become ROLE-EXACT (`isAdmin` is `role === 'admin'`; `adminOnly`
    turns away everyone but an administrator; a new `managerOnly` admits administrator or reseller),
    and the navigation tree offers a reseller only items whose endpoint answers a `ResellerCaller` with
    data. This is **advice only**: the backend refuses regardless, and every refusal above must hold with
    the SPA replaced by curl.

## Accepted residuals

These are stated so nobody rediscovers them as a bug, and each is a reviewer's item to accept or reject.

- **Server-global namespaces leak existence through conflicts.** Account names, reseller names (which
  are also login names, so one namespace with account names), plan names and domains are unique
  across tenants. A reseller creating a name another tenant holds learns that the name exists. Not
  closable without per-tenant namespaces; 409 is the honest answer.
- **The global `IX_Plans_Name` unique index stays for one release.** While it exists, a reseller plan
  cannot share a name with another owner's plan, and the 409 tells that reseller another tenant's plan
  name exists. It is left because the previous release's `CreatePlan` relies on it for duplicate
  detection; dropping it now would break the expand-then-contract promise. The drop is its own
  follow-up issue for the next release, with a `// contract-phase:` line.
- **The Accounts query filters are narrowing-only (D7).** They restrict a `ResellerCaller` and leave
  every other caller as it is today. Unattended code (reconcilers, event handlers) runs with an
  `AnonymousCaller`, is therefore NOT narrowed by them, and that is why such code must never take a
  reseller-controlled id as the whole of its scoping. Making the Accounts context fail closed for
  anonymous and customer callers is a separate hardening issue.
- **View-as expiry is not journalled**, as today; an expired session is a start with no end.
- **Rollback caveat.** After rolling the schema back, reseller-owned accounts appear in the previous
  release's administrator lists as ordinary accounts. No data is lost; the previous release simply
  ignores the owner column.
- **Out of scope here, filed as follow-up issues by the lead:** branding, reseller API keys and the
  Provisioning integration, moving an account between resellers, a reseller-visible audit screen,
  fail-closed Accounts context, the contract-phase drop of `IX_Plans_Name`. None of them may be
  half-built in this change.

## What a reviewer must check

Every search below owes its exact command and a positive control (rules/testing.md); use
`/usr/bin/grep`, not a shell wrapper, and `-E` for alternations.

1. **Every `switch` over `Caller` names `ResellerCaller`.** The set of files that mention
   `AccountlessCustomerCaller` and do not also mention `ResellerCaller` must be empty:

   ```
   comm -23 <(/usr/bin/grep -rl "AccountlessCustomerCaller" backend/src | sort) \
            <(/usr/bin/grep -rl "ResellerCaller" backend/src | sort)
   ```

   Positive control: run the first half alone and confirm it lists files (it does today, because the
   case exists). Then open `CallerCaseCensusTests` and confirm it lists five names and that its own
   positive control still fails when a case is removed from the list.
2. **No `IgnoreQueryFilters` was added in Accounts.** Re-run the command in rules/README.md
   (`grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l`) and compare with the count
   recorded there; any Accounts line is a finding. Use `-n`, not a count alone: a count cannot say where.
3. **The Accounts filters exist and a census sees them.** `Account`, `Plan` and `Reseller` each have a
   query filter on the reseller id, and `TenantScopeCensus` learns that an entity carrying `ResellerId`
   needs one, with a planted-unfiltered entity as its refusing control and a planted-filtered one as its
   accepting control. Nothing is exempted.
4. **The token issuer cannot produce a view-as token without a matching row.** Read
   `JwtAccessTokenIssuer.IssueViewAs` end to end: both kinds, both mismatch cases throw, and it is
   reachable only from the two start handlers through the one `ViewAsStarter`.
5. **`[RefusedWhileViewingAs]` is on BOTH start endpoints**, and `ViewAsBoundaryRequirement` is still on
   both policies in `Maran.Host/Authorization/RolePolicies.cs`.
6. **The view-as allow-list covers reseller-kind tokens** (issue #334). A reseller view-as token is an
   operator speaking through a login that is not a customer; confirm `ViewAsAccessDeclarationTests`
   and the run-time refusal (`HostViewAsNotAllowed`) treat an undeclared endpoint as refused for it
   too, and that the endpoints it may reach are read-only or pinned in `ViewAsSafeMutationPin` with a
   reason. A rule written for "customer token with an `act` claim" that silently exempts a token
   whose role is `Reseller` is the single most likely gap.
7. **Server-established members are guarded.** `StartViewAsResellerCommand` and any create command
   whose reseller id is stamped from the caller carry `[property: JsonIgnore][property: BindNever][BindNever]`;
   `BoundCommandGuardTests` has a case for the new command. A bound `ResellerId` on a create would let a
   reseller create an account owned by another reseller.
8. **Every new endpoint declares its policy and rate limit**; `EndpointAuthorizationPolicyTests` passes
   without edits to its exemptions. Actions that only an administrator may do keep
   `[Authorize(Policy = AdminOnly)]` per action even inside a widened controller (home-group repair is
   an example that stays admin-only).
9. **The migrations are expand-only.** `maran migrate guard` passes; the partial unique indexes
   (plan name per owner, one login per reseller) are PostgreSQL 13 compatible (no `NULLS NOT DISTINCT`);
   the `Role` column storage was read in `UserConfiguration` BEFORE the migration was written, and the
   finding is in the migration; the new `ForceTwoFactorForResellers` column has a default.
10. **The SPA is role-exact.** `isAdmin` and `adminOnly` no longer mean "not a customer"; every reader of
    `isAdmin` was audited; the navigation tree offers a reseller no admin-only or tenant-module item.
    Remember this is advice; confirm item 1 to 9 hold without it.
11. **Nothing in the SPA decides anything.** Replay the isolation, envelope and view-as refusals with
    curl and a reseller token.
12. **Doc comments match the behaviour.** A comment that says a filter, a claim or a check does
    something it does not is a defect of the same severity as the behaviour (rules/architecture.md);
    read the comments on `ResellerCaller`, the filters, `ResellerScope`, `IssueViewAs` and
    `ViewAsStarter` against the code, not against this note.

## What the author could not verify

- **Nothing exists yet.** This note precedes the code, so every requirement above is untested and
  unexecuted until the integrator's single gate pass; a reviewer should treat "the code meets it" as
  unproven for every line.
- **The `Reseller.AllocationRevision` concurrency token on PostgreSQL 13 and 16.** Expected to behave
  identically (it is an ordinary integer column), but asserted only by the planned
  two-concurrent-creates test; it was not observed. A `DbUpdateConcurrencyException` surfacing differently under the Wolverine
  transaction behaviour is the thing to watch.
- **EF's handling of the two partial unique indexes and of the `Role` column** is not predicted: the
  lead hand-checks the generated migrations against the plan, because EF will not get partial indexes
  right on its own and the column's storage (integer or string with a check) decides whether a
  migration is needed at all.
- **How the existing view-as allow-list (#334) classifies a token whose role is `Reseller`** was read
  from its note, not from its code; item 6 above is the place a reviewer should spend time.
- **The claim shape `Microsoft.IdentityModel.JsonWebTokens` produces for the `act` dictionary** on the
  way back in is the same open item as in the first view-as note; the new reseller kind reuses it and
  does not add an observation.
- **No external review of the envelope arithmetic at its boundary values.** Equal-to-limit must be
  accepted and one-over refused; this is a unit-test claim only.
- **Whether any unattended Accounts code path takes a reseller-supplied id** (D7) was not enumerated;
  the reviewer should list the callers that run with an `AnonymousCaller` and confirm none does.

## Deviations found when the code landed

Each entry below was checked against the code on 2026-11-03, after the slices were written. Where it
differs from what this note required or expected above, the code is the fact and the text above is the
plan that preceded it.

1. **The concurrency token is an explicit counter, not Npgsql's `xmin`.** `Reseller.AllocationRevision`
   (an integer, incremented by `Reseller.NoteAllocationChanged`) is mapped with `IsConcurrencyToken()` in
   `ResellerConfiguration`. Requirement 7 and the "could not verify" entry above said `xmin` and have
   been corrected to name the counter. A counter is an ordinary column, so it behaves the same on
   PostgreSQL 13 and 16 and is visible in a test; the race is still only proven by the planned
   two-concurrent-creates test.
2. **A new `Users.IsSuspended` column and `SessionRevocationReason.ResellerSuspended`.** A reseller has
   no account membership to carry its suspension, so the login carries it (`User.Suspend`/`Resume`,
   required, database default `false`); `User.MayAuthenticate` is false while it is set. Suspending a
   reseller revokes its sessions, including a view-as session an administrator opened on it, with the new
   revocation reason. Both are additive; the lead scaffolds the migration.
3. **`Identity.User` is exempted in `TenantScopeTests`.** The entity now carries a reseller id, so the
   census would otherwise demand a query filter on it. The exemption states the reason: the row is read
   at sign-in, before any principal exists, and by the login's own user id, so a filter keyed on the
   caller's reseller would hide it from the person signing in. Reseller isolation of what a reseller
   sells is the Accounts filters' job. This contradicts the earlier line "Nothing is exempted" in
   checklist item 3; a reviewer should weigh that reason.
4. **`AccountNamespaceClaims` adds THREE new `IgnoreQueryFilters()` calls in Accounts** (account name,
   reseller name, primary domain), each under an `RS0030` suppression with its reason. This is a
   finding against checklist item 2, which expected none: an account name is the Linux user and login
   name, and a domain is claimed once, so a read narrowed to the reseller's own rows would let it reuse
   a name or a domain another owner holds. The answer is a yes or a no and never a row, so a reseller
   learns that a name exists and nothing about its owner. The count in rules/README.md was raised in
   the same change.
5. **A reseller cannot skip the final backup.** Deleting an account with the final backup skipped is
   refused with `AccountFinalBackupSkipForbidden` (403, `ErrorType.Forbidden`) before anything is looked
   up; the decision to discard an account's last copy stays with the administrator.
6. **A reseller's resource summary reports usage as unavailable.** The module counters are tenant-scoped
   by account and a reseller has no account, so `AccountResourceSummaryReader` states each kind as
   `AccountResourceCount.Unavailable` for a reseller rather than a number that would be wrong or would
   leak another owner's usage.
7. **`RequiresAccountFeatureFilter` refuses a reseller with 403.** A reseller reaches no tenant module, so
   an action behind a plan feature answers `PlanFeatureNotIncluded` (`ErrorType.Forbidden`) instead of
   falling through to the "no account" answer.
8. **No `PUT /resellers/{id}` rename or contact edit exists.** `ResellersController` offers create,
   envelope change, suspend and reactivate only; a reseller's name and contact cannot be edited yet.
9. **There is no way yet to resend a reseller's invitation.** `ResendInvitation` is keyed by an account
   id; a reseller's invitation that expired or was lost cannot be reissued. Follow-up.
