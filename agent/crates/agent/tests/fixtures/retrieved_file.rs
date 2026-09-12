//! What one retrieval through an open FTPS control session produced.

/// What one retrieval through an open session produced.
///
/// The three parts are kept apart rather than folded into one transcript
/// because they are three different claims: the daemon ALLOWED the transfer,
/// the bytes that actually crossed the data channel, and the daemon considered
/// the transfer COMPLETE. A test that only looked at the last of them would
/// pass on a transfer that carried nothing.
pub struct RetrievedFile {
    /// The reply to `RETR` — `150` when the transfer was allowed to start.
    pub began: String,
    /// Everything the data channel carried.
    pub bytes: String,
    /// The reply that closed the transfer — `226` when it completed.
    pub finished: String,
}
