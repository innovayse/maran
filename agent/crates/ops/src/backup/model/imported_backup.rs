//! What a published import is.

/// The result of a successful import.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImportedBackup {
    /// The system user name of the account the backup belongs to, from the
    /// verified manifest.
    pub account: String,
    /// The id the artifact is published under: the source's backup id.
    pub backup_id: String,
    /// The artifact's size in bytes.
    pub artifact_bytes: u64,
    /// The artifact's lowercase hexadecimal SHA-256.
    pub artifact_sha256: String,
    /// How many database dumps the artifact holds.
    pub database_count: u32,
    /// Whether this very artifact (same id, same digest) was already published,
    /// in which case nothing was written.
    pub already_present: bool,
}
