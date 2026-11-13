# Marketplace service contract, version 1

This page is the contract between the panel and the marketplace service, written for the team that implements the
service. The service is not part of this repository. The panel side is `backend/src/Maran.Modules/Marketplace`, and
every statement below about what the panel sends, checks or refuses was read from that code; where this page says the
service SHOULD do something, the panel does not depend on it and the sentence says so. The machine-readable form is
[`openapi-v1.yaml`](openapi-v1.yaml). The operator-facing description of the same conversation is
[`docs/operations/marketplace.md`](../operations/marketplace.md); the package format a download carries is
[`ipm-format.md`](ipm-format.md); the security analysis is the threat note
`docs/superpowers/notes/2026-11-07-marketplace-loader-threat-note.md`.

The rule that governs the host the panel may talk to is `rules/security.md` item 10. The default host is
`https://api-marketplace-maran.innovayse.com`; an operator can point the panel at another host with `Marketplace__BaseUrl`, and
the panel then talks to that one host only.

The service behind that host is implemented in C# (.NET), following the same conventions as `backend/`
(`rules/csharp.md`). That is an owner decision; it does not change this contract, and the panel-side client in
`backend/src/Maran.Modules/Marketplace` is unaffected by it.

## Conventions

- Base: `https://api-marketplace-maran.innovayse.com/api/v1`. HTTPS only. The panel refuses a configured address that is not an
  absolute `https` URL with a host, without user information, query or fragment.
- JSON, UTF-8, camelCase member names. Enumerations are written as PascalCase words (`"Entitled"`), and the panel reads
  names without regard to case.
- Timestamps are ISO 8601 with an explicit offset (`2026-11-09T12:00:00Z`).
- Version strings are Semantic Versioning 2.0.0.
- The panel does not ask for compressed responses. The bytes the service signs are the bytes of the body on the wire.
- Display text (names, summaries, release notes, reasons, price text, capability names) arrives already localized in
  the language the request asked for (`Accept-Language` and the `locale` query parameter, `en`, `ru` or `hy`). The panel
  stores no domain constants and translates none of it. See "Locale" below for what each side does with a language
  outside that set.
- Additive only within `v1` (see "Evolution").

## Authentication

There are two credentials and the panel presents them differently.

**The lease credential.** The panel's licence component attaches it to a request by implementing
`ILeaseRequestAuthorizer` (`backend/src/Maran.Sdk/Interfaces/ILeaseRequestAuthorizer.cs`); the marketplace module never
sees the credential. The implementation is `LeaseRequestAuthorizer` in the Licensing module
(`backend/src/Maran.Modules/Licensing/Services/LeaseRequestAuthorizer.cs`); a panel with no Licensing module composed
keeps a stand-in that attaches nothing (`NoLeaseRequestAuthorizer`).

*Wire shape.* One request header, and nothing else is added to identify the licence:

```
Authorization: Bearer <payload>.<signature>
```

`<payload>` and `<signature>` are the two halves of the **lease the licence service itself issued to this panel**
(the `lease.json` the licence component stores, written by the renewal described in "Lease renewal"), each as base64url without padding (RFC 4648 section 5), joined by one
dot, and the credential is written by `LeaseBearerToken` (`backend/src/Maran.Modules/Licensing/Domain/Policies/`):

- `<payload>` is the exact UTF-8 bytes of the lease's JSON payload as it was signed, for example
  `{"kind":"lease","licence":"lic-001","issued":"2026-11-09T09:00:00Z","validUntil":"2026-11-23T09:00:00Z","server":"<machine id>"}`.
  `kind` is `lease`; `licence` is the licence id; `issued` and `validUntil` are ISO 8601 instants (a lease lives 14 days
  and is renewed daily); `server` is the machine id the lease is bound to and is absent for a lease bound to no server.
- `<signature>` is the 64-byte Ed25519 signature over those payload bytes, made by the **lease key** (a key of its own,
  not the licence key and not the marketplace metadata key).

The panel signs nothing and adds nothing. It holds no private key; the credential is the service's own signed statement
handed back unchanged, so the service verifies its own signature over the decoded `<payload>` with the lease public key,
then checks `kind` is `lease`, that `validUntil` has not passed and that `issued` is not in the future, and then looks up
the licence. The service SHOULD bind a grant to the licence and, when the lease has one, to the `server` member. Bytes beyond the
dot, a second dot, a missing half, padding or any character outside the base64url alphabet are not a credential.

