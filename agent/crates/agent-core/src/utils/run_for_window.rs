//! Running a program for a window, where outliving the window is success.

use std::path::Path;
use std::time::Duration;

use crate::utils::child_input::ChildInput;
use crate::utils::child_output::ChildOutput;
use crate::utils::spawn_error::SpawnError;
use crate::utils::supervise_child::supervise_child;
use crate::utils::supervised::Supervised;
use crate::utils::window_outcome::WindowOutcome;

/// Runs `program` with `arguments` for at most `window`, and reports whether it
/// ended inside it.
///
/// The inverse of [`crate::utils::run_bounded::run_bounded`]: a daemon
/// candidate that is still running when the window closes has read its
/// configuration, bound its socket and had no objection, so reaching the end of
/// the window is the SUCCESS case, [`WindowOutcome::Outlived`]. The child's whole
/// process group is killed at that point and the leader collected, so nothing the
/// candidate started survives it.
///
/// The child's input is `/dev/null`. Output is captured under `output` (an
/// overflow is an error, not a shortened answer); it is returned only when the
/// program ended inside the window, and discarded with the group when it did not.
///
/// All the guarantees of `run_bounded` hold: a group of its own, the cleared
/// environment, no joined thread past the window plus the reap ceiling.
///
/// # Errors
///
/// [`SpawnError::Start`] when the program cannot be started;
/// [`SpawnError::Wait`] when the child cannot be waited for or a stream cannot
/// be read; [`SpawnError::Bound`] with `OutputTooLarge` when a stream crosses its
/// cap. Reaching the end of the window is not an error.
pub fn run_for_window(
    program: &Path,
    arguments: &[&str],
    output: ChildOutput,
    window: Duration,
) -> Result<WindowOutcome, SpawnError> {
    match supervise_child(program, arguments, ChildInput::Null, output, window)? {
        Supervised::Finished(outcome) => Ok(WindowOutcome::Exited(outcome)),
        Supervised::Expired => Ok(WindowOutcome::Outlived),
    }
}

#[cfg(test)]
#[path = "../tests/utils/run_for_window_tests.rs"]
mod tests;
