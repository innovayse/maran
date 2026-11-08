//! A search outcome onto its wire payload.

use maran_ops::files::model::listed_entry::ListedEntry;
use maran_ops::files::model::search_match::SearchMatch;
use maran_ops::files::model::search_outcome::SearchOutcome;

use crate::proto::{SearchEntriesOk, SearchMatch as WireMatch};
use crate::services::files::to_wire_listed_entry::to_wire_listed_entry;

/// Converts one match: the path travels whole, and the entry is described like a
/// listing entry whose name is the path's last component (the contract promises
/// exactly that).
fn to_wire_match(found: SearchMatch) -> WireMatch {
    let name = found
        .path
        .rsplit('/')
        .next()
        .unwrap_or(found.path.as_str())
        .to_owned();

    WireMatch {
        entry: Some(to_wire_listed_entry(ListedEntry {
            name,
            kind: found.kind,
            size: found.size,
            mode: found.mode,
            modified_unix: found.modified_unix,
            uid: found.uid,
            gid: found.gid,
            link_count: found.links,
        })),
        path: found.path,
    }
}

/// Converts a finished search into the `SearchEntriesOk` payload.
///
/// `truncated` and `skipped_entries` are carried as measured: a search that a
/// bound stopped is still an OK answer, and a client that was not told it was cut
/// would read a partial result as the whole.
#[must_use]
pub fn to_wire_search_outcome(outcome: SearchOutcome) -> SearchEntriesOk {
    SearchEntriesOk {
        matches: outcome.matches.into_iter().map(to_wire_match).collect(),
        truncated: outcome.truncated,
        skipped_entries: outcome.skipped_entries,
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/to_wire_search_outcome_tests.rs"]
mod tests;
