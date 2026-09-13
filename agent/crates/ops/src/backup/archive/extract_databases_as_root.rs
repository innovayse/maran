//! Extracting the archive's SQL dumps into the root-only scratch, and checking
//! each one against the manifest before anything loads it.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::dump_database::dump_path;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::extract_spec::ExtractSpec;
use crate::backup::model::manifest_database::ManifestDatabase;

/// The scratch's directory of dumps — the archive's `databases/` member.
const DATABASES_DIRECTORY: &str = "databases";

/// Extracts `databases/` into the root-only scratch and verifies every dump the
/// restore intends to load.
///
/// # Why root, when the home is extracted as the account
///
/// The asymmetry is deliberate and it is one rule (R4), not two decisions. The
/// loader that reads these files connects to the database server as
/// `root@localhost` — the database superuser — so whatever is in the file it
/// reads is executed with that authority. A dump staged anywhere an account can
/// write is a dump the account can replace between the extraction and the load,
/// and arbitrary SQL as the superuser is a `FILE` privilege away from reading
/// `/etc/shadow` into a table. Root-only staging closes that window by removing
/// the space it needs.
///
/// The home is the opposite case and is extracted as the account for the
/// opposite reason: nothing about the home is executed with anybody's
/// authority, and running the extraction unprivileged converts "root writes
/// wherever the archive says" into "the account writes where the account
/// already could".
///
/// # The checksum here is not a formality
///
/// Each dump is hashed after extraction and compared against the digest the
/// manifest recorded when the backup was taken, **before the first
/// `DROP DATABASE`**. That ordering is the whole value: after the drop there is
/// nothing to refuse into, and a corrupted dump discovered then costs a
/// customer their database. The digest is SHA-256 because this comparison is
/// what decides whether SQL runs as the superuser, which makes it
/// security-relevant and rules out MD5 and SHA1 (rules/security.md item 9).
///
/// Only the databases the restore will actually load are checked. A manifest
/// entry the panel has refused (`allowed_databases`) is never loaded, so
/// hashing it would be work whose result nothing reads.
///
/// # Errors
///
/// - [`BackupError::ArchiveFailed`] when the member cannot be extracted.
/// - [`BackupError::ChecksumUnreadable`] when an expected dump is not there or
///   cannot be read to the end.
/// - [`BackupError::DumpChecksumMismatch`] naming the database whose dump is
///   not the one this backup wrote.
pub(crate) fn extract_databases_as_root(
    host: &dyn BackupHost,
    artifact: &Path,
    scratch: &Path,
    expected: &[(DatabaseName, ManifestDatabase)],
) -> Result<(), BackupError> {
    host.extract(&ExtractSpec {
        artifact: artifact.to_path_buf(),
        into: scratch.to_path_buf(),
        part: ArchivePart::Databases,
    })?;

    for (name, recorded) in expected {
        let (sha256, _bytes) = checksum_file(&extracted_dump_path(scratch, name))?;
        if sha256 != recorded.sha256 {
            return Err(BackupError::DumpChecksumMismatch {
                database: name.as_str().to_owned(),
            });
        }
    }

    Ok(())
}

/// Where one database's dump lands once the `databases/` member is extracted.
///
/// Built from the same helper the create side names its dumps with, so that
/// "what is a dump called" keeps one answer across the two halves of the
/// contract rather than two spellings that drift.
pub(crate) fn extracted_dump_path(scratch: &Path, database: &DatabaseName) -> PathBuf {
    dump_path(&scratch.join(DATABASES_DIRECTORY), database)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/extract_databases_as_root_tests.rs"]
mod tests;
