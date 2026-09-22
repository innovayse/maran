# Threat note — the per-database `GRANT` is a wildcard pattern

Required by `rules/security.md` ("Sensitive change escalation"). Covers
`agent/crates/ops/src/db/create_database.rs` — `grant_pattern_for` and the
`GRANT ALL PRIVILEGES` statement it feeds. **This change needs a second
reviewer, and that reviewer is OUTSTANDING.**

## This note is LATE, and here is what that costs the reader

`rules/security.md` says the note is written **first, before the change**. This one
was not. The defect was found and the escape shipped into the working tree by the
hunting lane, finding F0 — the per-database `GRANT` is a wildcard pattern, so a customer's MySQL
user can reach another tenant's database — on 2026-09-13,
which recorded both the note and the second reviewer as OUTSTANDING and wrote
neither. This file was written afterwards, by a different session, in the same day.

Saying so first is not ceremony. A note written after the change is, in the words of
`rules/architecture.md`, a justification validating a choice already made — and a
reader who cannot tell which parts are reasoning and which are reconstruction cannot
weigh any of it. So, explicitly:

- **Reconstruction.** Everything about why the escape was placed in
  `create_database.rs` rather than in `DatabaseName`, and why `%` was left
  unescaped. Those decisions were made before this note existed; §"The fix"
  reports them, it did not shape them.
- **Reasoning, produced by this note's own lane and new.** Everything in
  §"What was measured against a real server". The hunting lane asserted the
  server's behaviour from MySQL's and MariaDB's manuals and said plainly that no
  database had been asked. It has now been asked, and the answers are here.
- **Still not established by anybody.** §"Could a shipped installation already
  have been exploited?" — and it is a question this note cannot close from here.

## What this surface is

The agent's `CreateDatabase` rpc creates a MySQL/MariaDB database for a hosting
account, creates one dedicated database user for it, and grants that user
privileges on that database. It is the only place in the workspace that issues a
`GRANT`.

The attacker model is a hosting customer of a shared Maran server who can choose
their own account name (directly, if the panel lets them; or through whoever
provisions accounts, which in the intended deployment is a reseller or an
automated signup) and who is shown the password of their own database user by the
panel, as designed. They do not control the panel's code, the agent's arguments,
or any other tenant's credentials.

## The defect

```rust
// before the fix
host.execute(&format!(
    "GRANT ALL PRIVILEGES ON `{}`.* TO '{}'@'{USER_HOST}'",
    request.database.as_str(),
    request.user.as_str()
))?;
```

**The database-name position of a database-level `GRANT` is a LIKE-style pattern,
not an identifier.** `_` there matches any single character, `%` any sequence, and
backtick-quoting does not disable either. It is the one pattern position in the
agent; every other statement in `ops::db` compares with `=`, where `_` is literal,
and `CREATE`/`DROP DATABASE` take the name as a true identifier.

Every name this agent grants on carries `_`: a database name is
`<account>_<suffix>`, so it holds the separator at least, and `AccountName` permits
`_` inside the account half as well
(`agent/crates/agent-core/src/validation/system/name.rs:56`).

So a customer taking the legal account name `polydbgrant_ne` and asking for suffix
`shop` receives a grant on the pattern `polydbgrant?ne?shop`, which matches the
victim `polydbgrantone`'s `polydbgrantone_shop` character for character. The
attacker gets ALL PRIVILEGES on another tenant's database, authenticating with a
password the panel showed them.

**Consequence class:** cross-tenant read AND write of customer application data,
inside the one datastore customers keep it in, with `DROP TABLE` included. It is
invisible from above: the panel's rows say each user owns one database, the agent's
statement reads as scoped, and the server agrees with both while enforcing
something wider.

## The fix

`create_database.rs` writes the grant's name through a private `grant_pattern_for`,
which replaces `_` with `\_`.

Three choices, reported as reconstruction:

