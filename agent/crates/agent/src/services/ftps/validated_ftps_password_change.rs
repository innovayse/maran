//! Turning a `SetFtpsPassword` request into the values the operation takes.

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::ftps::validated_ftps_user::validated_ftps_user;
use crate::services::sftp::validated_credential::validated_credential;

/// Builds the login and the password `SetFtpsPassword` re-credentials with.
///
/// One bundle per request shape rather than two checks chained in the handler,
/// so the handler stays the three steps and nothing else (rules/rust.md
/// "Service anatomy").
///
/// **The account is CARRIED, and this rpc could not be safe without it.** Three
/// things downstream need it, and none can be recovered from the login name:
/// the per-account lock, without which a suspension landing between the shadow
/// read and `chpasswd` is undone on the strength of an expired fact; the
/// account's FTPS jail, which is what tells this login from a NEIGHBOURING
/// account's login of the same spelling — `<account>_<name>` has no unique
/// decomposition when account names may carry the separator, so a request
/// authorised for one tenant would otherwise write a credential onto another
/// tenant's login; and the same jail again, which is what stops this rpc
/// reaching the account's own SFTP login, whose name has the identical shape.
///
/// The two checks here are the ones that make the rpc safe to expose at all. The
/// login name is REBUILT from the account rather than taken off the wire, so a
/// request cannot name a login outside the account it was authorised for; and
/// the password cannot hold a colon or a newline, so it cannot add a second
/// `user:password` line to what `chpasswd` reads — which would set a password
/// for a login the caller does not own, `root:` included.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for a
/// login suffix that is empty or outside `[a-z0-9]`, for a prefixed login past
/// the system's length limit, or for a password outside the allowed alphabet.
/// An empty password is refused by that last check rather than treated as
/// "leave it unchanged" (`ftp.proto`): a silent no-op would report success for a
/// credential that was never rotated.
pub fn validated_ftps_password_change(
    account_username: &str,
    ftps_username: &str,
    password: &str,
) -> Result<(AccountName, FtpsUserName, Password), AgentError> {
    let (account, user) = validated_ftps_user(account_username, ftps_username)?;
    let password = validated_credential(password)?;

    Ok((account, user, password))
}
