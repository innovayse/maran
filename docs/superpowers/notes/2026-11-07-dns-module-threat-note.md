# DNS module (issue #178): an authoritative name server on the panel host — threat note

Date: 2026-11-07
Surface: three at once, each of which `rules/security.md` escalates on its own —

1. **A new listening port** (item 10): TCP and UDP 53, reachable from the whole internet, and
   optionally a transfer path (AXFR/IXFR) to configured secondaries.
2. **A new privileged installer step and a new root-driven agent area**: a name-server package, a
   Maran-owned systemd unit, zone files and daemon configuration written by the root agent.
3. **A new tenant surface**: customers write data that a daemon publishes to the world under names,
   and one customer's data must never answer for another customer's name.

Scope of this note: the whole of issue #178 (phases S0-S8 of `research_notes/plan-178.md`). It is
written **before any DNS code exists**, which is the shape `rules/security.md` requires: a note
written afterwards is a justification validating a choice already made, and validates nothing.

## Second reviewer: OUTSTANDING (#331)

`rules/security.md` ("Sensitive change escalation") requires a second reviewer and an explicit threat
note for a change to the installer's privileged steps, to the agent, and for a new listening port.
**No second reviewer has read this change. The requirement is OUTSTANDING.** The author is an agent
session: it cannot dispatch a reviewer, and it cannot be one — the author of a privileged change is
the person least able to see what they assumed.

Therefore, in the exact shape the rule permits and names as a debt:

- This note is a file under `docs/superpowers/notes/`, written first.
- The change **may land on a branch. It MUST NOT merge to `main`** until a reviewer has read this note
  and checked every item under "What a reviewer must check". Unless the owner records, in writing and
  for one named pre-release, the beta exception (rules/security.md), in which case the release notes
  and that CHANGELOG section say the second review of the DNS surface is outstanding. A stable release
  is never covered.
- The owner is told at hand-off that this surface carries an outstanding review.

A note recording the debt is the minimum, never the discharge of it.

## Owner decisions this note ASSUMES (not yet approved by the owner)

Every answer below is the plan's recommended option. If the owner chooses differently, the sections
that depend on it must be rewritten before the code, not after. Nothing here is a decision.

| # | Assumed | Sections that depend on it |
|---|---|---|
| D1 | BIND 9 is the daemon (base repositories of both families; inline signing; response rate limiting) | all of 1-9 |
| D2 | `named` keeps its own zone list (`allow-new-zones`, `rndc addzone`/`delzone`); the agent renders no index file | 8 |
| D3 | The panel is never a secondary: transfer OUT only, no outbound zone pull | 10 |
| D4 | The panel generates DNSSEC keys (ECDSA P-256) and TSIG secrets, stored encrypted in the `dns` schema, installed by the agent | 7, 10 |
| D5 | Apply order: agent first, then commit; a reconciler heals divergence; the database wins | 6 |
| D6 | New plan dimension `MaxDnsZones`; the per-zone record cap is an operator option | 6 |
| D7 | Deleting a site keeps its zone; the account's deletion removes it | 3 |
| D8 | No public-suffix list in v1; customers create zones only for hostnames their own sites claim; only an administrator creates any other (non-single-label) zone | 3 |
| D9 | DNS screens are administrator-only plus the customer's own; a reseller sees nothing | 5 |
| D10 | EnableDns opens 53 tcp+udp in the firewall and DisableDns closes it, if the FTPS precedent does so (see "could not verify") | 1 |
| D11 | A new administrator setting holds the public IPv4/IPv6, nameserver hostnames, hostmaster mailbox, default TTL | 4 |
| D12 | The note stays OUTSTANDING; the branch does not merge to `main` | above |

## What is being added, in one paragraph

An authoritative-only DNS server (BIND 9, D1) run by a Maran-owned unit `maran-dns.service` against a
Maran-written configuration. A new C# module `Maran.Modules.Dns` owns zones and records in the `dns`
schema; a new agent service `DnsService` renders each zone to a file from the COMPLETE record set the
panel sends, validates it with the daemon's own zone checker, and reloads that one zone. A zone is
created with a site, and a customer edits records of zones their account owns. DNSSEC and zone
transfer are separate, later, individually switched features. Nothing exists yet: the tree holds only
the `AccountFeature.Dns` flag, four `planned(178)` navigation entries
(`frontend/src/router/navigationTree.ts`), `HostNameRule`, and a comment in `Ssl/Services/AcmeClient.cs`
saying DNS-01 needs a TXT-writing facility. There is no Mail module either, so DKIM/SPF/DMARC can only
be delivered as a seam.

