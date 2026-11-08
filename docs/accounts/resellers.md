# Resellers

A reseller is a business the administrator sells a slice of the server to. It signs in to the panel,
creates its own plans and its own customer accounts inside that slice, and looks at those accounts
the way the customer does. It never sees another reseller's accounts, and it never sees the server
itself. This page describes what a reseller is, what bounds it (the envelope), who sees what, and how
the view-as chain works. The SPA checks the same rules before sending, but only as advice: the server
decides every one of them and its answer is what counts (issues #179, #314, #317).

## What a reseller is

A reseller is one named business (a row in `accounts.Resellers`) and exactly one login
(`identity.Users.Role = Reseller`, `Users.ResellerId`). The login is created the way an account owner's
login is: the administrator enters a contact e-mail and a language, the panel mails an invitation, and
the reseller sets its own password. The administrator never knows it.

- The reseller's **name is also its login name**, so it shares one namespace with account names: a
  reseller cannot be named like an existing account, and an account cannot be named like a reseller
  (`ResellerNameTaken`, `AccountNameTaken`).
- An administrator can **rename a reseller and change its contact address and language** afterwards
  (`PUT /api/v1/resellers/{id}`, the whole identity in one request; the envelope has its own
  `PUT /api/v1/resellers/{id}/envelope`). The login follows in the same request: it signs in with
  the new name, and its invitation and password-reset mails go to the new address. A new name is
  refused when any account or reseller holds it (`ResellerNameTaken`), and Identity refuses a name
  or an address another login holds (`UsernameTaken`, `EmailTaken`); a refusal changes neither the
  reseller nor its login. Sessions stay: a rename is not a credential change. The change is
  journalled as `ResellerUpdated` under the name the reseller had, and never records the addresses.
- Two-factor sign-in is **mandatory** for a reseller. Until it enrols, sign-in steers it to
  enrolment, exactly as it does an administrator (`ForceTwoFactorForResellers`, on by default).
- Every plan and every account carries an owner: the administrator (no reseller) or one reseller. The
  owner is set when the row is created and never edited. There is no transfer between resellers in
  this release.

## The envelope

The administrator sets an envelope when the reseller is created and can change it later. It is what
was sold. The reseller sees it read-only on its overview screen, next to its current usage.

| Part | What it bounds | Refused with |
|---|---|---|
| Per-plan ceilings: disk quota, sites, databases, SFTP users, FTP users, cron entries, PHP workers per pool, and the runtime limits | Every number of every plan the reseller owns may not exceed the ceiling. The plan's features must be a subset of the envelope's features, and its runtime limits no more permissive than the envelope's | `ResellerPlanExceedsEnvelope` (400), naming the offending fields |
| Maximum plans | How many plans the reseller owns | `ResellerPlanLimitReached` (409) |
| Maximum accounts | How many accounts the reseller owns, in any status | `ResellerAccountLimitReached` (409) |
| Disk pool | The sum, over the reseller's accounts, of each account's plan disk quota | `ResellerDiskPoolExceeded` (409) |

The disk pool is checked when an account is created, when an account changes plan, and when a plan is
edited to a larger disk quota (the sum is recomputed with the new value). Deleting an account
releases its share. Limits are inclusive: a request that lands exactly on a limit is accepted and one
over it is refused.

Two requests racing for the last slot cannot both succeed: the reseller row carries a concurrency
token and every change that allocates something touches it inside its transaction, so the loser is
answered with the same 409 code.

**Lowering an envelope below current usage is allowed.** Existing plans and accounts keep working
and nothing already created is edited; only new writes are refused until usage fits again. Both the
administrator and the reseller see the reseller as "over envelope" meanwhile.

A reseller uses only its own plans. The administrator's plans are not offered to a reseller, and an
account's plan must belong to the same owner as the account (`PlanOwnerMismatch`), for an
administrator's request too. Otherwise an account moved onto an administrator plan would drop out of
the reseller's lists.

## Who sees what

| | Administrator | Reseller | Customer |
|---|---|---|---|
| Resellers (create, rename and contact, envelope, suspend, reactivate, delete) | all | none (its own profile, envelope and usage through the overview) | none |
| Accounts | all | its own | its own account only |
| Plans | all | its own | none |
| Sites, certificates, tasks, cron, FTP, SFTP and the other account resources | all accounts' | none directly | its own account's |
| Server screens (firewall, services, system status, PHP versions) | yes | none | none |

A reseller reaches a customer's resources **only** through view-as. Another reseller's account or plan
answers **404**, identical to an id that does not exist, never 403: a 403 would confirm the row
exists. The 403 is used only for a role refusal of a whole endpoint, such as a reseller calling the
reseller list, which says nothing about any tenant. The same rule is enforced by the Accounts query
filters, so a handler that forgets a `Where` still cannot return another reseller's row.

Names that are unique across the whole panel (account names, reseller names, plan names for one more
release, domains) cannot be hidden: a reseller who asks for a name another tenant holds is told it is
taken.

## Suspending and deleting a reseller

- **Suspend** closes the reseller's login and refuses its mutations. A reseller token issued before
  the suspension lives for at most 15 minutes and can read but not write (`ResellerSuspended`, 409).
  Its customers' accounts are **not** suspended: a reseller's billing problem does not take its
  customers' sites down. An administrator suspends accounts separately when that is wanted.
- **Reactivate** reopens the login.
- **Delete** is refused while the reseller still owns accounts (`ResellerHasAccounts`) or plans.
  Deleting removes the reseller's login, sessions and API keys.

Every one of these is audited, with the actor, whether it succeeded or failed.

## Moving an account to another owner

An administrator can hand an account to another reseller, or to the administrator, with
`PUT /api/v1/accounts/{id}/owner` and a body `{ "resellerId": "<id>" | null, "planId": "<id>" }`. A null
`resellerId` is the administrator. A reseller cannot call it, for its own accounts or anyone else's
(403): an account leaves or joins a reseller only by the administrator's decision.

- **The plan is re-paired.** An account and its plan always share an owner, and a reseller cannot see
  another owner's plan, so the move names a plan of the **new** owner. A plan of any other owner is
  refused with 409 `PlanOwnerMismatch`. The new plan is applied like any plan change: a smaller disk
  quota than the account's files is refused whole (`AccountPlanQuotaBelowUsage`), and the new quota
  and runtime ceilings are put on the host before the row is written.
- **The new owner's envelope admits it** exactly as it admits a new account: its account limit
  (`ResellerAccountLimitReached`) and its disk pool, counting the new plan's quota
  (`ResellerDiskPoolExceeded`). Moving to the administrator has no envelope to check. Both owners'
  usage is counted from the account rows, so the former owner's falls and the new owner's rises in the
  same save. If the new owner's allocation changes under the move, it is refused with
  `ResellerAllocationChanged` and the host is put back; repeating it gives the exact refusal.
- **Sessions.** The view-as sessions the former reseller had open on the account end at once
  (`AccountTransferred` in the sessions screen). The customer's own sessions do not: their tokens name
  the account and no reseller, so the move changes nothing they can do. An administrator's view-as
  session stays, as an administrator sees every account.
- **Naming the owner and plan the account already has** returns the account unchanged and records
  nothing; naming its owner with a different plan is refused with `AccountOwnerUnchanged` - change the
  plan instead.
- **Audit.** `AccountOwnerChanged`, with the account, both owners and both plans in the subject, on
  success and on every refusal.

## View-as: the edges

View-as lets an operator use the panel as another login. It is **one hop per token**: the edges below
are all that exist, and no view-as token can start another (the start endpoints are refused while
viewing). The banner at the top of every screen is drawn from the session the backend issued, never
from what the page remembers, and says who you are viewing as and who you really are.

| From | To | How | If it is not allowed |
|---|---|---|---|
| Administrator | any account, including a reseller's | start from the account | 404 `ViewAsAccountNotFound` (missing, or no owner login) |
| Administrator | a reseller | start from the reseller | 404 `ViewAsResellerNotFound` |
| Reseller | one of its own accounts | start from the account | 404 for an account it does not own, same as a missing one; 409 `ViewAsLoginUnavailable` when the owner's login or its membership is suspended; 409 `ViewAsInvitationPending` when the owner has not accepted the invitation yet (a suspension is reported first when both hold) |
| Anyone viewing as another login | anything | none | refused (403) |
| Customer, anonymous | anything | none | refused (403 or 401) |

An administrator who is viewing as a reseller who wants to look at one of its accounts presses Back
and opens the account directly: an administrator already sees every account, so a second hop would add
no authority.

The token of a view-as session carries the **viewed login's** role. An administrator viewing as a
reseller holds the reseller's power only, and every administrator endpoint refuses it. Back returns
to the operator's own session, and the operator lands on the page the session was started from (the
account for an account view, the reseller for a reseller view). Starting and ending a view are
audited, and every entry written while viewing also names the operator.

