//! What an archive member is allowed to be.

/// The two kinds of member an extraction will write.
///
/// A closed pair: a link, a device, a FIFO or a sparse file is not a third
/// kind but a refusal of the whole archive (`ArchiveRefusal`), found before the
/// first byte is written.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum MemberKind {
    /// A regular file.
    File,
    /// A directory.
    Directory,
}
