//! DeleteSftpUser: the login only, never the files behind it.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;

use crate::accounts::take_account_lock;
use crate::sftp::model::account_jail::AccountJail;
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
/// # The login has to BELONG to the account
///
/// This is the whole reason `account` is a parameter, and it is the reason this
/// operation is not one line. A login name is `<account>_<name>` and an
/// [`AccountName`] may itself contain the separator, so the login `bob` of
/// account `alice` and the HOSTING ACCOUNT `alice_bob` are the same eleven
/// characters. No decode can tell them apart; the account the panel authorised
/// can, and it is passed in rather than parsed out.
///
/// So the name is looked up in the same enumeration
/// [`set_sftp_password`](crate::sftp::set_sftp_password) and
/// `remove_account_sftp` use: a candidate is this account's login only when its
/// passwd home is exactly this account's jail — a value the agent itself wrote
/// when it created the login, never a guess about a name. A hosting account's
/// home is `/home/<account>`, so a colliding name is never mistaken for a login.
///
/// Without this the operation ran `userdel` on whatever the name resolved to.
/// That is a cross-tenant destruction — the neighbour loses their system
/// identity, and because `userdel` runs without `-r` their populated home is
/// left owned by a uid the next `useradd` can be given. The threat note is
/// `docs/superpowers/notes/2026-09-09-transfer-login-deletion-threat-note.md`.
///
/// **A login the account does not hold is [`SftpError::NotFound`], and not a
/// refusal of its own.** Telling the caller "that login exists but is not
/// yours" would confirm a neighbouring tenant's existence on the host, which is
/// the same disclosure rules/security.md refuses in the panel when it answers
/// 404 rather than 403 for a resource another account owns. The price is that an
/// operator's typo and an attacker's probe get one answer; the audit trail is
/// where the two are told apart.
///
/// # The lock
///
/// The hosting account's lock (`crate::accounts::account_lock`) is taken as the
/// first statement and held until this returns, because the check above and the
/// `userdel` that acts on it are one read-modify-write: without it a login
/// created between the enumeration and the removal, or an account deletion
/// running the cascade, would make the fact the decision rests on expire before
/// it is used. It is the SAME lock `create_sftp_user`, `set_sftp_password`, the
/// account's deletion and the four backup operations take — no new lock is
/// introduced (rules/rust.md, "What this agent serialises") — and it never
/// waits, so it adds no edge to the wait-for graph. It is process-local: it
/// serialises this binary against itself and nothing else, not a second agent
/// and not an operator's own `userdel`.
///
/// # Idempotency
///
/// A second deletion reports [`SftpError::NotFound`] and changes nothing, which
/// is what makes a retry after a lost response safe. `userdel`'s own exit status
/// is still mapped to the same variant, so the two ways of not finding the login
/// — the ownership enumeration and the tool itself — converge on one answer.
///
/// # Errors
///
/// - [`SftpError::AccountBusy`] when another operation for the hosting account
///   is already running. Nothing was read and nothing was removed.
/// - [`SftpError::NotFound`] when the account holds no SFTP login of that name —
///   whether because the name is on no passwd entry at all, or because the entry
///   it names belongs to somebody else.
/// - [`SftpError::SpawnFailed`] when `userdel` refuses for any other reason, or
///   could not be run at all, and when the password database cannot be read.
pub fn delete_sftp_user(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    user: &SftpUserName,
) -> Result<(), SftpError> {
    // Owned guard, so the lock is released by every path out of this function,
    // including every error path and a panic.
    let _guard = take_account_lock(account).ok_or(SftpError::AccountBusy)?;

    let jail = AccountJail::for_account(account, distro.systemd_unit_directory());
    let logins = host.account_logins(distro.passwd_database(), account, jail.directory())?;
    if !logins.iter().any(|held| held.as_str() == user.as_str()) {
        return Err(SftpError::NotFound);
    }

    delete_sftp_user_under_lock(host, distro, user)
}

/// The removal itself, with the hosting account's lock ALREADY held and the
/// login already established as the account's.
///
/// Split from [`delete_sftp_user`] for the reason
/// [`set_sftp_password_under_lock`](crate::sftp::set_sftp_password) is:
/// the account-deletion cascade reaches this through `remove_account_sftp`
/// while holding the account's lock, and the lock never waits — so an entry
/// point that took it again would refuse the cascade its own deletion. The lock
/// stays taken exactly once, at an operation's entry, which is the property the
/// agent's no-cycle argument rests on.
///
/// The ownership check is the entry point's too, and the cascade does not need
/// it: it removes the logins of the very enumeration this check consults, so
/// re-filtering the same list against itself would answer nothing.
///
/// # Errors
///
/// - [`SftpError::NotFound`] when `userdel` reports no such login.
/// - [`SftpError::SpawnFailed`] when `userdel` refuses for any other reason, or
///   could not be run at all.
pub(crate) fn delete_sftp_user_under_lock(
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
