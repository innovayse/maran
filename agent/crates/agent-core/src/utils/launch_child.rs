//! The one place the core starts a child process.

use std::os::unix::process::CommandExt;
use std::path::Path;
use std::process::{Child, Command, Stdio};

use crate::utils::apply_child_environment::apply_child_environment;

/// Starts `program` with `arguments` as the leader of a process group of its own.
///
/// This is the single `Command::new` of the workspace. What it guarantees for
/// every child, whichever core entry point asked:
///
/// - **The environment is [`apply_child_environment`]'s and nothing else.**
/// - **The child leads a new process group** (`process_group(0)`), so a kill of
///   the group reaches the child and every descendant that stayed in it. This is
///   a safe `std` call: the group is set up in the child between fork and exec by
///   the standard library, not by code here.
/// - **All three standard streams are stated by the caller.** Nothing is
///   inherited by default; a caller that passes `Stdio::inherit()` does so by
///   name.
///
/// No shell is involved: the arguments reach `execve` one by one.
///
/// # Errors
///
/// Returns the `io::Error` of failing to start the program: not found, not
/// executable, the fork refused.
pub(crate) fn launch_child(
    program: &Path,
    arguments: &[&str],
    stdin: Stdio,
    stdout: Stdio,
    stderr: Stdio,
) -> std::io::Result<Child> {
    let mut command = Command::new(program);
    apply_child_environment(&mut command);
    command
        .args(arguments)
        .process_group(0)
        .stdin(stdin)
        .stdout(stdout)
        .stderr(stderr)
        .spawn()
}

#[cfg(test)]
#[path = "../tests/utils/launch_child_tests.rs"]
mod tests;
