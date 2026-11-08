//! One trashed entry onto its wire payload.

use maran_ops::files::model::trashed_entry::TrashedEntry as TrashedItem;

use crate::proto::TrashedEntry;

/// Converts one entry of the trash into the contract's `TrashedEntry`.
///
/// The id is the entry's canonical spelling, which is the only handle
/// `RestoreTrashEntry` accepts back.
#[must_use]
pub fn to_wire_trash_entry(item: TrashedItem) -> TrashedEntry {
    TrashedEntry {
        trash_id: item.trash_id,
        original_path: item.original_path,
        trashed_at_unix: item.trashed_at_unix,
        is_directory: item.is_directory,
        size_bytes: item.size_bytes,
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/to_wire_trash_entry_tests.rs"]
mod tests;
