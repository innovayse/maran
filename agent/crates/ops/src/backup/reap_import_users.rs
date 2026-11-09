//! Dropping the temporary import users an interrupted import left behind.

use crate::backup::backup_error::BackupError;
use crate::backup::restricted_dump_host::RestrictedDumpHost;

/// Drops every temporary import user the server still holds, and answers how
/// many it dropped.
///
/// # Why this exists
///
/// `load_dump_restricted` creates a user that holds every privilege on one
/// database and drops it on every path it controls. An agent killed between the
/// creation and the drop controls nothing: the user, and its password, stay on
/// the server for good. The user's name is the prefix `maran_imp_` and sixteen
/// hex digits, so this pass finds them by that exact shape and nothing else
/// (`ImportUser::from_listed`); the agent keeps no record of its own.
///
/// # What it drops, and what it leaves
///
/// Only names the host lists AND `ImportUser::from_listed` accepts, dropped
/// through the same `drop_import_user` the loader uses, which names the user
/// through the validated type. A user of any other name is never touched, and a
/// listing that fails drops NOTHING: a user the pass cannot see is not a user
/// that is gone.
///
/// A drop that fails does not stop the others: every leftover is attempted, and
/// the first failure is returned once all were. Users dropped before the
/// failure stay dropped, and a repeat converges.
///
/// # Concurrency
///
/// Meant to run at start-up, before the first connection is accepted, when no
/// import is running in this process. A user created by an import that is in
/// flight would be dropped from under it; the load then fails and the import is
/// refused, which fails closed.
///
/// # Errors
///
/// - [`BackupError::ImportUsersUnlistable`] and [`BackupError::Bound`] when the
///   users could not be listed; nothing was dropped.
/// - [`BackupError::DropFailed`] and [`BackupError::Bound`] for the first user
///   that could not be dropped.
pub fn reap_import_users(host: &dyn RestrictedDumpHost) -> Result<usize, BackupError> {
    let leftovers = host.leftover_import_users()?;

    let mut dropped = 0;
    let mut first_failure = None;
    for user in &leftovers {
        match host.drop_import_user(user) {
            Ok(()) => dropped += 1,
            Err(error) => {
                tracing::error!(
                    %error,
                    "a temporary import user left behind could not be dropped"
                );
                first_failure.get_or_insert(error);
            }
        }
    }

    match first_failure {
        Some(error) => Err(error),
        None => Ok(dropped),
    }
}

#[cfg(test)]
#[path = "../tests/backup/reap_import_users_tests.rs"]
mod tests;
