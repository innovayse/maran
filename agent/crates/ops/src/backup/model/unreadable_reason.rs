//! Why a listed backup's sidecar could not be trusted.

use serde::{Deserialize, Serialize};

/// Why [`BackupSummary::is_readable`](crate::backup::model::backup_summary::BackupSummary::is_readable)
/// is `false` for one entry a listing reports.
///
/// A listing already refuses to skip an entry it cannot vouch for — see
/// [`BackupSummary`](crate::backup::model::backup_summary::BackupSummary)'s own
/// doc for why silence there is the defect. This type exists because "cannot
/// vouch for" is not one fact: a sidecar whose JSON will not parse is
/// **probably damaged**, while a sidecar that parses fine but carries a
/// version this agent has never heard of is **probably fine**, written by an
/// agent (older or newer) that understands a shape this one does not — the
/// exact argument [`BackupManifest`](crate::backup::model::backup_manifest::BackupManifest)
/// already makes for refusing rather than guessing at an unknown version.
/// Collapsing the two into one "unreadable" would tell an operator staring at
/// a disk full of skipped-looking entries that a routine version skew and
/// actual corruption are the same problem, which is the same mistake
/// `PublicReadVerdict` exists to avoid making about "could not observe".
///
/// [`Self::NotARegularFile`] joins them for the same reason and not by
/// analogy: it is a third situation calling for a third reaction — nothing is
/// wrong with any sidecar, because the thing wearing the artifact's name is
/// not a file that could have one.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub enum UnreadableReason {
    /// The sidecar was missing, or its JSON did not parse into any shape this
    /// agent has ever written.
    Corrupt,

    /// The sidecar parsed, but named a version this agent does not
    /// understand and therefore will not read past.
    UnknownVersion {
        /// The version recorded in the sidecar. `0` when the sidecar predates
        /// the version field entirely — an agent from before this field
        /// existed never wrote one, and that absence is itself an unknown
        /// version rather than a parse failure.
        version: u32,
    },

    /// Something wearing an artifact's name — `<id>.tar.gz` with an id this
    /// agent really could have minted — is not a regular file. A directory
    /// and a symbolic link both wear that name convincingly, and both pass
    /// every test a name can be given.
    ///
    /// Reported rather than skipped, and rather than listed as an ordinary
    /// backup, because both of the other answers are wrong in a way that
    /// lasts:
    ///
    /// - Listed as ordinary, a directory is an entry retention counts, tries
    ///   to prune, and can never prune — `delete_backup`'s `remove_file`
    ///   fails on it forever — and a symbolic link is an artifact a restore
    ///   would read THROUGH, to bytes chosen by whoever placed the link.
    /// - Skipped, it is the invisible-to-retention outcome this whole module
    ///   is built to refuse.
    ///
    /// Reported unreadable, it is litter with a name and a reason, in front
    /// of the operator who can remove it. The distinction is made by asking
    /// the directory entry's own type, which `read_dir` already answered and
    /// which does not follow a link.
    NotARegularFile,
}

#[cfg(test)]
#[path = "../../tests/backup/unreadable_reason_tests.rs"]
mod tests;
