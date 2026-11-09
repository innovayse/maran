//! One entry a search found.

use maran_agent_core::privs::entry_kind::EntryKind;

/// An entry whose name contains the query.
///
/// The fields are the ones `DirectoryEntry` in `files.proto` carries. They
/// describe the ENTRY: for a symlink, the link, never its target.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SearchMatch {
    /// Path from the account's home, `/`-separated, ending in the entry's name.
    pub path: String,
    /// What the entry is.
    pub kind: EntryKind,
    /// Size in bytes (`st_size`); a link's own length.
    pub size: u64,
    /// Permission bits (`st_mode & 0o7777`).
    pub mode: u32,
    /// Last modification, Unix seconds.
    pub modified_unix: i64,
    /// Owning user id.
    pub uid: u32,
    /// Owning group id.
    pub gid: u32,
    /// Hard link count.
    pub links: u64,
}