*When the panel attaches it.* Only when the installed licence verifies and its lease stands as good now: issued by the
service for this licence, bound to this machine when it names one, signed by the pinned lease key, not future-dated and
not ended (a late daily renewal does not matter while the lease still holds). Otherwise it attaches nothing, and a
request that needs the credential is not sent (`LeaseRequired`). An **offline licence** holds no lease and so never
presents one: the grant is unavailable to it, and its modules arrive in the offline bundle.

*What it exposes.* The credential contains the licence id, the two instants and, for a bound lease, the machine id,
all of which the service chose when it issued the lease. No host name, account, site, user, panel version or other
customer data is part of it or travels with it (`rules/security.md` item 10). Stated limit: a bearer can be replayed by
anyone who sees it until `validUntil`; TLS protects it in transit, the service's server binding limits what a replay
obtains, and the panel never logs or stores the string.

| Request | Lease credential |
|---|---|
| `GET /catalogue`, `GET /modules/{id}`, `POST /updates` | Optional. Attached when the panel has one, so the service can state what the licence entitles. Without it the request still succeeds and entitlements are `Unknown`. |
| `POST /modules/{id}/versions/{version}/download-grant` | Required. If the panel has none, it does not send the request and reports `LeaseRequired` to the administrator. |
| `GET /keys` | Never sent. |
| `GET` of the grant's download address | Never sent. The grant is the credential. |

A lease credential is never placed in a URL.

**The grant.** A download address is single use, short lived and bound by the service to (licence, server identity,
module, version). The panel treats the grant id and the address as secrets: neither is logged, journalled or shown.

## Locale

The contract lists three languages: `en`, `ru` and `hy`.

- **The panel clamps; it never sends a fourth.** It takes the two-letter language of the administrator's interface
  culture (`ru-RU` is `ru`) and, when that is not one of the three (a language the panel gains later, or the invariant
  culture of a thread that has none), sends `en`. The clamp is `MarketplaceLocalePolicy`, and every request takes its
  language from it, so the `Accept-Language` header and the `locale` query parameter always agree and always name a
  listed language.
- **The service must not make an unlisted language fatal.** A `Accept-Language` header the service cannot match falls
  back to `en`. A `locale` query value outside the set is, today, answered `400` with `locale_invalid`; the service SHOULD
  fall back to `en` for it as well (so a panel that gains a language before the service does still browses), and the
  panel does not depend on it because it never sends such a value.
- Text that has no translation in the requested language arrives in `en`.

## What the panel sends, and never sends

The list is the whole of it for the marketplace's requests (the lease renewal is the next section, and sends less). Anything else on a request is a defect in the panel, and a field for a host name, an
account or a user may not be added to `v1` without amending `rules/security.md` item 10 first.

Sent:

- **The panel version**, in the `X-Maran-Panel-Version` request header, on every request except the download.
- **The interface language**, in the `Accept-Language` request header and, for catalogue and module requests, the
  `locale` query parameter. Requests whose answer is not shown to anyone (the grant, the key list) ask for `en`.
- **The release channel** (`stable` by default), in the catalogue query and in the updates body.
- **The administrator's search text and the page cursor**, in the catalogue query, when the administrator typed a
  search or asked for the next page.
- **The module id and version** being looked at or downloaded, in the path.
- **On the updates request only**, the id, installed version and package SHA-256 of each module the check is asked about.
- **The lease credential**, only as the table above says.
- **For a conditional request**, `If-None-Match` with the `ETag` of a page the panel already holds.
- What any HTTPS request implies: the connection's source address and the TLS handshake. The panel sets no cookies and
  no custom user agent.

Never sent: the server's host name, any account name, site, domain or user, any customer data, any list of the
panel's own modules other than the updates body above, the licence file or key, the database or any secret.

## Endpoints

All paths are below the base. `Auth` is the lease credential as defined above.

