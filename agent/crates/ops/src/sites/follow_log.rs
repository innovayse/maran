//! Reading a customer's log from the root daemon, safely and within bounds.
//!
//! Everything here exists because of one fact: **the log file belongs to the
//! account.** The account chooses its size, and can replace it between two
//! reads with something that is not a log at all. A naive `File::open` plus
//! `read_to_end` on a path inside `/home/<account>/` hands an unprivileged
//! customer four separate ways to attack the root process, and this module
//! closes each one explicitly:
//!
//! - **Size.** `truncate -s 50G` on their own log, or simply filling it, would
//!   make one `TailSiteLog` read fifty gigabytes into root's memory and take
//!   every other account's control plane down with it. So the history scan has
//!   a byte budget ([`HISTORY_BUDGET`]) and each follow read has a ceiling
//!   ([`FOLLOW_CEILING`]); the line cap bounds what is sent, these bound what
//!   is read.
//! - **A FIFO.** `rm access.log && mkfifo access.log`: opening a FIFO with no
//!   writer blocks in the kernel forever, and it is not a symlink, so
//!   `O_NOFOLLOW` says nothing about it. On a `spawn_blocking` thread that is
//!   one pool slot gone per call, and every operation in the agent needs that
//!   pool. So the open is `O_NONBLOCK` and the result is `fstat`ed and refused
//!   unless it is a regular file.
//! - **A hardlink.** `ln /etc/shadow access.log` is not a symlink either, and
//!   the path is genuinely inside the home, so every path check ever written
//!   passes it. Only the inode gives it away: the file must be owned by the
//!   account and have exactly one link.
//! - **A swapped directory.** `rmdir logs && ln -s /somewhere logs` between two
//!   polls redirects every later open, and `O_NOFOLLOW` never sees an
//!   intermediate component. `crate::validation::path`'s own documentation
//!   names this: *"resolving and then reopening by the original path would
//!   reintroduce the race this function exists to close."* So the directory is
//!   opened ONCE and the log is reached through that descriptor with
//!   `openat` — a descriptor names an inode, and no rename can move it.

use std::ffi::OsStr;
use std::fs::{File, Metadata, OpenOptions};
use std::io::{self, Read, Seek, SeekFrom};
use std::os::unix::fs::{MetadataExt, OpenOptionsExt};
use std::path::Path;
use std::time::{Duration, Instant};

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::sites::MAXIMUM_HISTORY_LINES;
use crate::sites::SitesOpError;
use crate::sites::log_sink::LogSink;
use crate::sites::model::log_tail_request::LogTailRequest;
use crate::sites::model::tail_end::TailEnd;

/// How long the follow sleeps between two reads of the log.
///
/// A poll and not an inotify watch: a watch is a kernel resource per stream
/// held in the root daemon for as long as a browser tab stays open. Half a
/// second is below what a human reading a log notices.
const POLL_INTERVAL: Duration = Duration::from_millis(500);

/// How long a tail may go without delivering a line before it gives up.
///
/// The other half of "a stream must not outlive its client". A dropped stream
/// is normally noticed through [`LogSink::is_listening`], but a client can also
/// simply stop reading without closing, and a `spawn_blocking` task cannot be
/// aborted from outside — so a tail that has had nothing to say for five
/// minutes ends itself. The panel reopens the stream; the daemon does not
/// accumulate threads.
///
/// Injectable at [`follow_with_patience`] for the same reason
/// `StreamLogSink::with_patience` is: a test that had to wait five minutes out
/// would never be written, and this guard would ship on an argument rather than
/// on a red-when-removed test. It is the give-up clock for the pool thread —
/// the one a customer exhausts by opening tabs on quiet sites — so it is the
/// last one that should be taken on trust.
const MAXIMUM_IDLE: Duration = Duration::from_secs(300);

/// The largest single read this module performs.
const READ_CHUNK: usize = 256 * 1024;

