//! The one mapping from cron operation failures onto the wire error.

use maran_ops::cron::CronError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a cron operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **`tool_output` is empty for every variant, and that is structural rather
/// than a choice made here.** No [`CronError`] variant has a field that could
/// hold a program's output: every payload is an `i32`. The realistic leak in
/// this area is `crontab(1)` quoting back the table it refused, and a managed
/// crontab carries the account's own environment assignments — an operator who
/// set `API_TOKEN=…` through the panel would find it in the operator log and in
/// every error path above it. There is nothing here to copy that into, so this
/// mapping cannot reintroduce it, and a future variant carrying a string would
/// have to be added deliberately in `ops` first (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &CronError) -> AgentError {
    let code = match error {
        // `cron.proto`: "creating an entry with identical schedule and command
        // to an existing one returns AlreadyExists rather than duplicating it".
        // An idempotency outcome, not a fault.
        CronError::AlreadyExists => ErrorCode::AlreadyExists,
        // `cron.proto`: "deleting a non-existent entry_id returns NotFound",
        // and updating, toggling or reading the output of one the account does
        // not own is the same shape of answer.
        CronError::NotFound => ErrorCode::NotFound,
        // Faults of this machine, not of the request. Every one of them leaves
        // the account's live crontab exactly as it was: `crontab(1)` installs a
        // table or it does not, an entry file writes or it does not, and there
        // is no half-applied state between them for the panel to reconcile.
        CronError::Privilege(_)
        | CronError::CrontabRefused { .. }
        | CronError::EntryFileUnwritable
        | CronError::EntryFileUnreadable
        | CronError::EntryFileUnremovable
        | CronError::EntryIdUnavailable => ErrorCode::SystemFailure,
        // CronError is #[non_exhaustive] (rules/rust.md), so a variant added in
        // the ops crate lands here rather than failing this build. It maps to a
        // system failure: the panel then reports a fault instead of silently
        // treating an unclassified failure as "not found" and carrying on.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/cron/cron_status_tests.rs"]
mod tests;
