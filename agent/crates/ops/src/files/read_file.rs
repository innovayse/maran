//! Streaming one file out of a customer's home.

use crate::files::FilesOpError;
use crate::files::browse_host::BrowseHost;
use crate::files::model::chunk_sink::ChunkSink;
use crate::files::model::read_file_input::ReadFileInput;
use crate::files::stream_opened_file::stream_opened_file;

/// Reads the file `input` names and sends it to `sink` in chunks.
///
/// Three steps: ask the host for an open descriptor to the file (the host forks
/// to the account, which walks and opens it, and re-checks what it hands back),
/// then pump the descriptor into the sink under the caller's byte limit. The
/// agent never reads a customer's file as root by path: the parent only ever
/// reads a descriptor the account opened.
///
/// Cancellation class: **stop**. The work's only product is the stream, so a
/// sink that reports the reader gone ends the read.
///
/// Returns the bytes sent.
///
/// # Errors
///
/// Returns whatever the host's open refuses with ([`FilesOpError::NotFound`],
/// [`FilesOpError::NotARegularFile`], [`FilesOpError::DirectoryUnusable`],
/// [`FilesOpError::HomeUnusable`], [`FilesOpError::Saturated`] or a privilege
/// failure), and from the pump [`FilesOpError::TooLarge`] and
/// [`FilesOpError::ReadFailed`].
pub fn read_file(
    host: &dyn BrowseHost,
    input: &ReadFileInput,
    sink: &mut dyn ChunkSink,
) -> Result<u64, FilesOpError> {
    let (file, _hold) = host
        .open_for_read_as_account(input.account(), input.path())?
        .into_parts();

    // `_hold` is the browse slot: it lives to the end of this function, which
    // is the end of the stream, and not merely until the file was open.
    stream_opened_file(file, input.max_bytes(), sink)
}

#[cfg(test)]
#[path = "../tests/files/read_file_tests.rs"]
mod tests;
