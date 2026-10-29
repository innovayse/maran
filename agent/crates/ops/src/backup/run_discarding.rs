//! Runs a program under a call class with its output discarded.

use std::path::Path;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::child_output::ChildOutput;
use maran_agent_core::utils::run_bounded::run_bounded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_agent_core::utils::spawn_request::SpawnRequest;

/// Runs `program` under `class`'s deadline with both output streams discarded,
/// and `input` as its standard input.
///
/// The shared body of the four spawns in this host whose output nothing reads:
/// the dump, the archive, the drop and the load. [`ChildOutput::Discard`] sends
/// both streams to the null device, so the returned outcome carries only the
/// exit status.
///
/// # Errors
///
/// The core's [`SpawnError`]; `bound_or` turns it into this area's error.
pub(super) fn run_discarding(
    program: &Path,
    arguments: &[&str],
    class: CallClass,
    input: ChildInput<'_>,
) -> Result<CommandOutcome, SpawnError> {
    run_bounded(SpawnRequest {
        program,
        arguments,
        class,
        input,
        output: ChildOutput::Discard,
    })
}
