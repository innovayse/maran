//! Making the identifier a trashed entry, or a copy in progress, is known by.

use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;

/// Builds the identifier `<unix seconds>-<16 lower-case hex digits>`.
///
/// Built in the PARENT, before the fork, from the clock and a nonce the host
/// derives from its process id, a nanosecond reading and a counter: the forked
/// child of a multi-threaded daemon should do as little as it can, and a clock
/// call and a format string are better done here. Uniqueness is not trusted to
/// the nonce alone. The directory the identifier names is created with
/// `mkdirat`, which refuses a name that exists, so a collision is a refusal and
/// never two operations sharing one entry.
///
/// The text is handed to [`TrashEntryId::parse`] rather than wrapped, so the
/// shape the listing, the restore and the purge accept is defined in one place
/// and an identifier made here can never disagree with it.
///
/// # Errors
///
/// Returns [`FilesOpError::InvalidTrashEntry`] when the text does not parse,
/// which would mean this function and the type disagree about the shape.
pub(crate) fn new_trash_entry_id(
    unix_seconds: u64,
    nonce: u64,
) -> Result<TrashEntryId, FilesOpError> {
    TrashEntryId::parse(&format!("{unix_seconds}-{nonce:016x}"))
        .map_err(|_| FilesOpError::InvalidTrashEntry)
}

#[cfg(test)]
#[path = "../tests/files/new_trash_entry_id_tests.rs"]
mod tests;
