//! Who an extraction of a backup archive runs as.

use maran_agent_core::validation::system::name::AccountName;

/// Who an extraction runs as.
///
/// Derived from [`crate::backup::model::archive_part::ArchivePart`] and never set independently — see
/// [`crate::backup::model::extract_spec::ExtractSpec::identity`] for why that is the point rather than an economy.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ExtractIdentity {
    /// The agent's own identity. Writes only into the root-only scratch.
    Root,

    /// The customer's identity, entered through `fork_as_account`.
    Account(AccountName),
}
