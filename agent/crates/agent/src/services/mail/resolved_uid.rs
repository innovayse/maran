//! The system user id of a hosting account, from the host's user database.

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::{AgentError, ErrorCode};

/// The system user id of a hosting account, read from the host's user database.
///
/// # Errors
///
/// `NotFound` when the account has no system user, `InvalidInput` when it
/// resolves to root or a system account (never a hosting account), and
/// `SystemFailure` when the user database could not be read.
pub fn resolved_uid(account: &AccountName) -> Result<u32, AgentError> {
    AccountIds::resolve(account)
        .map(|ids| ids.uid())
        .map_err(|error| uid_error(&error))
}

/// The one mapping from a failed lookup onto the wire error.
fn uid_error(error: &PrivError) -> AgentError {
    let (code, message) = match error {
        PrivError::NoSuchAccount => (ErrorCode::NotFound, "the account has no system user"),
        PrivError::RootAccount | PrivError::RootGroup | PrivError::SystemAccount => (
            ErrorCode::InvalidInput,
            "the account is not a hosting account",
        ),
        _ => (
            ErrorCode::SystemFailure,
            "the system user database could not be read",
        ),
    };
    AgentError {
        code: code as i32,
        message: message.to_owned(),
        tool_output: String::new(),
    }
}
