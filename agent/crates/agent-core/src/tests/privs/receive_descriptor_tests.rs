//! Receiving a descriptor: the one shape accepted, and every shape refused with nothing leaked.
//!
//! The sender here is a hand-written `sendmsg`, not `send_descriptor`, because the production
//! sender can only ever send one descriptor and the point is to be handed what a hostile or broken
//! one would send: none, two, a dozen, the wrong access mode, no data byte at all.
//!
//! "Nothing leaked" is observed through a pipe whose READ end is what gets sent: once every copy
//! of a read end is closed, a write to the pipe fails with `EPIPE`. A refusal that forgot to close
//! what arrived leaves a copy open, and the write succeeds. The acceptance test uses the same pipe
//! and shows the write succeeding while the receiver holds the descriptor, which is the control
//! that proves the observation can see an open copy.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::{Read, Write};
use std::os::fd::{AsFd, AsRawFd, RawFd};
use std::os::unix::net::UnixStream;

use super::receive_descriptor;
use crate::privs::priv_error::PrivError;

/// Sends `descriptors` and `data` to the peer of `socket` in one message, as a sender chooses.
fn send_raw(socket: &UnixStream, descriptors: &[RawFd], data: &[u8]) {
    let mut payload = data.to_vec();
    let mut chunk = libc::iovec {
        iov_base: payload.as_mut_ptr().cast(),
        iov_len: payload.len(),
    };
    // 512 bytes, aligned for `cmsghdr`: room for about a hundred descriptors.
    let mut control = [0_u64; 64];

    // SAFETY: `msghdr` is plain data for which all-zero bytes are a valid value.
    let mut message: libc::msghdr = unsafe { std::mem::zeroed() };
    message.msg_iov = &raw mut chunk;
    message.msg_iovlen = 1;

    if !descriptors.is_empty() {
        let bytes = std::mem::size_of_val(descriptors);
        message.msg_control = control.as_mut_ptr().cast();
        // SAFETY: `CMSG_SPACE` is pure arithmetic.
        message.msg_controllen = unsafe { libc::CMSG_SPACE(bytes as libc::c_uint) } as _;
        // SAFETY: `control` is live, aligned and large enough, so the first header lies inside it.
        let header = unsafe { libc::CMSG_FIRSTHDR(&raw const message) };
        // SAFETY: `header` points into `control`, which has room for `bytes` of data after it.
        unsafe {
            (*header).cmsg_level = libc::SOL_SOCKET;
            (*header).cmsg_type = libc::SCM_RIGHTS;
            (*header).cmsg_len = libc::CMSG_LEN(bytes as libc::c_uint) as _;
            std::ptr::copy_nonoverlapping(
                descriptors.as_ptr().cast::<u8>(),
                libc::CMSG_DATA(header),
                bytes,
            );
        }
    }

    // SAFETY: everything `message` points at is a live local; `sendmsg` only reads it.
    let sent = unsafe { libc::sendmsg(socket.as_raw_fd(), &raw const message, libc::MSG_NOSIGNAL) };
    assert_eq!(
        usize::try_from(sent).unwrap(),
        data.len(),
        "the test's own send failed"
    );
}

/// A pipe, as its two ends.
fn a_pipe() -> (std::io::PipeReader, std::io::PipeWriter) {
    std::io::pipe().unwrap()
}

/// A temporary file holding `contents`, opened for reading.
fn a_readable_file(contents: &[u8]) -> (tempfile::TempDir, File) {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("file");
    std::fs::write(&path, contents).unwrap();
    let file = File::open(&path).unwrap();
    (directory, file)
}

#[test]
fn one_read_only_descriptor_with_one_byte_is_received_and_readable() {
    let (_directory, file) = a_readable_file(b"the file's contents");
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[file.as_raw_fd()], &[0]);
    let mut received = receive_descriptor(receiver.as_fd()).unwrap();

    let mut contents = String::new();
    received.read_to_string(&mut contents).unwrap();
    assert_eq!(contents, "the file's contents");
}

#[test]
fn a_received_descriptor_cannot_be_written_to() {
    let (_directory, file) = a_readable_file(b"x");
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[file.as_raw_fd()], &[0]);
    let mut received = receive_descriptor(receiver.as_fd()).unwrap();

    assert!(received.write(b"y").is_err());
}

