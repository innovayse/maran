//! Revalidating what a `CreateAccount` request carries beyond the name.

use maran_ops::accounts::NewAccountSettings;

use crate::proto::{AgentError, CreateAccountRequest};
use crate::services::accounts::validated_inode_limit::validated_inode_limit;
use crate::services::accounts::validated_login_shell::validated_login_shell;

/// Turns a creation request's quota, inode ceiling and shell into the
/// settings the operation takes.
///
/// The account NAME is not read here: the service validates it once for every
/// rpc and names it on the audit slot, and this checks only what is new.
/// `quota_bytes` is carried as it arrived, since the quota tool is what bounds a
/// byte count and every figure is acceptable to it; the inode ceiling and the
/// shell are each read by the function that owns the rule (an absent inode
/// ceiling is unlimited and an unspecified shell is nologin, both for a caller
/// that predates the field).
///
/// # Errors
///
/// Returns the wire's invalid-input error naming the field when the inode
/// ceiling is present and malformed, or the shell is a number the enum does not
/// know.
pub fn validated_new_account(
    request: &CreateAccountRequest,
) -> Result<NewAccountSettings, AgentError> {
    Ok(NewAccountSettings {
        quota_bytes: request.quota_bytes,
        inode_limit: validated_inode_limit(request.inode_limit.as_ref())?,
        shell: validated_login_shell(request.login_shell)?,
    })
}

#[cfg(test)]
#[path = "../../tests/services/accounts/validated_new_account_tests.rs"]
mod tests;
