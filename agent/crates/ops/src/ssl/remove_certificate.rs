//! RemoveCertificate: putting a site back on plain HTTP and deleting the key.

use maran_distro::DistroAdapter;

use crate::sites::SiteCertificate;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::render_vhost::render_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::write_vhost::write_vhost;
use crate::ssl::remove_material::remove_material;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Removes `site`'s certificate: the vhost stops referring to the material,
/// and only then is the material deleted.
///
/// That order is the operation. Deleting the files first leaves the running
/// configuration naming an `ssl_certificate` that is not there, so the next
/// `nginx -t` fails — this one, or an unrelated site's minutes later — and
/// nginx does not start again after the next reboot. Rewiring first means the
/// worst case is a site back on plain HTTP with its material still on disk,
/// which the same command removes when it is run again.
///
/// Idempotent as the contract requires (`ssl.proto`): removing when no
/// certificate is installed is [`SslOpError::NotFound`], and a removal
/// interrupted between the two steps is completed by running it again.
///
/// # Errors
///
/// Returns [`SslOpError::NotFound`] when no material is installed for the
/// domain, [`SslOpError::SiteNotFound`] when the site's vhost is gone,
/// [`SslOpError::Render`] when the plain-HTTP vhost cannot be rendered, and
/// [`SslOpError::NginxValidation`], [`SslOpError::ReloadFailed`] or
/// [`SslOpError::MaterialWrite`] from the two writes — in the first two cases
/// with the previous content restored.
pub fn remove_certificate(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    site: &CreateSiteInput,
) -> Result<(), SslOpError> {
    let certificate = SiteCertificate::for_domain(&site.domain);

    // Either half being present counts as installed: the state to converge on
    // is "neither file is there", and a removal that ignored a lone private key
    // would leave the secret half behind for good.
    let has_certificate = host
        .read_material(certificate.certificate_path())?
        .is_some();
    let has_key = host.read_material(certificate.key_path())?.is_some();
    if !has_certificate && !has_key {
        return Err(SslOpError::NotFound {
            domain: site.domain.as_str().to_owned(),
        });
    }

    let paths = resolved_site_paths(host, &site.account, &site.domain)?;
    let Some(current) = host.read_config(&paths.config_path)? else {
        return Err(SslOpError::SiteNotFound {
            domain: site.domain.as_str().to_owned(),
        });
    };

    // The site as it will be: no certificate, so the site area renders the
    // plain-HTTP vhost — the same text `create_site` writes for a site that
    // never had one.
    let plain = CreateSiteInput {
        certificate: None,
        ..site.clone()
    };
    let wanted = render_vhost(&plain, &paths)?;
    if current != wanted {
        write_vhost(host, distro, &paths.config_path, &wanted)?;
    }

    remove_material(host, distro, &certificate)
}

#[cfg(test)]
#[path = "../tests/ssl/remove_certificate_tests.rs"]
mod tests;
