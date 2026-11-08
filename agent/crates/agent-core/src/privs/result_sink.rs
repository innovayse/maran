//! The write-only handle through which a dropped child hands bytes back to the root parent.

use std::io::{self, PipeWriter, Write};

/// The child's end of the bounded byte channel: it can write, and it can do nothing else.
///
/// Handed to the work closure of
/// [`fork_as_account_for_bytes`](super::fork_as_account_for_bytes::fork_as_account_for_bytes)
/// and nowhere else; the type has no public constructor. It borrows the pipe's write end, which
/// the parent created before the fork and keeps the far end of, so the child holds exactly one
/// descriptor beyond stdio and that descriptor can only append to a buffer the parent bounds.
///
/// It enforces no size of its own. The cap is the PARENT's: a child is the party that might be
/// confused or hostile, and a limit it applied to itself would be a limit only a well-behaved
/// child obeys. A write past the cap is accepted here and is the parent's cue to stop reading
/// and kill the child.
///
/// A write after the parent has stopped listening fails with `EPIPE` (the Rust runtime ignores
/// `SIGPIPE`, so the child gets an error and not a signal), and a work closure that propagates it
/// ends the child with a work-failed status. Nothing the child writes is a result until the
/// parent has also seen the child exit successfully.
pub struct ResultSink<'channel> {
    /// The pipe's write end, borrowed from the parent's frame for the length of the call.
    writer: &'channel PipeWriter,
}

impl<'channel> ResultSink<'channel> {
    /// Wraps the write end the parent made for this call.
    pub(super) const fn new(writer: &'channel PipeWriter) -> Self {
        Self { writer }
    }
}

impl Write for ResultSink<'_> {
    /// Writes to the pipe; `EINTR` is retried by [`Write::write_all`], which is what callers use.
    fn write(&mut self, buffer: &[u8]) -> io::Result<usize> {
        let mut writer = self.writer;
        writer.write(buffer)
    }

    /// A pipe has no userspace buffer to flush.
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
