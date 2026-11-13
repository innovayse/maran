//! The one-at-a-time rule for operations on the Node.js module.

use std::sync::{MutexGuard, TryLockError};

use crate::apps::{AppsHost, AppsOpError};

/// Takes the host's lock on the `nodejs` module without waiting, or refuses.
///
/// A host keeps ONE Node.js module stream, and the install and the removal each read the host
/// (is another major there?) and then act on what they read: two of them in flight would both
/// pass the check and the second would switch the stream under the first one's success. The lock
/// is taken as the first statement of both, so the check and the unit it authorises are one
/// critical section, and it is held until the guard drops.
///
/// It REFUSES and never waits (rules/rust.md, "What this agent serialises"): the work that holds
/// it runs for minutes, and a wait that long is a stalled panel. It is process-local, as every
/// lock of this agent is: it serialises this agent against itself, not against an operator's own
/// `dnf` or a second agent. A poisoned lock is taken over, because the lock guards no data.
///
/// # Errors
///
/// Returns [`AppsOpError::NodejsOperationInProgress`] while another Node.js module operation
/// holds it.
pub(crate) fn take_nodejs_module_operation<H: AppsHost>(
    host: &H,
) -> Result<MutexGuard<'_, ()>, AppsOpError> {
    match host.app_operation_lock().try_lock() {
        Ok(guard) => Ok(guard),
        Err(TryLockError::Poisoned(poisoned)) => Ok(poisoned.into_inner()),
        Err(TryLockError::WouldBlock) => Err(AppsOpError::NodejsOperationInProgress),
    }
}
