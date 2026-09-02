//! Turning a `SetSftpPassword` request into the values the operation takes.

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::proto::AgentError;
use crate::services::sftp::validated_credential::validated_credential;
use crate::services::sftp::validated_sftp_user::validated_sftp_user;

/// Builds the login and the password `SetSftpPassword` re-credentials with.
///
/// One bundle per request shape rather than two checks chained in the handler,
/// so the handler stays the three steps and nothing else (rules/rust.md
/// "Service anatomy"). The account is dropped here on purpose: setting a
/// password touches nothing the account owns — no jail, no mount, no home — so
/// there is nothing left for the operation to want it for, and passing it on
/// would suggest otherwise.
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
) -> Result<(SftpUserName, Password), AgentError> {
    let (_, user) = validated_sftp_user(account_username, sftp_username)?;
    let password = validated_credential(password)?;

    Ok((user, password))
}

#[cfg(test)]
#[path = "../../tests/services/sftp/validated_password_change_tests.rs"]
mod tests;
