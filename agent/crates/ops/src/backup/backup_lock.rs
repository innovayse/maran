//! The per-account lock every backup operation runs under.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, PoisonError};

use maran_agent_core::validation::system::name::AccountName;
use tokio::sync::{Mutex as AsyncMutex, OwnedMutexGuard};

/// The registry of per-account locks.
///
/// A map and not one process-wide lock, because the unit of serialisation here
/// is the ACCOUNT, not the host: two customers' backups share nothing — not a
/// home, not a scratch directory, not an artifact name — and serialising them
/// against each other would make one customer's slow database everyone's
/// nightly window. The firewall's own lock is process-wide for the opposite
/// reason: there is one kernel ruleset and no tenant to scope it to.
///
/// The outer lock is a `std::sync::Mutex` because it is held for the length of
/// a map lookup and nothing else; the inner ones are `tokio::sync::Mutex`
/// because they are the thing an operation holds for as long as a backup takes.
///
/// Entries are never removed. The map is therefore bounded by the number of
/// accounts this host has ever backed up, at one `Arc` and one name each — a
/// few kilobytes on a full server. Removing an entry when its last guard drops
/// would need the removal and the next lookup to be one atomic step, and
/// getting that wrong hands two operations two different locks for one account,
/// which is precisely the failure the lock exists to prevent.
static ACCOUNT_LOCKS: Mutex<Option<HashMap<String, Arc<AsyncMutex<()>>>>> = Mutex::new(None);

/// Takes `account`'s backup lock without waiting, or answers `None` because
/// another operation holds it.
///
/// **Without waiting, and that is the design.** A second creation for an
/// account is refused with `BackupError::AlreadyRunning` rather than queued:
/// the panel retries after a timeout, and a call that waited would hold an RPC
/// open for as long as the first backup runs — which for a twenty-gigabyte
/// account is measured in hours, not seconds.
///
/// The guard is owned, so a caller keeps it in a local and the lock is released
/// when the operation returns — by any path, including every error path and a
/// panic. There is no unlock to forget.
///
/// A poisoned registry lock is recovered from rather than propagated: it can
/// only be poisoned by a panic while a map lookup was in progress, the map is
/// not left inconsistent by that (a `HashMap` insert either happened or did
/// not), and a root daemon that refused every backup for the rest of its life
/// over one unrelated panic would be a worse outcome than the one being
/// guarded against.
pub(crate) fn take_account_lock(account: &AccountName) -> Option<OwnedMutexGuard<()>> {
    let mut registry = ACCOUNT_LOCKS.lock().unwrap_or_else(PoisonError::into_inner);

    let locks = registry.get_or_insert_with(HashMap::new);
    let lock = Arc::clone(
        locks
            .entry(account.as_str().to_owned())
            .or_insert_with(|| Arc::new(AsyncMutex::new(()))),
    );

    // Dropped before the wait-free attempt below, so the registry is never held
    // across anything but its own lookup.
    drop(registry);

    lock.try_lock_owned().ok()
}

#[cfg(test)]
#[path = "../tests/backup/backup_lock_tests.rs"]
mod tests;
