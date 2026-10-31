# Lists at scale: the sweep, the budget, the plans (issue #196, phase 4)

Measured 2026-10-31 on a workstation, PostgreSQL 16 (`postgres:16-alpine`) and 13
(`postgres:13-alpine`), over the scale dataset below. Every figure here is a measurement of that run,
not a promise; re-derive it with the commands in each section.

## 1. Every list the panel answers, and why it is or is not paged

The sweep looked for list endpoints that return a whole array and for fixed limits:

```
/usr/bin/grep -rn "\.Take(" backend/src --include=*.cs | grep -v Migrations
/usr/bin/grep -rn -A4 "\[HttpGet" backend/src --include=*.cs | grep "ProducesResponseType(typeof" | grep -v PagedResult
```

Positive control: the first command finds `QueryablePagingExtensions.cs:55`, the one pager every list
goes through.

| List | Decision | Why |
|---|---|---|
| Accounts, account choices, plans, plan choices, sites, certificates, databases, SFTP and FTP logins, backups, backup destinations, audit, tasks, sessions, bans, whitelist | Paged (phases 1–3) | Measured below. |
| `GET /api/v1/monitoring/accounts-disk` | **Paged in this phase**, and paged where the data lives | It returned one row per account, and the agent walked every home directory on each call. Now an `IPagedQuery` (search on the user name; sorts `username`, `quotaBytes`): the panel pages its own accounts, then asks the agent to measure only that page (`GetAccountsDiskUsageRequest.account_usernames`, contract revision 5). Used bytes cannot be a sort, because only the page is measured. |
| `GET /api/v1/firewall/rules` | Unpaged by decision | The removal check needs every rule the host runs (`ListRulesQueryHandler` remarks). Rules are what administrators add; they do not grow with accounts. |
| `GET /api/v1/cron-entries`, `GET /api/v1/cron-environment` | Unpaged, bound by construction | One account's crontab, read whole by the agent under `CRONTAB_CEILING` (256 KiB, `ops/src/cron/crontab_spool.rs`); a larger table is refused, never truncated. The environment is also held to `MaximumVariables` (32) on write. Does not grow with the number of accounts. |
| `GET /api/v1/sites/php-versions` | Unpaged, bound by construction | The PHP versions installed on the host: an administrator's installs, not a tenant's rows. |
| `GET /api/v1/monitoring/services` | Unpaged, bound by construction | One row per `AgentManagedService` value (five services). |
| `GET /api/v1/audit/actions`, `GET /api/v1/tasks/kinds` | Unpaged, bound by construction | The answer is the vocabulary of actions and `TaskKinds` the code writes. The read runs over tables that grow with accounts, so it is timed with the lists (section 3). |
| `GET /api/v1/tasks/running` | **Not settled here — follow-up** | Running tasks are bounded by work in flight, not by a constant, and the shell opens one stream per running task. Paging the read would not fix the stream fan-out, and the task tray is under change in another branch (#222). Needs its own issue. |
| Dashboard recent audit entries | Bound by a named constant | `RecentAuditEntries = 5` (`DashboardEndpoint`); the journal itself is the paged audit list. |
| `GET /api/v1/modules`, metrics chart | Bound by construction | The module registry; a chart's bucket count is fixed by its `ChartWindow`. |
| Site aliases inside `SiteDetailDto` | Not a list endpoint — finding | One site's aliases have no count limit in `CreateSiteCommandValidator`; only the request size bounds them. Worth a named maximum in its own issue. |

## 2. The dataset

`backend/tests/Maran.Host.IntegrationTests/Fixtures/ScaleDataset.cs` and `ScaleJournalDataset.cs`
write it through the modules' own contexts and entity constructors. Ids, names and instants are derived
from indexes and a fixed epoch, so two runs build identical tables.

| Rows | Count |
|---|---|
| Accounts (24 plans, every tenth suspended) | 1 200 |
| Sites, certificates (one per site) | 2 400 each |
| Databases | 2 400 |
| SFTP logins, FTP logins | 1 200 each |
| Backups (completed, failed, scheduled) | 3 600, plus 20 destinations |
| Audit events | 12 000 |
| Tasks (completed and failed) | 6 000 |
| Administrator sessions, bans, whitelist entries | 300, 1 000, 200 |

## 3. The budget and what it measured

`ListPerformanceBudgetTests` boots one panel over the dataset (class fixture `ScaleDatasetFixture`)
and times every shape the SPA sends — first page, last page, a search, each filter, each sort in both
directions — through the in-process test server: authentication, binding, validation, handler, SQL and
JSON. Three warm-up runs, then thirty measured; the 95th percentile must be **under 200 ms**. It runs
in the existing integration lane (`maran test backend`), so CI measures it on PostgreSQL 16 and 13.
The account disk list reaches a stub agent, so its figure is the panel's share only.

Slowest shape per list, p95 in ms:

| List | PG 16 | PG 13 |
|---|---|---|
| audit (search) | 25.3 | 66.8 |
| sites | 16.3 | 23.1 |
| backups | 12.1 | 17.2 |
| tasks | 8.4 | 15.9 |
| databases | 8.1 | 13.0 |
| bans | 11.1 | 8.5 |
| ftp / sftp logins | 9.3 / 7.8 | 10.9 / 9.0 |
| account choices / accounts | 8.3 / 6.2 | 7.5 / 9.1 |
| certificates | 6.3 | 10.6 |
| account disk | 6.1 | 8.2 |
| whitelist, sessions, plans, destinations | ≤ 7.7 | ≤ 8.1 |
| audit actions, task kinds | — | 7.4, 9.1 |

Why 200 ms: a list answering under it still reads as an immediate response to a click, and it sits
three times above the slowest shape on PostgreSQL 13 and eight times above it on 16. A query that loses
its index, or a handler that starts paging in memory, costs far more than that at this size.

## 4. EXPLAIN (ANALYZE, BUFFERS) review

Method: a one-off test, not committed, added an EF command interceptor to every module context, seeded
the dataset, ran `ANALYZE`, sent every shape of section 3 once, and ran `EXPLAIN (ANALYZE, BUFFERS)` on
each captured statement with its own parameters (325 statements). What it found:

- **Every statement finished under 17 ms** on PostgreSQL 16; most under 2 ms.
- **The total is a sequential or index-only scan of the filtered set, by nature.** Every page asks for
  `count(*)`, so a list whose filter is not selective reads the whole table whatever its indexes are.
  An index pays when a filter is selective or when the default sort can stop after one page.
- Search is `ILIKE '%term%'` and cannot use a B-tree; it scans. The slowest statement of the run is the
  audit search (16 ms over 12 000 rows). A trigram index would need the `pg_trgm` extension, which the
  installer does not create; not done here.
- Secondary sorts (`audit?sort=actor`, `tasks?sort=subject`) sort the table (6–12 ms). Left unindexed:
  they are not the default, and an index on the audit actor would cost a write on every audited request.

Two indexes added, each where the plan read a whole growing table for a selective or default shape:

| Index | Migration | Before | After |
|---|---|---|---|
| `IX_Certificates_SiteId` | `Ssl/…_AddCertificatesSiteIdIndex` | `certificates?siteId=` sequential scan of all 2 400 certificates for 1 row | Index-only scan, 0.12 ms |
| `IX_Backups_StartedAt` | `Backups/…_AddBackupsStartedAtIndex` | Administrator's first page: sequential scan and top-N sort of 3 600 backups, 1.83 ms | Backward index scan plus incremental sort over 26 rows, 0.08 ms; the `from`/`to` window becomes an index-only scan |

The composite `IX_Backups_AccountId_StartedAt` stays: it serves a customer's own listing. Incremental
sort (PostgreSQL 13 and later, the panel's floor) is what lets a single-column index serve the
`(StartedAt, Id)` order.
