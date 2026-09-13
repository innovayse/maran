//! Taking certificate material away through the same protocol that writes it.

use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::SiteCertificate;
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Removes the material for `certificate`, validating and reloading after each
/// unlink.
///
/// Called only AFTER the site's vhost has stopped referring to the files. The
/// other order looks equally reasonable and is not: unlink first and the
/// running configuration names an `ssl_certificate` that no longer exists, so
/// the next `nginx -t` — this one, or an unrelated operation's minutes later —
/// fails, and nginx does not come back after the next reboot.
///
/// # Errors
///
/// Returns [`SslOpError::NginxValidation`] or [`SslOpError::ReloadFailed`] with
/// the file put back, and [`SslOpError::MaterialWrite`] for every other failure
/// of the protocol.
pub(crate) fn remove_material(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    certificate: &SiteCertificate,
) -> Result<(), SslOpError> {
    let validator = Validator {
        program: distro.nginx_binary(),
        arguments: &[VALIDATE_ARGUMENT],
    };
    let reload_arguments = [RELOAD_SUBCOMMAND, distro.nginx_service()];
    let reload = Reload {
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.remove_material(certificate, &validator, &reload)
}
