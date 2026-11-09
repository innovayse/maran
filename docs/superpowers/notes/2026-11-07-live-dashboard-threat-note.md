# Threat note: the live dashboard, an authenticated metrics stream and new root-agent reads (issue #199)

`rules/security.md` requires a threat note, written first, for a change that adds a long-lived
authenticated stream, new reads performed by the root agent, and a new mutating root rpc. This change
adds a server-sent-events dashboard stream (`GET /api/v1/dashboard/stream`), per-viewer layout
preferences, and, in later slices, three agent rpcs: two read-only host measurements, one read-only
per-account resource reading, and one MUTATING rpc that reloads or restarts a managed service. It is
written BEFORE the code: every statement below is a requirement the code must meet, not a description
of code that exists. Where the code lands and differs, the code is wrong or this note is amended in
the same change.

**Second reviewer: OUTSTANDING (#331).** The work is carried out by agent sessions, which cannot
obtain one. The branch may exist and MUST NOT merge to `main` until a human reviewer has read this
note against the code (rules/security.md "Sensitive change escalation"). The owner is told at
hand-off that this surface carries an outstanding review. A note recording the debt is the minimum,
never the discharge of it.

## State of the tree when this note was written (2026-11-07, branch `feature/146-425-security`)

Measured by reading the tree, not assumed:

- No stream, feed, ring buffer or layout code for this issue exists. A search of `backend/src`,
  `frontend/src` and `proto` for `LiveMetrics`, `DashboardStream` and `dashboard/stream` finds nothing.
- The dashboard is today a composed Host read: `Maran.Host/Dashboard/DashboardEndpoint.cs` with
  `DashboardSectionReader`, which has no time bound (it catches cancellation and any exception and
  nothing more). A customer receives an empty `CustomerDashboardDto`.
- The stream machinery this change reuses exists: `Maran.Sdk/Streaming/EventStreamWriter.cs` and
  `EventStreamFrame.cs`, and `Maran.Host/RateLimiting/SiteLogStreamRateLimitPolicy.cs` (a concurrency
  limiter, not a window). The writer writes no `id:` and reads no `Last-Event-ID`; back-fill is
  therefore an explicit `since` parameter of the new endpoint.
- The agent measures little today: `GetHostMetrics` returns total CPU, memory, root filesystem, summed
  network counters and load. Per-core CPU, iowait, steal, swap, cache, per-mount usage, inodes, disk
  throughput, per-interface counters, connection count, host identity, per-account CPU and memory, and
  any way to reload or restart a service do not exist (the managed-service type is a read-only status
  enum). `PROTO_VERSION` is 20 in `agent/crates/agent/src/services/system/system_service.rs`.
- Files this change will need are modified and uncommitted by other work in the tree
  (`frontend/src/pages/SystemStatusPage.vue`, `frontend/src/components/dashboard/DashboardAttentionList.vue`,
  `backend/src/Maran.Host/Dashboard/{AttentionReader,AttentionTally,DashboardAttentionDto}.cs`).
  Sequencing is in "Owner decisions this note assumes".
- The stream-lifetime question is not new: the running-tasks stream note
  (`2026-10-31-running-tasks-stream-threat-note.md`) records that a revoked token does not end an open
  stream. This stream closes that gap for itself with a lifetime cap (surface 2).

## Delivery in slices, and which slice each section governs

One issue, four sequential slices, each green alone. A slice that touches the proto does exactly one
revision bump. This note is the note for all four; a slice's section must be present and reviewed
before that slice's code, which is why S4 is isolated.

| Slice | Delivers | Proto / agent | Governed by |
|---|---|---|---|
| S1 Live core | the stream, ring buffer and back-fill, chart primitive, widgets for what the panel already measures, quick actions, per-viewer layout | none | surfaces 1, 2, 3, 6, 8 |
| S2 Host detail and history | per-core, iowait, steal, swap, cache, per-mount and inodes, disk throughput, per-interface, connections, host identity strip, 1 h / 24 h / 7 d / 30 d ranges with rollups | 2 read-only rpcs, 1 bump | surface 4 |
| S3 Accounts | top accounts by CPU, memory and disk, customer live dashboard for the active account | 1 read-only rpc, 1 bump | surfaces 1 (tenant), 5 |
| S4 Service control | reload or restart a managed service | 1 MUTATING rpc, 1 bump | surface 7 |

## What is added

1. **The stream.** An authenticated SSE endpoint. For an administrator: host metrics every 2 s from
   one shared in-memory feed (never one agent call per client), service states, hosting totals,
   attention and activity, pushed on change. For a customer: only frames about their active account.
   A `since` back-fill parameter and a 1 hour in-memory ring (1800 points) serve reconnects.
2. **Per-viewer layout preferences**, `GET` and `PUT api/v1/me/dashboard-layout`, owned by the user.
3. **Agent read rpcs (S2, S3):** `GetLiveHostMetrics {}`, `GetHostIdentity {}`,
   `GetAccountsResourceUsage { repeated AccountName }`.
4. **One mutating agent rpc (S4):** `RestartManagedService { ManagedService, RestartAction }`.
5. **Rollups (S2):** a new table of 5-minute and 1-hour aggregates in the Monitoring schema (expand
   only; no existing column is touched), so `maran migrate guard` has nothing to refuse.

No new port, daemon, listener or outbound call is added (rules/security.md item 10): the stream is an
authenticated route on the existing listener, and the feed reads the agent over the existing unix
socket. If a reviewer reads otherwise, this note is wrong and the change needs a spec change first.

## What an attacker could do with each surface, and why it is safe once the requirements hold

### 1. Read host-wide figures, or another tenant's, as a customer (S1, S3)

Goal: a customer, a reseller or an API key sees the host's CPU, mounts, interface names, services or
another account's figures.

- The frame source is chosen in ONE place from the closed `Caller` type. `AdministratorCaller` gets
  `HostDashboardStreamSource`, which takes `ILiveMetricsFeed`. `CustomerCaller` gets
  `AccountDashboardStreamSource`, whose constructor does not take the host feed at all, so a customer
  path that reaches host frames does not compile.
- The account source reads its account id from `ICurrentUser.Caller` (the token), never from a
  parameter. The endpoint binds no account id; `since` is the only input.
- `ResellerCaller`, `ApiKeyCaller` and anonymous callers are refused with a typed 403 BEFORE the
  response headers go out, so nothing was streamed. A stream is a read; a reseller has its own
  overview page and gets no stream.
- View as: the endpoint declares `[ViewAsAccess(Read)]` (a read; no credential or running work is
  created). An operator viewing as a customer sees that customer's frames and nothing host-wide.
- Tests (each refusing check has an inverse control, rules/testing.md): a customer receives `account`
  frames and zero `host` frames while the administrator control receives both; a customer of account A
  never receives account B's figures, asserted with figures that differ per account (the IDOR shape for
  a stream); reseller, API key and anonymous are refused, each with an accepting control.
