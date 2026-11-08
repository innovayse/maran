//! Doing work as a hosting customer and bringing a bounded byte string back.

use std::io::{self, PipeReader, Read};
use std::os::fd::{AsFd, AsRawFd};
use std::time::{Duration, Instant};

use super::account_ids::AccountIds;
use super::drop_to_account::drop_to;
use super::fork_and_run::fork_and_run;
use super::poll_until_readable::poll_until_readable;
use super::priv_error::PrivError;
use super::result_sink::ResultSink;
use super::wait_for_child::{CHILD_PATIENCE, abandon, wait_until};

/// How many bytes the parent asks the pipe for at a time.
///
/// A stack buffer of a fixed size, not one sized by anything the child said: the child controls
/// how much it writes and when, and nothing it does may decide how much the parent allocates.
const READ_CHUNK: usize = 64 * 1024;

/// Runs `work` in a forked child that has dropped to `ids`, and returns the bytes it wrote to its
/// [`ResultSink`] — at most `max_bytes` of them, and only if the child then exited successfully.
///
/// The first of the two entry points beside [`fork_as_account`](super::fork_as_account::fork_as_account)
/// that let data come back from the dropped child. It exists because a read-only operation (a
/// directory listing, a name search) has a RESULT, and the alternative — reading the customer's
/// tree as root with ownership checks — makes every missing check a disclosure of any file on the
/// host (docs/superpowers/notes/2026-11-07-file-manager-browse-threat-note.md, "How data leaves the
/// dropped child"). The drop, its verification, the descriptor sweep and the exit-status protocol
/// are `fork_and_run`'s, shared with `fork_as_account`, not copied.
///
/// # What the parent trusts from the child
///
/// Nothing it cannot check, and each rule below is a refusal, not a repair:
///
/// - **The channel is made by the parent, before the fork**, and the child keeps exactly its end:
///   the sweep spares that one descriptor and closes the rest. The parent closes its own copy of
///   the write end straight after the fork, so end-of-file means the child is gone.
/// - **The byte count is bounded by the parent.** At most `max_bytes` are ever buffered; the first
///   byte past it ends the call with [`PrivError::ResultTooLarge`] and the child is killed and
///   reaped. No length the child states is believed or allocated for, because the child states
///   none — the bytes are a stream and the parent counts them.
/// - **A result is only a result if the child succeeded.** Whatever was written is discarded
///   unless the child exited with the success status; a child that wrote half a listing and then
///   failed, was killed, or timed out yields an error and no bytes, never a partial answer.
/// - **The parent reads while it waits.** A result larger than one pipe buffer (64 KiB on Linux)
///   fills the pipe and blocks the child until someone reads; a parent that only waited would
///   deadlock against it until the patience ran out. The read loop and the child's patience share
///   one deadline, `CHILD_PATIENCE`, so a child that goes silent costs the same two minutes any
///   wedged child costs.
///
/// What the bytes MEAN is the caller's to check. They are untrusted input to whatever decodes them
/// (a strict, bounded codec), exactly as a request from the network is.
///
/// **Callers MUST invoke this from `tokio::task::spawn_blocking`**, for the reason
/// `fork_as_account` states, and the same allocation rules apply to `work`.
///
/// # Errors
///
/// Everything [`fork_as_account`](super::fork_as_account::fork_as_account) can return, plus:
/// [`PrivError::ResultTooLarge`] when the child wrote more than `max_bytes`;
/// [`PrivError::ChannelFailed`] when the pipe could not be made or read.
pub fn fork_as_account_for_bytes<F>(
    ids: &AccountIds,
    max_bytes: usize,
    work: F,
) -> Result<Vec<u8>, PrivError>
where
    F: FnOnce(&mut ResultSink<'_>) -> Result<(), PrivError>,
{
    fork_for_bytes(|| drop_to(ids), CHILD_PATIENCE, max_bytes, work)
}

/// The whole call with the drop and the patience supplied.
///
/// The seam exists for the reason `wait_until` has one: a test is not root, so the drop is
/// replaced by a step that succeeds or fails on demand, and the patience is shortened so the
/// give-up path can be exercised in milliseconds instead of waited out. Production reaches it only
/// through [`fork_as_account_for_bytes`], with the real drop and `CHILD_PATIENCE`.
///
/// # Errors
///
/// As documented on [`fork_as_account_for_bytes`].
fn fork_for_bytes<D, F>(
    drop_step: D,
    patience: Duration,
    max_bytes: usize,
    work: F,
) -> Result<Vec<u8>, PrivError>
where
    D: FnOnce() -> Result<(), i32>,
    F: FnOnce(&mut ResultSink<'_>) -> Result<(), PrivError>,
{
    let (mut reader, writer) =
        std::io::pipe().map_err(|error| PrivError::channel_failed(&error))?;

    let child = fork_and_run(drop_step, Some(writer.as_raw_fd()), || {
        work(&mut ResultSink::new(&writer))
    })?;

    // The parent's copy of the write end goes NOW. While it is open, end-of-file can never
    // arrive, and a child that exits would leave the read loop waiting for the deadline.
    drop(writer);

    let deadline = Instant::now() + patience;
    let bytes = match drain(&mut reader, max_bytes, deadline) {
        Ok(bytes) => bytes,
        Err(refusal) => return Err(abandon(child, refusal)),
    };

    // The channel is closed from the child's side, so it is finished or about to be. Its status,
    // not the bytes, decides whether they count.
    wait_until(child, deadline.saturating_duration_since(Instant::now()))?;
    Ok(bytes)
}

/// Reads the channel to end-of-file, refusing to hold more than `max_bytes` or to wait past
/// `deadline`.
///
/// # Errors
///
/// [`PrivError::ResultTooLarge`] on the first byte past `max_bytes`;
/// [`PrivError::ChildTimedOut`] when the deadline passes before end-of-file;
/// [`PrivError::ChannelFailed`] when `poll` or `read` fails.
fn drain(
    reader: &mut PipeReader,
    max_bytes: usize,
    deadline: Instant,
) -> Result<Vec<u8>, PrivError> {
    let mut bytes = Vec::new();
    let mut chunk = [0_u8; READ_CHUNK];

    loop {
        if !poll_until_readable(reader.as_fd(), deadline)
            .map_err(|error| PrivError::channel_failed(&error))?
        {
            return Err(PrivError::ChildTimedOut);
        }

        match reader.read(&mut chunk) {
            Ok(0) => return Ok(bytes),
            Ok(read) => {
                // `bytes.len() <= max_bytes` is the loop's invariant, so the subtraction cannot
                // underflow, and unlike `len + read > max` it cannot overflow either.
                if read > max_bytes - bytes.len() {
                    return Err(PrivError::ResultTooLarge);
                }
                bytes.extend_from_slice(&chunk[..read]);
            }
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(error) => return Err(PrivError::channel_failed(&error)),
        }
    }
}

#[cfg(test)]
#[path = "../tests/privs/fork_as_account_for_bytes_tests.rs"]
mod tests;
