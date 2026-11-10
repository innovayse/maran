//! Validating and reloading the web server without writing anything.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroAdapter, SiteWebServer};

use crate::safe_write::model::{Reload, Validator};
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::sites::{SiteMaintenanceHost, SitesOpError};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// Validates `web_server`'s current configuration and reloads it.
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
/// manager refuses to reload it. Returns [`SitesOpError::Bound`] when either
/// program outlived its deadline and was killed: neither a refusal of the
/// configuration nor of the reload, and the caller (the startup reconciliation
/// runs this before the agent binds) decides what a bound means for it.
pub fn reload_web_server(
    host: &dyn SiteMaintenanceHost,
    distro: &dyn DistroAdapter,
    web_server: SiteWebServer,
) -> Result<(), SitesOpError> {
    let binary = distro.site_web_server_binary(web_server);
    let service_manager = distro.service_manager();
    // A pure check, so the short class: the web server's `-t` reads the tree and changes nothing.
    let validator = Validator {
        program: &binary,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    };
    // `--` before the unit: `systemctl` reads its options with `getopt_long`, which ends
    // option parsing at `--`. The program is the adapter's absolute path, and a bare
    // `"systemctl"` would be worse than a literal, because a root process would then
    // resolve it through `PATH`.
    let reload_arguments = [
        RELOAD_SUBCOMMAND,
        END_OF_OPTIONS,
        distro.site_web_server_service(web_server),
    ];
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };

    host.validate_and_reload(&validator, &reload)
}

#[cfg(test)]
#[path = "../tests/sites/reload_web_server_tests.rs"]
mod tests;
