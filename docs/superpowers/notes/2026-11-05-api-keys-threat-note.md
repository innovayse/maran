# API keys (issue #144) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
introduces a new credential class: a long-lived bearer secret that authenticates a caller with no
sign-in, no refresh cookie, no second factor and no session row. It is authentication and token
handling. The branch `fix/144-136-143-api-keys-idempotency-openapi` **may land on a branch and MUST NOT
merge to `main`** until a second reviewer has read this note and checked every item under "What a
reviewer must check". Written BEFORE the code, so it can contradict a choice instead of validating
one; the sections below that depend on code that does not exist yet say so. The same branch carries
#136 (idempotency) and #143 (OpenAPI) by the owner's batch exception; their risks are argued in their
own sections because both touch what an API key caller can see.

## What is being added, in one paragraph

An administrator creates a key (`POST api/v1/api-keys`), sees the plaintext once, and hands it to an
integration. The integration sends it in the header `X-Api-Key`. A new authentication scheme `ApiKey`
validates it against a row in the Identity schema. The key reaches only endpoints that opt in with a
policy naming that scheme and one scope. Every existing endpoint, the fallback policy and every
existing policy stay JWT-only. Two endpoints opt in: `GET api/v1/api-keys/self` (scope `self.read`,
implicit for every valid key) and `GET api/v1/server/status` (scope `server.read`). The vocabulary is
exactly those two scopes; no scope reaches tenant data, so no `accounts.*` scope exists (see
"Reconciliation with what was built").

## Why a new credential class changes the threat model

Every credential the panel has today is short-lived or interactive: a 15-minute JWT, a refresh token
bound to a session row the owner can list and revoke, a password protected by Argon2id and lockout, a
one-time setup token. A key is none of those: it is valid for months, nobody types it so no lockout
protects it, it lives in somebody else's CI variables and shell history, and it skips the second
factor (rules/security.md item 8 names anything that *acts* as a secret). What an attacker who obtains
one can do is exactly what the key's scopes, expiry and address binding allow, and nothing is asked of
them beyond possession. Every control below is therefore about bounding that blast radius and keeping
the key from reaching an attacker in the first place.

## The surface

| Piece | Where | Note |
|---|---|---|
| Key format | `mk_<8 hex>_<43 base64url>` | 32 random bytes of secret, 4 random bytes of prefix, drawn separately |
| Mint and digest | `SharedKernel/Utilities/Tokens/ApiKeyHasher.cs` | pure, no key, no salt, same shape as `RefreshTokenHasher` |
| Entity | `Identity/Domain/Entities/ApiKey.cs` | `Issue`, `Revoke`, `RecordUse`, `IsUsable(now, ip)`; private setters |
| Scheme | `Host/Authorization/ApiKeyAuthenticationHandler.cs` | scheme name `ApiKey`, NOT the default scheme |
| Policies | `Host/Authorization/ApiKeyPolicies.cs` | one per scope: scheme `ApiKey` + scope claim |
| Caller | `SharedKernel/Callers/ApiKeyCaller.cs` | a new case, argued below |
| Audit | `Identity/Filters/ApiKeyAuditFilter.cs` + `IdentityAuditJournal` | per call, never body or key |
| Management | `api/v1/api-keys` GET, POST, DELETE | `AdminOnly`, `ViewAsAccess.Refused` |

## The argument, by concern

### 1. The secret: entropy, digest at rest, comparison

- **256 bits from the system CSPRNG** (`RandomNumberGenerator.GetBytes(32)`, base64url). There is no
  structure for an attacker to exploit; online guessing is not a realistic path whatever the rate
  limit says (2^256 candidates). The rate limit exists for resource protection, not for guessing.
- **SHA-256 at rest is sound for exactly this reason** and Argon2id would be wrong, for the argument
  `RefreshTokenHasher` already makes: a deliberately slow hash protects a low-entropy secret against
  offline search, and a 256-bit secret has none to protect against, while a 100 ms hash would be
  paid on every API call. rules/security.md item 9 bans MD5 and SHA-1 for security-relevant use;
  SHA-256 over a full-entropy secret is what it is for. A database read therefore yields prefix,
  digest and metadata, from which no key can be recovered.
- **The digest covers the whole presented string**, prefix included, so a row cannot be matched by a
  differently-spelled header. The prefix is drawn from separate random bytes, not cut from the secret,
  so publishing it (the list screen shows it) leaks none of the 256 bits.
- **Constant-time compare**: `CryptographicOperations.FixedTimeEquals` over the two 32-byte digests.
  Comparing digests rather than keys already removes most of the timing signal (the attacker does not
  control the compared bytes), and the fixed-time call removes the rest; it is cheap and states the
  intent.
- **Lookup is by the unique indexed `Prefix`**, then the compare. 32 bits of prefix is not a secret
  and is not meant to be; it is an index, not a credential.
- **Format and length are checked before any hashing**: the header must match the key's shape and a
  fixed maximum length, otherwise the generic 401 (the shape is public, so refusing early discloses
  nothing, and it stops a megabyte header being hashed).
- A minting collision on the unique prefix (probability about 2^-32 per pair) retries with a fresh
  draw; it must never be surfaced as an error naming the prefix.

### 2. No oracle: one answer, equal work

Every one of these returns the **identical** generic `401` with no body detail, no distinguishing
header and no distinct log level visible to the caller: malformed key, unknown prefix, wrong secret,
revoked, expired, address not allowed.

- The handler always does the same work: parse, look the prefix up, compute SHA-256 of the presented
  value, `FixedTimeEquals` against the stored digest **or against a fixed dummy digest when the
  prefix is unknown**, and only then evaluate revoked, expired and address, never short-circuiting
  before the compare. An unknown prefix must not be measurably cheaper than a known one. (A database
  hit versus a miss is not perfectly equal in time; what that could leak is whether a 32-bit public
  index value exists, which names no secret. The reviewer judges whether that is acceptable; it is
  stated here rather than claimed away.)
