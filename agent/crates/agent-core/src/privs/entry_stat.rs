//! The facts about one directory entry that a sweep decides on.

use super::entry_kind::EntryKind;

/// What `fstatat(…, AT_SYMLINK_NOFOLLOW)` reports about an entry.
///
/// Every field describes the ENTRY itself: for a symlink that is the link, not
/// whatever it points at. That is what makes `uid` safe to decide on — a link
/// planted at a name cannot make a foreign file look like the account's.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct EntryStat {
    /// Owning user id (`st_uid`).
    pub uid: u32,
    /// Owning group id (`st_gid`).
    pub gid: u32,
    /// Device the entry lives on (`st_dev`); a child whose device differs from
    /// its parent's is a mount point and a walk does not cross it.
    pub device: u64,
    /// The kind of object the entry is.
    pub kind: EntryKind,
    /// Hard link count (`st_nlink`).
    pub links: u64,
}