1. **The escape is in the statement, not in `DatabaseName`.** `CREATE DATABASE` and
   `DROP DATABASE` take the name as an identifier, where a backslash would become
   part of the name — escaping upstream would create a database actually called
   `h\_stco\_main`.
2. **`%` is not escaped**, because a `DatabaseName` cannot hold one. Verified for
   this note: the only constructor is `DatabaseName::for_account`, the inner string
   is private, `AccountName` is `[a-z]` then `[a-z0-9_]`, and the requested half
   `prefixed` accepts is `[a-z0-9]`. `/usr/bin/grep -rnE "'%'|\"%\"|percent"`
   over `agent/crates/agent-core/src/validation/` hits only `env_var_value.rs`,
   which is about systemd specifier expansion (positive control for that pattern
   run against a planted `let x = '%';` — it matched).
3. The doc comment separates the **injection** axis (answered by the alphabet) from
   the **pattern** axis (answered by the escape), because the file's pre-existing
   and correct argument about interpolation reads as covering both and does not.

## What was measured against a real server

New with this note. The vulnerability was reproduced against a real MariaDB 10.11.14: the server
behaves exactly as F0 said, and the escape both closes the hole and leaves the owner served (full
working kept only as a scratch report, not committed). Server:
`mariadbd Ver 10.11.14-MariaDB-0ubuntu0.24.04.1`, the version the Ubuntu 24.04
polygon installs from the installer's own package list, reached over the local
socket as root exactly as `ProcessDbHost` does.

- **The vulnerability reproduces.** With the escape removed, account
  `polydbgrant_ne` (14 chars) received a grant that let its own user `SELECT` the
  planted row `VICTIM-CARD-4111111111111111` out of `polydbgrantone_shop`
  (`exit=0`), `INSERT` into that table, and list the victim in `SHOW DATABASES`.
  It also reached a database created **after** the grant was issued.
  Backtick-quoting did not help: `SHOW GRANTS` rendered the pattern in backticks
  and the match still happened.
- **The escaped form is accepted by the server.** This was the fix's central
  unmeasured premise — the hunting lane took `ON \`foo\_bar\`.*` from the manuals
  and noted that if the manuals were wrong every database creation would fail
  loudly. They are not wrong. `GRANT ALL PRIVILEGES ON \`polydbgrant\_ne\_shop\`.*`
  parses and stores the escaped pattern.
- **The escape closes the hole.** Same geometry, escape in place: the attacker's
  credential is refused —
  `ERROR 1142 (42000): SELECT command denied to user 'polydbgrant_ne_shop'@'localhost'
  for table \`polydbgrantone_shop\`.\`customers\`` — cannot write, and cannot see the
  victim in `SHOW DATABASES`.
- **It closes only the hole.** Inverse control: the same user still creates a
  table, inserts and selects on its own `polydbgrant_ne_shop`. An escape that had
  also broken the legitimate grant would have been the worse defect.
- **What `%` would do, if the alphabet ever admitted it.** Strictly worse than
  `_`: `ON \`polydbgrant%\`.*` reached a 19-character name, a 20-character name and
  the grantee's own, all from one grant, because `%` is not confined to a single
  length. Escaped (`\%`) it reached nothing but `information_schema`. So
  `grant_pattern_for` is the right and only place a second escaped character goes.

## Could a shipped installation already have been exploited?

The owner will ask this first, so it is answered plainly rather than buried.

**It cannot be determined from this repository, and this note does not pretend
otherwise.** What is certain is only this:

- The defective statement was in the shipped code, so **every Maran installation
  that has ever created a database carries pattern grants in its `mysql.db`**, not
  literal ones. That is a fact about the artefact, not a guess.
- Exploitation requires a second condition beyond the grant: an attacker account
  whose name collides with a victim's under a single-`_` pattern. That needs the
  attacker to choose an account name containing `_` at exactly a position where a
  victim's **same-length** name differs, and the same requested suffix.
