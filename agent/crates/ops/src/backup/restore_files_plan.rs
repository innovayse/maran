//! What a restore does to the account's files.

use maran_agent_core::validation::fs::relative_path::RelativePath;

/// What a restore does to the account's files, decided by its scope and carried
/// on the [`RestorePlan`](crate::backup::restore_plan::RestorePlan) so that the
/// irreversible half reads one answer instead of re-deriving it.
///
/// One meaning per variant, and none of them a flag: "swap the whole home",
/// "leave the files alone" and "replace this one directory" are three different
/// operations with three different recovery stories, not a bool and an option
/// that could be set in a combination nobody tested.
pub(super) enum RestoreFilesPlan {
    /// Swap the whole home for the staging tree, under the restore marker.
    WholeHome,

    /// Touch no file: a single-database restore.
    None,

    /// Replace this one directory of the home with the staging tree's copy of it.
    Directory(RelativePath),
}
