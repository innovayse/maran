//! Passing one open file descriptor to the other end of a unix socket.

use std::io;
use std::os::fd::{AsRawFd, BorrowedFd, RawFd};

/// The single data byte that carries the descriptor.
///
/// A stream socket delivers ancillary data only attached to at least one byte of ordinary data, so
/// the descriptor needs a vehicle. Its value means nothing and the receiver reads none into
/// meaning: only that exactly one byte arrived with exactly one descriptor.
const VEHICLE: u8 = 0;

/// Words of control buffer, as `u64` so the buffer is aligned for a `cmsghdr` without a cast that
/// hides the alignment. Sixty-four bytes, of which one `SCM_RIGHTS` message for one descriptor
/// uses twenty-four.
const CONTROL_WORDS: usize = 8;

/// Sends `descriptor` to the peer of `socket` as `SCM_RIGHTS`, with one data byte to carry it.
///
/// Called in the forked child by
/// [`fork_as_account_for_descriptor`](super::fork_as_account_for_descriptor::fork_as_account_for_descriptor),
/// after the work closure has opened the file as the account. Exactly one descriptor is sent, in
/// exactly one message, because the receiver refuses anything else; there is no entry point that
/// sends a list, so the count cannot be a parameter a caller gets wrong.
///
/// `MSG_NOSIGNAL` so a parent that has gone away produces `EPIPE` and not a signal.
///
/// Runs in the child, so it is allocation-free: both buffers are on the stack.
///
/// # Errors
///
/// The operating system's own error when `sendmsg` fails, and `WriteZero` when it accepted
/// something other than the one byte.
pub(super) fn send_descriptor(
    socket: BorrowedFd<'_>,
    descriptor: BorrowedFd<'_>,
) -> io::Result<()> {
    let mut vehicle = [VEHICLE];
    let mut chunk = libc::iovec {
        iov_base: vehicle.as_mut_ptr().cast(),
        iov_len: vehicle.len(),
    };
    let mut control = [0_u64; CONTROL_WORDS];

    // SAFETY: `msghdr` is plain data made of integers and raw pointers, for which all-zero bytes
    // are a valid value (null pointers, zero lengths). Every field the kernel reads is set below.
    let mut message: libc::msghdr = unsafe { std::mem::zeroed() };
    message.msg_iov = &raw mut chunk;
    message.msg_iovlen = 1;
    message.msg_control = control.as_mut_ptr().cast();
    // SAFETY: `CMSG_SPACE` is pure arithmetic on its argument; it is `unsafe` only because it
    // sits beside the macros that dereference.
    message.msg_controllen = unsafe { libc::CMSG_SPACE(size_of::<RawFd>() as libc::c_uint) } as _;

    // SAFETY: `message` describes `control`, which is live, aligned for `cmsghdr` and large
    // enough for one `SCM_RIGHTS` header, so the first header is non-null and lies inside it.
    let header = unsafe { libc::CMSG_FIRSTHDR(&raw const message) };
    if header.is_null() {
        return Err(io::Error::from_raw_os_error(libc::EINVAL));
    }

    // SAFETY: `header` is non-null and points at a zeroed header inside `control`; its data area
    // is `CMSG_LEN(size_of::<RawFd>())` bytes in, still inside the buffer, and is written with
    // an unaligned store so no alignment is assumed of the data pointer.
    unsafe {
        (*header).cmsg_level = libc::SOL_SOCKET;
        (*header).cmsg_type = libc::SCM_RIGHTS;
        (*header).cmsg_len = libc::CMSG_LEN(size_of::<RawFd>() as libc::c_uint) as _;
        libc::CMSG_DATA(header)
            .cast::<RawFd>()
            .write_unaligned(descriptor.as_raw_fd());
    }

    loop {
        // SAFETY: `message` and everything it points at (`chunk`, `vehicle`, `control`) are live
        // locals for the whole call, and `sendmsg` only reads them. The kernel duplicates the
        // descriptor into the peer's table; it does not move or close ours.
        let sent =
            unsafe { libc::sendmsg(socket.as_raw_fd(), &raw const message, libc::MSG_NOSIGNAL) };

        if sent == 1 {
            return Ok(());
        }
        if sent < 0 {
            let error = io::Error::last_os_error();
            if error.raw_os_error() == Some(libc::EINTR) {
                continue;
            }
            return Err(error);
        }
        return Err(io::Error::from(io::ErrorKind::WriteZero));
    }
}
