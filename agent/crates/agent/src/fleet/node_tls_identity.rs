//! The certificate and private key the node presents to the panel.

use std::fmt;

use rustls::pki_types::pem::PemObject;
use rustls::pki_types::{CertificateDer, PrivateKeyDer};

use super::fleet_listener_error::FleetListenerError;

/// The node's own TLS identity: its certificate chain and the private key of the leaf.
///
/// Built from the PEM text of the two identity files, which `node_identity` reads and
/// size-checks; this type only turns that text into what the TLS library takes. The key is the
/// one secret of the fleet listener, so `Debug` prints its presence and never its bytes: a request struct or an error
/// holding this value cannot leak it into a log line.
pub struct NodeTlsIdentity {
    /// The node's certificate chain, leaf first. A self-signed node has exactly one.
    chain: Vec<CertificateDer<'static>>,
    /// The leaf's private key.
    key: PrivateKeyDer<'static>,
}

impl NodeTlsIdentity {
    /// Reads the node's certificate chain and private key from PEM text, as the identity files
    /// hold them.
    ///
    /// Whether the key belongs to the certificate is checked when the TLS configuration is
    /// built, which refuses a pair that does not match.
    ///
    /// # Errors
    ///
    /// Returns [`FleetListenerError::Pem`] when `certificate_pem` holds no certificate or
    /// `private_key_pem` holds no private key, or either is not valid PEM.
    pub fn from_pem(
        certificate_pem: &str,
        private_key_pem: &str,
    ) -> Result<Self, FleetListenerError> {
        let chain = CertificateDer::pem_slice_iter(certificate_pem.as_bytes())
            .collect::<Result<Vec<_>, _>>()
            .map_err(FleetListenerError::Pem)?;
        if chain.is_empty() {
            return Err(FleetListenerError::Pem(
                rustls::pki_types::pem::Error::NoItemsFound,
            ));
        }

        let key = PrivateKeyDer::from_pem_slice(private_key_pem.as_bytes())
            .map_err(FleetListenerError::Pem)?;

        Ok(Self { chain, key })
    }

    /// Hands the chain and key to the TLS configuration, consuming the identity so the key has
    /// exactly one owner.
    pub(super) fn into_parts(self) -> (Vec<CertificateDer<'static>>, PrivateKeyDer<'static>) {
        (self.chain, self.key)
    }
}

impl fmt::Debug for NodeTlsIdentity {
    /// Prints how many certificates there are and that a key is present, never the key.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("NodeTlsIdentity")
            .field("certificates", &self.chain.len())
            .field("private_key", &"<redacted>")
            .finish()
    }
}
