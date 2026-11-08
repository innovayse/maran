//! Everything the swap of one restored directory is told, as one value.

use std::path::PathBuf;

use maran_agent_core::validation::fs::relative_path::RelativePath;

/// The three locations and the one name a single-directory swap works with.
///
/// Every path in it is agent-derived (the home from `AgentPaths`, the other two
/// from the restore's own staging and parking directories); only
/// [`Self::directory`] came from a request, and it is a [`RelativePath`], so it
/// cannot leave the home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DirectorySwap {
    /// The account's home, `/home/<account>` on a real host.
    pub home: PathBuf,

    /// The account-owned directory the archive's copy was extracted into. The
    /// restored tree is its entry named like the last component of
    /// [`Self::directory`].
    pub staging: PathBuf,

    /// The account-owned, empty directory the displaced tree is moved into until
    /// the restore has finished. Separate from [`Self::staging`] so that the
    /// cleanup which removes the staging tree on every path can never delete a
    /// tree that is still the only copy of the customer's previous site.
    pub parked: PathBuf,

    /// The directory being replaced, relative to [`Self::home`].
    pub directory: RelativePath,
}
