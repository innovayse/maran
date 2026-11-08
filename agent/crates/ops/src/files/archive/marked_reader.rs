//! A reader that remembers whether the failure was its own.

use std::io::{self, Read};

/// Wraps the source of a file being archived.
///
/// An archive crate copies from the source into its output and returns one
/// `io::Error` for either side failing. The two mean different things to the
/// customer (the file could not be read, versus the archive could not be
/// written, a full quota among them), so the reader notes when the error came
/// from it.
pub(crate) struct MarkedReader<'a> {
    /// The source.
    inner: &'a mut dyn Read,
    /// Whether a read has failed.
    failed: bool,
}

impl<'a> MarkedReader<'a> {
    /// Wraps `inner`.
    pub(crate) fn new(inner: &'a mut dyn Read) -> Self {
        Self {
            inner,
            failed: false,
        }
    }

    /// Whether a read of the source has failed.
    pub(crate) fn read_failed(&self) -> bool {
        self.failed
    }
}

impl Read for MarkedReader<'_> {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        let outcome = self.inner.read(buffer);
        if outcome.is_err() {
            self.failed = true;
        }
        outcome
    }
}
