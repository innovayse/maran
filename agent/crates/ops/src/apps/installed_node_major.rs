//! The Node.js major the host resolves, asked so that a broken interpreter is not "nothing there".

use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_distro::DistroAdapter;

use crate::apps::installed_probe::node_version;
use crate::apps::{AppsHost, AppsOpError};

/// The major (its digits) of the Node.js interpreter on this host, `None` when there is no
/// interpreter file at all.
///
/// Fails closed. A file that is there but whose `--version` gives no major (a non-zero exit, a
/// crash, a missing library after a half-done update, an answer that is not a version) is NOT
/// "nothing installed": the module install that would follow switches the stream under the
/// installed packages, and a host that cannot say what is on it is not one to do that to. The
/// path is the adapter's `node_binary` (the one file a module-stream family has), asked of the
/// filesystem and not of the package manager.
///
/// # Errors
///
/// Returns [`AppsOpError::NodeInterpreterUnreadable`] for an interpreter file that does not
/// report a major, and [`AppsOpError::Package`] with a bound when it outlived its deadline.
pub(crate) fn installed_node_major<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    requested: &NodeMajor,
) -> Result<Option<String>, AppsOpError> {
    let binary = distro.node_binary(requested);
    if !host.path_exists(binary.as_path()) {
        return Ok(None);
    }
    let unreadable = || AppsOpError::NodeInterpreterUnreadable {
        path: binary.as_str().to_owned(),
    };
    let version = node_version(host, distro, requested)?.ok_or_else(unreadable)?;
    let major = version.split('.').next().unwrap_or_default();
    if major.is_empty() || !major.chars().all(|character| character.is_ascii_digit()) {
        return Err(unreadable());
    }
    Ok(Some(major.to_owned()))
}