/// The most bytes the history scan will look through to find its lines.
///
/// A thousand lines can sit at the end of fifty gigabytes, so the line cap is
/// not a memory bound and this is. Four mebibytes is roughly four thousand
/// generously-sized log lines: enough that the cap, and not the budget, is what
/// normally ends the scan.
const HISTORY_BUDGET: u64 = 4 * 1024 * 1024;

/// The most bytes one follow poll will read and send.
///
/// A log written faster than the client reads is not buffered up: the excess is
/// dropped and reported by [`SKIPPED_MARKER`], because holding it would move
/// the account's write rate straight into the daemon's memory.
const FOLLOW_CEILING: u64 = 256 * 1024;

/// The line sent in place of output a busy log outran the reader with.
///
/// Reported and not silently dropped: an operator reading a log needs to know
/// there is a hole in it, and a gap nobody is told about is worse than no
/// output at all.
const SKIPPED_MARKER: &str = "[maran] skipped";

/// Flags every open of the log itself carries.
///
/// `O_NOFOLLOW` refuses a symlink in the final component, `O_NONBLOCK` makes a
/// FIFO return instead of blocking a root thread in the kernel, and
/// `O_CLOEXEC` keeps the descriptor out of anything the agent spawns later.
const LOG_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// Sends the historical tail of `request`'s log to `sink`, then follows it.
///
/// Returns WHY it ended, not merely that it did: see [`TailEnd`]. A tail the
/// agent stopped — a client dropped for not reading, or an idle ceiling reached
/// — is an ending the operator did not ask for, and one they must be able to
/// tell from a stream they closed themselves.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the account cannot be resolved,
/// when the log directory is not a directory the account owns, or when the log
/// is not a regular file the account owns with a single link. A log that does
/// not exist yet is not an error and yields no lines.
pub(crate) fn follow_log(
    request: &LogTailRequest,
    sink: &mut dyn LogSink,
) -> Result<TailEnd, SitesOpError> {
    // Resolved at the moment of use and never cached: an account deleted and
    // recreated gets a different uid, and a stale one would authorise a read of
    // whoever now holds it (`AccountIds`' own warning).
    let ids = AccountIds::resolve(&request.account).map_err(|_| unreadable(&request.file_name))?;

    follow_as(request, ids.uid(), sink)
}

/// The tail itself, for an already-resolved uid.
///
/// Split from [`follow_log`] at exactly the line that needs a real account in
/// the system's user database. Everything below this point — the pinned
/// directory, the inode checks, the bounded reads, the restart on truncation —
/// is the part a test must be able to reach, and a test can create a directory
/// it owns and pass its own uid. The split is what makes the newest `unsafe` in
/// the workspace testable without root.
///
/// # Errors
///
/// As [`follow_log`], minus the account resolution.
fn follow_as(
    request: &LogTailRequest,
    uid: u32,
    sink: &mut dyn LogSink,
) -> Result<TailEnd, SitesOpError> {
    follow_with_patience(request, uid, MAXIMUM_IDLE, sink)
}

