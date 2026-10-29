//! The loop that waits for one child under a deadline and an output cap.

use std::path::Path;
use std::process::{Child, Stdio};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::time::Duration;

use rustix::process::{Pid, Signal, kill_process_group};

use crate::command_outcome::CommandOutcome;
use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::caps::Caps;
use crate::utils::child_event::ChildEvent;
use crate::utils::child_input::ChildInput;
use crate::utils::child_output::ChildOutput;
use crate::utils::deadline::Deadline;
use crate::utils::has_exited::has_exited;
use crate::utils::kill_group_and_reap::kill_group_and_reap;
use crate::utils::launch_child::launch_child;
use crate::utils::output_stream::OutputStream;
use crate::utils::poll_interval::PollInterval;
use crate::utils::spawn_error::SpawnError;
use crate::utils::start_reader::start_reader;
use crate::utils::start_writer::start_writer;
use crate::utils::supervised::Supervised;

/// Runs `program` as the leader of a new process group and waits for it.
///
/// This is the body behind `run_bounded` and `run_for_window`; the entry points
/// differ only in what they make of [`Supervised::Expired`].
///
/// What it guarantees:
///
/// - The input is stated: `/dev/null`, a pipe the core writes, or a file.
/// - The environment is `apply_child_environment`'s and nothing else.
/// - Both streams are drained concurrently, each by its own thread, and a
///   stream that carries more than its cap ends the call with
///   [`BoundExceeded::OutputTooLarge`] after the group is killed; no captured
///   byte is returned. [`ChildOutput::Discard`] starts no reader at all.
/// - The wait has a deadline. When it passes, the whole group is killed while
///   the leader is still uncollected, then the leader is collected under a
///   ceiling of its own. The deadline covers the drain as well as the exit: a
///   leader that exited cleanly while a descendant still holds a pipe open is
///   waited for until the deadline, and then counts as expired.
/// - **No thread is joined.** Readers and the writer are detached. One that a
///   descendant outside the killed group keeps blocked outlives the call and is
///   left to finish by itself, so the call returns within *deadline plus the reap
///   ceiling* whatever that descendant does. Threads therefore do NOT all end at
///   the deadline: a leaked reader retains nothing past its cap, but it is a task
///   until the descendant lets go of the pipe.
/// - Threads per call: at most one per captured stream and one writer for
///   [`ChildInput::Bytes`].
/// - **The child's own verdict outranks a failed delivery.** A write that fails
///   (a broken pipe because the child exited without reading) does not end the
///   wait: it is remembered, and the child is waited for as usual, so a program
///   that exits non-zero speaks for itself through its status and its standard
///   error. Only a child that exits `0` having NOT received its input is an
///   input failure, because reporting success for a password never delivered
///   would be wrong. A child that closes its input but keeps running is bounded
///   by the deadline like any other.
/// - When the leader has exited and every stream has ended, the group is killed
///   once more before the call returns, so a descendant that closed its pipes
///   and stayed behind does not outlive the call. A descendant that left the
///   group escapes this, as it escapes every kill here.
///
/// A leader that survives `SIGKILL` for the whole reap ceiling is left
/// uncollected (a zombie until the agent exits) and the bound that caused the
/// kill is reported all the same.
///
/// # Errors
///
/// [`SpawnError::Start`] when the program cannot be started;
/// [`SpawnError::Input`] when the input cannot be delivered AND the child did
/// not give a non-zero verdict of its own (it exited `0`);
/// [`SpawnError::Wait`] when the child cannot be waited for, a stream cannot be
/// read or a helper thread cannot be started; [`SpawnError::Bound`] with
/// `OutputTooLarge` when a stream crosses its cap. In every error case after the child
/// started, except the final failure to collect an already exited child, the
/// group has been killed; [`SpawnError::Start`] has no group to kill.
pub(crate) fn supervise_child(
    program: &Path,
    arguments: &[&str],
    input: ChildInput<'_>,
    output: ChildOutput,
    deadline: Duration,
) -> Result<Supervised, SpawnError> {
    let name = program.display().to_string();

    let (stdin, to_write) = match input {
        ChildInput::Null => (Stdio::null(), None),
        ChildInput::Bytes(bytes) => (Stdio::piped(), Some(bytes.to_vec())),
        ChildInput::File(file) => (Stdio::from(file), None),
    };
    let (stdout, stderr, caps) = match output {
        ChildOutput::Capture {
            stdout_cap,
            stderr_cap,
        } => (
            Stdio::piped(),
            Stdio::piped(),
            Some(Caps {
                stdout: stdout_cap,
                stderr: stderr_cap,
            }),
        ),
        ChildOutput::Discard => (Stdio::null(), Stdio::null(), None),
    };

    let mut child = launch_child(program, arguments, stdin, stdout, stderr).map_err(|error| {
        SpawnError::Start {
            program: name.clone(),
            reason: error.to_string(),
        }
    })?;
    let limit = Deadline::after(deadline);

    let (sender, events) = mpsc::channel::<ChildEvent>();
    let pending = match start_helpers(&mut child, caps, to_write, &sender) {
        Ok(pending) => pending,
        Err(reason) => {
            let _ = kill_group_and_reap(&mut child);
            return Err(SpawnError::Wait {
                program: name,
                reason,
            });
        }
    };

    let waited = wait_for_child(&mut child, &events, pending, limit, caps, &name);
    // Held until here so the channel stays open for the whole wait: a sender that
    // dropped early would turn the timed receive into an instant error.
    drop(sender);

    match waited? {
        Waited::Expired => Ok(Supervised::Expired),
        Waited::Done {
            stdout,
            stderr,
            input_failed,
        } => {
            // The leader is still uncollected here (`has_exited` does not reap),
            // so the group id is still the child's and the signal cannot reach
            // an unrelated group. Best effort: ESRCH means nothing is left.
            let _ = kill_process_group(Pid::from_child(&child), Signal::KILL);

            let status = match child.try_wait() {
                Ok(Some(status)) => status,
                Ok(None) | Err(_) => {
                    return Err(SpawnError::Wait {
                        program: name,
                        reason: "the exited child could not be collected".to_owned(),
                    });
                }
            };

            // A child that exited 0 without reading its input did not do what
            // it was asked; any other status is the child's own verdict and
            // reaches the caller as an outcome.
            if input_failed && status.code() == Some(0) {
                return Err(SpawnError::Input { program: name });
            }

            Ok(Supervised::Finished(CommandOutcome {
                // -1 for a process killed by a signal the core did not send: it
                // did not exit, and reporting 0 would read as success.
                status: status.code().unwrap_or(-1),
                stdout: decode(stdout),
                stderr: decode(stderr),
            }))
        }
    }
}

