//! An archive file the child holds open, and how long it is.

use std::fs::File;

/// The descriptor of the archive and its length, read from the descriptor.
///
/// Held as a pair because the length is a fact about THIS inode at the moment it
/// was opened, and the ratio cap is computed from it. Both passes over the
/// archive read through the same descriptor, so a rename of the path in between
/// changes nothing; the file's CONTENT can still change, which is why the second
/// pass judges everything again.
#[derive(Debug)]
pub(crate) struct OpenedArchive {
    /// The archive, opened read-only without following a link.
    pub(crate) file: File,
    /// Its size in bytes when it was opened.
    pub(crate) length: u64,
}
