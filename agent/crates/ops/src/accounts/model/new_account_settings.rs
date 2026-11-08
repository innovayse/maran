//! What an account is created with beyond its name.

use maran_agent_core::validation::system::inode_limit::InodeLimit;

use crate::accounts::model::login_shell::LoginShell;
use crate::accounts::model::resource_ceiling::ResourceCeiling;

/// The settings one account is created with: its disk quota, its inode quota
/// and its login shell.
///
/// One carrier rather than three positional parameters, because `create` takes a
/// byte count, an inode ceiling and a shell, and two of the three have a shape a
/// caller could swap without the compiler noticing in the old signature. The
/// fields are public: every one of them is already a validated type or a plain
/// byte count the quota tool bounds, so there is no invalid combination for a
/// constructor to refuse.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NewAccountSettings {
    /// The byte quota applied at creation. `0` removes the limit, which is what
    /// `setquota` means by zero.
    pub quota_bytes: u64,
    /// The most files and directories the account may own.
    pub inode_limit: ResourceCeiling<InodeLimit>,
    /// The login shell the account is created with.
    pub shell: LoginShell,
}

impl NewAccountSettings {
    /// The settings a caller written before the inode limit and the shell
    /// existed asks for: only a byte quota, no inode limit, no login.
    ///
    /// Named so that "an old caller" is one definition rather than a literal
    /// repeated at every call site that has no use for the new fields.
    #[must_use]
    pub fn with_quota(quota_bytes: u64) -> Self {
        Self {
            quota_bytes,
            inode_limit: ResourceCeiling::Unlimited,
            shell: LoginShell::Nologin,
        }
    }
}