/// Turns captured bytes into text, replacing invalid UTF-8 with U+FFFD.
///
/// Valid UTF-8, which is nearly every capture, is adopted without a copy: a
/// lossy conversion of a borrowed slice copies even when nothing needs
/// replacing, and for a capture near its cap that is a second buffer of the same
/// size alive beside the first. Only invalid input pays for a lossy copy.
fn decode(bytes: Vec<u8>) -> String {
    String::from_utf8(bytes)
        .unwrap_or_else(|error| String::from_utf8_lossy(error.as_bytes()).into_owned())
}

/// What the wait loop ended with, before the leader is collected.
enum Waited {
    /// The deadline passed; the group is already killed and collected.
    Expired,
    /// The child exited and every stream ended; these are the captured bytes.
    Done {
        /// Everything on standard output.
        stdout: Vec<u8>,
        /// Everything on standard error.
        stderr: Vec<u8>,
        /// The input could not be delivered; the child may still have run to a
        /// verdict of its own, which the caller weighs against this.
        input_failed: bool,
    },
}

/// Starts the reader threads and the writer thread the request needs.
///
/// Returns how many output streams are still to report. On failure the reason is
/// returned and the caller kills the group.
fn start_helpers(
    child: &mut Child,
    caps: Option<Caps>,
    to_write: Option<Vec<u8>>,
    sender: &Sender<ChildEvent>,
) -> Result<usize, String> {
    let mut pending = 0;

    if let Some(caps) = caps {
        for stream in [OutputStream::Stdout, OutputStream::Stderr] {
            let cap = caps.of(stream);
            let started = match stream {
                OutputStream::Stdout => child
                    .stdout
                    .take()
                    .map(|pipe| start_reader(pipe, stream, cap, sender.clone())),
                OutputStream::Stderr => child
                    .stderr
                    .take()
                    .map(|pipe| start_reader(pipe, stream, cap, sender.clone())),
            };
            match started {
                Some(Ok(())) => pending += 1,
                Some(Err(error)) => {
                    return Err(format!("could not start a reader for {stream}: {error}"));
                }
                None => return Err(format!("{stream} was not piped")),
            }
        }
    }

    if let Some(bytes) = to_write {
        let Some(pipe) = child.stdin.take() else {
            return Err("standard input was not piped".to_owned());
        };
        if let Err(error) = start_writer(pipe, bytes, sender.clone()) {
            return Err(format!("could not start the input writer: {error}"));
        }
        // The writer reports exactly once, so the wait counts it like a reader:
        // an input failure that lands after the child exited is still seen.
        pending += 1;
    }

    Ok(pending)
}

