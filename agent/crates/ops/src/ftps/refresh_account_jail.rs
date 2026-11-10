//! RefreshAccountJail: make an account's FTPS jail show the account's CURRENT
//! home (issue #487).

use std::path::Path;

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::ftps::ensure_account_jail::ensure_account_jail;
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::ftps_jail::FtpsJail;

/// The subcommand that stops a unit and starts it again.
const RESTART: &str = "restart";

/// Mounts `account`'s real home into its FTPS jail again, and answers whether
/// the account has a jail at all.
///
/// A bind mount follows the INODE it was made from, not the path. A whole-account
/// restore renames the home aside and a different directory into its place, so
/// the jail keeps showing the displaced tree: the customer sees their files gone
/// over FTPS, and everything they upload lands in a tree the restore is about to
/// delete. Restarting the mount unit binds the directory that is at
/// `/home/<account>` now.
///
/// The unit is first brought to its current render (a unit installed before the
/// template carried `LazyUnmount=yes` would fail to stop while a session is
/// inside it). An account that has never had an FTPS login has no unit and
/// nothing to refresh: that is `Ok(false)`, not an error, and it creates no jail.
///
/// Idempotent: restarting a unit that is already bound to the current home
/// changes nothing observable.
///
/// **The lock.** This takes none and must be called with the hosting account's
/// lock held (`restore_backup` holds it): the account lock never waits, so
/// taking it again here would refuse the restore its own refresh. That lock is
/// process-local.
///
/// # Errors
///
/// Returns [`FtpsError::JailFailed`] when the unit cannot be brought to its
/// render or the restart exits non-zero, and [`FtpsError::Bound`] when the
/// service manager outruns its deadline.
pub fn refresh_account_jail(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<bool, FtpsError> {
    let jail = FtpsJail::for_account(account, distro.systemd_unit_directory());
    if !host.path_exists(Path::new(jail.unit_path())) {
        return Ok(false);
    }

    ensure_account_jail(host, distro, &jail)?;

    // `systemctl` honours the end-of-options marker, so the unit name after it
    // can never be read as an option.
    let restarted = host.run(
        &distro.service_manager(),
        &[RESTART, END_OF_OPTIONS, jail.unit_name()],
        CallClass::Reload,
    )?;
    if restarted.status != 0 {
        return Err(FtpsError::JailFailed);
    }

    Ok(true)
}

#[cfg(test)]
#[path = "../tests/ftps/refresh_account_jail_tests.rs"]
mod tests;
