//! What the file system says about one path.

/// The owner and mode of a path, as the kernel reports them.
///
/// Only what a security invariant compares: nothing here is read to be displayed.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PathFacts {
    /// The permission and special bits (setuid, setgid, sticky), for example
    /// `0o2755`; the file type bits are not included.
    pub mode: u32,
    /// The owning user id.
    pub uid: u32,
    /// The owning group id.
    pub gid: u32,
}