/// Waits until the child has exited and every captured stream has ended, or the
/// deadline passes, or something goes wrong.
///
/// The leader's exit is observed without collecting it (`has_exited`), so a
/// group kill is always sent while the group id is still the child's. Any
/// failure kills the group and collects the leader before it returns.
fn wait_for_child(
    child: &mut Child,
    events: &Receiver<ChildEvent>,
    mut pending: usize,
    limit: Deadline,
    caps: Option<Caps>,
    name: &str,
) -> Result<Waited, SpawnError> {
    let mut stdout = Vec::new();
    let mut stderr = Vec::new();
    let mut exited = false;
    let mut input_failed = false;
    let mut interval = PollInterval::new();

    loop {
        if exited && pending == 0 {
            return Ok(Waited::Done {
                stdout,
                stderr,
                input_failed,
            });
        }

        if limit.has_passed() {
            let _ = kill_group_and_reap(child);
            return Ok(Waited::Expired);
        }

        match events.recv_timeout(interval.next_nap()) {
            Ok(ChildEvent::Output { stream, bytes }) => {
                pending = pending.saturating_sub(1);
                match stream {
                    OutputStream::Stdout => stdout = bytes,
                    OutputStream::Stderr => stderr = bytes,
                }
            }
            Ok(ChildEvent::Overflow(stream)) => {
                let _ = kill_group_and_reap(child);
                return Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
                    program: name.to_owned(),
                    stream,
                    cap: caps.map_or(0, |caps| caps.of(stream)),
                }));
            }
            Ok(ChildEvent::ReadFailed(stream)) => {
                let _ = kill_group_and_reap(child);
                return Err(SpawnError::Wait {
                    program: name.to_owned(),
                    reason: format!("{stream} could not be read"),
                });
            }
            // Remembered, not acted on: a broken pipe usually means the child
            // exited, and its own status is the better answer. The wait goes on
            // under the same deadline.
            Ok(ChildEvent::InputFailed) => {
                pending = pending.saturating_sub(1);
                input_failed = true;
            }
            Ok(ChildEvent::InputDelivered) => pending = pending.saturating_sub(1),
            // No event inside the nap, or no sender left: either way the next
            // look at the clock and at the child decides. The channel cannot be
            // closed while this function runs, because the caller holds a sender.
            Err(RecvTimeoutError::Timeout | RecvTimeoutError::Disconnected) => {}
        }

        if !exited {
            match has_exited(child) {
                Ok(done) => exited = done,
                Err(error) => {
                    let _ = kill_group_and_reap(child);
                    return Err(SpawnError::Wait {
                        program: name.to_owned(),
                        reason: error.to_string(),
                    });
                }
            }
        }
    }
}
