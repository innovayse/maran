//! The parent's half of an upload: pulling chunks from the source and sending them on.

use std::io;

use crate::files::feed_stop::FeedStop;
use crate::files::model::upload_source::UploadSource;

/// Sends every chunk of `source` through `send`, counting against `limit`.
///
/// Returns `Ok(())` only when the source reported itself complete and every chunk was sent; the
/// caller then sends the terminator, and not before. The count is the PARENT's own, taken
/// before a chunk is sent, so a source that lies about its length gains nothing and the child
/// is never fed past the cap; the child counts again, because it is the party that might be
/// confused.
///
/// # Errors
///
/// [`FeedStop::OverLimit`] on the chunk that would cross `limit` (nothing of it is sent),
/// [`FeedStop::Aborted`] when the source did not finish, and [`FeedStop::Channel`] when
/// `send` failed.
pub(crate) fn feed_upload(
    source: &mut dyn UploadSource,
    limit: u64,
    send: &mut dyn FnMut(&[u8]) -> io::Result<()>,
) -> Result<(), FeedStop> {
    let mut sent: u64 = 0;
    loop {
        match source.next_chunk() {
            Ok(Some(chunk)) => {
                sent = sent.saturating_add(u64::try_from(chunk.len()).unwrap_or(u64::MAX));
                if sent > limit {
                    return Err(FeedStop::OverLimit);
                }
                send(chunk).map_err(FeedStop::Channel)?;
            }
            Ok(None) => return Ok(()),
            Err(_aborted) => return Err(FeedStop::Aborted),
        }
    }
}

#[cfg(test)]
#[path = "../tests/files/feed_upload_tests.rs"]
mod tests;