## What an attacker could do with this surface, and why it is safe now

### 1. The port that arrives by surprise (item 10)

- The installer step installs the package, **masks the distribution's own unit first**, installs
  `maran-dns.service` disabled and stopped. No socket is bound during the install. Port 53 opens only
  when an administrator runs EnableDns. The firewall rule for 53 is created by that same action and
  removed by DisableDns (D10).
- Mask first, as for FTPS (`installer/lib/89-ftps.sh`): on the Debian family the package postinst
  starts the daemon with the distribution's own configuration, which may recurse, before our unit
  exists. The ordering is the control.
- New listening port means a **spec change first**, not a PR (item 10): the spec amendment (S0) lands
  before any of this code.

### 2. Open resolver and reflection amplification

- Authoritative-only: `recursion no`, `allow-recursion { none; }`, `allow-query-cache { none; }`, no
  forwarders, no cache. A name we are not authoritative for is REFUSED.
- Response rate limiting on, `minimal-responses yes`, `minimal-any yes`, EDNS buffer 1232,
  `version none`, `hostname none`, `server-id none`, `allow-update { none; }`, `allow-transfer { none; }`
  unless a secondary is configured.
- **Residual risk, stated:** an authoritative server still reflects some amplification for names it
  serves (large TXT, DNSKEY). RRL bounds it; it does not remove it. UNOBSERVED: the amplification
  factor, in particular with a signed zone.

### 3. Zone hijack: who may create a zone for which name

- A customer may create a zone only for an apex that one of their own account's sites already claims
  (primary domain or alias), through a read-only Sdk window to Sites. The site claim is already unique
  server-wide.
