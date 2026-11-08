//! One open log file, as the traffic summary reads it.

use crate::sites::SitesOpError;

/// A site log that has been opened and proven a regular file root owns.
///
/// The seam between the summary's decisions (what to count, where to resume)
/// and the descriptor that serves the bytes, so the decisions are tested against
/// an in-memory file and the real one is the smallest piece of the area.
pub trait SiteLogFile {
    /// The device the file lives on.
    fn device(&self) -> u64;

    /// The file's inode number.
    fn inode(&self) -> u64;

    /// The file's length right now. It grows while the web server appends, so
    /// a reader samples it once and reads no further than the sample.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the length cannot be read.
    fn length(&self) -> Result<u64, SitesOpError>;

    /// Reads up to `buffer.len()` bytes starting at `offset`, returning how
    /// many were read; `0` means the file ends at or before `offset`.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the read fails.
    fn read_at(&self, offset: u64, buffer: &mut [u8]) -> Result<usize, SitesOpError>;
}
