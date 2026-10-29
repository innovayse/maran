//! One way an account can fail to be a hosting account, for the cron refusal tests.

use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::recording_cron_host::RecordingCronHost;

/// One way an account can fail to be a hosting account.
pub(crate) struct UnmanagedCase {
    /// What makes this account unmanaged, for the failure message.
    pub(crate) label: &'static str,
    /// The name the operation is asked to act on.
    pub(crate) account: AccountName,
    /// A host that holds a crontab for it and answers as the case says.
    pub(crate) host: RecordingCronHost,
    /// The refusal the type must give.
    pub(crate) expected: &'static ManagedAccountError,
}
