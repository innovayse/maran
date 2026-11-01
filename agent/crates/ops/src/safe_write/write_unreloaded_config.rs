//! The config-write protocol for a file nothing has to be told about.

use crate::safe_write::model::Validator;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::write_config_set::swap_set;
use crate::safe_write::{ConfigHost, SafeWriteError};

/// Writes `file` through the protocol — temporary file at its final mode beside
/// the target, `fsync` of the file and its directory, atomic rename, validation,
/// rollback on refusal — and stops there, because its reader re-opens it on
/// every use.
///
/// The case that needs it is an SFTP login's key file (issue #135): sshd opens
/// `AuthorizedKeysFile` afresh at each authentication, so the renamed file is
/// live the moment it is in place and there is no daemon to reload. Passing a
/// command that does nothing in the reload's place would make "reload" mean two
/// things; this entry says instead that there is none.
///
/// Extends `safe_write` rather than being written inside the area that needed
/// it (rules/rust.md "Config writes": *an area that needs a variation on this
/// protocol extends `safe_write` — it does not write its own copy*), and shares
/// [`super::write_config_set::write_config_set`]'s body so the two cannot drift.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] or [`SafeWriteError::Sync`] when
/// the temporary file cannot be written, have its mode set or be flushed — the
/// target is untouched. Returns [`SafeWriteError::Rename`] when the swap fails,
/// [`SafeWriteError::ValidationFailed`] when the validator refuses the swapped-in
/// file, [`SafeWriteError::SpawnFailed`] when it could not be started and
/// [`SafeWriteError::Bound`] when it outlived its deadline — each with the
/// previous file restored (or the new one removed, if there was none) — and
/// [`SafeWriteError::RollbackFailed`] when that restoration also failed.
pub fn write_unreloaded_config(
    host: &dyn ConfigHost,
    file: &ConfigFile<'_>,
    validator: &Validator<'_>,
) -> Result<(), SafeWriteError> {
    swap_set(host, std::slice::from_ref(file), validator, None)
}

#[cfg(test)]
#[path = "../tests/safe_write/write_unreloaded_config_tests.rs"]
mod tests;
