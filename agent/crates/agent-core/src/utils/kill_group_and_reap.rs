//! Ending a child's whole process group and collecting its leader.

use std::process::Child;
use std::time::Duration;

use rustix::process::{Pid, Signal, kill_process_group};

use crate::utils::deadline::Deadline;
use crate::utils::poll_interval::PollInterval;

/// How long the core waits for a killed leader to become collectable.
///
/// A ceiling of its own, because the kill and the reap are two mechanisms and
/// only one of them is argued by a comment: `SIGKILL` cannot be caught, but
/// nothing enforces that this is the signal sent, and a process in
/// uninterruptible sleep does not die until its I/O returns. Five seconds is not
/// a budget for the kernel to tear a process down, which takes microseconds; it
/// is the point past which the child is not dying at all.
pub(crate) const REAP_CEILING: Duration = Duration::from_secs(5);

/// Sends `SIGKILL` to the whole process group `child` leads, then collects the
/// leader, waiting at most [`REAP_CEILING`].
///
/// **Signal first, reap after.** The group is signalled while the leader is
/// still uncollected, so the group id is still the child's and cannot have been
/// recycled (see `has_exited`). The leader is a child of this process and is
/// never reaped by anyone but this function and the success path of the core.
///
/// The group kill is complete for descendants that stayed in the group. A
/// descendant that called `setsid` or `setpgid` for itself is outside it, and
/// only the unit's control group contains that when the agent stops.
///
/// The result of the signal is not examined: `ESRCH` means every member is
/// already gone, which is the goal. The leader is also signalled by pid as a
/// second statement of the same intent, in case the group could not be addressed.
///
/// Returns `true` when the leader was collected. `false` means it was still not
/// collectable after [`REAP_CEILING`] (or the wait itself failed); it is then
/// left uncollected, one zombie until the agent exits, and the caller reports
/// the bound that caused the kill all the same.
pub(crate) fn kill_group_and_reap(child: &mut Child) -> bool {
    let group = Pid::from_child(child);
    let _ = kill_process_group(group, Signal::KILL);
    let _ = child.kill();

    let patience = Deadline::after(REAP_CEILING);
    let mut interval = PollInterval::new();

    loop {
        match child.try_wait() {
            Ok(Some(_)) => return true,
            Ok(None) => {}
            Err(_) => return false,
        }

        if patience.has_passed() {
            return false;
        }

        std::thread::sleep(interval.next_nap());
    }
}

#[cfg(test)]
#[path = "../tests/utils/kill_group_and_reap_tests.rs"]
mod tests;
