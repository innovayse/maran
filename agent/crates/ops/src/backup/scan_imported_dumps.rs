//! The refusal of an imported archive's dumps, made before anything is dropped.

use std::fs::File;
use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::extract_databases_as_root::extracted_dump_path;
use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::scan_dump::scan_dump;

/// Scans every dump an imported archive supplied for a restore, refusing the
/// restore if any of them holds what a dump of its database must not.
///
/// This is the half of the restricted load that can be done early. The loader
/// scans a dump again when it loads it, but by then the restore has dropped the
/// databases before it, and a refusal there would be a rollback; here it is a
/// restore that never began, with the account exactly as it was. The scan is
/// of the file the loader will later be handed, the digest-checked extraction in
/// the root-only scratch.
///
/// Reading a dump twice costs one more pass at the speed of the disk, and buys
/// that a refused dump is found before the first drop.
///
/// # Errors
///
/// - [`BackupError::RestrictedLoadRefused`] when a dump holds something the scan
///   refuses.
/// - [`BackupError::LoadFailed`] when an extracted dump cannot be opened or read.
pub(super) fn scan_imported_dumps(
    scratch: &Path,
    databases: &[(DatabaseName, ManifestDatabase)],
) -> Result<(), BackupError> {
    for (name, _entry) in databases {
        let mut dump = File::open(extracted_dump_path(scratch, name)).map_err(|_| {
            BackupError::LoadFailed {
                status: PROGRAM_UNAVAILABLE,
            }
        })?;
        scan_dump(&mut dump, name)?;
    }

    Ok(())
}
