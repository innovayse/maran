//! SFTP logins: system accounts served by the host's OpenSSH daemon, each one
//! chrooted into a root-owned jail that its account's real home is
//! bind-mounted into.
//!
//! Three things shape everything in this area.
//!
//! **SFTP is OpenSSH, and nothing else is installed.** There is no FTP daemon,
//! no FTPS and no second listening port: an SFTP user is a system account in
//! the group sshd's `Match Group` block names, and that block — written once by
//! the installer, with `ChrootDirectory %h` and `ForceCommand internal-sftp` —
//! is what turns the account into a file transfer login rather than a shell.
//!
//! **The jail exists so the account's home never has to change.** OpenSSH
//! refuses to chroot into a directory that is not root-owned or is group- or
//! world-writable, and an account's home is `<account>:<web server group> 0750`
//! — an ownership that sites, nginx and php-fpm all depend on. So the chroot is
//! not the home: it is `/var/lib/maran/sftp/<account>`, root-owned `0755`, with
//! the real home bind-mounted at `home` inside it. The login lands in the jail,
//! enters `home`, and is in its own files with their own permissions. Nothing
//! about the home, the document root or the vhost changes, and cross-tenant
//! isolation is exactly what it was.
//!
//! **The login IS the account, numerically.** It is created with the account's
//! own uid and gid rather than an identity of its own, which is the other half
//! of leaving the home alone: a home of `<account>:<web server group> 0750`
//! gives a separate identity nothing at all, so a login with one would land in
//! its jail, see the home mounted inside it, and be refused every file in it.
//! Sharing the ids also makes an uploaded file come out owned by the account,
//! exactly as one the account created itself. See
//! [`AccountOwnership`] for why the two alternatives — widening the home, or
//! joining the web server's group — are both worse.
//!
//! There is **no caller-supplied chroot path** in this area and no path
//! resolution to get wrong: the jail is derived from a validated `AccountName`,
//! so the whole chroot-escape class is gone by construction rather than by a
//! check that has to be right every time.
//!
//! **The mount is declarative.** It is a systemd `.mount` unit, enabled, not a
//! `mount` call — an imperative mount is gone at the next boot, and every SFTP
//! login for that account would then land in an empty jail. The unit is written
//! through the same config-write protocol every other configuration goes
//! through, so a unit that will not load restores the previous one instead of
//! leaving a half-installed mount. Removing the jail and its unit belongs to the
//! account-deletion cascade, which is where the account's other host resources
//! are removed; deleting one login must not unmount an account that has others.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`SftpHost`]), one file that really touches the machine
//! ([`ProcessSftpHost`]), one error enum ([`SftpError`]) that structurally
//! cannot carry a tool's output, and `model/` for the typed input and the
//! derived jail paths.

mod create_sftp_user;
mod delete_sftp_user;
#[cfg(test)]
#[path = "../tests/sftp/fake_sftp_host.rs"]
pub(crate) mod fake_sftp_host;
pub mod model;
mod process_sftp_host;
mod remove_account_sftp;
mod set_sftp_password;
mod sftp_error;
mod sftp_host;

pub use create_sftp_user::create_sftp_user;
pub use delete_sftp_user::delete_sftp_user;
pub use model::account_jail::AccountJail;
pub use model::account_ownership::AccountOwnership;
pub use model::sftp_user_request::SftpUserRequest;
pub use process_sftp_host::ProcessSftpHost;
pub use remove_account_sftp::remove_account_sftp;
pub use set_sftp_password::set_sftp_password;
pub use sftp_error::SftpError;
pub use sftp_host::SftpHost;
