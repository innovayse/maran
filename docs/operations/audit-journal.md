# The audit journal: export, filters, retention and tamper evidence

The audit journal is the panel's record of who did what, when, from where and whether it worked.
It lives in the panel database (`identity.AuditEvents`) and is read by administrators only. This page
covers what an administrator can do with it (filter, export, set how long it is kept), the hash chain
that gives it partial tamper evidence, and, just as important, what none of that protects against.

Read "What the chain does not protect" before relying on the journal as evidence.

Status of the change this page describes (issues #145 and #146): the second security review is
OUTSTANDING and the branch must not merge to `main` until a reviewer has read it. The threat note is
`docs/superpowers/notes/2026-11-07-audit-retention-erasure-threat-note.md`.

## Filters

`GET /api/v1/audit` and the export below take the same filters, all optional and combinable:

| Filter | Meaning |
|---|---|
| `action` | Exactly this action. Letters, digits, `.`, `_`, `-`; longest accepted length is the action column's. |
| `outcome` | `succeeded` or `failed`. |
| `from`, `to` | A window of instants, both ends inclusive. `from` later than `to` is refused. |
| `search` | A term matched literally, case-insensitively, inside the actor name, the subject, the address the request came from, or the name of the operator who was viewing as the actor. |
| `actor` | Entries whose actor used exactly this name, compared without regard to case. At most the actor-name column length, same alphabet as `action`. It matches the actor, not the operator of a view-as session. |
| `account` | Entries recorded against this hosting account (an id). |

### The account filter has a coverage gap

An entry only carries an account when the panel could stamp one:

- entries written during a customer session, including a session an administrator opened with
  view-as, are stamped with the customer's account;
- the Accounts module stamps the account on five operations: create, suspend, reactivate, plan change
  and owner e-mail change.

**Entries an administrator initiates through the other nine journals (sites, databases, FTP, SFTP,
cron, backups, certificates, firewall, monitoring) carry no account.** They name the account, if at
all, only in their subject text, which `search` reaches and `account` does not. So the account filter,
the account scope of an export, the account part of a personal-data export and the account erasure
rule (see `personal-data.md`) all miss those entries. Threading the account through those journals is
a follow-up, not part of this change.

The deletion of an account is also deliberately written without an account id: the `AccountDeleted`
entry is the one record that an administrator destroyed an account, and it must not point at an id
that no longer exists.

## Export

`GET /api/v1/audit/export?format=csv|json` plus any filter above. Administrators only; refused while
viewing as a customer.

- **Formats.** `json` (the default) is one JSON array and is the lossless format. `csv` is RFC 4180:
  comma separated, `CRLF` line ends, UTF-8 without a byte-order mark, one English header row.
- **Order.** Oldest first.
- **Columns** (the same 14 in both formats, machine-neutral and not translated): `id`, `occurredAt`
  (UTC, ISO 8601 with microseconds), `actorUserId`, `actorUsername`, `impersonatorUsername`, `action`,
  `outcome` (`succeeded` or `failed`), `subject`, `ipAddress`, `userAgent`, `accountId`,
  `correlationId`, `chainIndex`, `rowHash` (lower-case hex). `chainIndex` and `rowHash` are empty for
  an entry not yet sealed into the chain.
- **Cap: 500,000 rows.** If the filters match more, the request is refused with a 400 telling the
  administrator to narrow the window; the file is never silently truncated. The writer stops at the
  row count counted when the export was planned, so entries written while the file streams do not
  push it past the cap.
- **Rate limit: 6 requests per hour** per signed-in administrator. The personal-data export shares
  the same bucket. Both numbers are configurable (`RateLimiting__AuditExportMaxRequests`,
  `RateLimiting__AuditExportWindowSeconds`, see `.env.example`). The limit slows a stolen
  administrator session; it does not stop it, because six exports of up to 500,000 rows is still a
  lot of rows.
- **Journalled.** Every export writes an `AuditExported` entry (format, filters, row count) before the
  first byte is sent, so it is recorded even if the client disconnects. The row count in it is the
  count at planning time, not the number of rows actually delivered.
- **Spreadsheet formula neutralisation (CSV).** Subjects and user agents can contain text an attacker
  typed, such as the name claimed on a failed sign-in, and spreadsheets execute cells that start with
  a formula character. A CSV cell whose first character is `=`, `+`, `-`, `@`, a tab or a carriage
  return gets a leading apostrophe. The apostrophe is real data to anything that is not a
  spreadsheet, which is why `json` is the format to use when a program reads the file.
- **Headers.** `Content-Disposition: attachment; filename="maran-audit-<UTC yyyyMMddTHHmmssZ>.<csv|json>"`
  and `Cache-Control: no-store`. The file name is built from the clock and the format only.

In the SPA the audit journal page (administrators only) carries an export menu that exports with the
filters currently applied, a retention card and an integrity card (chain status and a verify
button), all calling the endpoints on this page.

## Retention

Default: **the journal is kept forever.** Nothing is deleted unless an administrator opts in.

- `GET /api/v1/audit/retention` returns the window, the accepted bounds and when the daily run last
  finished. `PUT /api/v1/audit/retention` with `{ "retentionDays": 365 }` sets it; `null` means keep
  forever.
- **Bounds: 30 to 3650 days.** Anything else is refused, in the request validator and again in the
  entity. There is no value that means "delete everything now".
- **Changing the window is journalled** (`AuditRetentionChanged`, old and new value in the subject,
  `indefinite` for no limit). The change deletes nothing by itself.
- **A daily run applies it.** An hourly scheduler (first tick 3 minutes after the panel starts)
  checks whether 24 hours have passed since the last run; if so it runs retention, then the
  orphaned-actor sweep (see `personal-data.md`), then a chain verification. Because the run is daily,
  a shortened window takes effect at the next daily run, not at the request.
- **Every run is journalled**, including one that deleted nothing (`AuditRetentionRun`, subject
  `retentionDays=… cutoff=… purged=N throughChainIndex=M`). A failed retention step is journalled as
  a failed run and deletes nothing it has not anchored. "No `AuditRetentionRun` row" means no run
  happened.
- **Only a prefix is purged.** Retention deletes sealed entries from the oldest end, up to one before
  the smallest chain position that is still inside the window. An entry that is out of the window
  but sits after a newer one in the chain is kept. Unsealed entries are never purged; the run seals
  before it purges.
- **The anchor.** Each batch of 1,000 deletions advances a single anchor row (the last purged
  position and that entry's hash) in the same transaction as the deletion. Verification starts from
  the anchor, so the surviving chain still checks after a purge.
- **Raising the window restores nothing.** Purged entries are gone.

### A retention window is not a backup

Retention is deletion. An administrator, or anyone holding an administrator session, can shorten the
window to 30 days and let the next daily run destroy older entries, including the entries that record
their own earlier actions. The change and the run are journalled, but those entries are themselves
removed once they age past the new window, so the time in which the act stays visible in the panel is
the new retention period. An administrator is allowed to do this by definition and the panel cannot
prove afterwards that a purged prefix was purged legitimately. Nobody is alerted when the window
changes. An operator who needs more than this keeps off-box copies of exports (see Export) and, for
what the root agent did, the agent's own journal lines, which are outside this table.

## The hash chain

Each entry, once sealed, is linked to the one before it:

`RowHash = HMAC-SHA256(key, "maran-audit-v1" || previous RowHash || immutable fields || commitment)`

- **Immutable fields:** the entry id, its time (to the microsecond, which is what the database
  stores), the action, the outcome and the correlation id.
- **Commitment:** `SHA-256(salt || erasable fields)` with a random 16-byte salt per entry. The
  erasable fields are the actor id and name, the subject, the address, the user agent, the account
  id, and the id and name of a viewing operator.

The split exists so the personal-data erasure can replace the erasable fields without rewriting the
chain: erasing an entry changes those fields, sets an erasure time and destroys the salt, and leaves
the hash intact.

**Sealing is asynchronous.** The audit write path takes no lock and does not seal. The hourly
maintenance tick seals unsealed entries in time order, 500 per batch and at most 20 batches (10,000
entries) per tick. `GET /api/v1/audit/chain` returns the sealed and unsealed counts, the newest sealed
position and the purge point.

**The key** is derived with HKDF-SHA256 from the panel's encryption key (`Security__EncryptionKey`)
under the purpose label `maran/audit-chain/v1`. No separate secret exists, and it is never stored or
logged.

### What the chain detects

Against someone who can write to the table but does not hold the key:

- a changed action, outcome, time or correlation id on a sealed entry;
- a changed erasable field on a sealed entry that still has its salt;
- a deleted entry in the middle of the sealed range (a gap in the positions);
- reordered entries;
- an entry inserted after sealing (it cannot carry a valid hash without the key).

### What the chain does not protect

State these limits whenever the journal is used as evidence. The chain is not "tamper-proof".

1. **Who, from where and to what are not tamper-evident.** The actor, address, user agent, subject
   and account are erasable fields. A legitimate erasure and a malicious rewrite of those fields look
   the same to the verifier: the erasure time is set, the salt is gone, the fields differ, and the
   verifier skips the commitment of an erased entry. The erasure time is not covered by any hash. The
   chain shows that an entry with this action, outcome and time exists in this place in the order, and
   nothing more about the erasable fields once their salt is gone.
2. **The unsealed window.** An entry is unsealed for up to an hour in steady state. In that window it
   can be changed, deleted or forged with nothing to compare against. After an upgrade the entries
   already in the journal are not backfilled in one go: they are sealed at most 10,000 per hourly
   tick, so a journal of N existing entries is partly unsealed for about N / 10,000 hours. Sealing
   freezes what is in the table at that moment, so for those entries the chain attests nothing about
   their past. `UnsealedRows` in the chain status shows how much is not yet covered.
3. **A purged prefix.** The anchor records that a prefix was removed and the hash of its last entry;
   it is not keyed. Someone who can write the table can delete a prefix and set the anchor from values
   read off the surviving rows, and verification cannot tell that from a retention run.
4. **The tail.** Removing the newest sealed entries leaves a shorter valid chain. There is no
   externally held head. The chain status's newest position and an off-box export are the only
   cross-checks.
5. **The key.** A holder of the panel process or of `panel.env` can derive the key and rewrite the
   whole chain consistently. The chain says nothing against a compromised panel process or root.
6. **The clock.** Times come from the host clock. The chain shows sealing order, not that the clock
   was right.
7. **Rotating the panel encryption key reads as tampering.** The chain key is derived from
   `Security__EncryptionKey`. If an operator changes that key, every sealed entry was hashed under the
   old one, verification reports a break at the first sealed position, and nothing in the entries can
   tell that apart from an attack (no key version is stored). The repository has no documented
   procedure for rotating that key; export the journal before any change to it, and expect to accept
   the break afterwards. A break reported at the very first position is the signature of a changed key
   rather than of an edit to one entry.
8. **Restore and backup limits.** A panel backup (`maran panel-backup`) and the installer's
   pre-update database dump hold the journal and, for the panel backup, the encryption key; a restore
   brings back the database and the key of the same moment and the chain verifies. A database from
   one moment restored beside a key from another reads as a break. Erased personal data comes back
   with a restore (see `personal-data.md`). Neither the backup nor the dump is a protected copy of the
   journal: they are as readable and writable as the database they came from.
9. **Not a separate record of what the agent did.** The root agent's own journal lines are a different
   trail outside this table.

## Verification

- **Daily.** The daily maintenance run verifies the chain after retention and the orphaned-actor
  sweep, journals the result as `AuditChainVerified` (`succeeded` is false on a break) and logs a
  break at error level.
- **On demand.** `POST /api/v1/audit/chain/verification` (administrators only, refused while viewing
  as a customer, behind the general API rate limit) walks every sealed entry from the anchor and
  returns `{ intact, rowsChecked, firstBrokenChainIndex, break, verifiedAt }`. It journals
  `AuditChainVerified` too. A broken chain is a successful answer with `intact: false`, not an error.
  Only one walk runs at a time on a panel; a request that arrives while one runs waits for it and
  receives that walk's answer if it finished after the request arrived.
- **Break kinds:** `RowHashMismatch`, `CommitmentMismatch` (an entry that still has its salt no longer
  matches its commitment), `IndexGap`, `AnchorMismatch` (the first surviving entry does not follow
  from the anchor), `SaltMissing` (an entry has neither a salt nor an erasure mark).
- **A break is silent.** Nobody is e-mailed. It is visible to whoever reads the journal, the log or
  this endpoint, so an operator who relies on it checks one of them.
- A verification reads every sealed entry, so on a very large journal it costs the database real work.
  Its cost on a small server was not measured.
