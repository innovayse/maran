//! Doing work as a hosting customer, with an upload in and a fixed-size answer out.

use std::io::{self, Read, Write};
use std::net::Shutdown;
use std::os::fd::AsRawFd;
use std::os::unix::net::UnixStream;
use std::time::{Duration, Instant};

use super::account_ids::AccountIds;
use super::child_context::ChildContext;
use super::child_input_feed::ChildInputFeed;
use super::child_input_writer::ChildInputWriter;
use super::child_refusal::ChildRefusal;
use super::child_report::ChildReport;
use super::child_tally::ChildTally;
use super::decode_report::decode_report;
use super::decoded_report::DecodedReport;
use super::drop_to_account::drop_to;
use super::encode_report::{REPORT_BYTES, encode_report};
use super::fork_and_run::fork_and_run;
use super::priv_error::PrivError;
use super::wait_for_child::{abandon, wait_until};

/// How long a child that has lost its input is given to report and exit before it is killed.
const CLEAN_UP_GRACE: Duration = Duration::from_secs(5);

/// Shortest socket timeout, because a zero timeout is an error and not "no wait".
const SHORTEST_WAIT: Duration = Duration::from_millis(1);

/// Runs `work` in a forked child that has dropped to `ids`, optionally feeds it a framed upload,
/// and returns the two figures it reports.
///
/// The one entry point beside [`fork_as_account`](super::fork_as_account::fork_as_account) that
/// lets a typed answer, and an upload, cross the fork. The fork, the drop, the verification, the
/// descriptor sweep and the exit-status protocol are `fork_and_run`'s, shared and not copied
/// (docs/superpowers/notes/2026-11-07-file-manager-mutations-threat-note.md).
///
/// - **One channel, made by the parent before the fork**: a socket pair. The child keeps its end
///   (the sweep spares exactly that one descriptor) and the parent closes its copy at once.
/// - **In**: `input`, when given, runs on the calling thread with a [`ChildInputWriter`] bound to
///   the call's deadline. Returning `Err`, or `Ok` without
///   [`ChildInputWriter::complete`], is an abort: the parent half-closes the channel and the
///   child's [`ChildContext::read_frame`] answers `Aborted`.
/// - **Out**: when `work` returns, the fork writes `[status][first][second]`, 17 bytes, and the
///   child exits. The parent reads exactly that many with a deadline and decodes integers only.
///   A refusal number is `1..=63`. A child that exited successfully with no valid success
///   report, or whose report disagrees with its exit status, is [`PrivError::ReportMalformed`];
///   a refusal outside the range is a plain [`PrivError::WorkFailed`]. Nothing is trusted as a
///   verdict beyond the closed range.
/// - **The exit status stays the authority.** A success report counts only when the child also
///   exited successfully; a refusal report counts only when it exited with the work-failed status,
///   and then yields [`PrivError::WorkRefused`]. A drop failure, a signal or a timeout is
///   reported exactly as `fork_as_account` reports it.
/// - **An input failure does not hide the child's answer.** If the child refused mid-upload (a
///   full quota), the feed fails with a broken pipe; the child's refusal is still read and
///   wins. The feed's own error is reported only when the child gave no better reason.
///
/// `work` must not panic; a panic is the work-failed exit status and reports no reason.
///
/// **Callers MUST invoke this from `tokio::task::spawn_blocking`**: `input` runs on that blocking
/// thread and may block on a channel from the async side.
///
/// # Errors
///
/// Everything [`fork_as_account`](super::fork_as_account::fork_as_account) can return, plus
/// [`PrivError::WorkRefused`], [`PrivError::ReportMalformed`] and [`PrivError::ChannelFailed`]
/// (the socket pair could not be made or read, or `input` failed with no child answer; a deadline
/// that passes is `ETIMEDOUT`).
pub fn fork_reporting_as_account<F>(
    ids: &AccountIds,
    patience: Duration,
    input: Option<&mut ChildInputFeed<'_>>,
    work: F,
) -> Result<ChildReport, PrivError>
where
    F: FnOnce(&mut ChildContext<'_>) -> Result<ChildTally, ChildRefusal>,
{
    fork_reporting(|| drop_to(ids), patience, CLEAN_UP_GRACE, input, work)
}

/// The whole call with the drop, the patience and the clean-up grace supplied.
///
/// The seam exists for the reason `fork_for_bytes` has one: a test is not root, so the drop is a
/// step that succeeds or fails on demand, and the patience can be shortened. Production reaches it
/// only through [`fork_reporting_as_account`].
///
/// # Errors
///
/// As documented on [`fork_reporting_as_account`].
fn fork_reporting<D, F>(
    drop_step: D,
    patience: Duration,
    grace: Duration,
    input: Option<&mut ChildInputFeed<'_>>,
    work: F,
) -> Result<ChildReport, PrivError>
where
    D: FnOnce() -> Result<(), i32>,
    F: FnOnce(&mut ChildContext<'_>) -> Result<ChildTally, ChildRefusal>,
{
    let (parent_end, child_end) =
        UnixStream::pair().map_err(|error| PrivError::channel_failed(&error))?;

    let child = fork_and_run(drop_step, Some(child_end.as_raw_fd()), || {
        let mut context = ChildContext::new(&child_end);
        let outcome = work(&mut context);
        let mut channel = &child_end;
        let written = channel.write_all(&encode_report(&outcome));
        match (outcome, written) {
            (Ok(_), Ok(())) => Ok(()),
            _ => Err(PrivError::WorkFailed),
        }
    })?;

    // The parent's copy of the child's end goes NOW, so a dead child ends reads and fails writes
    // instead of leaving them to wait for the deadline.
    drop(child_end);

    let mut deadline = Instant::now() + patience;
    let fed = input.map_or(Ok(()), |feed| {
        feed(&mut ChildInputWriter::new(&parent_end, deadline))
    });
    // The abort, or the end of a stream that was completed: either way nothing more is coming.
    let _ = parent_end.shutdown(Shutdown::Write);
    if fed.is_err() {
        // The child has lost its input and needs a moment to report and exit, not the call's
        // remaining patience, which a timed-out feed has already spent.
        deadline = Instant::now() + grace;
    }

    let report = read_report(&parent_end, deadline);
    let waited = match &report {
        Err(timeout) => return Err(abandon(child, timeout_of(timeout))),
        Ok(_) => wait_until(child, deadline.saturating_duration_since(Instant::now())),
    };

    let answer = match (waited, report) {
        (Ok(()), Ok(Some(bytes))) => match decode_report(&bytes) {
            DecodedReport::Succeeded(report) => Ok(report),
            DecodedReport::Refused(_) | DecodedReport::Malformed => Err(PrivError::ReportMalformed),
        },
        (Ok(()), _) => Err(PrivError::ReportMalformed),
        (Err(PrivError::WorkFailed), Ok(Some(bytes))) => match decode_report(&bytes) {
            DecodedReport::Refused(reason) => Err(PrivError::WorkRefused { reason }),
            DecodedReport::Succeeded(_) | DecodedReport::Malformed => Err(PrivError::WorkFailed),
        },
        (Err(other), _) => Err(other),
    };

    match (answer, fed) {
        (Ok(_), Err(error)) => Err(feed_failure(&error)),
        (Err(PrivError::WorkFailed), Err(error)) => Err(feed_failure(&error)),
        (answer, _) => answer,
    }
}

/// How a failed feed is reported when the child gave no better reason.
fn feed_failure(error: &io::Error) -> PrivError {
    if error.kind() == io::ErrorKind::TimedOut {
        PrivError::ChannelFailed {
            errno: libc::ETIMEDOUT,
        }
    } else {
        PrivError::channel_failed(error)
    }
}

/// The error a stalled report read stands for.
fn timeout_of(error: &io::Error) -> PrivError {
    if matches!(
        error.kind(),
        io::ErrorKind::TimedOut | io::ErrorKind::WouldBlock
    ) {
        PrivError::ChildTimedOut
    } else {
        PrivError::channel_failed(error)
    }
}

/// Reads the child's report: `Ok(Some(bytes))` when all [`REPORT_BYTES`] arrived, `Ok(None)` when
/// the channel ended first (the child died or wrote nothing), and the I/O error when the read
/// failed or the deadline passed.
fn read_report(
    channel: &UnixStream,
    deadline: Instant,
) -> Result<Option<[u8; REPORT_BYTES]>, io::Error> {
    let mut bytes = [0_u8; REPORT_BYTES];
    let mut filled = 0;
    let mut reader = channel;
    while filled < REPORT_BYTES {
        let remaining = deadline.saturating_duration_since(Instant::now());
        if remaining.is_zero() {
            return Err(io::Error::from(io::ErrorKind::TimedOut));
        }
        channel.set_read_timeout(Some(remaining.max(SHORTEST_WAIT)))?;
        match reader.read(&mut bytes[filled..]) {
            Ok(0) => return Ok(None),
            Ok(read) => filled += read,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(error) => return Err(error),
        }
    }
    Ok(Some(bytes))
}

#[cfg(test)]
#[path = "../tests/privs/fork_reporting_as_account_tests.rs"]
mod tests;
