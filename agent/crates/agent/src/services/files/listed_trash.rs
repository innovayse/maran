//! Turning the contents of the trash into the wire's response.

use maran_ops::files::model::trashed_entry::TrashedEntry as TrashedItem;

use crate::proto::{
    AgentError, ListTrashOk, ListTrashResponse, list_trash_response::Result as Outcome,
};
use crate::services::files::to_wire_trash_entry::to_wire_trash_entry;

/// Builds the `ListTrashResponse` for a finished listing, one wire entry per item in the order
/// the operation returned them, or the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn listed_trash(result: Result<Vec<TrashedItem>, AgentError>) -> ListTrashResponse {
    let result = match result {
        Ok(items) => Outcome::Ok(ListTrashOk {
            entries: items.into_iter().map(to_wire_trash_entry).collect(),
        }),
        Err(error) => Outcome::Error(error),
    };

    ListTrashResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/listed_trash_tests.rs"]
mod tests;