/// The tail, with its idle ceiling supplied.
///
/// The second of the two seams this module needs, and it exists for exactly the
/// reason the first does: `StreamLogSink::with_patience` was built so the 30 s
/// give-up path could be tested rather than reasoned about, and every word of
/// that argument applies to this clock, which is 300 s. Both are give-up clocks
/// for the same blocking-pool thread. Taking the seam for one and not the other
/// left this one able to be disabled outright with no test noticing.
///
/// # Errors
///
/// As [`follow_log`], minus the account resolution.
fn follow_with_patience(
    request: &LogTailRequest,
    uid: u32,
    patience: Duration,
    sink: &mut dyn LogSink,
) -> Result<TailEnd, SitesOpError> {
    // Re-clamped here and not merely trusted from `tail_site_log`. That
    // function makes a point of clamping "HERE and not in the service, so the
    // ceiling cannot be bypassed by calling the operation from somewhere
    // else"; the same argument applies one layer down, and this is the layer
    // that turns the number into a read (rules/rust.md "Validation first").
    let history_lines = request.history_lines.min(MAXIMUM_HISTORY_LINES);

    let directory = open_directory(&request.directory, uid)?;

    let mut offset = match open_log(&directory, &request.file_name, uid)? {
        Some((mut file, metadata)) => {
            if let Some(end) = send_history(
                &mut file,
                &metadata,
                history_lines,
                sink,
                &request.file_name,
            )? {
                return Ok(end);
            }
            // Where the history ended. If the file was truncated during the
            // scan this is now past its end, which the first poll below reads
            // as a rotation and resets.
            metadata.len()
        }
        // No log yet, so nothing to send and nothing to skip past: the follow
        // starts from the beginning of the file that will appear.
        None => 0,
    };

    let mut idle_since = Instant::now();

    loop {
        std::thread::sleep(POLL_INTERVAL);

        // Asked every poll and NOT only when a line arrives. A tail opened on a
        // site with no traffic and then dropped would otherwise poll for the
        // life of the process, holding a pool thread nothing can reclaim.
        if !sink.is_listening() {
            return Ok(TailEnd::ClientClosed);
        }
        if idle_since.elapsed() >= patience {
            return Ok(TailEnd::Idle);
        }

        let Some((mut file, metadata)) = open_log(&directory, &request.file_name, uid)? else {
            continue;
        };

        let (text, skipped) = match window(&mut file, offset, metadata.len(), &request.file_name)? {
            Window::Unchanged => continue,
            Window::Restart => {
                offset = 0;
                continue;
            }
            Window::Ready {
                text,
                next,
                skipped,
            } => {
                offset = next;
                (text, skipped)
            }
        };

        if text.is_empty() {
            continue;
        }
        idle_since = Instant::now();

        if skipped > 0
            && let Err(end) = sink.line(&format!("{SKIPPED_MARKER} {skipped} bytes"), false)
        {
            return Ok(end);
        }
        for line in text.lines() {
            if let Err(end) = sink.line(line, false) {
                return Ok(end);
            }
        }
    }
}

/// What one poll should do with the bytes between where it left off and where
/// the log now ends.
///
/// Three outcomes rather than an `Option`, because the three are genuinely
/// different events and the loop must not confuse them: nothing new, the file
/// is not where we left it, and here are the lines.
#[derive(Debug)]
enum Window {
    /// The log has not grown since the last poll.
    Unchanged,
    /// The log was rotated or truncated. Read it again from its beginning.
    Restart,
    /// The whole lines the poll should deliver.
    Ready {
        /// The text, whole lines only.
        text: String,
        /// The offset the next poll continues from.
        next: u64,
        /// Bytes dropped because the log outran the reader.
        skipped: u64,
    },
}

/// Decides what a poll does, given where it left off and the length it just
/// read from the log's inode.
///
/// **This is the join between the two ways a truncation is noticed, and it is a
/// named function so that the join itself can be tested.** A rotation seen
/// between two polls arrives here as `end < offset`; one seen inside a poll —
/// the `copytruncate` window between the `fstat` and the read — arrives as a
/// short read from [`read_window`]. Both are the same event a step apart and
/// both answer [`Window::Restart`].
///
/// Inline in the loop, both producers had tests and the routing between them
/// did not: turning the short read into an error at the call site left the whole
/// suite green. A function has a name, and a name can be called from a test with
/// a length the file no longer has — which is the entirety of what the race
/// produces here (see [`read_window`]).
///
/// # Errors
///
/// As [`read_window`].
fn window(
    file: &mut File,
    offset: u64,
    end: u64,
    file_name: &OsStr,
) -> Result<Window, SitesOpError> {
    if end == offset {
        return Ok(Window::Unchanged);
    }

    if end < offset {
        return Ok(Window::Restart);
    }

    match read_window(file, offset, end, file_name)? {
        Some((text, next, skipped)) => Ok(Window::Ready {
            text,
            next,
            skipped,
        }),
        // Reporting this instead would kill the operator's tail every night at
        // midnight, when logrotate runs `copytruncate`.
        None => Ok(Window::Restart),
    }
}

/// The one failure this module reports, named after the log it was reading.
fn unreadable(file_name: &OsStr) -> SitesOpError {
    SitesOpError::LogUnreadable {
        path: Path::new(file_name).display().to_string(),
    }
}

