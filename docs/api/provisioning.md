# Provisioning API

The provisioning API lets a billing system or any other integration create, change and terminate the
hosting accounts of one reseller without a person signing in (issue #7). It is eight routes under
`api/v1/reseller-api/`, called with a reseller's API key. This page is the reference for the people who
write the integration. How keys are made, bound and revoked is in [api-keys.md](api-keys.md); how a
retried request is answered is in [idempotency.md](idempotency.md).

## Who may call it

Only a key a **reseller** owns, holding the scope a route needs. The administrator's own keys reach none
of these routes, and a signed-in login is refused here (it uses the panel's own `api/v1/accounts`
routes). A single-operator server that bills through this API creates one reseller to own the billed
accounts.

Send the key in the `X-Api-Key` header on every call:

```
X-Api-Key: mk_0123abcd_<43 characters>
```

| Scope | Routes it opens |
|---|---|
| `accounts.read` | `GET accounts/{id}`, `GET accounts/{id}/usage`, `GET info` (and the list and plan reads of [api-keys.md](api-keys.md)) |
| `accounts.write` | `POST accounts`, `POST accounts/{id}/suspend`, `POST accounts/{id}/reactivate`, `PUT accounts/{id}/plan` |
| `accounts.delete` | `DELETE accounts/{id}` |

- The scopes are independent: `accounts.write` does not allow terminating, `accounts.delete` does not
  allow creating, and neither allows reading. A key made only to provision cannot destroy data. Give a
  key `accounts.delete` only if the integration really terminates accounts.
- A key reaches only the accounts its reseller owns. An account of another reseller, an account that
  does not exist and an account already terminated are all the same `404 AccountNotFound`.
- A refused key (malformed, unknown, revoked, expired, wrong address, or the reseller is suspended) is
  the bodyless `401` described in [api-keys.md](api-keys.md). A valid key that lacks the scope for a route
  is `403`.

## Conventions

- Bodies and answers are JSON in camelCase. Enum values are camelCase strings (`"active"`,
  `"suspended"`). Identifiers are GUIDs. Instants are ISO 8601 with an offset.
- Every refusal is an RFC 7807 problem document with a stable `code`:

  ```json
  {
    "status": 409,
    "code": "AccountNameTaken",
    "title": "This account name is already taken.",
    "detail": "This account name is already taken.",
    "type": "https://httpstatuses.io/409",
    "correlationId": "0f3c..."
  }
  ```

  Branch on `status` and `code`, never on `title` or `detail`, which are localized with
  `Accept-Language` (`en`, `ru`, `hy`) and may be reworded. Quote `correlationId` when asking the
  operator about a call.
- The API only gains routes and fields inside a major version. A client must ignore response fields it
  does not know. The revision it talks to is `apiRevision` in `GET info`.

## Idempotency-Key is required on every write

`POST accounts`, `POST accounts/{id}/suspend`, `POST accounts/{id}/reactivate`, `PUT accounts/{id}/plan`
and `DELETE accounts/{id}` refuse a request without an `Idempotency-Key` header: `400` with the code
`IdempotencyKeyRequired`, before anything runs. This is stricter than the rest of the panel, where the
header is optional, because an integration retries unattended and a retried create without a key can
create two accounts.

- Use a fresh random value (a UUID) for each distinct operation, and send the **same** value and the
  **same** request when you retry one. Format and storage rules are in [idempotency.md](idempotency.md).
- A retry of a finished request returns the first status and body again with `Idempotent-Replayed: true`
  and does nothing a second time. That includes a stored error: a retried create that was a `409` is a
  `409` again, not a fresh attempt.
- The same value with a different request (another body, another account id) is `422
  IdempotencyKeyReused`. The same value while the first call is still running is `409
  IdempotencyRequestInProgress`; wait and retry.
- A server error (`5xx`) is never stored, so retrying with the same value runs the request again.
- The reads take no key and need none.
- A stored answer lives 24 hours. A retry after that is a new request: for a create it meets
  `AccountNameTaken`, for a terminate `AccountNotFound`. Treat those as "already done" when you are
  retrying your own operation.

## Rate limit

Writes are limited **per key**, not per address: by default **60 requests per minute for each key**
(`RateLimiting:ProvisioningMaxRequests` and `RateLimiting:ProvisioningWindowSeconds`, an operator
setting). The reads have a separate, general per-key bucket, so polling cannot use up the allowance for
writes and the reverse. A call over the limit is `429` with code `HostRateLimited` and a `Retry-After` header
in seconds; it is refused at once, never queued. Honour `Retry-After` and keep the same
`Idempotency-Key` when retrying.

## Routes

All paths below are relative to `api/v1/reseller-api/`.

| Operation | Method and path | Scope |
|---|---|---|
| Create an account | `POST accounts` | `accounts.write` |
| Read one account | `GET accounts/{id}` | `accounts.read` |
| Suspend | `POST accounts/{id}/suspend` | `accounts.write` |
| Reactivate | `POST accounts/{id}/reactivate` | `accounts.write` |
| Change plan | `PUT accounts/{id}/plan` | `accounts.write` |
| Terminate | `DELETE accounts/{id}` | `accounts.delete` |
| Usage of one account | `GET accounts/{id}/usage` | `accounts.read` |
| Server info | `GET info` | `accounts.read` |

Listing the reseller's accounts and plans (`GET accounts`, `GET plans`, `GET plans/{id}`) is described in
[api-keys.md](api-keys.md) and uses the same account shape.

The panel's own vocabulary is **reactivate**, not unsuspend.

### The account shape

Create, read, suspend, reactivate and change plan answer the same object:

| Field | Type | Meaning |
|---|---|---|
| `id` | GUID | the account |
| `name` | string | unique short name; also the Linux user name |
| `primaryDomain` | string | the account's primary domain |
| `planId` | GUID | the plan bounding its limits |
| `status` | string | `active` or `suspended` |
| `statusName` | string | the status, localized for the request |
| `createdAt` | instant | when the account was created |
| `resellerId` | GUID | always the key's reseller |
| `resellerName` | string | that reseller's name |

The owner's e-mail address is never returned by this API.

### Create an account

`POST accounts`, answer `201` with the account shape and a `Location` header pointing at
`GET accounts/{id}`.

| Field | Required | Rule |
|---|---|---|
| `name` | yes | 3 to 30 characters: a lowercase letter, then lowercase letters, digits or underscores (no hyphen); not a reserved system name; unique across the panel |
| `primaryDomain` | yes | a valid host name, written without a leading `www.`; unique |
| `planId` | yes | a plan the reseller owns (see `GET plans`) |
| `ownerEmail` | yes | the customer's address, up to 256 characters |
| `ownerLanguage` | no | `en`, `ru` or `hy`; the invitation is written in it; `en` when left out |

Do not send `resellerId`: the owner is always the key's reseller, and naming any other one is refused. The panel creates the Linux user, home
directory and disk quota, then mails the customer an invitation to set a password and sign in.

- **No site is created.** The account exists with its primary domain recorded; adding a site is a
  separate call by the customer or the panel's Sites API.
- **The invitation is sent by the panel.** An integration that sends its own welcome message sends a
  second message; it cannot suppress this one.

Refusals: `400` for a malformed field (`AccountNameInvalid`, `AccountNameReserved`,
`AccountDomainWwwPrefix`, `AccountOwnerLanguageUnsupported`, or a generic validation code); `404`
`PlanNotFound` for a plan the reseller does not own or that does not exist; `404` `ResellerNotFound` when
the body names a `resellerId` other than the key's reseller (the answer is the same as for a reseller that
does not exist); `409` `AccountNameTaken`,
`AccountDomainTaken`, and the envelope limits `ResellerAccountLimitReached` and
`ResellerDiskPoolExceeded`; a suspended reseller's keys answer `401`, so `ResellerSuspended` is not met here.

