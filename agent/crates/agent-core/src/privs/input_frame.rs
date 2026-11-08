//! One thing the child reads from its input channel.

/// What [`ChildContext::read_frame`](super::child_context::ChildContext::read_frame) found.
///
/// The three cases are the point: a stream that ends WITHOUT the terminator is not a
/// stream that finished, and a child that wrote a file from it must not publish the file.
#[derive(Debug, PartialEq, Eq)]
pub enum InputFrame<'buffer> {
    /// A non-empty block of bytes, valid until the next read.
    Data(&'buffer [u8]),
    /// The parent sent the terminator: everything was delivered.
    Complete,
    /// The channel ended, broke, or carried a frame over the ceiling before any terminator:
    /// the parent gave up, crashed, or was not sending. The upload is NOT complete.
    Aborted,
}
