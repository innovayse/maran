//! What running one program produced.

use core::fmt;

/// What running one program produced.
///
/// Shared by every `ops` area that spawns a process and reads its result —
/// `accounts` (`useradd`, `usermod`, …) and `safe_write` (validators,
/// reloads) both need exactly this shape, which is what moved it here rather
/// than letting each area keep its own copy (rules/rust.md "Operation
/// anatomy": "a type needed by two areas moves to `agent-core`; areas never
/// import each other").
///
/// **`Debug` is written by hand and prints no captured byte.** The derived one
/// printed `stdout` and `stderr` in full, and this is the workspace's most
/// widely handled value: every area that spawns anything holds one. Two of the
/// call sites decide the question on their own — `ops::accounts` puts
/// `getent shadow <account>`'s standard output here, which is the account's
/// entire shadow entry INCLUDING its password hash, and `ops::cron` puts a
/// customer's whole crontab here. Nothing formats an outcome today. That is a
/// fact about the current call sites, not a property of the type, and it is
/// precisely the leak `SecretString`'s own documentation describes: nobody logs
/// a hash on purpose, they log a struct that holds one.
///
/// [`crate::secret_string::SecretString`] is deliberately NOT used for the two
/// fields. Its whole guarantee is that `expose` is the single greppable reader,
/// and these fields are read by roughly a dozen parsers across `ops`; wrapping
/// them would replace one guarantee with a dozen `expose` calls and destroy the
/// property that type exists for. A redacting `Debug` costs the readers nothing
/// and covers every one of them, including the ones not written yet.
#[derive(Clone, PartialEq, Eq)]
pub struct CommandOutcome {
    /// The exit status, or -1 when the process was killed by a signal.
    pub status: i32,
    /// Everything the program wrote to standard output.
    pub stdout: String,
    /// Everything the program wrote to standard error.
    pub stderr: String,
}

impl fmt::Debug for CommandOutcome {
    /// Writes the status and the two capture LENGTHS, and never a captured byte.
    ///
    /// Lengths rather than nothing at all, because the question a `{:?}` of an
    /// outcome is usually asked to answer is "did the tool say anything?", and a
    /// byte count answers it without quoting what was said. A field printed as a
    /// count also cannot be mistaken for the content by a reader skimming a log.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("CommandOutcome")
            .field("status", &self.status)
            .field("stdout_len", &self.stdout.len())
            .field("stderr_len", &self.stderr.len())
            .finish()
    }
}

#[cfg(test)]
#[path = "tests/command_outcome_tests.rs"]
mod tests;
