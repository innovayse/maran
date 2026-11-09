//! Doing work as a hosting customer and bringing back one file it opened.

use std::fs::File;
use std::os::fd::{AsFd, AsRawFd, BorrowedFd};
use std::os::unix::net::UnixStream;
use std::time::{Duration, Instant};

use super::account_ids::AccountIds;
use super::drop_to_account::drop_to;
use super::fork_and_run::fork_and_run;
use super::poll_until_readable::poll_until_readable;
use super::priv_error::PrivError;
use super::receive_descriptor::receive_descriptor;
use super::send_descriptor::send_descriptor;
use super::wait_for_child::{CHILD_PATIENCE, abandon, wait_until};

/// Runs `work` in a forked child that has dropped to `ids`, and returns the file it opened — as an
/// open descriptor, read-only, once the child has exited successfully.
///
/// The second of the two entry points beside [`fork_as_account`](super::fork_as_account::fork_as_account)
/// that let something come back from the dropped child, and the one for READING a file. The child
/// walks to the file and opens it as the account; the parent receives the open descriptor and
/// reads from IT, never from a path. Why a descriptor and not a byte string: the child's lifetime
/// is bounded by `CHILD_PATIENCE`, and a 256 MiB download at a customer's link speed can outlast it.
/// A child that stays alive for as long as the consumer reads would be a customer-uid process held
/// open by the customer's own network, and one per download.
/// The drop, its verification, the descriptor sweep and the exit-status protocol are
/// `fork_and_run`'s, shared with `fork_as_account`, not copied.
///
/// # What the parent trusts from the child
///
/// Nothing it cannot check:
///
/// - **The channel is a socket pair made by the parent before the fork**; the child keeps its end
///   and nothing else, and the parent closes its copy of that end right after the fork.
/// - **Exactly one descriptor, one data byte, opened read-only**, or the call is an error and
///   every descriptor that arrived is closed. See [`PrivError::DescriptorNotReceived`] and
///   [`PrivError::DescriptorNotReadOnly`].
/// - **The descriptor only counts if the child then exited successfully.** A child that sent a
///   descriptor and then failed, was killed or hung yields an error and a closed descriptor.
/// - **The wait for the descriptor has the child's deadline**, `CHILD_PATIENCE`: a child that
///   never sends is killed and reaped, not waited for.
///
/// What the file IS — regular, owned by the account, one link — is not decided here: the caller
/// `fstat`s the received descriptor and refuses anything else before reading a byte, because the
/// child's own check is a claim (docs/superpowers/notes/2026-11-07-file-manager-browse-threat-note.md).
///
/// `work` runs in the child under the allocation rules of `fork_as_account`. It must open the file
/// with `O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC` so a FIFO planted in the file's place returns
/// instead of blocking the child, and return it; this function sends it.
///
/// **Callers MUST invoke this from `tokio::task::spawn_blocking`.**
///
/// # Errors
///
/// Everything [`fork_as_account`](super::fork_as_account::fork_as_account) can return, plus:
/// [`PrivError::DescriptorNotReceived`] when the child exited successfully without handing over
/// exactly one descriptor; [`PrivError::DescriptorNotReadOnly`] when it handed over one that could
/// write; [`PrivError::ChannelFailed`] when the socket pair could not be made or read.
pub fn fork_as_account_for_descriptor<F>(ids: &AccountIds, work: F) -> Result<File, PrivError>
where
    F: FnOnce() -> Result<File, PrivError>,
{
    fork_for_descriptor(|| drop_to(ids), CHILD_PATIENCE, hand_over(work))
}

/// What the child does in production: run `work` to open the file, then send the descriptor.
///
/// A function of its own, and not a closure inlined at the one call, so a test can run the real
/// child action under the seam below instead of a copy of it. A failure to send is a failed
/// child, reported by its exit status; the errno does not cross.
fn hand_over<F>(work: F) -> impl FnOnce(BorrowedFd<'_>) -> Result<(), PrivError>
where
    F: FnOnce() -> Result<File, PrivError>,
{
    move |channel: BorrowedFd<'_>| {
        let file = work()?;
        send_descriptor(channel, file.as_fd()).map_err(|_| PrivError::WorkFailed)
    }
}

/// The whole call with the drop, the patience and the child's action supplied.
///
/// The seam exists so a test, which is not root, can replace the drop and — more to the point —
/// can make the child do what a hostile or broken one would: send nothing, send twice, send and
/// then hang, send and then fail. Production reaches it only through
/// [`fork_as_account_for_descriptor`], whose child action opens the file and sends it.
///
/// # Errors
///
/// As documented on [`fork_as_account_for_descriptor`].
fn fork_for_descriptor<D, C>(
    drop_step: D,
    patience: Duration,
    child_action: C,
) -> Result<File, PrivError>
where
    D: FnOnce() -> Result<(), i32>,
    C: FnOnce(BorrowedFd<'_>) -> Result<(), PrivError>,
{
    let (parent_end, child_end) =
        UnixStream::pair().map_err(|error| PrivError::channel_failed(&error))?;

    let child = fork_and_run(drop_step, Some(child_end.as_raw_fd()), || {
        child_action(child_end.as_fd())
    })?;

    // The parent's copy of the child's end goes now, so a child that exits leaves the parent's
    // end at end-of-file instead of waiting for a peer that is itself.
    drop(child_end);

    let deadline = Instant::now() + patience;
    let received = match poll_until_readable(parent_end.as_fd(), deadline) {
        Ok(true) => receive_descriptor(parent_end.as_fd()),
        Ok(false) => return Err(abandon(child, PrivError::ChildTimedOut)),
        Err(error) => return Err(abandon(child, PrivError::channel_failed(&error))),
    };

    // The child's own outcome decides whether what arrived counts, and it also explains a
    // refusal: a child that failed before sending leaves end-of-file here, and the honest answer
    // is the failure it exited with, not "no descriptor".
    let outcome = wait_until(child, deadline.saturating_duration_since(Instant::now()));
    match (received, outcome) {
        (Ok(file), Ok(())) => Ok(file),
        (_, Err(child_failure)) => Err(child_failure),
        (Err(refusal), Ok(())) => Err(refusal),
    }
}

#[cfg(test)]
#[path = "../tests/privs/fork_as_account_for_descriptor_tests.rs"]
mod tests;
