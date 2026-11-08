//! Turning a recorded original path back into a path that may be used.

use maran_agent_core::validation::fs::mutable_path::MutablePath;

use crate::files::FilesOpError;

/// Parses the text the trash recorded as an entry's original place.
///
/// The record is a file the customer can edit, so it is validated here like a
/// path from outside: traversal, an absolute path, the home itself, the trash's
/// own reserved name and control characters are all refused by [`MutablePath`].
/// A restore therefore never follows a forged record anywhere the request could
/// not have named.
///
/// # Errors
///
/// Returns [`FilesOpError::InvalidTrashEntry`] when the text is not UTF-8 or
/// does not parse as a mutable path.
pub(crate) fn parse_trashed_original(bytes: &[u8]) -> Result<MutablePath, FilesOpError> {
    let text = std::str::from_utf8(bytes).map_err(|_| FilesOpError::InvalidTrashEntry)?;

    MutablePath::parse(text).map_err(|_| FilesOpError::InvalidTrashEntry)
}

#[cfg(test)]
#[path = "../tests/files/parse_trashed_original_tests.rs"]
mod tests;
