//! The one place a service hands a blocking operation to the runtime.

use crate::proto::AgentError;
use crate::services::wire::system_failure::system_failure;

/// Runs one blocking operation off the runtime's workers and maps its failure
/// onto the wire error.
///
/// Every operation in `ops` spawns processes and waits on them; rules/rust.md
/// requires that off the async workers, since a process wait on a worker
/// stalls every other in-flight command. This wrapper is written once so a
/// service cannot forget the `spawn_blocking` (the accounts service once did)
/// or map a panic differently from its neighbours.
///
/// `what` is the noun phrase of the failure message — "cron operation",
/// "certificate operation", "monitoring reading" — kept caller-chosen so the
/// messages operators already know did not change when the wrapper unified.
///
/// # Errors
///
/// Returns `map_error`'s mapping of whatever the operation failed on, or a
/// system failure when the blocking task did not finish — a panic inside the
/// agent has no domain answer to give, and rules/proto.md reserves gRPC
/// statuses for transport problems, which it is not.
pub async fn run_blocking<T, E>(
    what: &'static str,
    map_error: impl FnOnce(&E) -> AgentError,
    operation: impl FnOnce() -> Result<T, E> + Send + 'static,
) -> Result<T, AgentError>
where
    T: Send + 'static,
    E: Send + 'static,
{
    match tokio::task::spawn_blocking(operation).await {
        Ok(outcome) => outcome.map_err(|error| map_error(&error)),
        Err(error) => Err(system_failure(format!(
            "the {what} did not finish: {error}"
        ))),
    }
}