- The failure reason is **logged for the operator** at a level the caller cannot see (key id when
  known, reason, address), never returned. The log line carries the prefix and never the presented
  value.
- `401` carries the same `WWW-Authenticate` as every other authentication failure and no
  `ErrorMessages` code distinguishing the reasons. A revoked key that says "revoked" tells a thief the
  key was once real.

### 3. Transport: header only, never in a URL, never in a log

- **`X-Api-Key` only.** Not `Authorization: Bearer` (so a key can never be confused with a JWT, and a
  proxy that logs or rewrites `Authorization` treats them identically), **never a query parameter or
  cookie**: the handler reads that one header and nothing else, and a test sends the key in the query
  and in a cookie and expects 401. A URL is written to the nginx access log, browser history and
  `Referer`; a header is not.
- **What the code logs today** (measured by reading, to be re-run by the reviewer): `UseSerilogRequestLogging`
  with its default template logs method, path, status and elapsed time, and the panel enriches with the
  correlation id only (`ObservabilityExtensions.UsePanelRequestLogging`); it logs no request header
  and no body. `appsettings.json` sets no custom template. `Microsoft.AspNetCore` is held at Warning,
  so the framework's own header-bearing diagnostics are not on. nginx's shipped `log_format` is not
  verified here to omit custom headers (see "could not verify").
- **The redaction floor** (`SecretRedactionPolicy.ShortestRecognisableSecret = 8`) is the length below
  which a minted secret cannot be searched out of agent error text. A key is 55 characters, far above
  it. The floor is irrelevant to the key's path anyway, because a key never reaches the agent; it is
  cited so nobody mints a shorter "convenience" key.
- **What must be added** (none exists yet):
  1. A test that plants a key, calls an opt-in endpoint with a valid key, an invalid key and a
     malformed key, and asserts the plant appears in **no** captured log event, no problem body, no
     audit row and no Wolverine envelope (the positive control: the same harness finds a planted
     value it was told to look for).
  2. `ApiKeyCreatedDto` is the only place the plaintext exists. The create handler returns it through
     `InvokeAsync` (in-memory, not durable), and a test asserts the `wolverine` schema holds no
     envelope containing it (rules/csharp.md "a message carrying a secret is never durable").
  3. The audit entries `ApiKeyCreated`/`ApiKeyRevoked` carry name, prefix and scopes; a test asserts
     the digest and the plaintext are in none of the journal's columns.
  4. The SPA keeps the plaintext in component memory for the one-time dialog only: no store state
     that persists, no `localStorage`, no route parameter; it is cleared when the dialog closes.
  5. The exception middleware and `ProblemDetailsAuthorizationResultWriter` never echo a request
     header; a test fails a call after authentication and checks the body.
  6. The installer's nginx `log_format` must not include `$http_x_api_key`; add an installer check.

### 4. CSRF classification

`CsrfHeaderMiddleware` demands `X-Maran-Request` only for an unsafe method **that carries a cookie**
(its remarks: a credential no browser attaches automatically cannot be forged cross-site). A key is
exactly such a credential: a cross-site form or image cannot set `X-Api-Key`, and a cross-origin
`fetch` that sets it needs a preflight this server never answers; an attacker who could set it already
holds the key. So a key call carries no cookie and is out of that middleware's scope, as an
`Authorization`-header call is. Two consequences are pinned by tests: a key call with a cookie present
is still held to the header (stricter, harmless); and a **cookie can never authenticate a key call**
(the handler reads the header only), so the CSRF premise cannot be turned around.

### 5. Reach: a key can reach ONLY opt-in endpoints

- The `ApiKey` scheme is **not** the default scheme and is **not** forwarded to. The default is JWT
  (`AuthenticationExtensions`). `AdminOnly`, `ResellerOnly`, `AdminOrReseller`, `AnyAuthenticated`
  and the fallback policy name no authentication scheme, so they authenticate with the default scheme
  only; the `ApiKey` handler never runs for them and a key principal can never satisfy them. They also
  `RequireClaim(role, ...)`, which a key principal does not carry, and `AnyAuthenticated`/`AdminOnly`
  carry `TwoFactorEnrolmentCompleteRequirement` and `ViewAsBoundaryRequirement`, which are written for
  JWT claims. Belt and braces; a test calls every existing authorized endpoint with a valid
  all-scopes key and expects 401 (not 403: the JWT scheme saw no token). A census walks the composed
  endpoints and fails for any policy that names the `ApiKey` scheme and is not an `ApiKeyPolicies`
  member.
- **A key can never manage keys.** `api/v1/api-keys` is `AdminOnly` (JWT) and `ViewAsAccess.Refused`;
  no `ApiKeyPolicies` entry guards it. So a stolen key cannot mint a successor or revoke the audit
  trail's subject.