/// Opens the log directory, once, and proves it is one the account owns.
///
/// The descriptor returned is what every later open goes through, so this is
/// the only moment the directory is named by a path — and therefore the only
/// moment the path could be swapped.
fn open_directory(directory: &Path, uid: u32) -> Result<File, SitesOpError> {
    let opened = OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC)
        .open(directory)
        .map_err(|_| SitesOpError::LogUnreadable {
            path: directory.display().to_string(),
        })?;

    let metadata = opened.metadata().map_err(|_| SitesOpError::LogUnreadable {
        path: directory.display().to_string(),
    })?;

    // The account's own directory and not, say, one root created and the
    // account then replaced. Ownership is the only claim that survives the
    // account being able to rename things inside its home.
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(SitesOpError::LogUnreadable {
            path: directory.display().to_string(),
        });
    }

    Ok(opened)
}

/// Opens the log through `directory` and proves it is a file worth reading.
///
/// Returns `None` when there is no log yet, which is the ordinary state of a
/// site that has served no request. Every other refusal is an error: a symlink
/// stopped by `O_NOFOLLOW`, a FIFO, a device, a directory, a file the account
/// does not own, or one with a second link — reported rather than quietly
/// treated as "nothing here", because each of those is somebody trying
/// something.
fn open_log(
    directory: &File,
    file_name: &OsStr,
    uid: u32,
) -> Result<Option<(File, Metadata)>, SitesOpError> {
    let file = match open_in_directory(directory, file_name, LOG_FLAGS) {
        Ok(file) => file,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(_) => return Err(unreadable(file_name)),
    };

    let metadata = file.metadata().map_err(|_| unreadable(file_name))?;

    // `is_file` is the FIFO check and the device check and the directory check,
    // all three: `O_NONBLOCK` is what stopped the open of a FIFO from hanging,
    // and this is what stops us reading one. `nlink == 1` is the hardlink
    // check — `ln /etc/shadow access.log` is not a symlink and lives at a path
    // that really is inside the home, so only the inode gives it away.
    if !metadata.is_file() || metadata.uid() != uid || metadata.nlink() != 1 {
        return Err(unreadable(file_name));
    }

    Ok(Some((file, metadata)))
}

/// Sends the last `history_lines` lines, reading backwards within a budget.
///
/// Returns `Some` when the sink ended the tail during the batch, so the caller
/// stops rather than beginning to follow a stream nobody is reading.
///
/// Backwards and not `read_to_end`: the lines wanted are at the end, and the
/// file's size is the account's choice. The scan stops at whichever comes
/// first — enough newlines, [`HISTORY_BUDGET`] bytes, or the start of the file
/// — so the memory this holds is bounded by the budget and not by the log.
fn send_history(
    file: &mut File,
    metadata: &Metadata,
    history_lines: u32,
    sink: &mut dyn LogSink,
    file_name: &OsStr,
) -> Result<Option<TailEnd>, SitesOpError> {
    let end = metadata.len();
    let floor = end.saturating_sub(HISTORY_BUDGET);

    let mut cursor = end;
    let mut buffer: Vec<u8> = Vec::new();
    let mut newlines = 0_u64;

    while cursor > floor && newlines <= u64::from(history_lines) {
        let step = READ_CHUNK.min(usize::try_from(cursor - floor).unwrap_or(READ_CHUNK));
        cursor -= step as u64;

        let mut piece = vec![0_u8; step];
        if !read_exact_at(file, cursor, &mut piece, file_name)? {
            // Truncated underneath the scan. What was already read is real
            // content from further down the file, so it is sent rather than
            // discarded; the follow loop then resets to the new beginning.
            break;
        }

        newlines += piece.iter().filter(|byte| **byte == b'\n').count() as u64;
        piece.extend_from_slice(&buffer);
        buffer = piece;
    }

    // The first line of the window is dropped whenever the scan started
    // mid-file: it is a fragment, and half a log line read as a whole one is
    // worse than one line fewer.
    let text = String::from_utf8_lossy(&buffer);
    let mut lines: Vec<&str> = text.lines().collect();
    if cursor > 0 && !lines.is_empty() {
        lines.remove(0);
    }

    let start = lines.len().saturating_sub(history_lines as usize);
    for line in &lines[start..] {
        if let Err(end) = sink.line(line, true) {
            return Ok(Some(end));
        }
    }

    Ok(None)
}