- The fix does **not** repair existing grants. `grant_pattern_for` runs at
  `CreateDatabase` time only; a database created before the fix keeps its
  unescaped grant row, and that row is matched at every future connection. **This
  is the most important operational consequence in this note and there is no
  migration for it today.**

What would be needed to answer the question for a given server, and none of it can
be done from here:

1. `SELECT Host, Db, User FROM mysql.db WHERE Db LIKE '%\_%'` on the live server,
   to enumerate the unescaped pattern rows that actually exist.
2. For each such row, whether any **other** database on that host matches the
   pattern — a same-length name differing only where the row holds `_`. This is
   computable from `SHOW DATABASES` plus the row set, and the answer "no other
   name matches" clears that row.
3. Whether any collision found was ever used. `mysql.general_log` or the audit
   plugin would show it and neither is on by default, so on a default install
   **the answer is likely to be unknowable** — the server keeps no record of a
   successful authorised query, and to the server these queries were authorised.
4. Whether the deployment's provisioning path lets a customer choose their own
   account name at all, or whether names are assigned. Where they are assigned
   from a pattern that excludes `_`, exploitation was never reachable.

A remediation an operator can run today, and which this lane has NOT written: for
every row from (1), `REVOKE` it and re-`GRANT` the escaped form. It is a
re-grant, not a data migration, and the inverse control measured above says the
escaped form serves the owner correctly. Whether that belongs in the installer's
upgrade path or in a `maran` subcommand is the owner's decision.

## What a second reviewer must check

1. **The escape form against every shipped server version, not just one.**
   Measured here on MariaDB 10.11.14 (Ubuntu 24.04) only. AlmaLinux 9 ships a
   different MariaDB, and the support matrix names six operating systems. The
   polygon lane runs both families, which is where the rest of the matrix gets
   asked; the AlmaLinux half is listed below as what this lane could not verify.
2. **That `grant_pattern_for` is the only pattern position.** Re-run
   `/usr/bin/grep -rnE "GRANT|REVOKE|LIKE '" agent/crates/ops/src --include=*.rs`
   and confirm nothing new has appeared beside it.
3. **That the geometry of the new polygon test is still a colliding one.** The case
   `a_grant_for_an_account_whose_name_holds_the_separator_cannot_reach_a_same_length_neighbours_database`
   depends on two account-name constants being the same length and differing at one
   position where the attacker holds `_`. It asserts all three before creating
   anything, precisely so a rename cannot quietly restore the geometry in which the
   wildcard is invisible — but a reviewer should confirm that assertion is still
   the FIRST thing the test does.
4. **Whether existing installations get a re-grant.** §"Could a shipped
   installation already have been exploited?" — the fix is forward-only and
   nothing in the tree repairs a grant already issued.
5. **Whether `AccountName` should permit `_` at all.** Not touched here, and
   outside this lane's scope, but it is the upstream question: the separator being
   legal inside the account half is what makes a collision constructible, and
   `SftpUserName` and `FtpsUserName` decode at the same separator.

## What the author could not verify

- **The AlmaLinux 9 family.** Only the Ubuntu 24.04 polygon image was built and
  run. The failure direction if AlmaLinux's MariaDB rejected the escaped form is
  loud rather than silent — five of the seven cases in
  `databases_on_a_real_host.rs` call `create_database`, so all five go red at once
  — but it is unmeasured by this lane.
- **`scripts/test-baseline.txt`.** The new `#[ignore]`d case raises that suite's
  declared count from 6 to 7, and the polygon lane's fifth axis compares
  `passed + failed` against that number. The file is outside this lane's writable
  scope; the row owed is `rust	databases_on_a_real_host [tests/databases_on_a_real_host.rs]	0	0	6`
  (suite grew from a baseline of 6 to 7, one more), reported as prose because
  `scripts/test-baseline.txt` is not this lane's to edit, and **`maran polygon verify` will
  refuse this suite until the row is raised.**
- **Anything about a real deployment.** No shipped installation was examined. See
  the section above for what it would take.
