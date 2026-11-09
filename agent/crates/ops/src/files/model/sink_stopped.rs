//! The answer of a chunk sink whose reader has gone away.

/// The reader of a streamed read is gone, so sending more is pointless.
///
/// A value and not an error enum: it carries no reason, because the only thing
/// the reader's absence tells the sender is to stop. It is not a failure of the
/// read either — nobody is left to be told one.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SinkStopped;
