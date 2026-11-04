# Fail-closed Accounts context (issue #371) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
rewrites the tenant boundary of the Accounts module: the query filters on `Account`, `Plan` and
`Reseller` stop being "narrow a reseller, show everyone else everything" and become "show each kind of
caller only what that kind is entitled to, and nothing to a kind that is not named". It also adds
three deliberate bypasses of those filters. It touches the authorization floor under every Accounts
read, so it needs a second reviewer, and no agent session can dispatch one.

**Honest ordering.** Unlike the notes this repository prefers, this one was written **with the code,
not before it**. rules/security.md requires the note first, because a note written after the choice
validates the choice. The author knew the design when writing both; the reviewer should therefore
treat every "why it is safe" sentence below as a claim to re-derive from the QUESTION ("what does an
attacker who reaches this get?"), not as a conclusion already checked. Where this note and the code
disagree, the code is what ships and the note is wrong.

The branch (`fix/339-371-366-378-critical`) may land on its issue branch and on `dev` through a pull
request; it **MUST NOT merge to `main`** until a human reviewer has discharged this note, or the owner
has recorded the beta exception for a named pre-release in writing (rules/security.md "The one
recorded exception"). The owner is told at hand-off that the Accounts query filters carry an
outstanding review.

This note extends `2026-11-03-reseller-role-threat-note.md` (attack 1 and decision D7, "the Accounts
query filters are narrowing-only"). **D7 is superseded by this change**: the filters are no longer
narrowing-only, they decide for every kind of caller. That is the point of the issue, and it is stated
here so a reader of the earlier note does not take D7 as still true.

## What changes

`AccountsDbContext` (`backend/src/Maran.Modules/Accounts/Persistence/AccountsDbContext.cs`) computes
three per-context-instance values from `ICurrentUser.Caller` with an exhaustive `switch` over the
closed `Caller` type, every case named and `_ => throw new UnreachableException`:

| Caller | `SeesEveryRow` | `ResellerScope` | `CustomerAccountScope` |
|---|---|---|---|
| `AdministratorCaller` | true | null | null |
| `ResellerCaller` | false | its reseller id | null |
| `CustomerCaller` | false | null | its account id |
| `AnonymousCaller` | false | null | null |
| `AccountlessCustomerCaller` | false | null | null |

The filters read those values:

- `Account`: administrator sees all; a reseller sees `ResellerId == scope`; a customer sees
  `Id == its account`.
- `Plan`: administrator sees all; a reseller sees `ResellerId == scope`; a customer sees only the plan
  its own account names (a `Set<Account>().Any(...)` subquery, because `Account` carries `PlanId` and
  no navigation).
- `Reseller`: administrator sees all; a reseller sees its own row; a customer sees none.
- Anonymous and accountless customer: nothing, on every entity.

Before this change only `ResellerCaller` was narrowed. Administrator, customer and anonymous were all
unfiltered, and the module relied on its controllers' policies and on `AccountDirectory` to scope
them. The scope values are explicit: a `bool` and two `Guid?`. There is no sentinel `Guid.Empty`, and
every comparison is guarded by a null check on its scope (see "What could still go wrong", item 3).

Three reads then say, in the open, that they need rows the caller's kind cannot see. Each is an
`IgnoreQueryFilters()` call under an `RS0030` suppression carrying its reason on the line, which is
the repository's only way to bypass a filter in production code (rules/csharp.md "Tenant scoping is
the global query filter"):

1. **`PlanSeeder.SeedAsync`** — the start-up "does the panel hold any plan" existence read.
2. **`AccountDirectory.ListAsync`** — the host-wide account list, on both accounts and plans.
3. **`AccountNameDirectory.NamesOfAsync`** — account names for the header's account selector.

No Sdk signature changes: `IAccountDirectory` and `IAccountNameDirectory` keep their shapes. Doc
comments that argued "no `IgnoreQueryFilters` on purpose" and "the filter narrows a reseller and no
other caller" were wrong after this change and are corrected in the same change (rules/architecture.md
"A doc comment that describes what the code does not do is a defect").

## Assets

1. **Every tenant's account row**: system user name, primary domain, owner email, plan, status. A
   customer who can read another customer's account has enumerated the tenant.
2. **The isolation between resellers**, as in the reseller note: reseller A learns nothing of B.
3. **Plan rows**, which carry what a reseller or the administrator sold and priced.
4. **The meaning of "I see nothing"**: a caller with no principal must get an empty answer from this
   context, not a full one, so that a forgotten scoping `Where` in a new handler is a visible bug and
   not a disclosure.

## What an attacker could do with each bypass, and why it is safe

The bypass returns rows the caller's kind could not otherwise read, so the question for each is: who
can make this code run, and with what input. Callers were enumerated by search, not by memory:

```
grep -rnE "\.ListAsync\(cancellationToken\)" backend/src --include=*.cs
grep -rnE "IAccountNameDirectory" backend/src --include=*.cs
grep -rnE "IgnoreQueryFilters\(\)" backend/src/Maran.Modules/Accounts --include=*.cs
```

(Run with `/usr/bin/grep`, not a shell wrapper: rules/testing.md "A SEARCH that found nothing is a
check". The first two have positive controls in their own output, listed below: they find the callers
named.)

### 1. `PlanSeeder` — the existence read

- **Who can reach it.** Nobody over the network. It runs once at start-up from the Accounts module's
  seeding, as the anonymous caller. It takes no input; it returns a `bool` that decides whether to
  insert the three fixed standard plans.
- **What an attacker gains.** Nothing readable: the bool never leaves the method. The only influence
  is on whether standard plans are inserted, and an attacker who can make the panel start and control
  the plans table already owns the database.
- **Why it is needed.** Without the bypass the anonymous caller sees no plan, the existence read
  answers "none" on every boot, and the insert collides with the fixed plan ids (a crash loop on a
  healthy install). That is the fail-closed behaviour working as designed against a legitimate
  caller, which is why the bypass is explicit.

### 2. `AccountDirectory.ListAsync` — every account on the host

- **Who can reach it.** The Sdk window `IAccountDirectory.ListAsync`. The callers in this tree, taken
  from the grep above:
  - `Maran.Modules/Monitoring/Queries/ListAccountDiskUsage/ListAccountDiskUsageQueryHandler.cs` — behind
    `MonitoringController`, whose class-level policy is `AuthorizationPolicies.AdminOnly`.
  - `Maran.Modules/Backups/Jobs/BackupRunHandler.cs`, `Backups/Jobs/RetentionHandler.cs`,
    `Backups/Services/StartupBackupReconciler.cs` and `Sites/Services/StartupPhpSiteReconciler.cs` —
    unattended work (a scheduled run, a retention sweep, start-up reconciliation) with no principal.
  No controller a customer or a reseller can reach calls it. The Sdk is a surface paid modules also
  compile against, so a module bought later could call it; the interface's own doc comment already
  states that it applies no scope and puts the authorization on its caller (this change does not
  weaken that, it only stops the implementation from silently depending on the old filter shape).
- **What an attacker gains if they reached it.** Every tenant's system user name, plan allowances and
  status: the full enumeration the filters exist to prevent. That is why the reach is limited to the
  admin-only controller and to unattended code, and why a reviewer re-runs the grep: a new caller in a
  reachable-by-customer handler is the exact regression.
- **Why it is safe now.** Its input is none (no id, no filter), its callers are server-side only, and
  the one HTTP route is administrator-gated at class level.
- **What the bypass replaced.** Before, this method applied "no scope" by the happy accident that the
  filter did not narrow administrator or anonymous. After the filters fail closed, the same behaviour
  would have turned into an empty list for every unattended caller, which silently disables backups,
  retention and the PHP reconcile. So the bypass preserves existing behaviour deliberately and makes
  it visible.

### 3. `AccountNameDirectory.NamesOfAsync` — names for the account selector

- **Who can reach it.** `IAccountNameDirectory` has one caller:
  `Identity/Queries/ListMyAccounts/ListMyAccountsQueryHandler.cs`, dispatched from
  `MeController.GetAccountsAsync` (`GET api/v1/me/accounts`, `AnyAuthenticated`, view-as `Read`). It is
  not `IAccountDirectory.NamesOfAsync`: that method (used by Ftp) goes through the filter and is
  unchanged.
- **Where the ids come from.** The handler reads `AccountMemberships` for `query.UserId` in state
  `Active` and passes only those account ids. `UserId` is built by the controller from the signed-in
  principal (`SignedInCallerPolicy.Require(CurrentUser)`), never from the request. An attacker cannot
  put an id of their choosing into the list; they would have to hold a membership row for it.
- **What comes back.** A dictionary of id to name. Nothing else about the account leaves the module.
  An account that no longer exists is simply absent, so the handler leaves it out.
- **Why a bypass is needed.** The selector lists accounts OTHER than the active one, and for a
  customer acting for none it lists all of theirs. The customer filter shows a customer only the
  account it acts for, so the filtered read would hide exactly the accounts the selector is for.
- **Why it is safe now.** The disclosure is bounded to the names of accounts the caller already holds
  active memberships of. The residual is that the safety lives in the caller's choice of ids, which
  the interface's doc comment states; a future caller that passed ids not drawn from the caller's own
  memberships would be reading arbitrary account names. That is a limit of the design, listed below.

## What could still go wrong

1. **A new unattended handler silently sees nothing.** Any new background job, start-up reconciler,
   seeder or event handler for another module runs as the anonymous caller and now reads no account,
   plan or reseller through this context. That is the intended fail-closed direction: the failure is
   an empty result, not a disclosure, and it is **visible** (a job that does nothing shows up as
   nothing happening, in a test that expects a row, rather than as a tenant leak). The cost is that
   the author must notice the empty answer and add a reasoned bypass; the bypass count in
   rules/README.md "Mechanical enforcement" (`grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l`)
   is what shows that list growing. This change adds four calls (two in `ListAsync`, one each in the seeder and the name window) and the rules text
   will need updating if the count there is maintained for it.
2. **`CertificateRenewalHandler` already had an anonymous `FindAsync`.**
   `Ssl/Jobs/CertificateRenewalHandler.cs:297` calls `IAccountDirectory.FindAsync`, which goes
   through the tenant filter, from unattended renewal with no principal. Before this change that call
   already answered `null` for the anonymous caller (the directory's `FindAsync` checks the caller),
   so renewal's account lookup was already empty; the fail-closed context does not change that
   answer, and fixing it is **out of scope and a follow-up issue**. It is recorded here because it is
   the same defect class this change makes loud, and a reviewer reading "unattended code finds
   nothing" should know one instance is known and open.
3. **A null scope matching rows with a null owner.** EF translates `column == @param` with a null
   parameter as "both null". An `Account.ResellerId` is nullable (null means administrator-owned), so
   an unguarded `account.ResellerId == ResellerScope` for a caller with no reseller scope would match
   every administrator-owned account. Every comparison in the filters is therefore preceded by
   `ResellerScope != null` or `CustomerAccountScope != null`. A reviewer verifies each of the five
   comparisons has its guard; a guard dropped in a later edit is the same exposure as no filter.
4. **The customer `Plan` filter shows a plan to a customer that is not its own.** The subquery is
   `Set<Account>().Any(account => account.Id == scope && account.PlanId == plan.Id)`. A plan shared by
   two accounts is visible to each, which is correct; it also means a customer reads its plan's
   limits, which is the contract (the customer sees their own allowances). A change that widened the
   subquery to drop the `account.Id == scope` term would show every plan any account uses.
5. **View-as.** A view-as token carries the viewed login's role and account, so it is filtered as that
   customer or reseller (the view-as notes). The view-as start handlers resolve the target through
   the directory under the actor's own caller; this change does not touch that path, but a reviewer
   confirms an administrator viewing as a customer is now narrowed to that account in Accounts reads
   (it is an `AdministratorCaller` only when not viewing as; confirm by the token, not by assumption).
6. **Reads that relied on the old administrator or customer behaviour.** Any Accounts handler that
   ran as a customer and read a row other than its own account or plan (for example, a plan list for a
   create form, or a reseller lookup by a customer) now sees nothing. That is the point, and it is also
   where a regression would appear as a 404 or an empty list in the SPA. Not enumerated by search
   (see "What the author could not verify").
7. **The Sdk stays a power tool.** `IAccountDirectory.ListAsync` still returns every account to any
   module that calls it; the filters being fail-closed does not constrain it. The control remains the
   caller's boundary, as the interface states.

## What the author could not verify

Nothing below has been run. By the repository's rule for this work, no build, test or `maran` command
was executed while the code was written; a single gate pass runs after all code is done, and its
result is not part of this note.

- **That the code compiles and the suite is green.** Not run.
- **EF Core's translation of the customer `Plan` filter.** The filter contains `Set<Account>()` inside
  a query filter lambda that closes over per-instance scope properties. Whether EF translates a
  correlated `Any` over a second set inside a filter, and does so with the `Account` filter also
  applied to that inner set, was reasoned about and not observed. The inner set is itself narrowed by
  the `Account` filter, which agrees with the explicit comparison, but that agreement is an argument
  and the SQL was not read. An untranslatable filter would fail on the first query, which is loud; a
  translatable one that quietly applies a different predicate than intended is the case to rule out.
- **That the null guards produce the intended SQL.** Item 3 above is a claim about EF's null
  semantics for a nullable column against a nullable parameter. Not checked against the generated SQL.
- **Every call site that reads these entities as a non-administrator.** The change was verified by
  reading the Accounts module and the three bypasses' callers. Reads in other modules reach Accounts
  only through the Sdk windows, but the list of windows used by customer or reseller requests was not
  exhaustively enumerated.
- **That the tenant-scope architecture tests and the banned-symbol suppressions still pass.**
  `TenantScopeTests` and the banned-symbol suppressions were not exercised.
- **Anything about performance of the extra predicate** on the plan list for a customer.

## What a reviewer must check

1. **The table in "What changes" against the code.** `SeesEveryRow`, `ResellerScope` and
   `CustomerAccountScope` each name all five cases with no discard arm other than the
   `UnreachableException`, and the values match the table. Then each of the three filters against the
   table's rows.
2. **The null guards (item 3 above):** read the five guarded comparisons and confirm each has its
   `!= null` before it.
3. **The three bypasses, and no fourth.**
   `grep -rnE "IgnoreQueryFilters\(\)" backend/src/Maran.Modules/Accounts --include=*.cs` lists the
   Accounts module's calls. The three new ones are in `PlanSeeder.cs`, `AccountDirectory.cs` (two
   calls under one suppression) and `AccountNameDirectory.cs`. The existing ones in
   `AccountNamespaceClaims.cs` (issue #179) are unchanged by this diff. Anything else is new.
4. **Every caller of the two Sdk windows, again, from scratch.** Re-run the greps in "What an attacker
   could do". `IAccountDirectory.ListAsync` must be reachable only from the admin-only
   `MonitoringController` and from code with no request behind it; `IAccountNameDirectory` must be
   called only with ids read from the caller's own memberships. Add a positive control: the greps must
   print the callers listed above.
5. **A test per caller kind** for each entity: administrator sees all, reseller sees its own, a
   customer sees its account and its plan and no other, anonymous and accountless see nothing, and the
   reseller-row case for a customer. Cross-tenant 404 IDOR tests for account and plan must still hold.
   If these tests do not exist in the diff, they are the missing half of this change.
6. **That unattended work still works.** A backup run, retention, `StartupBackupReconciler`,
   `StartupPhpSiteReconciler` and the seeder each need an integration or unit test showing they see
   accounts and plans with an anonymous caller after the change, and a control showing that without
   the bypass they would see none.
7. **The corrected doc comments** (`AccountsDbContext`, `AccountDirectory`, `AccountNameDirectory`,
   `PlanSeeder`) describe the new behaviour and no sentence still says "no `IgnoreQueryFilters` on
   purpose" or "the filters only narrow a reseller".
8. **Docs that name the old behaviour.** rules/README.md counts the deliberate `IgnoreQueryFilters`
   calls and lists the arrivals; whether it is updated in this change is a question for the pull
   request, not answered here.

## Deviations found when the code landed

None recorded yet. This note was written alongside the code, so the usual section for differences
between the pre-written note and the final code has nothing to compare against; a reviewer who finds a
difference between this note and the diff should add it here, stated plainly.
