//! Proving that the account a login operation names is a hosting account.

use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::logins::logins_error::LoginsError;
use crate::logins::logins_host::LoginsHost;

/// Resolves `account` through the host's password database into the proof a
/// root effect on an account's uid requires.
///
/// The one place the login area decides what a hosting account is; the rule
/// itself lives in [`ManagedAccount`] and is not repeated here. It reads the
/// rows through the host seam and asks the type, so a fake host drives it
/// without a file.
///
/// Call it inside the account lock and use the result only there: the proof is
/// a fact about one moment (see [`ManagedAccount`]).
///
/// # Errors
///
/// - [`LoginsError::AccountMissing`] when the database cannot be read, or holds
///   no single row of that name.
/// - [`LoginsError::AccountNotManaged`] for every other refusal of the type: a
///   reserved name, a system identity, a home that is not the managed one, and
///   any refusal added later. They share one variant so the message cannot
///   tell a prober which rule fired.
pub(crate) fn resolve_managed_account(
    host: &dyn LoginsHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<ManagedAccount, LoginsError> {
    let rows = host.read_passwd(distro.passwd_database())?;

    ManagedAccount::resolve_rows(account.clone(), &rows).map_err(|refusal| match refusal {
        ManagedAccountError::NotFound => LoginsError::AccountMissing,
        _ => LoginsError::AccountNotManaged,
    })
}

#[cfg(test)]
#[path = "../tests/logins/resolve_managed_account_tests.rs"]
mod tests;
