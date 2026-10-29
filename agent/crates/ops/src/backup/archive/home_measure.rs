//! What one walk of an account's home found: how many bytes and how many members.

/// The result of measuring an account's home.
///
/// Both numbers come from the SAME walk, so they describe the same tree: the
/// bytes go into the manifest, and the member count is what `create_backup`
/// compares against the most members a restore will accept before it has
/// started any work.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct HomeMeasure {
    /// The summed size of every entry the walk visited, as `tar` counts it:
    /// a link counts as itself and a directory contributes nothing.
    pub(crate) bytes: u64,
    /// How many entries the walk visited: files, symlinks, other special
    /// files and directories alike, the home itself excluded. This is the
    /// number of home members the archive will hold (the manifest and the
    /// dumps add a handful more).
    pub(crate) members: u64,
}
