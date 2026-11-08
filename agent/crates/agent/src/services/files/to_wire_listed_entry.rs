//! A listed entry onto its wire payload.

use maran_ops::files::model::entry_kind::EntryKind;
use maran_ops::files::model::listed_entry::ListedEntry;

use crate::proto::DirectoryEntry;

/// Converts one entry of a listing, or one found by a search, into the contract's
/// `DirectoryEntry`.
///
/// `is_directory` is true only for a REAL directory: a symlink to a directory is
/// `is_symlink` with `is_directory` false, because the contract promises that a
/// client which descends into every `is_directory` entry never follows a link.
/// A FIFO, device or socket is neither; the wire has no field for "other", and
/// both flags false is how a client tells it from a file only by `mode`, which
/// is all it needs since such an entry is shown and never opened.
///
/// A unit of its own because the listing and the search describe an entry the
/// same way, and two copies of this mapping are two places for the contract's
/// promise about links to drift apart.
#[must_use]
pub fn to_wire_listed_entry(entry: ListedEntry) -> DirectoryEntry {
    DirectoryEntry {
        name: entry.name,
        is_directory: entry.kind == EntryKind::Directory,
        size_bytes: entry.size,
        mode: entry.mode,
        modified_at_unix: entry.modified_unix,
        is_symlink: entry.kind == EntryKind::Symlink,
        uid: entry.uid,
        gid: entry.gid,
        link_count: entry.link_count,
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/to_wire_listed_entry_tests.rs"]
mod tests;
