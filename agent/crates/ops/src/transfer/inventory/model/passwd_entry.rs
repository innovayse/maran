//! One account of the source's password database that may be a hosting account.

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::name::AccountName;

/// An account the password database lists, with its name and home validated.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PasswdEntry {
    /// The login name, as the ordinary account-name validator accepts it.
    pub name: AccountName,
    /// The home directory; a path on the SOURCE, only ever handed back to it.
    pub home: RemotePath,
}
