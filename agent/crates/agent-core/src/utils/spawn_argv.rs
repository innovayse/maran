//! The one body that turns an argv array into a finished [`CommandOutcome`].

use std::process::Command;

use crate::command_outcome::CommandOutcome;
use crate::utils::apply_child_environment::apply_child_environment;

/// Spawns `program` with `arguments` as an argv array and waits for it.
///
/// No shell is involved, at any point (rules/security.md item 3): the
/// arguments reach `execve` one by one, so there is no command line for
/// anything to re-parse. `program` must come from the `DistroAdapter`'s
/// allow-list and never from a request — that contract belongs to the caller
/// and is restated here because this is the function that spawns.
///
/// The child's standard input is closed — that is what `Command::output` gives
/// a child it was not asked to pipe to — so a tool that decides to prompt fails
/// instead of hanging a root daemon forever. Both output streams are captured
/// and come back as text, with anything that is not UTF-8 replaced rather than
/// refused: a diagnostic is for an operator to read, and losing the whole
/// message over one stray byte helps nobody.
///
/// The child's environment is [`apply_child_environment`]'s and nothing else:
/// the daemon's own environment is CLEARED and replaced by the two entries
/// that function names, so no `LD_PRELOAD`, `BASH_ENV` or `TAR_OPTIONS` the
/// unit or `/etc/maran/agent.env` happens to carry reaches anything this agent
/// starts.
///
/// Callers whose child needs stdin (`chpasswd`, `openssl`) or whose output must
/// be read bounded (the database client) do NOT belong here — those spawns
/// are deliberately different and stay beside their owners.
///
/// # Errors
///
/// Returns the `io::Error` of failing to START the program — not found, not
/// executable, fork refused. A program that started and exited non-zero is
/// NOT an error here: its status is the caller's domain decision, so it comes
/// back as a [`CommandOutcome`] like any success.
pub fn spawn_argv(program: &str, arguments: &[&str]) -> std::io::Result<CommandOutcome> {
    let mut command = Command::new(program);
    apply_child_environment(&mut command);
    let output = command.args(arguments).output()?;

    Ok(CommandOutcome {
        // -1 for a process killed by a signal: it did not exit, and reporting
        // 0 would read as success to every caller.
        status: output.status.code().unwrap_or(-1),
        stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
        stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
    })
}
