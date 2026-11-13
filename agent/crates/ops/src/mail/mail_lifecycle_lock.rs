//! The one lock that serialises the mail stack's start, stop and address refresh.

use std::sync::{Mutex, MutexGuard, PoisonError};

/// The lock itself: one per process, and a process is one host's whole agent.
static LIFECYCLE: Mutex<()> = Mutex::new(());

/// Takes the lock every operation that starts or stops the mail daemons, or reloads
/// Postfix on the strength of whether it is running, runs under.
///
/// # Why it exists
///
/// `refresh_self_clients` asks whether Postfix is running and then reloads it; a
/// `disable_mail` between the two stops the server, and a reload that starts a stopped
/// unit would put port 25 and 587 back on the network while mail is off (rules/security.md
/// item 10(8), "While mail is off nothing listens"). `enable_mail` and `disable_mail`
/// take this lock for their whole run, `refresh_self_clients` for its check, write and
/// reload, so the three cannot interleave. Two enables, or an enable and a disable, were
/// not serialised before either: this is also what makes the lifecycle one operation at a
/// time.
///
/// # Order
///
/// Taken OUTSIDE `config_tree_lock` (which the config-write protocol takes inside every
/// write), always in that order and never the reverse, so the two cannot deadlock. A
/// holder that panicked left the daemons as the protocol left them, so a poisoned lock is
/// still safe to take.
///
/// # Calling this
///
/// A blocking `std` lock: invoke the operations that take it from `spawn_blocking`, as
/// every mail operation is.
pub(crate) fn hold_mail_lifecycle() -> MutexGuard<'static, ()> {
    LIFECYCLE.lock().unwrap_or_else(PoisonError::into_inner)
}

/// Whether some thread holds the lifecycle lock right now. A test double calls this from inside a host
/// method to learn whether the operation that reached it holds the lock, which is how a test asserts "this
/// operation runs under the lifecycle lock" without a thread, a sleep or a race.
#[cfg(test)]
pub(crate) fn mail_lifecycle_is_held() -> bool {
    matches!(
        LIFECYCLE.try_lock(),
        Err(std::sync::TryLockError::WouldBlock)
    )
}