- The zone apex is unique server-wide, case-insensitively.
- **Parent/child overlap across accounts is refused in both directions.** If account A has
  `example.com`, account B cannot create `sub.example.com` (the daemon answers the most specific zone,
  so B would own a subtree of A's name), and the reverse. The same account may nest.
- Single-label names and names equal to a reserved host (`IReservedHostDirectory`) are refused.
- Rationale: the registrar delegation to this server is what makes a zone live. Whoever created the
  zone FIRST controls what a later, genuine owner who delegates to this server publishes. Binding
  creation to site ownership removes first-come-first-served for customers. **The administrator path
  (any non-single-label name no site claims) still has first-come-first-served and no public-suffix
  protection (D8): an administrator can create `com`-adjacent or a victim's name by mistake. That is
  accepted and stated, not solved.**
- Site delete keeps the zone (D7), so a released hostname can leave a live zone behind for a domain
  the customer no longer controls. Stated residue.

### 4. Record data is a config-injection surface (item 4)

- A zone file is a line-oriented structured file with directives (`$INCLUDE`, `$GENERATE`, `$ORIGIN`,
  `$TTL`) and `;` comments. Nothing a caller supplies is concatenated into it.
- The API accepts a CLOSED set of record types (A, AAAA, CNAME, MX, TXT, SRV, CAA, NS). Each rdata
  field has a validated agent-side type (`Ipv4Addr`, `Ipv6Addr`, a DNS-name type, numeric ranges, a CAA
  tag from an allow-list, a TXT body from an allow-listed alphabet). There is no raw record type.
- Owner names are validated labels (letters, digits, `-`, `_` for service labels, a leftmost `*`),
  63/253 limits, contained in the zone apex; an absolute name outside the zone is refused.
- TXT: printable ASCII 0x20-0x7E only, `"` and `\` refused (validated, not escaped). Rendered as quoted
  255-byte chunks. Newline, CR, NUL and any control character are refused at the API and again in the
  agent. Cost, stated: a legitimate TXT that needs `"` or `\` cannot be published.
- The agent renders from a template and then runs the daemon's own checker on the written file: a zone
  that does not load is never reloaded (render, swap, validate, reload; rules/rust.md).
- CNAME exclusivity, no CNAME at the apex, no customer-editable apex NS; the apex NS set and SOA are
  system-managed from the administrator's settings. Wildcards are allowed, bounded by the record cap.
- The SOA serial is computed in C# and sent; the agent never derives it. Rollover past 99 edits in a
  day falls back to `serial + 1`, which stays monotonic.

### 5. Tenant isolation

- `Zone` and `ZoneRecord` carry the account (records through the zone); `DnsDbContext` has the same
  fail-closed query filters as Accounts after #371: a reseller, an API key and an anonymous caller see
  nothing. A foreign zone answers **404, never 403**, with an IDOR test per endpoint. `TenantScopeTests`
  covers the new entities without an exemption.
- The navigation tree currently marks `dns-zones`, `dns-templates` and `dns-nameservers` as `MANAGERS`
  (administrator or reseller). Under D9 a reseller would see a menu item whose endpoint answers empty;
  the entries change to administrator-only. The SPA is advice only; enforcement is the module's.
- The agent never learns an account: it receives a validated zone name and a record set. Zone files are
  root-owned data under a Maran-owned directory, not under a customer home, so no customer path is
  involved and the `openat`/`O_NOFOLLOW` walk (item 2) does not apply. The zone file name is derived
  from the validated zone name by a type that cannot contain `/` or `..`.
- The unattended reconciler and the DNS-01 window run with no principal and therefore need deliberate
  `IgnoreQueryFilters` calls. Each is a counted, reasoned bypass and raises the count in
  `rules/README.md` (re-measure with the command given there; do not trust a number written here).

### 6. Resource exhaustion and divergence

- A cap on records per zone and zones per account (`MaxDnsZones`, D6), TTL bounds (60 s to 604800 s), a
  maximum rendered zone size, a checker run with a timeout, a disk-floor check before any write.
- Every edit re-renders the whole zone and reloads ONE zone, bounded by the cap and by a named
  rate-limit policy `dns`.
- Agent-first apply (D5) means that if the database save fails after the agent applied, the daemon
  serves NEWER data than the row for a time. The reconciler (start-up and periodic) pushes every zone
  whose applied serial differs; the database wins. For that window a customer can see a record resolve
  that the panel does not show. Stated.

### 7. Secrets

- TSIG keys and DNSSEC private keys are secrets. Generated by the panel (D4), encrypted at rest with
  `IEncryptionService` in the `dns` schema, written by the agent as root:<daemon group> 0640, never
  logged, never in an audit entry, never in a task failure. The wire fields use `SecretString`.
- If the owner picks the daemon-generated alternative, the keys exist only on disk, which violates
  "truth lives in PostgreSQL" and puts them outside the panel backup; this note would be rewritten.

### 8. The agent rpcs

- Closed, typed, idempotent, re-validated, audited (`mutating_rpcs.rs` / `read_only_rpcs.rs` census).
  No rpc takes a config fragment, a record in text form, or a path.
- Zone writes go through `safe_write` (the config-tree lock is a leaf). Under D2 named manages its own
  zone list, so no index file is rendered and no new waiting lock is added. If `addzone` persistence
  proves unusable, the fallback (a rendered `zones.conf` set under a new outer lock) breaks the proof
  in rules/rust.md that waiting locks are leaves, and is a new owner decision, not an implementation
  detail.
- Capability: a new `AgentCapability.Dns`; `AgentCapabilityGuard` refuses a module that reaches the
  DNS client without declaring it (item 13). Seams for Mail and Ssl are Sdk windows that hold no
  credential and never expose `IAgentDnsClient`.
- Every process spawn (`named-checkzone`, `named-checkconf`, `rndc`) is an argv array, an allow-listed
  absolute path from the distro adapter, with a timeout. No shell.

### 9. Confinement: AppArmor and SELinux

- Ubuntu confines `named` with an AppArmor profile; AlmaLinux and Rocky label `named` files with
  SELinux types. A Maran-owned zone directory outside the distribution's paths is DENIED by default on
  both. The distro adapter supplies the zone directory per family, chosen from a location the
  distribution already labels, and the installer adds the local override or context where needed.
- **UNOBSERVED:** whether `maran-dns.service` (our unit, our configuration) runs under the confined
  domain at all, and whether the confinement then permits reading our zone directory and writing the
  keys. If it runs unconfined, that is a weaker posture than the packaged unit and must be said so.
  The new unit also needs its row in `installer/systemd/unit-security-ceilings.txt`.

### 10. DNSSEC and transfer (off by default, later phases)

- DNSSEC: ECDSAP256SHA256 only; the DS record is shown to the customer to place at the registrar.
  **Disabling DNSSEC while the DS is still published at the registrar makes the zone fail validation
  for every validating resolver, which is an outage the customer causes to themselves.** The disable
  action keeps a DS-removal warning; it cannot detect the registrar.
- Transfer: outbound AXFR/IXFR only to configured secondary IPs, authenticated by TSIG and an IP ACL,
  `also-notify` to the same set. Secondary addresses and key names are validated types. The panel as a
  secondary is not in v1 (D3) because it would make the daemon an outbound client under item 10.
- A zone transfer discloses the entire zone. Anyone holding a TSIG secret can pull it; the secret's
  handling is section 7.

### 11. Seams for mail (#177) and ACME DNS-01 (#161)

- `IDnsRecordPublisher` and `IDnsChallengeRecords` are Sdk windows with the narrowest value that
  answers the question: the second writes a TXT at `_acme-challenge.<hostname>` only, in a zone the
  account owns, with short TTL and expiry. They are delivered in S5 and consumed by modules that do
  not exist yet (Mail) or are not changed here (Ssl). Neither is wired in this issue.
- DNS-01 against a zone nobody delegates to this server proves nothing; Ssl must check delegation and
  fail with a typed error rather than loop (#161's work, not this one's).

## What a reviewer must check

1. The packaged unit is masked, `maran-dns.service` is disabled and stopped after the installer, and no
   socket is bound before EnableDns (`ss -lntu | grep ':53 '` before and after; positive control: after
   Enable it IS listed; after Disable it is not, and the firewall rule is gone).
2. `named-checkconf -p` of the rendered configuration shows recursion off and RRL on; a real query for
   a foreign name is REFUSED.
3. Every zone rdata field is a validated type on the agent side. Try each of: newline, CR, `"`, `\`,
   `$INCLUDE`, `$GENERATE`, `;`, NUL, a 300-character label, `..`, `/` in a name. All refused, each by
   a named test, at the API and again at the agent.
4. Cross-account zone creation: the same apex; the parent of a foreign zone; a child of a foreign zone;
   an apex no site of the account claims. All a 404 or a typed conflict, never data.
5. IDOR on every endpoint; reseller, API key and anonymous callers get nothing; `TenantScopeTests`
   lists the new entities with no exemption.
6. No customer-supplied string reaches an argv except through a validated type; `rndc` is run by argv.
7. Secrets absent from logs, audit lines, task rows and error bodies. Mutation: log the TSIG key; a
   named test must fail.
8. AppArmor and SELinux results on both families (section 9), recorded in this note.
9. Mutation set (rules/testing.md): remove the recursion refusal; remove the overlap check; remove the
   CNAME-exclusivity check; remove a TXT character check; make Disable leave the firewall rule. Each
   must kill a NAMED test.
10. The apply-order divergence (section 6): kill the process between the agent call and the save; the
    reconciler must converge the daemon to the database.
11. The `IgnoreQueryFilters` count in `rules/README.md` matches the command's output after the change.
12. The expand-only migration (new schema) passes `maran migrate guard`.

## What the author could not verify

Nothing below was measured. This session read the plan and the tree; it ran no build, no test and no
daemon. Each cell is filled from the distribution's own documentation and a real-host run, never from
memory.

- **BIND per family.** Versions, package, binary, user, group and directory names on Ubuntu 22.04 and
  24.04, Debian 12 and 13, AlmaLinux and Rocky 9 and 10 (the architecture rules list PostgreSQL majors
  for several of these as unverified too).
- Whether `rndc addzone` persists identically on 9.16 (new-zone file) and 9.18+ (new-zone file or
  LMDB), which decides D2.
- Whether `dnssec-policy` exists on every supported build.
- AppArmor and SELinux behaviour (section 9).
- RRL defaults and the effective amplification factor, signed and unsigned.
- Whether FTPS leaves its firewall rule to the operator or creates it (D10). Not inspected; the plan
  says to inspect before coding and mirror it. `installer/lib/87-firewall.sh` only validates port
  numbers in what was read.
- Whether the existing `ISiteDirectory.FindByDomainAsync` covers aliases and can serve as the
  ownership window, or whether a new member is needed (the plan assumes a new
  `OwnsHostnameAsync`). Not read in full.
- Whether the Sites module can publish `SiteCreated`/`SiteDeleted` after commit without losing events;
  the plan accepts a lost message (the customer has a create-zone action).
- Behaviour when a hostname moves between accounts: the zone stays with the old account (D7). No
  story for account transfer (#127) or fleet placement (#180); both need follow-up issues.
- Whether a registrar glue/NS setup mistake by a customer can make this server answer for a name it
  should not; this is outside the panel's control and not testable here.

## Website docs needed (for the owner; agents do not touch the website)

DNS module overview (zones, records, per-type rules, TTL limits); nameserver setup (registrar glue and
NS delegation); DNSSEC (publishing and removing the DS record, with the outage warning); zone transfer
and secondary setup; DNS-01 and wildcard certificates once #161 lands; UI overview pages in en, ru, hy;
CHANGELOG entry.

## Open questions for the owner

D1-D12 above, in particular D1 (daemon), D3 (secondary role), D4 (key custody), D6 (plan dimension
widens Accounts, the reseller envelope and the plan form), D8 (public-suffix protection), D9 (reseller
visibility) and D10 (who opens port 53).
