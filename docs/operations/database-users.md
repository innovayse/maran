# Database users and privileges

A customer can create additional MySQL logins for their own account and choose which of their own
databases each login may touch, and with which privileges. Every login connects from the server
itself only; nothing here opens a database login to a network address.

## Model

- A **database user** is one login owned by an account, named `<account>_<name>` on the server. A
  database's **dedicated user** (the one created with it) is a database user like any other: it is
  listed, can have its privileges edited and its password reset.
- A **grant** is what one user holds on one database. A user has at most one grant per database, and
  holding nothing on a database is having no grant for it. A grant is always scoped to a single
  database; there is no global scope.
- *Dedicated* is derived, never stored: a user is a database's dedicated user when that database's
  user name is this user's name. A dedicated user cannot be dropped on its own (`409`); it is dropped
  with its database, and so are all its grants.
- The panel stores no password of any kind. A password is minted when a user is created or reset,
  shown once, and the server's own hash is the only copy. A lost password is replaced, never shown.

## Privileges

Eighteen database-level privileges can be granted: SELECT, INSERT, UPDATE, DELETE, CREATE, DROP,
ALTER, INDEX, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES, CREATE VIEW, SHOW VIEW, CREATE
ROUTINE, ALTER ROUTINE, EXECUTE, TRIGGER and EVENT. GRANT OPTION, FILE, PROCESS, SUPER, RELOAD,
SHUTDOWN, REPLICATION and every global privilege cannot be named by a request, by the panel's types
or by the agent's contract, so they can never be granted.

Three presets fill the boxes on the screen: *Read only* (SELECT, SHOW VIEW), *Read and write*
(SELECT, INSERT, UPDATE, DELETE, LOCK TABLES, SHOW VIEW, EXECUTE) and *Full access* (all eighteen). A
preset is a convenience: the grant stores the privileges, not the preset, so changing a preset
changes nothing already granted. The list and the presets are served by
`GET /api/v1/database-users/privilege-presets`, localized; the browser holds neither.

## Limits

An account may hold at most 50 additional users, not counting the dedicated user of each database
(those are bounded by the plan's database allowance). The count is re-taken under a per-account lock
when the row is written, so two simultaneous creations cannot both take the last slot.

## Endpoints

| Method and route | Meaning |
|---|---|
| `GET /api/v1/database-users` | One page of users (filter `accountId`, `search`, sort `name` or `createdAt`). |
| `GET /api/v1/database-users/privilege-presets` | The privileges and the presets over them. |
| `GET /api/v1/database-users/{id}` | One user with the databases a grant may still be added on. |
| `POST /api/v1/database-users` | Creates a user holding nothing and returns the password once. |
| `POST /api/v1/database-users/{id}/password` | Gives the user a new password and returns it once. |
| `PUT /api/v1/database-users/{id}/grants/{databaseId}` | Makes the user's privileges on that database exactly the set sent; an empty set removes the grant. |
| `DELETE /api/v1/database-users/{id}` | Drops an additional user. A dedicated user answers `409`. |

Another account's user or database answers `404`, never `403`. Every mutation is refused while an
operator is viewing the panel as the customer. Every mutation is recorded in the audit journal
(`DatabaseUserCreated`, `DatabaseUserDropped`, `DatabaseUserPasswordReset`,
`DatabasePrivilegesChanged`), naming the user and the database and never a password or a privilege.

## Existing databases

Databases created before this feature have no user row. A start-up pass writes the dedicated user of
every such database, holding the full-access grant the server already gave it, and runs on every
start, so a rollback and roll-forward through a release that creates databases without user rows is
repaired by the next start.

## What a restore does to users

A restore reloads database contents and does not touch the server's grant table, so users and their
grants survive it. This has not been verified on a real host.
