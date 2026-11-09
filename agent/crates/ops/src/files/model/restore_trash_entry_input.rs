//! Everything one restore from the trash is told, already validated.

use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to move one trash entry back into the home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RestoreTrashEntryInput {
    /// The account whose trash is read and whose home receives the entry.
    pub account: AccountName,
    /// The entry to restore, as the listing reported it.
    pub id: TrashEntryId,
    /// Where to put it. `None` means the path the entry had when it was
    /// trashed, which is read from a file the customer can edit and is
    /// validated again like any other path before it is used.
    pub destination: Option<MutablePath>,
}
