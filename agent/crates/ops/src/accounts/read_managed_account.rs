//! Proving, from the password database file, that a name is a hosting account.

use std::fs;

use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
use maran_agent_core::validation::system::name::AccountName;

/// Reads the local password database at `passwd_database` and resolves
/// `account` against it.
///
/// The one host-side step the cron, SFTP and FTPS hosts share: read the file
/// whole, hand its text to [`ManagedAccount::resolve`], which reads nothing
/// itself. Each host maps the refusal onto its own error list.
///
/// `passwd_database` comes from the `DistroAdapter`, so `ops` names no platform
/// location (rules/architecture.md). A file that cannot be read is
/// [`ManagedAccountError::NotFound`]: absence of evidence is not a pass, and the
/// refusal is the same one an absent row gets.
///
/// # Errors
///
/// Every refusal of [`ManagedAccount::resolve`], and
/// [`ManagedAccountError::NotFound`] when the file cannot be read.
pub(crate) fn read_managed_account(
    passwd_database: &str,
    account: &AccountName,
) -> Result<ManagedAccount, ManagedAccountError> {
    let passwd = fs::read_to_string(passwd_database).map_err(|_| ManagedAccountError::NotFound)?;

    ManagedAccount::resolve(account.clone(), &passwd)
}
