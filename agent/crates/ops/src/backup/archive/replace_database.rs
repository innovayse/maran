//! Dropping one database and loading a dump in its place — the step that
//! cannot be undone by doing nothing.

use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;

/// Drops `database` and loads the SQL at `from` in its place.
///
/// # The name says `replace`, and that is on purpose
///
/// The plan called this file `load_dump`, and a reader of a call to
/// `load_dump(…)` would not know a `DROP DATABASE` was inside it. **The first
/// `DROP DATABASE` a restore performs is its point of no return** — everything
/// before it is undone by doing nothing, and everything after it is undone only
/// by reloading a dump taken moments earlier. A function whose name hides that
/// step is a function whose call sites stop thinking about it, so the name says
/// what it does and the caller is left in no doubt about which side of the line
/// each call is on.
///
/// # Why there is no `CREATE DATABASE` between the two
///
/// There does not need to be one. The dumps this loads were taken with
/// `--databases`, which is on the create side's argv for exactly this reason:
/// the file carries its own `CREATE DATABASE` and `USE`. An explicit `CREATE`
/// here would be this agent composing DDL of its own alongside DDL it already
/// has, and the two would then have to be kept in step — the second spelling
/// being the one nothing tests.
///
/// # This function takes no view on WHEN it may be called
///
/// It does not take the rollback dump, and it does not check that one exists.
/// That is the operation's decision and belongs where the ordering is visible:
/// the rollback dump of a database is taken on the line immediately before its
/// own drop, per database rather than all up front, and a helper that took it
/// here would hide the ordering that matters most.
///
/// # Errors
///
/// - [`BackupError::DropFailed`] when the database could not be dropped. The
///   database is untouched, so this is still the recoverable side of the line.
/// - [`BackupError::LoadFailed`] when the dump could not be loaded. The
///   database is now gone and the caller owes a rollback.
pub(crate) fn replace_database(
    host: &dyn BackupHost,
    database: &DatabaseName,
    from: &Path,
) -> Result<(), BackupError> {
    host.drop_database(database)?;
    host.load_dump(database, from)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/replace_database_tests.rs"]
mod tests;
