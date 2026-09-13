//! What, if anything, a finished log tail owes its client.

use maran_ops::sites::{SitesOpError, TailEnd};

use crate::proto::{AgentError, ErrorCode};
use crate::services::sites::site_status::to_agent_error;

/// The terminal message for a tail that has ended, or `None` when there is
/// nothing to say.
///
/// A tail can end three ways and only one of them is the client's own decision.
/// Returning `None` for all of them — which is what a bare `Ok(())` used to
/// mean — leaves a client that was DROPPED for not reading, a client that
/// closed its tab, and a log that went quiet for five minutes all looking
/// identical on the wire: a stream that simply stopped. An operator watching a
/// log then cannot tell "the agent gave up on you" from "there was nothing
/// more to show", and a silent truncation of exactly the thing they are
/// watching is the failure this rpc exists to prevent.
///
/// A separate unit from the handler because it is a decision with three cases
/// and a handler is a translation layer (rules/rust.md "Service anatomy") — and
/// because inline in the handler it had no test that could fail when it was
/// deleted.
///
/// **Each ending carries its own `ErrorCode`.** An earlier revision put both
/// under `ERROR_CODE_SYSTEM_FAILURE` and let the English message be the only
/// difference, on the argument that the enum is the whole contract's shared
/// vocabulary and should not grow a log-tail-shaped entry. That argument was
/// wrong twice over. It made the panel string-match on prose to tell the two
/// apart, which `rules/rust.md` forbids — *"customer-facing wording is produced
/// by the C# side from the typed variant"* — and it reported an ending
/// `TailEnd::Idle` itself calls benign under the same code as a failed
/// `nginx -t` or an unreadable log, so a quiet site would surface as a fault.
///
/// The two new codes are named for streaming rpcs in general rather than for
/// this one, because `InstallPhpVersion`, `CreateBackup` and `RestoreBackup`
/// end the same two ways and will want the same answer.
#[must_use]
pub fn tail_terminal(outcome: &Result<TailEnd, SitesOpError>) -> Option<AgentError> {
    let (code, message) = match outcome {
        Err(error) => return Some(to_agent_error(error)),
        // Nobody is left to tell, and nothing went wrong.
        Ok(TailEnd::ClientClosed) => return None,
        Ok(TailEnd::ClientStalled) => (
            ErrorCode::StreamDropped,
            "the log stream was dropped: the client stopped reading it",
        ),
        Ok(TailEnd::Idle) => (
            ErrorCode::StreamIdle,
            "the log stream was closed: nothing was written to the log for the agent's \
             maximum idle time",
        ),
    };

    Some(AgentError {
        code: code as i32,
        message: message.to_owned(),
        tool_output: String::new(),
    })
}

#[cfg(test)]
#[path = "../../tests/services/sites/tail_terminal_tests.rs"]
mod tests;