/// Reads the whole lines between `offset` and `end`, at most
/// [`FOLLOW_CEILING`] of them.
///
/// Returns the text, the offset to continue from, and how many bytes were
/// dropped because the log outran the reader. The offset never advances past
/// the last newline, so a line the web server is still writing is read once,
/// when it is complete, rather than twice in halves.
fn read_window(
    file: &mut File,
    offset: u64,
    end: u64,
    file_name: &OsStr,
) -> Result<Option<(String, u64, u64)>, SitesOpError> {
    // Stated rather than left to be proved by reading the caller: `follow_log`
    // handles `end < offset` and `end == offset` before calling. A root process
    // must not panic on input (rules/rust.md), so the subtraction saturates as
    // well as being asserted — the assertion catches the mistake in a test, the
    // saturation keeps it from becoming a crash in production.
    debug_assert!(end > offset, "read_window requires a non-empty window");
    let available = end.saturating_sub(offset);
    let skipped = available.saturating_sub(FOLLOW_CEILING);
    let begin = offset + skipped;

    let size = usize::try_from(end - begin).unwrap_or(usize::MAX);
    let mut bytes = vec![0_u8; size];
    if !read_exact_at(file, begin, &mut bytes, file_name)? {
        return Ok(None);
    }

    // A window that starts mid-file starts mid-line; drop the fragment.
    let body = if skipped > 0 {
        match bytes.iter().position(|byte| *byte == b'\n') {
            Some(first) => &bytes[first + 1..],
            None => &bytes[..0],
        }
    } else {
        &bytes[..]
    };
    let dropped = bytes.len() - body.len();

    let Some(last) = body.iter().rposition(|byte| *byte == b'\n') else {
        // Only a partial line so far: leave the offset where it was and read it
        // whole on the next poll.
        return Ok(Some((String::new(), offset, 0)));
    };

    // A log line is arbitrary bytes the internet chose — a request path, a user
    // agent — so it is decoded lossily rather than refused: a tail that stops
    // working because one client sent invalid UTF-8 stops working exactly when
    // an operator needs it.
    let text = String::from_utf8_lossy(&body[..=last]).into_owned();

    // `skipped + dropped`, not `skipped`: the fragment discarded at the front
    // of the window is as much a hole in the operator's log as the bytes the
    // window never reached, and the marker exists to say there is a hole.
    Ok(Some((
        text,
        begin + dropped as u64 + last as u64 + 1,
        skipped + dropped as u64,
    )))
}

/// Fills `buffer` from `position`.
///
/// Returns `false` — not an error — when the file ended early. That is a
/// truncation racing the read, which on a web server's log is `logrotate`
/// running `copytruncate` at midnight: an ordinary daily event that the caller
/// answers by starting again from the new beginning. Only a genuine IO failure
/// is an error.
///
/// `read_exact` retries `ErrorKind::Interrupted` itself, so EINTR needs no
/// handling here.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`], naming the log, when the seek or
/// the read fails for any reason other than the file having ended.
fn read_exact_at(
    file: &mut File,
    position: u64,
    buffer: &mut [u8],
    file_name: &OsStr,
) -> Result<bool, SitesOpError> {
    file.seek(SeekFrom::Start(position))
        .map_err(|_| unreadable(file_name))?;

    match file.read_exact(buffer) {
        Ok(()) => Ok(true),
        Err(error) if error.kind() == io::ErrorKind::UnexpectedEof => Ok(false),
        Err(_) => Err(unreadable(file_name)),
    }
}

#[cfg(test)]
#[path = "../tests/sites/follow_log_tests.rs"]
mod tests;
