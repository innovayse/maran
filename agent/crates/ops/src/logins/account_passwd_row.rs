//! Finding the hosting account's own row in the host's password database.

use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::validation::system::name::AccountName;

use crate::logins::logins_error::LoginsError;

/// The password-database row belonging to `account` itself.
///
/// The row whose name is EXACTLY the validated account name, and never one
/// selected by a prefix, a suffix or a separator. Account names may contain the
/// separator this agent builds login names with, so `alice_bob` is both a
/// possible account and a possible login of the account `alice`; a comparison
/// that was not exact would hand a neighbouring tenant's row back as this
/// account's, which is how a cross-tenant destructive operation has already
/// shipped from this tree once.
///
/// It is its own unit because two operations need it and the second one needs it
/// for something far more dangerous than the first: [`account_logins`] reads the
/// uid to decide which rows belong to the account, and
/// [`end_account_sessions`] reads it to decide which uid's processes to KILL.
/// The rule of two (rules/rust.md) says the second copy moves to a named home,
/// and here a second copy that drifted would be a second answer to "whose
/// processes are these".
///
/// [`account_logins`]: super::account_logins::account_logins
/// [`end_account_sessions`]: super::end_account_sessions::end_account_sessions
///
/// # Errors
///
/// - [`LoginsError::AccountMissing`] when the database holds no row for the
///   account. Not an empty answer and not a default uid: without the row there
///   is no uid, and every caller of this function would otherwise have to
///   invent one.
pub(crate) fn account_passwd_row<'rows>(
    rows: &'rows [SystemAccount],
    account: &AccountName,
) -> Result<&'rows SystemAccount, LoginsError> {
    rows.iter()
        .find(|row| row.name == account.as_str())
        .ok_or(LoginsError::AccountMissing)
}

#[cfg(test)]
#[path = "../tests/logins/account_passwd_row_tests.rs"]
mod tests;
