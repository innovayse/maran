//! What a directory listing is ordered by.

/// The key a listing is sorted on. Directories always come before files,
/// whichever key is chosen.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DirectorySort {
    /// By entry name, ignoring case.
    Name,
    /// By size in bytes.
    Size,
    /// By modification time.
    Modified,
}
