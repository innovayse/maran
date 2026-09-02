//! DeleteSftpUser: the login only, never the files behind it.

use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;

use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// Removes the system login `user`.
///
/// # What is deliberately not removed
///
/// `userdel` is run **without** `-r`. The directory the login's passwd entry
/// points at is the account's jail, and the account's real home is bind-mounted
/// inside it — so `-r` would walk into the mount and delete the customer's
/// entire website, for an operation whose meaning is "revoke one login". An SFTP
/// login is a key to the account's files, not the owner of them; taking a key
/// away destroys nothing.
///
/// The jail and its mount unit are account resources with account lifetime, and
/// they are removed by the account-deletion cascade, not here: an account may
/// have several logins, and unmounting on the first deletion would break the
/// others.
///
/// # Idempotency
///
/// A second deletion reports [`SftpError::NotFound`] and changes nothing, which
/// is what makes a retry after a lost response safe. The decision is `userdel`'s
/// own exit status rather than a lookup first, so there is no gap between
/// checking and acting.
///
/// # Errors
///
/// - [`SftpError::NotFound`] when no such login is on this host.
/// - [`SftpError::SpawnFailed`] when `userdel` refuses for any other reason, or
///   could not be run at all.
pub fn delete_sftp_user(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    user: &SftpUserName,
) -> Result<(), SftpError> {
    let outcome = host.run(distro.userdel_binary(), &[user.as_str()], None)?;
    if outcome.status != 0 {
        return Err(SftpError::from_userdel(outcome.status));
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sftp/delete_sftp_user_tests.rs"]
mod tests;
