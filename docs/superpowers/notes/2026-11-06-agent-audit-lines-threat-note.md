# Threat note: one audit line per mutating agent rpc (issue #106)

`rules/security.md` requires a threat note, written first, and a second reviewer for a change to the
privileged request path. This change puts a tower layer in front of every service of the root agent
(`maran-agent`, the only root process). This note is written BEFORE the code: every statement below
is a requirement the code must meet, not a description of code that exists. Where the code lands and
differs, the code is wrong or this note is amended in the same change.

**Second review: OUTSTANDING — must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch `fix/106-107-agent-audit-lines-and-unit-scores`
MUST NOT merge to `main` until a human reviewer has read the #106 half (rules/security.md "Sensitive
change escalation"). The owner is told at hand-off that this surface carries an outstanding review.
Nothing here discharges it. The #107 half of the branch (unit hardening scores in CI) touches no
request path and is not covered by this note.

The change also amends `rules/rust.md` "Logging" (see the last section). The issue asks for it, and
the pull request is the vehicle for the owner's approval of that amendment; the rules change by PR
with the owner's approval, never silently.

## What is added

1. A tower `Layer` installed with `Server::builder().layer(...)` in `agent/crates/agent/src/server.rs`,
   wrapping every service, in a new folder `agent/crates/agent/src/audit/`.
2. A closed, hand-written classification of every rpc path as mutating or read-only, checked by a
   census test against `proto/agent/v1/contract-baseline.txt`.
3. A per-request `AuditSlot` that a handler fills with validated values it already holds.
4. One line per mutating rpc, emitted with `tracing` at INFO on the target `maran_audit` to stdout,
   which systemd captures into the journal under the agent's unit.

Not added: no daemon, no port, no rpc, no proto change (no `PROTO_VERSION` bump), no file, no
database, no outbound call, no new crate (tower moves from a dev-dependency to a dependency;
`http-body`, `http-body-util` and `pin-project-lite` are used only if already in `Cargo.lock`, each
justified in the pull request).

## 1. What the line is for

The line is the **independent trail of what the root agent actually did**, for the case where
`maran-api` and its database are compromised.

The panel already has an audit table in PostgreSQL. A compromised API process (running as the
unprivileged `maran` user) can write anything to that table and can delete from it with the
database credentials it holds. It is the trail of what the API says it asked for. The agent's line is
a second trail, written by a different process, in a different trust domain, to a store the API cannot
rewrite: the journal belongs to root, and a `maran` process cannot edit or delete an entry
already in it. An operator reconciling "the panel says nothing happened" against "the agent says a
database was dropped" is the use case.

What the line records, in order: the method (the agent's own constant, not the caller's string), the
outcome the agent reached, the duration, and the account and the site domain when the handler had
validated them. It is a record of **requests the agent received and the result it produced**. It is not
an authorization log, not a record of who the human was (the agent does not know), and not a record of
the effect on the host beyond the agent's own verdict.

### What the layer sees, and what it cannot

- **A compromised API that sends a request.** The line is written by the agent from its own view:
  the path it routed, the typed values it validated, the verdict it computed. The API cannot choose
  the text of the line, cannot make the agent omit a mutating call (the layer wraps the router, so a
  mutating path is logged before any handler runs), and cannot back-date or edit it afterwards. What
  the API CAN choose is which valid account name or domain it asks about, and that is the truth the
  line is meant to preserve: "the agent was asked to drop the database of account X at time T".
- **A compromised root agent can forge anything.** The layer runs inside the process it is meant to
  watch. An attacker with code execution in `maran-agent`, or root on the host, can write any journal
  line, suppress the layer, or edit the journal. This trail is NOT evidence against a compromised
  agent or a compromised host, and nothing in this change claims it is. Shipping journal lines off
  the host (so root on the host cannot rewrite them) is outside this issue.
- **Journal forgery by the API's own process.** Any process can write a line to the journal through
  the syslog socket. A compromised `maran-api` can therefore emit text that looks like an audit line.
  It cannot forge the journal's own trusted fields (`_SYSTEMD_UNIT`, `_UID`, `_PID`, `_COMM` are
  stamped by journald, not supplied by the writer). So the reading rule for the operator is: trust a
  line only when `_SYSTEMD_UNIT=maran-agent.service`. The operator documentation must say so; the
  line's text alone proves nothing.
