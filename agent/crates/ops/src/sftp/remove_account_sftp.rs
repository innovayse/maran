//! Every SFTP resource one account owns, taken away together.

use std::path::Path;

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::sftp::delete_sftp_user::delete_sftp_user;
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The subcommand that turns a unit off and stops it in the same call.
///
/// `disable` alone would only remove the boot-time symlink and leave the mount
/// in place until the next reboot — which is the state this operation exists to
/// make impossible.
const DISABLE: &str = "disable";

/// The flag that also stops the unit now, rather than at the next boot only.
const STOP_NOW: &str = "--now";

/// The subcommand that makes the service manager forget a unit file that has
/// been removed.
const DAEMON_RELOAD: &str = "daemon-reload";

/// Removes every SFTP login `account` has, then its jail and the mount unit
/// that filled it.
///
/// # Why the account's SFTP resources are removed as a set
///
/// An account may hold several logins, and a jail is shared by all of them —
/// which is why [`delete_sftp_user`] deliberately unmounts nothing. Taking the
/// jail down belongs to the moment the ACCOUNT goes, and that is here.
///
/// The logins come from the HOST's own password database rather than from a
/// list the panel supplies. The panel's rows are what a customer's own delete is
/// authorised by; they are not a guarantee of what is on the machine, and a
/// cascade that trusted them would leave behind exactly the logins the panel had
/// forgotten — live credentials into a home that is about to be handed to
/// whoever gets this account name next.
///
/// # The order, which is the whole of the risk
///
/// 1. **The logins**, which are the passwd entries whose home is this account's
///    jail. They share the account's uid, and `userdel` on the
///    account itself refuses to remove a home directory another passwd entry
///    still claims. Revoking them first also means no session can be opened
///    against a jail that is about to be dismantled.
/// 2. **The unmount**, through the unit that made the mount. It is not
///    best-effort: a bind mount surviving this operation is a mount of a home
///    that `userdel` is about to delete, into a jail nothing owns any more, and
///    the uninstaller refuses to remove `/var/lib/maran` while any mount remains
///    under it.
/// 3. **The jail directories**, removed with a plain directory removal that
///    refuses a directory which is not empty. That refusal is the safety
///    property, not a limitation: the mount point still holds the account's real
///    home if step 2 did not take effect, so a recursive removal here would
///    delete the customer's entire website. An unremovable directory therefore
///    stops the deletion instead, with the account still present and
///    recoverable.
/// 4. **The unit file**, and a `daemon-reload` so the service manager forgets
///    it. A unit file left behind naming a `Where=` that no longer exists is a
///    failing unit on the next boot, and — worse — the unit a re-created account
///    of the same name would inherit rather than being given a fresh one.
///
/// # Idempotency
///
/// An account with no logins, no jail and no unit is success and touches
/// nothing. A login that vanished between the listing and its removal is
/// [`SftpError::NotFound`], which is the answer a second deletion converges on
/// and is not an error here. Both are what make a retry after a lost response
/// safe.
///
/// # Errors
///
/// - [`SftpError::JailFailed`] when the unit cannot be stopped, a jail
///   directory cannot be removed — which is what a mount that is still in place
///   looks like — or the unit file cannot be taken away.
/// - [`SftpError::SpawnFailed`] when `userdel` refuses a login for a reason
///   other than the login being absent, or could not be run at all.
pub fn remove_account_sftp(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<(), SftpError> {
    let jail = AccountJail::for_account(account, distro.systemd_unit_directory());

    for user in host.account_logins(distro.passwd_database(), account, jail.directory())? {
        match delete_sftp_user(host, distro, &user) {
            // The listing and the removal are two operations with a gap between
            // them, and a login removed inside that gap is the state this
            // function wanted anyway.
            Ok(()) | Err(SftpError::NotFound) => {}
            Err(error) => return Err(error),
        }
    }

    remove_jail(host, distro, &jail)
}

/// Stops `jail`'s mount, then removes its directories and its unit file.
///
/// # Errors
///
/// Returns [`SftpError::JailFailed`] when the service manager refuses to stop
/// the unit, when a directory will not come away — the shape a surviving mount
/// takes — or when the unit file cannot be removed.
fn remove_jail(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    jail: &AccountJail,
) -> Result<(), SftpError> {
    let unit_path = Path::new(jail.unit_path());

    // Asked before it is stopped, because `systemctl disable` refuses a unit it
    // has no file for — and an account whose jail was never built must not fail
    // its own deletion over a unit that was correctly never written.
    let installed = host.path_exists(unit_path);
    if installed {
        let outcome = host.run(
            distro.service_manager(),
            &[DISABLE, STOP_NOW, jail.unit_name()],
            None,
        )?;
        if outcome.status != 0 {
            return Err(SftpError::JailFailed);
        }
    }

    // The mount point first, then the jail that contains it: a directory
    // removal that refuses a non-empty directory cannot take the outer one
    // while the inner one is still there.
    host.remove_directory(Path::new(jail.mount_point()))?;
    host.remove_directory(Path::new(jail.directory()))?;

    if !installed {
        return Ok(());
    }

    host.remove_file(unit_path)?;

    let reloaded = host.run(distro.service_manager(), &[DAEMON_RELOAD], None)?;
    if reloaded.status != 0 {
        return Err(SftpError::JailFailed);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/sftp/remove_account_sftp_tests.rs"]
mod tests;
