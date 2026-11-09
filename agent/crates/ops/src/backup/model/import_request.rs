//! What one import carries, revalidated.

use crate::backup::package::signed_manifest::SignedManifest;

/// The header of an import: the signed manifest, the fingerprint the operator
/// confirmed and the caller's size bound.
///
/// Built by the service layer from the first message of the stream. Nothing in it
/// is trusted yet: [`import_backup`](crate::backup::import_backup) verifies the
/// signature and the fingerprint before it touches anything.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImportRequest {
    /// The signed manifest, exactly as the exporting host produced it.
    pub signed: SignedManifest,
    /// The fingerprint of the signer's key the operator confirmed: lowercase
    /// hexadecimal SHA-256 of the public key, 64 characters.
    pub expected_fingerprint: String,
    /// The most artifact bytes the caller will receive, or 0 for the agent's own
    /// ceiling. The smaller of the two applies.
    pub max_bytes: u64,
}
