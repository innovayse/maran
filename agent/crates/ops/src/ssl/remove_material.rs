//! Taking certificate material away through the same protocol that writes it.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::SiteCertificate;
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;
use maran_agent_core::end_of_options::END_OF_OPTIONS;

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
/// the file put back, [`SslOpError::Bound`] when the validator or the reload
/// outruns its deadline (the file is put back as for a refusal), and
/// [`SslOpError::MaterialWrite`] for every other failure of the protocol.
pub(crate) fn remove_material(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    certificate: &SiteCertificate,
) -> Result<(), SslOpError> {
    let nginx = distro.nginx_binary();
    let service_manager = distro.service_manager();
    let validator = Validator {
        program: &nginx,
        arguments: &[VALIDATE_ARGUMENT],
        // `nginx -t` only reads: a pure validation.
        class: CallClass::Validate,
    };
    // `--` before the unit: `systemctl` reads its options with `getopt_long`,
    // which ends option parsing at `--`, on both families.
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, distro.nginx_service()];
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };

    host.remove_material(certificate, &validator, &reload)
}
