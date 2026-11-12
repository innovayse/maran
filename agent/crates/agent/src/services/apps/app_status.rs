//! The one mapping from app operation failures onto the wire error.

use maran_ops::apps::AppsOpError;
use maran_ops::php::PhpOpError;

use crate::proto::{AgentError, ErrorCode};
use crate::services::php::php_status::to_agent_error as php_to_agent_error;

/// Converts an app operation failure into the `AgentError` the contract carries.
///
/// The package and PHP failures are the PHP area's, mapped by its own function so a package
/// source that refused, one nobody could reach and a package no source carries travel as the
/// same three codes whichever rpc hit them. `tool_output` is for an operator's log and never
/// rendered to a customer (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &AppsOpError) -> AgentError {
    let (code, tool_output) = match error {
        // A request the agent will not carry out as sent: an id outside the catalogue, an action
        // the app cannot take. No tool ran.
        AppsOpError::UnsupportedApp { .. } | AppsOpError::ActionRefused { .. } => {
            (ErrorCode::InvalidInput, String::new())
        }
        AppsOpError::NotInstalled { .. } => (ErrorCode::AppNotInstalled, String::new()),
        // A removal that would take other packages with it is the app being used by them: the
        // same refusal an operator answers by removing those first.
        AppsOpError::InUse { .. } | AppsOpError::RemovalWouldRemoveOthers { .. } => {
            (ErrorCode::AppInUse, String::new())
        }
        AppsOpError::WebServerAlreadyInstalled { .. } => {
            (ErrorCode::WebServerAlreadyInstalled, String::new())
        }
        // Another major put there by a module stream: the operator switches by uninstalling it.
        AppsOpError::NodeMajorAlreadyInstalled { .. } => {
            (ErrorCode::NodeMajorAlreadyInstalled, String::new())
        }
        // The four Node.js refusals below changed nothing and are answered by the operator, each
        // by a different act, so each travels as its own code and the panel can say which
        // (issue #532): a Node.js the module system does not own, a stream enabled by hand, an
        // interpreter that cannot be read, and another module operation in flight.
        AppsOpError::UnmanagedNodeInstalled { .. } => {
            (ErrorCode::NodeUnmanagedInstall, String::new())
        }
        AppsOpError::NodeStreamEnabledByOperator { .. } => {
            (ErrorCode::NodeStreamEnabledByOperator, String::new())
        }
        AppsOpError::NodeInterpreterUnreadable { .. } => {
            (ErrorCode::NodeInterpreterUnreadable, String::new())
        }
        AppsOpError::NodejsOperationInProgress => {
            (ErrorCode::NodeOperationInProgress, String::new())
        }
        AppsOpError::ServiceManager { stderr } => (ErrorCode::SystemFailure, stderr.clone()),
        // Refused or failed with the host at fault: nothing the operator is told to run.
        AppsOpError::StillAbsent { .. }
        | AppsOpError::RemovalNotSimulated { .. }
        | AppsOpError::HostUnreadable { .. } => (ErrorCode::SystemFailure, String::new()),
        AppsOpError::Package(package) => {
            return php_to_agent_error(&PhpOpError::from(package.clone()));
        }
        AppsOpError::Php(php) => return php_to_agent_error(php),
        // AppsOpError is #[non_exhaustive]: a variant added in the ops crate is a fault of this
        // host until this match says otherwise.
        _ => (ErrorCode::SystemFailure, String::new()),
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output,
    }
}

#[cfg(test)]
#[path = "../../tests/services/apps/app_status_tests.rs"]
mod tests;
