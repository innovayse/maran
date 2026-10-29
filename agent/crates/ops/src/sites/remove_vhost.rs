//! Taking a vhost away through the same protocol that writes one.

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::sites::{SiteHost, SitesOpError};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// Removes the vhost at `target`, then validates and reloads.
///
/// The validator and the reload are built exactly as
/// [`super::write_vhost::write_vhost`] builds them, from the same constants
/// and the same [`DistroAdapter`] methods: a removal that reloaded a different
/// service, or checked with a different binary, would be a second opinion
/// about what "the web server" means on this host.
///
/// # Errors
///
/// Returns [`SitesOpError::NginxValidation`] when the tree fails to validate
/// once the file is gone and [`SitesOpError::ReloadFailed`] when the reload
/// refuses — in both cases the vhost has been put back.
/// [`SitesOpError::Bound`] is a program killed at its deadline, with the vhost put
/// back as well, and no verdict on the tree.
/// [`SitesOpError::ConfigWrite`] covers every other failure of the protocol.
pub(crate) fn remove_vhost(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    target: &Path,
) -> Result<(), SitesOpError> {
    let nginx = distro.nginx_binary();
    let service_manager = distro.service_manager();
    // A pure check, so the short class: `nginx -t` reads the tree and changes nothing.
    let validator = Validator {
        program: &nginx,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    };
    // `--` before the unit: `systemctl` reads its options with `getopt_long`, which ends
    // option parsing at `--`. The program is the adapter's absolute path, and a bare
    // `"systemctl"` would be worse than a literal, because a root process would then
    // resolve it through `PATH`.
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, distro.nginx_service()];
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };

    host.remove_config(target, &validator, &reload)
}

#[cfg(test)]
#[path = "../tests/sites/remove_vhost_tests.rs"]
mod tests;
