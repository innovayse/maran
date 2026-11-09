//! A file opened for reading, together with whatever must outlive the read.

use std::fs::File;

/// An open descriptor and the resources held on its behalf.
///
/// A browse slot (the per-account and per-host bound on simultaneous browse
/// operations) must stay taken until the LAST byte of a download has been sent,
/// not only until the file is open: a long download is the longest-lived browse
/// operation there is, and releasing at open would let any number of them run
/// past the cap. The host therefore hands its hold back with the file, and the
/// read drops it when it ends. The hold is opaque here because only the host
/// knows what it is.
pub struct OpenedForRead {
    /// The descriptor, already re-checked by the host.
    file: File,
    /// Dropped when this value is; never read.
    _hold: Box<dyn Send + Sync>,
}

impl OpenedForRead {
    /// Pairs `file` with `hold`, which is released when the read ends.
    #[must_use]
    pub fn new(file: File, hold: impl Send + Sync + 'static) -> Self {
        Self {
            file,
            _hold: Box::new(hold),
        }
    }

    /// Splits the pair: the file to read and the hold to keep until done.
    #[must_use]
    pub fn into_parts(self) -> (File, Box<dyn Send + Sync>) {
        (self.file, self._hold)
    }
}

impl std::fmt::Debug for OpenedForRead {
    /// Shows that a file is held, never its descriptor or the hold.
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.write_str("OpenedForRead")
    }
}
