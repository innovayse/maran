//! The parent's bounded handle for feeding a forked child.

use std::io::{self, Write};
use std::os::unix::net::UnixStream;
use std::time::{Duration, Instant};

/// Shortest socket timeout, because a zero timeout is an error and not "no wait".
const SHORTEST_WAIT: Duration = Duration::from_millis(1);

/// The parent's end of the input channel, bound to the call's deadline.
///
/// Handed to the `input` closure of
/// [`fork_reporting_as_account`](super::fork_reporting_as_account::fork_reporting_as_account);
/// no public constructor. Every send waits for room with the deadline, so a child that stops
/// reading cannot hold the parent past it. The stream is framed `[u32 big-endian
/// length][bytes]`; [`ChildInputWriter::complete`] sends the zero-length terminator. A closure that
/// returns without completing (or with an error) is an ABORT: the child sees the channel end with
/// no terminator and must not publish what it was writing.
pub struct ChildInputWriter<'channel> {
    /// The parent's end of the socket pair.
    channel: &'channel UnixStream,
    /// When the whole call gives up.
    deadline: Instant,
}

impl<'channel> ChildInputWriter<'channel> {
    /// Most bytes in one frame. Larger sends are split; the child refuses a larger frame.
    pub const MAXIMUM_FRAME_BYTES: usize = 64 * 1024;

    /// Binds the channel to the deadline.
    pub(super) const fn new(channel: &'channel UnixStream, deadline: Instant) -> Self {
        Self { channel, deadline }
    }

    /// Sends `bytes` as one or more frames; an empty slice sends nothing.
    ///
    /// # Errors
    ///
    /// [`io::ErrorKind::TimedOut`] when the deadline passes first; the operating system's
    /// error otherwise (`EPIPE` when the child has finished or refused).
    pub fn send(&mut self, bytes: &[u8]) -> io::Result<()> {
        for block in bytes.chunks(Self::MAXIMUM_FRAME_BYTES) {
            // `chunks` of at most 64 KiB always fit a u32.
            let length = u32::try_from(block.len()).unwrap_or(u32::MAX);
            self.write_all_by_deadline(&length.to_be_bytes())?;
            self.write_all_by_deadline(block)?;
        }
        Ok(())
    }

    /// Sends the terminator: the child may now publish what it received.
    ///
    /// # Errors
    ///
    /// As [`ChildInputWriter::send`].
    pub fn complete(&mut self) -> io::Result<()> {
        self.write_all_by_deadline(&0_u32.to_be_bytes())
    }

    /// The raw channel, so a test can play a hostile parent that frames badly.
    #[cfg(test)]
    pub(super) fn raw_channel_for_tests(&self) -> &UnixStream {
        self.channel
    }

    /// Writes every byte or fails, never waiting past the deadline.
    fn write_all_by_deadline(&mut self, mut bytes: &[u8]) -> io::Result<()> {
        while !bytes.is_empty() {
            let remaining = self.deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                return Err(io::Error::from(io::ErrorKind::TimedOut));
            }
            self.channel
                .set_write_timeout(Some(remaining.max(SHORTEST_WAIT)))?;
            let mut channel = self.channel;
            match channel.write(bytes) {
                Ok(0) => return Err(io::Error::from(io::ErrorKind::WriteZero)),
                Ok(written) => bytes = &bytes[written..],
                Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
                Err(error) if error.kind() == io::ErrorKind::WouldBlock => {
                    return Err(io::Error::from(io::ErrorKind::TimedOut));
                }
                Err(error) => return Err(error),
            }
        }
        Ok(())
    }
}
