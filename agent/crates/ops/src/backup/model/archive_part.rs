//! Which member of a backup archive an extraction is for.

use maran_agent_core::validation::system::name::AccountName;

/// Which of the archive's three members an extraction is for.
///
/// The archive's internal layout is fixed and is part of the contract (R2), so
/// this enum is closed: there is no "some other member" case, because a member
/// outside these three is an archive this agent does not understand and is
/// refused by the pre-scan rather than placed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ArchivePart {
    /// `manifest.json` — read root-side, before anything is trusted.
    Manifest,

    /// `databases/` — the SQL dumps, read root-side (R4).
    Databases,

    /// `home/` — the account's files, read AS the account (R3).
    Home {
        /// The account whose identity the extraction runs under.
        account: AccountName,
    },
}
