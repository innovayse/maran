//! Whether the material a caller sent is already the material on disk.

use crate::sites::SiteCertificate;
use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Whether exactly this material is already on disk for `certificate`.
///
/// Both halves are compared, not just the certificate: a crash between the key
/// write and the certificate write leaves a pair that does not belong together,
/// and a check that looked only at the certificate would call that state
/// converged and leave the site unable to complete a handshake.
///
/// # Errors
///
/// Returns [`SslOpError::MaterialWrite`] when a file exists but cannot be read
/// — which is deliberately not treated as "nothing installed", since that
/// reading would overwrite live material without knowing what it replaced.
pub(crate) fn material_installed(
    host: &dyn SslHost,
    certificate: &SiteCertificate,
    material: &CertificateMaterial,
) -> Result<bool, SslOpError> {
    let installed_certificate = host.read_material(certificate.certificate_path())?;
    let installed_key = host.read_material(certificate.key_path())?;

    Ok(
        installed_certificate.as_deref() == Some(material.certificate_pem())
            && installed_key.as_deref() == Some(material.private_key_pem()),
    )
}
