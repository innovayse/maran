//! Why a reboot probe gave no answer.

/// A reason the reboot probe's run could not be read as an answer
/// (issue #163).
///
/// Every variant means the agent did NOT find out. None is "no reboot is
/// needed": a probe that failed and was read as that would tell the operator a
/// host is safe to leave. Like [`crate::PendingUpdatesFailure`], no variant
/// carries a byte the tool printed; the one number held is an exit status.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum RebootProbeFailure {
    /// The probe program exited with a status its documentation gives no
    /// answer to: `needs-restarting -r` answers 0 and 1 and anything else,
    /// 127 for a program that is not installed included, is an error.
    #[error("the reboot probe exited with status {status}")]
    Unsuccessful {
        /// The exit status the probe returned.
        status: i32,
    },
    /// An exit status was offered to read, but this family's probe is a file
    /// whose presence is the answer, so there is no run to read.
    #[error("this family's reboot probe is a file and has no program run to read")]
    NotAProgramProbe,
}
