# The billing contract

`lifecycle.json` in this folder is the sequence of calls a billing provider relies on when it provisions
hosting accounts through the reseller API (issue #7; the reference for the API itself is
[../provisioning.md](../provisioning.md)): create an account, create it again with the same key, read it,
read its usage, suspend, reactivate, change its plan, read the server info, terminate it, and read it
once more. Each step states the request the provider sends and what the provider reads from the answer.

The file is one artefact held by two sides:

- **The panel** replays it against a real host on every change
  (`backend/tests/Maran.Host.IntegrationTests/BillingContract/BillingContractReplayTests.cs`, a gate in
  the backend job). A route, a status, a code, a required field or a type that the file relies on cannot
  move without that test failing, and a route in the file that no controller declares fails a second test.
- **The billing side** replays the same file against a staging panel before it ships a driver, so its
  expectations and the panel's behaviour are checked against one document and not against two
  descriptions of it.

## What a step says

Each entry of `interactions` has:

| Field | Meaning |
|---|---|
| `name` | what the step does, in words; unique |
| `request` | `method`, `path` (relative to `basePath`), optional `headers` and JSON `body` |
| `credential` | which key signs the call: `full` when omitted, or `provisioner`, `reader`, `unknown` (a well-formed key the panel never issued) or `none` (no key header) |
| `requires` | inputs the step needs (`smallPlanId`, `foreignAccountId`, ...); the step is skipped when the replay was not given one |
| `arrange` | a condition on the host the step needs (see `arrangements`); the step is skipped when the replay cannot create it |
| `response.status` | the HTTP status, always checked |
| `response.code` | the machine `code` of the problem document, when the step expects a refusal |
| `response.headers` | response headers that must carry the stated value (`Idempotent-Replayed: true`) |
| `response.empty` | the body must be empty (the bodyless `401`) |
| `response.properties` | what the provider reads: `name` (a dotted path; `$` is the body itself), `type`, and optionally `equals` (a literal) or `equalsVariable` (a captured or supplied value) |
| `response.absent` | properties the answer must not carry (the customer's e-mail address) |
| `response.capture` | values kept for later steps, `variable: path` |

`type` is one of `string`, `guid`, `instant`, `integer`, `number`, `boolean`, `object`, `array`.
Placeholders `{{name}}` in paths, header values, bodies and expected values are replaced by an input, a
captured value or a built-in (`run`, `suffix`, `randomGuid`; the file's `builtins` says what each is).

## The additive law

The file lists **the properties the provider reads, not the whole answer**. That is deliberate and it
is the same direction as `maran openapi` (docs/api/README.md): a field the panel adds passes, a field the
provider reads that is removed, retyped or made optional fails.

- Adding a step, a property to read or an input is a compatible change.
- Removing a step or a property, changing a status, a code or a type is **breaking** and is a decision
  for a new major version of the API (rules/architecture.md): bump `revision` and say why in the commit.
- A step the replay cannot run is *skipped*, never passed silently: the report lists every skipped step.
  The panel's own replay must skip nothing.

## Replaying it against a staging panel

1. In the staging panel, create a reseller with room for a few accounts and one plan (`planId`). Create
   a key for it with `accounts.read`, `accounts.write` and `accounts.delete` (the `full` credential).
2. Optionally create a key with `accounts.read` only (`reader`) and one with `accounts.read` and
   `accounts.write` (`provisioner`), a second, smaller plan (`smallPlanId`), a plan of another reseller
   (`foreignPlanId`) and an account of another reseller (`foreignAccountId`). Steps that need what you
   did not supply are skipped.
3. Replay the interactions in order with your HTTP client: expand the placeholders, send the request
   with the credential's key in `X-Api-Key`, and check the answer against `response`.
4. Steps with `arrange` need a host condition that a staging panel cannot be told to produce
   (`diskUsedAbovePlan`, `finalBackupFails`); skip them. The panel's own test arranges both.
5. The verdict is: **every step that ran matched, and the list of skipped steps is the one you expected**.
   The replay creates one account named `bc<suffix>` and terminates it before it ends; a replay that
   stops half way leaves that account behind, and the terminate step of the next replay will not find it
   (it names a fresh suffix), so remove a leftover by hand.

## What it does not cover

Rate limiting (a replay is far below the limit), the order of fields, localized wording of `title` and
`detail`, response headers other than the ones a step names, and the reads that list accounts and plans
(`GET accounts`, `GET plans`), which [../api-keys.md](../api-keys.md) describes.
