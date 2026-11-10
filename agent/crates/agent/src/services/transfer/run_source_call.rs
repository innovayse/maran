//! One blocking operation against a registered source, off the async workers.

use maran_ops::transfer::TransferOpError;

use crate::proto::AgentError;
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::wire::run_blocking::run_blocking;

/// Runs one blocking operation of the transfer area and keeps the reason of its
/// failure.
///
/// Goes through [`run_blocking`], the one `spawn_blocking` wrapper, so a process
/// wait never stands on an async worker and a panicked task is the same system
/// failure everywhere. The wrapper maps an error onto an `AgentError` and so would
/// drop the connection reason the source rpcs answer with; the operation's own
/// result is therefore carried INSIDE the wrapper's success arm and mapped here,
/// where both halves of the answer are built together.
///
/// # Errors
///
/// The [`SourceFailure`] of the operation's error, or a plain system failure when
/// the blocking task did not finish.
pub async fn run_source_call<T>(
    operation: impl FnOnce() -> Result<T, TransferOpError> + Send + 'static,
) -> Result<T, SourceFailure>
where
    T: Send + 'static,
{
    let joined = run_blocking(
        "source operation",
        |error: &AgentError| error.clone(),
        move || Ok::<_, AgentError>(operation()),
    )
    .await;

    match joined {
        Ok(Ok(value)) => Ok(value),
        Ok(Err(error)) => Err(SourceFailure::of(&error)),
        Err(error) => Err(SourceFailure::plain(error)),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/run_source_call_tests.rs"]
mod tests;
