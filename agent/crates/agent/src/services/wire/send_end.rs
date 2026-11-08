//! Why a [`DeadlineSender`](super::deadline_sender::DeadlineSender) stopped sending.

/// The two ways a bounded send into a response stream's channel ends without
/// delivering the message.
///
/// They stay apart because they have different owners. A closed channel is the
/// client's own decision and nobody is left to be told; a stalled one is the
/// agent's decision, taken while the stream was still open, and the operator is
/// owed an explanation. Each stream maps them onto its own vocabulary
/// (`TailEnd` for a log tail, `SinkStopped` for a file read).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SendEnd {
    /// The receiving half is gone: tonic tore the stream down.
    Closed,
    /// The channel was still full at the deadline: the client is connected and
    /// is not reading.
    Stalled,
}
