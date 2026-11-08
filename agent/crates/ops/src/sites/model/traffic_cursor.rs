//! Where the next traffic summary continues.

/// A position in one of a site's access logs.
///
/// The `file_id` is opaque to everything but the summary that made it: it names
/// the file by device, inode and a fingerprint of its first bytes, so a cursor
/// into a file that has since been rotated away is recognised rather than
/// followed into whatever now has that name. The `offset` is the count of bytes
/// already summarised and is always at a line boundary.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrafficCursor {
    /// Identity of the file the offset belongs to.
    pub file_id: String,
    /// Bytes of that file already counted.
    pub offset: u64,
}
