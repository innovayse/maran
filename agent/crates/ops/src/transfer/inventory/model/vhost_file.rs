//! One web-server configuration file read from a source.

use maran_agent_core::validation::fs::remote_path::RemotePath;

/// A file the listing named, and its text.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VhostFile {
    /// Where the file is on the source.
    pub path: RemotePath,
    /// Its text, at most one command output long.
    pub content: String,
}