- **`ICurrentUser` and the `Caller` closed type.** Three ways to represent an api-key principal were
  considered:
  - *As `AdministratorCaller`*: rejected. Everything in the tree that asks "is this an administrator"
    (tenant-filter decisions, resellers' narrowing, journals) would grant a key the whole server with
    no scope.
  - *As `AnonymousCaller`*: rejected. It loses the identity the audit journal must record and makes
    every handler treat the key as nobody, so a tenant filter that fails closed would hide the bug
    behind "empty result".
  - ***A new case `ApiKeyCaller`*, which is the choice.** It is not a `PrincipalCaller`: a key is not
    a panel login, has no username to impersonate and no `ViewedBy`; it carries the key id, the key
    name and the granted scopes, and nothing else. The hierarchy is closed by a `private protected`
    constructor, so every `switch` over `Caller` stops compiling until it decides what a key is — that
    forced decision is the point (rules/csharp.md Callers). The default for modules that have no
    reason to serve a key is "sees nothing", the same as `AnonymousCaller`, and each existing switch
    is read in review rather than defaulted.
  - `HttpContextCurrentUser` today **throws** `UnrecognisedPrincipalException` for an authenticated
    principal with no readable `sub` or no recognised role. A key principal carries neither
    `PanelClaimTypes.UserId` nor `Role`, so if one ever reached a JWT-policy endpoint it would fail
    closed with a generic 500 instead of degrading. The new branch is added **before** that check and
    keyed on a distinct claim (`PanelClaimTypes.ApiKeyId`), which a JWT cannot carry because the token
    issuer never writes it; a test mints a JWT with that claim and expects it ignored/refused.
- **Why a key never acts as an account owner or administrator on the SPA surface:** it has no
  `role`, `account` or `reseller` claim, so no existing policy names it, no tenant filter shows it a
  customer's row, and the `Caller` case it maps to is not `AdministratorCaller`. No scope in this
  change reaches tenant data: the tenant contexts (Accounts, Sites, Ftp, Sftp, Ssl, Cron, Tasks) each
  decide `ApiKeyCaller` explicitly and show it nothing (fail closed).
- **A key is as wide as an administrator for the endpoints it opts into.** A `server.read` key reads
  the server status, which is administrator-grade, non-tenant data. The scope is the limit, the
  creator is an administrator, and the list is a closed vocabulary; the note does not pretend a key
  is narrower than the endpoints it names.

### 6. Scopes: a closed vocabulary

`Sdk/Contracts/ApiKeyScopes.cs`: `self.read` (implicit for every valid key) and `server.read`, and
nothing else. The create validator accepts only members of the set; the SPA's choices come from
`GET api/v1/api-keys/scopes` (backend-localized), never a SPA constant. A stored scope that the
running build does not know (a rollback, a removed scope) grants **nothing**: the claim builder
intersects with the current vocabulary. Scopes are not editable after creation; rotation is create
then revoke, so no endpoint widens an existing key (no speculative `Update`; YAGNI and one less path
to attack).

### 7. IP binding and how the real client address is known

- A key may carry an `AllowedCidrs` list. The address checked is `ClientAddress.Of(
  HttpContext.Connection.RemoteIpAddress)` read in the handler, the same single spelling the audit
  journal, session list and rate limiter use (IPv4-mapped unwrapped, scope id removed). The handler
  never reads `X-Forwarded-For` itself.
- **How the address is determined.** The panel listens on a unix socket behind nginx.
  `PanelPeerAddressMiddleware` reads the peer uid from `SO_PEERCRED`, admits only the configured
  reverse-proxy uid, stamps the connection as loopback; `UseForwardedHeaders` (`ForwardLimit = 1`,
  `KnownProxies` loopback only) then takes the **single, rightmost** `X-Forwarded-For` entry. The
  shipped nginx site writes `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for`
  (`installer/nginx/maran.conf:145`), which appends the address nginx saw as the last entry, so a
  client-supplied earlier entry is ignored. The address is trustworthy exactly as far as that chain
  holds: a local process that is not nginx is refused before the header is read.
- **Behind a further proxy or CDN the address is the proxy's, not the client's.** Then an IP-bound key
  either denies everyone (bound to the real client) or admits everyone behind that proxy (bound to
  the proxy). This is a deployment this design does not support; it is a stated limit, and the
  create dialog says the binding is by the address the panel's own proxy sees.
- A request with no peer (`ClientAddress.Unknown`, the in-memory test host) matches **no** CIDR: a
  bound key is denied there. A key with no list is not bound. A `/0` list is refused at creation as
  a binding that binds nothing.
- CIDR parsing is its own risk: `CidrRange` lives in the Firewall module and a module may not
  reference another, so the shared rule is new code in `SharedKernel/Utilities/Network/CidrRule.cs`
  (host bits, mapped addresses, v4/v6 mix). A wrong parser is a wrong allow-list; it carries a
  positive and inverse control per family.

### 8. Expiry and revocation

- `ExpiresAt` is optional. A key with none lives until revoked; that is the administrator's choice,
  audited, and the SPA defaults to a finite expiry. The reviewer may decide the product should
  require one. Expiry is evaluated with `IClock` on every request against the row, so there is no
  token to outlive it.
- **Revocation takes effect on the next request.** The handler loads the row by prefix **on every
  request, with no cache**, so `RevokedAt` is read fresh; there is no cached principal, no JWT
  lifetime and no refresh path. The stated bound is therefore: a request already past the handler
  when the revoke commits completes, and nothing after it does. If a future cache is added it must
  state its bound in this note first.
- Revoke is a rich-model method (`Revoke(at)`), idempotent, first revocation time wins.

### 9. Last-use bookkeeping and what it leaks

**As built, this differs from the design written first** (see the reconciliation table): the throttle
is `ApiKey.RecordUse`, which returns false when `LastUsedAt` is less than a minute old, and the stamp
is saved with the context's ordinary `SaveChangesAsync` in `ApiKeyAuthenticator.RecordUseAsync`; a
`DbUpdateException` is caught, the entity detached, a warning logged, and the call proceeds. It is a
read-modify-write, not one conditional `UPDATE`. Residual risk, judged: two concurrent first calls
inside one window both read an old `LastUsedAt`, both pass the throttle, and both write; the later
write wins. Cost: a second tiny write and a last-use address that may be either caller's. EF writes
only the two changed columns, so the race cannot touch `RevokedAt`, the digest or the scopes, and
revocation is read fresh on the next request regardless. It cannot grant access and cannot lose a
revocation; it can only blur a diagnostic field by seconds. Accepted, not free. Original design text
follows, kept for the reviewer: the intended statement was a **single conditional `UPDATE ... WHERE
LastUsedAt IS NULL OR LastUsedAt < now - 1 minute`**, so a busy key causes at most one write per
minute and concurrent calls do not each write. Consequences, stated: the displayed last use is accurate only to a
minute; the address shown is the one of the first call in the window; and the write is on the
authenticated path of every call, so a failure to write must never fail the request (logged, not
returned). It **leaks** to the list's reader (an administrator) the last address that used a key,
which is the purpose; it leaks nothing to the caller. It is written **only after a successful
validation**, so an attacker presenting wrong keys cannot move a real key's last-use, and a
failed attempt is not recorded on any key row (it cannot be: an unknown prefix has no row).

### 10. Per-call audit

`ApiKeyAuditFilter` writes, through `IdentityAuditJournal`, one entry per call: key id, key prefix,
HTTP method, **route template** (not the raw URL: no ids from the path, no query string), status
code, caller address. Never the body, never the key, never a header. Failure handling is a choice the
reviewer must make: the filter runs after the action, so a mutation has already happened when the
audit write fails. The design is to log the failed write loudly and still return the response (a
refused response for a mutation that already ran is worse than a late audit line), with the module
journal of the mutated resource recording the key as the actor independently; the reviewer decides
whether the stricter alternative, auditing before execution with status unknown, is preferable. Every
other module's journal now receives a `Caller` that may be an `ApiKeyCaller`; each must spell that
actor (`api-key:<name>/<prefix>`) deliberately, which the exhaustive switch forces.

### 11. View-as

A view-as token speaks for another login and cannot be used to manage keys: `api/v1/api-keys` is
`AdminOnly` and `ViewAsAccess.Refused`. A key is not a login and cannot be viewed-as or view as
anyone; the `ViewAsBoundaryRequirement` does not run on `ApiKeyPolicies` because a key principal has no
`act` claim to inspect, and no code path lets a key request a view-as session (the view-as endpoints
are JWT-only). A test asserts a view-as token receives `HostViewAsNotAllowed` on all three management
actions and that a key receives 401 on the view-as endpoints.

### 12. Administrator-only management

Create, list and revoke are `AdminOnly`: an administrator who has completed any forced second-factor
enrolment, through JWT. Resellers and customers get no key management in this change (the lead's
default; the issue asks). The creator is recorded (`CreatedByUserId`). **Residual:** creating a key
requires the second factor if the administrator's session has it, but **using** the key does not.
That is the nature of a machine credential, and the controls are scope, expiry, address binding, audit
and one-time display. The note records it as accepted, not as free.

### 13. Rate limiting, brute force and enumeration

- Key-authenticated routes use the `api` policy, but `RateLimitPartitionKey.For` today partitions
  by `account:` then `user:` then `ip:`; a key principal has neither claim and would fall to
  `ip:`, so every integration behind one NAT would share a budget and a noisy one starves the rest. A
  branch is added **first**: `apikey:<id>`.
- **Failed attempts are the harder half and the ordering is a real risk.** In `Program.cs` the
  pipeline is `UsePanelAuthentication` (authentication and authorization) then `UsePanelRateLimiting`.
  A request that fails authentication at the `ApiKey` handler is answered by the authorization
  middleware **before** the rate limiter runs, so as ordered, failed key attempts would not be
  counted by the `api` limiter at all. Online guessing is infeasible (256 bits), but every attempt
  costs a database lookup and a hash, which is an unauthenticated resource cost. The design therefore
  needs a failed-attempt limiter keyed by `ClientAddress`, consulted by the handler before the lookup
  (cheap in-memory partition, same fixed-window family as `LoginRateLimitPolicy`), answering the same
  generic 401 when exhausted... **or** a 429; the reviewer decides, with the observation that a 429
  distinguishes a throttled address from a wrong key and discloses nothing about any key. **As built:
  the same bodyless 401, not a 429** (deviation, below). It does
  **not** feed the Firewall ban path: an address-ban reachable by anyone who can send bad keys lets an
  attacker spoof-free but cheaply ban an integrator's NAT address. This is the least settled part of
  the design and is on the list below.
- Enumeration: there is nothing to enumerate; the prefix is public by design and the answer is
  identical in every failure. Listing keys is administrator-only and shows prefixes, never digests.

## #136 idempotency store — risks

- **Replaying another caller's response.** The unique index is `(CallerKey, Key)` and the lookup is
  always by both. `CallerKey` is a typed string, `user:<guid>` for a JWT principal and
  `apikey:<guid>` for a key, so a user id and a key id cannot collide and a response is replayable
  only by the principal that made the original request. It is never an account-crossing value: a
  customer's retry cannot read an administrator's stored response, and two administrators do not share
  keys. The filter runs **after authorization**, so a caller whose permission was removed cannot
  replay a stored success. An anonymous caller has no `CallerKey` and the attribute is refused on an
  anonymous action (a test).
- **The request hash** is SHA-256 over method + route values + the **query string** + body. The
  contract in the plan says method+route+body; the query string is added because two requests that
  differ only in a query value are not the same request. A reused key with a different hash is a 422
  `IdempotencyKeyReused`, never a silent replay of the first answer.
- **Never store secrets in responses.** An action that returns a credential (a password, a key, a token)
  is marked `[IdempotencyNoStore]`; the filter then stores nothing and answers a replay `409
  IdempotencyResponseNotStored`. `POST api/v1/api-keys` is such an action and is **not** `[Idempotent]`
  at all (a retried create that minted a second live key is worse than a failed retry). A census test
  walks every `[Idempotent]` action's return type and fails on a member named like a secret; this is
  a heuristic and is stated as one. Responses are stored as plain text in the `idempotency` schema for
  at most 24 hours (the purge job), so anything that is personal data but not a secret is at rest in
  clear for a day, and a deleted account's stored responses outlive the deletion by up to that time;
  the module reports them to `ModuleAccountResidueAuditor` or the purge removes them, a decision for the
  reviewer.
- **Body size cap.** The filter buffers the request body to hash it and stores the response: both are
  capped (request: the panel's body limit applies before the filter; response: 256 KiB). A response
  over the cap is treated as not storable (the call succeeds, the replay answers 409), never as an
  error and never truncated.
- **Key charset and length**: `[A-Za-z0-9_:.-]{1,255}`, validated before any database access, so the
  header is never a vector for log injection, SQL or a path. A malformed key is a 400 with a code.
- **In-progress rows**: reservation is an atomic insert against the unique index. A process that dies
  with a reservation held would 409 every retry for 24 hours; reservations carry a short lease
  (minutes) after which they are reclaimable. A 5xx removes its reservation so the client can retry.
- **Residual:** the write that stores the response and the action's own commit are different
  contexts (one handler, one `DbContext`), so a crash between them executes the action and does not
  record it; the retry runs it again. Idempotency here narrows duplicate execution, it does not prove
  exactly-once, and the docs say so.

## #143 OpenAPI — risks

- **No runtime surface** (rules/security.md item 10). The document is produced by the CLI verb
  `maran openapi` and by a test host; nothing calls `MapOpenApi` in the running panel, and a test
  asserts the composed panel answers 404 (to an administrator) on the usual spec routes. A new
  listening endpoint would be a spec change first, not a PR.
- **The spec is the route inventory of the open modules**, which is already public in a
  source-available repository; it is generated from the composed set the CLI builds, so a paid
  marketplace module that is not in the repository is not described. The reviewer checks the composed
  set.
- **No secrets and no internal detail in the spec.** Server-established command members
  (`[JsonIgnore]`+`[BindNever]`) must be absent from request schemas (a test: no `ipAddress`,
  `userAgent`, `userId` in any request body). XML doc comments feed the schema, and this repository
  writes doc comments that describe internals at length; the generator is configured to emit
  summaries of operations and DTOs only, and the reviewer reads the generated `openapi.json` for
  paths, file names, version numbers or threat reasoning that leaked into a description. No example
  value is a real credential or hostname.
- The breaking-change gate treats **a security requirement added** as breaking, which is what makes
  the new `ApiKey` scheme visible in the diff the first time it appears.
- The release asset is checksummed with the others and **not signed** (owner decision). An
  unsigned file published in the same place as its checksum proves integrity against corruption, not
  against a compromised release channel.

## Security checklist (rules/security.md items 1-13)

1. **Input:** key header shape/length, create body (name, scopes, CIDRs, expiry), idempotency header
   and `?search`/filter parameters validated at the boundary by FluentValidation or the model binders.
2. **Paths:** no path is built from any of this; nothing touches the filesystem.
3. **Shell:** nothing spawns a process; `maran openapi` uses fixed arguments through the toolbox.
4. **Config-file injection:** no caller value is written into a config file; the key name is a
   database value only.
5. **SQL:** EF Core only; the throttled last-use write and the idempotency reservation are
   parameterised, and any `// raw-sql:` use carries the comment.
6. **AuthZ:** every new endpoint names its policy; key management is `AdminOnly`; the tenant filter
   is untouched; a key with no tenant claim sees no customer row. Keys are not tenant rows (no
   `AccountId`) and are not subject to the 404/403 IDOR rule; `TenantScopeTests` must not need an
   exemption, and if it asks for one that is a finding.
7. **Configuration:** no new variable is expected; if one is added (a lease, a limiter size) it lands
   in `.env.example` in the same PR.
8. **Secrets:** the plaintext exists once in the create response; never in logs, URLs, audit rows,
   Wolverine envelopes or the spec (section 3 list).
9. **Crypto:** SHA-256 over 256-bit CSPRNG output (argued above), `FixedTimeEquals`, no home-grown
   primitive, no MD5/SHA-1.
10. **Surface:** no new port, daemon or outbound call; no runtime OpenAPI endpoint; the `ApiKey` scheme
    adds an authenticated entry point on the existing listener only.
11. **Dependencies:** `Microsoft.AspNetCore.OpenApi` (and `Microsoft.Extensions.ApiDescription.Server`
    only if needed) pinned in `Directory.Packages.props` with a one-line justification in the PR;
    `maran licenses --check` must stay clean.
12. **Agent:** no rpc added; keys never reach the agent.
13. **Module reach into the agent:** Identity and Idempotency declare no `AgentCapability`; the guard
    must still pass with the new module composed.

## Accepted residual

- A stolen key is usable from anywhere an unbound key is allowed until it is revoked or expires, with
  no second factor.
- A key with no expiry is a standing credential by the administrator's choice.
- An unknown-prefix request and a known-prefix request are equal in hashing and compare work, not
  provably in database time.
- An unaudited mutation is possible if the audit write fails after the action ran.
- Idempotency is at-most-once-per-reservation, not exactly-once.
- Address binding is only as good as the single-hop proxy chain; a second proxy breaks it silently.

## Reconciliation with what was built

Added after the code was written (2026-11-05, by reading the working tree; nothing was built or run).
Everything above this heading is the design as argued before the code; where it differs from the code
the row below says so and the code wins. Status: **MET** (read in the named file), **DEVIATES** (code
differs from the design; stated), **NOT VERIFIED** (no code or test observed here). Paths are under
`backend/src` unless they start with `tests`.

| Requirement | Status | Where and what |
|---|---|---|
| Key `mk_<8 hex>_<43 base64url>`, 32 CSPRNG bytes, prefix from separate bytes | MET | `Maran.SharedKernel/Utilities/Tokens/ApiKeyHasher.cs` `Mint` |
| SHA-256 digest at rest, fixed-time compare, shape and length checked before any hash | MET | `ApiKeyHasher.Digest`, `DigestsMatch` (`FixedTimeEquals`), `TryParse` (exact length 55) |
| Unknown prefix still hashes and compares | MET | `Maran.Modules/Identity/Services/ApiKeyAuthenticator.cs`: digest computed and compared against a dummy digest before any state is read. A malformed shape returns earlier by design |
| One identical bodyless 401 for every failure | MET | `Maran.Host/Authorization/ApiKeyAuthenticationHandler.cs`: every failure is `AuthenticateResult.Fail` with one fixed text; `HandleChallengeAsync` writes 401 and `WWW-Authenticate: ApiKey`. A missing header takes the same challenge. No test compares bodies |
| Header only, never query or cookie | MET | The handler reads `X-Api-Key` (`ApiKeyDefaults.HeaderName`) and nothing else; a repeated header is refused. The planned test sending a key in query and cookie does not exist: NOT VERIFIED by test |
| Scopes: closed vocabulary `self.read` (implicit) and `server.read` only | MET (lead override) | `Maran.Sdk/Contracts/ApiKeyScopes.cs`; create validator and `ApiKey.Issue` refuse others; the claim builder and `HttpContextCurrentUser.ReadApiKey` intersect with the vocabulary |
| `server.read` reaches `GET api/v1/server/status` | DEVIATES from the lead's wording (modules, updates count) | `Maran.Modules/Monitoring/Controllers/ServerStatusController.cs`: a new endpoint, scheme `ApiKey` plus `ApiKeyPolicyNames.ServerRead`. `GET api/v1/modules` is NOT key-reachable (`Maran.Host/Modules/ModulesEndpoint.cs` stays anonymous-and-JWT, and offers an `ApiKeyCaller` no module); the updates count is untouched |
| A key reaches only opt-in endpoints; existing policies stay JWT-only | MET by construction, NOT VERIFIED by call | Scheme is not the default (`Maran.Host/Extensions/AuthenticationExtensions.cs`); `ApiKeyPolicies.cs` names the scheme; `tests/Maran.ArchitectureTests/ControllerAuthorizationCensus.cs` admits the scheme only with an `ApiKeyPolicyNames` policy. The planned test calling every endpoint with a valid key and expecting 401 does not exist |
| Key management is `AdminOnly`, `ViewAsAccess.Refused`, not key-reachable | MET | `Maran.Modules/Identity/Controllers/ApiKeysController.cs` |
| `ApiKeyCaller` is a `Caller` case, not a `PrincipalCaller`; tenant contexts fail closed | MET (read), exhaustive build NOT RUN | `Maran.SharedKernel/Callers/ApiKeyCaller.cs`; each `DbContext` and directory switch in `Maran.Modules/{Accounts,Sites,Ftp,Sftp,Ssl,Cron,Tasks}` names it and returns `false`/`null`/empty; every `*AuditJournal` spells the actor `api-key:<id>`; `SignedInCallerPolicy` throws for it; `tests/Maran.ArchitectureTests/CallerCaseCensusTests.cs` exists |
| `HttpContextCurrentUser` decides a key by authentication type, not by a claim a JWT could carry | MET | `Maran.Host/Security/HttpContextCurrentUser.cs` `IsApiKeyPrincipal`, read before the role check |
| IP binding by `ClientAddress`, `Unknown` matches nothing, `/0` refused | MET (read) | `ApiKeyAuthenticator.IsAddressAllowed` (unparsable address denies), `CreateApiKeyCommandValidator` (`PrefixLength > 0`). `CidrRule` tests not read here |
| Revocation and expiry take effect on the next request, no cache | MET | The authenticator loads the row by prefix every call. `grep -rnE "IMemoryCache\|MemoryCache\|Cache" backend/src/Maran.Host/Authorization backend/src/Maran.Modules/Identity/Services` hits only `SecurityPolicyCache` and `JwtAccessTokenIssuer`, which belong to JWT issuing, not the key path; the pattern is capable of a hit (it found those) |
| Failed-attempt limiter before the lookup, keyed by address, separate from the `api` limiter | MET | `Maran.Host/RateLimiting/ApiKeyFailureLimiter.cs`, consulted first in the handler; not wired to the firewall ban path (no reference to it) |
| Limiter answers "generic 401 or 429" | DEVIATES | A locked-out address gets the same 401 as a wrong key, not a 429. Consequence: a legitimate key used from a locked-out address is refused with an indistinguishable 401 until the window ends; the operator sees no hint. In exchange nothing distinguishes throttled from wrong |
| Limiter is "progressive like the login policy" | DEVIATES | It is one fixed window: `RateLimitOptions.LoginMaxAttempts` (5) failures inside `LoginLockoutSeconds` (300) from the first failure, then the window resets. In memory, per process: a restart clears it; at 50,000 tracked addresses new addresses are not recorded (it fails open). Failed keys are not counted by the `api` limiter, which runs after authorization |
| A key's lifetime vs its creating administrator | DEVIATES from what a reader may assume | A key keeps working after the administrator who created it is disabled or deleted: `CreatedByUserId` is recorded for the audit trail and read by nothing at authentication, so key lifecycle is independent of the creator by design. A departed administrator's keys stay live until revoked, expired or found in the list. A revoke-all-by-administrator action (and revoking on disable or delete) is a follow-up and is **NOT built** |
| Lockout is per address, so a shared NAT can lock out an integrator | DEVIATES (owner decision) | The failure limiter counts wrong-key attempts per client address, not per key or per integrator. Behind a shared NAT or egress proxy, one misconfigured client (or an attacker sharing the address) exhausts the window and every legitimate key from that address gets the same indistinguishable 401 until it ends. Accepted by the owner in exchange for not letting an attacker lock a specific key out from anywhere; an integrator on a shared address is advised to bind the key to that address and rotate a key that fails unexpectedly |
| `apikey:<id>` rate-limit partition for authenticated key calls | MET | `Maran.Host/RateLimiting/RateLimitPartitionKey.cs`, first branch, only when the identity's authentication type is `ApiKey` |
| CSRF unchanged | MET | `Maran.Host/Middleware/CsrfHeaderMiddleware.cs` logic is unchanged (only its doc comment grew): it still demands `X-Maran-Request` only for an unsafe method carrying a cookie, which a key call does not carry, and the key handler never reads a cookie. A key call that does carry a cookie stays held to the header. No test pins either consequence: NOT VERIFIED by test |
| Per-call audit | DEVIATES in content, with a stated blind spot | `Maran.Modules/Identity/Services/ApiKeyAuditFilter.cs` is an Identity-owned global action filter (`IdentityModule.cs` adds it to MVC options); it acts only for an `ApiKeyCaller`, runs after the action and records method, route template, status and `ClientAddress` through `IdentityAuditJournal.RecordApiKeyCallAsync`. The actor is `api-key:<key id>` with no name and no prefix (the design said prefix); user agent is empty. **Blind spot:** an action filter sees only requests that reach an action. A request refused before it (a 401 from the scheme, a 403 from a policy, a 429 from the limiter, an automatic model-validation 400, a short-circuiting resource filter) is not journalled by this filter; refused authentications are only in the operator log. A failed audit write is logged and the response still returns (a mutation can go unaudited; no key-reachable endpoint mutates today) |
| Last use written once per minute by a single conditional UPDATE | DEVIATES | Throttled read-modify-write through the entity; failure is a logged `DbUpdateException`, call proceeds. Residual judged in section 9: harmless to security, blurs a diagnostic field. Written only after a successful validation, so wrong keys cannot move it (MET) |
| Redaction: the key appears in no log, error body, audit row or envelope | MET by reading, NOT VERIFIED by test | Checked by reading: `UsePanelRequestLogging` in `Maran.Host/Extensions/ObservabilityExtensions.cs` enriches the Serilog request line with the correlation id only (the line holds method, path, status, elapsed; no header, no query, no body); `grep -rniE "HttpLogging\|UseHttpLogging\|LogHeaders\|IncludeHeaders" backend/src --include=*.cs` finds nothing (`/usr/bin/grep`, case-insensitive; positive control: the pattern `UseSerilogRequestLogging` finds `ObservabilityExtensions.cs`); the authenticator logs prefix or key id and address, never the presented value; the audit entry is built from named members. Not observed: no test plants a key and scans captured logs, bodies, journal rows and Wolverine envelopes; nginx's `log_format` was not read |
| Creation: plaintext only in `ApiKeyCreatedDto` (a `SensitiveString`, so its record `ToString()` prints `[redacted]`); not durable; count, collision check and insert are serialised by a panel-wide advisory lock (`ApiKeyCreationLock`), an empty address list is refused, and exhausted prefix redraws answer `ApiKeyPrefixUnavailable` | MET (read), NOT VERIFIED by test | `CreateApiKeyCommandHandler` returns it in-memory through `InvokeAsync`; the audit subject is prefix, name and scopes. No `wolverine` schema scan exists |
| `POST api/v1/api-keys` not `[Idempotent]` | MET | `ApiKeysController.CreateAsync` carries no filter; no action in the tree carries the no-store marker (`/usr/bin/grep -rnE "IdempotencyNoStore\]" backend/src --include=*.cs` prints nothing; positive control: dropping the `\]` finds the attribute's own file), so the replay-409 path is unused today |
| Idempotency `CallerKey` typed and always in the lookup | MET, DEVIATES in spelling | `Maran.Sdk/Filters/IdempotencyCallerKey.cs`: `user:<guid>` and `key:<guid N>` (design said `apikey:`); store looks up by (callerKey, key) |
| Idempotency: request hash, charset, caps, lease, purge | MET (read) | `IdempotencyRequestHash.cs` (method, route pattern, route values, query, body, body over 1 MiB refused as 400 `IdempotencyKeyInvalid`), `IdempotencyFilter.cs` (charset `[A-Za-z0-9_:.-]`, 255, response over 256 KiB not stored, 5xx not stored and the reservation released), `IdempotencyRetention` (5 minute reservation lease, 24 hour completed window), `IdempotencyPurgeScheduler` (hourly, after a 7 minute start-up delay) |
| Idempotency census of secret-like response members | NOT VERIFIED | Not found in `backend/tests` |
| No runtime OpenAPI endpoint | MET (read) | `grep -rn "MapOpenApi\|AddOpenApi" backend/src --include=*.cs` finds nothing; the document comes from `Maran.Host/Cli/Commands/OpenApiCliCommand.cs` |
| Integration, redaction, key-in-query/cookie, view-as and snapshot tests | NOT VERIFIED | No `Maran.Host.IntegrationTests` file mentions `ApiKey` or `Idempotency`; no ContractSnapshots files were found by `grep -rlE "ApiKey\|Idempotency" backend/tests --include=*.cs` |
| Builds, tests, migration | NOT RUN | No build was run when this section was written |

## What a reviewer must check

1. `ApiKeyAuthenticationHandler` is the only code that reads `X-Api-Key`:
   `grep -rnE "X-Api-Key|ApiKeyHeader" backend/src --include=*.cs` lists the handler, the options and
   nothing that reads a query string or cookie. Positive control: the same pattern finds the handler.
2. The scheme is not the default and no existing policy names it:
   `grep -rnE "AuthenticationSchemes|\"ApiKey\"" backend/src --include=*.cs` lists only
   `ApiKeyPolicies`, the scheme registration and its tests; and a test calls every existing authorized
   endpoint with a valid all-scopes key and gets 401.
3. All failure paths return one identical 401, and the handler does the hash and the compare on an
   unknown prefix (read the handler, do not trust the test alone; the test compares bodies and
   headers, not timings).
4. `ApiKeyCaller` is a case of `Caller`, is not a `PrincipalCaller`, and every `switch` over `Caller`
   in `backend/src` decides it (the build is exhaustive; the reviewer reads each arm that is not
   "sees nothing").
5. A JWT carrying the key-id claim is refused or ignored; a key principal reaching a JWT policy fails
   closed.
6. The digest comparison is `CryptographicOperations.FixedTimeEquals`, the key never appears in
   `Log*` calls, audit entries, DTOs other than `ApiKeyCreatedDto`, or exception messages.
7. Revocation: no cache between the row and the handler;
   `grep -rnE "IMemoryCache|MemoryCache|Cache" backend/src/Maran.Host/Authorization backend/src/Maran.Modules/Identity/Services`
   reports nothing in the key path.
8. Last-use: **as built it is a throttled read-modify-write**, not one conditional UPDATE (judge the
   residual under section 9); a failed write cannot fail a request (`DbUpdateException` is caught).
9. The failed-attempt limiter exists, runs before the lookup, keys on `ClientAddress`, and does not
   call the firewall ban path; `RateLimitPartitionKey` has the `apikey:` branch before `ip:`. Decide
   whether answering the same 401 (not a 429) at lockout is acceptable.
10. IP binding reads only `ClientAddress.Of(RemoteIpAddress)`; `Unknown` matches no CIDR; `/0` is
    refused; `CidrRule` has accept and refuse controls for IPv4, IPv6 and mapped forms.
11. The three management actions are `AdminOnly` and `ViewAsAccess.Refused`; no `ApiKeyPolicies` entry
    guards them.
12. `ApiKeyScopes` is the only vocabulary; an unknown stored scope grants nothing.
13. Idempotency: `CallerKey` is typed and always in the lookup; `[IdempotencyNoStore]` on every
    secret-returning action; `POST api/v1/api-keys` is not `[Idempotent]`; the 256 KiB and charset
    rules hold; reservations expire.
14. OpenAPI: no `MapOpenApi` in the running host
    (`grep -rn "MapOpenApi" backend/src --include=*.cs` reports only the CLI/test composition); the
    generated `openapi.json` has no server-established members and no internal prose.
15. Every new key and error code is in the en, ru and hy resx triples; the migration is expand-only.

## What the author could not verify

- Nothing was built or run when this note was written (shared-tree brief: no builds), and the code did
  not exist yet; every claim about behaviour is a design commitment, not a measurement. The integrator's
  run is the first one.
- Whether nginx's shipped `log_format` omits custom request headers; only the `proxy_set_header`
  line was read, not the log format or the installed server blocks on every supported platform.
- How the real request pipeline orders an `ApiKey`-scheme failure against the rate limiter: read from
  `Program.cs` order, not observed. The failed-attempt limiter exists because this was read, not
  because it was seen to fail.
- (Resolved by the lead override: the Accounts controller is not touched for keys; no `accounts.*`
  scope exists.)
- That no existing `switch` over `Caller`, `HttpContextCurrentUser` consumer, tenant filter or
  journal silently treats an unknown caller as an administrator: the compiler finds the `switch`es, it
  does not find `is AdministratorCaller` style checks or claim reads that bypass `Caller`;
  `grep -rnE "PanelClaimTypes\.(Role|UserId)" backend/src --include=*.cs` is the reviewer's list.
- The behaviour behind a second proxy or CDN, and IPv6 forwarded-address handling beyond what
  `ClientAddress` documents.
- Timing equality of the unknown-prefix path against the known-prefix path: argued, not measured.
- Whether the second-factor bypass of a machine credential is acceptable to the owner's threat
  model; this note records it, it cannot decide it.

## Addendum (issue #551): a key lives only as long as its creator's right to sign in

Status: **Second review: OUTSTANDING**, as the note above. Written after the code was read and before
the change was built or run; nothing in it is a measurement.

### What changed

1. **The authenticator judges the creator of EVERY key** (`ApiKeyAuthenticator`, after the digest
   comparison and the state and address checks, for a matching key only): the login named by
   `ApiKey.CreatedByUserId` must exist and satisfy `User.MayAuthenticate` (active lifecycle, not
   suspended), read on every call with no cache. This replaces the earlier rule, which looked only at
   the login of a RESELLER owner (`ApiKey.ResellerId`); a key the administrator owns was never tied to
   anybody's login. A reseller's key is created by the reseller's own login, so the one rule covers it.
2. **Suspending a key creator revokes its keys, and a resume does not revive them.**
   `ResellerSuspendingHandler` stages the revocation of every unrevoked key the reseller's login created
   in the same save as the suspension (`ApiKeyCreatorRevoker`, Identity `Services/`) and journals one
   `ApiKeyRevoked` entry per key after the commit: the panel as actor, the subject naming prefix, name
   and scopes (never the digest) and the reason `CreatorDisabled`. The revocation is on the key row, so a
   leaked key stays dead after a resume and a new key has to be issued. The authenticator's creator
   check stays as defence in depth. No administrator-login suspension path exists today.
3. **Deleting a login refuses its keys.** Only the administrator and reseller logins create keys
   (`ApiKeysController`, `ResellerApiKeysController`); customer and sub-user logins cannot, so their
   deletion paths do not call the revoker. The reseller-deletion handler already removes the reseller's
   keys outright, which is stronger. Even with no row revoked, a deleted creator's key is refused (the
   lookup finds no login).
4. No migration: `RevokedAt` exists and the reason lives in the journal's subject text.

### What an attacker could do with this surface, and why it is safe now

- **Keep using a key after its issuer was shut out.** Before: an administrator-owned key never looked at
  a login, and a reseller-owned one only at the reseller's. Now every key is judged on every call, so
  the key dies with the creator's access on the next request.
- **Learn which keys exist.** The creator check runs after the digest comparison and only for a matching
  key, and its refusal is the same `ApiKeyDenied` as every other: no new difference between an unknown
  prefix and a known one. The added read is one primary-key lookup for a matching key; its timing is
  argued, not measured.
- **Lock an integration out by suspending a person.** This is the intended consequence (owner decision O-12); an integration that must outlive one person's login is
  issued by a login that is not expected to leave.

### What a reviewer must check

- `grep -n "CreatedByUserId" backend/src/Maran.Modules/Identity/Services/ApiKeyAuthenticator.cs` shows
  the creator read; the refusal precedes `RecordUseAsync`, so a refused key moves no last-use stamp.
- The unit tests `ApiKeyAuthenticatorTests` hold the active creator accepted (inverse control), a missing
  and a not-yet-active creator refused, a suspended reseller's key refused (defence in depth) and a
  deleted creator's key refused; `ApiKeyCreatorRevokerTests` and `ResellerSuspendingHandlerTests` hold
  that only the suspended login's keys are revoked, journalled, and not revived by a resume.
- That `ResellerSuspendingHandler` stages the revocation BEFORE its `SaveChangesAsync` and records the
  entries AFTER it.

### What the author could not verify

- Nothing was built or run. The Wolverine handler classes are generated at build time and gitignored, so
  the changed constructors are resolved by the container, which no test here exercises.
- That no deployment issues keys from a login that is meant to be disabled routinely (a shared service
  account that is suspended on holiday), which this change would now lock out.
- Timing equality of the unknown-prefix and known-prefix paths with the extra read: argued, not measured.

Second review: OUTSTANDING (#331)
