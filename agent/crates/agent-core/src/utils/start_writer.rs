//! Delivering a child's input from a thread of its own.

use std::io::Write;
use std::process::ChildStdin;
use std::sync::mpsc::Sender;

use crate::utils::child_event::ChildEvent;

/// Starts a thread that writes `bytes` to `pipe`, closes it, and reports on
/// `events` exactly once: delivered, or failed. The waiting side decides what a failure
/// means: a broken pipe from a child that has since exited is not, by itself, an
/// input error.
///
/// A thread and not a write on the waiting thread, because a child that does not
/// read leaves the write blocked once the pipe's buffer is full, and the waiting
/// thread is the one that enforces the deadline. When the deadline kills the
/// group the pipe's read end closes and this write fails, so the thread ends by
/// itself.
///
/// The pipe is closed as soon as the bytes are written: a tool that reads to
/// end-of-input (`chpasswd`) waits forever on a pipe left open. The thread owns a
/// copy of the bytes, since the caller's slice does not outlive the call, and
/// overwrites that copy with zeros once the write is over. That is best effort
/// (the allocator may have moved the buffer while it was a `Vec`), and it is
/// stated as such: the copy is a credential for every caller that uses this.
///
/// # Errors
///
/// Returns the `io::Error` of failing to start the thread.
pub(crate) fn start_writer(
    mut pipe: ChildStdin,
    mut bytes: Vec<u8>,
    events: Sender<ChildEvent>,
) -> std::io::Result<()> {
    std::thread::Builder::new()
        .name("maran-spawn-writer".to_owned())
        .spawn(move || {
            let written = pipe.write_all(&bytes);
            bytes.fill(0);
            drop(pipe);

            let _ = events.send(if written.is_err() {
                ChildEvent::InputFailed
            } else {
                ChildEvent::InputDelivered
            });
        })
        .map(drop)
}

#[cfg(test)]
#[path = "../tests/utils/start_writer_tests.rs"]
mod tests;