- **Refused peers.** The layer sits OUTSIDE the `PeerGuard` interceptor (which is per service,
  inside the router), so a peer the guard refuses still produces a line for a mutating path, with
  `outcome=transport:PermissionDenied`. A process probing the socket is visible, not silent.
- **What the layer cannot see.** Read-only rpcs (by design, see 6), unknown paths (a request for a
  path in neither list produces no line; tonic answers `Unimplemented` — UNOBSERVED), a connection
  that never sends a request, the effect on the host (the line says the agent returned `ok`, not that
  nginx is serving the new vhost), and any work done by a process the agent started that outlives the
  request. A mutating rpc whose handler never settled is not hidden: it is `outcome=unset`.

## 2. Log injection

The line is built only from values that cannot carry free text. Listed exhaustively, because the
claim is only as good as the list:

| Field | Source | Why it cannot carry a caller string |
|---|---|---|
| `method` | the matching constant in `MUTATING_RPCS`, e.g. `maran.agent.v1.SitesService/CreateSite` | the layer compares the request path against the list and prints the LIST ENTRY, never the request's own bytes |
| `outcome` | `ok`, the `ErrorCode` variant name, `unset`, or `transport:<name>` | closed enums; a gRPC status is mapped through a closed table of names, and an unrecognised number is the fixed word `Unknown`, never a printed digit string from the wire |
| `duration_ms` | a monotonic clock difference | an integer |
| `account` | `AccountName` (allow-list regex `^[a-z][a-z0-9_]{2,29}$`) | validated type; `AuditSlot::account` has no `&str` entry point |
| `site` | `Domain` (allow-listed grammar) | validated type; `AuditSlot::site` has no `&str` entry point |

- **No free text enters the line, so no newline, carriage return, space or `=` can.** The values
  that can contain spaces or separators (a message, a path, an error string, a header) are not
  accepted by any `AuditSlot` method. The slot's methods take `&AccountName`, `&Domain` and
  `ErrorCode`, and nothing else, so adding a raw string is a compile error and a review reject
  rather than a discipline.
- **Field separators.** Fields are written as `key=value` separated by single spaces. Because every
  value is from the table above, none contains a space; the format needs no escaping because it never
  has anything to escape. A test asserts that the rendered line matches a fixed pattern end to end
  (`^event=rpc method=\S+ outcome=\S+ duration_ms=\d+ account=\S+ site=\S+$`) for every combination of
  field state, which is the check that the claim "one line, no break" holds on the real output and not
  on the type signatures.
