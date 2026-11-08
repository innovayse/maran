# Idempotency keys

A client that loses a connection after sending a request cannot tell whether the panel acted. An
idempotency key lets it send the same request again and get the first answer back instead of acting a
second time (issue #136). It applies only to the endpoints listed below, and only when the client sends
the header, except on the reseller API writes, which refuse a request that lacks it.

## The header

`Idempotency-Key: <value>`

- 1 to 255 characters from `A-Z a-z 0-9 _ - : .`. Anything else, an empty value, or the header sent
  twice is refused with `400` and the code `IdempotencyKeyInvalid`, before anything is stored. A
  random UUID is a good key.
- Without the header the request runs exactly as it did before, except on the reseller API writes (see
  "Endpoints that accept it"), which are refused.
- A key belongs to the caller that sent it: it is stored with the signed-in login's id (or an API
  key's id), so another caller sending the same value is a different key and can never read this
  caller's stored answer. Use a fresh key for each distinct operation.

## What the panel does with it

For a request that carries a valid key, the panel decides from what it already holds for this caller
and key:

| Situation | Answer |
|---|---|
| First time this key is seen | The request runs and its answer is stored |
| Same key, same request, finished | The stored status and body are returned again, with the header `Idempotent-Replayed: true`; nothing runs |
| Same key, a different request | `422` with code `IdempotencyKeyReused`; nothing runs |
| Same key, the first request still running | `409` with code `IdempotencyRequestInProgress`; retry shortly |
| Same key, the stored answer is one the panel refuses to keep | `409` with code `IdempotencyResponseNotStored` |

"Same request" means the same method, route, route values, query string and body. The comparison is
over a keyed fingerprint (HMAC-SHA256 under a key derived from the panel's secret) of those, so a changed
query value or body field is a different request. It is keyed because a body can carry a password: a
plain digest stored beside it could be guessed offline by anyone who could read the table. A body
larger than 1 MiB cannot be compared and is refused as `IdempotencyKeyInvalid` when a key is sent.

### Which answers are stored

- Stored: successful answers (2xx) and deterministic client errors (4xx), for example a `409` because
  a name is taken or a `404`. Replaying a stored 4xx returns that error again, not a fresh attempt.
- Never stored: server errors (5xx), a response of a kind the panel cannot reproduce byte for byte, and
  any response body over 256 KiB. The reservation is released, so the client may retry with the same
  key and the request runs again.
- A response from an action marked as carrying a secret is never stored, and a replay of it answers
  `IdempotencyResponseNotStored`. No endpoint carries that mark today; the one that returns a secret,
  creating an API key, does not accept idempotency at all.
- Request bodies are never stored, only their keyed fingerprint.

### How long

- A reservation for a request still running lasts 5 minutes. If the panel dies mid-request, the key
  becomes usable again after that. Each reservation carries its own token, so a request that outlives
  its 5 minutes cannot complete or release the record of a retry that took the key after it. It can
  still run twice: an operation longer than 5 minutes (deleting an account takes a final backup first)
  that is retried after the lease lapsed is run a second time.
- A finished answer is kept for 24 hours from when the key was first seen, then removed by an hourly
  job. After that the same key is new, and the request runs again.
- Stored answers are plain text in the panel's database for those 24 hours, and a deleted account's
  stored answers are not removed with it; they expire on their own.

## What it does not promise

Idempotency narrows duplicate execution; it does not prove exactly-once. The action and the storing of
its answer are two writes. If the panel stops between them, the action has happened and no answer was
stored, so a retry with the same key runs it again. Repeating a request that already ran is still
handled by the endpoint itself: suspending a suspended account is a no-op, creating an account with a taken
name is a `409`, and deleting a deleted account is a `404`.

If the action succeeds but the panel cannot store its answer (its database is unreachable), the response
is still the action's own answer, not a `500`; the key then stays reserved until its 5 minutes pass.

## Endpoints that accept it

The panel's own account routes in `api/v1/accounts` (`AccountsController`), for administrators and
resellers signed in with a login, where the header is optional:

| Endpoint | Operation |
|---|---|
| `POST api/v1/accounts` | create an account |
| `POST api/v1/accounts/{id}/suspend` | suspend an account |
| `POST api/v1/accounts/{id}/reactivate` | lift a suspension |
| `PUT api/v1/accounts/{id}/plan` | change an account's plan |
| `DELETE api/v1/accounts/{id}` | delete an account |

The reseller API writes in `api/v1/reseller-api/` (the provisioning API, called with a reseller's API
key), where the header is **required**: a request without it is refused `400` with the code
`IdempotencyKeyRequired` before anything runs, because an integration retries unattended and a retried
create without a key can create two accounts. The key is stored under the API key's id
([provisioning.md](provisioning.md) has the whole contract of these routes):

| Endpoint | Operation |
|---|---|
| `POST api/v1/reseller-api/accounts` | create an account |
| `POST api/v1/reseller-api/accounts/{id}/suspend` | suspend an account |
| `POST api/v1/reseller-api/accounts/{id}/reactivate` | lift a suspension |
| `PUT api/v1/reseller-api/accounts/{id}/plan` | change an account's plan |
| `DELETE api/v1/reseller-api/accounts/{id}` | terminate an account |

None of these answers carries a credential, which is why they may be stored. Every other endpoint
ignores the header, and the reads of the reseller API take none.
