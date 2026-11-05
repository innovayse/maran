# API keys

An API key is a long-lived credential that lets an integration call a small, named set of panel
endpoints without signing in. It is for machines. Only an administrator creates, lists and revokes keys,
and only from a signed-in session: no key can manage keys (issue #144).

## What a key can and cannot do

A key reaches only endpoints that explicitly accept it. Every other endpoint, including everything the
panel's own screens use, stays reachable by a signed-in login alone: a key sent there is answered with
a plain 401.

| Scope | Endpoint a key may call with it |
|---|---|
| `self.read` | `GET api/v1/api-keys/self`: the key's own id, name, prefix, scopes, expiry and last use |
| `server.read` | `GET api/v1/server/status`: the server status |

- `self.read` is implicit: every valid key has it, and it is not chosen at creation.
- `server.read` is chosen at creation. The list of scopes a key can carry comes from
  `GET api/v1/api-keys/scopes`, localized, so a client never hard-codes it.
- No scope reaches customer data. A key is not an account owner, a reseller or an administrator: every
  tenant list answers it with nothing.
- A key cannot create, list or revoke keys, cannot sign anyone in, and cannot ask to view as another
  login.
- A key skips the second factor. Creating it needed an administrator's session, using it needs only
  possession. Limit what that is worth with the controls below: a narrow scope, an expiry, an address
  list, and prompt revocation.

## Create a key

`POST api/v1/api-keys`, as a signed-in administrator, with a JSON body:

| Field | Required | Rule |
|---|---|---|
| `name` | yes | 1 to 64 characters, no control characters; shown in lists |
| `scopes` | yes | at least one value from `GET api/v1/api-keys/scopes` |
| `allowedCidrs` | no | up to 16 address ranges (IPv4 or IPv6, for example `203.0.113.0/24`); a `/0` range is refused because it binds nothing |
| `expiresAt` | no | an instant in the future; without it the key lives until revoked |

The answer is `201` with the key's description and its `secret`. A key looks like
`mk_<8 hex>_<43 characters>` (55 characters). The panel holds only a one-way digest, so:

- **The secret is shown once**, in this response. It is not stored, cannot be shown again, and cannot
  be recovered by anyone. If it is lost, create a new key and revoke the old one.
- The 8-hex part (the prefix) is not secret. Lists and audit entries show it so a key can be told
  apart.
- The panel holds at most 100 working keys (not revoked, not expired). Past that, creation is refused
  with `409`.

Creating a key is not an idempotent operation and does not accept an `Idempotency-Key` replay: a
retried create that minted a second live key would be worse than a failed retry.

## Use a key

Send it in the `X-Api-Key` header:

```
curl -H "X-Api-Key: mk_0123abcd_..." https://panel.example/api/v1/api-keys/self
```

- Only that header is read. A key in the query string, in a cookie or in `Authorization` is not a key.
  Never put one in a URL: URLs are written to access logs.
- Every refusal is the same bodyless `401`: malformed, unknown, wrong, revoked, expired, or sent from
  an address outside its list. The response does not say which, so a thief learns nothing from it. The
  operator can see the reason in the panel's log, which records the key's id or prefix and the caller's
  address, never the presented value.
- Calls made with a key are rate limited per key, so one noisy integration does not starve another
  behind the same address.
- Failed attempts are counted per client address: after 5 failures within 300 seconds (the sign-in
  limits, `RateLimiting:LoginMaxAttempts` and `RateLimiting:LoginLockoutSeconds`), further key requests
  from that address get the same `401` until the window ends, **even with a valid key**. The count
  lives in memory and starts again when the panel restarts.

## Address binding

A key with `allowedCidrs` is accepted only from those ranges. The address checked is the one the
panel's own reverse proxy saw for the client, spelled one way everywhere (an IPv4 address carried in
an IPv6 form is read as IPv4). Consequences:

- Behind a second proxy or a CDN the panel sees that proxy's address, not the client's. A bound key
  then either fails for everyone or is accepted for everyone behind the proxy. Bind keys only when the
  panel's proxy sees the real client.
- A request whose address cannot be determined matches no range, so a bound key is refused.
- A key without `allowedCidrs` is not bound.

## Expiry and revoking

- Expiry is checked against the clock on every request. An expired key shows as `expired` in the list
  and answers `401`.
- `DELETE api/v1/api-keys/{id}` revokes a key. It is effective from the next request: the panel reads
  the key's row on every call and caches nothing. A request already past authentication when the
  revoke commits completes. Revoking an already revoked key succeeds and changes nothing; an unknown
  id is `404`.
- Scopes, address list and expiry cannot be edited. To change one, create a new key and revoke the old.

## Rotation

Rotate on a schedule and whenever a key may have leaked:

1. Create the new key with the same scopes, ideally with an expiry shorter than your rotation period.
2. Deploy it to the integration.
3. Read the old key's last use in the list; when it stops moving, nothing depends on it.
4. Revoke the old key.

Prefer a finite expiry for every key. Prefer the narrowest scope. Prefer an address list when the
integration has a stable address the panel sees.

## Listing, last use and audit

`GET api/v1/api-keys` lists keys, paged: search by name, filter by status (`active`, `revoked`,
`expired`), sort by `name`, `createdAt` or `lastUsedAt`. A key's status is derived from its revocation
and expiry and is not stored. The list never contains a secret or a digest.
`GET api/v1/api-keys/statuses` lists those three values with localized names, in that order, for a status filter.

- **Last use** (`lastUsedAt`, `lastUsedIp`) is written after a successful authentication, at most once
  per minute per key. It is therefore accurate to about a minute, and the address shown is the one of
  the call that wrote it. A refused key never moves it, so wrong guesses cannot disturb a real key's
  record. If the write fails, the call still succeeds.
- **Audit:** the journal records `ApiKeyCreated` and `ApiKeyRevoked` with the key's prefix, name and
  scopes (never the secret or digest), and one `ApiKeyCall` entry per call a key makes to an
  endpoint: method, route template (never the concrete URL, so no ids or query string), status and
  caller address. The actor of a call entry is the key's id; it has no user.
- **What the call audit does not see:** it is recorded when a request reaches an endpoint. A request
  refused before that, such as a bad key, a rate limit or a refused policy, writes no call entry. Wrong
  keys are visible only in the panel's operational log.
