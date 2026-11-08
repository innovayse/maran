//! The document an exporting host signs.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;
use serde::{Deserialize, Serialize};

use crate::backup::BackupError;
use crate::backup::verify_backup::is_sha256_hex;

/// The manifest version this agent writes and the only one it reads.
pub const TRANSFER_MANIFEST_VERSION: u32 = 1;

/// The most database names a manifest may carry.
///
/// A bound on an attacker-controlled list: far above anything an account owns,
/// far below a size that costs memory.
pub const MAXIMUM_DATABASES: usize = 512;

/// The most bytes of manifest JSON the importer will parse.
pub const MAXIMUM_MANIFEST_BYTES: usize = 1024 * 1024;

/// What the exporting host claims about the artifact it sends.
///
/// Read strictly: an unknown or duplicated field is a refusal (`deny_unknown_fields`
/// and serde's own duplicate-field error), so a field one side invented cannot be
/// smuggled past the other.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct TransferManifest {
    /// Always [`TRANSFER_MANIFEST_VERSION`].
    pub version: u32,
    /// The system user name of the account the backup belongs to.
    pub account: String,
    /// The backup's id on the exporting host; the importing host publishes it
    /// under the same id.
    pub backup_id: String,
    /// The artifact's size in bytes.
    pub artifact_bytes: u64,
    /// The artifact's lowercase hexadecimal SHA-256.
    pub artifact_sha256: String,
    /// The measured size of the account's home when it was archived.
    pub home_bytes: u64,
    /// The names of the databases the artifact holds a dump of.
    pub databases: Vec<String>,
    /// When the backup was made, in unix seconds.
    pub created_at_unix: i64,
    /// The exporting agent's version, for the operator's eyes only.
    pub agent_version: String,
    /// The fingerprint of the key that signed this document.
    pub signer_fingerprint: String,
}

impl TransferManifest {
    /// Reads and judges every field a peer chose, and answers the validated names.
    ///
    /// A value read out of a manifest is used for nothing before this has
    /// accepted it: the account and the backup id become paths, the digest is
    /// compared with one computed here, and each database name is decoded against
    /// the account so a name no account could own is refused.
    ///
    /// # Errors
    ///
    /// [`BackupError::PackageManifestMalformed`] for an unknown version, a name
    /// that does not parse, a digest that is not 64 lowercase hexadecimal
    /// characters, an empty artifact, or more than [`MAXIMUM_DATABASES`] names.
    pub fn validated(&self) -> Result<(AccountName, BackupId), BackupError> {
        if self.version != TRANSFER_MANIFEST_VERSION
            || self.artifact_bytes == 0
            || self.databases.len() > MAXIMUM_DATABASES
            || !is_sha256_hex(&self.artifact_sha256)
            || !is_sha256_hex(&self.signer_fingerprint)
        {
            return Err(BackupError::PackageManifestMalformed);
        }

        let account =
            AccountName::parse(&self.account).map_err(|_| BackupError::PackageManifestMalformed)?;
        let backup_id =
            BackupId::parse(&self.backup_id).map_err(|_| BackupError::PackageManifestMalformed)?;
        for name in &self.databases {
            if DatabaseName::decode(&account, name).is_none() {
                return Err(BackupError::PackageManifestMalformed);
            }
        }

        Ok((account, backup_id))
    }
}

#[cfg(test)]
#[path = "../../tests/backup/package/transfer_manifest_tests.rs"]
mod tests;
