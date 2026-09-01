//! Why a file operation inside a customer's home could not be done.

use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::path_error::PathError;

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
