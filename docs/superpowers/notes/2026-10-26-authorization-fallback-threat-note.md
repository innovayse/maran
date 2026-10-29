# Threat note: the authorization fallback is administrator-only (issue #89)

`rules/security.md` ("Sensitive change escalation") requires a threat note, written first, and a
second reviewer for any change to authentication or authorization. This change edits the policy
that every endpoint in the panel falls back to when it declares nothing. Its blast radius is every
route, present and future, including routes a marketplace module brings with it.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by agent sessions,
which cannot obtain one, and a note written by the author of the change does not stand in for a
reviewer. Branch `fix/89-host-fallback-policy` MUST NOT merge to `main` until a human second
reviewer has read the change against this note and signed off. `main` fast-forwards from `dev`
only, so merging the branch into `dev` first would hold `main` back as well. The owner is told so
at hand-off.

**This note was written first.** The working tree was clean at `7e75b67` (the merge of
`fix/78-79-80-site-creation` into `dev`) when the first version of this file was created; later
passes only corrected and extended it. The ordering rests on file mtimes, which record the last
write and not the first, so it fits "written first" without proving it.

**Base commit, re-checked.** The branch worktree is based on `1eb8dde`, which descends from
`7e75b67`; `dev` has since advanced to `c47c53c`, which this branch does not contain. The census
below was taken at `7e75b67` and re-run at `1eb8dde`: the bare-form pattern returns the same six
sites at both (`git grep -nE '^\s*\[Authorize\]|\.RequireAuthorization\(\)' 1eb8dde -- 'backend/src/*.cs'`),
and the metadata-convention pattern returns nothing at `1eb8dde`. Controllers deriving from
`BaseApiController` number 29 at the base. Merging `dev` forward (reviewer item 10) must repeat
both searches, because a controller added on `dev` in between would not be in this census.

**Measurement status.** Phase A of the branch wrote code and tests and ran nothing. Every
statement below about what the running server does (a status code, a build result) is marked
UNMEASURED until the verification run is recorded in the section "Run evidence", which is empty
at the time of writing. Census figures are readings of source at `7e75b67`, each with the command
that regenerates it. The bare-form census commands (`git grep ... 7e75b67 ...`) double as their own
positive control: run against `7e75b67` they return the six bare sites, run against the branch
they return none.

## What an attacker could do with the old surface

`RolePolicies.Configure` set `FallbackPolicy` and `DefaultPolicy` both to `AnyAuthenticated`
("signed in, two-factor enrolment complete"). **A customer passes it.** So at `7e75b67`:

- An endpoint with no authorization metadata at all (no `IAuthorizeData`, no `AuthorizationPolicy`,
  no `IAllowAnonymous`) was evaluated against the fallback and opened to every signed-in customer.
- An endpoint with a bare `[Authorize]` or `.RequireAuthorization()` was evaluated against the
  default policy and opened to every signed-in customer.
- An anonymous caller was refused (401) by both, which is the only direction the comments described.

The attacker is any signed-in customer: on a shared hosting server, a stranger who bought an
account. The trigger is something a developer forgets, not something the customer does: one
attribute missing on a new administrator controller, ours or a marketplace module's (which
`MapControllers` picks up from the module's application part like any other). The customer then
reaches server-wide administration, and no test failed, because nothing walked the route table
asking each endpoint for an explicit named policy, nothing pinned the set of anonymous endpoints,
and the per-module authorization tests cover hand-written route lists.

### Census at `7e75b67` (what was open: nothing)

```
/usr/bin/grep -rlE ':\s*BaseApiController\b' backend/src --include=*.cs | wc -l          # 29 controllers
/usr/bin/grep -rlE '^\[(Authorize|AllowAnonymous)' backend/src --include=*.cs | wc -l    # 28 with a class-level attribute
```

The one controller without a class-level attribute, `Identity/Controllers/AuthController.cs`,
declared one on every action (11 `[Http*]`, 11 `[Authorize]`/`[AllowAnonymous]`). The Host mapped
five minimal endpoints, all with explicit metadata: `/health`, `/health/live`, `/health/ready`,
`/api/v1/modules` (`.AllowAnonymous()`) and `/api/v1/dashboard` (bare `.RequireAuthorization()`).
`Program.cs` has the only `MapControllers`; no `MapHub`, `MapGroup`, `MapFallback` or `MapOpenApi`.
No convention or global `AuthorizeFilter` adds metadata behind the attributes:

