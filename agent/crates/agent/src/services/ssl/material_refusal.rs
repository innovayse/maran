//! Which failures of a certificate install are a verdict on the material.

use maran_ops::ssl::SslOpError;

use crate::proto::CertificateMaterialRefusal;

/// Sorts an install failure into a verdict on the material the caller sent,
/// or hands it back as some other failure.
///
/// Shared by `InstallCertificate` and `InstallPanelCertificate`, whose
/// `refused` cases carry the same three answers: a half openssl cannot read,
/// or a key that is not the certificate's. Each has a different fix, so each
/// has its own reason rather than one `ERROR_CODE_INVALID_INPUT`.
///
/// # Errors
///
/// Returns the failure unchanged when it is not a verdict on the material, for
/// the caller to map through `to_agent_error`.
pub fn material_refusal(error: SslOpError) -> Result<CertificateMaterialRefusal, SslOpError> {
    match error {
        SslOpError::MalformedCertificate { .. } => {
            Ok(CertificateMaterialRefusal::MalformedCertificate)
        }
        SslOpError::MalformedPrivateKey => Ok(CertificateMaterialRefusal::MalformedPrivateKey),
        SslOpError::KeyDoesNotMatchCertificate => {
            Ok(CertificateMaterialRefusal::KeyDoesNotMatchCertificate)
        }
        other => Err(other),
    }
}
