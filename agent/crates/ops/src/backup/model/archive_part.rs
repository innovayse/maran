//! Which member of a backup archive an extraction is for.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

/// Which part of the archive an extraction is for.
///
/// The archive's internal layout is fixed and is part of the contract (R2), so
/// this enum is closed: there is no "some other member" case, because a member
/// outside the three top-level ones is an archive this agent does not
/// understand and is refused by the pre-scan rather than placed. Two of the
/// variants name a SUBSET of a top-level member, for the single-database and
/// single-directory restores (issue #160): they select less, and never a name
/// the layout does not already allow.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ArchivePart {
    /// `manifest.json` — read root-side, before anything is trusted.
    Manifest,

    /// `databases/` — the SQL dumps, read root-side (R4).
    Databases,

    /// `databases/<database>.sql` — ONE dump, read root-side (R4) for the same
    /// reason the whole `databases/` member is. The member name is derived from
    /// the validated [`DatabaseName`], which has no separator in its alphabet.
    Database {
        /// The database whose dump is extracted.
        database: DatabaseName,
    },

    /// `home/` — the account's files, read AS the account (R3).
    Home {
        /// The account whose identity the extraction runs under.
        account: AccountName,
    },

    /// `home/<directory>` — one directory of the account's files, read AS the
    /// account (R3), with the leading components stripped so that the directory
    /// itself lands directly in the extraction target.
    Directory {
        /// The account whose identity the extraction runs under.
        account: AccountName,
        /// The directory to extract, relative to the home.
        directory: RelativePath,
    },
}