- **A request for an unknown path.** The caller controls the HTTP/2 `:path`. The layer never prints it:
  a path in neither list yields no line at all, and a path in the list yields the list's own text.
  A hostile path with embedded newlines or control characters therefore cannot reach the journal
  through this layer. (Whether tonic's router logs it elsewhere is outside this change.) The cost is
  the blind spot stated in 1: probing with unknown paths is not recorded here.
- **Where tracing writes.** `tracing`'s formatter prints the message and fields to stdout; the line
  is emitted as a single event with the line text as one pre-built message field, so the formatter
  adds no caller-controlled decoration. The layer emits through a fixed target; it does not format a
  caller value through `{:?}`, which could carry escapes.

## 3. Secrets

- The slot accepts typed entries only: `AccountName`, `Domain`, an `ErrorCode`. It does not accept a
  `SecretString`, a password, a path, file contents, an error message, a header or a request body.
  There is no method on the slot that takes a generic `Display` or `Debug` value.
- `SetSftpPassword`, `SetFtpsPassword`, `SetDatabasePassword`, `SetSftpAuthorizedKeys` and the
  database credentials in `CreateDatabase` pass through handlers that already hold the secret in a
  `SecretString`. The audit wiring in those handlers adds the account (and nothing derived from the
  secret) and the settled outcome.
- The response-body wrapper reads only the HTTP trailers' `grpc-status` to learn the transport
  outcome. It never reads, copies or formats data frames, request or response, so a response that
  carries a tool excerpt (`AgentError.tool_output`) cannot reach the line.
- **Canary test.** The integration test sends `SetSftpPassword` with a distinctive canary password
  and asserts the canary appears in no captured line. Its positive control plants the canary in a line
  and asserts the same probe FINDS it, so a probe that looks at the wrong stream cannot pass
  (rules/testing.md, "A check must be able to observe what it reports on").
- Blind spot of the canary test: it proves absence for the rpcs it exercises. A secret reaching the
  line through a future slot method is prevented by the slot having none, not by the test.

## 4. Exactly-once, and streaming

The rule is: one line per mutating request that reached the layer, emitted exactly once, whatever
way the request ends.

- **Unary.** The line is emitted when the response body reaches end of stream.
- **Server-streaming** (`CreateBackup`, `RestoreBackup`, `InstallPhpVersion`). The line is emitted
  at end of stream, with the outcome the terminal message settled. A stream that emits progress and
  then an error is one line carrying that error code, not one line per message.
- **Client-streaming** (`WriteFile`). The request body is the stream; the response is one message.
  The line is emitted with the response, and the outcome is whatever the header or size rule settled.
- **Dropped clients.** If the client disconnects, the response body is dropped before end of stream.
  The wrapper emits the line in its `Drop`. The work behind a dropped client is not cancelled for the
  rpcs that run to completion (rules/rust.md "Async and blocking"), so the line's outcome for that case
  is whatever was settled when the body was dropped, usually `unset`. `unset` means "the agent was
  asked, and the client left before the agent said how it ended". It is not a claim that nothing
  happened, and the note says so rather than letting `unset` read as "failed".
- **Guard.** A once-only flag (`AtomicBool`, compare-and-swap) decides who emits: the end-of-stream
  path or `Drop`. Both paths may run (end of stream, then drop of the wrapper); the second finds the
  flag set and emits nothing. A test drives all four orders: end only, drop only, end then drop, and
  an error from the inner body then drop.
- **A handler that never settles.** A path that returns without calling `settle` produces
  `outcome=unset`, unless the trailers carry a non-OK gRPC status, in which case
  `outcome=transport:<name>`. A gap in the handler wiring is therefore visible as `unset` in the
  journal rather than as a missing line.
- **A panic inside a handler.** tonic turns it into a failed request; the wrapper still ends or is
  dropped, so the line is emitted (as `unset` or `transport:Unknown`).

## 5. Volume, rate limiting and denial of service

- **journald can drop lines.** journald applies rate limiting (`RateLimitIntervalSec` /
  `RateLimitBurst`, per service) and storage limits, and discards the excess with only a
  "Suppressed N messages" notice from journald itself. A burst of mutating requests, or a
  compromised API sending a flood on purpose, can push audit lines out of the journal. The agent
  cannot see that this happened. **This is a blind spot and it is stated as one: UNOBSERVED.** The
  absence of a line in the journal is not proof that an rpc did not happen. The shipped test cannot
  observe journald's behaviour (it captures the tracing writer, not the journal), and the note does
  not claim a rate guarantee. An operator who needs a lossless trail must raise or disable the rate
  limit for the agent's unit and ship the journal off the host; this change does not touch journald
  configuration.
- **No unbounded growth.** The slot is a fixed-size record (two small optional values and an outcome),
  not a list; a handler that calls `account` a thousand times overwrites the same field. The line is a
  bounded length by construction (see 2).
- **Overhead on the request path.** The body wrapper adds one pointer indirection per frame and
  reads trailers only. It does not buffer, copy or inspect data frames, so streaming a 4 GB `WriteFile`
  or a long `TailSiteLog` adds no memory. A read-only path is passed through untouched (the layer
  decides on the path before wrapping anything).
- **No global lock.** The slot is per request: `Arc<Mutex<record>>` created in the layer for each
  mutating request and dropped with it. Two requests never contend on the same mutex, and no lock
  is shared between requests, so the layer adds no wait-for edge to the table in rules/rust.md
  "What this agent serialises" and does not make one request wait on another. The mutex is held only
  for a field assignment, never across an `.await` and never while calling into `ops`.
- **Logging cost under flood.** Each mutating request costs one formatted line. The agent's socket is
  mode 0660 and the `PeerGuard` already limits who can connect; an authorised flood is bounded by the
  same limits as the work behind each rpc. Logging is not made cheaper by dropping lines.

## 6. Classification, and why read-only rpcs have no line

- `MUTATING_RPCS` and `READ_ONLY_RPCS` are hand-written lists of full gRPC paths. A census test
  enumerates every `rpc <package.Service> <Name>` line of `contract-baseline.txt` and fails on an rpc
  in neither list, an rpc in both, and a listed path the contract no longer has. Its positive control
  plants a fake rpc and requires the census to name it.
- **Why a list and not a default.** An unlisted rpc defaulting to "mutating" would log new read-only
  rpcs as noise; defaulting to "read-only" would silently hide a new mutating rpc. The census makes
  adding an rpc without classifying it a red build, which is what matters (rules/security.md item 12:
  a new rpc is audited).
- **What the census does not prove.** That the lists are RIGHT. A mutating rpc filed as read-only is
  invisible to the census and invisible in the journal. This is a reviewer item (below), and the lists
  are short enough to read in one pass. A read-only rpc that discloses secrets or sensitive data
  (for example a later "read this file" rpc) is also not logged, by design: this trail covers what
  the agent changed, not what it disclosed. That is a limit, stated.

## 7. Placement relative to the PeerGuard

`Server::builder().layer(...)` wraps the whole router, so the layer runs before the per-service
interceptor. That ordering is deliberate: the guard turns a refused peer into a `PermissionDenied`
status, and the layer sees that status in the response trailers and records
`outcome=transport:PermissionDenied`. If the layer sat inside the guard, a refused peer would never
reach it.

Consequences the reviewer must accept or reject:

- The layer sees every request before authorization. It must therefore do nothing that depends on the
  peer being trusted: it does not read the body, does not call `ops`, and holds only the request's
  path and a fresh slot. A hostile peer gets the same cost as an authorised one, which is the cost of
  one slot allocation and one line.
- The layer does not change the guard's decision. It cannot turn a refusal into an acceptance: it
  forwards the request unchanged and observes the response.

## 8. Failure behaviour

A root process must not panic on input (rules/rust.md "Toolchain & lints"). The layer is in the path
of every rpc, so a fault in it is a fault in all of them.

- **No `unwrap`, `expect` or `panic!`** (workspace lints deny them outside tests). Every fallible step
  returns or degrades.
- **Poisoned mutex.** The slot's mutex can be poisoned only if a thread panics while holding it, and
  the code under the lock is a field assignment. If it is nevertheless poisoned, each slot method
  does nothing, and the line's outcome reads `unset`. The slot never calls `into_inner` to continue
  with data written by a panicked thread, and never panics itself. A test poisons the mutex
  deliberately and requires `unset` and no panic.
- **Detached slot.** `AuditSlot::of` returns a detached slot when the layer did not install one (a
  unit test constructing a service directly, a read-only rpc). Handlers therefore never branch on
  presence, and an absent layer is not an error.
- **Emission cannot fail the rpc.** Writing the line has no `Result` the response depends on. A full
  stdout pipe or a journald that has stopped is a logging failure and does not alter the response the
  peer receives. The layer never retries and never blocks the response waiting for the journal.
- **Drop must not panic.** Emission in `Drop` takes no lock that can block and handles a poisoned slot
  as above. A panic in drop during unwinding aborts the process, so the drop path is the most
  carefully reviewed code in the change.
- **Handler wiring is advisory.** If a handler forgets `settle`, the cost is `outcome=unset`, not a
  failed request. The layer changes no response and no status.

## 9. What the closing test proves, and does not

The integration test `agent/crates/agent/tests/audit_lines.rs` starts the real server in-process on a
temporary unix socket (as `handshake.rs` does) with a capturing tracing writer and asserts:

- exactly one line for each mutating rpc exercised (a rejected `CreateAccount` with a reserved name,
  a duplicate `CreateSite`, `SetSftpPassword` with a canary, a database operation, and others),
  with the field set pinned;
- zero lines for `ListDatabases`, `GetAgentInfo` and other read-only rpcs;
- the canary appears in no line, and the probe finds a planted canary (positive control);
- a streaming rpc with an invalid account yields exactly one line carrying its terminal error code.

It **proves** that, with the real router and the real layer on a real socket, a mutating rpc yields
one line with the pinned fields, a read-only one yields none, a secret in a request does not reach the
captured writer, and a stream ends with one line.

It does **not** prove:

- that the line reaches the journal (the writer captured is the tracing writer, not journald),
  that journald keeps it, or that the unit identity on it is right on a real host: UNOBSERVED HERE;
- that the lists classify correctly (the census proves coverage, not correctness);
- that the success of a mutating rpc corresponds to an effect on the host;
- exactly-once under a real client disconnect at an arbitrary moment (the unit tests drive the
  four orders against the body wrapper; the integration test drops a client on one rpc);
- anything about a refused peer unless the test connects as a uid the policy refuses; if the test
  user cannot do that on the machine running it, the refused-peer case is covered at the unit level
  only and the test prints that it did not observe it;
- behaviour under a flood, or under a compromised agent.

## 10. The `rules/rust.md` "Logging" amendment

The current first bullet says: "`tracing` only; one span per command carrying `correlation_id` and the
command name, opened at the service layer so every `ops` call inherits it." **That span does not
exist.** The agent opens no span per command, it carries no `correlation_id` (nothing in the proto
or in the services creates one), and `ops` calls inherit nothing. The sentence describes code that was
never written, which is the defect rules/architecture.md ("A doc comment that describes what the code
does not do") and rules/testing.md ("Documentation must not describe code that does not exist") name:
it stops the next reader looking for the trail that is not there.

The amendment REPLACES that claim with what is built, in the same change as the layer:

- `tracing` only; the structured fields of a log event are written as fields, not interpolated into
  prose.
- Every mutating rpc yields exactly one audit line from the audit layer, on the target `maran_audit`,
  with the pinned fields (`event`, `method`, `outcome`, `duration_ms`, `account`, `site`). Handlers
  never log it themselves; they fill the request's `AuditSlot` with validated values.
- Read-only rpcs yield no audit line. There is no per-command span and no correlation id; a
  correlation id between the panel's audit row and the agent's line is not provided by this change.
- The `AuditSlot` accepts only validated types (`AccountName`, `Domain`, `ErrorCode`). The list of
  mutating rpcs is closed and checked by the census; a new rpc is classified in the same change.
- The existing bullets (no secrets, no file contents, no `println!`, an error logged once at the
  boundary that handles it) stay.

The same change adds the `audit/` rows to the canonical layout in `rules/rust.md` and to
`agent/CLAUDE.md`. Per `rules/security.md` the owner approves the amendment through the pull request;
the branch is also the unmergeable one, so the amendment is not in force on `main` until the review
debt is discharged.

## rules/security.md checklist (items 1-13), as applied to this change

1. **Input:** the layer reads no request body; every value on the line is a validated type
   (`AccountName`, `Domain`) or a closed enum, and the request path is matched against a constant list.
2. **Paths:** no path is read, built or logged; the slot has no path entry point, and no file is
   touched.
3. **Shell:** no process is spawned and no command string built; the layer is in-process.
4. **Config-file injection:** the journal line is line-oriented; it is safe by construction because no
   caller-supplied text enters it (section 2), and a test pins the full-line pattern.
5. **SQL:** none; the agent has no database and the API is untouched.
6. **AuthZ:** the layer authorizes nothing and changes no decision; it sits outside the `PeerGuard`
   and observes its verdict; no tenant data is read.
7. **Configuration:** no new variable is read; no `.env.example` entry is needed.
8. **Secrets:** no secret type is accepted by the slot, the body wrapper never reads data frames, and
   a canary test with a positive control covers the rpcs that carry credentials.
9. **Crypto:** none added; the layer neither signs nor hashes anything, and the line is not
   tamper-evident against a compromised agent or host (section 1).
10. **Surface:** no new port, daemon or outbound call; the line goes to stdout, captured by the unit's
    existing journal path.
11. **Dependencies:** no new crate; `tower` moves from dev-dependency to dependency, and
    `http-body`, `http-body-util`, `pin-project-lite` only if already in `Cargo.lock`, each justified in
    the PR.
12. **Agent:** no new rpc; this audits the existing ones, and the census makes a new rpc's
    classification a build failure, serving the "audited" half of the item.
13. **Module reach:** unaffected; the C# `AgentCapability` declarations do not change and the layer is
    inside the agent, not a client of it.

## What an attacker could do with this surface

- **Send many mutating requests to hide one.** They can fill the journal rate limit so lines are
  suppressed. Stated as a blind spot (section 5), not prevented.
- **Probe with unknown paths.** No line is written (section 1). Stated as a blind spot.
- **Make a line lie.** Only by controlling the agent process (out of scope, section 1), or by writing
  a look-alike line from another process, which carries a different `_SYSTEMD_UNIT`.
- **Break the line format.** Not possible through this layer: no caller string enters (section 2).
- **Crash or stall the agent through the layer.** The body wrapper does not buffer; the slot is
  per-request; failure paths degrade to `unset` (sections 5 and 8). A residual risk is a bug in the
  wrapper's poll or drop code, which is why those two functions are what the reviewer reads first.
- **Use the layer to learn something.** The line is readable by whoever can read the journal
  (root and the `systemd-journal` and `adm` groups on the supported families). It carries account
  names and domains, which are not secrets, but it is not public. Sharing journal access is the
  operator's decision.

## What a reviewer must check

1. `audit_layer.rs` and `audit_service.rs`: the decision on the path is made before any wrapping; a
   path in neither list is passed through unchanged; the request is forwarded unchanged.
2. The body wrapper's `poll_frame` and `Drop`: no `unwrap`, no `expect`, no panic path; data frames are
   passed through unread; trailers are read for `grpc-status` only; the once-only flag decides the
   emitter; no lock is held across an `.await`.
3. `audit_slot.rs`: the only public mutators are `account(&AccountName)`, `site(&Domain)`, `ok`,
   `refused(ErrorCode)` and `settle`; none takes a `&str`, a `String`, a generic `Display` or
   `Debug`, or a `SecretString`. A poisoned mutex degrades to `unset` and nothing continues on the
   poisoned data.
4. `audit_line.rs`: the rendered line is built only from the five sources in section 2; the
   `transport:` name comes from a closed table; the pinned pattern test would fail if a free-text
   field were added.
5. The two lists against the proto: read them against `contract-baseline.txt` yourself, rpc by rpc,
   for a mutating rpc filed as read-only (the census cannot catch that). In particular `VerifyBackup`
   (reads an artifact, extracts nothing into a home) and `ProbeDestination` as read-only.
6. Handler wiring in the thirteen service folders: `AuditSlot::of` taken before the request is
   consumed; `account` / `site` called with the validated value; `settle` on the same `Result` that
   becomes the response; nothing derived from a `SecretString` is passed; no handler logs on its own;
   the diff in each service file is only that, with no other behaviour change.
7. `server.rs`: the layer is added with `Server::builder().layer(...)` and every service keeps its
   `PeerGuard` interceptor; the layer is outside it, as section 7 requires.
8. The `Cargo.toml` change: tower as a dependency, and the other three crates only if already in
   `Cargo.lock`; `cargo tree` shows no new crate.
9. The `rules/rust.md` "Logging" amendment: the old span claim is gone, nothing now claims a
   `correlation_id` or a span, and the new text states exactly what is built, including the
   journald-drop blind spot.
10. The operator documentation (written by the owner in the website repository; the PR body lists
    it as "Website docs needed"): it must say to trust a line only with `_SYSTEMD_UNIT=maran-agent.service`,
    that journald can drop lines under rate limiting, and that the trail does not survive a
    compromised agent or host.

## What the author could not verify

1. Nothing in this note has run: the code does not exist yet, and this session runs no build and no
   test. Every statement above is a requirement until a gate pass and a reviewer say otherwise.
2. That `Server::builder().layer(...)` in the pinned tonic version wraps the service registry so a
   path rejected by the router (unknown service) or by the interceptor still passes through the layer,
   and that the interceptor's `PermissionDenied` is visible as a trailers-only response the wrapper
   can read. This is the premise of section 7 and is read from tonic's documentation and source, not
   observed.
3. How tonic frames an error: whether `grpc-status` for a trailers-only response arrives in the
   response headers rather than in trailers. The wrapper must read both; this is derived, not run.
4. That tracing's formatter, with the agent's subscriber configuration, writes one event as exactly
   one journal line with no wrapping, and that ANSI colour is off when stdout is not a terminal. Read,
   not observed under systemd.
5. journald's rate limit and storage behaviour on the supported systems (Ubuntu 22.04, 24.04,
   Debian 12, 13, AlmaLinux and Rocky 9, 10): defaults differ by version and by distribution
   configuration and must be read from each one's documentation, not assumed.
6. Whether any supported journal configuration exposes the lines to a group that should not see
   account names and domains.
7. Exactly-once behaviour under a real client disconnect at an arbitrary point of a real
   `CreateBackup` or `RestoreBackup`; the tests drive it at chosen points only.
8. Behaviour under a flood on a loaded host: the overhead of the layer was reasoned, not measured.
9. The correctness of the first-cut classification in the plan (13 service folders, about 60 mutating
   paths); it comes from a reading, not from running each rpc.