```
curl -X POST https://panel.example/api/v1/reseller-api/accounts \
  -H "X-Api-Key: mk_0123abcd_..." \
  -H "Idempotency-Key: 6f1c2a3e-0000-4000-8000-000000000001" \
  -H "Content-Type: application/json" \
  -d '{"name":"olive","primaryDomain":"olive.example.com","planId":"<plan guid>","ownerEmail":"owner@example.com","ownerLanguage":"en"}'
```

### Read one account

`GET accounts/{id}` answers `200` with the account shape. Use it to confirm a state after a write.

### Suspend and reactivate

`POST accounts/{id}/suspend` stops the account's sites and services while its data stays.
`POST accounts/{id}/reactivate` lifts it. No body. Both answer `200` with the account shape. Calling
either on an account that is already suspended or already active is not a no-op and not an error: the
operation runs again and the answer is `200` with the account as it now is.

### Change plan

`PUT accounts/{id}/plan` with body `{"planId": "<plan guid>"}` moves the account to another plan of the
same reseller and applies the new disk quota on the host. Answers `200` with the account shape. Refusals:
`404` `PlanNotFound`; `409` `AccountPlanQuotaBelowUsage` when the account already uses more disk than the
new plan allows (free space or choose a larger plan); `409` `ResellerDiskPoolExceeded` when the move
would take the reseller over its disk pool.

### Terminate

`DELETE accounts/{id}` (no body, no query) removes the account, its Linux user and everything under its
home directory.

