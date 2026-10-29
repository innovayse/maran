//! Handing certificate material to the one config-write protocol.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::SiteCertificate;
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// Writes `material` into the agent's own store with the nginx validator and
/// reload attached.
///
/// The validator and the reload are built exactly as the site area builds
/// them, from the same constants and the same [`DistroAdapter`] methods: a
/// certificate write that checked with a different binary, or reloaded a
/// different service, would be a second opinion about what "the web server"
/// means on this host.
///
/// This is the FIRST of the two writes an installation performs, and it is
/// separate from the vhost write on purpose. Fusing them would mean one
/// rollback covering both, and the failure worth surviving is the second one:
/// a certificate nginx rejects must leave the site on the plain-HTTP vhost it
/// already had, serving traffic, rather than on a TLS vhost pointing at
/// material the server refused.
///
/// # Errors
///
/// Returns [`SslOpError::NginxValidation`] or [`SslOpError::ReloadFailed`] with
/// the previous material restored, [`SslOpError::Bound`] when the validator or
/// the reload outruns its deadline (the previous material is restored and the
/// lock released by then), and [`SslOpError::MaterialWrite`] for every other
/// failure of the protocol — never carrying the material itself.
pub(crate) fn write_material(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    certificate: &SiteCertificate,
    material: &CertificateMaterial,
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

    host.write_material(certificate, material, &validator, &reload)
}
