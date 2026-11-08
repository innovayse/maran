# Moving an account between Maran servers (issue #127): threat note

`rules/security.md` requires a threat note, written first, for a change that touches the agent's
privileged paths, a new credential class and a new outbound call. It is written BEFORE the code: every
statement below is a requirement the code must meet. Where the code lands and differs, the code is wrong
or this note is amended in the same change.

**Second review: OUTSTANDING (#331) - must not merge to main.** The work is carried out by agent
sessions, which cannot obtain a second reviewer. The branch carrying this change MUST NOT merge to `main`
until a human reviewer has read it and checked every item under "What a reviewer must check"
(rules/security.md "Sensitive change escalation"). Nothing here discharges the review; a beta
pre-release does not discharge it either. The owner is told at hand-off that this surface carries an
outstanding review.

The design this note describes comes from `research_notes/plan-127-197.md`, written before any code, and
is checked here against the tree as of 2026-11-07 (HEAD `05e58e81`, branch `feature/146-425-security`).
**Every decision it takes from the plan's recommended option is listed under "Owner decisions this note
assumes" and none is approved yet.** If the owner chooses differently, this note is amended before the
code, not after.

## What is added

1. A signed migration package: a length-prefixed container holding a signed JSON panel manifest and the
   account's backup artifact. An administrator exports it on the source, downloads it as a file or lets
   another panel pull it.
2. Two agent rpcs on `BackupService`: `ExportBackup` (server-streaming, reads one published artifact out
   in bounded chunks) and `ImportBackup` (client-streaming, stages, verifies and publishes one artifact
   under the SOURCE backup id), plus a restricted dump loader used only for imported artifacts. This is
   a proto change and a `PROTO_VERSION` bump (20 in the tree today; the number is allocated at merge
   time, never reserved early).
3. A new module `Maran.Modules.Migration` (schema `migration`): signing key, export records, export
   grants, import records and jobs. New Sdk contracts `IAccountMigrationExporter` and
   `IAccountMigrationImporter`, implemented by Accounts, Sites, Databases, Cron, Sftp, Ftp and Ssl; an
   Sdk act-window for the artifact implemented by Backups.
4. A new authentication scheme `MigrationGrant` (in `Maran.Host/Authorization`) for ONE endpoint family
   on the source: `GET api/v1/migration-pull/...`, authenticated by a single-use bearer grant.
5. A new outbound HTTPS call from the receiving panel to an administrator-typed URL (named client and
   resilience pipeline), used only by the pull.
6. A job-caller primitive (SharedKernel and Host): a durable job re-establishes the starting
   administrator as the caller for each of its steps.
7. One installer nginx location with large-body and no-buffering settings, and the
   `MARAN_MIGRATION_*` variables in `.env.example` and `installer/panel.env.example`.

Not added: no daemon, no listening port, no second database client, no agent-side network client (the
panel relays the bytes; the root agent opens no outbound connection for this issue), no new key scheme
beyond the P-384 one rules/security.md item 9 already names.

Checked against the tree on 2026-11-07, none of this exists yet: there is no
`backend/src/Maran.Modules/Migration`, no `ExportBackup` or `ImportBackup` in `proto/agent/v1/backup.proto`
(the service has Create, Restore, List, Delete, Verify, ProbeDestination), no `IAccountMigration*` in the
Sdk and no `SuppressInvitationMail` on `AccountCreated`.

## Rules this change touches, and what they say today

- **rules/security.md item 10 (surface).** It lists one phone-home and one operator-triggered CLI fetch;
  anything else "requires a spec change first, not a PR". The pull is a third outbound call. The spec
  amendment and the proposed item 10 text are a gate that comes BEFORE code, and the rules edit is a
  draft for the owner's approval, never landed silently (rules/README.md: "Rules change by PR with the
  owner's approval").
- **rules/security.md item 12 (agent).** Two new rpcs must be closed, typed, idempotent, re-validated and
  audited. `ExportBackup` mutates nothing but customer data leaves the host; see decision D12.
- **rules/security.md item 13 (module reach).** The Migration manifest declares `Backup`, `System` and
  `Monitor` (from the plan; the reviewer checks the list is the minimum that works); the
  `AgentCapabilityGuard` refuses composition otherwise.
- **rules/architecture.md.** The module references only `Maran.Sdk` and `Maran.SharedKernel`; every
  cross-module need goes through the Sdk; the agent stays stateless (no DB) and the API spawns nothing.

## 1. What an attacker could do with this surface, and why it is safe now

### 1. The package is untrusted input on the receiving side

Whoever can place a file in front of the import (a stolen backup of the old panel, a package from a
compromised source, a man in the middle of an unpinned pull) controls every byte of it.

- **Header.** Magic, then a length-prefixed JSON header capped at 1 MiB, parsed with a strict reader:
  unknown schema version refused, duplicate keys refused, every string bounded. The artifact length is
  taken from the SIGNED header and the reader stops after exactly that many bytes; trailing bytes are an
  error.
- **Signature.** ECDSA P-384 / SHA-384 over the exact header bytes, verified BEFORE a byte of the
  artifact is forwarded to the agent. The header carries the artifact SHA-256; the agent hashes while it
  receives and refuses a mismatch. P-384/SHA-384 is already named in rules/security.md item 9 for release
  manifests; this is a third use of the same scheme with a third key (the per-server migration key),
  and a reviewer checks which key the code under review actually uses.
- **What a valid signature proves.** Only that the holder of the key signed it. It does NOT prove the
  sender is the server the operator thinks it is. The import therefore requires the operator to confirm
  the key fingerprint (shown in the dry-run report next to the source's name), and the pull pins it from
  the grant. This is trust on first use, stated as such: the confirmation is only as good as the channel
  the operator compared the fingerprint over.
- **Nothing in the package is executed, evaluated or rendered as HTML.** Names are validated by the same
  validators the ordinary create operations use (the importer dispatches the module's own commands, so
  it is not a second, weaker way to create things); the SPA shows them as text.

### 2. The archive and its dumps are authored by someone else (the finding that changes the restore)

This is the most important finding of the design. The existing restore treats the archive as the
agent's own output: it loads each dump with `host.load_dump` as the database superuser
(`agent/crates/ops/src/backup/backup_host.rs`, `process_backup_host.rs`). That is safe today for exactly
one reason: the dump is the panel's OWN output, and the panel hands the agent the digest it recorded.
For an imported artifact that premise is false. A hostile dump that says `CREATE DATABASE other_db; USE
other_db; DROP TABLE ...`, `GRANT ... ON *.*`, `SELECT ... INTO OUTFILE` or `INSTALL PLUGIN` would run
with full privilege. The `allowed_databases` check pins the NAMES the manifest may name; it does nothing
about the SQL text.

- **Home directory.** Extracted as the account uid by the existing path (`extract_home_as_account`): the
  member scan first (layout, `..`), bounds, symlink refusal. A hostile package can therefore write only
  what the customer could upload. This claim is inherited from the existing restore and not re-derived
  here; the reviewer confirms that the imported artifact reaches the SAME function with the SAME
  checks and not a parallel copy.
- **Databases.** Every dump of an IMPORTED artifact is loaded through a restricted loader. The agent
  creates a temporary user `maran_imp_<random>`@`localhost` holding ALL privileges on that one database
  only, loads the dump as that user, and drops the user on every path, including a killed client. A dump
  naming another database, a GRANT, INTO OUTFILE, INSTALL PLUGIN or SET GLOBAL then fails with "access
  denied" instead of succeeding. The names are still pinned to `allowed_databases`.
- **Marker.** Which artifacts take the restricted path is recorded in the sidecar as a new optional
  field (`imported: true`, additive; sidecar version unchanged). The marker is not trusted from the
  archive: it is written by the agent when it publishes the stage, and an artifact that carries no
  marker keeps the existing superuser path. A reviewer checks that a missing marker cannot be reached by
  an import (the import is the only writer of the sidecar for these artifacts, and the digest covers the
  manifest inside the archive).
- **Fidelity cost.** Definers of routines, triggers and events: the restricted user cannot create an
  object definer-ed as another user. A package whose dump cannot be loaded restricted is REFUSED with a
  typed error, never loaded with more privilege. This is requirement, not result; see "What the author
  could not verify" and decision D4.
- **Bounds.** The staged artifact is capped (`MigrationOptions.MaxPackageBytes`), the free-space floor
  applies before the first byte is written, the staging name is minted by the agent (never taken from
  the caller), and a half-written stage is removed on every path. The agent does not rewrite dump text:
  it is hashed, and a rewrite would destroy the digest check.
- **No rename.** A restore requires the embedded manifest to name the target account
  (`ManifestAccountMismatch` in `read_manifest.rs`) and the sidecar to agree with it
  (`require_sidecar_agrees.rs`); the manifest is covered by the artifact digest. So the account keeps its
  system user name and the artifact keeps its backup id. A package that would collide is reported as a
  conflict, never renamed (decision D1). This is a safety property: a rename would need dump rewriting,
  which breaks the digest and the premise above.

#### The restricted loader as built (agent, `ops::backup`)

Written after the code of the loader, from what it does. Spike S1 (a real dump loaded on MariaDB
10.5, 10.6 and 10.11) has NOT been run: nothing below is measured on a server.

- **Two halves, because neither is enough alone.** A pre-scan reads the dump's text before the
  server is involved (`scan_dump`), and the dump is then loaded by a temporary user holding every
  privilege on the one database and nothing else (`load_dump_restricted`).
- **The pre-scan is the only control against the database CLIENT's own commands.** The client runs
  as a root-started process and acts on a backslash command (`\!` hands a line to a shell) or a
  named one (`system`, `source`, `tee`) before any privilege is consulted. A backslash outside a
  quote is therefore refused wherever it stands, and so is any statement whose first word is not on
  a closed list. The client is also started with `--binary-mode`, which turns its commands off for
  piped input; that flag is NOT verified on every shipped client version, and the scan does not
  rely on it.
- **An allow-list, not a deny-list.** A statement is accepted only if it is a table or row
  statement of the kind a dump carries (`CREATE TABLE`, `DROP TABLE`, `INSERT`, `REPLACE`,
  `LOCK`/`UNLOCK TABLES`, `ALTER TABLE`, transaction control, `USE`/`CREATE DATABASE`/`DROP
  DATABASE` of the target only, and `SET` of a short closed list of session variables with no
  expression). Executable comments (`/*!...*/`, `/*M!...*/`) are read as code, since the server
  runs them; the one exception is a version no server reaches (the dump tools write
  `/*M!999999\- enable the sandbox mode */` first), which is read in a state that allows no quote,
  no `;` and no backslash but that one.
- **Fidelity cost, decision D4 pending.** Triggers, procedures, functions, events and views are
  REFUSED (`StoredProgram`), as is any `DEFINER` clause: each would be created owned by a user
  that no longer exists. Nothing is imported "without" them yet; whether that is wanted waits for
  the owner and for spike S1.
- **What the scan does not do.** It does not look for `other_db`.`table` inside ordinary
  statements; the temporary user's privileges do. It does not bound its own time (it reads a local
  file). A user left behind by an agent killed between creating and dropping it has the prefix
  `maran_imp_` and no reaper finds it yet.
- **The restore routes an imported artifact through it.** The sidecar's `imported` flag (the agent's
  own record, written by the import and never read out of the archive) is read by
  `require_sidecar_agrees` in the same pass that compares the sidecar with the manifest, and rides
  on the restore plan as `DumpOrigin`. For an imported artifact every dump of the plan is scanned in
  `prepare_restore`, right after the dumps are extracted and before the home is extracted or
  anything is dropped, so a refused dump is a restore that never began and the account is
  untouched. Step 6 then loads each dump through `load_dump_restricted` (which scans it once more
  from the descriptor it loads), never as the superuser. This holds for the whole and the
  single-database scopes alike; a directory-only restore loads no dump. The rollback dumps are
  this agent's own output and are the only dumps an imported restore still loads as the superuser.
  The panel's own artifacts are unchanged. Tests: `restore_backup_tests.rs` (the imported, refused,
  own and rollback cases against a recording host) and `replace_database_tests.rs`; what they do
  not exercise is a real server, which spike S1 still owes.
