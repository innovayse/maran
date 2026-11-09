//! The sidecar a verification holds an artifact to.

use std::path::Path;

/// The sidecar file of one stored artifact, and the digest of the artifact's
/// stored bytes taken for this verification.
///
/// The two travel together because they are used together: the manifest read
/// out of the archive is compared with the sidecar, and the sidecar's digest is
/// compared with the one just computed. Handing them over as one value keeps a
/// digest from being passed beside a different artifact's sidecar.
#[derive(Debug, Clone, Copy)]
pub(crate) struct SidecarClaim<'a> {
    /// The sidecar file beside the artifact.
    pub sidecar: &'a Path,

    /// The SHA-256 of the stored artifact's bytes, lowercase hex, as measured
    /// by this verification and not as the sidecar records it.
    pub artifact_sha256: &'a str,
}
