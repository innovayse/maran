//! What a process that was fed the private key is allowed to tell us.

use std::fmt;

/// The result of running a tool whose standard input was the private key.
///
/// A separate type from `CommandOutcome`, and a deliberately crippled one: it
/// carries **no stderr at all**, and its stdout can only be compared, never
/// read. That is the invariant this task's review demanded be moved out of
/// discipline and into the type system — *the output of a process whose stdin
/// was the key is never attached to an error*.
///
/// The previous shape of this rule was a redaction filter applied by hand at
/// each call site: a blacklist that had to guess how openssl might echo a key
/// (base64 at one column width, or the hex primes `openssl pkey -text` prints),
/// that failed open when it guessed wrong, and that had to be remembered. This
/// type cannot be forgotten: there is no `String` on it for a `{}` to reach, so
/// no error variant, log line or panic message can carry what the tool saw.
///
/// stderr is dropped at construction, in the one file that spawns processes, so
/// it does not exist by the time any operation could format it.
pub struct KeyToolOutcome {
    /// Whether the tool exited zero.
    succeeded: bool,
    /// What it printed. Reachable only through [`Self::output_matches`].
    stdout: String,
}

impl KeyToolOutcome {
    /// Records an outcome, discarding the tool's stderr.
    #[must_use]
    pub fn new(succeeded: bool, stdout: String) -> Self {
        Self { succeeded, stdout }
    }

    /// Whether the tool exited zero.
    #[must_use]
    pub fn succeeded(&self) -> bool {
        self.succeeded
    }

    /// Whether the tool printed exactly `other`, ignoring surrounding
    /// whitespace.
    ///
    /// A comparison and not an accessor. The one question the agent asks of a
    /// key-fed tool is "does this equal the public key the certificate implies?"
    /// — a boolean, which cannot leak anything. Two tools are also not obliged
    /// to agree about a trailing newline, so a mismatch invented by whitespace
    /// would refuse a perfectly good pair.
    #[must_use]
    pub fn output_matches(&self, other: &str) -> bool {
        self.stdout.trim() == other.trim()
    }
}

impl fmt::Debug for KeyToolOutcome {
    /// Prints the status and nothing else.
    ///
    /// Hand-written for the same reason `CertificateMaterial`'s is: a derive
    /// would put the tool's output into the first `dbg!` or `tracing` field
    /// anyone ever writes, and that output was produced from the key.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("KeyToolOutcome")
            .field("succeeded", &self.succeeded)
            .field("stdout", &"<never read>")
            .finish()
    }
}
