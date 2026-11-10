//! How one run of `rsync` ended.

/// The outcome of one time-sliced `rsync` run.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RsyncSliceOutcome {
    /// `rsync` exited with a status that means the copy is complete (0, or 24:
    /// source files vanished while it ran, which on a live source is expected).
    Finished,
    /// The run reached its deadline and was killed with its process group; the
    /// partial state is kept and the next run continues from it.
    SliceEnded,
    /// `rsync` exited with a status that is a failure.
    Failed {
        /// The exit status, 1..=255.
        status: i32,
    },
}

impl RsyncSliceOutcome {
    /// The `rsync` exit status that means files vanished from the source while it
    /// ran. On a live source this is expected and not a failure.
    const VANISHED: i32 = 24;

    /// Reads the exit status of an `rsync` run that was not killed.
    ///
    /// 0 and 24 end the copy; any other status is a failure. A run that reached
    /// its deadline has no status and is [`Self::SliceEnded`] by the host that
    /// killed it.
    #[must_use]
    pub fn of_status(status: i32) -> Self {
        if status == 0 || status == Self::VANISHED {
            Self::Finished
        } else {
            Self::Failed { status }
        }
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/model/rsync_slice_outcome_tests.rs"]
mod tests;
