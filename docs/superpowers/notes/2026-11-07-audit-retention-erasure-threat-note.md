# Threat note: audit export, retention and tamper evidence (#145), personal-data export and erasure (#146)

`rules/security.md` ("Sensitive change escalation") requires a threat note, written first, and a
second reviewer for a change to the security record and to what the panel discloses about people.
This change does four things at once to the audit journal (`identity.AuditEvents`): it lets an
administrator **delete** it on a schedule (retention), **rewrite** parts of it (erasure), **read all
of it out of the panel** (export), and it adds a **keyed hash chain** meant to show that it was not
altered. Each of the four weakens or relies on another, which is why they share one note. This note is
written BEFORE the code: every statement below is a requirement the code must meet, not a description
of code that exists. Where the code lands and differs, the code is wrong or this note is amended in
the same change.

The plan this note answers is `research_notes/plan-145-146-audit.md` (untracked, not committed). Where
this note adds a requirement the plan does not state, it says so, and section 10 collects them.

**Second review: OUTSTANDING — must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch `feature/145-146-audit` MUST NOT merge to `main`
until a human reviewer has read the surfaces listed in section 11 (rules/security.md "Sensitive change
escalation"). The owner is told at hand-off that this change carries an outstanding review. Nothing
here discharges it: a threat note written by the author closes nothing, it only lets the reviewer read
an argument instead of a diff.

## Owner decisions to confirm

Two decisions are taken here as defaults so work can start. They stay flagged until the owner confirms
or changes them, and each is a one-line constant, so changing one later costs nothing in code.

**B1 — retention is OFF by default.** `AuditRetentionPolicy.RetentionDays` is `null`, meaning keep
forever. An operator opts in with a value from 30 to 3650 days. Reasons: the journal has been
documented as never deleted, and an upgrade that silently starts destroying security records is a
surprise nobody asked for; and it keeps the retention attack (section 2) dormant until someone turns
it on. The cost, stated so it is not discovered later: with the default, personal data in the journal
(addresses, user agents, usernames in subjects) of every LIVE user is kept indefinitely, and so are
the pseudonymised rows of erased people and the account name in the tombstone (B2). An operator who is
bound by a storage-limitation rule must opt in. The CHANGELOG names the default so it can be changed.

**B2 — the `AccountDeleted` tombstone keeps the account name.** The final `AccountDeleted` row keeps
the account name as its subject and carries NO `AccountId`. The alternative is to erase the name too and
keep only "an account was deleted by X at T". Reason for the default: the row is the record that an
administrator destroyed an account, and without the name nobody can answer "which account, and was it
the one the customer is asking about". Cost: the name is an identifier that may point at a person or a
company, and it cannot be erased on request; it leaves the journal only when retention removes the row
(never, under B1's default). It is an owner and privacy-policy choice, not an engineering one.

## Decisions taken (owner's defaults, recorded)

- **B3 — this change is treated as sensitive.** By analogy to rules/security.md: it irreversibly
  deletes the append-only security record, rewrites rows, and exposes personal data over an API. Hence
  this note, the OUTSTANDING review, and the unmergeable branch.
- **B4 — the chain key is derived by HKDF-SHA256 from the panel's existing encryption key**
  (`Security__EncryptionKey`), purpose label `maran/audit-chain/v1`. No new secret, no installer or
  `panel.env` change. The argument for it, and the rotation consequence, are in section 3.
- **B5 — administrator-initiated entries of the nine other journals carry no account.** The account
  filter, the account export and the account erasure cover customer-session entries, viewed-as entries
  and Accounts-module entries only. Threading the account through the Sites, Databases, Ftp, Sftp, Cron,
  Backups, Ssl, Firewall and Monitoring journals is a follow-up issue. The consequence is a coverage
  limit that the operator documentation must state in plain words: an account's erasure does not reach
  an administrator-initiated entry whose subject does not name the account (section 4).

## What is added

1. In Identity (which owns `identity.AuditEvents`; there is no Audit module): new nullable columns on
   `AuditEvents` (`AccountId`, `ChainIndex`, `RowHash`, `ErasableCommitment`, `CommitmentSalt`,
   `ErasedAt`), two singleton tables (`AuditRetentionPolicies`, `AuditChainAnchors`), one expand-only
   migration.
2. Endpoints, all `AdminOnly`: journal export, retention read/write, chain status and verification,
   and the personal-data export of an account or a user.
3. One hosted service (`AuditMaintenanceScheduler`, hourly) that seals the chain, and once a day runs
   retention, the orphan-actor sweep and chain verification.
4. `AuditPseudonymiser`, called inside the account cascade and `RemoveSubUser`, and by the sweep.
5. `IPersonalDataProvider` (Sdk) implemented by each module that holds an account's rows.
6. A rate-limit policy `audit-export`, and `CsvField` and `HkdfSubKeyDeriver` helpers.

Not added: no daemon, port, rpc or proto change, no outbound call, no new package, no `IgnoreQueryFilters`
(the count in rules/README.md stays 35; re-measure with the command given there), no change to the
agent, no new secret. The agent never sees any of this.

## 1. What the journal is, and what it is not

The journal is a PostgreSQL table the panel's own database role can `INSERT`, `UPDATE` and `DELETE`
(it must: retention and erasure are those operations). It is **not append-only at the database level
and this change does not make it so.** The only tamper evidence is the chain (section 3), and the chain
has stated limits. Anyone who can write to the database as the panel's role can change or delete rows;
the question this note answers is what they can do **undetected**, and what an administrator can do
**without any database access at all**, through the new endpoints.

Who holds what, for the reading of every section below:

| Attacker | Holds | Can reach |
|---|---|---|
| Stolen or abusive administrator session | the administrator's token | every new endpoint, nothing below the API |
| Database writer without the process | rows in `identity.*` (SQL injection, a leaked connection string, a dump) | the table, not `panel.env` |
| Compromised `maran-api` process | the process, `panel.env` (root:maran 0640), the key | everything above, plus the chain key |
| Root on the host | everything | everything; nothing here defends against it |

An independent trail of what the root agent did exists outside the panel database (the agent's own
audit lines, issue #106, a separate branch and a separate note). That trail, and an off-box copy of an
export, are the only records that survive a rewrite of this table. Say so in the operator
documentation: a retention window is not a backup.

## 2. The retention attack

**Attack.** An administrator, or a stolen administrator session, sets retention to 30 days to destroy
evidence of their own earlier actions. The next daily run deletes everything older than 30 days. Under
B1's default the attack is two steps (enable at 30, wait for the run); it is the same attack whether
retention was off or at 3650.

**What is true and cannot be fixed.** An administrator may change retention by definition, and a purge
of an old prefix cannot be shown to have been legitimate after the fact. This note does not claim to
prevent it. It claims to make it loud, bounded and attributable, and it states the limit.

**Mitigations (requirements):**

1. **The change is journalled.** `AuditRetentionChanged`, with the old and the new value in the subject
   (`null` is spelled as a fixed word, never an empty string), the actor, the address and the user
   agent, written by `IdentityAuditJournal` and nowhere else.
2. **Every run is journalled, including a run that deleted nothing.** `AuditRetentionRun`, subject
   `retentionDays=… cutoff=… purged=N throughChainIndex=M`. A purge therefore leaves a row stating how
   much was destroyed and through which index. The row is written whether the run purged 0 or 100000,
   so "no row" means "no run", and a run's absence is itself visible.
3. **Floor of 30 days, ceiling of 3650**, enforced in the validator (`AuditRetentionOutOfRange`) and
   again in the entity (`AuditRetentionPolicy.Replace` refuses a value outside the bounds, because a
   validator is bypassed by a command dispatched on the bus). `0`, negatives and values below 30 are
   refused: there is no way to say "delete everything now".
4. **Only a sealed prefix is purged**, and only the prefix `ChainIndex <= M`, where M is one less than
   the smallest `ChainIndex` among rows still inside the window. A row that is out of window but has a
   higher index (it committed late) is kept: the rule errs towards keeping. Unsealed rows are never
   purged, and a run seals before it purges.
5. **The anchor advances in the same transaction as the delete, before it**, per batch of 1,000. A
   crash leaves either the old state or the new one, never rows deleted with the anchor behind
   (which verification would read as a break) or the anchor ahead of rows still present.
6. **The change is not retroactive past the floor and not instant**: the run is daily, so a shortened
   window takes effect at the next daily run, not at the request. That gives the other administrators
   up to a day, and the `AuditRetentionChanged` row itself is recent and survives the purge it
   enables for at least the new retention period.

**Stated limits (requirements on the documentation):**

- An administrator who shortens retention, waits, and lets the rows that record the shortening age out
  (30 days at the floor) leaves no trace in this table. The window in which the act is visible is the
  new retention period itself. An operator who needs more must keep an off-box copy of exports.
- Raising retention restores nothing. Purged rows are gone; the anchor records that a prefix was
  removed, not what it said.
- A database writer needs no endpoint and no policy: they can delete a prefix and advance the anchor
  with values read from the surviving rows (section 3). The retention run's journalling does not
  constrain them.
- **Not in the plan, for the reviewer to decide:** an operator alert on `AuditRetentionChanged` through
  `OperatorAlertMailer` (the one sanctioned path, rules/csharp.md), and a delay between saving a
  shortened window and its first run. Both would raise the cost of the attack; neither is specified
  here. If neither is added, the documentation must say that a shortened window is silent to everyone
  who does not read the journal.

## 3. The chain

**Construction (as planned).** `RowHash = HMAC-SHA256(key, "maran-audit-v1" ‖ previousRowHash ‖
canon(I) ‖ ErasableCommitment)`. `I` is the immutable set (`Id`, `OccurredAt` truncated to microseconds,
`Action`, `Succeeded`, `CorrelationId`); `E` is the erasable set (actor id and name, subject, address,
user agent, account id, impersonator id and name). `ErasableCommitment = SHA-256(salt ‖ canon(E))`
with a random 16-byte per-row salt. Every field is length-prefixed (4-byte big-endian length, UTF-8,
null as `0xFFFFFFFF`), so two different field splits cannot produce the same bytes. Rows are sealed
asynchronously, in `(OccurredAt, Id)` order, receiving the next `ChainIndex`; a unique index on
`ChainIndex` makes a double seal fail. Erasure replaces `E`, sets `ErasedAt` and nulls the salt
(cryptographic shredding); `ErasableCommitment` and `RowHash` stay. Primitives are BCL (`HMACSHA256`,
`HKDF`, `SHA256`), compared with `CryptographicOperations.FixedTimeEquals`: no home-grown crypto, no
MD5 or SHA-1 (rules/security.md item 9).

### What the chain detects

Against an attacker who can write to the table but does not hold the key:

- a changed immutable field (`Action`, `Succeeded`, `OccurredAt`, `CorrelationId`, `Id`) of a sealed row;
- a changed erasable field of a sealed row **that still has its salt**;
- a **deleted row in the middle** of the sealed range (a gap in `ChainIndex`, and a successor whose
  previous hash no longer matches);
- **reordered** sealed rows;
- a row **inserted after sealing** (no valid hash without the key).

### What the chain does NOT detect

1. **A rewrite of the erasable fields of any row, by anyone who can write the table.** This is the
   most important limit and the plan does not state it. A legitimate erasure and a malicious rewrite
   look identical to the verifier: set `ErasedAt`, null the salt, change `E`; the verifier skips the
   commitment of an erased row by design and `ErasedAt` is not covered by any hash. So **who** did a
   thing, **from where**, and **to what** (actor, address, user agent, subject, account) are NOT
   tamper-evident. What the chain protects is that a row with this action, outcome and time exists in
   this order. The operator documentation must say exactly that, and must not say "the journal is
   tamper-proof". **Not in the plan, for the reviewer to decide:** closing it needs the erasure itself
   to be authenticated by the key (for example an erasure mark `HMAC(key, rowHash ‖ "erased" ‖ ErasedAt)`
   verified for every row with `ErasedAt` set). That is a small addition and it would restore the
   guarantee against a keyless writer; it is not in the plan and not assumed here.
2. **Deletion at the tail.** Removing the newest sealed rows leaves a shorter, valid chain: there is no
   externally held head. The status endpoint's `LastChainIndex` and an off-box export are the only
   cross-checks.
3. **Deletion of the oldest rows (the purged prefix).** A writer deletes a prefix and sets the anchor to
   the `RowHash` of the last deleted row, a value they read from the table. The anchor is not keyed.
   Verification starts from the anchor and cannot tell a retention run from this. (Keying the anchor
   would move this from "undetected by a database writer" to "detected"; the plan does not.)
4. **Anything in the unsealed window** (below).
5. **Truth of time.** `OccurredAt` comes from `IClock` on the host; there is no external timestamp. The
   chain shows order of sealing, not that the clock was right.
6. **A holder of the key.** A compromised `maran-api` process or root reads the key from the process or
   from `panel.env` and can rewrite the whole chain consistently. The chain says nothing against them,
   and this note does not pretend it does.
7. **That the sealed rows were genuine when sealed.** Sealing freezes what is in the table. A row
   forged before it was sealed is sealed as genuine. For rows that existed before this release (no
   backfill, sealed on the first ticks after upgrade) the chain attests nothing about their past: it
   starts the clock at the moment they are sealed.

### The unsealed window

Sealing is asynchronous so the audit write path takes no lock and stays untouched. In steady state a row
is unsealed for up to one hour (the hourly tick, up to 20 batches of 500). **After the upgrade** the
backlog of pre-existing rows is sealed at no more than 10,000 per tick, so a journal of N legacy rows
is partly unsealed for about N/10,000 hours. In that window a row can be deleted, altered or forged
with nothing to compare against. The status endpoint reports `UnsealedRows` so the window is visible,
and the documentation states both numbers. The window is a deliberate trade for not locking writes; it
is not a defect to be hidden.

### Verification

Walks rows in `ChainIndex` order from the anchor in keyset batches. Requirements: a row with a salt
must reproduce its commitment from the current `E`; a row with `ErasedAt` and no salt skips that check;
**a row with neither is a break** (`SaltMissing`); the row hash and the absence of index gaps must
match. `POST api/v1/audit/chain/verification` and the daily run both journal `AuditChainVerified`,
`Succeeded = false` on a break, and the daily run logs at Error. A break is **not** mailed to anyone
in this change: it is silent to everyone who does not read the journal or the log. The documentation
says so. Cost: a verification reads every row, so the manual endpoint is rate limited (policy `api`)
and two verifications must not run at once on one panel (single-flight; **a requirement the plan does
not state**), otherwise an administrator can use it to hold the database busy.

### The key: HKDF from the panel key, and the rotation consequence

`key = HKDF-SHA256(IKM = the 32-byte panel encryption key, info = "maran/audit-chain/v1")`. The same
IKM feeds the AES-GCM service; domain separation by the info label is what makes that sound, and
`HkdfSubKeyDeriver` must have a test that two purposes give two keys.

**Why derive it and not store a separate secret (argued, as B4 asks).** The adversary the chain
exists for is the database writer who does not hold the process (SQL injection, a leaked database
credential, a dump). A separate `Audit__ChainKey` in `panel.env` would sit in the same file, with the
same owner and mode (root:maran 0640), readable by exactly the same process as the encryption key.
Against a process or host compromise it adds no confidentiality; against a database writer the
derived key is already out of reach. What a separate secret would buy is independent rotation, and it
would cost an installer change, three `.env.example` files, an upgrade path for existing installs, and
one more secret that a restore must carry. Panel backup and restore already carry the encryption key
(`installer/cli/lib/panel-restore.sh` restores `Security__EncryptionKey`), so a restored panel keeps
its chain verifiable. The derivation wins on every axis that matters for this threat and loses only on
rotation.

**Rotation consequence (the cost, stated).** If an operator rotates `Security__EncryptionKey`, every
sealed row's hash was made under the old key: verification reports a break at the first sealed row, and
**the verdict is the same as for tampering**. Nothing in this change can tell a rotation from an attack
(there is no key version on the row). No re-seal tool is in scope; after a rotation the chain is
re-based by the operator accepting the break. Requirements: the documentation says this at the place
where rotation is described, the `AuditChainVerified` subject names the first broken index so a total
break at index 1 reads as "key changed" to a person, and a rotation must be preceded by an export. I
searched for an existing rotation procedure and found none (command in section 12), so the
documentation has nowhere to inherit it from. **Not in the plan:** a key-version column. If the owner
wants rotation to be distinguishable from tampering, that is the field to add, and it must be added
now, since it is an expand-only migration only before the first row is sealed.

## 4. Erasure (#146)

**What it is.** Pseudonymisation of the erasable columns, in the same database transaction as the
deletion that causes it, not deletion of rows. The aim is that after a person or an account is
deleted, the journal no longer says who they were, while it still says what happened, when, and which
administrator did it.

### Who may erase, and by what path

There is **no endpoint that takes an id and erases it.** No `DELETE`, no `POST …/erase`, no manual
erase button, no API-key scope. Erasure happens only on three paths, each caused by something that
already needs its own authority:

1. the `AccountDeleting` cascade (Identity's `AccountDeletingHandler`), when an account is deleted;
2. `RemoveSubUser`, when the login row itself is deleted;
3. the **orphan-actor sweep** in the daily maintenance run (a system actor, no request).

Erasure adds no authority: whoever may delete an account or a sub-user could already do so, and the
audit consequence follows. The cost of this design is that the three triggers are the attack surface,
and two findings follow from reading the tree:

- **A third path deletes users and the plan names two.** `ResellerDeletingHandler` also removes a login
  (`_dbContext.Users.Remove(login)`). Either it calls the pseudonymiser in its transaction like the
  other two, or the person's rows stay readable until the next sweep (up to a day). **Requirement: wire
  it**, so the in-transaction guarantee is true for every path that deletes a login. Found with
  `grep -rnE "Users\.Remove|\.Remove\(user" backend/src/Maran.Modules/Identity --include=*.cs`, which
  listed `RemoveSubUser`, `ResellerDeletingHandler` and `AccountDeletingHandler` (`RemoveRange`); a
  deletion path outside Identity is not covered by that search.
- **The sweep is the most dangerous erase path, because its input is database state.** It pseudonymises
  rows whose `ActorUserId` is neither null, `Guid.Empty` nor an existing user. Anyone who can delete a
  `Users` row (an owner removing a sub-user, a database writer) causes erasure of that actor within a
  day, including the row of an **administrator** if one is ever deleted by a path or a writer, so
  "admin accountability is not erased" holds only while no administrator row disappears. And a bug that
  makes the user lookup see nothing (an empty table after a restore, a filtered context) would
  pseudonymise the whole journal's actors in one run, irreversibly. **Requirements (not in the plan):**
  (a) the sweep refuses to run when the user lookup returns no rows at all; (b) it caps the distinct
  actors it will erase in one run and, above the cap, erases nothing and journals a failed
  `PersonalDataErased` row naming the count; (c) a test runs the sweep with an anonymous caller (the
  maintenance job has none), with a positive control that a genuinely deleted user is erased and an
  inverse control that a live user, `Guid.Empty` and null actors are not. I searched Identity for a
  query filter on the user set (`grep -rn "HasQueryFilter" backend/src/Maran.Modules/Identity
  --include=*.cs`, no hits) so the anonymous-caller risk looks absent today; the test keeps it absent.

### What is replaced and what is kept

- **User** (the person's rows: `ActorUserId = user`, or `ImpersonatorUserId = user`, or `Subject` EQUAL
  to the user's stored username or e-mail, case-insensitively). Replaced: `ActorUserId` by one fresh
  random GUID shared by that user's rows; `ActorUsername` by `erased-user-<8 hex>`; `IpAddress` and
  `UserAgent` by empty; the impersonator name when the impersonator is that user; a matched `Subject` by
  the same text. Kept: `OccurredAt`, `Action`, `Succeeded`, `CorrelationId`, `AccountId`, and any
  `Subject` that was not a match.
- **Account** (rows with `AccountId = account`). Replaced: `Subject` by `erased-account-<8 hex>` and
  `AccountId` by a fresh random GUID. Kept: the actor columns of live users. An administrator's
  accountability for what they did to a deleted account is deliberately not erased.
- **Tombstone (B2, to confirm).** The `AccountDeleted` row is built by the deletion handler after the
  cascade, with the account name as its subject and no `AccountId`. Requirement: the delete flow never
  passes the account id to the journal for the tombstone or for a failure entry written after the
  cascade, so neither can carry a live account id (the plan places this in `AccountAuditJournal`; a
  test asserts it).

The pseudonym is a **random** value with no table behind it: nothing maps the original id, name or
e-mail to the pseudonym, and nothing derived from the original (no hash of it) is used, so the pseudonym
cannot be recomputed. Requirement: no log line at any level, no `PersonalDataErased` subject and no
exception message carries the original id, username or e-mail of an erased person; the summary row
holds the pseudonym text and the row count only.

### What erasure does NOT do (requirements on the documentation)

- **It is pseudonymisation, not anonymisation.** All of one person's rows share one pseudonym id, so
  they remain linkable to each other; with the address and user agent emptied, linkage to the person is
  broken, but the pattern of activity is not.
- **A subject that merely contains the name survives.** Matching is on equality. "Reset requested for
  alice@example.test" is not equal to "alice@example.test" and is kept. The reviewer must read the
  subject formats of the Identity journal (section 11); the nine other journals' subjects name sites,
  databases and domains, which the account rule replaces only for rows that carry the account (B5).
- **Rows of the account that carry no `AccountId`** (administrator-initiated entries of the nine other
  journals, B5) are not matched by the account rule. Their subject may name a site or database of the
  deleted account.
- **Entries about people who never had a login** (a failed sign-in with an e-mail address someone typed)
  are not matched by any rule unless the typed text equals a deleted user's name.
- **Logical, not physical.** An `UPDATE` leaves the old tuple until vacuum; write-ahead log, replicas
  and the panel's own database dumps (the installer's pre-update dump, `panel-backup`) keep the
  pre-erasure rows. **Restoring a backup restores the erased data** and its users with it. The
  documentation says this; this change adds no purge of backups.
- **Other stores are out of scope:** the Wolverine message store, mail the panel sent, the Serilog
  log files, the agent's journal lines (account names and domains), customer content in home
  directories and database contents.
- **Erased rows follow the same retention as any row.** There is no second clock. Under B1's default
  they are kept forever, pseudonymised.

### Atomicity

The caller opens one explicit transaction; the pseudonymiser mutates tracked entities through the
entity's own methods (`ErasePerson`, `EraseAccount`), `SaveChanges` per batch of 1,000 inside that
transaction, and never commits. The guarantee is **scoped**: it is true for Identity's transaction. The
rest of an account deletion runs in other modules' contexts (a handler works with one `DbContext`,
rules/csharp.md), so a failure after Identity's cascade committed leaves the logins removed and the rows
erased with the account still alive. That is exactly as atomic as the existing user removal, no more;
the documentation and the code comments must not claim that an aborted deletion never erases.
Requirement: the batches do not keep every row tracked (clear the tracker per batch), so erasing a
large account does not hold the whole journal in memory.

Because the sweep re-erases anything orphaned, a rollback to the previous release (which neither
erases nor stamps the new columns) followed by an upgrade converges: rows the old release left behind
are erased on the next daily run.

### The account column and the tenant census

`AuditEvent` gains `AccountId`, which makes it look like a customer's row to `TenantScopeTests` and to
`ModuleAccountResidueAuditor`. The first is answered by a **named exemption with its reason** (an
administrator-only, server-wide table, also written with no principal on failed sign-ins); the reason
must also say that exposing the journal to customers would need a query filter and the removal of the
exemption. The second is answered by the cascade erasing before the residue check; a test asserts the
residue audit reports clean after a deletion. The `AccountId` stamp is taken from the authenticated
caller or passed explicitly by `AccountAuditJournal`; it is never bound from a request.

## 5. Export as exfiltration (#145)

The export is the only way the panel hands the whole security record, with every address and user
agent in it, to one caller in one response. It is therefore treated as a data-disclosure endpoint.

**Controls (requirements):**

1. **Administrator only.** `AuthorizationPolicies.AdminOnly` at class level on every new controller. No
   API-key scope is added; keys stay narrow, and a key is refused (tested). No customer, reseller or
   anonymous caller reaches any of it (tested per caller kind).
2. **View-as is refused.** `[ViewAsAccess(ViewAsAccess.Refused)]` on the export and privacy actions,
   although administrator-only actions are exempt from the declaration (belt and braces, deliberate).
   The test asserts the request is refused and names which control refused it (the policy's 403 or
   `HostViewAsNotAllowed`); the author did not verify which fires first for a viewed-as token.
3. **Rate limit.** Named policy `audit-export`, fixed window, 6 requests per hour, shared with the
   personal-data export. This slows a stolen session; it does not stop it: six exports an hour of up to
   500,000 rows each is millions of rows an hour. The partition key of the policy (user, address or
   both) must be the authenticated administrator, not only the address; the reviewer reads it (the plan
   copies the `BackupVerify` shape and the author did not read that partition).
4. **Journalled before streaming.** `AuditExported` records the format, the filters and the row count,
   written by the query handler before the controller streams, so a disconnect does not erase the fact.
   The count is the **intended** count, not the delivered count; the documentation says so. The filter
   text written into the subject is bounded to the column length (`ColumnText`).
5. **Capped, never truncated.** 500,000 rows; above that the handler refuses with 400
   `AuditExportTooLarge`. **Requirement (not in the plan):** the writer is bounded by the plan, not
   only checked against it. It stops after the planned row count, or at an upper keyset bound taken when
   the plan was made, so rows written while the stream runs cannot push an export past the cap.
6. **`Cache-Control: no-store`, `Content-Disposition: attachment`.** The file name is built from the
   clock and the format only (`maran-audit-<UTC>.<csv|json>`); no caller-supplied text enters a header.
7. **No token in a URL.** The SPA downloads through `useApi` with the bearer header and saves a blob; a
   plain link would need the token in the address, and is not used.
8. **CSV formula injection is neutralised.** `Subject` and `UserAgent` carry text an attacker typed
   (a claimed name on a failed sign-in, a user-agent header), and a spreadsheet opens a CSV the
   administrator downloaded. `CsvField.Of` prefixes `'` to a cell whose first character is `=`, `+`,
   `-`, `@`, tab or carriage return, tested on the raw value before quoting, with RFC 4180 quoting
   (doubled quotes, CRLF). The tests carry a hostile subject and user agent for each leading character
   and an inverse control (a plain value is unchanged). Limits: the apostrophe is data to a program
   that is not a spreadsheet, so the **JSON export is the lossless format**; and the rule is the
   spreadsheet-injection list as I know it, which the reviewer should re-check against a current one.
9. **JSON** is written with `Utf8JsonWriter`, which escapes; no string is concatenated into it.
10. **Filters are typed and parameterised.** `actor` is an exact case-insensitive match limited to 64
    characters with the action alphabet; `account` is a `Guid?`; the search term goes through
    `LikePattern`. No filter text reaches SQL unparameterised (item 5).
11. **No secret columns.** The export row has 14 fixed machine-neutral columns; the journal holds no
    password, token or key, and `Subject` is written only by the journals (rules/csharp.md), whose
    redaction policy is the control. The export adds none.

**The personal-data export (#146) adds:**

- **Allow-listed providers.** An `IPersonalDataProvider` returns named string fields only, chosen field
  by field. **Never exported:** password hashes, TOTP secrets, recovery codes, token hashes, API-key
  digests, encrypted columns, task and cron output, and files in homes. Fail-closed on secrets: a new
  secret column on an entity is not exported until a provider names it.
- **Census.** `PersonalDataProviderCensusTests` fails when a module whose model holds an `AccountId`
  entity registers no provider, with a named exemption list and a planted violation, in the form of
  `TenantScopeTests`. Its limit, stated: it proves a provider EXISTS, not that it exports every field
  of its entity or that a field it exports is harmless. Completeness (a new personal column is missing
  from the export) is the failure the census cannot see; the secret-leak failure is covered by a
  planted-secret positive control **per provider**.
- **Runs as the administrator.** Tenant filters admit an administrator, so no `IgnoreQueryFilters` is
  added.
- **Other people's data.** An account export contains its team's usernames and e-mails. It is for the
  administrator; the documentation says that forwarding it to the data subject without removing other
  people's data is the operator's act, not the panel's.
- **Same cap and refusals.** 500,000 audit rows (`PersonalDataExportTooLarge`); an unknown subject is a
  404 (`PrivacySubjectNotFound`) for an administrator, with no tenant disclosure because only an
  administrator reaches it. The route id is written into the query by the action and never bound from
  the body or the query string (`BoundCommandGuardTests`).
- **Journalled** as `PersonalDataExported`, naming the kind and the (non-secret) subject id.

## 6. Checklist (rules/security.md items 1 to 13)

1. **Input:** every filter and the format are validated (FluentValidator) and the bound members that the
   server establishes are guarded; the retention value is checked in the validator and the entity.
2. **Paths:** none read or built. The export writes to the response, never to disk.
3. **Shell:** none; the API spawns nothing.
4. **Config-file injection:** the line-oriented output is the CSV, covered by section 5, item 8.
5. **SQL:** EF Core only. The migration is generated; the filtered unique index and the `bytea` columns
   are the portability points (section 12). Any `// raw-sql:` is parameterised.
6. **AuthZ:** `AdminOnly` on every endpoint; the `AuditEvent` exemption is named with its reason; the
   erase paths take no id from a caller. Tenant IDOR does not apply (administrator-only), and the tests
   assert refusal for customer, reseller, key and anonymous callers.
7. **Configuration:** the two new rate-limit variables are documented in the root `.env.example`. No
   other variable and no secret.
8. **Secrets:** no secret in a log, subject or export; the chain key is derived, held in memory and
   never logged or stored. The erasure code logs no original identifier.
9. **Crypto:** HMAC-SHA256, HKDF-SHA256, SHA-256, `FixedTimeEquals`; Ed25519 and Argon2id untouched.
10. **Surface:** no new port, daemon or outbound call.
11. **Dependencies:** none added.
12. **Agent:** no rpc added or changed.
13. **Module reach:** providers touch only the Sdk window. No new `AgentCapability`.

## 7. What an attacker could do with this surface

- **Destroy evidence of their own actions** by shortening retention (section 2): bounded to the
  retention period of visibility; not preventable by an administrator by definition.
- **Rewrite who or what a row says** without a trace, through the erasure marker (section 3, limit 1):
  open unless the erasure is authenticated.
- **Trigger erasure of an actor** by getting a `Users` row deleted, and the sweep does the rest
  (section 4): bounded by the guards required there.
- **Exfiltrate the journal** with a stolen administrator session: slowed to six exports an hour, always
  journalled, not prevented.
- **Hold the database busy** with chain verification or repeated maximum exports: bounded by the rate
  limits and single-flight, not eliminated.
- **Plant a spreadsheet formula** through a claimed username or a user agent: neutralised in the CSV.
- **Use a rotated key to hide tampering** (every row breaks, so a real break is lost in the noise): a
  stated cost of B4 without a key version.
- **Learn something from the export** after it leaves the panel: out of the panel's control; the file
  is plain text, and its handling is the operator's.

## 8. Failure behaviour

- **The maintenance handler must not stop the panel or the journal.** An exception in the seal, the
  retention run, the sweep or the verification is caught at the handler boundary, logged at Error with
  no row content, and the other steps still run. A failed retention run journals `AuditRetentionRun`
  with a failed outcome and deletes nothing it did not anchor.
- **Retention fails closed.** Any doubt (the policy row missing, a value out of bounds, the anchor not
  found, the key not derivable) means no purge. `null` and "cannot tell" both mean keep.
- **Sealing is idempotent.** A second seal of the same row fails on the unique index and changes
  nothing.
- **A restore** brings back rows, users and the anchor from the same moment; the key comes from
  `panel.env` restored with them. A restore that mixes a database from one moment with a key from
  another reads as a break.

## 9. What the closing tests prove, and what they do not

They prove, on the real surface with PostgreSQL: the export is refused to every non-administrator, is
journalled, is capped and rate limited; a hostile cell is neutralised; a purge removes only a sealed
prefix, advances the anchor and is itself journalled; a clean chain verifies and a planted tamper
(positive control) is found; an erased row still verifies; a deleted account leaves no original id, name,
e-mail, address or user agent in any column (a planted original is found, the probe's positive control).

They do **not** prove:

- that a tamper of an erasable field is detected (it is not, section 3);
- that the unsealed window is empty, or that tail deletion is noticed;
- behaviour of the filtered unique index, `bytea` handling and `HKDF` on PostgreSQL 13 and on the
  other supported distributions, unless the whole suite is run on 13 as well as 16 (rules/testing.md);
- that every field of every provider is harmless, beyond the planted secrets;
- that physical remains (dead tuples, write-ahead log, dumps) are gone;
- anything about a compromised process, host or key.

## 10. Requirements this note adds to the plan

1. The documentation states that actor, address, user agent, subject and account are NOT tamper-evident
   (section 3, limit 1); the lead decides whether to add a keyed erasure mark to close it.
2. `AuditRetentionPolicy.Replace` enforces the bounds in the entity as well as the validator.
3. The anchor and the purge batch are one transaction, anchor first (the plan says so; kept as a
   requirement).
4. `ResellerDeletingHandler` calls the pseudonymiser in its transaction (section 4).
5. The orphan sweep refuses on an empty user set, caps the actors it erases per run, and has an
   anonymous-caller test with positive and inverse controls.
6. The export writer is bounded by the plan, not only checked against the cap.
7. Single-flight on chain verification.
8. The export rate-limit partition is the authenticated administrator.
9. No log line, subject or exception names an erased person's original identifiers.
10. The change-tracker is cleared per batch in the pseudonymiser.
11. The `AuditEvent` tenant exemption's reason says a customer-facing journal needs a filter.
12. The documentation covers: the unsealed window and its post-upgrade length, the rotation
    consequence, the silent break, the restore and backup limit, the physical-remains limit, the
    coverage limit of B5, and that a retention window is not a backup.

## 11. What a reviewer must check

Read, in this order, as the author could not check them:

1. `AuditRetentionRunner`: the cutoff arithmetic and the prefix rule (M is the smallest index inside the
   window, less one); the anchor advanced in the same transaction and before the delete; a `null` policy
   purges nothing; a failed run purges nothing it did not anchor; nothing purges an unsealed row.
2. `AuditPseudonymiser` and every caller (`AccountDeletingHandler`, `RemoveSubUser…Handler`,
   `ResellerDeletingHandler`, the sweep): which columns each method changes and which it keeps; the
   transaction is the caller's and nothing here commits; the sweep's guards (section 4); the pseudonym
   is random and unmapped; the matching of `Subject` is equality.
3. `AuditChainVerifier` and `AuditChainSealer`: the branch for a row with neither salt nor `ErasedAt`;
   the constant-time comparison; the canonical form is length-prefixed and truncates `OccurredAt` to
   microseconds exactly as PostgreSQL stores it; the key is taken from `ISubKeyDeriver` and appears in
   no log; the double-seal failure path.
4. `HkdfSubKeyDeriver`: the IKM is the same 32 bytes the AES-GCM service holds, the info is the purpose
   label, no key is cached in a static.
5. The **field lists of every `IPersonalDataProvider`**, entity by entity, against the entity's columns:
   nothing secret-bearing, nothing encrypted, no output text. Read the entity, not the provider alone.
6. The **subject formats** each journal writes (Identity's first), to judge what the equality rule
   misses.
7. `AuditExportWriter` and `PersonalDataExportWriter`: bounded by the plan; flushes per batch;
   `no-store`; the file name carries no caller text; `CsvField` against the leading characters.
8. The controllers: `AdminOnly`, `ViewAsAccess.Refused`, the rate-limit policy and its partition key,
   the server-established members guarded, the route id stamped by the action.
9. `DatabaseAuditWriter`'s `AccountId` stamp and `AccountAuditJournal`: the delete flow passes no
   account id.
10. The migration: expand-only, nullable columns, `maran migrate guard` clean, no `// contract-phase:`.
11. The tests listed in the plan, for the controls: a planted tamper is found, a clean chain accepted,
    a planted secret found by the probe, a planted original identifier found after erasure.

## 12. What the author could not verify

1. Nothing in this note has run: the code does not exist yet, and this session runs no build and no test.
   Every statement above is a requirement until a gate pass and a reviewer say otherwise.
2. PostgreSQL 13 behaviour of the filtered unique index and the `bytea` columns, and the other
   supported distributions; the baseline is PostgreSQL 16 for development. Read, not observed.
3. That `Microsoft.Extensions.Hosting` hosted-service scheduling and Wolverine handler discovery work
   for the new job the way the existing `TaskRetentionScheduler` does; copied from its shape, not run.
4. The partition key of the `BackupVerify`-shaped rate-limit policy, and whether view-as or the
   administrator policy answers first for a viewed-as token.
5. Whether any deletion path for logins exists outside Identity. The search above covers
   `backend/src/Maran.Modules/Identity` only; a hit elsewhere would be a fourth path.
6. What every journal writes into `Subject`. Identity's was not read in full for this note.
7. That no procedure for rotating `Security__EncryptionKey` exists. The search was
   `/usr/bin/grep -rniE "EncryptionKey" docs rules installer --include=*.md --include=*.sh -n | /usr/bin/grep -iE "rotat|change|replace|lose|lost"`,
   which returned nothing; its positive control,
   `/usr/bin/grep -rn "Security__EncryptionKey" installer docs --include=*.md --include=*.sh --include=*.example`,
   returned four lines in `installer/`. It looks only at the paths and file types named, so a
   procedure in an issue or a file type not listed would not be found.
8. Whether any code path deletes an administrator's `Users` row today. If one does, the sweep erases
   that administrator's accountability within a day.
9. Behaviour under a large journal: the cost of sealing a long backlog, of a full verification and of a
   500,000-row export on a small host was reasoned, not measured.
10. That the spreadsheet-injection prefix list is complete for the spreadsheet programs the operator
    uses.