## Branding

A reseller can give its customers' shell its own face: a **name**, a **logo** and an **accent colour**
(issue #367). They are stored on the server (`accounts.ResellerBrandings`, one row per reseller that
set any), and the SPA only renders what it is told. A reseller edits them under Settings, Branding.

| Call | Who | What |
|---|---|---|
| `GET /api/v1/reseller/branding` | the reseller | its own values, every member `null` when none is set |
| `PUT /api/v1/reseller/branding` | the reseller, never while an administrator views as it | replaces all three together; a member sent as `null` goes back to the panel's own, and a body with all three `null` removes the row |
| `GET /api/v1/accounts/me/branding` | any signed-in caller | the branding of the reseller that owns the caller's account; every member `null` for an administrator, a reseller, an account the administrator owns, or a reseller that set nothing |

- **What a customer sees.** The header shows the logo in place of the product's mark and the name in
  place of the product's name, each independently. The accent colour replaces the fill under white text
  (primary buttons, the selected menu item) and its hover. The sign-in screen and everything outside a
  customer's signed-in shell keep the panel's own look, because nobody is identified yet.
- **What the server accepts.** The name is at most 40 characters with no control characters
  (`BrandingDisplayNameInvalid`). The colour is `#rrggbb` (`BrandingAccentColorInvalid`) and must have
  a contrast of at least 4.5:1 against white (`BrandingAccentColorLowContrast`): white text sits on it,
  and a pale colour would make every primary button of the reseller's customers unreadable. The logo is
  a base64 data URI of a PNG, JPEG or WebP image whose first bytes really are that type
  (`BrandingLogoInvalid`) and at most 140 000 characters, about 100 KB of image
  (`BrandingLogoTooLarge`). SVG is refused: it can carry script, and one tenant's content is shown to
  another's users. A data URI also needs no outside host, which the panel's content-security policy
  would refuse anyway.
