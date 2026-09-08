//! The group a restored home must end up owned by.

use maran_agent_core::privs::group_id::GroupId;
use maran_distro::DistroAdapter;

use crate::proto::AgentError;
use crate::services::wire::system_failure::system_failure;

/// Resolves the gid a restore re-applies to the account's home root.
///
/// `AccountOperations` creates a home as `<account>:<web server group>` mode
/// `0750`, because the web server has to traverse it to serve the account's
/// sites. A restore that put the account's own group back instead would break
/// every one of those sites, silently, with a 403 nobody can explain from the
/// panel — so the finalising step re-applies exactly that arrangement, and this
/// is where the group's NAME becomes a NUMBER.
///
/// The name is the distro adapter's, because the two families spell it
/// differently, and the number is the host's, because a name is not a gid on
/// any of them. Nothing about this is a decision the service makes — which is
/// why it is a named unit here rather than two lines inside the handler
/// (rules/rust.md "Service anatomy").
///
/// # Errors
///
/// A system failure when the group does not exist on this host, when the group
/// database could not be read, or when the name resolves to gid 0. All three
/// refuse the restore rather than falling back to the account's own group: a
/// home whose group is guessed is a home that either stops serving or is
/// readable by a process that should not reach it, and both are worse than a
/// restore an operator is told about.
pub fn home_group(distro: &dyn DistroAdapter) -> Result<u32, AgentError> {
    GroupId::resolve(distro.web_server_group())
        .map(|group| group.gid())
        .map_err(|error| system_failure(format!("the web server's group is unusable: {error}")))
}
