//! Turning a `CreateFtpsUser` request into the typed input the operation takes.

use maran_ops::ftps::FtpsUserRequest;

use crate::proto::AgentError;
use crate::services::ftps::validated_ftps_user::validated_ftps_user;
use crate::services::sftp::validated_credential::validated_credential;

/// Builds the operation's input from the three values `CreateFtpsUser` carries.
///
/// Every field of the result is a validated type and none of them is a
/// `String`. What that buys is set out on each of the two checks this composes:
/// the login name cannot be another tenant's, and the password cannot break out
/// of the `user:password` line `chpasswd` reads.
///
/// **There is no fourth field, and the absence is the security property.** The
/// jail the login is chrooted into is derived from the account, so no request
/// can name the directory it will be confined to — the whole chroot-escape class
/// of bug has nothing to aim at.
///
/// **The password check is `sftp::validated_credential`, imported rather than
/// copied.** It is one sentence about one type: `Password` refuses the colon and
/// the newline, and both daemons' credentials go through the same `chpasswd`.
/// A second copy here would be the rule of two firing (rules/rust.md) — one
/// could be replaced by a call to the other with nothing observable changing.
/// Its strictly correct home is `services/wire/`, beside `validated_account`,
/// and moving it there is filed as a debt rather than done here: that folder is
/// outside this task's writable scope, and a check that two services share is
/// exactly what `wire/` is for.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for a
/// login suffix that is empty or outside `[a-z0-9]`, for a prefixed login past
/// the system's length limit, or for a password outside the allowed alphabet.
pub fn validated_ftps_creation(
    account_username: &str,
    ftps_username: &str,
    password: &str,
) -> Result<FtpsUserRequest, AgentError> {
    let (account, user) = validated_ftps_user(account_username, ftps_username)?;
    let password = validated_credential(password)?;

    Ok(FtpsUserRequest {
        account,
        user,
        password,
    })
}
