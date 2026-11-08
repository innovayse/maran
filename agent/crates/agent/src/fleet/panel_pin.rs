//! The one panel certificate a node admits.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;

use super::certificate_bytes::certificate_bytes;
use super::fleet_identity_error::FleetIdentityError;
use super::read_identity_file::read_identity_file;
use super::require_private_directory::require_private_directory;

/// The most a pin file may hold; see `node_identity` for the sizing.
const MAX_PIN_FILE_BYTES: u64 = 32 * 1024;

/// The pinned panel certificate: the node's entire access list.
///
/// There is no certificate authority in fleet mode. A node admits exactly one client, the panel
/// whose certificate sits in `panel.pem`, and the listener's trust anchor and its per-request
/// guard are both built from this value. Removing the file removes the panel's access, which is
/// how a node is detached from a panel (`maran node leave`).
///
/// Holds the certificate as PEM (what the TLS configuration is built from) and its fingerprint
/// (what the per-request guard compares the presented certificate against).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PanelPin {
    /// The pinned certificate, PEM, exactly one block.
    pub certificate_pem: String,
    /// The SHA-256 fingerprint of that certificate's DER encoding.
    pub fingerprint: CertificateFingerprint,
}

impl PanelPin {
    /// Reads `panel.pem` from `directory`.
    ///
    /// Read afresh each time it is called, never cached by this type, so a listener that asks
    /// at every handshake sees a removed pin at once.
    ///
    /// # Errors
    ///
    /// - Every error of the directory check: the directory missing, not a plain directory,
    ///   owned by a uid other than `owner_uid`, or readable by group or others.
    /// - [`FleetIdentityError::FileMissing`] when there is no `panel.pem`, which is the state of
    ///   a node that has been detached, or has not been enrolled yet.
    /// - [`FleetIdentityError::FileNotPlain`], [`FleetIdentityError::FileTooLarge`] for a file
    ///   that is not a small plain file.
    /// - [`FleetIdentityError::CertificateMalformed`] when it is not exactly one PEM
    ///   certificate.
    pub fn read(directory: &Path, owner_uid: u32) -> Result<Self, FleetIdentityError> {
        require_private_directory(directory, owner_uid)?;

        let path = directory.join(AgentPaths::FLEET_PANEL_PIN_FILE);
        let bytes = read_identity_file(&path, MAX_PIN_FILE_BYTES)?;
        let certificate_pem =
            String::from_utf8(bytes).map_err(|_| FleetIdentityError::CertificateMalformed {
                path: path.clone(),
                reason: "it is not text",
            })?;
        let der = certificate_bytes(&path, &certificate_pem)?;

        Ok(Self {
            certificate_pem,
            fingerprint: CertificateFingerprint::of_der(&der),
        })
    }
}

#[cfg(test)]
#[path = "../tests/fleet/panel_pin_tests.rs"]
mod tests;