| Method and path | Auth | Purpose | Answer |
|---|---|---|---|
| `GET /catalogue?channel=&locale=&search=&cursor=` | optional | One page of modules compatible with this panel | signed `CataloguePage` |
| `GET /modules/{moduleId}?locale=` | optional | One module with every compatible version | signed `ModuleDetails` |
| `POST /updates` | optional | Updates and revocations for installed modules | signed `UpdatesReport` |
| `GET /keys` | none | The service's current metadata signing keys | signed `KeySet` |
| `POST /modules/{moduleId}/versions/{version}/download-grant` | required | Entitlement check; a single-use grant | signed `DownloadGrant` |
| `GET {grant.url}` (path `/downloads/{grantId}`) | grant | The `.ipm` bytes | `application/octet-stream` |
| `POST /lease` | none (the licence id in the body) | A fresh licence lease for this panel's licence (see "Lease renewal") | the lease envelope, signed by the lease key |

`search` and `cursor` are omitted when empty. The panel sends no `panelVersion` query parameter; the panel version is
the request header, and the service decides compatibility from it.

### `GET /catalogue`

Answer (`200`, signed):

```json
{
  "items": [
    {
      "id": "mail",
      "displayName": "Mail",
      "summary": "Mailboxes and domains",
      "publisher": "Innovayse",
      "publisherId": "innovayse",
      "tier": "AddOn",
      "entitlement": { "state": "Entitled", "until": "2027-01-01T00:00:00Z" },
      "revoked": false,
      "latest": {
        "version": "1.2.0",
        "minPanelVersion": "1.3.0",
        "maxPanelVersion": null,
        "sizeBytes": 8123456,
        "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        "publishedAt": "2026-10-01T00:00:00Z",
        "releaseNotes": "Adds catch-all mailboxes.",
        "revoked": false,
        "revokedReason": null
      },
      "price": { "display": "9 EUR / month", "currency": "EUR", "purchaseUrl": "https://cabinet.example/buy/mail" }
    }
  ],
  "nextCursor": null,
  "signedAt": "2026-11-09T09:00:00Z",
  "validUntil": "2026-11-10T09:00:00Z"
}
```

- `items` lists only modules for which at least one version fits the panel version in the request header. `latest` is the
  newest such version, or absent when none is compatible.
- `publisher` is the publisher's **display name** (`Innovayse`), already the text to print; the panel shows it as it
  is. `publisherId` is the publisher's stable identifier (`innovayse`), the value the service's own publisher
  administration uses. The panel keeps it and never prints it. The two never share a field: a service that put the
  identifier into `publisher` would show administrators an internal key. `publisherId` is required of the service;
  the panel reads it as optional until every deployed service sends it (a missing `publisherId` is `null`, a
  missing `publisher` makes the answer unusable). The same two members appear in `manifest`.
- `tier` is the licence tier's wire name (`Included`, `AddOn`, `PlanGated`).
- `entitlement.state` is one of `NotRequired` (an `Included` module), `Entitled`, `NotPurchased`, `LeaseExpired`,
  `Unknown` (no lease credential, or the lease could not be judged). `until` is the end of the entitlement or absent.
  `LeaseExpired` also covers a lease whose licence is bound to another server, or has been revoked: the lease is
  judged and found not valid, and the grant refuses the same case with `lease_expired`.
  **A word outside this set makes the panel discard the whole answer**, so a new state is a breaking change (see
  "Evolution").
- `price` is display only; a purchase happens in the vendor's own cabinet, never in the panel. `purchaseUrl` is shown
  by the panel only when it is `https`.
- `sha256` is the SHA-256 of the package FILE, 64 hexadecimal characters. It is a download-integrity value; the
  package's own signature and per-file SHA-384 pins are checked later by the loader (`ipm-format.md`).
- The service SHOULD page with an opaque `cursor`; `nextCursor` is absent or `null` on the last page.

The panel keeps a verified page in memory for 10 minutes per (channel, locale, search, cursor) and sends the page's
`ETag` as `If-None-Match` afterwards. A `304` is accepted only when the panel holds that page, and the page's
`signedAt` is judged again at that moment, so the service SHOULD answer `304` only while the page it refers to is still
inside the 24-hour freshness window, and otherwise answer `200` with a fresh signature.

### `GET /modules/{moduleId}`

