//! The file a remote dump is written to.

use std::fs::File;
use std::path::PathBuf;

/// A root-only file on the host's disk that a dump is written into, and its path
/// so it can be removed.
#[derive(Debug)]
pub struct DumpFile {
    /// The open file, positioned at its start.
    pub file: File,
    /// Where it is, for [`crate::transfer::TransferWorkHost::remove_dump_file`].
    pub path: PathBuf,
}
