//! The one body that turns an argv array into a finished [`CommandOutcome`].

use std::process::Command;

use crate::command_outcome::CommandOutcome;

/// The locale variable every spawn here sets.
///
/// `LC_ALL` and not `LANG`, because `LC_ALL` overrides every other locale
/// variable — one assignment settles the question whatever the daemon's own
/// environment holds. It is public so a test can assert on the child's real
/// environment by name rather than restating the string.
pub const LOCALE_VARIABLE: &str = "LC_ALL";

/// The locale every spawn here runs under.
///
/// `C`, so the diagnostics a caller reads back are the ones its matching was
/// written against. This started as the accounts host's own pin, added because
/// `quota` links gettext and a translated header made its parse silently yield
/// "unlimited", and because `remove_crontab` decides "there was nothing to
/// remove" by reading `crontab`'s own message — a message in another language
/// is a refusal it cannot recognise, which made an account with no crontab
/// undeletable. Nothing sets a locale on the agent's unit, so the daemon's
/// environment decided it.
///
/// It is pinned on the SPAWN rather than per call site, and now on the ONE
/// spawn rather than one file's: every caller that reads a program's output has
/// the same exposure, and a rule honoured at one call site and forgotten at the
/// next is the shape of defect this repository keeps finding.
pub const LOCALE_VALUE: &str = "C";

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
/// The child also runs under [`LOCALE_VARIABLE`]`=`[`LOCALE_VALUE`], for the
/// reason those constants give.
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
    let output = Command::new(program)
        .args(arguments)
        .env(LOCALE_VARIABLE, LOCALE_VALUE)
        .output()?;

    Ok(CommandOutcome {
        // -1 for a process killed by a signal: it did not exit, and reporting
        // 0 would read as success to every caller.
        status: output.status.code().unwrap_or(-1),
        stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
        stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
    })
}
