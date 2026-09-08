//! SetAccountLoginsLocked: every key into one account's home, turned at once.

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The flag that prefixes a password hash so nothing can match it.
const LOCK: &str = "--lock";

/// The flag that removes that prefix again.
const UNLOCK: &str = "--unlock";

/// Locks, or unlocks, every SFTP login `account` holds.
///
/// # Why this operation has to exist at all
///
/// Because `usermod --lock <account>` does not reach these logins. Each is its
/// OWN passwd entry, named `<account>_<name>` and created with
/// `useradd --non-unique --uid <account uid>` so that it writes as the account
/// — which means locking the account's own entry leaves every one of them
/// authenticating. That is not "a site keeps serving": it is a live write
/// credential into a customer's home surviving the suspension that was meant
/// to take it away.
///
/// # Where the list comes from
///
/// The host's password database, through the same enumeration
/// `remove_account_sftp` uses, and never a list the panel supplies. A list can
/// only describe what the panel remembers creating; a login it has forgotten is
/// exactly the one that would keep working.
///
/// # What it does not do
///
/// Delete anything, and change no password. Locking prefixes the stored hash
/// and unlocking removes that prefix, so the resume gives the customer back the
/// credential they already had rather than a new one they would have to be
/// told about. The jail, the bind mount and the files behind the logins are
/// untouched.
///
/// # Idempotency, and the one login that does not come back
///
/// `usermod --lock` and `usermod --unlock` are both idempotent and both answer
/// zero, so either direction may be repeated. The exception is measurable and
/// is stated rather than hidden: a login that has NO password at all cannot be
/// unlocked — `usermod` says so and still exits zero — so it stays locked after
/// a resume. Every login this agent creates is given a password in the same
/// operation that creates it, so such a login was made by hand;
/// [`inspect_account_logins`](super::inspect_account_logins) reports it as
/// still locked and the panel says so instead of pretending otherwise.
///
/// An account with no logins is a success that touches nothing.
///
/// # Errors
///
/// - [`SftpError::AccountMissing`] when the password database cannot be read.
/// - [`SftpError::SpawnFailed`] when `usermod` refuses a login for any reason,
///   or could not be run at all. The first refusal stops the operation: a
///   partially locked account is a state the caller must be able to see, and
///   the attestation will report exactly which logins are still open.
pub fn set_account_logins_locked(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    locked: bool,
) -> Result<(), SftpError> {
    let jail = AccountJail::for_account(account, distro.systemd_unit_directory());
    let flag = if locked { LOCK } else { UNLOCK };

    for user in host.account_logins(distro.passwd_database(), account, jail.directory())? {
        let outcome = host.run(distro.usermod_binary(), &[flag, user.as_str()], None)?;
        if outcome.status != 0 {
            return Err(SftpError::SpawnFailed {
                code: outcome.status,
            });
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sftp/set_account_logins_locked_tests.rs"]
mod tests;
