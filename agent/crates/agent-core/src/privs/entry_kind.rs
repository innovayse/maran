//! What kind of filesystem object a directory entry is.

/// The kind of an entry as `fstatat` reports it WITHOUT following a symlink.
///
/// Deliberately coarse: a sweep that decides whether to unlink, descend or leave
/// an entry alone needs these four answers and nothing finer. Anything that is
/// not a regular file, a directory or a symlink (a FIFO, a socket, a device
/// node) is [`EntryKind::Other`], which callers treat as "unlink by name, never
/// open" because opening a FIFO can block a root thread in the kernel forever.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EntryKind {
    /// A regular file.
    File,
    /// A directory.
    Directory,
    /// A symbolic link; the link itself, never its target.
    Symlink,
    /// A FIFO, socket, character device or block device.
    Other,
}
