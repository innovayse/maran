//! The reasons a forked child gives for refusing a file-manager mutation.

use crate::files::FilesOpError;
use crate::files::model::limit_kind::LimitKind;

/// A refusal as it crosses from the child to the parent: one byte.
///
/// The child cannot hand a Rust value back, and a channel carrying text or a
/// path from an unprivileged child into the root parent would be a surface, so
/// the only thing that crosses is a number from this closed list. The parent
/// turns it back into the [`FilesOpError`] it stands for in one place
/// ([`FilesRefusal::into_error`]), and a number outside the list is not trusted:
/// it becomes a plain failure and never a guess.
///
/// The numbers are part of the child-to-parent contract and are never reused;
/// `0` is reserved by the fork for success. They stay below 64, the range the
/// fork leaves to its callers.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub(crate) enum FilesRefusal {
    /// [`FilesOpError::HomeUnusable`].
    HomeUnusable = 1,
    /// [`FilesOpError::DirectoryUnusable`].
    DirectoryUnusable = 2,
    /// [`FilesOpError::NotFound`].
    NotFound = 3,
    /// [`FilesOpError::AlreadyExists`].
    AlreadyExists = 4,
    /// [`FilesOpError::CrossesDevice`].
    CrossesDevice = 5,
    /// [`FilesOpError::TooDeep`].
    TooDeep = 6,
    /// [`FilesOpError::NotADirectory`].
    NotADirectory = 7,
    /// [`FilesOpError::UnsupportedEntry`].
    UnsupportedEntry = 8,
    /// [`FilesOpError::NotOwned`].
    NotOwned = 9,
    /// [`FilesOpError::IntoItself`].
    IntoItself = 10,
    /// [`FilesOpError::LimitReached`] with [`LimitKind::Entries`].
    EntryLimit = 11,
    /// [`FilesOpError::LimitReached`] with [`LimitKind::Bytes`].
    ByteLimit = 12,
    /// [`FilesOpError::QuotaExceeded`].
    QuotaExceeded = 13,
    /// [`FilesOpError::InvalidTrashEntry`].
    InvalidTrashEntry = 14,
    /// [`FilesOpError::WriteFailed`].
    WriteFailed = 15,
    /// [`FilesOpError::RemoveFailed`].
    RemoveFailed = 16,
    /// [`FilesOpError::MutationFailed`].
    MutationFailed = 17,
    /// [`FilesOpError::NotARegularFile`].
    NotARegularFile = 18,
    /// [`FilesOpError::TooLarge`].
    TooLarge = 19,
}

impl FilesRefusal {
    /// The refusal a child reports for `error`.
    ///
    /// A variant a child cannot raise (the privilege, containment-check,
    /// saturation and free-space ones belong to the parent) and any variant
    /// added later map to [`FilesRefusal::MutationFailed`]: an unclassified
    /// failure must read as a failure, never as success or as some other verdict.
    #[must_use]
    pub(crate) fn of(error: &FilesOpError) -> Self {
        match error {
            FilesOpError::HomeUnusable => Self::HomeUnusable,
            FilesOpError::DirectoryUnusable => Self::DirectoryUnusable,
            FilesOpError::NotFound => Self::NotFound,
            FilesOpError::AlreadyExists => Self::AlreadyExists,
            FilesOpError::CrossesDevice => Self::CrossesDevice,
            FilesOpError::TooDeep => Self::TooDeep,
            FilesOpError::NotADirectory => Self::NotADirectory,
            FilesOpError::UnsupportedEntry => Self::UnsupportedEntry,
            FilesOpError::NotOwned => Self::NotOwned,
            FilesOpError::IntoItself => Self::IntoItself,
            FilesOpError::LimitReached(LimitKind::Entries) => Self::EntryLimit,
            FilesOpError::LimitReached(LimitKind::Bytes) => Self::ByteLimit,
            FilesOpError::QuotaExceeded => Self::QuotaExceeded,
            FilesOpError::InvalidTrashEntry => Self::InvalidTrashEntry,
            FilesOpError::WriteFailed => Self::WriteFailed,
            FilesOpError::RemoveFailed => Self::RemoveFailed,
            FilesOpError::NotARegularFile => Self::NotARegularFile,
            FilesOpError::TooLarge => Self::TooLarge,
            _ => Self::MutationFailed,
        }
    }

    /// The byte that crosses.
    #[must_use]
    pub(crate) fn code(self) -> u8 {
        self as u8
    }

    /// The refusal a received byte stands for, or `None` for a byte outside the list.
    #[must_use]
    pub(crate) fn from_code(code: u8) -> Option<Self> {
        Some(match code {
            1 => Self::HomeUnusable,
            2 => Self::DirectoryUnusable,
            3 => Self::NotFound,
            4 => Self::AlreadyExists,
            5 => Self::CrossesDevice,
            6 => Self::TooDeep,
            7 => Self::NotADirectory,
            8 => Self::UnsupportedEntry,
            9 => Self::NotOwned,
            10 => Self::IntoItself,
            11 => Self::EntryLimit,
            12 => Self::ByteLimit,
            13 => Self::QuotaExceeded,
            14 => Self::InvalidTrashEntry,
            15 => Self::WriteFailed,
            16 => Self::RemoveFailed,
            17 => Self::MutationFailed,
            18 => Self::NotARegularFile,
            19 => Self::TooLarge,
            _ => return None,
        })
    }

    /// The error this refusal stands for in the parent.
    #[must_use]
    pub(crate) fn into_error(self) -> FilesOpError {
        match self {
            Self::HomeUnusable => FilesOpError::HomeUnusable,
            Self::DirectoryUnusable => FilesOpError::DirectoryUnusable,
            Self::NotFound => FilesOpError::NotFound,
            Self::AlreadyExists => FilesOpError::AlreadyExists,
            Self::CrossesDevice => FilesOpError::CrossesDevice,
            Self::TooDeep => FilesOpError::TooDeep,
            Self::NotADirectory => FilesOpError::NotADirectory,
            Self::UnsupportedEntry => FilesOpError::UnsupportedEntry,
            Self::NotOwned => FilesOpError::NotOwned,
            Self::IntoItself => FilesOpError::IntoItself,
            Self::EntryLimit => FilesOpError::LimitReached(LimitKind::Entries),
            Self::ByteLimit => FilesOpError::LimitReached(LimitKind::Bytes),
            Self::QuotaExceeded => FilesOpError::QuotaExceeded,
            Self::InvalidTrashEntry => FilesOpError::InvalidTrashEntry,
            Self::WriteFailed => FilesOpError::WriteFailed,
            Self::RemoveFailed => FilesOpError::RemoveFailed,
            Self::MutationFailed => FilesOpError::MutationFailed,
            Self::NotARegularFile => FilesOpError::NotARegularFile,
            Self::TooLarge => FilesOpError::TooLarge,
        }
    }
}

#[cfg(test)]
#[path = "../tests/files/files_refusal_tests.rs"]
mod tests;
