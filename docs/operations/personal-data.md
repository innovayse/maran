# Personal data: what the panel holds, how to export it, and what erasure does

This page is for the operator who has to answer "what do you hold about this person or this hosting
account?" and "delete it". It describes what the panel itself does (issue #146). Its limits are as
much part of the answer as its features.

The second security review of this change is OUTSTANDING (see
`docs/superpowers/notes/2026-11-07-audit-retention-erasure-threat-note.md`); the branch must not merge
to `main` until a reviewer has read it.

## What the panel holds

| Where | What |
|---|---|
| Logins (Identity) | username, e-mail address, role, lifecycle state, creation and last sign-in times; memberships of hosting accounts (account, role, state); membership offers (invitee identifier, outcome, times); sessions (issue and expiry times, address, user agent, revocation time); pinned pages |
| Hosting accounts (Accounts) | name, primary domain, owner e-mail, owner language, status, creation time, plan name, reseller id |
| An account's records in other modules | sites and their hostnames; database and database-user names; FTP and SFTP user names, and SFTP key labels, algorithms and fingerprints; certificate domains and issuers; backup and backup-schedule metadata; scheduled-job run metadata; task metadata |
| The audit journal | who acted (id and name), who was viewing as them, what the action was, its outcome, the subject text, the address and user agent of the request, the account it concerned (when stamped), a correlation id and the time |

Never exported, and never read by an export: password hashes, second-factor secrets, recovery codes,
token hashes, API-key digests, encrypted columns, scheduled-job and task output, and anything in a
home directory. Each module's provider names the fields it exports one by one; a column added to an
entity later is not exported until a provider names it.

## Export

Administrators only; refused while viewing as a customer. Both calls answer with a JSON document as
an attachment (`maran-personal-data-<account|user>-<UTC time>.json`, `Cache-Control: no-store`):

- `GET /api/v1/privacy/accounts/{accountId}/export`
- `GET /api/v1/privacy/users/{userId}/export`

An unknown account or user is a 404. The document is
`{ subject, generatedAt, sections: [{ module, name, records: [...] }], auditEntries: [...] }`.

- **A user export** holds the Identity sections about that person (login, memberships, sessions with
  address and user agent, pinned pages and any membership offers addressed to them) and the
  journal entries where that person is the actor or the viewing operator. No other module answers for
  a user.
- **An account export** holds every module's section for the account, the team (user id, username,
  e-mail, role, state of each member) and the journal entries carrying that account id. It therefore
  contains other people's names and e-mail addresses; removing them before the file goes to a data
  subject is the operator's act, not the panel's.
- **Audit entries** in both exports are capped at 500,000 rows. Above that the call is refused with
  a 400, not truncated. The chain columns (`chainIndex`, `rowHash`) are left out.
- **Rate limit and journal.** The calls share the audit export's rate limit (6 per hour per
  administrator by default) and each writes a `PersonalDataExported` entry naming the kind and the
  subject id.
- **There is no listing of users**, so a user id is found in the `team` section of an account export.
- **Account entries have a coverage gap.** Entries an administrator initiated through the sites,
  databases, FTP, SFTP, cron, backups, certificates, firewall and monitoring journals carry no
  account id, so they are not in an account export and are not matched by the account erasure rule
  below. See the account filter in `audit-journal.md`.
- **The SPA** offers the account export as an administrator action on the account detail page. A
  user export is API only: there is no screen for it.

## Erasure

There is **no endpoint and no button that takes an id and erases it.** Erasure follows from deleting
something, and runs in four places only:

