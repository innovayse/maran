//! Asking the password database which of an account's logins still work.

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::model::sftp_login_suspension_fact::SftpLoginSuspensionFact;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The argument that makes `passwd` report a login's state instead of changing
/// it.
const PASSWORD_STATUS_ARGUMENT: &str = "-S";

/// What `passwd -S` prints in its second field for a locked password — the
/// FIRST LETTER of it, because the two families do not spell the rest the same.
///
/// Measured on both polygon images rather than assumed, because assuming cost
/// the RHEL family a working suspension:
///
/// ```text
/// ubuntu24:  u1 P  …   u1 L  …        (Debian shadow-utils)
/// alma9:     u1 PS …   u1 LK …        (RHEL's passwd)
/// ```
///
/// An exact comparison against `"L"` is therefore true on Debian and FALSE on
/// every RHEL host, which made the login half of the suspension attestation
/// report every account as unlocked there and refuse every suspension. Matching
/// the first letter is unambiguous on both: the other states are `P`/`PS` for a
/// usable password and `NP` for none at all, and neither begins with `L`.
const LOCKED_PASSWORD_PREFIX: char = 'L';

/// Reports every SFTP login `account` holds and whether each is locked.
///
/// Read-only, and asked of the HOST's password database rather than of a list
/// the panel supplies — the reason `remove_account_sftp` gives: a list can only
/// describe what the panel remembers creating, and a login it has forgotten is
/// exactly the one still letting a suspended customer in.
///
/// # What an empty answer means
///
/// That the account holds no SFTP login, which is a suspended state and not a
/// failure to look. The blind answer this could otherwise degenerate into
/// cannot happen: a password database that cannot be enumerated returns the
/// error below rather than an empty list, and so does a `passwd` that refuses
/// or prints something unreadable.
///
/// # Errors
///
/// - [`SftpError::AccountMissing`] when the password database cannot be read.
/// - [`SftpError::SpawnFailed`] when `passwd -S` refuses or could not be run.
/// - [`SftpError::StatusUnreadable`] when it printed something this agent
///   cannot read as the state of the login it asked about.
pub fn inspect_account_logins(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<Vec<SftpLoginSuspensionFact>, SftpError> {
    let jail = AccountJail::for_account(account, distro.systemd_unit_directory());
    let logins = host.account_logins(distro.passwd_database(), account, jail.directory())?;
    let mut facts = Vec::with_capacity(logins.len());

    for user in logins {
        let locked = login_locked(host, distro, user.as_str())?;
        facts.push(SftpLoginSuspensionFact { user, locked });
    }

    Ok(facts)
}

/// Whether `username`'s password is locked, as `passwd -S` reports it.
///
/// The name on the line is compared with the one that was asked about before
/// the state beside it is believed. `passwd -S` with no argument prints the
/// CALLER's own account, so a line about somebody else is an answer to a
/// different question, and reading its second letter would report root's state
/// as a customer's.
fn login_locked(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    username: &str,
) -> Result<bool, SftpError> {
    let outcome = host.run(
        distro.passwd_binary(),
        &[PASSWORD_STATUS_ARGUMENT, username],
        None,
    )?;
    if outcome.status != 0 {
        return Err(SftpError::SpawnFailed {
            code: outcome.status,
        });
    }

    let mut fields = outcome
        .stdout
        .lines()
        .next()
        .unwrap_or_default()
        .split_whitespace();

    match (fields.next(), fields.next()) {
        (Some(name), Some(state)) if name == username => {
            Ok(state.starts_with(LOCKED_PASSWORD_PREFIX))
        }
        _ => Err(SftpError::StatusUnreadable),
    }
}

#[cfg(test)]
#[path = "../tests/sftp/inspect_account_logins_tests.rs"]
mod tests;
