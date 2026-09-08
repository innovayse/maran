//! One SFTP login, and whether the host will still let it in.

use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

/// What one `<account>_<name>` passwd entry can be observed to be right now.
///
/// Observed per login and never inferred from the account's own state. That
/// inference is precisely the defect this type exists to detect: an SFTP login
/// is a separate passwd entry sharing the account's uid, so
/// `usermod --lock <account>` leaves every one of them authenticating, and a
/// suspended customer kept a working WRITE credential into their home for as
/// long as nobody looked.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SftpLoginSuspensionFact {
    /// The login's full system name, as the password database spells it.
    pub user: SftpUserName,

    /// True when `passwd -S` reports that entry's password as locked.
    ///
    /// The password database's own answer about that exact entry. It is not
    /// read out of `/etc/shadow`: one letter is what is wanted, and no hash
    /// needs to enter this process to get it.
    pub locked: bool,
}
