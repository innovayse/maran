//! Why a file operation inside a customer's home could not be done.

use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::slots_full::SlotsFull;
use maran_agent_core::validation::fs::path_error::PathError;

use crate::files::model::limit_kind::LimitKind;

/// Failures of the customer-file operations.
///
/// **No variant carries a path**, and that is a property of the enum rather
/// than of anyone's care at the call sites. Everything this area touches lives
/// inside a hosting customer's home, so every path it could name is either the
/// caller's own input — which the caller already has — or a fragment of a
/// customer's directory tree, which is exactly what rules/security.md item 8
/// keeps out of messages and logs. The panel maps the variant to a customer
/// sentence of its own; the agent supplies the category and nothing else.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum FilesOpError {
    /// The account could not be resolved, the privilege drop failed, or the
    /// forked child did not finish. The work either did not run or did not
    /// complete; see [`PrivError`] for which.
    #[error("privileged work as the account failed: {0}")]
    Privilege(#[from] PrivError),
    /// The account's home directory is missing, is not a directory, or is not
    /// owned by the account. Nothing was written.
    ///
    /// Its own variant rather than part of [`FilesOpError::DirectoryUnusable`]
    /// because the two mean opposite things about who is at fault: a home that
    /// is not the account's is a broken or tampered-with host, while a bad
    /// level below it is something the account did inside its own tree.
    #[error("the account's home is not a directory the account owns")]
    HomeUnusable,
    /// A directory on the way to the file could not be created, could not be
    /// opened, or turned out not to be a directory the account owns — a symlink
    /// refused by `O_NOFOLLOW`, or a level replaced between two steps of the
    /// walk.
    #[error("a directory on the way to the file is not usable")]
    DirectoryUnusable,
    /// The resolved path left the account's home. The containment check
    /// (rules/security.md item 2) refused it and nothing was written or
    /// removed.
    #[error("the path escapes the account's home")]
    EscapesHome,
    /// The entry does not exist. The idempotent answer for a removal of
    /// something already gone, as `files.proto` specifies.
    #[error("no such entry")]
    NotFound,
    /// The entry exists but is not a plain file the account owns with a single
    /// link — a FIFO, a device, a directory, a hardlink to somebody else's
    /// file. Refused rather than acted on.
    #[error("the entry is not a regular file the account owns")]
    NotARegularFile,
    /// The content did not reach the disk: the create, the write, the `fsync`
    /// or the rename failed.
    #[error("the file could not be written")]
    WriteFailed,
    /// The entry could not be unlinked even though it was there and was the
    /// right kind of thing.
    #[error("the entry could not be removed")]
    RemoveFailed,
    /// A directory held more entries than one listing will read (100 000), or an
    /// answer from the dropped child outgrew its ceiling. Nothing is returned:
    /// a partial listing would pass for the whole directory.
    #[error("the directory or its answer is too large")]
    TooLarge,
    /// Too many browse or bulk file operations were already running, for this
    /// account or for the host. Refused immediately, never queued; the caller may retry.
    #[error("too many browse operations are running")]
    Saturated,
    /// A read of a file that passed every check failed part-way: the operating
    /// system refused a `read`. Nothing is carried; the stream ends in this error
    /// and the panel discards the partial body.
    #[error("the file could not be read")]
    ReadFailed,
    /// The destination of a move, copy, restore or directory creation is already
    /// held by an entry (any kind, a link included). Nothing was replaced.
    #[error("the destination already exists")]
    AlreadyExists,
    /// A rename or a walk would cross onto another filesystem. There is no
    /// copy-and-delete fallback, so nothing was moved.
    #[error("the entry lies on another filesystem")]
    CrossesDevice,
    /// A tree is deeper than the agent will walk (128 levels). Refused rather
    /// than half done.
    #[error("the tree is deeper than the agent will walk")]
    TooDeep,
    /// A directory was needed and something else was found.
    #[error("a directory was expected")]
    NotADirectory,
    /// The entry is a symlink or a special file where the operation accepts only
    /// regular files and directories (or, for a move, anything but a link).
    #[error("the entry is a link or a special file")]
    UnsupportedEntry,
    /// The entry, or a directory the operation had to enter, is not owned by the
    /// account. Ownership is the claim that survives an account handing write
    /// access around inside its own home.
    #[error("the entry is not owned by the account")]
    NotOwned,
    /// A destination lies inside the entry being moved or copied.
    #[error("the destination lies inside the source")]
    IntoItself,
    /// The request stated an allowance and the operation reached it. Nothing
    /// partial is left behind by a copy; a removal may be re-run to finish.
    #[error("a stated limit was reached: {0:?}")]
    LimitReached(LimitKind),
    /// The account's disk quota ended the operation (`EDQUOT`). `ENOSPC` is not
    /// this: a full filesystem is the host's problem, not the account's.
    #[error("the account's disk quota is exhausted")]
    QuotaExceeded,
    /// A trash entry is not in the shape the agent writes: no payload, or an
    /// original-path record that does not validate.
    #[error("the trash entry is not in the shape the agent writes")]
    InvalidTrashEntry,
    /// A rename, directory creation or permission change was refused by the
    /// operating system for a reason no other variant names.
    #[error("the change could not be made")]
    MutationFailed,
    /// An archive holds something the extraction will not write: a link, a device
    /// or other special member, a name that leaves the destination, a collision
    /// between members, an encrypted member, or an expansion past the ratio the
    /// request allowed. The archive was refused as a whole (issue #168).
    #[error("the archive holds a member the agent will not write")]
    ArchiveUnsafe,
    /// A file named as an archive is not the format stated, or is damaged.
    /// Distinct from [`FilesOpError::ArchiveUnsafe`]: nothing hostile was found,
    /// the bytes are simply not what was said.
    #[error("the file is not a readable archive of the stated format")]
    ArchiveUnreadable,
    /// The write was refused at the free-space floor before anything was
    /// created (issue #148).
    #[error(transparent)]
    FreeSpace(#[from] FloorRefusal),
}

impl From<SlotsFull> for FilesOpError {
    /// A bounded count with no slot left is [`FilesOpError::Saturated`].
    fn from(_: SlotsFull) -> Self {
        Self::Saturated
    }
}

impl From<PathError> for FilesOpError {
    /// Maps the containment primitive's answer onto this area's vocabulary.
    ///
    /// [`PathError::NotFound`] becomes [`FilesOpError::NotFound`] rather than a
    /// containment failure: a challenge file that is not there is the ordinary
    /// outcome of removing one twice, and reporting it as an escape would tell
    /// an operator an attack happened every time a renewal was retried.
    fn from(error: PathError) -> Self {
        match error {
            PathError::NotFound => Self::NotFound,
            // `PathError` is `#[non_exhaustive]`, so a reason added there lands
            // here rather than failing this build — as a refusal, which is the
            // safe direction: an unclassified containment answer must never be
            // read as "contained".
            _ => Self::EscapesHome,
        }
    }
}
