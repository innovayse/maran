//! The bounded core for a process that must not start a thread and whose standard
//! output goes to a file.

use std::fs::File;
use std::path::Path;
use std::process::Stdio;
use std::time::Duration;

use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::deadline::Deadline;
use crate::utils::has_exited::has_exited;
use crate::utils::kill_group_and_reap::kill_group_and_reap;
use crate::utils::launch_child::launch_child;
use crate::utils::output_stream::OutputStream;
use crate::utils::poll_interval::PollInterval;
use crate::utils::spawn_error::SpawnError;

/// Runs `program` with `arguments` to its exit code, under `deadline`, without
/// starting a thread, writing its standard output to `output` and refusing more
/// than `cap` bytes of it.
///
/// The twin of `run_unthreaded` for a child whose product is a stream too large
/// to hold in memory (a database dump): standard output is the file, standard
/// error is the null device, and the wait is the same poll with a growing nap.
/// The file's length is read on every poll, so a child that writes past `cap` is
/// killed with its whole process group within one nap of crossing it, and the
/// answer is [`BoundExceeded::OutputTooLarge`], never a file that merely stops
/// growing. The file is the caller's: this function does not truncate it, read it
/// or remove it, and a refused run leaves whatever was written for the caller to
/// delete.
///
/// - The child leads a process group of its own, killed whole at the deadline or
///   at the cap.
/// - `stdin` is the caller's, taken as a [`Stdio`] (the null device for a
///   command that reads nothing).
/// - The environment is `apply_child_environment`'s and nothing else.
///
/// Returns the exit code, or -1 when the child died of a signal the core did not
/// send.
///
/// # Errors
///
/// [`SpawnError::Start`] when the file cannot be duplicated or the program cannot
/// be started; [`SpawnError::Wait`] when the child cannot be waited for;
/// [`SpawnError::Bound`] with [`BoundExceeded::TimedOut`] when the deadline
/// passes and with [`BoundExceeded::OutputTooLarge`] when the file grows past
/// `cap`, in both cases after the group has been killed.
pub fn run_unthreaded_to_file(
    program: &Path,
    arguments: &[&str],
    stdin: Stdio,
    output: &File,
    cap: u64,
    deadline: Duration,
) -> Result<i32, SpawnError> {
    let name = program.display().to_string();
    let start_error = |reason: String| SpawnError::Start {
        program: name.clone(),
        reason,
    };
    let stdout = output
        .try_clone()
        .map_err(|error| start_error(error.to_string()))?;

    let mut child = launch_child(
        program,
        arguments,
        stdin,
        Stdio::from(stdout),
        Stdio::null(),
    )
    .map_err(|error| start_error(error.to_string()))?;
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

        if output.metadata().is_ok_and(|metadata| metadata.len() > cap) {
            let _ = kill_group_and_reap(&mut child);
            return Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
                program: name,
                stream: OutputStream::Stdout,
                cap,
            }));
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

    // The child may have written its last bytes between the final poll and its
    // exit; a file over the cap is refused whatever the exit status says.
    if output.metadata().is_ok_and(|metadata| metadata.len() > cap) {
        return Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
            program: name,
            stream: OutputStream::Stdout,
            cap,
        }));
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
#[path = "../tests/utils/run_unthreaded_to_file_tests.rs"]
mod tests;