- Stated limit: the other endpoints of this change (layout) are caller-scoped, not tenant-scoped, so
  there is no row to be 404 about. The tenant boundary of this change is the stream's account source.

### 2. Exhaust the server with streams (S1)

Goal: open many streams, or one that never ends, or one a slow client makes the server buffer.

- Bounds are named options in `DashboardStreamOptions`, validated at start: concurrent streams per
  user (default 4) and per process (default 64) through a concurrency limiter with `QueueLimit = 0`
  that answers 429 at once (the shape of `SiteLogStreamRateLimitPolicy`; a fixed window is the wrong
  primitive for a stream, its remarks say why); heartbeat 15 s; **maximum stream lifetime 15
  minutes**, after which the server ends the stream with a terminal `end` frame
  (`reason: "renew"`) and the SPA reopens it silently, which re-checks the token.
- Per-connection output is a bounded channel of 2 with drop-oldest, so a slow client sees the latest
  reading and nothing buffers without bound.
- `history` back-fill is capped at the ring size. `since` is parsed strictly: a bad value is the
  standard 400; a value older than the ring is clamped to it.
- Cost on the host is independent of the number of streams. The feed is demand-driven (it polls the
  agent only while at least one subscriber exists, stops 30 s after the last leaves), makes one call
  per tick, and never overlaps itself (a tick that overruns is skipped). Ten tabs cost the agent what
  one costs. A performance-budget test reads a counting fake agent to hold this.
