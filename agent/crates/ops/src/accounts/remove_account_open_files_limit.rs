//! Taking an account's `pam_limits` drop-in away when the account is deleted.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::managed_account::ManagedAccount;

use crate::accounts::{AccountError, SystemHost};

/// Removes `<limits dir>/maran-<account>.conf`, when it is there.
///
/// A step of the deletion sequence, run BEFORE `userdel` like every other
/// leftover: the file is named after the account, so one that outlived it would
/// carry the departed account's ceiling onto whoever `useradd` gives the name
/// next - the same recycled-name hazard the crontab, the mail spool and the SFTP
/// key files are removed for. Fails closed: a file that exists and cannot be
/// removed refuses the deletion with the account still present, so the same
/// deletion can be retried once whatever refused is fixed.
///
/// Idempotent, and a no-op that touches nothing on an account that never had a
/// ceiling: a file that is not there is success.
///
/// # Errors
///
/// [`AccountError::LimitsFileFailed`] when the file exists and could not be
/// removed.
pub(super) fn remove_account_open_files_limit<H: SystemHost>(
    host: &H,
    account: &ManagedAccount,
) -> Result<(), AccountError> {
    host.remove_limits_file(&AgentPaths::account_limits_file(account.name()))
}
