//! Turning a `CreateSftpUser` request into the typed input the operation takes.

use maran_ops::sftp::SftpUserRequest;

use crate::proto::AgentError;
use crate::services::sftp::validated_credential::validated_credential;
use crate::services::sftp::validated_sftp_user::validated_sftp_user;

/// Builds the operation's input from the three values `CreateSftpUser` carries.
///
/// Every field of the result is a validated type and none of them is a
/// `String`. What that buys is set out on each of the two checks this composes:
/// the login name cannot be another tenant's, and the password cannot break out
/// of the `user:password` line `chpasswd` reads.
///
/// **There is no fourth field, and the absence is the security property.** The
/// jail the login is chrooted into is derived from the account, so no request
/// can name the directory it will be confined to — the whole chroot-escape
/// class of bug has nothing to aim at. `ftp.proto` reserves the retired
/// `chroot_path` field number and name so that protoc itself refuses a
/// re-introduction.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for a
/// login suffix that is empty or outside `[a-z0-9]`, for a prefixed login past
/// the system's length limit, or for a password outside the allowed alphabet.
pub fn validated_creation(
    account_username: &str,
    sftp_username: &str,
    password: &str,
) -> Result<SftpUserRequest, AgentError> {
    let (account, user) = validated_sftp_user(account_username, sftp_username)?;
    let password = validated_credential(password)?;

    Ok(SftpUserRequest {
        account,
        user,
        password,
    })
}
