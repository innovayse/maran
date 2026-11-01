//! InstallPanelCertificate: the panel's own TLS pair, replaced in place.

use maran_distro::DistroAdapter;

use crate::sites::SiteCertificate;
use crate::ssl::certificate_expiry::certificate_expiry;
use crate::ssl::key_matches_certificate::key_matches_certificate;
use crate::ssl::material_installed::material_installed;
use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;
use crate::ssl::write_material::write_material;

/// Installs `material` as the panel's own certificate and key — the pair the
/// panel's nginx vhost already names — and returns the certificate's expiry
/// as Unix seconds, for the panel to schedule renewal from (issue #132).
///
/// The site installation's steps, minus the one that does not apply. The key
/// is checked against the certificate before anything is written; both files
/// are written as ONE change of the config-write protocol, validated with
/// `nginx -t` and reloaded once, with the previous pair restored on any
/// refusal — so a pair nginx rejects leaves the panel on the certificate it was
/// serving. There is no vhost to rewire: the installer rendered the panel vhost
/// against these two fixed names, and replacing the files is the whole change.
///
/// Idempotent as `ssl.proto` requires: byte-identical material already on disk
/// writes nothing and reloads nothing.
///
/// This function does not know what ACME is: the order, the challenge and the
/// account key are the panel's (spec §9). The self-signed pair the installer
/// generated is replaced like any other material, and when no certificate is
/// ever issued it stays, which is the fallback the issue asks for.
///
/// # Errors
///
/// Returns [`SslOpError::KeyDoesNotMatchCertificate`],
/// [`SslOpError::MalformedCertificate`] or [`SslOpError::MalformedPrivateKey`]
/// before anything is written; [`SslOpError::ExpiryUnreadable`] when the
/// certificate's `notAfter` cannot be read; [`SslOpError::NginxValidation`] or
/// [`SslOpError::ReloadFailed`] with the previous pair restored;
/// [`SslOpError::MaterialWrite`] for every other write failure; and
/// [`SslOpError::Bound`] when openssl, the validator or the reload outruns its
/// deadline.
pub fn install_panel_certificate(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    material: &CertificateMaterial,
) -> Result<i64, SslOpError> {
    let certificate = SiteCertificate::for_panel();

    // Material already on disk was checked when it was installed, so a retry
    // proves nothing new by spawning openssl again.
    let already_installed = material_installed(host, &certificate, material)?;

    if !already_installed && !key_matches_certificate(host, distro, material)? {
        return Err(SslOpError::KeyDoesNotMatchCertificate);
    }

    // Read from the certificate itself: the caller's idea of the expiry would
    // be a second copy of a fact, and the copy is the one that goes stale.
    let expiry = certificate_expiry(host, distro, material)?;

    if !already_installed {
        write_material(host, distro, &certificate, material)?;
    }

    Ok(expiry)
}

#[cfg(test)]
#[path = "../tests/ssl/install_panel_certificate_tests.rs"]
mod tests;
