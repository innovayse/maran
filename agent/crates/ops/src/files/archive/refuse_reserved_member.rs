//! Keeping an archive out of the account's trash.

use maran_agent_core::agent_paths::AgentPaths;

use super::archive_member::ArchiveMember;
use super::archive_refusal::ArchiveRefusal;

/// Refuses a member that would be written into the trash directory.
///
/// The trash (`.maran-trash`, directly in the home) is reachable only through
/// the trash operations: they check what they read back from it, and nothing
/// else is meant to put files there. An archive extracted into the home whose
/// first member component is that name would write straight into it, so the
/// destination decides: `destination_is_home` is true only when the member's
/// first component IS a component of the home. Below any other destination the
/// name is an ordinary directory.
///
/// # Errors
///
/// [`ArchiveRefusal::UnsafeName`] for such a member.
pub(crate) fn refuse_reserved_member(
    destination_is_home: bool,
    member: &ArchiveMember,
) -> Result<(), ArchiveRefusal> {
    if destination_is_home
        && member.name.components().next() == Some(AgentPaths::TRASH_DIRECTORY_NAME)
    {
        return Err(ArchiveRefusal::UnsafeName);
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/files/archive/refuse_reserved_member_tests.rs"]
mod tests;
