//! One entry of a directory, as the entry itself is.

use super::entry_kind::EntryKind;

/// What `lstat` says about one entry. A symlink is described by the link, never
/// by its target, and its target text is not carried at all.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ListedEntry {
    /// The entry name, valid UTF-8 without control characters.
    pub name: String,
    /// What kind of object the entry is.
    pub kind: EntryKind,
    /// Size in bytes (`st_size`; a link's own length).
    pub size: u64,
    /// Permission bits (`st_mode & 0o7777`).
    pub mode: u32,
    /// Last modification time, Unix seconds.
    pub modified_unix: i64,
    /// Owning user id.
    pub uid: u32,
    /// Owning group id.
    pub gid: u32,
    /// Hard link count.
    pub link_count: u64,
}
