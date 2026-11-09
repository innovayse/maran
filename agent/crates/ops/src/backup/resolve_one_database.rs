//! The single-database restore's pairing of the one wanted database with the
//! manifest and with the names the panel knows.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::manifest_database::ManifestDatabase;

/// Pairs `wanted` with the manifest entry that describes its dump, after proving
/// the panel still knows the database (issue #160).
///
/// Two refusals, in this order, and they are different findings:
///
/// - **A database outside `allowed` is refused whatever the archive holds.** The
///   panel's own rows decide which databases exist; restoring one the panel has
///   forgotten would resurrect it with no row saying who owns it, exactly the
///   orphan [`resolve_databases`](crate::backup::resolve_databases) refuses to
///   create. The request's name is also what reaches the dump client's argv, so
///   the returned name is the panel-supplied [`DatabaseName`] and nothing that
///   came out of the archive.
/// - **A database the manifest does not list is a finding about the backup**,
///   not about the request: the database was created after the backup was
///   taken. Nothing is created and nothing is dropped.
///
/// # Errors
///
/// [`BackupError::UnknownDatabase`] for a name outside `allowed` (a validated
/// [`DatabaseName`], so its text is safe to carry), and [`BackupError::DatabaseNotInBackup`]
/// for a known database the manifest does not name.
pub(super) fn resolve_one_database(
    manifest: &BackupManifest,
    allowed: &[DatabaseName],
    wanted: &DatabaseName,
) -> Result<(DatabaseName, ManifestDatabase), BackupError> {
    let known = allowed.iter().find(|name| *name == wanted).ok_or_else(|| {
        BackupError::UnknownDatabase {
            name: wanted.as_str().to_owned(),
        }
    })?;

    let entry = manifest
        .databases
        .iter()
        .find(|entry| entry.name == known.as_str())
        .ok_or(BackupError::DatabaseNotInBackup)?;

    Ok((known.clone(), entry.clone()))
}

#[cfg(test)]
#[path = "../tests/backup/resolve_one_database_tests.rs"]
mod tests;
