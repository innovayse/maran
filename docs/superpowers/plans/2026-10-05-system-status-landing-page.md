# System Status Landing Page Implementation Plan

> **For agentic workers:** execute task-by-task. Per `rules/testing.md` and `CLAUDE.md`,
> implementation comes first and tests follow in a dedicated pass — this plan is ordered that way
> deliberately and does NOT use TDD.

**Goal:** Turn `/`, the screen every sign-in lands on, from one sentence into the server's status:
what the machine is doing, whether what must be running is running, what is on it, and what needs
attention.

**Architecture:** One new read endpoint, `GET /api/v1/dashboard`, in `Maran.Host`. It composes the
answer from the queries the modules ALREADY publish, dispatched over Wolverine. No module gains a
query, no module learns about another, and the Host is the only place allowed to see them all
(`ModuleIsolationTests`). The SPA gets one store and one page reading one endpoint.

**Tech Stack:** C# .NET 9 + Wolverine (backend), Vue 3 + TypeScript + Tailwind + the panel's UI kit
(frontend), Playwright (end-to-end).

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md`; issue #70 states the gap and the
screen's content.

## Global Constraints

- Doc comments on ALL production code, private included. One file = one type/unit (`rules/`).
- Every repository file is English. Russian only in chat.
- Frontend: const arrow functions only; UI kit components only, never raw HTML elements for
  anything the kit covers; `useApi`/`apis` composables are called from Pinia stores ONLY.
- Security enforcement is the backend's. SPA checks are advice and must err permissive
  (`rules/`, memory "security logic on the backend").
- No new user-facing string without all three locales (en, ru, hy) — the resource parity law
  fails the build otherwise.
- `maran check`, `dotnet test`, `npm run lint && npm run typecheck && npm run build` must pass.

---

### The authorization decision, stated once

This is the single thing in this plan that can be got dangerously wrong, so it is written down
before any task refers to it.

Every module's controller carries its own `[Authorize]` policy; its query HANDLER carries none.
Dispatching `ListAccountsQuery` from the Host therefore runs a query whose HTTP surface is
administrators-only, with no policy in the way. The endpoint must supply that decision itself:

- The route is `[Authorize]` — a signed-in caller only, never anonymous. `/api/v1/modules` is
  anonymous because it describes composition; this one describes the server's contents.
- Every server-wide section (resources, services, counts, attention, recent audit) is read ONLY
  after `ICurrentUser.IsAdmin` is confirmed true. `ICurrentUser` fails closed, so an
  unauthenticated or non-admin caller reads `false`.
- A non-administrator receives the health verdict the page shows today and nothing else. That is
  not a stub: a customer's landing screen is the client zone, which is issue #49's subject, and
  inventing a second one here would be the thing that issue then has to undo.

Any later section added to this endpoint inherits that rule or it does not go in.

---

### Task 1: The endpoint's shape

**Files:**
- Create: `backend/src/Maran.Host/Dashboard/DashboardDto.cs`
- Create: `backend/src/Maran.Host/Dashboard/DashboardCountsDto.cs`
- Create: `backend/src/Maran.Host/Dashboard/DashboardAttentionDto.cs`

**Interfaces:**
- Produces: `DashboardDto(bool IsAdministrator, HostMetricsDto? Resources,
  IReadOnlyList<ServiceStatusDto> Services, DashboardCountsDto? Counts,
  DashboardAttentionDto? Attention, IReadOnlyList<AuditEventDto> RecentAudit)`.
  Every administrator-only section is NULLABLE, and null means "not for this caller" — the one
  shape both audiences receive, so the SPA has no second contract to drift from.
- `DashboardCountsDto(int Accounts, int Sites, int Databases)` — three, not four. A server-wide
  count of scheduled tasks was in the issue and is NOT here: the Cron module keeps no table, the
  agent reads one account's crontab at a time, and a count would be one privileged agent call per
  account — making the landing page the panel's most expensive screen. The reason lives in the
  DTO's own doc comment so it is not rediscovered.
- `DashboardAttentionDto(int CertificatesExpiringSoon, int FailedBackups, int BannedAddresses,
  int FailedTasks)` — counts, not lists: the page links to the screen that holds the detail, and
  a landing page that embeds four tables is four screens badly.

- [ ] **Step 1:** Write the three records with doc comments on every member, including the
      "null means not for this caller" rule on each nullable section.
- [ ] **Step 2:** `dotnet build backend/` — expect success.
- [ ] **Step 3:** Commit.

---

### Task 2: The endpoint

**Files:**
- Create: `backend/src/Maran.Host/Dashboard/DashboardEndpoint.cs`
- Modify: wherever `MapModuleCatalogue()` is called (`Program.cs` / endpoint wiring), adding
  `MapDashboard()` beside it.

**Interfaces:**
- Consumes: Task 1's DTOs; `IMessageBus`; `ICurrentUser`; and these EXISTING module queries —
  `GetHostMetricsQuery`, `ListServiceStatusesQuery`, `ListAccountsQuery`, `ListSitesQuery`,
  `ListDatabasesQuery`, `ListCertificatesQuery`, `ListBackupsQuery`,
  `ListBansQuery`, `ListTasksQuery`, `ListAuditEventsQuery`.
  Confirm each query's exact constructor and result type before writing the call; do not guess a
  signature.
- Produces: `GET /api/v1/dashboard`.

- [ ] **Step 1:** Map the route with `[Authorize]`, following `ModulesEndpoint`'s file shape.
- [ ] **Step 2:** Return `new DashboardDto(false, null, [], null, null, [])` when
      `ICurrentUser.IsAdmin` is false, and return before dispatching ANY query.
- [ ] **Step 3:** For an administrator, dispatch the queries concurrently and compose. Each
      section is wrapped so that one refusal or one failure empties THAT section and leaves the
      rest — the agent being unreachable must not blank the counts, which come from the database.
      Document why per section, the way `monitoring.ts` documents its split reads.
- [ ] **Step 4:** Certificates "expiring soon" uses a named constant with the reason for the
      number in its doc comment, not a bare literal.
- [ ] **Step 5:** `dotnet build backend/` — expect success.
- [ ] **Step 6:** Commit.

---

### Task 3: The store

**Files:**
- Create: `frontend/src/composables/apis/useDashboardApi.ts`
- Create: `frontend/src/stores/dashboard.ts`
- Create: `frontend/src/types/dashboard.ts`

**Interfaces:**
- Produces: `useDashboardStore()` exposing `isAdministrator`, `resources`, `services`, `counts`,
  `attention`, `recentAudit`, `loading`, `isLoaded`, `errorMessage`, and `load()`.
- Mirrors Task 1's DTOs field-for-field in `types/dashboard.ts`, with the nullability intact.

- [ ] **Step 1:** Write the api composable (one `get`), the types, and the store — the store calls
      the composable, the page never does.
- [ ] **Step 2:** Store backend-localized `title`/`detail` verbatim; generate no error text.
- [ ] **Step 3:** `npm run typecheck` — expect success.
- [ ] **Step 4:** Commit.

---

### Task 4: The page

**Files:**
- Modify: `frontend/src/pages/SystemStatusPage.vue`
- Create: `frontend/src/components/dashboard/DashboardResourceCard.vue`
- Create: `frontend/src/components/dashboard/DashboardServiceList.vue`
- Create: `frontend/src/components/dashboard/DashboardCountGrid.vue`
- Create: `frontend/src/components/dashboard/DashboardAttentionList.vue`
- Modify: `frontend/src/locales/en.json`, `ru.json`, `hy.json`

- [ ] **Step 1:** Keep today's four health branches EXACTLY as they are — the page is still the
      always-available route, and an unreachable panel must read as it does now.
- [ ] **Step 2:** Add the administrator sections below the verdict, each from the UI kit, each
      hidden when its store section is null. Every count links to the screen that owns the detail.
- [ ] **Step 3:** A zero is a real answer and must render as `0`, never as an empty state — a
      freshly installed server has no accounts, and a blank panel there would read as a failure.
- [ ] **Step 4:** All three locales, same keys.
- [ ] **Step 5:** `npm run lint && npm run typecheck && npm run build` — expect success.
- [ ] **Step 6:** Commit.

---

### Task 5: Tests, in their own pass

**Files:**
- Create: `backend/tests/Maran.Host.Tests/Dashboard/DashboardEndpointTests.cs`
- Create: `backend/tests/Maran.Host.IntegrationTests/DashboardEndpointIntegrationTests.cs`
- Create: `frontend/e2e/dashboard.spec.ts`

- [ ] **Step 1:** The authorization claims first, because they are the ones that matter: an
      anonymous caller is refused; a customer gets every administrator section null AND no module
      query is dispatched (assert on the bus, not only on the response — a response that happens
      to be empty would pass a weaker check while the data had already been read).
- [ ] **Step 2:** An administrator gets every section; one failing section empties only itself.
- [ ] **Step 3:** A zero count renders as `0` and not as an empty state.
- [ ] **Step 4:** Mutation-check each new test: make the production code wrong on purpose, confirm
      the test fails, put it back. A test that cannot fail is not a test.
- [ ] **Step 5:** `dotnet test` and the Playwright suite — expect 0 failures.
- [ ] **Step 6:** Commit.

---

### Task 6: Verify on a real server

- [ ] **Step 1:** Build and install on the Ubuntu server, sign in as an administrator, and read the
      page in a browser at full page height.
- [ ] **Step 2:** Check every number against the host: the counts against the database, the
      resources against `top`/`df`, the services against `systemctl is-active`. A dashboard whose
      numbers are not the server's numbers is worse than the one sentence it replaced.
- [ ] **Step 3:** Confirm the customer view shows no administrator section, signed in as a customer.
- [ ] **Step 4:** Comment on #70 with what was measured, and close it.
