//! Validating and reloading the web server without writing anything.

use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::sites::{SiteMaintenanceHost, SitesOpError};

/// Validates the web server's current configuration and reloads it.
///
/// The batching call `sites.proto` describes: a panel that made ten site
/// changes applies them with one reload instead of ten. It is the write
/// protocol with the write taken out — the same validator and the same reload
/// command, both from the [`DistroAdapter`], so a batch reload cannot check a
/// different binary or poke a different service than the one every individual
/// write already used.
///
/// Idempotent, and nothing is rolled back on failure because nothing was
/// changed: a refusal here leaves the running configuration exactly as it was.
///
/// # Errors
///
/// Returns [`SitesOpError::NginxValidation`] when `nginx -t` refuses the
/// configuration on disk, and [`SitesOpError::ReloadFailed`] when the service
/// manager refuses to reload it.
pub fn reload_web_server(
    host: &dyn SiteMaintenanceHost,
    distro: &dyn DistroAdapter,
) -> Result<(), SitesOpError> {
    let validator = Validator {
        program: distro.nginx_binary(),
        arguments: &[VALIDATE_ARGUMENT],
    };
    let reload_arguments = [RELOAD_SUBCOMMAND, distro.nginx_service()];
    let reload = Reload {
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.validate_and_reload(&validator, &reload)
}