- Honest limit: the lifetime cap bounds how long a revoked token keeps a stream, to 15 minutes at
  worst. It does not make revocation immediate.

### 3. Smuggle content through a name (S1, S2)

Goal: a hostname, mount point, interface name, service detail or account name that breaks a frame or
the page.

- Frames are serialized JSON through `EventStreamFrame`; a newline inside a value cannot end a frame.
- Strings that come from the host (hostname, mount point, interface, OS name, kernel release) are
  length-capped and stripped of control characters in the Sdk contract constructor, reusing the
  existing text-sanitising utility rather than writing a second one.
- The SPA renders every string as text and never as HTML.

### 4. The agent's new host reads (S2)

Goal: make the root agent read a path, a unit or a file the caller chose.

- `GetLiveHostMetrics {}` and `GetHostIdentity {}` take NO caller-supplied path, unit or name. They
  read a fixed set of kernel files (`/proc/stat`, `/proc/meminfo`, `/proc/loadavg`, `/proc/net/dev`,
  `/proc/net/sockstat`, `/proc/diskstats`, `/proc/uptime`, `/proc/sys/kernel/hostname`,
  `/proc/sys/kernel/osrelease`, and the OS description file) and call `statvfs` on mount points taken
  from `/proc/self/mountinfo`.
- The mount list is filtered to an allow-list of real filesystem types; pseudo and network
  filesystems are skipped, so a bind or FUSE mount cannot make root call `statvfs` on a hostile path.
  A customer cannot create mounts at all, but the filter does not rely on that.
- Output is bounded (at most 64 mounts, 64 interfaces, 512 cores). Names are printable and capped. A
  malformed or truncated file is a typed error, never a panic. No process is spawned, no shell.
- Both are classified READ-ONLY in `audit/read_only_rpcs.rs`; the census fails otherwise.
- Where a path or a file name differs by distribution, the difference lives in the `distro` crate, and
  is found by reading both families' documentation, not by running one machine (rules/architecture.md).

### 5. The per-account reading (S3)

Goal: use the rpc as an oracle for which accounts exist, or to read another account's slice.

- `GetAccountsResourceUsage` takes at most 500 `AccountName` values, each re-validated, duplicates
  collapsed. The cgroup directory is derived from the uid the AGENT resolves for the account
  (`user-<uid>.slice`), never from a string path. It reads three counters.
- An account that does not exist, and a slice that is stopped, are both "no reading": an unknown name
  is simply absent from the answer, so the rpc is not an existence oracle.
- The panel asks only for accounts it owns; a customer's stream asks for exactly its own active
  account. Read-only class.
- Where cgroup v2 is not mounted the answer is "unmeasured", never zeros.

### 6. Layout preferences (S1)

- The row is owned by `UserId` from the token, never from the body. It carries no `AccountId` (it is
  not tenant data; an `AccountId` here would be the wrong key).
- Keys are validated against the closed Sdk vocabularies (`DashboardWidgetKey`, `DashboardActionKey`);
  an unknown key is a 400; at most 32 entries; whole-list replace.
