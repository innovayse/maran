//! Revalidating the action of an app rpc.

use maran_ops::apps::AppServiceAction;

use crate::proto::{AgentError, AppServiceAction as WireAction, ErrorCode};

/// Reads the action of a request.
///
/// # Errors
///
/// Returns the wire error for an action the contract does not know, or the unset zero: a
/// request that names no action does nothing rather than defaulting to one.
pub fn validated_action(action: i32) -> Result<AppServiceAction, AgentError> {
    match WireAction::try_from(action) {
        Ok(WireAction::Start) => Ok(AppServiceAction::Start),
        Ok(WireAction::Stop) => Ok(AppServiceAction::Stop),
        Ok(WireAction::Restart) => Ok(AppServiceAction::Restart),
        Ok(WireAction::Reload) => Ok(AppServiceAction::Reload),
        Ok(WireAction::Unspecified) | Err(_) => Err(AgentError {
            code: ErrorCode::InvalidInput as i32,
            message: "the service action is not one the agent knows".to_owned(),
            tool_output: String::new(),
        }),
    }
}

#[cfg(test)]
#[path = "../../tests/services/apps/validated_action_tests.rs"]
mod tests;
