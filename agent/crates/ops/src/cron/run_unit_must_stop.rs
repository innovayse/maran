//! Whether a finished run left its unit running.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::spawn_error::SpawnError;

/// Whether the answer of the `systemd-run` client means the agent must stop the
/// run's unit itself (issue #336).
///
/// True for the two ways the BOUNDED CORE ends a client: it passed its output
/// ceiling, or it outlived its own deadline. Either way the core killed the
/// client's process group, and the client is not the job — the job is a
/// separate unit the service manager runs, and it keeps running after its
/// client is gone, until `RuntimeMaxSec`.
///
/// False for every other answer. A client that returned has either waited for
/// the job to end (`--wait`) or reported that it could not start one; and a
/// client that could not be started at all started nothing. In none of those
/// is there a job the agent knows to be running.
pub(crate) fn run_unit_must_stop(answer: &Result<CommandOutcome, SpawnError>) -> bool {
    matches!(answer, Err(SpawnError::Bound(_)))
}

#[cfg(test)]
#[path = "../tests/cron/run_unit_must_stop_tests.rs"]
mod tests;
