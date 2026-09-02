//! Rebuilding one SFTP login's name from the account the panel authorised.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::proto::AgentError;
use crate::services::sites::invalid_input::invalid_input;

/// Rebuilds the login named by `account_username` and the suffix
/// `sftp_username`, returning the account beside it.
///
/// **The name is built, never forwarded.** `SftpUserName` has no constructor
/// that takes a whole name: the only way to obtain one is `for_account`, which
/// applies the account prefix and restricts the suffix to `[a-z0-9]`. So a
/// request cannot name another tenant's login — a suffix that tried would
/// produce a name under the CALLER's own account. That matters more here than
/// almost anywhere else in the contract, because the two rpcs this feeds are
/// "set this login's password" and "delete this login": a forwarded name would
/// let one customer take over or revoke another customer's file access.
///
/// The account is returned as well as the login because the creation needs both
/// — the jail, its mount point and the bind-mount unit's name are all derived
/// from the account, not from the login.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for an
/// empty suffix, for one carrying anything outside `[a-z0-9]` — the separator
/// included, so a suffix cannot smuggle in a second prefix — or for a prefixed
/// result past the system's login-name length limit.
pub fn validated_sftp_user(
    account_username: &str,
    sftp_username: &str,
) -> Result<(AccountName, SftpUserName), AgentError> {
    let account =
        AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))?;
    let user = SftpUserName::for_account(&account, sftp_username)
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, user))
}

#[cfg(test)]
#[path = "../../tests/services/sftp/validated_sftp_user_tests.rs"]
mod tests;