```
git grep -nE 'IAuthorizeData|IAllowAnonymous|AuthorizeAttribute|AllowAnonymousAttribute|Conventions\.Add|IApplicationModelConvention|IControllerModelConvention|AuthorizeFilter' 7e75b67 -- 'backend/src/*.cs'
```

returned nothing (positive control: the same pattern over `backend/tests` finds
`TwoFactorEnrolmentSteeringTests.cs`, `IAllowAnonymous`). So no matched endpoint relied on the
fallback. The six bare sites relied on the default policy, customer-level by intent:

```
git grep -nE '^\s*\[Authorize\]|\.RequireAuthorization\(\)' 7e75b67 -- 'backend/src/*.cs'
```

lists `SessionsController.cs:20` (class), `AuthController.cs` `logout-all`, `two-factor/enrol`,
`two-factor/confirm`, `two-factor/disable`, and `DashboardEndpoint.cs:99`. This is a reading of
source, not the runtime route table; `EndpointAuthorizationPolicyTests` below replaces it with one.

## The change

1. **The fallback fails closed for customers.** `options.FallbackPolicy` is `AdminOnly`
   (authenticated, `Role = Admin`, two-factor enrolment complete). An endpoint that declares nothing
   is reachable by an administrator only.
2. **The default policy is unchanged** (`AnyAuthenticated`), as the issue proposes: a bare
   `[Authorize]` still means "any signed-in user who has finished enrolment".
3. **Every bare declaration in the tree is explicit.** The six sites name
   `AuthorizationPolicies.AnyAuthenticated`. Behaviour-identical by construction: `DefaultPolicy` IS
   `options.GetPolicy(AnyAuthenticated)`.
