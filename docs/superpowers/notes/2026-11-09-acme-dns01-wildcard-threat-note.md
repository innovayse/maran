# Wildcard certificates through DNS-01 on the panel's own name server (issue #161) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation", issue #331).
This is a new cross-module WRITE path into an internet-facing DNS zone (the Ssl module publishes a record
into a zone the Dns module owns), a certificate with a larger blast radius than any the panel issued
before (a wildcard covers every name under a customer's domain), and a deliberate bypass of the Dns
module's tenant filter for unattended work. None of the three is on the listed surfaces by name, but each
is what the escalation rule exists for, so it is treated as sensitive. No agent session can dispatch a
reviewer.

The change may land on its branch and on `dev`; it **MUST NOT merge to `main`** until a human reviewer has
discharged this note, or the owner has recorded the beta exception for a named pre-release in writing
(rules/security.md "The one recorded exception"). The owner is told at hand-off that the DNS challenge
window and the wildcard issuance path carry an outstanding review.

**Honest ordering.** The design below was fixed by the plan before any code (`research_notes/plan-383-384-161-325-372.md`
section 4, options O-161-1 to O-161-7, the recommended ones). The note records the argument and the code
that implements it; where they ever disagree, the code ships and the note is wrong, and the reviewer adds
the difference under "Deviations found when the code landed" (none yet).

This note supersedes one sentence of `2026-11-07-dns-module-threat-note.md` section 11 ("Ssl must check
delegation"): see "No delegation pre-check" below.

## What is added, in one paragraph

A customer with a plan that sells DNS, a site, and a zone for the site's domain on this server can request
a certificate that covers the domain and `*.domain`. The Ssl module orders it from the ACME authority with
two identifiers, proves both over DNS-01 by publishing TXT records at `_acme-challenge.<domain>` through a
new Sdk window `IDnsChallengeRecords` (implemented by the Dns module), removes them again, and installs the
result through the existing agent rpc. Renewal repeats the same proof unattended. Nothing else changes:
HTTP-01 stays the only path for a plain certificate, third-party DNS providers are out of v1, there is no
proto change, no agent change and no new outbound call (the panel's call to the authority is the existing
ACME call; the authority queries the name server this host already runs).

## What changes, by file

1. `Maran.Sdk/Interfaces/IDnsChallengeRecords.cs`, `Contracts/DnsChallengeHandle.cs`,
   `Contracts/DnsChallengeAvailability.cs`: the window. Three members: `CheckAsync`, `PublishAsync`,
   `RemoveAsync`. No credential, no zone name in or out, no record type argument.
2. `Dns/Services/DnsChallengeRecords.cs` implements it over `DnsZoneEditor` (open, change through the zone's
   own methods, publish to the agent first, then save: the order every other zone change takes).
   `DnsZoneEditor.OpenForAccountAsync` is the new, second way in: it lifts the tenant filter and replaces it,
   on the same line, with `AccountId == <the account the caller names>`.
3. `Dns/Domain/Policies/ChallengeRecordPolicy.cs` is the law of the one record: `IsValue`, `IsHostname`,
   `ZoneCandidates`, `RecordNameIn`, `IsChallengeName`, `MaxValuesPerName`, `MaxAge`.
   `Zone.AddChallengeRecord`, `Zone.RemoveChallengeRecord` and `Zone.RemoveStaleChallengeRecords` are the
   only entity methods that touch a record of origin `AcmeChallenge`.
4. `Dns/Services/DnsChallengeSweeper.cs` (registered by the Host beside `DnsZoneReconciler`): hourly, removes
   challenge records older than `ChallengeRecordPolicy.MaxAge` (one hour).
5. Ssl: `IAcmeDnsChallengeWriter` / `AcmeDnsChallengeWriter` (the hook; wraps the window and words its
   refusals), `AcmeDnsChallengeResponder` (DNS-01), `AcmeHttpChallengeResponder` (HTTP-01, split out of
   `AcmeClient` with no behaviour change), `AcmeAuthorizationPoller`, `AcmeOrderFinalizer`,
   `WildcardAvailabilityCheck`, `DnsZoneChallenge`, `Certificate.IsWildcard` (additive column, default false),
   `IssueCertificateCommand.Wildcard`, `GET /api/v1/certificates/issuance-options`.
6. `rules/README.md`: the count of deliberate `IgnoreQueryFilters` bypasses, three more (see below).

## Assets

1. **The authority's trust in a name.** A wildcard certificate for `*.example.com` lets whoever holds its
   key impersonate every host under the domain. The proof of control is the TXT record, so whoever can
   write that record into the zone can obtain the certificate.
2. **A customer's zone** (what an internet resolver is told about their names). The window writes into it.
3. **The tenant boundary** between zones. One account's challenge must never land in, or remove anything
   from, another account's zone.
4. **The wildcard certificate's private key.** Generated per issuance by the panel, handed once to the
   agent install, never stored in the panel database. Unchanged by this work; its blast radius is what grew.

## What an attacker could do with this surface, and why it is safe now

### 1. Customer A publishes into customer B's zone (IDOR)
- The window takes the account as an ARGUMENT and finds the zone among that account's zones only
  (`FindZoneAsync`: `AccountId == accountId && candidates.Contains(Name)`). The Ssl caller passes the
  SITE's owner (`site.AccountId`, read through the tenant-scoped site directory, which answers null for a
  site the caller does not own), never an id from the request. A hostname inside another account's zone is
  therefore a hostname with no zone, answered `DnsChallengeZoneNotFound`, exactly as a customer's own query
  would answer.
- Removal takes a `DnsChallengeHandle(AccountId, ZoneId, RecordId)` and opens the zone through the same
  account-scoped path. A forged handle naming another account's zone finds no zone.
- Overlapping zones across accounts are refused when the zone is created (the Dns module's
  `ZoneOverlapPolicy`), so "the most specific zone that contains the name" is always one account's.

### 2. The window used to write something other than a challenge
- A value must be exactly forty-three characters of `[A-Za-z0-9_-]` (a base64url SHA-256, unpadded).
  Nothing else: not a SPF line, a DKIM key, a CNAME target, text with a quote or a newline. The check is
  `ChallengeRecordPolicy.IsValue`, run in the window AND again in the entity (`Zone.AddChallengeRecord`
  throws for a draft that is not a TXT at a challenge name holding a digest), so a second caller of the
  entity cannot bypass it.
- The record name is built by `RecordNameIn` from a hostname that `IsHostname` accepted (two or more labels,
  lower case, no `*`, no trailing dot, no underscore, so the challenge label cannot be embedded). The type
  is TXT and the TTL is the minimum (60 s); the origin is `AcmeChallenge`.
- Nothing a caller supplies is concatenated into a zone file: the record is a typed row, rendered by the
  agent from typed fields as for every other record (the Dns threat note, section 4).

### 3. A wildcard for a domain the account does not control
- The Ssl handler establishes site ownership FIRST through the tenant-scoped site directory (404 for
  another account's site or an unknown one, the same answer), then the plan (`AccountFeature.Dns` through
  `IAccountFeatureDirectory`), then that a zone of the account contains the domain
  (`WildcardAvailabilityCheck`). Only then is anything ordered.
- The authority is the final oracle: it queries this server's name server for the TXT records. A customer
  who creates a zone for a domain they do not control gets a zone that nobody delegates to this server, and
  the authority's resolver never reaches it, so the order fails. Zone creation itself is bound to the
  account's own sites (the Dns note, section 3), so the customer cannot create a zone for a domain no site of
  theirs claims.
- **Residual, stated:** if a customer legitimately controls a domain's delegation (they own it), they get a
  wildcard for it. That is the intended grant.

### 4. Stale challenge records
- Removal runs in a `finally` that ignores the caller's cancellation, on success, on a refused challenge, on
  a failed call and on cancellation. A removal that fails or throws is logged and never turns a successful
  validation into a failure (a valid authorization is worth more than a tidy zone).
- A crash between publishing and removing is the case `finally` cannot cover. `DnsChallengeSweeper` removes
  challenge records older than one hour, hourly and at start-up. It reads every account's zones with no
  principal (a counted, reasoned bypass) and touches records of origin `AcmeChallenge` only
  (`Zone.RemoveStaleChallengeRecords`), so a customer's own TXT at the same name is never read or removed.
- Customers cannot edit or delete these rows through the record endpoints: `Zone.ChangeRecord` and
  `RemoveRecord` refuse any origin other than `Customer` (`RecordNotEditable`), pinned by a test with its
  accepting control.

### 5. Zone bloat and the record cap (O-161-4)
- Challenge records are exempt from the zone's record cap, so a customer who filled the zone to the limit
  cannot make their certificate impossible to issue. They are bounded instead by
  `ChallengeRecordPolicy.MaxValuesPerName` (two values per name; a third is refused `DnsChallengeValuesFull`)
  and by the hourly sweep. The exemption means a zone can briefly hold the cap plus two records per
  challenge name; the cap exists to bound rendered size, and two records are inside the agent's own ceiling.

### 6. The zone lock held across the authority's validation (denial of service on the zone)
- Publishing and removing are separate calls; each takes the zone's lock for ONE write and releases it by
  commit (`DnsZoneEditor`). Nothing holds it while the authority validates, which can take minutes. A test
  plants a fake authority that blocks the validation, takes the zone's lock meanwhile, and requires it to be
  free; holding the lock across the validation fails that test.

### 7. The unattended caller
- Renewal has an anonymous caller, for whom the Dns tenant filter shows no zone. The window therefore lifts
  the filter in three places, each a counted, reasoned `RS0030` bypass that is replaced on the same line by
  an explicit scope: `DnsZoneEditor.OpenForAccountAsync` and `DnsChallengeRecords.FindZoneAsync` (both scoped
  by `AccountId == accountId`) and `DnsChallengeSweeper` (no scope by design: it sweeps for the host). The
  account id comes from the certificate's own row in the renewal handler. No controller reaches the window:
  the Ssl module consumes it through `IEnumerable<IDnsChallengeRecords>` in a constructor (service location
  would be a review reject), and the Dns module's controllers do not call it.
- `rules/README.md` states the count; it was re-measured with the command it carries, and each entry names
  the file and the issue.

### 8. CAA and delegation surprises
- A CAA record that does not allow wildcard issuance (`issuewild`) for the authority makes the order fail at
  the authority. The failure is worded by the problem TYPE it names (`dns`, `caa`, `unauthorized` become
  `AcmeDnsChallengeFailed`, whose sentence tells the customer to check delegation and `issuewild`). Only the
  type is logged; the authority's prose never leaves the module (rules/security.md item 8).
- **No delegation pre-check (O-161-5).** The panel makes no DNS lookup itself. A resolver in the panel or
  agent would be a new outbound call (item 10), and it could still disagree with the authority's resolver.
  This supersedes the earlier sentence in the Dns note ("Ssl must check delegation"): the authority is the
  oracle, and the typed error carries the hint.

### 9. Information leaked by the options query
- `GET /api/v1/certificates/issuance-options` is tenant-scoped through the site directory (a domain the
  caller does not host answers 404) and answers whether a wildcard is possible and, if not, a sentence. The
  reasons name the caller's own plan, the DNS module and the caller's own zone; none discloses another
  tenant. The SPA shows the sentence verbatim and holds no domain constants.

### 10. The rollback caveat (O-161-6)
- `Certificates."IsWildcard"` is additive (default false). The previous release ignores the column. If the
  code is rolled back, the old renewal re-orders a wildcard certificate as a SINGLE-NAME certificate, and
  aliases that relied on the wildcard silently lose TLS at the next renewal. This is accepted and stated in
  the CHANGELOG. Rollback is guaranteed one release back, and this column is not a contract-phase item.

## What this does not change

- The wildcard key's location: the agent's certificate store, outside every account's home, written by the
  agent as root. The agent does not check certificate names against the domain (`ops/ssl/certificate_state.rs`
  documents it), so a wildcard installs through the existing `InstallCertificate`.
- No proto change, no agent change, no new listening port, no new daemon, no new outbound call.
- The ACME account is still one registration for the whole panel, and the authority's rate limits still
  apply to it. A wildcard order is one order with two authorizations.

## What a reviewer must check

1. Foreign zone: with accounts A and B each holding a zone, `PublishAsync(A, "<name in B's zone>", value)`
   answers `DnsChallengeZoneNotFound` and B's zone is unchanged; `RemoveAsync` with a handle naming B's zone
   and A's account removes nothing. (Tests: `DnsChallengeRecordsTests`, with their accepting controls.)
2. Value shape: `"v=spf1 -all"`, 42 and 44 characters, a value with `=`, `"`, a newline are all refused;
   a 43-character base64url value is accepted.
3. Hostname shape: `*.example.com`, `example.com.`, `Example.com`, `_acme-challenge.example.com`,
   `example` are refused.
4. Origin guard: a customer record cannot be removed through the window (`RecordNotEditable`); a challenge
   record cannot be changed or removed through the record endpoints.
5. The lock is free between publish and remove (the planted-break test).
6. All authorizations of a wildcard order are answered; records are removed on success, on a refused
   challenge and on cancellation; an already-valid authorization publishes nothing.
7. Run the mutation set (below) and read each named test die.
8. `grep -rn "IDnsChallengeRecords" backend/src` shows the Sdk interface, the Dns implementation, the Dns
   module registration, and the Ssl writer's constructor, and nothing else.
9. `grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l` equals the number in
   `rules/README.md`.

## Mutation set (author's plan, scored against the whole suite)

Zone-ownership scope in `FindZoneAsync` (remove `AccountId == accountId`); the value-shape check in
`ChallengeRecordPolicy.IsValue`; the origin guard in `Zone.RemoveChallengeRecord`; removal in the
responder's `finally`; the lock release between publish and remove; the loop over every authorization. Each
must kill a named test. **Status: see "What the author could not verify".**

## What the author could not verify

- **A real authority.** No test ran against a public authority. The Pebble-based DNS-01 test exists and is
  opt-in (`MARAN_PEBBLE_TESTS=1`); see its own status in the pull request.
- **A real name server answering the authority.** The agent's own tests cover rendering and serving a zone;
  nothing here ran `dig` against the daemon for a record published by the window.
- **Resolver caching at a real authority.** Both values are published before either challenge is answered
  and each record uses the minimum TTL; whether a given authority's resolver honours that is its own.
- **Concurrent orders for two domains in one zone** were reasoned about (the zone lock serialises each
  write; the two-values-per-NAME bound is per name, not per zone) and covered by unit tests, not by a
  load run.
- **Mutation scoring** is recorded in the pull request when it ran, not here.

## Deviations found when the code landed

None yet. The reviewer adds them here.

## Second review: OUTSTANDING (#331)

A reviewer must read items 1 to 9 above against the code, re-derive "why it is safe" for sections 1, 2 and 7
rather than accept them, and decide whether `OpenForAccountAsync` should be named for the unscoped lift the
way `ISiteDirectory.FindByIdUnscopedAsync` is.
