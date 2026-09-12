//! Turning a `SetSftpPassword` request into the values the operation takes.

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::proto::AgentError;
use crate::services::sftp::validated_credential::validated_credential;
use crate::services::sftp::validated_sftp_user::validated_sftp_user;

/// Builds the login and the password `SetSftpPassword` re-credentials with.
///
/// One bundle per request shape rather than two checks chained in the handler,
/// so the handler stays the three steps and nothing else (rules/rust.md
/// "Service anatomy"). The account is CARRIED rather than dropped, and it used
/// to be dropped on the argument that setting a password touches nothing the
/// account owns. It touches two things: the account's per-account lock, which is
/// what keeps a password change from landing inside a suspension, and the
/// account's jail, which is what tells this login from a neighbouring account's
/// login of the same spelling. Neither can be recovered from the login name —
/// `<account>_<name>` has no unique decomposition when account names may carry
/// the separator — so the account the panel authorised is passed on, and stays
/// the source of truth.
///
/// The two checks are the ones that make this rpc safe to expose at all. The
/// login name is REBUILT from the account rather than taken off the wire, so a
/// request cannot re-credential another tenant's login; and the password cannot
/// hold a colon or a newline, so it cannot add a second `user:password` line to
/// what `chpasswd` reads — which would set a password for a login the caller
/// does not own, `root:` included.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for a
/// login suffix that is empty or outside `[a-z0-9]`, for a prefixed login past
/// the system's length limit, or for a password outside the allowed alphabet.
/// An empty password is refused by that last check rather than treated as
/// "leave it unchanged" (`ftp.proto`): a silent no-op would report success for
/// a credential that was never rotated.
pub fn validated_password_change(
    account_username: &str,
    sftp_username: &str,
    password: &str,
) -> Result<(AccountName, SftpUserName, Password), AgentError> {
    let (account, user) = validated_sftp_user(account_username, sftp_username)?;
    let password = validated_credential(password)?;

    Ok((account, user, password))
}

#[cfg(test)]
#[path = "../../tests/services/sftp/validated_password_change_tests.rs"]
mod tests;
