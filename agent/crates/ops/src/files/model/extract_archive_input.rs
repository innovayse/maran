//! What an extraction of one archive carries.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::archive_format::ArchiveFormat;

/// One validated request to extract an archive.
///
/// Every field is a type that cannot hold what the operation refuses: the
/// caps are required and inside the agent's ceilings, both paths are browse
/// paths, and the format is a closed enum. The archive path is never the home
/// (a directory is not an archive); the destination may be the home, which is
/// where "extract here" lands.
#[derive(Debug, Clone)]
pub struct ExtractArchiveInput {
    /// The account whose home holds the archive and receives the members.
    pub account: AccountName,
    /// The archive file, relative to the home.
    pub archive: BrowsePath,
    /// The directory the members are written into, relative to the home; created
    /// when absent.
    pub destination: BrowsePath,
    /// The container format the caller says the archive has.
    pub format: ArchiveFormat,
    /// The allowances the extraction may use up.
    pub bounds: ExtractBounds,
}
