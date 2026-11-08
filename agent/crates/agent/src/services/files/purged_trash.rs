//! Turning the outcome of a trash purge into the wire's response.

use crate::proto::{
    AgentError, PurgeExpiredTrashOk, PurgeExpiredTrashResponse,
    purge_expired_trash_response::Result as Outcome,
};

/// Builds the `PurgeExpiredTrashResponse` for a finished purge: how many entries went away, or
/// the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn purged_trash(result: Result<u64, AgentError>) -> PurgeExpiredTrashResponse {
    let result = match result {
        Ok(entries_removed) => Outcome::Ok(PurgeExpiredTrashOk { entries_removed }),
        Err(error) => Outcome::Error(error),
    };

    PurgeExpiredTrashResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/purged_trash_tests.rs"]
mod tests;