#[test]
fn a_received_descriptor_is_close_on_exec() {
    let (_directory, file) = a_readable_file(b"x");
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[file.as_raw_fd()], &[0]);
    let received = receive_descriptor(receiver.as_fd()).unwrap();

    // SAFETY: `fcntl` with `F_GETFD` takes an integer and touches no memory.
    let flags = unsafe { libc::fcntl(received.as_raw_fd(), libc::F_GETFD) };
    assert!(flags >= 0 && flags & libc::FD_CLOEXEC != 0);
}

#[test]
fn the_control_a_held_copy_of_a_read_end_keeps_the_pipe_writable() {
    // The observation the refusal tests below rely on, shown able to see an open copy: while the
    // receiver holds the descriptor, the write succeeds; once it is dropped, the write fails.
    let (reader, mut writer) = a_pipe();
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[reader.as_raw_fd()], &[0]);
    drop(reader);
    let received = receive_descriptor(receiver.as_fd()).unwrap();
    assert!(
        writer.write_all(b"x").is_ok(),
        "the received copy must keep the pipe open"
    );

    drop(received);
    assert!(
        writer.write_all(b"y").is_err(),
        "with every copy closed the pipe must be broken"
    );
}

#[test]
fn a_data_byte_with_no_descriptor_is_refused() {
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[], &[0]);

    assert_eq!(
        receive_descriptor(receiver.as_fd()).unwrap_err(),
        PrivError::DescriptorNotReceived
    );
}

#[test]
fn a_peer_that_closed_without_sending_is_refused() {
    let (sender, receiver) = UnixStream::pair().unwrap();
    drop(sender);

    assert_eq!(
        receive_descriptor(receiver.as_fd()).unwrap_err(),
        PrivError::DescriptorNotReceived
    );
}

#[test]
fn two_descriptors_in_one_message_are_refused_and_both_are_closed() {
    let (reader, mut writer) = a_pipe();
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[reader.as_raw_fd(), reader.as_raw_fd()], &[0]);
    drop(reader);

    assert_eq!(
        receive_descriptor(receiver.as_fd()).unwrap_err(),
        PrivError::DescriptorNotReceived
    );
    assert!(
        writer.write_all(b"x").is_err(),
        "a refused descriptor must not stay open"
    );
}

#[test]
fn more_descriptors_than_the_buffer_holds_are_refused_and_all_are_closed() {
    // Twelve against a capacity of four: the kernel truncates the control message, flags it, and
    // closes what did not fit; the receiver must close the four that did.
    let (reader, mut writer) = a_pipe();
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[reader.as_raw_fd(); 12], &[0]);
    drop(reader);

    assert_eq!(
        receive_descriptor(receiver.as_fd()).unwrap_err(),
        PrivError::DescriptorNotReceived
    );
    assert!(
        writer.write_all(b"x").is_err(),
        "no copy of a truncated message may survive"
    );
}

#[test]
fn a_descriptor_that_can_write_is_refused_and_closed() {
    let directory = tempfile::tempdir().unwrap();
    let writable = std::fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .truncate(true)
        .open(directory.path().join("file"))
        .unwrap();
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[writable.as_raw_fd()], &[0]);

    assert_eq!(
        receive_descriptor(receiver.as_fd()).unwrap_err(),
        PrivError::DescriptorNotReadOnly
    );
}

#[test]
fn a_write_only_descriptor_is_refused_too() {
    let (_reader, writer) = a_pipe();
    let (sender, receiver) = UnixStream::pair().unwrap();

    send_raw(&sender, &[writer.as_raw_fd()], &[0]);

    assert_eq!(
        receive_descriptor(receiver.as_fd()).unwrap_err(),
        PrivError::DescriptorNotReadOnly
    );
}

#[test]
fn receiving_from_something_that_is_not_a_socket_is_a_channel_failure() {
    let (_directory, file) = a_readable_file(b"x");

    assert_eq!(
        receive_descriptor(file.as_fd()).unwrap_err(),
        PrivError::ChannelFailed {
            errno: libc::ENOTSOCK
        }
    );
}