- The write is `[ViewAsAccess(Refused)]`: a look must not persist anything for the viewed user.
- The rows are removed with the user. No user-deleted message is known to exist today; if none does,
  that follow-up is recorded with the personal-data erasure work (#146) rather than assumed.

### 7. Reloading or restarting a service (S4) - the most dangerous addition

Goal: use a root rpc to take the host or the operator's way back in down, or to run something else.

- The rpc takes a closed `ManagedService` enum and a closed action enum `{ RELOAD, RESTART }`. It
  never takes a unit name. The distro adapter maps the enum to the unit and to the actions allowed for
  it, so no platform literal appears in `ops`.
- **Never** the agent itself, the panel API, PostgreSQL, or the SSH daemon: a restart that fails would
  lock the operator out. The allow-list is the set of enum values the adapter answers for, and those
  four have no entry.
- The web server and PHP-FPM default to RELOAD (graceful, no dropped customer connections). The
  database server is RESTART-only and the interface asks for confirmation.
- A web-server reload is preceded by the existing configuration validation, so a broken tree refuses
  instead of taking every site down.
- Per-service cooldown (30 s) and refusal while one is in progress, in the shape of the existing
  per-account guard: it never waits, so it adds no wait-for edge to the lock table in
  `rules/rust.md`. The implementer must re-argue that in the doc comment and add the table row.
- MUTATING class in `audit/mutating_rpcs.rs`, audit slot settled. C# side: administrator only,
  `[ViewAsAccess(Refused)]`, no API-key scope, `[Idempotent]`, journalled (who, which service, which
  action, outcome), rate limited. Monitoring's manifest declares the agent capability the guard
  requires; `AgentCapabilityGuard` throws on an undeclared one.
- This section is the one a reviewer reads first and the one that may be refused outright by the
  owner (decision D8).

### 8. Quick actions

Quick actions are links, not endpoints. No quick action introduces a mutating endpoint of its own
except surface 7; each opens the owning page, which enforces its own policy server-side. The SPA's
role and module filtering is advice only and errs permissive (rules/security.md item 6).

## What a reviewer must check

1. `AccountDashboardStreamSource` has no path to the host feed: its constructor, its DI registration,
   and the customer test that receives zero host frames.
2. The stream refuses reseller, API key and anonymous before headers; the lifetime cap and the
   per-user and per-process caps exist, each with a test and an inverse control; the clock in the
   lifetime test is injected (no sleeps).
3. The feed polls the agent only while subscribed and never concurrently with itself (the counting
   fake in the performance-budget test).
4. For every new rpc: its class in `read_only_rpcs.rs` or `mutating_rpcs.rs`, bounded output, no
   request-derived path or unit. For `RestartManagedService`: the allow-list (the four excluded
   services have no entry), the cooldown, the in-progress refusal, validation before reload, and that
   no code path accepts a unit name.
5. That nothing in a frame, an error or a journal entry carries a secret, a path outside the closed
   set, or a machine identifier.
6. That the section-reader timeout added for this change turns a slow provider into "unavailable" and
   does not turn a failure into an empty value.
7. That each slice bumps the proto exactly once and the baseline change is its own reviewed commit
   (rules/proto.md).

## What the author could not verify

- Behaviour behind a buffering reverse proxy. The tests run on an in-process test server, the limit
  `EventStreamWriter` already states. An operator running a proxy needs an unbuffered route and a read
  timeout above the 15 s heartbeat.
- cgroup v2 behaviour on every supported family and version (Ubuntu 22.04 and 24.04, Debian 12 and 13,
  AlmaLinux and Rocky 9 and 10), and on a host booted with cgroup v1. `/proc/diskstats` semantics for
  device-mapper and NVMe devices need the real-host agent lane.
- That a customer's processes cannot influence the figures root reads in a way that matters (cgroup
  counters are kernel-maintained; this is stated, not proven).
- The API's CPU and memory cost with ten open tabs on a real host. The integration test counts agent
  calls and allocations; wall-clock cost needs the owner's run.
- That the working-tree edits to the shared dashboard files are complete, and what order they land in.
- Nothing was built or run by the author of this note.

## Owner decisions this note assumes

None is recorded as approved. These are the recommendations of the plan
(`research_notes/plan-199.md`, section 6); if the owner answers differently, this note is amended
before the affected slice's code.

