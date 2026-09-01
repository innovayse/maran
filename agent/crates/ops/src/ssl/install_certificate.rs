//! InstallCertificate: placing material, rewiring the vhost, reloading.

use maran_distro::DistroAdapter;

use crate::sites::SiteCertificate;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_vhost::write_vhost;
use crate::ssl::certificate_expiry::certificate_expiry;
use crate::ssl::key_matches_certificate::key_matches_certificate;
use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;
use crate::ssl::write_material::write_material;

/// Installs `material` for `site`: both files in the agent's own store, the
/// site's vhost rewired to point at them, nginx reloaded — and the
/// certificate's expiry returned, as Unix seconds, for the panel to schedule
/// renewal from.
///
/// **This function does not know what ACME is, and must never learn.** The spec
/// puts ordering, challenges and account keys in C# (§9: the agent *"only
/// places certificate files and does a reload"*). There is no HTTP client in
/// this crate, and its absence is the enforcement: an agent that could fetch a
/// certificate would be an agent that could be told where to fetch it from.
///
/// The order of what follows is the whole design, and each step protects
/// against a failure the next one cannot:
///
/// 1. The key is checked against the certificate, in memory, before anything is
///    written. A mismatched pair passes `nginx -t` and fails at the first
///    handshake — the site goes down at the moment it was meant to become
///    secure, with the swap already committed. The check is skipped only when
///    exactly this material is already installed, which is the state that check
///    once approved.
/// 2. The site is looked up. Material written for a site that does not exist is
///    a private key on disk that nothing serves and no operation removes.
/// 3. The material is written — both files, one rename each, the key at `0600`
///    — as ONE call of the write protocol, validated and reloaded once at the
///    end. Two separate writes would leave a mismatched pair on disk in between,
///    and `nginx -t` loads them: every renewal on the host would fail with
///    `key values mismatch`.
/// 4. Any self-signed marker beside the material is removed — on EVERY call,
///    including the no-op one, so that a single failed removal is retried rather
///    than becoming permanent. A real certificate must not inherit a
///    placeholder's licence to be overwritten by the next `GenerateSelfSigned`.
/// 5. The vhost is rewired as a SECOND call of the write protocol, so that a
///    certificate nginx rejects rolls back to the plain-HTTP vhost and leaves a
///    working site rather than a broken one.
///
/// Idempotent as the contract requires (`ssl.proto`): installing byte-identical
/// material again writes nothing and reloads nothing, and installing different
/// material for the same domain replaces it. The comparison is against what is
/// on disk, not against a marker, so a retry after a crash between the two
/// writes completes the half that did not happen.
///
/// # Errors
///
/// Returns [`SslOpError::KeyDoesNotMatchCertificate`] when the pair does not
/// belong together — before anything is written — and
/// [`SslOpError::MalformedCertificate`] or [`SslOpError::MalformedPrivateKey`]
/// when openssl cannot read a half. Returns [`SslOpError::ExpiryUnreadable`]
/// when the certificate's `notAfter` cannot be established, and
/// [`SslOpError::SiteNotFound`] when no vhost for the domain exists. Returns
/// [`SslOpError::MaterialWrite`] when the material cannot be placed,
/// [`SslOpError::Render`] when the vhost cannot be rendered,
/// [`SslOpError::NginxValidation`] when nginx rejects it and
/// [`SslOpError::ReloadFailed`] when the reload refuses it — in both cases with
/// the previous vhost restored.
pub fn install_certificate(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    site: &CreateSiteInput,
    material: &CertificateMaterial,
) -> Result<i64, SslOpError> {
    let certificate = SiteCertificate::for_domain(&site.domain);

    // Asked first, and it is only a pair of file reads: material that is already
    // installed was checked against its certificate when it was installed, so a
    // retry does not spawn openssl to prove the same thing again. A no-op
    // therefore costs two reads and one `-enddate`, not four processes.
    let already_installed = is_installed(host, &certificate, material)?;

    if !already_installed && !key_matches_certificate(host, distro, material)? {
        return Err(SslOpError::KeyDoesNotMatchCertificate);
    }

    // Read from the certificate itself, before it is installed: a caller's idea
    // of the expiry is a second copy of a fact, and the copy is the one that
    // goes stale.
    let expiry = certificate_expiry(host, distro, material)?;

    let paths = resolved_site_paths(host, &site.account, &site.domain)?;
    let Some(current) = host.read_config(&paths.config_path)? else {
        return Err(SslOpError::SiteNotFound {
            domain: site.domain.as_str().to_owned(),
        });
    };

    if !already_installed {
        write_material(host, distro, &certificate, material)?;
    }

    // The material in this directory is not the placeholder a marker would
    // describe, so any marker goes — and NOT on a best-effort basis. Left
    // behind, it would hand a certificate the customer paid an authority for the
    // placeholder's licence to be destroyed by the next `GenerateSelfSigned`.
    //
    // OUTSIDE the guard above, and that is the whole point of its position. A
    // single failed removal — a full disk, a transient error — used to be
    // permanent: the retry found the material already installed, skipped the
    // block, and never tried again, leaving a marker beside real material for
    // good. `subject == issuer` rescues that only when the new certificate is
    // CA-signed; a customer's own self-signed certificate plus a stuck marker is
    // indistinguishable from a placeholder, and the next `GenerateSelfSigned`
    // destroys it. Removing a file that is not there is already a no-op, so
    // running this on every path costs nothing and closes the window.
    host.remove_self_signed_marker(&self_signed_marker(&certificate))?;

    // The site as it will be: the same input, with the certificate it now has.
    // Rendered by the site area's own renderer, so the TLS vhost a certificate
    // installation produces is byte-identical to the one `create_site` would
    // produce for a site that already had one — two renderings of a vhost is
    // how the port-80 half and the TLS half come to disagree.
    let secured = CreateSiteInput {
        certificate: Some(certificate),
        ..site.clone()
    };
    let wanted = render_vhost(&secured, &paths)?;

    // The second write, and skipped when there is nothing to change: the panel
    // retries after a timeout, and a reload of nginx per retry is a storm.
    if current != wanted {
        write_vhost(host, distro, &paths.config_path, &wanted)?;
    }

    Ok(expiry)
}

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
fn is_installed(
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

#[cfg(test)]
#[path = "../tests/ssl/install_certificate_tests.rs"]
mod tests;
