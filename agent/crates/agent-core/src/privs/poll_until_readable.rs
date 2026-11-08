//! Waiting, with a deadline, for a channel from the child to have something to read.

use std::io;
use std::os::fd::{AsRawFd, BorrowedFd};
use std::time::Instant;

/// Waits until `channel` can be read without blocking, or `deadline` passes.
///
/// Returns `true` when a read would not block — which includes the far end having closed, because
/// a read then answers end-of-file at once — and `false` when the deadline came first.
///
/// This is what lets the parent wait for a child's data and for the child's patience in one place.
/// A bare blocking `read` or `recvmsg` would sit for as long as the child chose to keep its end
/// open and say nothing, and the bound this module exists to keep (`CHILD_PATIENCE`) would be a
/// promise nobody enforced for the one rpc family whose child is expected to talk.
///
/// Restarted on `EINTR` with the time that is LEFT and not the original timeout: the agent
/// registers signal handlers, so a signal landing during the wait is a path this daemon takes, and
/// restarting with the full timeout would let a steady stream of them extend the deadline forever.
///
/// # Errors
///
/// The operating system's own error when `poll` fails for any reason other than being
/// interrupted, and `EBADF` when the descriptor is not open — reported by `poll` in the events
/// rather than in its return value, so it is checked for here.
pub(super) fn poll_until_readable(channel: BorrowedFd<'_>, deadline: Instant) -> io::Result<bool> {
    loop {
        let remaining = deadline.saturating_duration_since(Instant::now());
        // Rounded UP to a whole millisecond: rounding down would turn a 900-microsecond
        // remainder into a zero-timeout poll that returns "not ready" and is called again, a
        // busy loop for the last millisecond of every wait that times out.
        let millis = i32::try_from(remaining.as_micros().div_ceil(1000)).unwrap_or(i32::MAX);

        let mut watched = libc::pollfd {
            fd: channel.as_raw_fd(),
            events: libc::POLLIN,
            revents: 0,
        };

        // SAFETY: `watched` is a live, writable `pollfd` and the count passed is exactly 1, which
        // is what `poll` reads and writes. `channel` is a borrowed, open descriptor for the whole
        // call, so the number cannot name a descriptor closed and reused by another thread.
        let ready = unsafe { libc::poll(&raw mut watched, 1, millis) };

        if ready > 0 {
            if watched.revents & libc::POLLNVAL != 0 {
                return Err(io::Error::from_raw_os_error(libc::EBADF));
            }
            return Ok(true);
        }
        if ready == 0 {
            return Ok(false);
        }

        let error = io::Error::last_os_error();
        if error.raw_os_error() != Some(libc::EINTR) {
            return Err(error);
        }
    }
}
