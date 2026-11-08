# Threat note addendum: restoring one database or one site from a local backup (issue #160)

Addendum to `2026-11-07-pre-restore-backup-threat-note.md` and
`2026-11-07-account-restore-proof-threat-note.md`. It adds a scope to the restore the root agent
performs and to the endpoint that drives it, and it adds a second descriptor walk and one rename
sequence that run as the customer. That is the agent's privs surface (rules/security.md "Sensitive
change escalation"), so it is recorded here.

**The second reviewer requirement is OUTSTANDING (#331).** An agent session wrote this and cannot be
that reviewer. The branch MUST NOT merge to `main` until a human has read this note and the diff.

Scope of this change: a LOCAL backup only. No remote destination and no encryption are touched; both
are another change and were left alone. The existing key parameter of `restore_backup` is carried
through unchanged and is not read here.

## The decision: a smaller target, not a smaller ceremony

A restore narrowed to one database or one site is the same replacement with a smaller target. Every
guard of the whole restore applies unchanged: the typed account name, the pre-restore backup (#433,
`PreRestore` is taken whatever the scope), the checksum against the panel's digest, the member
pre-scan, the manifest and sidecar agreement, the account lock, the free-space floor and the task and
audit records. Nothing is relaxed because "only one thing is replaced"; what a database restore
overwrites is just as gone.

## What changed

- **Contract** (`RestoreBackupRequest.scope`, a `RestoreScope` message with a `oneof` of
  `database_name` or `site_domain`). Additive; an absent scope is the whole account, so an older
  panel keeps restoring exactly what it did. No path is ever on the wire. **A contract revision bump
  is needed** (the lead's combined bump).
- **Agent, database scope**: `resolve_one_database` (the name must be in `allowed_databases` and in
  the manifest), `extract_database_as_root` (one member, `databases/<name>.sql`, root-side, checked
  against the manifest digest before any drop), then the existing `replace_databases` over a list of
  one: rollback dump, drop, load, rollback on failure. No file is read, swapped or re-owned.
- **Agent, site scope**: the service derives the directory from a validated `Domain` with
  `SitePaths::document_root_in_home` (the same spelling site creation used), as a `RelativePath`.
  `extract_directory_as_account` extracts the one member `home/<dir>` AS the account into the
  account-owned staging directory with `--strip-components`; `restore_directory` makes a second
  account-owned directory for parking; `swap_directory` (new `BackupHost` method) runs inside
  `fork_as_account` and does the swap by descriptors: `open_parent_of` descends the home with
  `openat` + `O_NOFOLLOW`, requiring every level to be a directory the account owns, then two
  `renameat2(RENAME_NOREPLACE)` calls (live directory into the parking directory, restored tree into
  its place) with an immediate reverse rename if the second fails. The parent types the outcome by
  looking at whether the displaced tree is still parked.
- **Panel**: `RestoreBackupCommand` gains `DatabaseName` and `SiteDomain` (at most one). The handler
  resolves them against the panel's own rows BEFORE the pre-restore backup: the database must be in
  `IAccountDatabaseDirectory.ListNamesAsync(account)`, the site must be found by
  `ISiteDirectory.FindByDomainAsync` AND belong to the backup's account. `GET
  /api/v1/backups/{id}/restore-choices` feeds the dialog (new `ISiteDirectory.ListDomainsAsync`,
  names only). The verdict is stated per scope in `RestoreRunner.IsComplete`. The audit subject names
  the part (`alice/database/alice_shop`, `alice/site/shop.example.com`), never a path.
- **SPA**: a scope picker in the restore dialog; the request carries at most one narrowing field.

## What an attacker could do, and why it is safe now

- **Aim a root restore at a directory of their choosing.** The wire carries a domain, not a path. The
  agent validates it as a `Domain` (shape check; no `/`, `..` or control character is in its
  alphabet), derives `sites/<domain>` itself and re-parses it as a `RelativePath`. The panel checks
  the site is the backup's account's. A test feeds traversal strings and expects `InvalidInput`.
- **Restore another account's database.** The name must decode to the account (`DatabaseName::decode`),
  be in the panel's list for the account, and be named by the manifest. A neighbour's, a system
  database and a name the panel has forgotten are refused; the last is never created.
- **Make the agent write outside the home with root's authority.** Both file steps run as the account
  (extraction in the existing `fork_as_account` path, the swap in a new one). The swap never copies
  and never follows a link: every directory is opened with `O_NOFOLLOW` and an ownership check, and a
  rename that would cross a filesystem fails instead of degrading to a copy. The worst the account can
  do with it is move its own directories, which it could already do.
- **Swap a component between the check and the use.** The walk holds descriptors; a level renamed or
  replaced after it was opened redirects nothing. The staging and parking directories are opened by
  path, but their parent is the root-owned `0711` staging root, so the account cannot rename them.
- **Make a hostile archive place the directory elsewhere.** The member name is built from the validated
  path, passed after `--` and with `--strip-components` (a count, never a transform), no
  `--absolute-names`; the whole-archive member pre-scan still runs first and still refuses any member
  outside the layout. A symlink member at the site's own name is moved into place as a link inside the
  customer's home, as the customer's own backup content; root never follows it.
- **Load a doctored dump as the database superuser.** Unchanged argument: the dump goes to the
  root-only scratch and is compared with the manifest digest before the first drop. Extracting one
  member means the other databases' dumps never reach the scratch.
- **Use the endpoint to enumerate.** `restore-choices` is a 404 for another account's backup, authorises
  the account before listing, and returns names the caller can already see.

## Stated limits (what the author could not verify)

- **Not run.** No build, test, lint, structure or mutation run was made by the author; the lead's gate
  does that. Every unit test named in the report is written and unrun.
- **No polygon test.** The rename sequence is proved against real directories as the current user, not
  on AlmaLinux or Ubuntu as a dropped customer uid. A polygon case (a site restored as a real account,
  and the swap with a symlink planted in the parent chain) is owed, and adding one changes the declared
  count in `scripts/test-baseline.txt`.
- **A second descent.** `ops::files::open_parent_directory` is private to the file manager on purpose
  and security.md item 2 names it as the one walk. `open_restore_directory.rs` is a second, smaller walk
  that only renames. A reviewer should decide whether it should instead move to `agent-core::privs` and
  be shared; it was kept local rather than widening another area's seam.
- **No marker, no automatic recovery for the site swap.** The whole-home swap writes a marker so a
  killed agent can be finished at start. This swap has none. A kill between its two renames leaves the
  directory missing and the old tree in `<staging root>/<account>.previous.<id>/previous`, which the
  start-up reconciliation leaves alone (an unmarked entry). The window is microseconds; the net is the
  pre-restore backup. A retry of the same restore removes that parking directory first, so an operator
  who wants the parked tree must take it before retrying.
- **The panel cannot tell a missing part from a missing artifact.** The agent answers NOT_FOUND for
  both; the sentence a customer reads says both. A site cannot be pre-checked by the panel at all (the
  manifest lists databases, not sites).
- **The choices are the account's current databases and sites,** not what the archive holds.
- **A database restore does not check the home filesystem's free-space floor,** because it writes
  nothing there; the scratch's room is asked before the first drop as for a whole restore.
- Everything in the whole restore's own note about what a restore does not undo (vhosts, certificates,
  crontab, firewall, SFTP logins) is unchanged.

## What a reviewer must check

1. `validated_scope.rs`: the only way a `RestoreScope::Directory` is built from the wire is
   `SitePaths::document_root_in_home(&Domain::parse_existing(..))`.
2. `ExtractSpec::member` and `stripped_components` for `Directory`: the member is `home/` plus the
   components, and the strip count is `1 + parent components` (`extract_spec_tests`).
3. `swap_directory_as_account.rs`: every directory reached by `open_owned_directory`/`open_parent_of`,
   `RENAME_NOREPLACE` on both renames, the reverse rename on failure, and that nothing outside the three
   held directories is touched. `ProcessBackupHost::swap_directory` runs it inside `fork_as_account`
   and nowhere else.
4. `restore_directory.rs`: the parking directory is separate from the staging one, and is not removed
   when the error says the tree is parked there.
5. `RestoreBackupCommandHandler`: no path from the confirmation to `_runner.RunAsync` skips the
   pre-restore backup (#433), and a name the panel does not own is refused before it.
6. `RestoreRunner.IsComplete`: a database restore is whole only for `(false, 1, 1)` and a site restore
   only for `(true, 0, 0)`.
