//! SetSftpPassword: one `user:password` line, over standard input.

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;

use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The separator `chpasswd` expects between the login and its password.
const FIELD_SEPARATOR: char = ':';

/// Sets `user`'s password to `password`.
///
/// # Why standard input, and why that is enough
///
/// `chpasswd` reads `user:password` lines from standard input, and this
/// operation gives it exactly one. The argument vector carries nothing but the
/// program itself, deliberately: a command line is readable through `/proc` by
/// every local user on the host — including every other tenant's SFTP login and
/// every php-fpm pool — so a password passed as an argument is a password that
/// has already leaked to the people it is meant to be kept from. A pipe is
/// readable only by the two processes at its ends.
///
/// One line reaches the tool because a [`Password`] **cannot hold** a newline or
/// a colon. That is not a detail of this function; it is what makes the design
/// safe. A newline would end the line early and start a second one, and a second
/// `user:password` line is a password set for a login the caller does not own —
/// `root:` included. A colon would move the boundary between the two fields of
/// the first line. Neither character has a `Password` value, so neither can
/// arrive here to be escaped or missed: the value is validated, not escaped
/// (rules/security.md §4).
///
/// Read that before widening the password alphabet. Accepting more characters
/// and quoting them here would replace a guarantee the type system enforces with
/// an escaping routine nobody has reviewed.
///
/// # Errors
///
/// - [`SftpError::PasswordRejected`] when `chpasswd` refuses the line — the
///   login exists and its password is unchanged.
/// - [`SftpError::SpawnFailed`] when `chpasswd` could not be run at all, or its
///   standard input could not be written.
pub fn set_sftp_password(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    user: &SftpUserName,
    password: &Password,
) -> Result<(), SftpError> {
    let line = format!("{}{FIELD_SEPARATOR}{}\n", user.as_str(), password.as_str());

    let outcome = host.run(distro.chpasswd_binary(), &[], Some(&line))?;
    if outcome.status != 0 {
        return Err(SftpError::PasswordRejected);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sftp/set_sftp_password_tests.rs"]
mod tests;
