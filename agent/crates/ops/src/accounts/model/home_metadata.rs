//! What the filesystem itself says about a path a home repair examines.

/// The facts about one path that `RepairHomeGroups`'s predicate needs, read
/// with `lstat` and never `stat`.
///
/// `lstat`, so a symlink sitting where a home should be is reported as the
/// link itself rather than followed to whatever it points at — the same
/// reason [`super::super::AccountOperations`]'s own `chgrp` call passes
/// `--no-dereference`. A caller that stat'd through the link could be made to
/// re-group an arbitrary target a customer's account can no longer even
/// write, since the account has no shell to have planted it after creation,
/// but a compromised web application under an OLDER, wide-mode home could.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct HomeMetadata {
    /// Whether the path itself is a symlink.
    pub is_symlink: bool,

    /// Whether the path is a directory. `false` for a symlink even when its
    /// target is a directory, because `is_symlink` already takes that path
    /// out of consideration before this field is read.
    pub is_directory: bool,

    /// The path's owning uid.
    pub owner_uid: u32,

    /// The path's owning gid.
    pub group_gid: u32,

    /// The id of the device the path's inode lives on.
    ///
    /// Compared against the account home root's own device to decide whether
    /// the path is a separate mount — the repair's `DifferentMount` refusal.
    pub device: u64,
}
