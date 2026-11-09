//! The verdict the child of an archive operation reaches, in the form that crosses a pipe.

use crate::files::FilesOpError;
use crate::files::model::limit_kind::LimitKind;

/// Why an archive operation stopped, as the dropped child knows it.
///
/// The child's outcome crosses to the root parent as bytes it writes itself
/// (`ArchiveAnswer`), so the reason is a closed set with a one-byte code and
/// the parent reads nothing it does not recognise. Several variants name
/// different ways an archive can be unsafe: they exist so the unit tests can
/// pin each refusal separately, and all of them reach the wire as the one
/// `ARCHIVE_UNSAFE` code (the panel words it for the customer, the agent
/// supplies the category and nothing else).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub(crate) enum ArchiveRefusal {
    /// The account's home is missing, is not a directory, or is not the account's.
    HomeUnusable = 1,
    /// A directory on the way is missing, is a link, is not a directory or is not the account's.
    DirectoryUnusable = 2,
    /// The archive file does not exist.
    NotFound = 3,
    /// The archive path names something that is not a regular file.
    NotARegularFile = 4,
    /// A member's name is one no filesystem write may follow.
    UnsafeName = 5,
    /// A member is a symbolic or hard link.
    LinkMember = 6,
    /// A member is a device, FIFO, socket or other special file.
    SpecialMember = 7,
    /// A member is a sparse file.
    SparseMember = 8,
    /// A zip member is encrypted.
    EncryptedMember = 9,
    /// A zip member uses a compression method other than stored or deflate.
    UnsupportedCompression = 10,
    /// Two members share a name.
    DuplicateMember = 11,
    /// A member is written below a name another member made a file.
    FileAsDirectory = 12,
    /// A member holds more bytes than its header declared.
    SizeMismatch = 13,
    /// The archive expands beyond the ratio the request allowed.
    BombRatio = 14,
    /// Non-zero bytes follow the end of a tar archive.
    TrailingData = 15,
    /// The archive is not the format stated, or is damaged.
    Unreadable = 16,
    /// A name the extraction must create is already taken.
    AlreadyExists = 17,
    /// The archive or the sources hold more entries than the request allowed.
    EntryLimit = 18,
    /// The archive or the sources hold more bytes than the request allowed.
    ByteLimit = 19,
    /// The account's disk quota ended the write.
    Quota = 20,
    /// A write, a permission change or a rename failed.
    WriteFailed = 21,
    /// A source file could not be read.
    ReadFailed = 22,
    /// A source is a link or a special file, or has a name an archive cannot hold.
    UnsupportedEntry = 23,
    /// A source tree is deeper than the agent will walk.
    TooDeep = 24,
}

impl ArchiveRefusal {
    /// Every refusal, so a code can be read back without a numeric cast.
    const ALL: [Self; 24] = [
        Self::HomeUnusable,
        Self::DirectoryUnusable,
        Self::NotFound,
        Self::NotARegularFile,
        Self::UnsafeName,
        Self::LinkMember,
        Self::SpecialMember,
        Self::SparseMember,
        Self::EncryptedMember,
        Self::UnsupportedCompression,
        Self::DuplicateMember,
        Self::FileAsDirectory,
        Self::SizeMismatch,
        Self::BombRatio,
        Self::TrailingData,
        Self::Unreadable,
        Self::AlreadyExists,
        Self::EntryLimit,
        Self::ByteLimit,
        Self::Quota,
        Self::WriteFailed,
        Self::ReadFailed,
        Self::UnsupportedEntry,
        Self::TooDeep,
    ];

    /// The one byte this refusal is written as.
    pub(crate) const fn code(self) -> u8 {
        self as u8
    }

    /// The refusal a byte stands for, or `None` for a byte no refusal has.
    pub(crate) fn from_code(code: u8) -> Option<Self> {
        Self::ALL.into_iter().find(|refusal| refusal.code() == code)
    }

    /// The refusal for an error the shared walk of this area returned.
    ///
    /// Only the two variants the walk produces are distinguished; anything else
    /// is read as a directory problem, the safe direction.
    pub(crate) fn from_walk_error(error: &FilesOpError) -> Self {
        match error {
            FilesOpError::HomeUnusable => Self::HomeUnusable,
            _ => Self::DirectoryUnusable,
        }
    }

    /// The error the parent reports for this refusal.
    pub(crate) fn into_op_error(self) -> FilesOpError {
        match self {
            Self::HomeUnusable => FilesOpError::HomeUnusable,
            Self::DirectoryUnusable => FilesOpError::DirectoryUnusable,
            Self::NotFound => FilesOpError::NotFound,
            Self::NotARegularFile => FilesOpError::NotARegularFile,
            Self::UnsafeName
            | Self::LinkMember
            | Self::SpecialMember
            | Self::SparseMember
            | Self::EncryptedMember
            | Self::UnsupportedCompression
            | Self::DuplicateMember
            | Self::FileAsDirectory
            | Self::SizeMismatch
            | Self::BombRatio
            | Self::TrailingData => FilesOpError::ArchiveUnsafe,
            Self::Unreadable => FilesOpError::ArchiveUnreadable,
            Self::AlreadyExists => FilesOpError::AlreadyExists,
            Self::EntryLimit => FilesOpError::LimitReached(LimitKind::Entries),
            Self::ByteLimit => FilesOpError::LimitReached(LimitKind::Bytes),
            Self::Quota => FilesOpError::QuotaExceeded,
            Self::WriteFailed => FilesOpError::WriteFailed,
            Self::ReadFailed => FilesOpError::ReadFailed,
            Self::UnsupportedEntry => FilesOpError::UnsupportedEntry,
            Self::TooDeep => FilesOpError::TooDeep,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/archive_refusal_tests.rs"]
mod tests;
