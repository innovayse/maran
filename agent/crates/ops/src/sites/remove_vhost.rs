//! Taking a vhost away through the same protocol that writes one.

use std::path::Path;

use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::sites::{SiteHost, SitesOpError};

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
/// [`SitesOpError::ConfigWrite`] covers every other failure of the protocol.
pub(crate) fn remove_vhost(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    target: &Path,
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

    host.remove_config(target, &validator, &reload)
}