- D1: four slices from one issue, each its own PR.
- D2: on-demand sampling (the feed polls only while a dashboard is open; a cold start is filled from
  stored 1-minute samples).
- D3: a new SVG live-chart component; the single-SVG-site sentence in `rules/vue.md` then names two
  sites. A rules change needs the owner's approval; this note does not make it.
- D4: "small forms in a modal on the dashboard" is met by navigating to the owning page with a query
  that opens that page's own dialog (no cross-feature import, one validation path).
- D5: migrate the running-tasks store to the shared stream lifecycle.
- D6: data with no source today is deferred and shown as nothing, never as zero: bandwidth per account,
  SSH and FTP failed-login counts, site up/down probing, restart counts.
- D8: which services may be reloaded or restarted (web server, PHP-FPM, database server, cron daemon;
  never the SSH daemon, the agent, the panel or PostgreSQL), and whether the capability is wanted at
  all given the lock-out risk.
- D9: administrators and customers only; resellers get no stream.
- D10: 15-minute maximum stream lifetime with silent renewal.
- D11: no spec change is needed for the new stream (no new port, daemon or outbound call).
- Sequencing assumption: the first-run checklist and the delivery-failure attention row land first
  (plan 142-164); this change then rebases onto them and neither forks nor reimplements either.

## Amendments from the S1 code (the backend of the stream, written after it)

Written when the S1 backend landed, because the note's first paragraph says the code must match it or the
note must be amended. Each item is where the code differs from, or is narrower than, a sentence above.

- **Only the host readings are one shared poll.** The services, the counts, the attention block and the
  newest audit entries are read once per CONNECTION, through the bus, under the caller's own request, on
  intervals of 30, 10 and 5 seconds (`DashboardStreamOptions`). A shared background reader was the
  intention above, and it cannot work: the module handlers behind those reads resolve their tenant
  context from the current caller, and a reader with none gets an anonymous caller's empty answer. The
  consequence for surface 2: the service-status read IS an agent call per connection per 30 seconds, not
  one per tick for everybody. It is bounded by the per-user cap (4), the per-process cap (64) and the
  interval, each read is bounded by `DashboardSectionReader.DefaultSectionTimeout` (3 seconds, a slow
  module is "unavailable"), and a section is sent only when its content differs from the last one sent.
  The performance-budget test counts the HOST-READING calls only and says so.
- **S1 serves administrators only, and the refusal is the authorization policy's.** The route requires
  the administrator policy, so a customer, a reseller, an API key and an anonymous caller are answered
  403 or 401 before any header is written. There is no `Caller` switch in the endpoint yet and no
  `ViewAsAccess` declaration (an administrator-only action is exempt from it); both arrive with the
  customer source in S3. Surface 1's customer paragraph is therefore S3's, not S1's, and its tests are
  S3's.
- **`ILiveMetricsFeed.Subscribe()` returns a registration at once, instead of `SubscribeAsync`
  returning an enumerable.** An async iterator registers only at its first move, which left a window
  between reading the back-fill and starting to listen in which a reading was lost or sent twice. The
  stream subscribes first and skips live readings not newer than the newest back-filled one.
- **The per-process cap is `DashboardStreamGate`, not the rate limiter.** One policy yields one
  partition and the per-process bound is a different partition. A stream refused by either cap is the
  panel's one rate-limited answer (429, code `HostRateLimited`, `Retry-After`); a code of its own was
  not added.
- **The lifetime is judged against the injected clock at every wake of the stream (every
  `TickMilliseconds`, 1 second by default)**, so a stream ends at most one tick after its lifetime.
- **Not built in S1:** the provider queries listed in the plan (failed logins, two-factor coverage,
  firewall state, accounts by status, SFTP and FTP logins), the notification feed inside `activity`, and
  the per-viewer layout (surface 6, a separate task). Nothing in this note's requirements depends on them.

## Second review: OUTSTANDING (#331)
