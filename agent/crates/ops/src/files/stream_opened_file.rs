//! Pumping an already opened file into a sink, bounded.

use std::fs::File;
use std::io::{ErrorKind, Read};
use std::os::unix::fs::MetadataExt;

use crate::files::FilesOpError;
use crate::files::model::chunk_sink::ChunkSink;

/// Bytes read and sent at a time: 64 KiB.
const CHUNK_BYTES: usize = 64 * 1024;

/// Sends the content of `file` to `sink`, refusing anything over `max_bytes`.
///
/// The parent's half of the read. The descriptor arrived from a child that ran
/// as the customer, so nothing it claims is trusted: the file is judged again
/// here, on the descriptor, before one byte is read — a regular file with
/// exactly one link, and no larger than `max_bytes`. The size is read from the
/// descriptor and is not enough on its own: a customer can grow the file while
/// it is being read, so the bytes are COUNTED as they stream and the read ends
/// with [`FilesOpError::TooLarge`] the moment the count passes the limit (the
/// excess is never sent). The panel discards a body that ended in an error.
///
/// Returns the bytes sent. A sink that stops ends the read with what was sent
/// so far: the reader left, which is not a failure of the file.
///
/// # Errors
///
/// Returns [`FilesOpError::NotARegularFile`] when the descriptor is not a
/// regular file with a single link, [`FilesOpError::TooLarge`] when the file is
/// or becomes larger than `max_bytes`, and [`FilesOpError::ReadFailed`] when a
/// read fails.
pub(crate) fn stream_opened_file(
    file: File,
    max_bytes: u64,
    sink: &mut dyn ChunkSink,
) -> Result<u64, FilesOpError> {
    let metadata = file.metadata().map_err(|_| FilesOpError::NotARegularFile)?;
    if !metadata.is_file() || metadata.nlink() != 1 {
        return Err(FilesOpError::NotARegularFile);
    }
    if metadata.len() > max_bytes {
        return Err(FilesOpError::TooLarge);
    }

    // One byte past the limit is allowed through so that growth is SEEN: a
    // reader capped at exactly the limit would end quietly at the limit.
    let mut reader = file.take(max_bytes.saturating_add(1));
    let mut buffer = vec![0_u8; CHUNK_BYTES];
    let mut sent: u64 = 0;

    loop {
        let read = match reader.read(&mut buffer) {
            Ok(0) => return Ok(sent),
            Ok(read) => read,
            Err(error) if error.kind() == ErrorKind::Interrupted => continue,
            Err(_) => return Err(FilesOpError::ReadFailed),
        };

        let total = sent.saturating_add(read as u64);
        if total > max_bytes {
            return Err(FilesOpError::TooLarge);
        }
        if sink.send(&buffer[..read]).is_err() {
            return Ok(sent);
        }
        sent = total;
    }
}

#[cfg(test)]
#[path = "../tests/files/stream_opened_file_tests.rs"]
mod tests;
