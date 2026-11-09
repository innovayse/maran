//! Keeping every path an archive operation names out of the account's trash.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::archive_refusal::ArchiveRefusal;

/// Refuses a path whose first component is the trash directory.
///
/// The trash (`.maran-trash`, directly in the home) is reachable only through
/// the trash operations, which check what they read back from it. The service
/// layer refuses such a path before the fork; this is the second layer, inside
/// the operation itself, so an archive operation reached by any other caller
/// still cannot read from or write into the trash. It applies to every path an
/// operation names: the archive to extract, the extraction's destination, each
/// source of a creation and the archive a creation writes. A member's name is
/// judged separately by [`super::refuse_reserved_member::refuse_reserved_member`],
/// because whether a member lands in the trash depends on the destination.
///
/// Only the first component is the trash: the same name deeper in the tree is
/// an ordinary directory.
///
/// # Errors
///
/// [`ArchiveRefusal::UnsafeName`] for such a path.
pub(crate) fn refuse_reserved_location(path: &BrowsePath) -> Result<(), ArchiveRefusal> {
    if path.components().first().map(String::as_str) == Some(AgentPaths::TRASH_DIRECTORY_NAME) {
        return Err(ArchiveRefusal::UnsafeName);
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/files/archive/refuse_reserved_location_tests.rs"]
mod tests;
