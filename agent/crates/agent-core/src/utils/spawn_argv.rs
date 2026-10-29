//! The default path from an argv array to a finished [`CommandOutcome`], bounded.

use std::path::Path;

use crate::command_outcome::CommandOutcome;
use crate::utils::call_class::CallClass;
use crate::utils::child_input::ChildInput;
use crate::utils::child_output::ChildOutput;
use crate::utils::run_bounded::run_bounded;
use crate::utils::spawn_error::SpawnError;
use crate::utils::spawn_request::SpawnRequest;

/// Spawns `program` with `arguments` as an argv array and waits for it, under
/// the deadline of `class`.
///
/// No shell is involved, at any point (rules/security.md item 3): the
/// arguments reach `execve` one by one, so there is no command line for
/// anything to re-parse. `program` must come from the `DistroAdapter`'s
/// allow-list and never from a request — that contract belongs to the caller
/// and is restated here because this is the function that spawns.
///
/// This is [`run_bounded`] with the two choices nearly every caller wants
/// already made, which is why it stays the default path:
///
/// - the child's standard input is `/dev/null`, set explicitly (so a tool that
///   decides to prompt fails instead of hanging a root daemon until its
///   deadline, and the daemon's own descriptor 0 is never inherited);
/// - both output streams are captured under
///   [`ChildOutput::DEFAULT_CAP`] each and come back as text, with anything that
///   is not UTF-8 replaced rather than refused: a diagnostic is for an operator
///   to read, and losing the whole message over one stray byte helps nobody.
///
/// The child leads a process group of its own, and the whole group is killed at
/// the deadline. The environment is `apply_child_environment`'s and nothing
/// else: the daemon's own environment is CLEARED and replaced by the entries that
/// function names, so no `LD_PRELOAD`, `BASH_ENV` or `TAR_OPTIONS` the unit or
/// `/etc/maran/agent.env` happens to carry reaches anything this agent starts.
///
/// A caller whose child needs input bytes, a file for standard input, a larger
/// or no capture, or whose end of a window means success, builds a
/// [`SpawnRequest`] and calls [`run_bounded`] (or `run_for_window`) itself. They
/// keep what makes them different; the wait is the same one.
///
/// # Errors
///
/// [`SpawnError::Start`] when the program cannot be started — not found, not
/// executable, fork refused. A deadline that passes or output that exceeds the
/// cap is [`SpawnError::Bound`]: the group was killed, and the caller gets no
/// outcome to parse. A program that started and exited non-zero is NOT an error
/// here: its status is the caller's domain decision, so it comes back as a
/// [`CommandOutcome`] like any success.
pub fn spawn_argv(
    program: &Path,
    arguments: &[&str],
    class: CallClass,
) -> Result<CommandOutcome, SpawnError> {
    run_bounded(SpawnRequest {
        program,
        arguments,
        class,
        input: ChildInput::Null,
        output: ChildOutput::DEFAULT,
    })
}

#[cfg(test)]
#[path = "../tests/utils/spawn_argv_tests.rs"]
mod tests;
