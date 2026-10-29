//! Ending an account's systemd user manager and lingering state before its uid is freed.

use std::time::Duration;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::DistroAdapter;

use crate::accounts::{AccountError, SystemHost};

/// Where systemd records that an account lingers: one empty file per user, named
/// after the user.
///
/// The same path on every systemd host, so it is a systemd fact and not a
/// distribution one; it is only ever statted, never opened.
const LINGER_DIRECTORY: &str = "/var/lib/systemd/linger";

/// The parent of the per-uid runtime directory systemd creates for a user manager.
///
/// The directory `/run/user/<uid>` exists exactly while the user's manager (or a
/// login session) does, which is why its disappearance is the proof of the end.
const RUNTIME_DIRECTORY: &str = "/run/user";

/// How many times the end of the user manager is looked for before giving up.
///
/// Bounded because the operation runs under the account's lock: a manager that
/// cannot be stopped must end in a refusal, not in an operation that never returns.
const POLLS: u32 = 10;

/// How long to wait between two looks at the runtime directory.
///
/// Spent through [`SystemHost::pause`], so no test waits it; ten looks hold the
/// lock for under two seconds of waiting.
const PAUSE: Duration = Duration::from_millis(200);

/// Turns off lingering for the account and stops its systemd user manager, so that
/// nothing still running under the uid outlives the deletion.
///
/// # Why
///
/// A lingering account keeps a `systemd --user` manager (and everything it
/// started) alive with no login session, and `/run/user/<uid>` with it. The
/// process cull ends the processes, but the lingering flag brings a manager back
/// and the runtime directory stays owned by a uid the next `useradd` may hand to
/// a new tenant.
///
/// # How
///
/// Gated on the filesystem, not on `loginctl`: when neither
/// `/var/lib/systemd/linger/<name>` nor `/run/user/<uid>` exists, nothing is run
/// at all (the common case, and the only possible one on a host without systemd).
/// A linger file is removed with `loginctl disable-linger <name>` and its absence
/// confirmed. A runtime directory is ended with `loginctl terminate-user <uid>`
/// and then polled, up to ten looks 200 ms apart, until it is gone.
///
/// # How the account is confined
///
/// The name and uid come only from the [`ManagedAccount`] the caller holds, which
/// refuses uid 0, ids below the distro's floor and foreign homes. The program is
/// `distro.loginctl_binary()`, every other argument is a constant or the name/uid
/// rendered with `Display`, and the host spawns an argv array: no shell.
///
/// # The lock
///
/// Does not take a lock; must be called with the account's lock held.
///
/// # Errors
///
/// - [`AccountError::HomeInspection`] when a state path could not be inspected.
/// - [`AccountError::CommandUnavailable`] / [`AccountError::Bound`] when `loginctl`
///   could not be started or outran its deadline.
/// - [`AccountError::CommandFailed`] when `loginctl` exited non-zero.
/// - [`AccountError::UserManagerRemains`] when the linger file or the runtime
///   directory is still present after the commands and every look. Nothing has been
///   deleted at that point.
pub(crate) fn end_account_user_manager(
    host: &dyn SystemHost,
    distro: &dyn DistroAdapter,
    account: &ManagedAccount,
) -> Result<(), AccountError> {
    let linger_path = format!("{LINGER_DIRECTORY}/{}", account.name().as_str());
    let runtime_path = format!("{RUNTIME_DIRECTORY}/{}", account.uid());

    let lingers = exists(host, &linger_path)?;
    let has_runtime = exists(host, &runtime_path)?;
    if !lingers && !has_runtime {
        return Ok(());
    }

    let loginctl = distro.loginctl_binary();

    if lingers {
        let outcome = host.run(
            &loginctl,
            &["disable-linger", account.name().as_str()],
            CallClass::Mutation,
        )?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(loginctl.as_str(), &outcome));
        }
        if exists(host, &linger_path)? {
            return Err(remains(account));
        }
    }

    if has_runtime {
        let uid = account.uid().to_string();
        let outcome = host.run(
            &loginctl,
            &["terminate-user", uid.as_str()],
            CallClass::Mutation,
        )?;
        if outcome.status != 0 {
            return Err(AccountError::command_failed(loginctl.as_str(), &outcome));
        }
        for look in 1..=POLLS {
            if !exists(host, &runtime_path)? {
                return Ok(());
            }
            if look < POLLS {
                host.pause(PAUSE);
            }
        }
        return Err(remains(account));
    }

    Ok(())
}

/// Reports whether anything (a symlink included, never followed) exists at `path`.
///
/// # Errors
///
/// Returns [`AccountError::HomeInspection`] when the path could not be inspected.
fn exists(host: &dyn SystemHost, path: &str) -> Result<bool, AccountError> {
    Ok(host.home_metadata(path)?.is_some())
}

/// The refusal for a user manager that survived, logged once here.
fn remains(account: &ManagedAccount) -> AccountError {
    tracing::warn!(
        username = account.name().as_str(),
        uid = account.uid(),
        "the user manager of the account survived; the deletion was stopped before userdel"
    );
    AccountError::UserManagerRemains {
        username: account.name().as_str().to_owned(),
    }
}

#[cfg(test)]
#[path = "../tests/accounts/end_account_user_manager_tests.rs"]
mod tests;
