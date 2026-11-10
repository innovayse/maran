# The licence window: formats, rule, conformance vectors

This folder holds the one artefact that any implementation of the panel's licence window, the open one in
`backend/src/Maran.Modules/Licensing/` and the closed one, is held to: `conformance-vectors.json`.

**Test keys only.** The seeds that signed every fixture are committed under `backend/tests` and are
public. The file carries two TEST public keys (the licence key and the lease key) and no production key.
A real build trusts the key its release embeds (`installer/keys/licence-verification.pub` and
`installer/keys/lease-verification.pub`, generated offline by the issuing service and not in this
repository) and refuses every licence or lease while it has none.

## The licence file (`/var/lib/maran/licence.json`)

```json
{"payload": { ... }, "signature": "<base64 Ed25519 over the payload's exact text>"}
```

Payload members: `id`, `product` (`maran`), `tier` (display only), `modules` (the module ids the licence
covers), `expiry` (ISO 8601), `server` (optional machine id the licence is bound to) and `offline`
(optional boolean, absent means false; a licence that says `true` needs no lease). A member of the wrong
type is malformed; `offline` sits inside the signed payload, so it cannot be switched on by editing the file.

## The lease file (`/var/lib/maran/lease.json`)

```json
{"payload": {"kind": "lease", "licence": "<id>", "issued": "<ISO>", "validUntil": "<ISO>", "server": "<machine id, optional>"},
 "signature": "<base64 Ed25519 over the payload's exact text, made with the LEASE key>"}
```

The lease key is a different key from the licence key, so a leaked lease key cannot mint licences and the
reverse. `validUntil` must be later than `issued`. A lease is issued for 14 days and renewed daily.

## The rule (`ModuleCoveragePolicy` over `LeaseGracePolicy`)

For one module id, the verdict is:

| Situation | Verdict | `validUntil` |
|---|---|---|
| No licence, a forged or product-mismatched one, or one bound to another server | `notCovered` | none |
| Licence valid, module not in its `modules` | `notCovered` | none |
| Licence valid, `offline: true` | `covered` | the licence's expiry |
| Licence valid, lease fresh or only overdue (older than one day, not yet ended) | `covered` | the earlier of lease end and licence expiry |
| Licence valid, lease missing or ended | `lapsed` | the lease end, if there is one |
| Licence valid, lease for another licence or server, issued more than five minutes in the future, or not trusted | `notCovered` | none |
| Licence expired, module was in its `modules` | `lapsed` | the licence's expiry |

Equality at an end is an end: a lease whose `validUntil` equals "now" has ended. A lease issued exactly
five minutes ahead of "now" is believed; one tick more is refused. Module ids are compared exactly.

An included module never asks the window. A refusal of a paid module never touches sign-in, health, the
licence status and install endpoints, backups or the `maran` command line.

## Running the vectors

`backend/tests/Maran.Modules.Licensing.Tests/Vectors/ConformanceVectorTests.cs` runs every vector through the
real file sources, signature checks, the lease rule and the coverage rule with the clock set to the vector's
`now`. Each vector lists the licence text (or null), the lease text (or null), a module id and the expected
`verdict` and `validUntil`. The host's machine id for the run is `hostMachineId`.
