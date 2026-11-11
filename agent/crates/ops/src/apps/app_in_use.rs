//! Whether something on this host still uses an app.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_distro::DistroAdapter;

use crate::apps::{AppsHost, AppsOpError};
use crate::sites::node_unit_path::{declared_node_major, is_node_unit_file_name};

/// The one pool php-fpm's own package ships, which belongs to no account.
const PACKAGED_POOL: &str = "www.conf";

/// What a vhost or pool file ends with.
const CONFIG_SUFFIX: &str = ".conf";

/// Refuses to let `id` go while something on this host uses it.
///
/// The panel checks first, from its own rows, and answers `apps.inUse` itself; this is the
/// agent's check of what is ON DISK, so a panel whose rows disagree with the host cannot remove
/// an app out from under a live site:
///
/// - a web server is in use when the agent's vhost directory for it holds any `*.conf`;
/// - a PHP version is in use when its pool directory holds a pool other than the one the
///   package ships;
/// - a Node.js major is in use when the service manager's unit directory holds a per-site
///   `maran-node-*.service` unit that declares that major in its `Description=` line, or one that
///   states no major at all (the agent cannot tell what it runs, so it keeps the runtime).
///
/// # Errors
///
/// Returns [`AppsOpError::InUse`] naming what uses the app and [`AppsOpError::HostUnreadable`]
/// when a directory that would tell cannot be read (the check fails closed).
pub(crate) fn ensure_not_in_use<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<(), AppsOpError> {
    let (directory, ignored) = match id {
        ValidatedAppId::Nginx => (AgentPaths::NGINX_INCLUDE_DIRECTORY.to_owned(), None),
        ValidatedAppId::Apache => (AgentPaths::APACHE_INCLUDE_DIRECTORY.to_owned(), None),
        ValidatedAppId::Php(version) => (
            distro.php_fpm_pool_directory(version.as_str()),
            Some(PACKAGED_POOL),
        ),
        ValidatedAppId::Nodejs(major) => return ensure_no_node_site_runs(host, distro, id, major),
    };

    let users = host
        .directory_entries(Path::new(&directory))?
        .into_iter()
        .filter(|entry| entry.ends_with(CONFIG_SUFFIX) && Some(entry.as_str()) != ignored)
        .count();
    if users == 0 {
        return Ok(());
    }

    Err(AppsOpError::InUse {
        app: id.as_id(),
        users: format!(
            "{users} configuration file(s) in {directory} still serve sites or accounts"
        ),
    })
}

/// Refuses to let Node.js `major` go while a site's unit runs on it (issue #528).
fn ensure_no_node_site_runs<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    major: &NodeMajor,
) -> Result<(), AppsOpError> {
    let directory = distro.systemd_unit_directory();
    let users = host
        .directory_entries(Path::new(directory))?
        .into_iter()
        .filter(|entry| is_node_unit_file_name(entry))
        .filter(|entry| {
            let unit = host.read_config(&Path::new(directory).join(entry));
            // A unit that cannot be read or states no major is kept as a user: removing a runtime
            // out from under a site that might run on it is the harm this check exists for.
            unit.as_deref()
                .and_then(declared_node_major)
                .is_none_or(|declared| declared == major.as_str())
        })
        .count();
    if users == 0 {
        return Ok(());
    }

    Err(AppsOpError::InUse {
        app: id.as_id(),
        users: format!(
            "{users} Node.js site unit(s) in {directory} still run on Node.js {}",
            major.as_str()
        ),
    })
}

/// Whether the agent's vhost directory of the web server `id` holds any site. `false` for an app
/// that is not a web server.
///
/// # Errors
///
/// Returns [`AppsOpError::HostUnreadable`] when the directory exists and cannot be read.
pub(crate) fn holds_sites<H: AppsHost>(host: &H, id: &ValidatedAppId) -> Result<bool, AppsOpError> {
    let directory = match id {
        ValidatedAppId::Nginx => AgentPaths::NGINX_INCLUDE_DIRECTORY,
        ValidatedAppId::Apache => AgentPaths::APACHE_INCLUDE_DIRECTORY,
        _ => return Ok(false),
    };

    Ok(host
        .directory_entries(Path::new(directory))?
        .iter()
        .any(|entry| entry.ends_with(CONFIG_SUFFIX)))
}