```json
{
  "module": { "id": "mail", "displayName": "Mail", "summary": "…", "publisher": "Innovayse", "publisherId": "innovayse", "tier": "AddOn",
              "entitlement": { "state": "Entitled", "until": null }, "revoked": false },
  "versions": [
    { "version": "1.2.0", "minPanelVersion": "1.3.0", "maxPanelVersion": null, "sizeBytes": 8123456,
      "sha256": "…", "publishedAt": "2026-10-01T00:00:00Z", "releaseNotes": "…", "revoked": false, "revokedReason": null }
  ],
  "manifest": {
    "publisher": "Innovayse",
    "publisherId": "innovayse",
    "tier": "AddOn",
    "minPanelVersion": "1.3.0",
    "schemaName": "mail",
    "dependencies": ["accounts"],
    "agentCapabilities": [ { "key": "Mail", "displayName": "Manage mailboxes on this server" } ],
    "permissions": [ { "key": "AdminOnly", "displayName": "Administrators only" } ]
  },
  "signedAt": "2026-11-09T09:00:00Z",
  "validUntil": "2026-11-10T09:00:00Z"
}
```

- `versions` lists every version compatible with the panel, newest first. A withdrawn version stays in the list with
  `revoked: true` and a localized `revokedReason`.
- `manifest` is the SERVER'S copy of the newest version's signed manifest summary. The panel shows it to the
  administrator before an install; it never shows text from the package itself. `agentCapabilities` and `permissions`
  carry the machine key and the localized name. `schemaName` is absent for a module with no tables.
- The `module.id` in the answer must equal the id requested: a signed answer about another module is refused.

### `POST /updates`

Request body (the only request body the panel sends):

```json
{ "channel": "stable", "installed": [ { "id": "mail", "version": "1.1.0", "sha256": "…" } ] }
```

Answer:

```json
{
  "items": [
    { "moduleId": "mail", "revoked": false, "revokedReason": null, "latest": { "version": "1.2.0", "…": "…" } }
  ],
  "signedAt": "2026-11-09T09:00:00Z",
  "validUntil": "2026-11-10T09:00:00Z"
}
```

One notice per installed module the service has something to say about: a newer compatible version (`latest`, same
shape as in the catalogue), a revocation of the installed version, or both. The check is made only when an
administrator asks for it; the panel never repeats it on a timer.

### `GET /keys`

```json
{
  "keys": [ { "id": "metadata-2027", "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n…\n-----END PUBLIC KEY-----\n" } ],
  "signedAt": "2026-11-09T09:00:00Z",
  "validUntil": "2026-11-10T09:00:00Z"
}
```

The list is the service's way to announce a new metadata key. It is trusted only if it is signed by a key the panel
already trusts (the pinned key, or one accepted earlier); a list signed only by the keys it announces proves nothing and
is refused whole. Every entry must be an ECDSA P-384 public key in a `PUBLIC KEY` PEM block, and at most eight keys are
accepted at a time. Accepted keys live in the panel's memory and are forgotten at restart; the pinned file is the
root of trust again afterwards, so a rotation is not complete until the new key is also shipped with a panel release.

### `POST /modules/{moduleId}/versions/{version}/download-grant`

No request body. Answer (`200` or `201`, signed):

```json
{
  "grantId": "g_8b1e…",
  "url": "https://api-marketplace-maran.innovayse.com/api/v1/downloads/g_8b1e…",
  "expiresAt": "2026-11-09T09:10:00Z",
  "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "sizeBytes": 8123456,
  "encrypted": false
}
```

The panel accepts the grant only if all of these hold, and otherwise downloads nothing:

- `url` is `https` on the SAME host and port as the configured base address (`Marketplace__BaseUrl`), without user
  information (`GrantHostRefused` otherwise; see "The grant address" below);
