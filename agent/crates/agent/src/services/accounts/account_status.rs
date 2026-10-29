//! The one mapping from account operation failures onto the wire error.

use maran_ops::accounts::AccountError;

use crate::proto::{AgentError, ErrorCode};

/// Converts an operation failure into the `AgentError` the contract carries.
///
/// It lives beside the service rather than inside it so that the match never grows
/// into the handler, and so one variant maps to one code in exactly one place
/// (rules/rust.md "Service anatomy").
///
/// **`tool_output` is empty for every variant of this area, and that is
/// structural rather than remembered: no variant of [`AccountError`] has a field
/// a tool's output could occupy.** `CommandFailed` used to carry the refusing
/// program's standard error and this function copied it here. The account area
/// is the one area besides the database area whose tools read credential
/// material — `getent shadow` answers with a password hash — so it was given the
/// database area's shape: the wire carries the program and its exit status, and
/// the tool's own words are written once to the agent's root-owned log
/// (rules/security.md item 8). `sites` and `firewall` still fill `tool_output`
/// and are right to: their tools never touch a credential.
///
/// A refusal to act on a user that is not a hosting account
/// ([`AccountError::NotManaged`]) is `InvalidInput` and its sentence names no
/// rule: the reason is in the agent's log and not on the wire, so a caller
/// probing the rules learns nothing about their shape. A user whose identity
/// changed under a running deletion ([`AccountError::IdentityChanged`]) is a
/// system failure, because the panel's picture of the host no longer matches
/// it.
#[must_use]
pub fn to_agent_error(error: &AccountError) -> AgentError {
    let code = match error {
        AccountError::InvalidName(_) => ErrorCode::InvalidInput,
        AccountError::AlreadyExists { .. } => ErrorCode::AlreadyExists,
        AccountError::NotFound { .. } => ErrorCode::NotFound,
        AccountError::CommandFailed { .. }
        | AccountError::CommandUnavailable { .. }
        | AccountError::UnreadableOutput { .. } => ErrorCode::SystemFailure,
        // The refusals that stop a deletion before `userdel`. They carry
        // the refusing area's own sentence in `message`: the sentence is this
        // agent's, not a tool's, so there is no client or daemon output in it to
        // redact. A system failure and not a `NotFound`, because the account IS
        // still there — deliberately, and the panel must report a fault rather
        // than treat the deletion as done.
        AccountError::PoolRemoval { .. }
        | AccountError::DatabaseRemoval { .. }
        | AccountError::SftpRemoval { .. }
        | AccountError::FtpsRemoval { .. } => ErrorCode::SystemFailure,
        // Processes of the account's uid survived the bounded kill rounds, so the
        // deletion stopped before `userdel` with the user, its uid and its home
        // still present. SYSTEM_FAILURE: the panel must report a fault and keep
        // the account, and the same deletion can be retried once the survivor has
        // exited. Named ahead of the wildcard so a missing arm cannot compile
        // silently. The message carries the account name only.
        AccountError::ProcessesRemain { .. } => ErrorCode::SystemFailure,
        // The name is well formed but the user behind it is not a hosting account
        // (reserved, a system identity, a home elsewhere). INVALID_INPUT: not
        // NOT_FOUND, because the user may exist, and not SYSTEM_FAILURE, because
        // nothing on this host is broken and the panel must not report a fault for
        // a refusal. Nothing was run.
        AccountError::NotManaged { .. } => ErrorCode::InvalidInput,
        // The user was replaced while a deletion waited, so `userdel` did not run.
        AccountError::IdentityChanged { .. } => ErrorCode::SystemFailure,
        // Another operation for this same account holds the account's lock, so
        // this one was refused at its first statement and `userdel` never ran.
        // ACCOUNT_BUSY and not SYSTEM_FAILURE: nothing on this host is broken,
        // nothing changed, and the panel can say "try again in a moment" and
        // reissue the identical request — which is exactly what
        // `ERROR_CODE_ACCOUNT_BUSY` is defined to mean in `common.proto`.
        AccountError::Busy { .. } => ErrorCode::AccountBusy,
        // A deadline or an output cap ended the program and its process group was
        // killed. This is a fault of the host, never a verdict on the request: the
        // work may or may not have finished, and `tool_output` stays empty because
        // what the program printed before the kill is neither complete nor safe to
        // forward. Named ahead of the wildcard so a missing arm cannot compile
        // silently (threat note, plan item 11).
        AccountError::Bound(_) => ErrorCode::SystemFailure,
        // AccountError is #[non_exhaustive] (rules/rust.md), so a variant added in the
        // ops crate lands here rather than failing this build. It maps to a system
        // failure: the panel then reports a fault instead of silently treating an
        // unclassified failure as "not found" and carrying on.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        // Empty for every variant, unconditionally: there is nothing in this
        // area's error to put here. Written as a literal rather than as a
        // `match` arm so that a variant added to `AccountError` cannot acquire
        // tool output by being added to a list.
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/account_status_tests.rs"]
mod tests;
