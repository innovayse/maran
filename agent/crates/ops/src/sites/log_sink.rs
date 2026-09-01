//! Where a log tail's lines go, and how it learns nobody is listening.

use crate::sites::model::tail_end::TailEnd;

/// The receiving end of a log tail.
///
/// Two methods rather than one callback returning `bool`, and the second is
/// the important one. A tail whose only liveness signal is the return value of
/// a line delivery learns that its client is gone only when a line arrives —
/// so a tail opened on a site with no traffic, and then dropped, polls forever.
/// A `spawn_blocking` task cannot be aborted, so nothing else reclaims it, and
/// enough of them exhaust the pool every operation in the agent needs.
///
/// **[`Self::line`] MUST return within a bounded time.** The follow loop checks
/// [`Self::is_listening`] and its idle deadline at the TOP of each poll, so a
/// `line` that parks forever is parked *between* two checks and neither guard
/// can ever fire. A client that stops reading without closing its stream — an
/// exhausted HTTP/2 window, a suspended laptop — is exactly that case: the
/// channel behind the sink fills, and a plain blocking send would hold a
/// blocking-pool thread for the life of the process. The bound has to sit on
/// the call that can block, not on the loop around it, so it is part of this
/// contract rather than of the caller's.
///
/// [`Self::is_listening`] must be cheap and must not block at all.
pub trait LogSink {
    /// Delivers one line. `historical` is true for the batch read before the
    /// follow began.
    ///
    /// Returns `Err` with the reason the tail should stop — and the reason
    /// rather than a bare `false`, because the two ways a delivery can fail are
    /// not the same event: [`TailEnd::ClientClosed`] is the client's own
    /// decision and there is nobody left to tell, while
    /// [`TailEnd::ClientStalled`] is the agent dropping a client that stopped
    /// reading, which the operator must be told about.
    ///
    /// Must return within a bounded time; see the trait's own documentation for
    /// why that obligation is here and not in the follow loop.
    ///
    /// # Errors
    ///
    /// Returns the ending the tail should report when the line could not be
    /// delivered.
    fn line(&mut self, line: &str, historical: bool) -> Result<(), TailEnd>;

    /// Whether anyone is still reading, asked at the top of every poll
    /// independently of whether a line arrived.
    fn is_listening(&mut self) -> bool;
}
