//! The per-stream output caps of one supervised capture.

use crate::utils::output_stream::OutputStream;

/// The per-stream caps a capture runs under, or zeros when nothing is captured.
#[derive(Debug, Clone, Copy)]
pub(super) struct Caps {
    /// The standard output cap.
    pub(super) stdout: u64,
    /// The standard error cap.
    pub(super) stderr: u64,
}

impl Caps {
    /// The cap that applies to `stream`.
    pub(super) const fn of(self, stream: OutputStream) -> u64 {
        match stream {
            OutputStream::Stdout => self.stdout,
            OutputStream::Stderr => self.stderr,
        }
    }
}