- **A final backup is always taken first.** A key cannot skip it. If the backup fails the termination is
  refused and the account stays exactly as it was; retry later. The refusal is `500` with the code
  `FinalBackupFailed`, not a `409`. Current behaviour: a `5xx` is never stored by idempotency, so the
  retry may use the same `Idempotency-Key` and runs the termination again. The backup is the operator's safety net
  for a deletion that was a mistake; ask the operator for a restore.
- It can take a while. Use a client timeout of several minutes and retry with the **same**
  `Idempotency-Key`.
- Answers `200` with a JSON number: the bytes the removal freed.
- Terminating is not confirmed by a second factor; this is why it is a scope of its own.
- A second terminate of the same account is `404 AccountNotFound`.

### Usage

`GET accounts/{id}/usage` answers `200`:

```json
{
  "accountId": "<guid>",
  "status": "active",
  "statusName": "Active",
  "planId": "<guid>",
  "planDisplayName": "Small",
  "disk": { "usedBytes": 52428800, "limitBytes": 1073741824 },
  "resources": [
    { "kind": "sites", "count": { "state": "unavailable" }, "limit": { "kind": "limited", "maximum": 5 } },
    { "kind": "databases", "count": { "state": "unavailable" }, "limit": { "kind": "unlimited" } }
  ]
}
```

- `disk.usedBytes` is measured on the host by the agent when you ask, and `disk.limitBytes` is the plan's
  quota. If the measurement fails the answer is an error (`503`), never a zero.
- **`resources[].count` is `{"state":"unavailable"}` for a key.** The modules that own sites, databases,
  logins, cron entries and backups show a key nothing, and a zero would read as "this account owns
  nothing". When a count is given it is `{"state":"counted","used":N}`. Branch on `state`; do not treat
  `unavailable` as zero. `limit` is `{"kind":"limited","maximum":N}` or `{"kind":"unlimited"}`.
- `kind` is one of `sites`, `databases`, `sftpLogins`, `ftpLogins`, `cronEntries`, `backups`; more kinds
  may be added.

Bill disk by `disk`, which is real. Anything else a plan sells (sites, databases) is not measurable by a
key today.

### Server info

`GET info` answers `200` with what this key's reseller may provision:

| Field | Meaning |
|---|---|
| `apiRevision` | the revision of this API; it only grows, by additions |
| `accountCount` | accounts the reseller owns now, in any status |
| `planCount` | plans the reseller owns now |
| `diskAllocatedMb` | disk promised to those accounts |
| `envelope` | what the operator sold the reseller: `maxAccounts`, `diskPoolMb`, `maxPlans`, per-plan ceilings, `features` and the CPU, memory and process ceilings; a ceiling is `{"kind":"limited","maximum":N}` or `{"kind":"unlimited"}` |

It carries nothing about the host: no panel version, no server disk or capacity. The headroom is
`envelope` minus the figures beside it; it may be negative if the operator lowered the envelope.

## Errors at a glance

| Status | Codes | Meaning for the integration |
|---|---|---|
| `400` | `IdempotencyKeyRequired`, `IdempotencyKeyInvalid`, `AccountNameInvalid`, `AccountNameReserved`, `AccountDomainWwwPrefix`, `AccountOwnerLanguageUnsupported` | fix the request |
| `401` | none (no body) | the key is not accepted; do not retry |
| `403` | scope missing | the key lacks the scope for this route |
| `404` | `AccountNotFound`, `PlanNotFound`, `ResellerNotFound` | no such account or plan for this reseller, or a create that names another reseller |
| `409` | `AccountNameTaken`, `AccountDomainTaken`, `AccountPlanQuotaBelowUsage`, `ResellerAccountLimitReached`, `ResellerDiskPoolExceeded`, `IdempotencyRequestInProgress` | a state conflict; `IdempotencyRequestInProgress` is retried, the others are decisions |
| `422` | `IdempotencyKeyReused` | the key was used for a different request |
| `429` | `HostRateLimited` | wait `Retry-After` seconds |
| `500` | `FinalBackupFailed` (terminate) | the final backup failed and the account is untouched; not stored by idempotency, retry later with the same key |
| `503` | the host could not answer (for example a disk measurement) | transient; retry with the same key |

## Polling, not webhooks

The panel sends no callbacks. An integration learns the state of an account by asking: `GET accounts/{id}`
for the status, `GET accounts/{id}/usage` for disk. After a write, the answer to the write already carries
the new state; poll only to reconcile (for example nightly, listing `GET accounts`). Keep polling well
inside the read bucket.

## The billing contract

The sequence a billing provider relies on (create, replay, read, usage, suspend, reactivate, change plan,
info, terminate, read after terminate, and the error cases) is held as a machine-readable file,
`docs/api/billing-contract/lifecycle.json`, replayed against the real panel by an integration test on
every change. A provider replays the same file against a staging panel; see
[billing-contract/README.md](billing-contract/README.md).
