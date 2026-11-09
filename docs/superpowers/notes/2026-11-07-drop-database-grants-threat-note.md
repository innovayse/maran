# Threat note — a dropped database must not leave grants behind (issue #432)

Required by `rules/security.md` ("Sensitive change escalation"): the change touches the
agent's root-run database operations (`agent/crates/ops/src/db/`). Written BEFORE the change.
**The second reviewer is OUTSTANDING.** This change may land on a branch and MUST NOT merge to
`main` until one has read it.

## What the surface is

`drop_database` and `drop_account_databases` remove a customer's MariaDB/MySQL database. The
server's `DROP DATABASE` does not touch `mysql.db`: the rows that grant a user privileges on the
database stay, because a database-level grant is stored by name (a pattern), not by reference. A
database created later under the same name is reached by every grant still stored for that name.

## What was verified in the code (not assumed)

- The panel issues exactly one kind of grant: `GRANT ALL PRIVILEGES ON <db>.* TO <dedicated user>`
  (`create_database.rs`), and `drop_database` ends with `DROP USER IF EXISTS` for the user the
  CALLER names. `DROP USER` removes that user's grants, so the common path leaves nothing.
- The path is NOT closed in three cases, all of which leave a grant on a name that can be
  created again:
  1. The `user` passed to `drop_database` is named independently of the database (db.proto), so
     a drop that names another or a mistyped user removes that user and leaves the real grantee.
  2. `DROP DATABASE` runs before `DROP USER` on purpose; a crash between them leaves the user and
     its grant, and `create_database` then reuses that user (`CREATE USER IF NOT EXISTS`), so the
     recreated database is reachable with the OLD credential.
  3. A grant held by a user other than the dedicated one (an operator's hand-made grant, a user
     shared between two databases) is never looked at.
- The Rust code therefore needs the fix; the issue is confirmed in the shape "a stale grant can
  outlive its database", with the dedicated-user path already covered.

## The attack

A customer (or a previous tenant of a recycled account name) keeps a credential that holds a
grant on `acct_shop`. The database is dropped and later `acct_shop` is created again for someone
else. The old credential reads and writes the new tenant's data. Impact: cross-tenant data
access. Reachability: needs a leftover grant (cases above) plus a later create of the same name.

## The fix and why it is safe

Before `DROP DATABASE`, both operations read `mysql.db` (the existing `SELECT Host, Db, User`
read `repair_grants` already uses, now shared) and run
`REVOKE ALL PRIVILEGES, GRANT OPTION ON <db>.* FROM '<user>'@'<host>'` for every row whose `Db`
is, literally, this database's escaped grant pattern or its legacy unescaped spelling. Revoke
comes first: a crash after it leaves a database nobody can reach and a retry finishes; a crash
after the drop can no longer leave a grant.

- Matching is exact string equality against `grant_pattern_for(name)` and `name`. Wildcard rows
  such as `acct%` are NOT touched: they are not this database's grants.
- The statement interpolates a user and host read FROM THE SERVER, which is stored data and not
  a validated type. They are interpolated only when they match a strict allow-list alphabet
  (user `[A-Za-z0-9_.-]`, host `[A-Za-z0-9_.:%-]`); a row that does not is a typed
  `DbError::Unparsable` and the drop stops BEFORE anything is dropped. Failing closed is chosen
  over skipping, because skipping would silently keep the grant.
- Idempotent: a second revoke finds no rows. `NotFound` still sends no statement beyond the
  existence listing.

## What a reviewer must check

1. The allow-list alphabets cannot carry a quote, backtick, backslash, semicolon or whitespace.
2. Revoke-before-drop ordering, and that a failure between them is retry-safe.
3. That `drop_account_databases` revokes per database before its drops.

## What the author could not verify

- Behaviour on MySQL (as opposed to MariaDB): the real-host test is MariaDB-only; `REVOKE ...
  GRANT OPTION ON db.*` is documented for both.
- Table-, column- and routine-level grants (`tables_priv`, `columns_priv`, `procs_priv`). The panel
  never issues them; an operator's hand-made ones on a dropped database are NOT revoked here.
- Grants left by drops that happened BEFORE this fix are not hunted down (no database is named
  to key on); the retry returns `NotFound` and cleans nothing.
