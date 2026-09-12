//! Rebuilding one FTPS login's name from the account the panel authorised.

use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds the login named by `account_username` and the suffix
/// `ftps_username`, returning the account beside it.
///
/// **The name is built, never forwarded.** [`FtpsUserName`] has no constructor
/// that takes a whole name: the only way to obtain one is `for_account`, which
/// applies the account prefix and restricts the suffix to `[a-z0-9]`. So a
/// request cannot name another tenant's login — a suffix that tried would
/// produce a name under the CALLER's own account. That matters more here than
/// almost anywhere else in the contract, because the two rpcs this feeds are
/// "set this login's password" and "delete this login": a forwarded name would
/// let one customer take over or revoke another customer's file access.
///
/// The account is returned as well as the login, and it is not decoration. It is
/// what the operation takes its per-account lock on, and what the login's jail
/// is derived from for the ownership check — neither of which can be recovered
/// from the login name, since `<account>_<name>` has no unique decomposition
/// when account names may carry the separator.
///
/// It is a different type from the SFTP login's name, which is the point: the
/// two carry different group memberships and live in different jails, so the
/// compiler refuses the swap rather than a reviewer having to catch it.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for an
/// empty suffix, for one carrying anything outside `[a-z0-9]` — the separator
/// included, so a suffix cannot smuggle in a second prefix — or for a prefixed
/// result past the system's login-name length limit.
pub fn validated_ftps_user(
    account_username: &str,
    ftps_username: &str,
) -> Result<(AccountName, FtpsUserName), AgentError> {
    let account =
        AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))?;
    let user = FtpsUserName::for_account(&account, ftps_username)
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, user))
}

#[cfg(test)]
#[path = "../../tests/services/ftps/validated_ftps_user_tests.rs"]
mod tests;