4. **Class-level anonymity is gone.** `SetupController` carried a class-level `[AllowAnonymous]`
   (which outranks an action's `[Authorize]` and had already left `logout-all` open once); its two
   actions now carry it individually.
5. **The comments are corrected** to say what the code does: `RolePolicies.cs`, the summaries on
   `AuthorizationPolicies` (including what a module author gets for an unmarked endpoint and for a
   bare `[Authorize]`), and the 2026-08-30 note's guarantee paragraph.

## What holds the guarantee up

Two layers, covering different things.

**Merge-time pins guard our tree.** A forgotten attribute, a bare `[Authorize]`, or a new
`[AllowAnonymous]` fails CI by route or action name.

- `backend/tests/Maran.Host.IntegrationTests/EndpointAuthorizationPolicyTests.cs` walks the real
  composed host's `EndpointDataSource`. Tests: `Every_endpoint_names_its_policy_or_is_marked_anonymous`,
  `The_anonymous_endpoints_are_exactly_the_pinned_set`,
  `The_walk_saw_every_controller_action_and_every_host_endpoint`, and positive and inverse controls
  (`The_census_reports_an_endpoint_with_no_authorization_metadata`, `..._a_bare_authorize`,
  `..._a_role_only_authorize`, `..._an_unknown_policy_name`, `..._a_policy_object_without_a_name`,
  `..._an_anonymous_endpoint_missing_from_the_pin`, `..._a_pinned_endpoint_that_is_no_longer_anonymous`,
  `The_census_accepts_an_endpoint_naming_a_known_policy`, `The_census_accepts_a_pinned_anonymous_endpoint`).
- `backend/tests/Maran.ArchitectureTests/ControllerAuthorizationDeclarationTests.cs` (with
  `ControllerAuthorizationCensus`) does the same for controller actions from the type metadata.
- `backend/tests/Maran.ArchitectureTests/AnonymityDeclarationTests.cs`
  (`No_composed_controller_grants_anonymity_at_class_level`, plus a walk-coverage test and two
  controls) holds the "never on a class" claim.
- Per-module `Controllers/` test folders in Accounts, Identity and Sites assert the explicit
  declarations of the converted controllers.

**The fallback guards the running server.** A marketplace module installed on a production host
never ran this repository's tests; for its controllers the fallback is the only line.
`backend/tests/Maran.Host.Tests/Authorization/RolePoliciesTests.cs` holds the policy objects
(fallback is the administrator policy, default is the customer-level one, both carry the
two-factor requirement). `AuthorizationFallbackEndpointTests` and `CustomerSelfServiceEndpointTests`
in `Maran.Host.IntegrationTests` observe it over HTTP: an unmarked probe endpoint
(`UnmarkedProbeController`) answers 401 anonymous, 403 customer (empty body), 200 administrator.

**Why `AdminOnly` and not a policy that refuses everyone.** An administrator already holds every
power the panel grants, so an unmarked endpoint opened to them grants nothing new, whereas a
deny-all fallback would protect nothing more and turn an administrator's typo'd URL into a 403.

## The behaviour that changes (measured, see "Run evidence")

The fallback is also evaluated when a request matched no endpoint, or matched only the framework's
405 endpoint, because neither carries authorization metadata. This is the expected behaviour of the
framework's authorization middleware and is what `AuthorizationFallbackEndpointTests` asserts:
`GET /api/v1/no-such-endpoint` and `DELETE /api/v1/dashboard` answer 401 anonymous, **403 to a
signed-in customer**, and 404 / 405 to an administrator. For a customer that moves 404/405 to 403
(a disclosure improvement: an existing administrator route and a non-existent path look the same).
Anonymous callers and administrators are unchanged.

An earlier correction in this branch claimed a true 404 "most likely stays 404" and the
`RolePolicies.cs` comment said the same; the branch's own test asserts 403, and the two disagreed.
The claim of 403 is the one kept (it is what the test asserts and what the middleware's fallback
handling is documented to do), and it has since been observed (403 for a customer, 404 and 405 for an administrator). Had the run shown 404, this section,
the test and the `RolePolicies.cs` comment change together; the branch does not merge with them in
conflict. The SPA's own requests target mapped routes, and the Playwright fixtures returning 404
are stubs that never reach the API.

## What this change does NOT close

- **An explicit but wrong policy.** `[Authorize(Policy = AnyAuthenticated)]` on an administrator
  controller passes both layers. A pin of the exact set of customer-reachable endpoints would close
  it for our tree; it is not in the issue and is offered to the reviewer.
- **A third-party module's explicit `[AllowAnonymous]` or `AnyAuthenticated`.** Neither layer
  reaches a module's declared intent on a server that never ran our CI.
- **A third-party module's bare `[Authorize]`** stays customer-level because the default policy is
  kept. Raising `DefaultPolicy` to `AdminOnly` would fail closed there too, at no cost to our tree;
  the failure it would cause is loud. An owner decision, not taken here.
- **Policy objects instead of names.** `RequireAuthorization(new AuthorizationPolicyBuilder()...)`
  attaches `AuthorizationPolicy` metadata; nothing in our tree does it, and the pin test reports it
  (`The_census_reports_a_policy_object_without_a_name`).
- **Role-only `[Authorize(Roles = ...)]`.** Reasoned from how `AuthorizationPolicy.CombineAsync`
  builds a policy, not observed: a `Roles` value with no `Policy` does not merge in the default
  policy, so it would skip the two-factor requirement. The pin test refuses the form either way.

## What a second reviewer must check

1. `RolePolicies.Configure`: `FallbackPolicy` is `AdminOnly`, `DefaultPolicy` is `AnyAuthenticated`,
   and the comments describe each correctly.
2. The six converted sites carry `Policy = AnyAuthenticated`, and the two-factor enrolment actions
   still carry `[AllowDuringTwoFactorEnrolment]`.
3. The anonymous pin in `EndpointAuthorizationPolicyTests` lists 13 endpoints (`/health`,
   `/health/live`, `/health/ready`, `/api/v1/modules`, `GET /api/v1/setup/state`, `POST
   /api/v1/setup`, and `POST /api/v1/auth/` `login`, `refresh`, `logout`, `two-factor`,
   `forgot-password`, `reset-password`, `accept-invitation`), each with a reason, and no entry was
   added to make the test pass.
4. The pin tests cannot pass vacuously: count floor, positive controls, inverse controls, all run
   against the checker the real walk uses.
5. The HTTP fallback test hits an endpoint with no metadata of any kind
   (`The_probe_endpoint_carries_no_authorization_metadata` is its precondition).
6. Mutation evidence: named tests red for (a) a removed administrator-controller attribute, (b) an
   added bare `[Authorize]`, (c) the fallback reverted to `AnyAuthenticated`. Proven by the mutation run (see "Run evidence").
7. `SetupController` still answers `GET /api/v1/setup/state` and `POST /api/v1/setup` anonymously.
8. `scripts/test-baseline.txt` grew by exactly the new tests and nothing vanished.
9. No existing test relied on a signed-in customer receiving 404 or 405 from an unmatched path.
   `grep -rnE 'MethodNotAllowed|Status405' backend/tests` found nothing at `7e75b67`; the 404
   assertions (`HttpStatusCode\.NotFound`, 31 lines) were not read one by one. Partly open: see "Run evidence", left unverified item 3.
10. The branch is rebased on or merged with the current `dev` before the pull request; re-run the
    class-level count against the merge base and the branch.
11. Whether the sentence "several auth endpoints carry exactly that" in
    `2026-09-04-cron-firewall-monitoring-threat-note.md` still reads correctly now that no endpoint
    carries a bare `[Authorize]`. The executed plan `plans/2026-08-30-maran-auth.md` still gives the
    old instruction and is left alone as a record.

## What the author could not verify

- **No second reviewer** (see top).
- **Nothing was built or run in Phase A.** The edits were read, not compiled; whether they build
  clean under warnings-as-errors (an unused `using` is IDE0005) is for the verification phase.
- **The runtime route table** is measured only by `EndpointAuthorizationPolicyTests` once run;
  endpoints contributed by something other than source the greps read would not appear in the census.
- **That unmatched and 405 requests are evaluated against the fallback** (see above).
- **SPA handling of a 403 where a 404 used to come back** on an uncomposed module's route:
  reasoned from the catalogue, not exercised.
- **Third-party modules.** None exists yet, which is why the runtime layer exists.

## Independent agent review

Five agent lenses (correctness, security, rules, compatibility, design) read the Phase A change.
This is an agent review. It does not replace the human second reviewer, which stays **OUTSTANDING**:
the branch MUST NOT merge to `main` until a human has read the change against this note and signed
off. The reviewers ran nothing; every runtime claim below remains unmeasured until Phase B.

Verdicts: correctness approve, security approve, compatibility approve, design approve, rules
changes required (documentation gaps in test files, below).

### Security-lens checklist (rules/security.md)

| Item | Result | Note |
|---|---|---|
| 1 Input validated at the boundary | n/a | Only authorization metadata and comments changed. |
| 2 Paths | n/a | No path-handling code touched. |
| 3 Shell | yes | Nothing spawns or builds a command. |
| 4 Config-file injection | n/a | No config-file writes touched. |
| 5 SQL | n/a | No SQL touched. |
| 6 AuthZ | yes | No bare `[Authorize]` or `.RequireAuthorization()` in `backend/src`; the five Host minimal endpoints are explicit; the six formerly bare sites name `AnyAuthenticated`; `SetupController` anonymity moved from the class to its two actions; `FallbackPolicy` is `AdminOnly`; the only `MapControllers` call is in `Program.cs`. Runtime behaviour unmeasured. |
| 7 Configuration documented | n/a | No new variables. |
| 8 Secrets not in logs or errors | yes | The fallback's 403 has an empty body. |
| 9 Crypto | n/a | Not touched. |
| 10 Surface | yes | No ports, daemons or outbound calls added. |
| 11 Dependencies | yes | None added. |
| 12 Agent rpc closed and typed | n/a | No agent change. |
| 13 Module reach into the agent | n/a | No manifest change. |
| Escalation | yes | This note exists, states the review is OUTSTANDING and the no-merge-to-main rule, and its plan matches the code. "Written first" rests on mtimes, as stated above. |

Compatibility lens checklist: existing routes keep their status codes per role (yes); the setup
flow stays anonymous on upgrade (yes); no endpoint is left without metadata (yes); no migration,
proto, agent or OS-matrix impact (n/a); rollback needs no data step (yes).

### Findings and decisions

No lens raised a blocking finding. Major findings (rules lens) and the decision on each:

| Severity | Finding | Decision |
|---|---|---|
| major | Private `const ProbePath` and field `_pg` in `AuthorizationFallbackEndpointTests` have no XML doc comment (rules/csharp.md requires one on every member, tests included). | Accepted. Fixers add the summaries. |
| major | Private field `_pg` without a doc comment in `CustomerSelfServiceEndpointTests`. | Accepted. Same fix. |
| major | Same in `EndpointAuthorizationPolicyTests`. | Accepted. Same fix. |
| major | Same in `LogoutEverywhereAuditTests`. | Accepted. Same fix. |
| major | Same in `SessionRevokeAuditTests`. | Accepted. Same fix. |

Minor findings and the decision on each:

| Lens | Finding | Decision |
|---|---|---|
| correctness, security, rules, design | The claim that a customer gets 403 and an administrator 404 on a path that matched no endpoint (comment in `RolePolicies.cs`, this note, `AuthorizationFallbackEndpointTests`) is unobserved. | Kept as UNMEASURED. Phase B runs the test. If a customer gets 404, the comment, the test and this note change together. |
| compatibility | The framework's authorization middleware may skip policy evaluation when no endpoint matched, so every caller would get 404 (as before, so no HTTP contract changes). | Same decision as above. The risk is a wrong claim, not a hole: an unmatched path serves nothing either way. If observed, the "behaviour that changes" section is rewritten to say nothing changes. |
| correctness, security | Stale wording ("written but had not been run", UNMEASURED and UNPROVEN markers) in this note and in `2026-08-30-auth-threat-note.md`. | Accepted. Replaced with measured results in the "Run evidence" section after Phase B, or removed if a test is not green. |
| correctness, security, compatibility, design | `git diff dev` shows unrelated deletions because `dev` is ahead of the branch base. | Accepted. Rebase or merge `dev` before the pull request, confirm the diff holds only #89 files, and repeat the bare-form grep and the class-level anonymity census (reviewer item 10). |
| security | A third-party module's bare `[Authorize]` still reaches customers because `DefaultPolicy` stays `AnyAuthenticated`. | Not changed. Already listed under "What this change does NOT close". Raising `DefaultPolicy` is an owner decision, raised again at hand-off. |
| security | The human second review is outstanding. | Open. Owner told at hand-off; no merge to `main`. |
| rules | Three reviewers read only `rules/README.md`, so the doc-comment gap was found by one lens only. | Accepted. The bare-form grep over `backend/src` is re-run at the end of Phase B and must return nothing. |

Closing: correctness, compatibility and design judge issue #89 closed by this change; the security
lens leaves that to the second review. Nothing here lifts the OUTSTANDING status.

## Run evidence

Recorded after the verification phase (2026-10-28). Everything here was observed on the branch
worktree unless marked otherwise. The human second review is still **OUTSTANDING**; nothing below
lifts it, and the branch must not merge to `main` until a human reviewer signs off.

### Gates

| Gate | Result |
|---|---|
| `maran check` | OK: dotnet 9.0.317, cargo, rustc, node, protoc and docker present. |
| `maran structure` | STRUCTURE-OK. |
| Scope check (`git diff` of frontend, agent, proto, installer; grep for migrations, project files, packages) | Empty: no proto, agent, SPA, migration or dependency change. |
| `maran format --check backend` | NO VERDICT: it queued behind another branch's job on the shared slot lock and did not finish. UNVERIFIED. |
| `maran test backend` (full suite) | NO VERDICT: three attempts were killed by memory pressure (the 5G scope was OOM-killed). No whole-suite total and no comparison with `scripts/test-baseline.txt` exists. UNVERIFIED; left to CI. |
| `maran api`, `maran mutate`, backend build with `-warnaserror` as a separate gate | Not run. Each test project that was run did build clean, which includes the repository's analyzer settings. |

### Measurements: before and after

"Before" is `dev` at `c47c53c` with this branch's new test files laid over it, in a private copy.
"After" is the current worktree (a `diff -rq` of `backend/` against the copy was empty). The
"before" is therefore not a bare `dev` tree with the issue's literal test endpoint.

Twelve `Maran.Host.IntegrationTests` classes, 100 tests: before 96 passed and 4 failed, after 100
passed and 0 failed.

| Request | Before | After |
|---|---|---|
| Customer, endpoint with no authorization metadata (test probe) | 200 | 403, empty body |
| Customer, path that matches no endpoint | 404 | 403 |
| Customer, wrong verb on `/api/v1/dashboard` | 405 | 403 |
| Anonymous, each of the three | 401 | 401 |
| Administrator, probe / unmatched path / wrong verb | 200 / 404 / 405 | 200 / 404 / 405 |

The 403 claim for a customer on an unmatched path and on a wrong verb, which this note carried as
UNMEASURED, is now observed, so the section above and the `RolePolicies.cs` comment stay as written.
On `dev` the route-table census failed and named seven endpoints with a bare or role-only authorize:
`GET /api/v1/dashboard`, `POST /api/v1/auth/logout-all`, `POST /api/v1/auth/two-factor/enrol`,
`confirm` and `disable`, `GET /api/v1/sessions` and `DELETE /api/v1/sessions/{id}`. It is green on
the branch, so the census can see a violation. The classes covering the self-service flows
(sessions, dashboard, two-factor, setup, auth, accounts `me` and invitations, sites authorization)
all pass on the branch.

Static re-check on the branch: no bare `[Authorize]` and no bare `.RequireAuthorization()` in
`backend/src` code (hits only in comments); `FallbackPolicy` is `AdminOnly`; no controller file lacks
an authorize or anonymous declaration (0 of 29); the anonymous pin holds 13 routes (9 controller
actions plus the 4 Host routes); `grep test-probes backend/src` is empty.

### Tests on the branch, unmutated

`Maran.Host.Tests` 290 of 290 (baseline 281 plus the 9 new `RolePoliciesTests`),
`Maran.ArchitectureTests` 91 of 91 (baseline 80 plus 11), the five new Host.IntegrationTests
classes 29 of 29. These are filtered or per-project runs, not the maran test verdict.

### Mutation proof

38 mutations were run with a private harness (backup outside the tree, one-line edit, touch, test
the affected projects, restore, `cmp`). Every restore was verified with `cmp`. 35 compiled and all
35 were killed by the tests named for them. Three did not compile as specified (a role claim that
orphaned a `using`, a bare `.RequireAuthorization()` that orphaned a `using`, and an
`[AllowAnonymous]` above an `[Authorize]`, which the analyzer rejects as ASP0026); each was replaced
by a compiling variant, and all three variants were killed.

- Fallback reverted to `AnyAuthenticated`: killed by `RolePoliciesTests` and by the three
  fallback integration tests.
- Fallback set to null: killed by the anonymous-refusal tests in both projects.
- Default policy changed to `AdminOnly`, or to a plain authenticated-user policy, or either policy's
  two-factor requirement removed: killed by `RolePoliciesTests`.
- Role claim changed from administrator to customer: killed by the unit tests and the
  administrator controls.
- Bare `[Authorize]` introduced on `LogoutEverywhereAsync`, on `DashboardEndpoint` and on
  `BackupsController`: killed ONLY by the census tests (architecture, Identity and the route-table
  census) and the per-module policy pin; the behaviour tests stay green, as expected.
- Attribute swaps between `AdminOnly` and `AnyAuthenticated` in Identity, Accounts, Sites, Backups,
  Tasks, Firewall, Licensing and Databases controllers: killed by the per-module policy tests.
- Added or removed `[AllowAnonymous]` (class level on `SetupController`, removed from
  `GetStateAsync`, replaced on a customer action): killed by the pin tests.
- Renamed `/health/live`, removed the Licensing assembly from the architecture walk, blinded or
  over-refusing census checkers, and a cross-customer predicate in the session revoke handler:
  killed.

Not run: the multi-line class-level anonymity variant (`SetupController` class attribute plus
deleting both action attributes; the class-only variant covers the same guard), the
action-level-passes-over-refuses checker variant, the Cron, Ftp and Sftp attribute swaps, and the
remaining checker mutations in the integration census. No survivor was found among those that ran.

### Left unverified

1. The format gate and the full backend suite (see above). A green full suite and the baseline
   comparison are required before merge; `scripts/test-baseline.txt` has not been updated here.
2. Reviewer items 8 and 10: the baseline growth, and merging `dev` forward (`c47c53c`) with both
   census searches repeated.
3. Reviewer item 9 remains partly open: the 12 classes ran, but the 31 `HttpStatusCode.NotFound`
   assertions in other test projects were not read, and those projects were not run.
4. The other modules' authorization integration tests (Databases, Ftp, Sftp, Backups, Ssl, Cron,
   Tasks) were not run. The branch changed no endpoint file in those modules; that is a reading of
   the diff, not a measurement.
5. The census walk's route count was not printed, so the 107 controller verb attributes plus 5 Host
   Map calls (static counts) were not reconciled with the walked total. A named-but-wrong policy is
   invisible to the census and is covered only by the per-endpoint tests.
6. One architecture test, `The_class_level_anonymity_check_accepts_action_level_anonymity`, also
   went red under the class-level `SetupController` mutation although it is meant to use only
   fixtures. It may read the real controllers; worth a look by the reviewer.
7. Runs were taken under heavy shared load with several memory kills; each reported class has its
   own totals line, and one class needed a re-run.
8. Stale wording elsewhere: the sentence "written but had not been run" in
   `2026-08-30-auth-threat-note.md` can now be replaced with the measured result above.
9. The human second review: OUTSTANDING.
