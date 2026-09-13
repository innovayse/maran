//! Handing a rendered vhost to the one config-write protocol.

use std::path::Path;

use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::{SiteHost, SitesOpError};

/// The subcommand that makes the service manager re-read a configuration.
///
/// Shared with the removal path, which must reload the same way: two spellings
/// of "reload nginx" is how one of them ends up not reloading anything.
pub(crate) const RELOAD_SUBCOMMAND: &str = "reload";

/// The argument that makes nginx check its configuration instead of serving.
pub(crate) const VALIDATE_ARGUMENT: &str = "-t";

/// Writes `contents` to `target` with the nginx validator and reload attached.
///
/// Every site operation that produces a configuration goes through here, so
/// the validator and the reload command are chosen once. Both come from the
/// [`DistroAdapter`] — the binary path and the service name differ between
/// families, and an operation that wrote either as a literal would be guessing
/// (rules/rust.md "Distro adapter").
///
/// # Errors
///
/// Returns [`SitesOpError::NginxValidation`] when `nginx -t` rejects the
/// rendered configuration and [`SitesOpError::ReloadFailed`] when the reload
/// refuses it — in both cases the previous vhost has already been restored, or
/// removed if the site had none. Returns [`SitesOpError::ConfigWrite`] for
/// every other failure of the protocol, including a rollback that itself
/// failed.
pub(crate) fn write_vhost(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    target: &Path,
    contents: &str,
) -> Result<(), SitesOpError> {
    let validator = Validator {
        program: distro.nginx_binary(),
        arguments: &[VALIDATE_ARGUMENT],
    };
    let reload_arguments = [RELOAD_SUBCOMMAND, distro.nginx_service()];
    let reload = Reload {
        // The absolute path of the service manager, from the adapter. `ops`
        // names no binary path of its own — and a bare `"systemctl"` would be
        // worse than the literal, because a root process would then resolve
        // the program through `PATH`.
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.write_config(target, contents, &validator, &reload)
}
