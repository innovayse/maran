//! The bounded core for a process that must not start a thread.

use std::path::Path;
use std::process::Stdio;
use std::time::Duration;

use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::deadline::Deadline;
use crate::utils::has_exited::has_exited;
use crate::utils::kill_group_and_reap::kill_group_and_reap;
use crate::utils::launch_child::launch_child;
use crate::utils::poll_interval::PollInterval;
use crate::utils::spawn_error::SpawnError;

/// Runs `program` with `arguments` to its exit code, under `deadline`, without
/// starting a thread.
///
/// For a forked child (the account-side archive extraction inside
/// `fork_as_account`) and for the root-side extraction. Starting reader threads
/// after a fork of a multi-threaded process is exactly what the fork discipline
/// forbids, so this entry point has none: standard output and standard error go
/// to `/dev/null`, and the wait is a poll with a growing nap.
///
/// - The child leads a process group of its own and the whole group is killed at
///   the deadline, with the leader still uncollected. A caller that forks (the
///   account-side extraction) must give this a deadline strictly below the
///   fork's own patience, so that THIS kill, which reaches the archiver and its
///   compressor, is the one that fires; the fork's kill reaches only its own
///   child. If the fork ever kills its child's group instead, it must also kill
///   this inner group, or make the forked child the leader and stop giving the
///   archiver a group of its own.
/// - `stdin` is the caller's, taken as a [`Stdio`] on purpose: the extraction's
///   input IS the daemon's own descriptor 0 (the artifact), which is the one case
///   where inheriting is the intent. Every other caller passes a file it opened
///   or `Stdio::null()`.
/// - The environment is `apply_child_environment`'s and nothing else.
///
/// Returns the exit code, or -1 when the child died of a signal the core did not
/// send.
///
/// # Errors
///
/// [`SpawnError::Start`] when the program cannot be started;
/// [`SpawnError::Wait`] when the child cannot be waited for;
/// [`SpawnError::Bound`] with [`BoundExceeded::TimedOut`] when the deadline
/// passes, after the group has been killed.
pub fn run_unthreaded(
    program: &Path,
    arguments: &[&str],
    stdin: Stdio,
    deadline: Duration,
) -> Result<i32, SpawnError> {
    let name = program.display().to_string();

    let mut child =
        launch_child(program, arguments, stdin, Stdio::null(), Stdio::null()).map_err(|error| {
            SpawnError::Start {
                program: name.clone(),
                reason: error.to_string(),
            }
        })?;
    let limit = Deadline::after(deadline);
    let mut interval = PollInterval::new();

    loop {
        match has_exited(&child) {
            Ok(true) => break,
            Ok(false) => {}
            Err(error) => {
                let _ = kill_group_and_reap(&mut child);
                return Err(SpawnError::Wait {
                    program: name,
                    reason: error.to_string(),
                });
            }
        }

        if limit.has_passed() {
            let _ = kill_group_and_reap(&mut child);
            return Err(SpawnError::Bound(BoundExceeded::TimedOut {
                program: name,
                deadline,
            }));
        }

        std::thread::sleep(interval.next_nap());
    }

    match child.try_wait() {
        Ok(Some(status)) => Ok(status.code().unwrap_or(-1)),
        Ok(None) | Err(_) => Err(SpawnError::Wait {
            program: name,
            reason: "the exited child could not be collected".to_owned(),
        }),
    }
}

#[cfg(test)]
#[path = "../tests/utils/run_unthreaded_tests.rs"]
mod tests;
