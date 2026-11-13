//! Refusing to remove, as a module stream, a Node.js the module system never installed.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::DistroAdapter;

use crate::apps::installed_node_major::installed_node_major;
use crate::apps::node_origin::{NodeOrigin, node_origin};
use crate::apps::{AppsHost, AppsOpError};

/// Refuses the removal of `id` (a Node.js major on a module-stream family) when the interpreter
/// of that major is on the host but no stream of it is enabled.
///
/// Such a Node.js came from the plain `nodejs` package. `module remove nodejs:<n>/common` finds
/// no profile to remove for it (`Unable to match profile`, `Nothing to do`, recorded), so the
/// dry run could never read as a plan and the unit would remove nothing: the panel would show
/// the app installed and unremovable for ever. The refusal names the operator's own command
/// instead. Any other id, and a host with no interpreter of another major, pass: whether the
/// app is installed is the caller's next question.
///
/// # Errors
///
/// Returns [`AppsOpError::UnmanagedNodeInstalled`] for such a Node.js,
/// [`AppsOpError::NodeInterpreterUnreadable`] for an interpreter that reports no major, and
/// [`AppsOpError::Package`] with a bound when a program outlived its deadline.
pub(crate) fn ensure_node_is_managed<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
) -> Result<(), AppsOpError> {
    let ValidatedAppId::Nodejs(major) = id else {
        return Ok(());
    };
    let Some(installed) = installed_node_major(host, distro, major)? else {
        return Ok(());
    };
    if installed != major.as_str() {
        return Ok(());
    }
    // Only a listing that was read and shows no stream of this major is "plain"; one that could
    // not be read leaves the removal to its own dry run, which refuses what it cannot read.
    if node_origin(host, distro, &installed)? == NodeOrigin::Plain {
        return Err(AppsOpError::UnmanagedNodeInstalled {
            requested: id.as_id(),
            installed,
            command: distro.nodejs_unmanaged_removal_command().to_owned(),
        });
    }
    Ok(())
}
