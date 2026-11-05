//! The one mapping from monitoring failures onto the wire error.

use maran_ops::monitor::MonitorError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a monitoring failure into the `AgentError` the contract carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **Every variant is a system failure, and the short list is the point.** This
/// area accepts no input at all — none of its rpcs carries a field except the one that names accounts — so
/// there is no `INVALID_INPUT` to map and no `NOT_FOUND`: a unit that is not
/// installed on this host is reported as a STATE, not as a missing resource,
/// and a service that is down is an answer rather than an error. What is left
/// is a machine that could not be read, which is the one thing this area
/// refuses to guess about: a statistics file it cannot understand fails the
/// call instead of being reported as zero, because a zero is a claim about the
/// host and the panel would draw it as one.
///
/// **`tool_output` is empty for every variant, structurally.** No
/// [`MonitorError`] variant has a field that could hold a program's output:
/// the payloads are an exit status, a line number, and a path with a bound.
#[must_use]
pub fn to_agent_error(error: &MonitorError) -> AgentError {
    let code = match error {
        MonitorError::HostStatisticsUnavailable
        | MonitorError::FilesystemUnavailable
        | MonitorError::ServiceManagerUnavailable { .. }
        | MonitorError::AccountsUnavailable
        | MonitorError::SshdConfigUnavailable
        | MonitorError::ClockStateUnavailable => ErrorCode::SystemFailure,
        // The package manager could not be read: not started, a status that is not
        // a success, a line the reader does not know, or a catalogue never built.
        // Named ahead of the wildcard so a missing arm cannot compile silently,
        // and never an empty answer: the panel draws a badge for "no updates" and
        // must not be handed one for a read that failed (issue #324). `message`
        // carries only the reason's own words, an exit status or a line number.
        MonitorError::UpdateCatalogueUnavailable { .. } => ErrorCode::SystemFailure,
        // The reboot probe could not be run, or answered with a status it gives
        // no meaning to (issue #163). Named ahead of the wildcard so a missing arm
        // cannot compile silently, and never `NOT_REQUIRED`: the panel must not be
        // told a host is safe to leave by a probe that did not run. `message`
        // carries an exit status at most.
        MonitorError::RebootProbeNotRun | MonitorError::RebootProbeFailed { .. } => {
            ErrorCode::SystemFailure
        }
        // A deadline or an output cap ended the program and its process group was
        // killed. This is a fault of the host, never a verdict on the request: the
        // work may or may not have finished, and `tool_output` stays empty because
        // what the program printed before the kill is neither complete nor safe to
        // forward. Named ahead of the wildcard so a missing arm cannot compile
        // silently (threat note, plan item 11).
        MonitorError::Bound(_) => ErrorCode::SystemFailure,
        // MonitorError is #[non_exhaustive] (rules/rust.md), so a variant added
        // in the ops crate lands here rather than failing this build. It maps to
        // a system failure, which is what every variant here already is.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/monitor/monitor_status_tests.rs"]
mod tests;
