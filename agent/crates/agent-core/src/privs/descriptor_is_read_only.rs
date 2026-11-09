//! Whether an open descriptor can only be read.

use std::io;
use std::os::fd::{AsRawFd, BorrowedFd};

/// Whether `descriptor` was opened `O_RDONLY`, as the kernel records it.
///
/// Asked of a descriptor received from the dropped child, because what the child opened is the
/// child's claim: a hand-off that exists so the root parent can READ a customer's file must not
/// accept a descriptor that could also write one. The access mode is read from the open file
/// description itself (`F_GETFL`), the only place it lives; nothing the child says about it is
/// consulted.
///
/// # Errors
///
/// The operating system's own error when `fcntl` fails, for example `EBADF` for a descriptor that
/// is not open.
pub(super) fn descriptor_is_read_only(descriptor: BorrowedFd<'_>) -> io::Result<bool> {
    // SAFETY: `fcntl` with `F_GETFL` takes an integer descriptor and no argument, and touches no
    // memory. `descriptor` is borrowed and open for the whole call.
    let flags = unsafe { libc::fcntl(descriptor.as_raw_fd(), libc::F_GETFL) };
    if flags < 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(flags & libc::O_ACCMODE == libc::O_RDONLY)
}
