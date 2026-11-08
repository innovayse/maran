//! A reader that refuses to deliver more than it was allowed.

use std::io::{self, Read};

/// Wraps the decompressed stream of a `.tar.gz` and ends it past a ceiling.
///
/// The tar reader only consumes what its headers declare, so the stream's
/// length is otherwise decided by the archive: padding, an endless run of zero
/// blocks after the end marker, or a gzip member that never ends. This puts a
/// bound the archive does not control under all of it. Past the bound the read
/// fails with [`io::ErrorKind::FileTooLarge`], which the walk reads as "the
/// stream outgrew the allowance", and which no gzip decoder produces by itself.
pub(crate) struct CountingReader<R> {
    /// The stream being bounded.
    inner: R,
    /// How many more bytes may be delivered.
    remaining: u64,
}

impl<R: Read> CountingReader<R> {
    /// Bounds `inner` to `limit` bytes.
    pub(crate) fn new(inner: R, limit: u64) -> Self {
        Self {
            inner,
            remaining: limit,
        }
    }
}

impl<R: Read> Read for CountingReader<R> {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        // One byte more than is left is asked for, so the first byte past the
        // bound is SEEN and refused instead of silently being the last one.
        let allowed = usize::try_from(self.remaining.saturating_add(1))
            .map_or(buffer.len(), |allowed| allowed.min(buffer.len()));
        let read = self.inner.read(&mut buffer[..allowed])?;
        let read_bytes = read as u64;
        if read_bytes > self.remaining {
            return Err(io::Error::from(io::ErrorKind::FileTooLarge));
        }
        self.remaining -= read_bytes;
        Ok(read)
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/counting_reader_tests.rs"]
mod tests;
