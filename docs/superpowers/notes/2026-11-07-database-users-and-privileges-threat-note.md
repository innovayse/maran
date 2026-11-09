# Threat note: extra database users and per-database privileges, agent side (issue #156, users lane)

`rules/security.md` requires a threat note, written first, for a change to a privileged surface.
This change adds three rpcs to the root agent (`maran-agent`) that create and drop MySQL logins and
change what a login may do on a database. This note is written BEFORE the code: every statement below
is a requirement the code must meet, not a description of code that exists. Where the code lands and
differs, the code is wrong or this note is amended in the same change.

**Second review: OUTSTANDING (tracked in #331).** The work is carried out by agent sessions, which
cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human reviewer has read
this note and checked every item under "What a reviewer must check" (rules/security.md "Sensitive
change escalation"). The owner is told at hand-off that this surface carries an outstanding review.
Nothing here discharges it.

**Scope.** This note covers the *users and privileges* lane only. Remote access (a login reachable
from another address, a listener on a network interface, a firewall rule opened with it) is NOT
covered and NOT built: it is a new reachable surface that needs an owner decision and a spec change
first (rules/security.md item 10), and it gets its own note when that decision exists. Nothing in this
change lets a login connect from anywhere but `localhost`.

## What is added

1. Proto: `enum DatabasePrivilege` (18 values and the zero value) and three rpcs on `DbService`:
   `CreateDatabaseUser`, `DropDatabaseUser`, `SetDatabaseUserPrivileges`. `PROTO_VERSION` 20 -> 21.
2. `agent-core::validation::db`: `DatabasePrivilege` (the closed set) and `DatabasePrivilegeSet`
   (a duplicate-free, bounded, deterministically ordered set of them).
3. `ops::db`: `create_database_user`, `drop_database_user`, `set_database_user_privileges`, the
   models they take, and a crate-private privilege-to-column table.
4. `agent::services::db`: the three handlers and the request-to-validated-input bundles. The three
   rpcs are classified mutating in `audit/mutating_rpcs.rs`, so each yields an audit line.

Not added: no daemon, no port, no listener change, no outbound call, no file, no new dependency, no
change to the existing four mutating db rpcs' behaviour.

## What changes about the threat model

- Before: every database login existed as `'u'@'localhost'` with `ALL PRIVILEGES` on exactly one
  database, created together with that database. A customer could not widen or narrow their own access.
- After: a customer can create further logins for their own account, and choose which of their own
  databases each may touch and with which of 18 privileges. A mistake in the agent's statement
  construction is now a cross-tenant read or write, because a database-level grant is scoped by a
  *pattern* in `mysql.db`, not by the database's identity (see
  `docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md`).
- Unchanged: the agent holds no database credential (it authenticates by `unix_socket` as
  `root@localhost`), every login is local, and the panel still stores no password.

## The argument, by concern

1. **Grant scoping, the cross-tenant risk.** Every grant this change issues is
   ``ON `<grant_pattern_for(db)>`.*``, built from a `DatabaseName` and the escape in `grant_pattern.rs`,
   which is the one place that escape is written. The scope is never `*.*` and never a bare `*`. A
   user name and a database name can only be built by `for_account`, so a request cannot name another
   account's login or database: the service rebuilds both from the account the panel authorised and a
   suffix naming a neighbour produces a name under the caller's own account. *Held up by:* the
   statement-pinning tests in `set_database_user_privileges_tests.rs` (every GRANT and REVOKE it sends
   carries the escaped pattern and the account's own user) and the existing `grant_pattern_tests.rs`.

2. **The privilege set is closed, so the dangerous privileges are not representable.** The enum has
   exactly the 18 database-level privileges a customer may hold on their own database: SELECT, INSERT,
   UPDATE, DELETE, CREATE, DROP, ALTER, INDEX, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES,
   CREATE VIEW, SHOW VIEW, CREATE ROUTINE, ALTER ROUTINE, EXECUTE, TRIGGER, EVENT. `GRANT OPTION`,
   `FILE`, `PROCESS`, `SUPER`, `RELOAD`, `SHUTDOWN`, `REPLICATION *` and every global privilege have no
   variant, so no request, however malformed, can produce a statement that grants them. The wire
   value is mapped to the variant in one `match` in the service layer; `UNSPECIFIED` and any number the
   contract does not define are refused as invalid input, never defaulted. The SQL keyword of each
   variant is a `&'static str` constant: no caller text is ever rendered into a privilege list.
   *Held up by:* a test that every privilege the type can render is on a fixed allow-list of keywords
   (and that `GRANT OPTION` is not among them), a census test that all 18 proto values map to
   distinct variants, and refusal tests for zero, negative and out-of-range numbers.

3. **A privilege change must never grant the right to grant.** `set_database_user_privileges` always
   revokes `GRANT OPTION` on the database when the stored row holds it, whatever set was asked for.
   A row can hold it only if someone granted it by hand or an older agent did; this makes the panel's
   record of a login's power true again instead of silently excluding the one privilege that lets the
   customer hand their database to a login the panel does not know. *Held up by:* a test that plants a
   row holding `Grant_priv = 'Y'` and requires the revoke.

4. **Minimal delta, no window with no access.** The operation reads the login's current row for that
   database (named columns only, `Host = 'localhost'`, the user from a validated type), grants what is
   missing first, then revokes what is surplus. A crash between the two leaves the login with too much,
   never with nothing, and a retry converges. This is the same ordering `repair_grants` already uses
   and for the same reason. A second call with the same set sends no statement beyond the read.
   *Held up by:* statement-order tests and an idempotency test.

5. **Wildcard rows an older agent left behind.** A row stored under the unescaped spelling of the
   database name is a wildcard that also matches neighbours' databases. If the operation finds one for
   the same login and database it revokes it after the escaped grant is in place, so the login ends
   with exactly the requested set and no residual reach through the wildcard. It does not touch a
   row it cannot identify as this database's own (any other `Db` value is left alone and unread by the
   delta).

6. **Injection.** The statements interpolate a `DbUserName`, a `DatabaseName`, a `Password` and keywords
   from the closed enum. The first three cannot hold a quote, backtick, backslash, semicolon, space or
   newline; the keywords are constants. Values read back from `mysql.db` are used only as `'Y'` / `'N'`
   comparisons and as the `Db` column compared against the two spellings, and are never interpolated
   into a statement. The existing one-statement guard and statement-size ceiling in `ProcessDbHost`
   stay in force; a password travels on standard input, never in argv.

7. **Password handling.** `CreateDatabaseUser` carries a password exactly as `CreateDatabase` does:
   parsed into `Password` (which prints `<password>`), interpolated once into `CREATE USER ... IDENTIFIED
   BY`, never logged, never echoed, and no response message has a password field (`db.proto` header).
   `DbError` still cannot hold the client's output. Resetting an extra login's password uses the
   existing `SetDatabasePassword` rpc, which is already keyed on a user suffix; this change adds none.

8. **Idempotency and the tenant boundary of the panel.** `CreateDatabaseUser` on an existing login is
   `AlreadyExists` and leaves its password alone (a retry must not reset a credential already shown).
   `DropDatabaseUser` of a missing login is `NotFound`; `DROP USER` removes the login's rows from every
   grant table, so no privilege survives it. `SetDatabaseUserPrivileges` on a missing login or database
   is `NotFound`; an empty set means "revoke everything this login holds on that database" and is a
   no-op, not an error, when it holds nothing. Whether a login may be dropped on its own (the dedicated
   login of a database may not) is a panel rule, enforced in the C# module; the agent does not know
   which login is dedicated and must not pretend to (`db.proto`, `DatabaseInfo.db_username`).

9. **Audit.** All three rpcs are listed in `MUTATING_RPCS`; the existing census test fails an
   unclassified rpc. Each handler takes the audit slot, names the validated account, and settles it
   with the same `Result` that becomes the response. The line carries the method and the account and
   never a user name, a database name, a privilege or a password.

## What a reviewer must check

- `ops/src/db/set_database_user_privileges.rs`: read every statement. Confirm no value reaches SQL
  except from a validated type or a constant; the pattern is the escaped one; GRANT precedes REVOKE;
  `GRANT OPTION` is revoked and never granted.
- `agent-core/src/validation/db/database_privilege.rs`: the 18 keywords and their absence of
  anything outside the list in concern 2. `ops/src/db/privilege_column.rs`: each column name is the
  one `mysql.db` has for that privilege on the supported servers.
- `agent/src/services/db/validated_privilege_set.rs`: `UNSPECIFIED`, unknown numbers, duplicates and
  an over-long list are refused, and nothing is defaulted.
- `audit/mutating_rpcs.rs`: the three paths are present.
- That `maran proto --accept` recorded exactly this change and `PROTO_VERSION` moved by one.

## What the author could not verify

- **No real server was available.** The unit tests run against an in-memory fake that models
  `mysql.db`; no statement here has been sent to MariaDB. A polygon pass on Ubuntu 24.04 and
  AlmaLinux 9 is owed: an extra login, the privilege delta visible in `SHOW GRANTS`, a login that
  cannot read a database it was not granted, and the escaped row being the one that is changed.
- **The `mysql.db` column set across the supported MariaDB majors (10.5, 10.6, 10.11, 11.x).** The 18
  privilege columns and `Grant_priv` are named from the server's documentation, not from the
  machines; `Delete_history_priv` exists on newer MariaDB and is deliberately not read.
- **DELETE HISTORY on MariaDB 10.3.4 and later.** `ALL PRIVILEGES`, which `CreateDatabase` grants,
  includes it, and it is not one of the 18. A privilege change on a dedicated login narrows the 18 and
  `GRANT OPTION` but does not remove DELETE HISTORY. It lets a login purge row history of a
  system-versioned table of its own database; it crosses no tenant boundary. Fixing it means either
  granting an explicit list at creation or reading the extra column, and is a decision for the owner.
- **Two simultaneous changes to the same login and database.** There is no lock: the read and the
  statements are not one critical section, so two overlapping calls can leave a mix of the two sets.
  The final state is always within the 18 privileges on the caller's own database, and a repeat of the
  last call converges, so no boundary is crossed; the panel should serialise per login.
- `FLUSH PRIVILEGES` is not issued (`ops/src/db/mod.rs` explains why); account-management statements
  take effect immediately on the supported servers, which a polygon pass should confirm for REVOKE.

## Honest ordering

This note was written before the code. The panel module, the endpoints and the SPA for this lane are
not part of this change. The remote-access lane, the listener and the firewall claim are not designed
here and need the owner's decision first.