1. deleting a hosting account (for every login removed with it, and for the account's own entries);
2. removing a sub-user whose login row is deleted;
3. deleting a reseller's login;
4. the daily orphaned-actor sweep (below).

It is **pseudonymisation of the audit journal, not deletion of rows.** The journal keeps saying what
happened, when, with what outcome, and which administrator did it; it stops saying who the removed
person was. It happens in the same database transaction as the removal of the logins (see the limit
below), and one summary entry (`PersonalDataErased`, holding the pseudonym and a row count, never the
original id, name or e-mail) is written after the commit.

### The rule for a user

Rows matched: the user is the actor, or the viewing operator, or the subject text **equals** the
user's stored username, e-mail address or id (case-insensitively).

- The actor id becomes a fresh random id, shared by all that user's rows, and the actor name becomes
  `erased-user-<8 hex>`.
- The address and user agent become empty.
- A viewing operator who is the user is replaced by the same pseudonym; when the user was the
  operator in a view-as session, the actor columns of the other, living person are kept.
- A matching subject becomes the same `erased-user-<8 hex>` text.
- Kept: the time, the action, the outcome, the correlation id, the account id, and any subject that
  was not a match.

### The rule for an account

Rows matched: those carrying the account id.

- The subject becomes `erased-account-<8 hex>` and the account id becomes a fresh random id that
  points at no account.
- Kept: the actor columns of the living people who acted. An administrator's accountability for what
  was done to a deleted account is not erased.

### The tombstone keeps the account name

The final `AccountDeleted` entry is written after the cascade with the account **name** as its
subject and no account id. It is the record that an administrator destroyed an account, and without
the name nobody can say which. The name is an identifier that can point at a person or a company, and
it is not erased on request; it leaves the journal only when retention removes the entry, which under
the default (keep forever) is never. This is a default that can be changed.

### The orphaned-actor sweep

The daily maintenance run pseudonymises the rows of any actor id that points at no login: users
deleted before this change existed, and any deletion path that did not erase. It never matches a
missing actor, an empty id or a row already carrying a pseudonym name. It is guarded because its
input is database state:

- it refuses to run when the login table has no row at all;
- it refuses when more than 200 distinct actors would be erased in one run;
- either refusal writes a failed `PersonalDataErased` entry and erases nothing.

Because the sweep re-erases whatever is orphaned, rolling back to a release that did not erase and
upgrading again converges: the rows that release left are pseudonymised on the next daily run.

### What erasure does not do

- **It is pseudonymisation.** One person's rows share one pseudonym, so they remain linkable to each
  other; with the address and user agent emptied they are no longer linked to the person, but the
  pattern of activity remains.
- **A subject that merely mentions the name survives.** Matching is equality: "reset requested for
  alice@example.test" is not equal to "alice@example.test" and is kept. Subject formats of the other
  journals name sites, databases and domains, which the account rule replaces only for entries that
  carry the account id.
- **Administrator-initiated entries with no account id** (the coverage gap above) are not matched by
  the account rule, and their subject may name a site or database of the deleted account.
- **Entries about people who never had a login** (a failed sign-in with an address someone typed)
  are matched only if the typed text equals a deleted user's name or address.
- **The retention of erased rows is the same as any row.** There is no second clock. Under the default
  they are kept forever, pseudonymised.
- **It is logical, not physical.** An update leaves the old row version in the database until it is
  cleaned up, and the write-ahead log, any replica, the installer's pre-update database dump and
  `maran panel-backup` archives keep the data as it was when they were taken. **Restoring a backup
  restores the erased data**, and the users with it. The panel does not purge backups.
- **Atomicity is Identity's only.** The removal of logins and the pseudonymisation of their rows
  commit together; the rest of an account deletion runs in other modules' contexts. A failure after
  Identity committed leaves the logins gone and their rows pseudonymised while the account is still
  alive, and the deletion can be retried. It is not true that an aborted deletion never erases.
- **The erasure is not tamper-evident** (see `audit-journal.md`): the chain cannot tell it from a
  rewrite of the same columns.

## Out of scope

Not erased and not exported, because they are not the panel's records or are held elsewhere:

- files in a home directory, mail, and the contents of customers' databases (customer content);
- scheduled-job and task output;
- the panel's message store, mail the panel sent, its log files, and the root agent's journal lines
  (which can contain account names and domains);
- copies in backups and dumps (above).
