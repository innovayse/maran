//! Loads a dump someone else wrote, as a user that holds one database.

use std::fs::File;
use std::io::{Seek as _, SeekFrom};

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::model::import_user::{ImportUser, NAME_RANDOM_BYTES, PASSWORD_RANDOM_BYTES};
use crate::backup::restricted_dump_host::RestrictedDumpHost;
use crate::backup::scan_dump::scan_dump;

/// Loads `dump` into `database` without ever giving it more privilege than that
/// one database.
///
/// The existing restore loads a dump as the database superuser. That is safe
/// only while the dump is this agent's own output; for an imported artifact it
/// is not, because `GRANT ... ON *.*`, `SELECT ... INTO OUTFILE` or a
/// `USE other_account_db` would run with every privilege there is. This is the
/// path an imported dump takes instead, and the one rule of it is that a dump
/// which cannot be loaded here is REFUSED, never loaded with more.
///
/// In order:
///
/// 0. The descriptor is rewound, so it is read from its first byte whatever the
///    caller left it at.
/// 1. The dump is scanned (`scan_dump`). A dump whose text holds a client
///    command, a statement a dump does not carry, a definer, a trigger, routine,
///    event or view, or another database's name is refused here, before the
///    server has heard of it.
/// 2. A temporary user is created with every privilege on `database` only.
/// 3. The dump is loaded as that user, from the descriptor that was scanned.
/// 4. The user is dropped, on every path: after a refused creation, after a
///    failed or killed load, and after a good one.
///
/// If the load failed and so did the drop, the load's error is returned and the
/// drop's is logged; if the load succeeded and the drop failed, the drop's error
/// is returned, because a user holding a whole database must not be left behind
/// silently. A user left behind by an agent that was killed
/// between steps 2 and 4 has the prefix `maran_imp_`; `reap_import_users`
/// drops it when the agent next starts.
///
/// **Cancellation class: run to completion.** The function holds a blocking
/// thread and has no safe midpoint. It reclaims everything it made.
///
/// Fidelity cost, stated: no view, trigger, procedure, function or event is
/// imported. Each would be created owned by a user that no longer exists. Whether
/// a dump with such objects is imported with them left out is the owner's
/// decision (the plan's D4); until it is made they are refused.
///
/// # Errors
///
/// - [`BackupError::RestrictedLoadRefused`] when the scan refused the dump.
/// - [`BackupError::LoadFailed`] when the dump cannot be read, no randomness is
///   available, or the server refuses a statement of it.
/// - [`BackupError::DropFailed`] when the load went through and the temporary
///   user could not be dropped.
/// - [`BackupError::Bound`] when a client was killed at its deadline.
pub fn load_dump_restricted(
    host: &dyn RestrictedDumpHost,
    database: &DatabaseName,
    mut dump: File,
) -> Result<(), BackupError> {
    let unreadable = || BackupError::LoadFailed {
        status: PROGRAM_UNAVAILABLE,
    };
    dump.seek(SeekFrom::Start(0)).map_err(|_| unreadable())?;
    scan_dump(&mut dump, database)?;
    dump.seek(SeekFrom::Start(0)).map_err(|_| unreadable())?;

    let mut name_bytes = [0_u8; NAME_RANDOM_BYTES];
    let mut password_bytes = [0_u8; PASSWORD_RANDOM_BYTES];
    getrandom::fill(&mut name_bytes).map_err(|_| BackupError::LoadFailed {
        status: PROGRAM_UNAVAILABLE,
    })?;
    getrandom::fill(&mut password_bytes).map_err(|_| BackupError::LoadFailed {
        status: PROGRAM_UNAVAILABLE,
    })?;
    let user = ImportUser::from_random(&name_bytes);
    let password = ImportUser::password_from_random(&password_bytes);

    let loaded = host
        .create_import_user(&user, &password, database)
        .and_then(|()| host.load_dump_as(&user, &password, dump));
    let dropped = host.drop_import_user(&user);

    match (loaded, dropped) {
        (Err(error), Err(drop_error)) => {
            tracing::error!(
                error = %drop_error,
                "the temporary import user could not be dropped after a failed load"
            );
            Err(error)
        }
        (Err(error), Ok(())) => Err(error),
        (Ok(()), Err(drop_error)) => Err(drop_error),
        (Ok(()), Ok(())) => Ok(()),
    }
}

#[cfg(test)]
#[path = "../tests/backup/load_dump_restricted_tests.rs"]
mod tests;
