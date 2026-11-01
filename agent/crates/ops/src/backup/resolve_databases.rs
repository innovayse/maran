//! The restore's pairing of manifest databases with the names the panel knows.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::scan_members::refused_member_name;
use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::manifest_database::ManifestDatabase;

/// Pairs every database the manifest names with the validated name the panel
/// still knows it by.
///
/// **`allowed_databases` is the panel's decision, carried on the request, and a
/// manifest entry that is not in it is REFUSED — never created.** The panel is
/// the only thing that knows which databases still exist as far as the product
/// is concerned; the archive knows only what was true when it was written.
/// Creating a database the panel has forgotten would resurrect it on the server
/// with no row anywhere saying who owns it or who may reach it — an orphan
/// owned by a user nothing points at, invisible to every screen and to every
/// later cleanup, holding a customer's data.
///
/// Refusing the whole restore rather than skipping the entry is the same
/// judgement the member pre-scan makes: an archive whose contents this panel
/// cannot account for is one it does not understand, and quietly restoring the
/// part it recognised is how a customer is told a restore succeeded when a
/// database is missing from it.
///
/// The pairing also does the work of validation: the returned name is a
/// [`DatabaseName`] the panel supplied, so nothing that reaches the dump
/// client's argv or a dump file's path came out of the archive.
///
/// # Errors
///
/// [`BackupError::UnknownDatabase`], naming the first entry the panel does not
/// know. The name is escaped on the way in, because a manifest is archive
/// content like any other member.
pub(super) fn resolve_databases(
    manifest: &BackupManifest,
    allowed: &[DatabaseName],
) -> Result<Vec<(DatabaseName, ManifestDatabase)>, BackupError> {
    let mut resolved = Vec::with_capacity(manifest.databases.len());
    for entry in &manifest.databases {
        let known = allowed
            .iter()
            .find(|name| name.as_str() == entry.name)
            .ok_or_else(|| BackupError::UnknownDatabase {
                name: refused_member_name(&entry.name),
            })?;
        resolved.push((known.clone(), entry.clone()));
    }

    Ok(resolved)
}
