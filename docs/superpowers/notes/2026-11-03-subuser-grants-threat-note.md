# Sub-user grants for Sites, SSL, Tasks and PHP, and a membership check on every request: threat note (issue #340)

**Second review: OUTSTANDING.** This changes who may reach a customer's websites, certificates and
background tasks, and it adds a database read to the authorization of every customer request, so
rules/security.md "Sensitive change escalation" (auth and token handling) applies. It may land on a
branch and MUST NOT merge to `main` until a second reviewer has read it. The author wrote this note
after the code, in the same session; that is the weaker order the rule warns about, and the reviewer
should read the code against the note and not the note against itself.

## What changes

1. **Four new features in `AccountFeature`** (`Maran.Sdk/Contracts/AccountFeature.cs`): `Sites` (64),
   `Ssl` (128), `Tasks` (256), `Php` (512). `All` becomes 1023. They are plan features and member
   grants at once, because the enum is both: the plan says what the account sells, the member's
   `AccountMembership.Grants` says what the owner gave that person, and
   `RequiresAccountFeatureFilter` asks both, plan first.
2. **Enforcement by the existing mechanism, nothing new.** `SitesController` carries
   `[RequiresAccountFeature(Sites)]` on the class and `[RequiresAccountFeature(Php)]` on
   `ChangePhpVersionAsync` (both filters run: Php is asked IN ADDITION to Sites). `CertificatesController`
   carries `Ssl`, `TasksController` carries `Tasks`. The filter only applies to a `CustomerCaller`;
   administrators are unaffected, as for the six existing features.
3. **A membership check on every customer request.** `AccountMembershipActiveHandler` (the central
   guard from #331, attached to the `AnyAuthenticated` policy) used to refuse only a SUSPENDED
   membership and deliberately let a login with NO membership through, leaving a removed member to the
   feature gate. It now reads the caller's membership of the account their token names (one indexed
   read, by `(UserId, AccountId)`), and: no row fails the requirement explicitly (generic 403
   `HostForbidden`), a suspended row leaves it unmet (409 `AccountMembershipSuspended`, unchanged),
   an active row succeeds. Exempt as before: administrators, view-as tokens, a customer token naming no
   account.
4. **A data migration on Accounts** (`AddSitesSslTasksPhpToPlanFeatures`): `Plans."Features" |= 960`.
   Those four were never gated, so every plan sold them in effect; without it a plan stored as the old
   full set (63) would stop selling websites, certificates, tasks and PHP the moment the filters ship.
   Additive, so the previous release (which ignores unknown bits) still runs against the schema.
5. Backend-provided lists (`GET /api/v1/plans/features`, `GET /api/v1/me/sub-users/features`,
   `accountAccess.features` of the profile) enumerate `AccountFeature.All.Members()` and therefore grow
   by themselves; the labels are `AccountFeature<Name>` in both `DisplayNames` tables, en/ru/hy. The SPA
   only gained the four names in its `AccountFeatureName` union.

## What an attacker could try, and why it fails

| Attempt | Why it fails |
|---|---|
| A member with no grants lists, creates or deletes sites, reads logs | The class-level filter answers 403 `AccountFeatureNotGranted` before any handler runs. Covered by `SubUserGatedModulesEndpointTests`. |
| A member with only `Sites` issues or removes a certificate, or reads the task list | Each module asks for its own feature; a grant opens its own module and no other (`A_grant_opens_its_own_module_and_not_the_others`). |
| A member with `Sites` switches a site's PHP version | `ChangePhpVersionAsync` additionally asks `Php`; `Php` alone does not open the site endpoints either (`The_php_grant_alone_does_not_open_the_site_endpoints`). |
| A removed member keeps using the access token they already hold for up to fifteen minutes | The central handler finds no membership and fails the requirement on the next request, on every `AnyAuthenticated` endpoint and not only the gated ones. Covered by `AccountMembershipActiveHandlerTests` and `A_removed_members_token_is_refused_at_once` / `..._on_an_endpoint_that_has_no_feature`. |
| A member with the Sites grant reads another account's site by id | Tenant scoping is untouched: the query filter hides the row and the answer is 404, not 403 (`A_granted_member_gets_404_for_another_accounts_site`). |
| A plan without `Sites` sells sites through a member's grant | The plan is asked first (403 `PlanFeatureNotIncluded`); a grant can only narrow what the plan sells (`An_account_whose_plan_does_not_sell_sites_...`). |
| A customer learns something about the account from the removed-member refusal | The refusal is the generic `HostForbidden`, the same a stranger's token would meet; it names nothing of the account. |

## What this does NOT cover (stated limits)

- **`POST /api/v1/sites` with a `phpVersion` does not ask `Php`.** A member with `Sites` but not `Php`
  can still choose the version at creation; only switching an existing site is gated. The version
  field is optional and the host decides what is installed, so this is a product-level gap rather than
  an escalation, but it is a gap. Closing it needs the create action to ask the Sdk window
  (`IAccountMemberGrants`) when the field is present, which is controller logic this change avoided.
- **`GET /api/v1/sites/php-versions` (the installed-versions list) is under `Sites` only**, because the
  site form reads it; it names nothing but what the host has installed.
- **Tasks are account-wide.** Every kind of task of the account (backups, certificates, site work) is
  visible to a member holding `Tasks`, whatever other features they lack. The log stays withheld from
  customers, as before.
- **A member without `Tasks` gets 403 from the shell's polling** (failed-count, running stream). The SPA
  treats an unanswered count as "no badge"; nobody has checked every screen that reads it.
- **Existing members are not granted the four new features.** A migration that granted them would give
  `Sites` etc. back to members whose owners had deliberately given them nothing, which is the hole this
  change closes; so a member invited before this release loses access to these areas until the owner
  grants them under Sub-users. This is a behaviour change for beta users and needs the owner's
  acknowledgement in the release notes.
- **A view-as token is exempt from the membership check** (its own per-request session check ends it
  when the session is revoked). Removing a member does not, by itself, end an operator's view-as session
  that was started against that member.
- **The SPA menu is unchanged.** It filters on the plan's features (`accounts/me`), not on a member's
  grants, so a member without `Sites` still sees the Sites menu and meets the 403 page. The menu is
  advice (rules/vue.md); the backend is the boundary.
- The per-request read is one query on a unique index per authorized customer request. It was not
  measured.

## What the author could not verify

Nothing was built or run (the working tree is shared, and the gate pass belongs to the coordinator).
Hand-written and therefore PENDING in the gate pass: the migration `AddSitesSslTasksPhpToPlanFeatures`
and its designer (copied from the previous Accounts designer, the model being unchanged), so
`maran migrate check` and `maran migrate guard` have not seen them; the new unit and integration
tests; and the Playwright stubs, whose feature lists gained the four names. The second review stays
OUTSTANDING. A reviewer should check:

1. Every customer-reachable action of Sites, Ssl and Tasks sits under the class-level attribute
   (`grep -n "RequiresAccountFeature" backend/src/Maran.Modules/{Sites,Ssl,Tasks}/Controllers/*.cs`),
   and that no other controller in those modules serves a customer without it.
2. `AccountMembershipActiveHandler` is still attached to `AnyAuthenticated` only, and that nothing
   customer-reachable carries `AdminOnly` by accident to avoid it.
3. That no customer flow legitimately holds a token naming an account without a membership of it
   (the only producers are sign-in, refresh and switching, all of which read memberships).
4. That the 960 in the migration equals `Sites | Ssl | Tasks | Php` in the enum.
