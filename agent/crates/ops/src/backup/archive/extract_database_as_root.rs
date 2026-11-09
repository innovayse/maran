//! Extracting ONE database's dump into the root-only scratch, and checking it
//! against the manifest before anything loads it.

use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::extract_databases_as_root::verify_extracted_dump;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::extract_spec::ExtractSpec;
use crate::backup::model::manifest_database::ManifestDatabase;

/// Extracts `databases/<database>.sql` alone into the root-only scratch and
/// verifies it against the digest the manifest recorded (issue #160).
///
/// **Everything `extract_databases_as_root` argues for holds here unchanged**:
/// root-side, because the loader connects as the database superuser, and
/// checked BEFORE the first `DROP DATABASE`, because after the drop there is
/// nothing to refuse into. What is different is only the selection. The
/// extraction names one member, derived from a [`DatabaseName`] the panel
/// supplied, so the other databases' dumps are neither unpacked into the
/// scratch nor read — a single-database restore costs the disk and the time of
/// one dump, and a dump the restore will never load is never written where the
/// loader's account could reach it.
///
/// # Errors
///
/// As [`extract_databases_as_root`](crate::backup::archive::extract_databases_as_root::extract_databases_as_root):
/// [`BackupError::ArchiveFailed`], [`BackupError::Bound`],
/// [`BackupError::ChecksumUnreadable`] and [`BackupError::DumpChecksumMismatch`].
pub(crate) fn extract_database_as_root(
    host: &dyn BackupHost,
    artifact: &Path,
    scratch: &Path,
    database: &DatabaseName,
    recorded: &ManifestDatabase,
) -> Result<(), BackupError> {
    host.extract(&ExtractSpec {
        artifact: artifact.to_path_buf(),
        into: scratch.to_path_buf(),
        part: ArchivePart::Database {
            database: database.clone(),
        },
    })?;

    verify_extracted_dump(scratch, database, recorded)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/extract_database_as_root_tests.rs"]
mod tests;
