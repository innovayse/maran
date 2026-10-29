//! Chooses between a bound and the backup area's own failure.

use maran_agent_core::utils::spawn_error::SpawnError;

use crate::backup::backup_error::BackupError;

/// Chooses between a bound and the area's own "could not run it" error.
///
/// A deadline or an output cap is [`BackupError::Bound`], carried through by
/// name: it must never become `otherwise`, which says the program ran and
/// refused (or never ran), because a program that was killed has been shown to
/// be neither. Every other way the core can fail to produce an outcome — the
/// program could not be started, its input could not be delivered, it could not
/// be waited for — is `otherwise`, as it was before the core existed.
pub(super) fn bound_or(error: SpawnError, otherwise: BackupError) -> BackupError {
    match error {
        SpawnError::Bound(bound) => BackupError::Bound(bound),
        _ => otherwise,
    }
}

#[cfg(test)]
#[path = "../tests/backup/bound_or_tests.rs"]
mod tests;
