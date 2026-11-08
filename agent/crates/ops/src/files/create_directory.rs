//! CreateDirectory: one directory, and its missing parents, in a customer's home.

use crate::files::model::create_directory_input::CreateDirectoryInput;
use crate::files::{FilesHost, FilesOpError};

/// Creates one directory inside an account's home, as the account.
///
/// The final directory gets the requested mode and the parents the call creates
/// get the agent's directory mode; parents that exist are left exactly as they
/// are. Creating a name that is taken, by a directory, a file or a link, is
/// [`FilesOpError::AlreadyExists`] and changes nothing: the file manager treats
/// that as "it is already there" and not as a fault, which is the idempotence
/// this operation can offer.
///
/// # Errors
///
/// Returns [`FilesOpError::AlreadyExists`] when the name is taken;
/// [`FilesOpError::Privilege`] when the account cannot be resolved or the
/// privilege drop fails; [`FilesOpError::HomeUnusable`] and
/// [`FilesOpError::DirectoryUnusable`] when the walk is refused;
/// [`FilesOpError::QuotaExceeded`] when the account's quota ends it; and
/// [`FilesOpError::MutationFailed`] when the directory cannot be made.
pub fn create_directory(
    host: &dyn FilesHost,
    input: &CreateDirectoryInput,
) -> Result<(), FilesOpError> {
    host.create_directory_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/create_directory_tests.rs"]
mod tests;
