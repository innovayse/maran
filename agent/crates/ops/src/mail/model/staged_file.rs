//! One file of a configuration change, held until it is written.

use std::path::PathBuf;

/// A rendered file and where it goes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StagedFile {
    /// Absolute path the content is renamed onto.
    pub target: PathBuf,
    /// The rendered content.
    pub contents: String,
    /// The mode the file lands at.
    pub mode: u32,
}
