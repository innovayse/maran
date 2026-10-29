//! Looking at whether a child has exited without collecting it.

use std::process::Child;

use rustix::io::Errno;
use rustix::process::{Pid, WaitId, WaitIdOptions, waitid};

/// Whether `child` has exited, WITHOUT reaping it.
///
/// The child stays a zombie until the caller collects it, and that is the point:
/// while it is a zombie its pid, and therefore its process group id, cannot be
/// given to anybody else. A group kill sent after the reap would be aimed at a
/// number the kernel may already have handed to an unrelated group, as root. So
/// the order everywhere in the core is: observe the exit here, signal the group,
/// and only then reap.
///
/// An interrupted call (`EINTR`) answers "not yet": the caller looks again after
/// its next nap.
///
/// # Errors
///
/// Returns the operating system's error when the child cannot be looked at, for
/// example when something else already collected it.
pub(crate) fn has_exited(child: &Child) -> std::io::Result<bool> {
    let pid = Pid::from_child(child);
    let options = WaitIdOptions::EXITED | WaitIdOptions::NOHANG | WaitIdOptions::NOWAIT;

    match waitid(WaitId::Pid(pid), options) {
        Ok(status) => Ok(status.is_some()),
        Err(error) if error == Errno::INTR => Ok(false),
        Err(error) => Err(std::io::Error::from(error)),
    }
}

#[cfg(test)]
#[path = "../tests/utils/has_exited_tests.rs"]
mod tests;
