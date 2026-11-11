//! Reading the Postfix user's uid out of the host's user database.

use crate::mail::mail_error::MailError;
use crate::mail::model::mail_daemon::MailDaemon;

/// The uid of `user` in the text of `/etc/passwd`.
///
/// # Errors
///
/// Returns [`MailError::NotInstalled`] for Postfix when the user has no usable
/// line: a host without the Postfix user has no Postfix, and the rule's exempt
/// list must never be guessed.
pub fn postfix_uid_from_passwd(passwd: &str, user: &str) -> Result<u32, MailError> {
    passwd
        .lines()
        .filter_map(|line| {
            let mut fields = line.split(':');
            let name = fields.next()?;
            let uid = fields.nth(1)?;
            (name == user).then(|| uid.parse::<u32>().ok()).flatten()
        })
        .next()
        .ok_or(MailError::NotInstalled {
            daemon: MailDaemon::Postfix,
        })
}

#[cfg(test)]
#[path = "../tests/mail/postfix_uid_from_passwd_tests.rs"]
mod tests;
