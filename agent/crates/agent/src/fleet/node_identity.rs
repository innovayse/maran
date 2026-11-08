//! A node's own identity, read back from the fleet directory.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;

use super::certificate_bytes::certificate_bytes;
use super::fleet_identity_error::FleetIdentityError;
use super::read_identity_file::read_identity_file;
use super::require_private_directory::require_private_directory;

/// The most a key file may hold. A PKCS#8 P-256 key in PEM is about 240 bytes.
const MAX_KEY_FILE_BYTES: u64 = 16 * 1024;

/// The most a certificate file may hold. A PEM-wrapped certificate within the 16 KiB of DER the
/// reader accepts is under 22 KiB.
const MAX_CERTIFICATE_FILE_BYTES: u64 = 32 * 1024;

/// The first line of the key file the generator writes (PKCS#8, which is what `rcgen` emits).
const KEY_BEGIN_MARKER: &str = "-----BEGIN PRIVATE KEY-----";

/// What the fleet listener presents to a connecting panel: the node's certificate and the key
/// that proves it is the node's, with the fingerprint the operator approves it by.
///
/// The key is a [`SecretString`], so a `{:?}` of this struct prints «redacted» in its place and
/// the key cannot reach a log line through a derived `Debug`. The only reader of it is
/// [`SecretString::expose`], named to be greppable.
#[derive(Debug)]
pub struct NodeIdentity {
    /// The node certificate, PEM.
    pub certificate_pem: String,
    /// The node's private key, PKCS#8 PEM.
    pub private_key_pem: SecretString,
    /// The SHA-256 fingerprint of the certificate's DER encoding: the value the operator reads
    /// off this node's terminal and types into the panel.
    pub fingerprint: CertificateFingerprint,
}

impl NodeIdentity {
    /// Reads `node.key` and `node.pem` from `directory`.
    ///
    /// Done on demand and not cached by this type: whether to re-read is the listener's
    /// decision, and a reader that refuses a directory that has since become loose is the
    /// behaviour wanted at every start.
    ///
    /// # Errors
    ///
    /// - Every error of the directory check: the directory missing, not a plain directory,
    ///   owned by a uid other than `owner_uid`, or readable by group or others.
    /// - [`FleetIdentityError::FileMissing`], [`FleetIdentityError::FileNotPlain`] and
    ///   [`FleetIdentityError::FileTooLarge`] for either file.
    /// - [`FleetIdentityError::KeyMalformed`] when the key file is not UTF-8 text starting with
    ///   a `PRIVATE KEY` line.
    /// - [`FleetIdentityError::CertificateMalformed`] when the certificate file is not exactly
    ///   one PEM certificate.
    pub fn read(directory: &Path, owner_uid: u32) -> Result<Self, FleetIdentityError> {
        require_private_directory(directory, owner_uid)?;

        let key_path = directory.join(AgentPaths::FLEET_NODE_KEY_FILE);
        let key_bytes = read_identity_file(&key_path, MAX_KEY_FILE_BYTES)?;
        let key_text =
            String::from_utf8(key_bytes).map_err(|_| FleetIdentityError::KeyMalformed {
                path: key_path.clone(),
            })?;
        if key_text.lines().next() != Some(KEY_BEGIN_MARKER) {
            return Err(FleetIdentityError::KeyMalformed { path: key_path });
        }

        let certificate_path = directory.join(AgentPaths::FLEET_NODE_CERTIFICATE_FILE);
        let certificate_bytes_read =
            read_identity_file(&certificate_path, MAX_CERTIFICATE_FILE_BYTES)?;
        let certificate_pem = String::from_utf8(certificate_bytes_read).map_err(|_| {
            FleetIdentityError::CertificateMalformed {
                path: certificate_path.clone(),
                reason: "it is not text",
            }
        })?;
        let der = certificate_bytes(&certificate_path, &certificate_pem)?;

        Ok(Self {
            certificate_pem,
            private_key_pem: SecretString::new(key_text),
            fingerprint: CertificateFingerprint::of_der(&der),
        })
    }
}

#[cfg(test)]
#[path = "../tests/fleet/node_identity_tests.rs"]
mod tests;
