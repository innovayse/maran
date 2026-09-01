//! Why a log tail stopped.

/// The three ways a tail ends, only one of which the client asked for.
///
/// A tail that returns `Ok(())` for all three tells the operator nothing: a
/// client that closed its tab, a client the agent gave up on because it stopped
/// reading, and a log that has said nothing for five minutes all look like a
/// stream that simply ended. Two of those are the agent's decision, and an
/// operator watching a log needs to know when the thing they are watching was
/// taken away from them — a silent truncation of exactly that is the failure
/// this whole rpc exists to prevent.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TailEnd {
    /// The client closed the stream. Voluntary, and reported to nobody: there
    /// is no longer anyone to report it to.
    ClientClosed,
    /// The client stopped reading without closing, and the agent gave up
    /// waiting for it. Involuntary, and worth saying: the stream the operator
    /// is looking at has stopped and it was not their doing.
    ClientStalled,
    /// Nothing was written to the log for the tail's maximum idle time.
    /// Involuntary, and benign — but still an ending the operator did not ask
    /// for, and one they would otherwise read as "this site stopped getting
    /// traffic" rather than "the agent stopped watching".
    Idle,
}

impl TailEnd {
    /// Whether the operator should be told about this ending.
    ///
    /// The one thing a caller ever needs to decide from this type, written here
    /// so that a second caller cannot decide it differently.
    #[must_use]
    pub fn is_involuntary(self) -> bool {
        matches!(self, Self::ClientStalled | Self::Idle)
    }
}
