//! Reading a reporting fork's failure back as the file manager's error.

use maran_agent_core::privs::priv_error::PrivError;

use crate::files::FilesOpError;
use crate::files::files_refusal::FilesRefusal;

/// Turns what a reporting fork failed with into a [`FilesOpError`].
///
/// The one place a refusal number from the child becomes an error, shared by every
/// job that forks (`fork_files_work`, `fork_upload_work`) so they cannot read the
/// closed list differently. A [`PrivError::WorkRefused`] whose number is on the
/// list becomes the error it names; a number outside it is a plain
/// [`PrivError::WorkFailed`], so a confused child cannot choose what the panel is
/// told beyond that list. Every other fork failure is carried as
/// [`FilesOpError::Privilege`].
pub(crate) fn files_error_of_privilege(error: PrivError) -> FilesOpError {
    match error {
        PrivError::WorkRefused { reason } => FilesRefusal::from_code(reason).map_or(
            FilesOpError::Privilege(PrivError::WorkFailed),
            FilesRefusal::into_error,
        ),
        other => FilesOpError::Privilege(other),
    }
}
