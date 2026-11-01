//! Taking one name of a login's key file away.

use std::path::Path;

use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// Removes `path`, one of a login's two key file names, when it is there.
///
/// Shared by the key write (which removes the name it did not write) and by
/// the login's deletion (which removes both). The host's removal answers
/// [`SftpError::JailFailed`] for a directory it could not empty; a key file is
/// not the jail, so the answer here is named for what failed.
///
/// # Errors
///
/// [`SftpError::KeyFileFailed`] when the file exists and could not be removed.
/// A file that is not there is success.
pub(crate) fn remove_key_file(host: &dyn SftpHost, path: &Path) -> Result<(), SftpError> {
    host.remove_file(path).map_err(|_| SftpError::KeyFileFailed)
}