- **Who reads what.** The Accounts query filter shows a reseller its own row and a customer the row of
  the reseller that owns its account, and nobody else's. A suspended reseller can read its branding
  and cannot change it (`ResellerSuspended`).
- **Audit.** A change is journalled as `ResellerBrandingChanged` under the reseller's name, refusals
  included. The values are never recorded: the journal is never deleted, the current branding is
  readable from the row, and a logo does not belong in a journal line.

## The reseller's audit journal

A reseller reads its own part of the audit journal at `GET /api/v1/audit/reseller` (with `/actions` and
`/outcomes` for the filters), and in the panel from the sidebar's Activity entry (`/reseller/audit`,
issue #370).

- **What is in it.** The entries stamped with an account the reseller owns, and the entries its own
  login made (matched by the login's user id, never by the name typed at a sign-in form). An entry an
  administrator wrote about one of its accounts is in it; an entry about another reseller's account, the
  administrator's own accounts, or the server at large is not.
- **One query, two scopes.** The route takes the same page, size, search, sort and filters as the
  administrator's journal and answers in the same shape. What narrows it is the caller: the reseller
  comes from its own token and its accounts from the Accounts module, so there is no parameter to widen
  the scope with. Any other caller that reaches the query is refused (`AuditJournalNotReadable`).
- **404 outside the scope.** Filtering by an account the reseller does not own answers `404
  AuditAccountNotFound`, exactly as for an account that does not exist.
- **Read-only, no upkeep.** The export, the retention setting and the integrity check stay the
  administrator's. An administrator viewing the panel as the reseller may read the journal.

## API keys

A reseller can issue API keys to the integrations that provision on its behalf (issue #368). A key
belongs to the reseller that created it and reads that reseller's own plans and accounts, nothing else:
`GET api/v1/reseller-api/plans`, `GET api/v1/reseller-api/plans/{id}` and
`GET api/v1/reseller-api/accounts`, with the `plans.read` and `accounts.read` scopes. The reseller
manages its keys at `api/v1/reseller/api-keys` (create, list, revoke, scopes), signed in as itself; a
view-as session is refused there. How a key is shown, sent, bound to an address, expired and audited is
on [the API keys page](../api/api-keys.md); the rules specific to a reseller's key are:

- **Another reseller's row is `404`.** A key is narrowed to its reseller's accounts and plans by the
  same filters as the reseller's login. It cannot read a single account's detail (which carries the
  owner's e-mail address), cannot write anything, and cannot manage keys.
- **Its life follows the reseller's.** Suspending the reseller makes its keys answer `401` at once;
  reactivating it lets the keys that are neither revoked nor expired work again. Deleting the reseller
  deletes its keys.
- **Its own ceiling.** A reseller holds at most 20 working keys, apart from the administrator's.
- **The administrator's own key list does not show them.** It holds the administrator's own keys only.
  The administrator sees and revokes a single reseller's key from that reseller's page (API keys), over
  `GET api/v1/resellers/{resellerId}/api-keys` and `DELETE api/v1/resellers/{resellerId}/api-keys/{id}`,
  without suspending the reseller; the revoke is journalled under the administrator (issue #435). The
  reseller manages its own keys on Settings, API keys.

## Not in this release

Each has its own follow-up issue: creating, suspending and deleting accounts by key (it waits for the
Provisioning API), a screen for moving an account to another owner (the API exists),
making the Accounts data layer refuse by default for every caller that is not a reseller, and removing
the global plan-name uniqueness once the previous release no longer needs it.
