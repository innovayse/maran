//! Dropping one database and loading a dump in its place — the step that
//! cannot be undone by doing nothing.

use std::fs::File;
use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::backup_host::BackupHost;
use crate::backup::load_dump_restricted::load_dump_restricted;
use crate::backup::model::dump_origin::DumpOrigin;

/// Drops `database` and loads the SQL at `from` in its place, as the database
/// superuser for this host's own dumps and through the restricted loader for
/// someone else's.
///
/// # `origin` says whose text `from` is, and is never defaulted
///
/// [`DumpOrigin::Own`] is the host's own output, loaded by the superuser client
/// ([`BackupHost::load_dump`]): the panel's own rollback dumps, and the dumps of
/// an archive this host made. [`DumpOrigin::Imported`] is an archive's dump from
/// another host, which goes to
/// [`load_dump_restricted`](crate::backup::load_dump_restricted) and nowhere
/// else: a dump the restricted loader cannot load is a failed restore, never a
/// load with more privilege. The file is opened BEFORE the drop on that path,
/// so a dump that cannot even be read is refused on the recoverable side of the
/// line.
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
///   database is now gone and the caller owes a rollback. For an imported dump
///   that cannot be opened, the same error is returned before the drop.
/// - [`BackupError::RestrictedLoadRefused`] when an imported dump is refused by
///   the restricted loader's scan. The scan runs again here, behind the one
///   `prepare_restore` ran before the first drop, so reaching it means the file
///   changed in between; the database is gone and the caller owes a rollback.
/// - [`BackupError::Bound`] when either client run was killed at its deadline.
///   Nothing says how far the server got, so the caller treats the database as
///   possibly dropped and owes the same rollback it owes a failed load.
pub(crate) fn replace_database(
    host: &dyn BackupHost,
    database: &DatabaseName,
    from: &Path,
    origin: DumpOrigin,
) -> Result<(), BackupError> {
    match origin {
        DumpOrigin::Own => {
            host.drop_database(database)?;
            host.load_dump(database, from)
        }
        DumpOrigin::Imported => {
            let dump = File::open(from).map_err(|_| BackupError::LoadFailed {
                status: PROGRAM_UNAVAILABLE,
            })?;
            host.drop_database(database)?;
            load_dump_restricted(host.restricted_loader(), database, dump)
        }
    }
}

#[cfg(test)]
#[path = "../../tests/backup/archive/replace_database_tests.rs"]
mod tests;