- **Two reviewer-added restrictions of the scan and the client.** A `/*M!...*/` comment is code to a
  MariaDB client and an ordinary comment to a MySQL one, so the scan lets it hold nothing on which
  the two readings part: no quote, no `;`, no backslash, no `#`, `-` or `/` (`Malformed`). Without
  that, a statement the scan reads as inert text could be run by the client.
  The client is started with `--default-character-set=utf8mb4`: the scan reads bytes, and a client
  left on a character set such as `sjis` would treat a byte of a multi-byte character as a backslash
  or quote that the scan did not see, ending a literal where the scan believes it continues.

### 3. The pull endpoint on the source

A new way for a remote caller to obtain an account's data.

- **Credential.** A single-use grant: 256 bits of randomness, only its digest stored (a pure digest
  helper in `SharedKernel/Utilities/Tokens/`, as `ApiKeyHasher`), shown once to the issuing
  administrator, valid for one account's package only, expiring within 60 minutes (default 30),
  optionally bound to the target's address (`CidrRule`). It travels in `Authorization: Bearer`, never in
  a URL or query string (rules/security.md item 8) and never in a log.
- **Consumption.** The manifest may be fetched up to 3 times (the dry-run preview); the artifact once.
  The artifact fetch consumes the grant at its first byte, so an interrupted pull needs a new grant. A
  replayed grant is refused and counted.
