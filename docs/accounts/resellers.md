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
| Resellers (create, envelope, suspend, reactivate, delete) | all | none (its own profile, envelope and usage through the overview) | none |
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
  Deleting removes the reseller's login and sessions.

Every one of these is audited, with the actor, whether it succeeded or failed.

## View-as: the edges

View-as lets an operator use the panel as another login. It is **one hop per token**: the edges below
are all that exist, and no view-as token can start another (the start endpoints are refused while
viewing). The banner at the top of every screen is drawn from the session the backend issued, never
from what the page remembers, and says who you are viewing as and who you really are.

| From | To | How | If it is not allowed |
|---|---|---|---|
| Administrator | any account, including a reseller's | start from the account | 404 `ViewAsAccountNotFound` (missing, or no owner login) |
| Administrator | a reseller | start from the reseller | 404 `ViewAsResellerNotFound` |
| Reseller | one of its own accounts | start from the account | 404 for an account it does not own, same as a missing one; 409 `ViewAsLoginUnavailable` when the owner's login may not sign in |
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

## Not in this release

Each has its own follow-up issue: branding for a reseller's customers, reseller API keys and the
provisioning integration, moving an account between resellers, an audit screen a reseller can read,
making the Accounts data layer refuse by default for every caller that is not a reseller, and removing
the global plan-name uniqueness once the previous release no longer needs it.
