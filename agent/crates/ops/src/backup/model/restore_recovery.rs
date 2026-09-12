//! What one startup reconciliation actually did.

/// The tally a startup reconciliation answers with.
///
/// A value and not a `()`, for the reason rules/testing.md gives: the case that
/// matters most about a reconciler is that it does **nothing** to healthy state,
/// and "nothing happened" is not observable unless the function says so. A test
/// that could only assert the home is still there would pass just as well
/// against a reconciler that moved the home away and back.
///
/// It is also what the daemon logs at startup. An operator reading
/// `swaps_completed=1` in the journal is reading the one line that says a
/// customer's home was put back, and `refused=1` is the line that says one was
/// not and needs a human.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct RestoreRecovery {
    /// Swaps found mid-window and carried forward to the state a successful
    /// restore leaves: the staging tree became the home.
    pub swaps_completed: u32,

    /// Swaps found mid-window whose forward completion failed, and whose parked
    /// home was therefore renamed back. The customer has a working home.
    pub swaps_rolled_back: u32,

    /// Swaps found already finished — the marker outlived its own restore.
    /// Nothing was moved for these.
    pub swaps_already_done: u32,

    /// Swaps found before the first rename. Their staging tree was removed and
    /// the live home was not touched.
    pub swaps_abandoned: u32,

    /// Markers this reconciliation refused to act on: unreadable, of an unknown
    /// version, naming something that does not validate, in a state no
    /// automatic answer is safe for, or belonging to an account whose backup
    /// lock is held. Each one is a log line naming what a human must look at.
    pub refused: u32,

    /// Directories in the staging root with no marker at all. Left in place and
    /// logged: without a marker the account is only inferable from the name.
    pub unmarked_left: u32,

    /// Rollback dump sets under the bulk scratch that survived this start.
    pub rollback_sets_kept: u32,

    /// Scratch directories removed by this start — the reproducible half, plus
    /// rollback sets past the retention cap.
    pub scratch_entries_removed: u32,
}
