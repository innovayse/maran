//! The seam between the panel's challenge operations and the machine.

use std::path::Path;

use crate::ssl::ssl_op_error::SslOpError;

/// The operating-system operations the panel's ACME challenge needs.
///
/// A seam of its own rather than more methods on [`super::SslHost`]: a
/// challenge file is neither certificate material nor a vhost, it touches no
/// nginx and no openssl, and the free-space question it asks is one no other
/// certificate operation asks. The one implementation that touches the machine
/// is [`super::ProcessPanelChallengeHost`]; the operations decide against a fake.
pub trait PanelChallengeHost: Send + Sync {
    /// Bytes an unprivileged writer could still add to the filesystem
    /// `directory` lives on — `f_bavail`, asked of the directory itself.
    ///
    /// # Errors
    ///
    /// Returns the underlying error when the filesystem cannot be measured;
    /// the caller refuses the write rather than assuming room.
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error>;

    /// Writes `contents` at `path`, replacing whatever name is there and
    /// following no link at it.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::ChallengeWrite`] when the file cannot be written.
    fn write_challenge(&self, path: &Path, contents: &str) -> Result<(), SslOpError>;

    /// Removes the file at `path`.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::ChallengeNotFound`] when nothing is there, and
    /// [`SslOpError::ChallengeWrite`] when it is there and cannot be removed.
    fn remove_challenge(&self, path: &Path) -> Result<(), SslOpError>;
}
