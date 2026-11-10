//! Handing a rendered vhost to the one config-write protocol.

use std::path::Path;

use maran_distro::{DistroAdapter, SiteWebServer};

use crate::safe_write::model::Reload;
use crate::sites::web_server_validator::web_server_validator;
use crate::sites::{SiteHost, SitesOpError};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// The subcommand that makes the service manager re-read a configuration.
///
/// Shared with the removal path, which must reload the same way: two spellings
/// of "reload nginx" is how one of them ends up not reloading anything.
pub(crate) const RELOAD_SUBCOMMAND: &str = "reload";

/// The argument that makes the web server check its configuration instead of serving.
///
/// The same word for nginx (`nginx -t`) and for Apache (`apachectl -t`).
pub(crate) const VALIDATE_ARGUMENT: &str = "-t";

/// Writes `contents` to `target` with `web_server`'s validator and reload attached.
///
/// Every site operation that REPLACES a configuration goes through here; the
/// one that introduces a vhost, `create_site`, goes through `create_vhost`,
/// which builds the same validator and reload from the same constants. Both
/// come from the
/// [`DistroAdapter`] — the binary path and the service name differ between
/// families, and an operation that wrote either as a literal would be guessing
/// (rules/rust.md "Distro adapter").
///
/// # Errors
///
/// Returns [`SitesOpError::NginxValidation`] when the web server's `-t` rejects the
/// rendered configuration and [`SitesOpError::ReloadFailed`] when the reload
/// refuses it — in both cases the previous vhost has already been restored, or
/// removed if the site had none. Returns [`SitesOpError::Bound`] when the
/// validator or the reload outlived its deadline and was killed, again with the
/// previous vhost restored; a bound is never reported as a failed validation or
/// reload. Returns [`SitesOpError::ConfigWrite`] for every other failure of the
/// protocol, including a rollback that itself failed.
pub(crate) fn write_vhost(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    web_server: SiteWebServer,
    target: &Path,
    contents: &str,
) -> Result<(), SitesOpError> {
    let binary = distro.site_web_server_binary(web_server);
    let service_manager = distro.service_manager();
    let validator = web_server_validator(&binary);
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

    host.write_config(target, contents, &validator, &reload)
}

#[cfg(test)]
#[path = "../tests/sites/write_vhost_tests.rs"]
mod tests;
