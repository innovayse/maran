//! Every FTPS resource one account owns, taken away together.

use std::path::Path;

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::ftps::delete_ftps_user::delete_ftps_user_under_lock;
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::ftps_jail::FtpsJail;

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

/// Removes every FTPS login `account` has, then its jail and the mount unit
/// that filled it.
///
/// # Why the account's FTPS resources are removed as a set
///
/// An account may hold several FTPS logins, and a jail is shared by all of them
/// — which is why [`delete_ftps_user`](crate::ftps::delete_ftps_user) deliberately unmounts nothing. Taking the
/// jail down belongs to the moment the ACCOUNT goes, and that is here.
///
/// The logins come from the HOST's own password database rather than from a list
/// the panel supplies. The panel's rows are what a customer's own delete is
/// authorised by; they are not a guarantee of what is on the machine, and a
/// cascade that trusted them would leave behind exactly the logins the panel had
/// forgotten — live credentials into a home that is about to be handed to
/// whoever gets this account name next.
///
/// # This is the FTPS half only, and that separation is the point
///
/// The enumeration is filtered by the FTPS jail, so an SFTP login of the same
/// account — same uid, same name shape, home in the SFTP jail — is invisible to
/// it, and `sftp::remove_account_sftp` is equally blind to this one. Neither can
/// unmount the other's jail: the two `.mount` units have different `Where=`
/// values, different escaped names and different file paths, all derived from
/// two different roots.
///
/// # The order, which is the whole of the risk
///
/// 1. **The logins**, which are the passwd entries whose home is this account's
///    FTPS jail. They share the account's uid, and `userdel` on the account
///    itself refuses to remove a home directory another passwd entry still
///    claims. Revoking them first also means no session can be opened against a
///    jail that is about to be dismantled.
/// 2. **The unmount**, through the unit that made the mount. It is not
///    best-effort: a bind mount surviving this operation is a mount of a home
///    that `userdel` is about to delete, into a jail nothing owns any more, and
///    the uninstaller refuses to remove the agent's state directories while any
///    mount remains under them.
/// 3. **The jail directories**, removed with a plain directory removal that
///    refuses a directory which is not empty. That refusal is the safety
///    property, not a limitation: the mount point still holds the account's real
///    home if step 2 did not take effect, so a recursive removal here would
///    delete the customer's entire website. An unremovable directory therefore
///    stops the deletion instead, with the account still present and
///    recoverable. `remove_dir`, never `remove_dir_all`.
/// 4. **The unit file**, and a `daemon-reload` so the service manager forgets
///    it. A unit file left behind naming a `Where=` that no longer exists is a
///    failing unit on the next boot, and — worse — the unit a re-created account
///    of the same name would inherit rather than being given a fresh one.
///
/// # Idempotency
///
/// An account with no FTPS logins, no jail and no unit is success and touches
/// nothing — which is the common case, since FTPS is off on a host that never
/// enabled it. A login that vanished between the listing and its removal is
/// [`FtpsError::NotFound`], which is the answer a second deletion converges on
/// and is not an error here. Both are what make a retry after a lost response
/// safe, and both are why this step can be added to the account-deletion cascade
/// without changing what a deletion does on a host that has no FTPS at all.
///
/// # No lock is taken here
///
/// Its one caller is `accounts::AccountOperations::delete`, which already holds
/// the account's lock for the whole cascade. That lock **never waits** — it
/// refuses — so a second acquisition inside the sequence would not deadlock, it
/// would make the deletion refuse itself. Taking it here would be a bug, not
/// defence in depth (rules/rust.md, "What this agent serialises").
///
/// # Errors
///
/// - [`FtpsError::JailFailed`] when the unit cannot be stopped, a jail directory
///   cannot be removed — which is what a mount that is still in place looks
///   like — or the unit file cannot be taken away.
/// - [`FtpsError::AccountMissing`] when the host's password database cannot be
///   read at all, so the logins could not be enumerated. An account with no
///   logins is not that.
/// - [`FtpsError::SpawnFailed`] when `userdel` refuses a login for a reason
///   other than the login being absent, or could not be run at all.
pub fn remove_account_ftps(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<(), FtpsError> {
    let jail = FtpsJail::for_account(account, distro.systemd_unit_directory());

    for user in host.account_logins(distro.passwd_database(), account, jail.directory())? {
        match delete_ftps_user_under_lock(host, distro, &user) {
            // The listing and the removal are two operations with a gap between
            // them, and a login removed inside that gap is the state this
            // function wanted anyway.
            Ok(()) | Err(FtpsError::NotFound) => {}
            Err(error) => return Err(error),
        }
    }

    remove_jail(host, distro, &jail)
}

/// Stops `jail`'s mount, then removes its directories and its unit file.
///
/// # Errors
///
/// Returns [`FtpsError::JailFailed`] when the service manager refuses to stop
/// the unit, when a directory will not come away — the shape a surviving mount
/// takes — or when the unit file cannot be removed.
fn remove_jail(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    jail: &FtpsJail,
) -> Result<(), FtpsError> {
    let unit_path = Path::new(jail.unit_path());

    // Asked before it is stopped, because `systemctl disable` refuses a unit it
    // has no file for — and an account whose FTPS jail was never built must not
    // fail its own deletion over a unit that was correctly never written. That
    // is the ordinary case on a host where FTPS was never enabled.
    let installed = host.path_exists(unit_path);
    if installed {
        let outcome = host.run(
            distro.service_manager(),
            &[DISABLE, STOP_NOW, jail.unit_name()],
        )?;
        if outcome.status != 0 {
            return Err(FtpsError::JailFailed);
        }
    }

    // The mount point first, then the jail that contains it: a directory removal
    // that refuses a non-empty directory cannot take the outer one while the
    // inner one is still there.
    host.remove_directory(Path::new(jail.mount_point()))?;
    host.remove_directory(Path::new(jail.directory()))?;

    if !installed {
        return Ok(());
    }

    host.remove_file(unit_path)?;

    let reloaded = host.run(distro.service_manager(), &[DAEMON_RELOAD])?;
    if reloaded.status != 0 {
        return Err(FtpsError::JailFailed);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/ftps/remove_account_ftps_tests.rs"]
mod tests;