- `expiresAt` is in the future;
- `sizeBytes` equals the size the signed catalogue announced for that version, and is between 1 and the panel's
  `MaxPackageBytes` (never above 256 MiB, the package reader's unpacked total);
- `sha256` equals the digest the signed catalogue announced (compared as 32 bytes).

**The grant address.** The panel compares scheme, host and port only; the path is the service's. The service forms the
address from its own `Marketplace__PublicBaseUrl` and the grant's path (`/api/v1/downloads/{grantId}`), so:

- `Marketplace__PublicBaseUrl` on the service and `Marketplace__BaseUrl` on the panel must name the **same origin**
  (scheme `https`, same host, same port) in every environment. A mismatch is not detected until the first install,
  where the panel refuses the grant with `GrantHostRefused` and the administrator is told to compare the two settings
  (`docs/operations/marketplace.md`, "The download address must be on the same origin").
- `PublicBaseUrl` MUST be an origin: no path, user information, query or fragment. A path prefix would be dropped from
  the grant address, so the service refuses such a value at start-up rather than issue grants that cannot work.
  A service published under a path prefix on its host cannot satisfy this contract; serve it at the root.

`encrypted` says the package body is encrypted for this panel; decrypting it belongs to the loader.
The grant is single use. The service MUST bind it to (licence, server identity, module, version) and SHOULD refuse a
second use. The grant request is never retried by the panel, because a retry would consume a second grant.

### `GET /downloads/{grantId}`

The panel requests the grant's `url` with `Accept: application/octet-stream` and nothing else: no lease credential, no
panel version, no language. Up to three same-host `https` redirects are followed; any other redirect ends the download.
The answer must be `200`. If it carries `Content-Length`, that must equal the grant's `sizeBytes`. The panel streams the
body to a temporary file in its package directory, computing SHA-256 as it goes, stops at one byte more than
`sizeBytes`, and compares the digest with the signed catalogue's. The body carries no signature header: its integrity
rests on the signed digest, and its authenticity on the package's own signature, checked by the loader.
A download is never retried.

## Lease renewal

The lease that the credential above hands back is obtained here. The request is the one conversation in this contract
that the panel starts by itself: the Licensing module's background worker makes it about once a day, with no
administrator involved (`rules/security.md` item 10, fifth exception). The panel side is
`backend/src/Maran.Modules/Licensing/Services/LeaseServiceClient.cs` (the HTTP exchange, the only type that
reaches the network), `LeaseRenewer.cs` (what is accepted and written) and `LeaseRenewalWorker.cs` (the schedule). The host
is the same one as the rest of this page by default; an operator can point the lease renewal at another with
`Licensing__LeaseServiceBaseUrl`, independently of `Marketplace__BaseUrl`.

`POST /api/v1/lease`

```
POST /api/v1/lease HTTP/1.1
Host: api-marketplace-maran.innovayse.com
Accept: application/json
Content-Type: application/json; charset=utf-8

{"licence":"lic-001"}
```

- **The only thing sent is the licence id**, the `id` member of the licence the service itself issued. There is no
  `Authorization` header, no `X-Maran-Panel-Version`, no machine id, no host name, no account or customer data, no
  cookie. The service looks the licence up and, when the licence is bound to a server, binds the lease to the `server`
  it holds for that licence; the panel does not tell it.
- **Answer `200`, `application/json`,** at most 16 KiB (a lease is a few hundred bytes), the lease envelope exactly as the
  panel will store it:

```json
{
  "payload": {
    "kind": "lease",
    "licence": "lic-001",
    "issued": "2026-11-09T09:00:00Z",
    "validUntil": "2026-11-23T09:00:00Z",
    "server": "<machine id, only for a licence bound to a server>"
  },
  "signature": "<base64 of the 64-byte Ed25519 signature>"
}
```

  The signature is over the UTF-8 bytes of the `payload` member's JSON text **exactly as it is written in the answer**
  (the text between and including its braces), made with the **lease key**: the same key and the same bytes as the
  bearer credential above, so the panel can hand this lease back unchanged. It is NOT the `X-Maran-Signature` header of the
  metadata answers; this answer carries none and the panel ignores one.
- **Cadence the service can rely on.** About once a day per panel (22 to 24 hours after a stored lease, jittered), and
  only for a licence that is not an offline licence. After a failure the panel asks again 6 to 7 hours later. It does not
  retry within an attempt and does not follow a redirect. The service SHOULD answer the same licence idempotently; it
  may issue a new lease each time, and SHOULD set `issued` to the moment of issue and `validUntil` fourteen days later.

What the panel checks before it writes anything (`LeaseRenewer`; every check is a refusal that leaves the stored lease
untouched):

| Check | If it fails |
|---|---|
| Status is `200` and the body is at most 16 KiB | `408`, `429` and every `5xx` count as "unreachable"; every other status (a redirect included) counts as "refused"; a larger body is discarded |
| The envelope parses, `payload.kind` is `lease`, `licence`, `issued` and `validUntil` are present and `validUntil` is after `issued` | rejected |
| The Ed25519 signature verifies under the pinned lease key | rejected |
| `payload.licence` is the installed licence's id, and any `server` (the licence's, and the lease's) is this machine | rejected |
| `issued` is no more than five minutes in the future and no more than 24 hours in the past | rejected (an old lease replayed as a renewal is not a renewal) |
| `validUntil` is later than the end of the lease already standing | not written (a replay can never shorten cover) |
| The installed licence is still the one that was asked about when the file is written | dropped |

A failure is written to the panel's audit journal and logged by kind only; three failed attempts in a row raise an
operator alert. Errors are RFC 7807 documents like the rest of this page; the panel selects its behaviour from the
status alone and never shows `title` or `detail`. Suggested codes: `licence_unknown` (`404`), `licence_revoked` (`403`
or `410`: a revoked licence gets no new lease, which is how a stolen licence is cut off before it expires).

## Signed answers

Every `2xx` answer of `/catalogue`, `/modules/{id}`, `/updates`, `/keys` and the grant carries:

```
X-Maran-Signature: <base64 of a DER-encoded ECDSA P-384 / SHA-384 signature>
```

over the exact bytes of the response body. The panel reads the status first, then verifies the signature over the bytes,
and only then parses the JSON, so a forged body never reaches the parser. A missing, malformed (over 256 characters, not
base64) or non-verifying signature is `CatalogueUnsigned`.

The signing key is the **marketplace metadata key**. Its public half is shipped with the panel as
`installer/keys/marketplace-metadata.pub` (installed by the installer to the path in `Marketplace__MetadataKeyPath`,
default `/usr/local/maran/keys/marketplace-metadata.pub`); the file may hold several `PUBLIC KEY` blocks, all P-384, and
any other block makes the whole file unusable. The file is read at each verification, so installing the key later needs
no restart. When the file is absent the panel fails closed (`CatalogueKeyMissing`): it believes nothing and offers no
install.

Freshness. Catalogue, module, updates and key-list documents carry `signedAt` and `validUntil`. The panel treats a
document as stale (`CatalogueStale`) when `signedAt` is older than `CatalogueMaxAgeHours` (24 by default, at most 168),
when `signedAt` is more than five minutes in the future, or when `validUntil` has passed. The service SHOULD set
`signedAt` to the moment of signing and `validUntil` to no more than 24 hours later. The grant has no `signedAt`; its
`expiresAt` is its freshness.

Errors are not signed.

## Errors

A failure is an RFC 7807 `application/problem+json` document with a stable snake_case `code`:

```json
{ "type": "https://api-marketplace-maran.innovayse.com/problems/entitlement_missing", "title": "Not entitled", "status": 403,
  "detail": "Your licence does not include this module.", "code": "entitlement_missing" }
```

| `code` | Status | What the panel does |
|---|---|---|
| `entitlement_missing` | 403 | The licence does not cover the module; install is refused with the panel's own text (`EntitlementMissing`). |
| `lease_expired` | 401 | The lease is not valid (expired, absent, bound to another server than the licence, or the licence is revoked); the panel reports `LeaseRequired`. |
| `incompatible_panel` | 409 | No version fits this panel (`IncompatiblePanel`). |
| `revoked` | 410 | The version or module was withdrawn (`PackageRevoked`). |
| `not_found` | 404 | Unknown module or version (`ModuleNotInCatalogue`); on a package download, an unknown, expired or already used grant (`DownloadGrantUnusable`). |
| `licence_unknown` | 404 | The licence the lease names is no longer known to the service (`LicenceUnknown`). |
| `licence_revoked` | 403 | The licence was revoked (`LicenceRevoked`). |
| `licence_offline` | 409 | `POST /lease` only: the licence is an offline licence and takes no lease. The marketplace module never sees it; the Licensing module treats it as a refused renewal (it never sends a lease request for an offline licence, so this is a defensive code). |
| `rate_limited` | 429 | The server asks too often (`MarketplaceRateLimited`); send `Retry-After` in seconds. |
| `panel_version_invalid`, `channel_invalid`, `locale_invalid`, `search_too_long`, `cursor_invalid`, `module_id_invalid`, `version_invalid`, `licence_id_invalid`, `installed_list_too_long`, `installed_entry_invalid`, `request_parameter_invalid` | 400 | The request broke a bound this page states. The panel treats any `400` as a refused request (`MarketplaceRequestRefused`) and shows its own sentence. A correct panel never provokes them (the locale is clamped, the channel and ids are validated before sending), so seeing one means a panel and a service that disagree about a bound: a defect to report. |
| `authentication_required` | 401 | The service endpoints for publishers and administrators answer it; the panel never calls those. On a panel endpoint a `401` without a known code is read as `LeaseRequired`. |
| `unexpected_failure` | 500 | The service failed. Read by status: the marketplace is unreachable (`MarketplaceUnreachable`); a `GET` is retried. |
| any other code | any | Read by status (below). |

The panel selects its behaviour from `code`. When the code is absent or unknown it falls back to the status: `401`
means `LeaseRequired`, `404` means not found, `429` means rate limited, `408` and every `5xx` mean the marketplace is
unreachable, and any other status means the marketplace refused the request. A refused package download is read the same
way (its body is capped like a grant), except that there a `404` or `410` without a known code means the grant is no
longer usable. **The panel does not show the service's `title` or `detail`**: it
renders its own localized sentence for the code it selected, so a service sentence is for logs and humans reading the
raw response. (This differs from an earlier draft of the issue, which had the panel render `detail` as received.)

A `GET` that fails to connect, or answers `408`, `429` or a `5xx`, is retried up to two more times after a short growing
pause, or after the `Retry-After` the answer carried (seconds only, waited for at most 10 seconds); anything else is not
retried. Each attempt has a 30-second deadline (`RequestTimeoutSeconds`, at most 300).

## Limits the panel holds the service to

| Limit | Default | Ceiling the operator can set |
|---|---|---|
| Catalogue, module, updates and key-list body | 4 MiB | 16 MiB |
| Grant body | 16 KiB | 1 MiB |
| Package | the grant's `sizeBytes`, never above 256 MiB | 256 MiB |
| JSON nesting depth | 16 | fixed |
| Redirects followed | 3, `GET` only, same host over `https` | fixed |

A body over its limit is discarded. A `POST` is never redirected. An answer missing a required member, with a number
written as a string, or with an unknown enumeration word is refused as a whole; unknown extra members are ignored.

## Evolution

Within `v1` the contract only gains: new optional members, new endpoints, new `code` values (an unknown code falls back
to its status). Renaming or removing a member, changing a type or the meaning of a value, adding a word to
`entitlement.state`, or requiring a new request member is a breaking change and belongs in `/api/v2`. The panel's path
prefix is compiled into the module, so a new major version is a panel release.

No member carrying a host name, an account, a site or a user may be added to any request without an owner-approved
amendment of `rules/security.md` item 10.

## Key custody

Four keys exist, with four purposes, and none signs in another's role:

| Key | Scheme | Signs | Custody | Public half |
|---|---|---|---|---|
| Licence | Ed25519 | licences | Innovayse licence service | embedded in the panel |
| Release | ECDSA P-384 / SHA-384 | release manifests and artifacts | offline | `installer/keys/release-signing.pub` |
| Module signing | ECDSA P-384 / SHA-384 | each `.ipm` manifest | offline, never on a build host | `installer/keys/module-signing.pub` (supplied by the owner) |
| Marketplace metadata | ECDSA P-384 / SHA-384 | the answers on this page | the online service | `installer/keys/marketplace-metadata.pub` (supplied by the owner) |

The metadata key is hot by nature: it signs every response. It therefore MUST NOT be able to sign code; a package is
trusted only for a signature by the offline module-signing key, which the loader checks, so a stolen metadata key lets an
attacker lie about the catalogue (and the panel's checks limit that to what the digests and the package signature allow)
but not make the panel run code. The repository commits no key file; until the owner supplies
`marketplace-metadata.pub` no panel verifies against a production key.
