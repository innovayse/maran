//! Everything `CreateSftpUser` needs, already validated.

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

/// The account the login belongs to, the login itself, and its password.
///
/// Every field is a validated type and none of them is a `String`, which is the
/// area's whole injection defence:
///
/// - [`AccountName`] is what the jail's paths and the mount unit's name are
///   built from. A `&str` here would put a caller-supplied path segment into a
///   root-owned directory tree and into a systemd unit file name.
/// - [`SftpUserName`] can only be built by `for_account`, which applies the
///   `<account>_<name>` prefix and restricts the requested half to `[a-z0-9]`.
///   There is no constructor for an unprefixed name, so a login naming another
///   tenant cannot arrive here at all — the service rebuilds the name from the
///   account the panel authorised.
/// - [`Password`] can only hold letters, digits and `-_.=+`. The colon and the
///   newline it refuses are exactly the two characters that would let a value
///   break out of the `user:password` line `chpasswd` reads, which is how a
///   customer would otherwise set a password for a login that is not theirs.
///   It prints itself as `<password>`, so the `#[derive(Debug)]` on this struct
///   is safe to reach a tracing field.
///
/// There is no home or chroot field, and that absence is a security property
/// rather than an omission: the jail is derived from `account`, so no request
/// can name the directory it will be chrooted into.
#[derive(Debug, Clone)]
pub struct SftpUserRequest {
    /// The hosting account the login belongs to, and whose home it reaches.
    pub account: AccountName,
    /// The system login to create, prefixed with that account.
    pub user: SftpUserName,
    /// The password the login is created with.
    ///
    /// Supplied by the caller and never generated here: the panel is the single
    /// place a password is minted and stored, so the agent has nothing to keep
    /// and nothing to leak.
    pub password: Password,
}
