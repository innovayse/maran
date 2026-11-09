//! The TLS-level check that a client is the pinned panel.

use std::sync::Arc;

use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;
use rustls::client::danger::HandshakeSignatureValid;
use rustls::crypto::CryptoProvider;
use rustls::pki_types::{CertificateDer, UnixTime};
use rustls::server::danger::{ClientCertVerified, ClientCertVerifier};
use rustls::{CertificateError, DigitallySignedStruct, DistinguishedName, Error, SignatureScheme};

use super::pin_source::PinSource;

/// Admits a TLS client only when its certificate is the pinned panel certificate.
///
/// Two independent facts must hold, and the second is what the first cannot give. The chain
/// check over the pin as the only trust anchor (signature, validity window, key usage) is what
/// the TLS library does for a self-signed leaf used as an anchor; it compares subject and public
/// key, so the same key re-issued as a new certificate passes it. The fingerprint comparison
/// refuses that: the presented DER must be byte-for-byte the pinned one. Neither check is
/// written in terms of a host name, because identity here is the certificate, not a name.
///
/// The pin is read from its [`PinSource`] on every call, so a removed or replaced pin takes
/// effect at the next handshake. Session resumption is switched off in the server
/// configuration, so there is no admitted-once ticket that skips this verifier.
#[derive(Debug)]
pub struct PinnedClientVerifier {
    /// Where the current pin comes from.
    pin_source: Arc<dyn PinSource>,
    /// The provider whose algorithms verify the handshake signature.
    provider: Arc<CryptoProvider>,
}

impl PinnedClientVerifier {
    /// A verifier that admits what `pin_source` currently pins, checking signatures with
    /// `provider`.
    #[must_use]
    pub fn new(pin_source: Arc<dyn PinSource>, provider: Arc<CryptoProvider>) -> Self {
        Self {
            pin_source,
            provider,
        }
    }
}

impl ClientCertVerifier for PinnedClientVerifier {
    /// No certificate-authority hint is sent: there is exactly one acceptable client and it
    /// knows which certificate it is. A client library that filters its certificates by the
    /// server's hints must therefore be given its certificate directly.
    fn root_hint_subjects(&self) -> &[DistinguishedName] {
        &[]
    }

    /// Admits `end_entity` when it is the pinned certificate and the chain check over the pin
    /// accepts it at `now`.
    ///
    /// # Errors
    ///
    /// Returns an error when nothing is pinned, when the client sent intermediates (a pinned
    /// self-signed leaf has none), when the presented certificate is not byte-for-byte the
    /// pinned one, or when the chain check refuses it (expired, wrong key usage, bad signature).
    fn verify_client_cert(
        &self,
        end_entity: &CertificateDer<'_>,
        intermediates: &[CertificateDer<'_>],
        now: UnixTime,
    ) -> Result<ClientCertVerified, Error> {
        let pin = self
            .pin_source
            .current()
            .ok_or_else(|| Error::General("no panel certificate is pinned".to_owned()))?;

        if !intermediates.is_empty() {
            return Err(Error::InvalidCertificate(
                CertificateError::ApplicationVerificationFailure,
            ));
        }

        let presented = CertificateFingerprint::of_der(end_entity.as_ref());
        if presented != *pin.fingerprint() {
            return Err(Error::InvalidCertificate(
                CertificateError::ApplicationVerificationFailure,
            ));
        }

        pin.chain_verifier()
            .verify_client_cert(end_entity, intermediates, now)
    }

    /// Verifies a TLS 1.2 handshake signature. The listener is TLS 1.3 only, so this is never
    /// reached; it is answered honestly rather than refused, so that a change to the version
    /// list cannot turn it into an unchecked path.
    fn verify_tls12_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, Error> {
        rustls::crypto::verify_tls12_signature(
            message,
            cert,
            dss,
            &self.provider.signature_verification_algorithms,
        )
    }

    /// Verifies the TLS 1.3 handshake signature, which is the client's proof that it holds the
    /// private key of the certificate it presented.
    fn verify_tls13_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, Error> {
        rustls::crypto::verify_tls13_signature(
            message,
            cert,
            dss,
            &self.provider.signature_verification_algorithms,
        )
    }

    /// The signature schemes the provider can verify.
    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        self.provider
            .signature_verification_algorithms
            .supported_schemes()
    }
}

#[cfg(test)]
#[path = "../tests/fleet/pinned_client_verifier_tests.rs"]
mod tests;
