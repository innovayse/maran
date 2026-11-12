//! Revalidating what a `SetLocalSubmission` request carries.

use maran_agent_core::validation::mail::local_submission::LocalSubmission;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::{AgentError, SetLocalSubmissionRequest};
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds the binding a `SetLocalSubmission` request asks for through the
/// validated types, so nothing reaches a rendered map file that did not pass the
/// agent's own check.
///
/// The panel validated all of it already. This is the agent's own gate, and it
/// decides lines of the filter's maps: a separator in a domain or a login would
/// change what a line means. The system user id is NOT part of the request: the
/// agent resolves it from the account's user name through `resolve_uid`, so the
/// number the filter trusts comes from the host's user database and never from a
/// caller.
///
/// # Errors
///
/// Returns the wire error, code `InvalidInput`, for the first part the agent will
/// not accept (the message names the part and the rule, never the refused value),
/// or whatever `resolve_uid` answers when the account has no usable system user.
pub fn validated_local_submission(
    request: &SetLocalSubmissionRequest,
    resolve_uid: impl FnOnce(&AccountName) -> Result<u32, AgentError>,
) -> Result<LocalSubmission, AgentError> {
    let account = AccountName::parse(&request.account_username)
        .map_err(|error| invalid_input(format!("account: {error}")))?;
    let uid = resolve_uid(&account)?;

    let alias_fanouts: Vec<(&str, u32)> = request
        .alias_fanouts
        .iter()
        .map(|fanout| (fanout.alias.as_str(), fanout.external_targets))
        .collect();

    LocalSubmission::parse(
        account.as_str(),
        uid,
        &request.domains,
        request.send_limit_per_hour,
        &request.mailbox_logins,
        &alias_fanouts,
    )
    .map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/mail/validated_local_submission_tests.rs"]
mod tests;
