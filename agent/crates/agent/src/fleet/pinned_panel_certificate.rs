//! The panel certificate this node trusts, and the check that a client really is it.

use std::sync::Arc;

use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;
use rustls::RootCertStore;
use rustls::pki_types::CertificateDer;
use rustls::pki_types::pem::PemObject;
use rustls::server::WebPkiClientVerifier;
use rustls::server::danger::ClientCertVerifier;

use super::fleet_crypto_provider::fleet_crypto_provider;
use super::fleet_listener_error::FleetListenerError;

/// The one certificate a node admits as a client: the panel's, pinned when the node enrolled.
///
/// There is no certificate authority in a fleet, so trust is the certificate itself. It is held
/// three ways, each for what it is used for: the DER, which is handed to the TLS library as the
/// trust anchor; its SHA-256 [`CertificateFingerprint`], which is the comparison that decides
/// admission; and a ready-made chain verifier over that anchor, so the validity window,
/// extended key usage and signature of a presented certificate are checked by the library that
/// already does it rather than by code written here.
#[derive(Debug, Clone)]
pub struct PinnedPanelCertificate {
    /// The pinned certificate, DER.
    certificate: CertificateDer<'static>,
    /// Its SHA-256 fingerprint.
    fingerprint: CertificateFingerprint,
    /// Verifies a presented certificate against the pinned one as the only trust anchor.
    chain_verifier: Arc<dyn ClientCertVerifier>,
}

impl PinnedPanelCertificate {
    /// Pins the certificate in `pem`, the text of `panel.pem`: exactly the first certificate
    /// block. The caller is responsible for the file holding only one.
    ///
    /// # Errors
    ///
    /// - [`FleetListenerError::Pem`] when `pem` holds no certificate.
    /// - Every error of [`PinnedPanelCertificate::from_der`].
    pub fn from_pem(pem: &str) -> Result<Self, FleetListenerError> {
        let certificate =
            CertificateDer::from_pem_slice(pem.as_bytes()).map_err(FleetListenerError::Pem)?;
        Self::from_der(certificate.as_ref().to_vec())
    }

    /// Pins the certificate whose DER encoding is `der`.
    ///
    /// # Errors
    ///
    /// - [`FleetListenerError::PinnedCertificate`] when `der` is not a certificate the TLS
    ///   library can use as a trust anchor. The pin source treats it as "nothing is pinned" and
    ///   admits nobody, and says so in the log.
    /// - [`FleetListenerError::PinnedVerifier`] when a verifier cannot be built over it.
    pub fn from_der(der: Vec<u8>) -> Result<Self, FleetListenerError> {
        let certificate = CertificateDer::from(der);
        let fingerprint = CertificateFingerprint::of_der(certificate.as_ref());

        let mut anchors = RootCertStore::empty();
        anchors
            .add(certificate.clone())
            .map_err(FleetListenerError::PinnedCertificate)?;

        let chain_verifier =
            WebPkiClientVerifier::builder_with_provider(Arc::new(anchors), fleet_crypto_provider())
                .build()
                .map_err(|error| FleetListenerError::PinnedVerifier(error.to_string()))?;

        Ok(Self {
            certificate,
            fingerprint,
            chain_verifier,
        })
    }

    /// The pinned certificate, DER.
    #[must_use]
    pub fn certificate(&self) -> &CertificateDer<'static> {
        &self.certificate
    }

    /// The fingerprint a presented certificate must equal.
    #[must_use]
    pub fn fingerprint(&self) -> &CertificateFingerprint {
        &self.fingerprint
    }

    /// The verifier that checks a presented certificate against the pinned one.
    pub(super) fn chain_verifier(&self) -> &Arc<dyn ClientCertVerifier> {
        &self.chain_verifier
    }
}