- **Brute force and enumeration.** Failures are counted per address by a limiter modelled on
  `ApiKeyFailureLimiter`; the endpoint has its own rate-limit policy. A refused grant answers one
  generic 404-shaped refusal whatever the reason (unknown, expired, used, wrong account): nothing about
  another tenant is confirmed (rules/security.md item 6, 404 never 403).
- **Reach.** The scheme reaches only the Migration module's package endpoints. Every other policy stays
  JWT-only, and the existing `ApiKey` policies stay exactly as they are (`ApiKeyPolicies.cs` is the
  only place that names the `ApiKey` scheme; the grant gets its own handler and policy beside it, built
  by analogy so a reviewer reads it against code that already passed review).
- **Residual.** Anyone who holds an unused, unexpired grant can read that one package. That is the point
  of the grant, and its blast radius is one account for under an hour.

### 4. The receiving panel makes an outbound request to a URL an administrator typed

- https only; certificate validation mandatory (no "ignore errors" switch); redirects not followed.
- The resolved address must not be loopback, link-local, unspecified, multicast or one of the panel's
  own addresses, checked on the CONNECTED socket address (a `SocketsHttpHandler` connect callback), not
  on the URL text, so a name that resolves to 127.0.0.1 is refused. **Private ranges ARE allowed** (a
  source on the same LAN is a normal case); that is stated, not hidden, and it means an administrator
  can make the panel send one authenticated GET to an internal https service. The request carries the
  grant only and the response is parsed as a package, so the primitive returns nothing a non-package
  service would reveal beyond "refused".
