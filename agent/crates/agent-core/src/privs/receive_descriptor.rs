//! Receiving exactly one open file descriptor from the other end of a unix socket.

use std::fs::File;
use std::io;
use std::os::fd::{AsFd, AsRawFd, BorrowedFd, FromRawFd, OwnedFd, RawFd};

use super::descriptor_is_read_only::descriptor_is_read_only;
use super::priv_error::PrivError;

/// Words of control buffer, as `u64` so the buffer is aligned for a `cmsghdr`. Sixty-four bytes.
const CONTROL_WORDS: usize = 8;

/// How many descriptors the receive is prepared to take in before it can tell "one" from "more".
///
/// Two would be enough to detect "not exactly one"; four leaves room to hold, and therefore close,
/// a few more without relying on the kernel to do it. A message carrying more than fit is flagged
/// truncated (`MSG_CTRUNC`) and refused, and the kernel closes what did not fit.
const DESCRIPTOR_CAPACITY: usize = 4;

/// Receives one descriptor, with its one data byte, from `socket`; refuses anything else.
///
/// The parent's half of
/// [`fork_as_account_for_descriptor`](super::fork_as_account_for_descriptor::fork_as_account_for_descriptor).
/// What arrives is a claim by a process at the customer's uid, so the rules are those of a
/// request from the network: accepted only in the exact shape expected, otherwise refused and
/// everything received along the way closed.
///
/// - **The descriptors are claimed before they are judged.** The kernel installs every descriptor
///   in the message into this process the moment `recvmsg` returns, whether or not the message is
///   acceptable. Each is wrapped in an owning handle immediately, so every refusal below closes
///   them by dropping them; a refusal that returned early would leak a customer-chosen descriptor
///   into the root daemon.
/// - **`MSG_CMSG_CLOEXEC`**, so the received descriptor is not inherited by a program this daemon
///   spawns while it holds it.
/// - **Exactly one descriptor and exactly one data byte.** None, two, or more than the buffer
///   holds (`MSG_CTRUNC`) is [`PrivError::DescriptorNotReceived`]. Zero data bytes is
///   end-of-file: the child exited without sending.
/// - **Read-only.** A descriptor opened for writing is [`PrivError::DescriptorNotReadOnly`]; the
///   hand-off exists so root can read.
///
/// What the descriptor IS — a regular file, owned by the account, with one link — is not decided
/// here, because this function does not know what the caller asked for. The caller `fstat`s it
/// before reading a byte (rules/security.md item 2).
///
/// Blocks until a message arrives; the caller waits for readiness with its own deadline first.
///
/// # Errors
///
/// [`PrivError::DescriptorNotReceived`], [`PrivError::DescriptorNotReadOnly`] as above, and
/// [`PrivError::ChannelFailed`] when `recvmsg` or `fcntl` fails.
pub(super) fn receive_descriptor(socket: BorrowedFd<'_>) -> Result<File, PrivError> {
    let mut vehicle = [0_u8; 1];
    let mut chunk = libc::iovec {
        iov_base: vehicle.as_mut_ptr().cast(),
        iov_len: vehicle.len(),
    };
    let mut control = [0_u64; CONTROL_WORDS];

    // SAFETY: `msghdr` is plain data for which all-zero bytes are a valid value.
    let mut message: libc::msghdr = unsafe { std::mem::zeroed() };
    message.msg_iov = &raw mut chunk;
    message.msg_iovlen = 1;
    message.msg_control = control.as_mut_ptr().cast();
    // SAFETY: `CMSG_SPACE` is pure arithmetic; see `send_descriptor`. The result is at most the
    // sixty-four bytes of `control`, so the kernel is never told the buffer is larger than it is.
    message.msg_controllen =
        unsafe { libc::CMSG_SPACE((DESCRIPTOR_CAPACITY * size_of::<RawFd>()) as libc::c_uint) }
            as _;

    let received = loop {
        // SAFETY: `message` and everything it points at are live locals for the whole call;
        // `recvmsg` writes only into `vehicle` and `control`, within the lengths given.
        let count =
            unsafe { libc::recvmsg(socket.as_raw_fd(), &raw mut message, libc::MSG_CMSG_CLOEXEC) };
        if count >= 0 {
            break count;
        }
        let error = io::Error::last_os_error();
        if error.raw_os_error() != Some(libc::EINTR) {
            return Err(PrivError::channel_failed(&error));
        }
    };

    let mut descriptors = claim_descriptors(&message);

    if received != 1 || message.msg_flags & libc::MSG_CTRUNC != 0 || descriptors.len() != 1 {
        return Err(PrivError::DescriptorNotReceived);
    }
    let Some(descriptor) = descriptors.pop() else {
        return Err(PrivError::DescriptorNotReceived);
    };

    match descriptor_is_read_only(descriptor.as_fd()) {
        Ok(true) => Ok(File::from(descriptor)),
        Ok(false) => Err(PrivError::DescriptorNotReadOnly),
        Err(error) => Err(PrivError::channel_failed(&error)),
    }
}

/// Takes ownership of every descriptor the kernel installed with `message`.
///
/// Walks all control messages, not only the first, and counts every `SCM_RIGHTS` payload: a
/// sender that splits descriptors across messages must not be able to slip one past a check that
/// looked at the first only. The returned handles close their descriptors when dropped.
fn claim_descriptors(message: &libc::msghdr) -> Vec<OwnedFd> {
    let mut claimed = Vec::new();

    // SAFETY: `message` was filled in by a successful `recvmsg`, so its control pointer and length
    // describe the live buffer the kernel wrote and every header reached from it lies inside.
    let mut header = unsafe { libc::CMSG_FIRSTHDR(message) };
    while !header.is_null() {
        // SAFETY: `header` is non-null and inside the kernel-filled control buffer, as above.
        let (level, kind, length) = unsafe {
            (
                (*header).cmsg_level,
                (*header).cmsg_type,
                (*header).cmsg_len,
            )
        };

        if level == libc::SOL_SOCKET && kind == libc::SCM_RIGHTS {
            // SAFETY: `CMSG_LEN` is pure arithmetic.
            let header_length = unsafe { libc::CMSG_LEN(0) } as usize;
            let count = length.saturating_sub(header_length) / size_of::<RawFd>();
            // SAFETY: the data area of this message holds `count` descriptors, as the kernel
            // computed its length; it is read with unaligned loads.
            let data = unsafe { libc::CMSG_DATA(header) }.cast::<RawFd>();
            for index in 0..count {
                // SAFETY: `index < count`, so the load is inside the message's data area.
                let raw = unsafe { data.add(index).read_unaligned() };
                // SAFETY: the kernel installed this descriptor into this process during
                // `recvmsg` and nothing else holds its number, so it is open, ours to close and
                // not yet owned by any other handle.
                claimed.push(unsafe { OwnedFd::from_raw_fd(raw) });
            }
        }

        // SAFETY: as for `CMSG_FIRSTHDR`; `header` is a header of this message.
        header = unsafe { libc::CMSG_NXTHDR(message, header) };
    }

    claimed
}

#[cfg(test)]
#[path = "../tests/privs/receive_descriptor_tests.rs"]
mod tests;
