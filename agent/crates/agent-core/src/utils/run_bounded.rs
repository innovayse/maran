//! The bounded core: run one program under a deadline, in a group, under a cap.

use std::time::Duration;

use crate::command_outcome::CommandOutcome;
use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::spawn_error::SpawnError;
use crate::utils::spawn_request::SpawnRequest;
use crate::utils::supervise_child::supervise_child;
use crate::utils::supervised::Supervised;

/// Runs the program `request` describes and waits for it, bounded in time,
/// reach and size.
///
/// This is what every process this agent starts is run through. For every child
/// it guarantees:
///
/// - **A deadline.** The request's [`crate::utils::call_class::CallClass`] says
///   how long. When it passes, the child's WHOLE process group gets `SIGKILL`
///   while the leader is still uncollected, the leader is collected, and the call
///   returns [`BoundExceeded::TimedOut`]. The kill ends the program and the
///   descendants that stayed in its group; it does not cancel work handed to
///   the service manager or a database server, and a descendant that left the
///   group with its own `setsid` is outside it.
/// - **A group of its own.** The child leads a new process group, so one kill
///   reaches `tar` and the compressor it started, or `apt-get` and its
///   maintainer scripts.
/// - **An explicit standard input** — null, the bytes given, or the file given,
///   never the daemon's own.
/// - **A ceiling on what is kept.** Output past the request's cap is
///   [`BoundExceeded::OutputTooLarge`], the group is killed, and no captured byte
///   is returned, so a parser is never handed a shortened answer.
/// - **The cleared environment**, [`crate::utils::apply_child_environment`]'s
///   and nothing else.
///
/// No shell is involved, at any point (rules/security.md item 3): the arguments
/// reach `execve` one by one. `request.program` must come from the
/// `DistroAdapter`'s allow-list and never from a request; that contract belongs
/// to the caller and is restated here because this is the function that spawns.
///
/// **Threads.** At most one per captured stream and one more to deliver
/// [`crate::utils::child_input::ChildInput::Bytes`]; none for
/// [`crate::utils::child_output::ChildOutput::Discard`] with a null or file
/// input. None is joined past *deadline plus the reap ceiling*, so a reader that
/// a descendant outside the killed group keeps blocked is left to finish on its
/// own: it keeps nothing past its cap, but it is a task for as long as that
/// descendant holds the pipe. This function does not claim every thread ends at
/// the deadline.
///
/// A leader that exited cleanly while a descendant still holds a pipe open is
/// waited for under the same deadline, and counts as timed out when it passes.
///
/// A forked child must not use this function: it starts threads. See
/// `run_unthreaded`.
///
/// The returned `status` is the exit code, or -1 when the child died of a signal
/// the core did NOT send.
///
/// # Errors
///
/// [`SpawnError::Start`] when the program cannot be started;
/// [`SpawnError::Input`] when the input cannot be delivered;
/// [`SpawnError::Wait`] when the child cannot be waited for or a stream cannot
/// be read; [`SpawnError::Bound`] with [`BoundExceeded::TimedOut`] past the
/// deadline or [`BoundExceeded::OutputTooLarge`] past a cap. A program that
/// started and exited non-zero is NOT an error.
pub fn run_bounded(request: SpawnRequest<'_>) -> Result<CommandOutcome, SpawnError> {
    let deadline = request.class.deadline();

    run_until(request, deadline)
}

/// [`run_bounded`] with the deadline supplied.
///
/// The seam exists so the timeout path can be driven in milliseconds: the
/// shortest class is 60 s, and a test that waited that long per case would not
/// be written. Production always arrives here through [`run_bounded`], with the
/// request's class's deadline.
///
/// # Errors
///
/// As documented on [`run_bounded`].
fn run_until(request: SpawnRequest<'_>, deadline: Duration) -> Result<CommandOutcome, SpawnError> {
    let SpawnRequest {
        program,
        arguments,
        class: _,
        input,
        output,
    } = request;

    match supervise_child(program, arguments, input, output, deadline)? {
        Supervised::Finished(outcome) => Ok(outcome),
        Supervised::Expired => Err(SpawnError::Bound(BoundExceeded::TimedOut {
            program: program.display().to_string(),
            deadline,
        })),
    }
}

#[cfg(test)]
#[path = "../tests/utils/run_bounded_tests.rs"]
mod tests;
