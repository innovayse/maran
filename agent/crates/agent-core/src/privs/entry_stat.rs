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
    /// Size in bytes (`st_size`); for a symlink the length of the link's own text,
    /// never of its target. A negative reading, which no supported filesystem
    /// produces, is reported as zero.
    pub size: u64,
    /// The full `st_mode`: the file-type bits as well as the permission bits and
    /// the set-id and sticky bits. A caller that wants the permissions masks it.
    pub mode: u32,
    /// Last modification time (`st_mtime`) in whole seconds since the Unix epoch;
    /// negative for a time before it.
    pub modified_unix: i64,
}