- Response bounded by the package header length; per-read and overall timeouts via the named pipeline.
- Only an administrator can start it; view-as is `Refused`; it is audited with the host (never the
  token). The API spawns nothing; the root agent opens no outbound connection.

### 5. The job-caller primitive is an impersonation facility

A durable job runs long after the request. It must not be a way to keep administrator power alive.

- A job step re-checks, on every step, that the starting user still exists, is an administrator and is
  not disabled; otherwise the job halts with `MigrationInitiatorGone`. The caller is built from the
  user row, never from a value carried in the message.
- Messages carry ids only. No token, password or key is ever in a Wolverine envelope (the envelope body
  is persisted; rules/csharp.md "Queue durability").
- The facility lives in SharedKernel and Host, registers no new `IgnoreQueryFilters` call, and a test
  proves a step run with no assigned caller sees nothing (with a positive control: with an assigned
  caller it sees rows).
- Today `ICurrentUser` has one production implementation, `HttpContextCurrentUser`
  (`Maran.Host/Security/`), plus `DemoSeedAwareCurrentUser` in the demo host and per-module design-time
  stubs. The reviewer checks that the new resolution (assigned caller, else the HTTP caller) cannot be
  entered from a request path, and that the demo host's variant does not inherit it.
- The unattended-work precedent is the fail-closed Accounts context (#371): a background step with no
  principal sees nothing. This primitive is the first thing that deliberately gives such a step a
  principal, which is why it is a decision (D13) and a reviewer item, not a detail.

### 6. What leaves the old server, and what never does

Leaves (all of it customer or panel data an administrator could already read): the home directory, one
SQL dump per database, account name, primary domain, owner e-mail and language, plan limits, site
hostnames and settings, cron entries, login names, SFTP public keys, certificate metadata (domains,
issuer, not-after).

Never leaves: any password or hash, any session, refresh token, API key or TOTP secret, any certificate
or SFTP private key, SMTP/DB/object-store credentials, the panel's own signing and encryption keys.

- Each module's exporter is an allow-list of named fields (the `IPersonalDataProvider` rule), so a new
  secret column on an entity is not exported by accident. A test per exporter plants a secret value in
  the entity and asserts it is absent from the serialized section, with a positive control (a field on
  the allow-list does appear).
- **Not secret-free by construction:** the home directory and the dumps are the customer's data, which
  can contain the customer's own passwords (a `wp-config.php`, a users table of hashes), and a cron
  command or environment value can hold a token. These travel as the customer's data and the UI says so
  (D8).
- **At rest.** The package is plaintext on the administrator's disk and in the agent's backup root until
  it expires (default 7 days); the UI says so (D11). Encrypting to the target's public key is a
  follow-up, not a claim.
- The owner e-mail held in `migration` rows is personal data and gets an `IPersonalDataProvider`
  (rules/testing.md Definition of Done).

### 7. Rollback is a deletion, so its failure modes matter

A failed import deletes the half-built account through `DeleteAccountCommand` with
`SkipFinalBackup = true` (nothing worth keeping exists yet). The cascade is the existing
`AccountDeleting` one and is checked by `IAccountResidueAuditor`. If the rollback itself fails, the import
record ends `RolledBackIncomplete`, names the account, and the administrator is told what remains.

- The rollback must never delete an account the import did not create: the job records the created
  account id and deletes only that id, and refuses if the account's `CreatedAt` predates the job.
- A name collision found only after the account row exists is the dangerous case: the dry-run is advice
  and the start re-checks, but the check and the create are two steps. The create is the authority; a
  collision at that step fails before anything is created and nothing is rolled back.
- Importers create only panel rows and agent configs that live outside the home; the restore afterwards
  supplies every byte inside it. A reviewer checks that site creation writes nothing the restore would
  then erase wrongly and that a second convergence pass is clean.

### 8. Tenant boundary

Export and import are administrator-only (D6). The package's account name is data, not an identity: the
target account id is minted on the target. A package can never name an existing account as its
destination. Customer and reseller callers get 404 on every migration endpoint (IDOR tests), an API key
has no policy that admits it, and view-as is `Refused`.

The `migration` entities carry no tenant `AccountId` in the sense `TenantScopeTests` walks. They are
administrator-only; the exemption is listed in that test with its reason, and the reviewer checks the
reason holds (an entity naming an account by id is an administrator's bookkeeping, never a customer's
row).

### 9. Invitation mail

An imported account is created with the invitation mail suppressed (an additive
`AccountCreated.SuppressInvitationMail`, D7), so a customer is not invited to a server that is not live.
The administrator issues the link at cut-over (`IssueInvitationLink`). The flag is a new knob on a
security-adjacent event: the reviewer checks it defaults to the existing behaviour and that nothing
but the Migration importer sets it.

## What a reviewer must check

1. A hostile dump cannot touch another database, grant privilege, write a file or load a plugin: the
   restricted-loader test exists, has a positive control (a clean dump loads) and was mutation-scored
   (loader run as superuser, the named test dies).
2. The package reader verifies the signature before any artifact byte reaches the agent, enforces the
   length from the signed header, and refuses trailing bytes; flipping one bit anywhere refuses.
3. No exporter returns a secret: the planted-secret tests exist per module and have a positive control.
4. The grant is single-use for the artifact, expires, is address-bound when asked, is stored as a
   digest, travels only in a header, and a refusal reveals nothing about which condition failed.
5. The outbound check is on the connected socket address, redirects are not followed, TLS validation
   cannot be switched off.
6. The job-caller is rebuilt from the user row on each step; a demoted or disabled initiator halts the
   job; a step with no assignment sees nothing.
7. Rollback deletes only the account the job created, refuses otherwise, and reports an incomplete
   rollback instead of claiming success.
8. `ExportBackup` and `ImportBackup` take the account backup lock without waiting, are bounded in memory
   and bytes, are classified in the audit lists, and state their cancellation class in their doc
   comments.
9. Every new endpoint declares its permission, view-as is `Refused`, customers and resellers get 404,
   an API key is refused, and each new error code is in the Sdk kind census.
10. The marker that routes an artifact to the restricted loader cannot be absent for an imported
    artifact, and an existing (panel-authored) artifact still takes the existing path unchanged.
11. The installer's nginx location is observed in the SERVED file (`nginx -T`), not the template, and
    does not widen the body limit for any other location.
12. The migration is expand-only (a new schema; `maran migrate guard` has nothing to drop), and the
    `IgnoreQueryFilters` count in rules/README.md is re-measured by the command that sentence names, not
    edited by hand.

## What the author could not verify

- **No code exists and nothing was run.** This note is written before the change, from the plan and a
  read of the current tree; no build, test or command that mutates anything was executed for it.
- That a restricted loader can load a real dump with stored routines and triggers on MariaDB 10.6
  (Ubuntu 22.04), 10.11 (Debian 12) and 10.5 (AlmaLinux 9). Spike S1 measures it. Until then the claim
  "a hostile dump cannot escape its database" is a requirement, not a result.
- Which database server versions the supported systems ship beyond those three (rules/architecture.md
  leaves several cells "not verified in this repository"). The loader is read against each family's
  documentation, not tested on the developer's machine.
- That `ImportBackup` bounds hold under a malicious client that streams slowly or never finishes.
- That nginx in front of the panel streams a multi-gigabyte body with buffering off on all three nginx
  versions the platforms ship (1.18, 1.20, 1.24); directives newer than 1.18 are avoided but this is
  not measured.
- The behaviour of a package larger than the free space: the floor is checked up front; a write that
  outgrows it mid-stream is bounded by `MaxPackageBytes`, not by the floor.
- Whether `ExportBackup` holding the account's backup lock for the length of a multi-gigabyte download
  blocks a scheduled backup in a way an operator will find surprising.
- That the shared hot-spot files (`contract-baseline.txt`, `PROTO_VERSION` and its `REVISIONS` row,
  `AgentProtoVersionWindow.cs`, the audit rpc lists) merge cleanly: the working tree at the time of
  writing carries uncommitted edits to several of them from `feature/146-425-security`, and #178 plans
  the same ones. The count in rules/README.md (35) differs from a fresh
  `grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l`, which printed 48 here on
  2026-11-07; the author did not establish which is current and the reviewer re-measures it.

## Owner decisions this note assumes (plan's recommended option; owner NOT yet approved)

| # | Assumed |
|---|---|
| D1 | The account system user name must be free on the target; a collision is a dry-run conflict, no rename. |
| D2 | Certificates travel as metadata only; ACME ones are re-issued after DNS moves, custom ones re-uploaded. |
| D3 | No password in the package. Database and login passwords are generated and shown once (or typed by the operator per DB user); SFTP/FTPS login handling (locked vs generated, D3b) is open. |
| D4 | Imported dumps load through a single-database temporary user; never with more privilege. Whether a package with routines imports with a warning or is refused waits for spike S1. |
| D5 | Per-server ECDSA P-384 / SHA-384 signature (BCL, no dependency) and mandatory fingerprint confirmation by the operator. |
| D6 | Administrators only in v1. |
| D7 | Invitation mail suppressed on import; the administrator issues the link at cut-over. |
| D8 | Cron commands and environment values are carried as customer data; D8b: cron entries import DISABLED. |
| D9 | The panel relays the bytes between agents; the root agent opens no outbound connection in this issue. |
| D10 | One `Migration` module for both #127 and #197. |
| D11 | Package plaintext at rest until it expires (default 7 days), with a UI warning. |
| D12 | `ExportBackup` is classified with the mutating rpcs in the agent audit lists (rules/rust.md "Logging" is amended in the same PR, with approval). |
| D13 | The job-caller primitive (an impersonation facility) is accepted in SharedKernel and Host with the constraints of threat 5. |

Also needing the owner before code: the proposed text of rules/security.md item 10 and the spec
amendment for the pull endpoint family and the outbound call; the one new `IgnoreQueryFilters`
(unattended reaping of expired migration artifacts and stale stages, in Backups) and its README entry.

## Open questions for the owner

1. Is moving an account in v1 scope for resellers' customers at all, or administrators only (D6)? An
   administrator who exports an account holds its owner's e-mail and data on their disk.
2. Is a package that cannot be loaded restricted (routines, triggers, events with definers) refused or
   imported with the objects dropped and a warning (D4)?
3. Does the owner accept private-range sources for the pull (threat 4), given the SSRF-shaped residue?
4. Naming: `docs/superpowers/notes/2026-11-07-account-transfer-threat-note.md` already means moving an
   account between RESELLERS (#369). Is "migration" the agreed word for between SERVERS everywhere
   (module, tasks, UI), so the two are never confused?

## Website docs needed (for the owner; agents do not touch website/ or GitLab)

- Operator guide "Move an account to another Maran server": export, download or pull token, dry-run
  report, import, DNS cut-over, suspending the old copy (en, ru, hy).
- A security page explaining what a package contains and what it never contains (no passwords, hashes,
  sessions, tokens or private keys; the customer's own files and dumps do travel), and that the package
  is plaintext at rest until it expires.
- CHANGELOG entry text for #127.


## Amendments made by the agent-side change (2026-11-07)

Where the code landed and differs from the text above, this is the record (rules/security.md: the note is
amended in the same change).

- **Service and file.** `ExportBackup` and `ImportBackup` are on a new `TransferService`
  (`proto/agent/v1/transfer.proto`), not on `BackupService`. They add a service, two rpcs and an
  `ImportRefusalReason` enum; no existing message changed.
- **The agent verifies as well as the panel.** The agent signs the export manifest (ECDSA P-384 / SHA-384,
  `aws-lc-rs`, already compiled in as the provider beneath `rustls`) with the per-server key the panel
  sends with the call, and `ImportBackup` itself checks, before it touches anything, that the key
  fingerprint equals the one the operator confirmed and that the signature verifies over the exact manifest
  bytes. The panel's own verification before forwarding stays a requirement; this is the second line. The
  private key travels per call on the local unix socket (like object-store credentials), is never logged
  and is not written to disk; the agent overwrites its copy of the bytes once parsed (best effort).
- **The manifest the agent signs** carries account, backup id, artifact size and SHA-256, database names,
  home bytes, creation time, agent version and the signer's fingerprint. The panel's richer package header
  (plan section 2.2) wraps or accompanies it; nothing in the agent depends on that header.
- **Sealed artifacts are not exportable** (the receiving host holds no key); the panel exports from an
  unsealed backup made for the purpose.
- **System user name (D1)** is checked by the agent on import (`getpwnam`), before anything is written;
  the same id with the same digest is a converged retry, another digest is refused.
- **Marker.** `imported: true` is written into the sidecar by `ImportBackup` after publication; the
  restricted loader keys on it. `ImportBackup` itself loads no SQL.
- **Open for a reviewer.** `ExportBackup` is in the mutating audit list as D12 says; if the owner approves
  the "disclosing" class of rules/rust.md draft 58b, it moves to that list.
