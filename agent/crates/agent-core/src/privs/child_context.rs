//! What a forked child can do with the channel to its parent.

use std::io::Read;
use std::os::unix::net::UnixStream;

use super::child_input_writer::ChildInputWriter;
use super::input_frame::InputFrame;

/// The child's view of its channel: it can read framed input and nothing else.
///
/// No public constructor; handed to the work closure of
/// [`fork_reporting_as_account`](super::fork_reporting_as_account::fork_reporting_as_account).
/// The report goes back through the same socket, written by the fork itself, never by the work.
pub struct ChildContext<'channel> {
    /// The child's end of the socket pair, made by the parent before the fork.
    channel: &'channel UnixStream,
    /// Room for one frame, allocated on the first read.
    buffer: Vec<u8>,
}

impl<'channel> ChildContext<'channel> {
    /// Wraps the child's end of the channel.
    pub(super) const fn new(channel: &'channel UnixStream) -> Self {
        Self {
            channel,
            buffer: Vec::new(),
        }
    }

    /// Reads the next frame: `[u32 big-endian length][bytes]`, where length `0` is the terminator.
    ///
    /// A length over [`ChildInputWriter::MAXIMUM_FRAME_BYTES`], a short read and any error all answer
    /// [`InputFrame::Aborted`]: nothing the parent sent is believed beyond a bounded block.
    pub fn read_frame(&mut self) -> InputFrame<'_> {
        let mut header = [0_u8; 4];
        if self.channel.read_exact(&mut header).is_err() {
            return InputFrame::Aborted;
        }
        let length = u32::from_be_bytes(header) as usize;
        if length == 0 {
            return InputFrame::Complete;
        }
        if length > ChildInputWriter::MAXIMUM_FRAME_BYTES {
            return InputFrame::Aborted;
        }
        self.buffer.resize(length, 0);
        if self.channel.read_exact(&mut self.buffer).is_err() {
            return InputFrame::Aborted;
        }
        InputFrame::Data(&self.buffer)
    }
}
