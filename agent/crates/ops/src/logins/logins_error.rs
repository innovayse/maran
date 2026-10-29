//! Failures of the file-transfer login enumeration and its lock.

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

/// The `code` reported when a program could not be started at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `SpawnFailed { code: -1 }` knows the
/// tool never ran, rather than looking up status -1 in its manual.
const PROGRAM_UNAVAILABLE: i32 = -1;

/// What can go wrong while observing or locking an account's logins.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"), and a
/// deliberately narrow one: **no variant carries a program's output**. Every
/// payload is an `i32` or a [`BoundExceeded`], which names the program and the
/// bound that was hit and never a byte the program printed. The
/// realistic leak here is not a careless log line — it is a tool quoting back
/// the line it refused, which in this area's neighbourhood contains a
/// customer's password in full. A shape with no field for a tool's text cannot
/// hold that (rules/security.md item 8).
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum LoginsError {
    /// The password database could not be read, or holds no row for the
    /// hosting account.
    ///
    /// Both are this one variant because both mean the same thing to a caller:
    /// the question was not answered. An account with no logins is an empty
    /// answer, never this — the distinction that matters is "nothing was
    /// found" against "nothing could be looked at", and only the second is an
    /// error.
    ///
    /// A missing account row is an error rather than an enumeration that
    /// carries on, because the row is where the account's uid comes from, and
    /// without the uid the unmanaged count could only be a hard zero — the
    /// blind answer that reads as "there was nothing else".
    #[error("the hosting account could not be read from the password database")]
    AccountMissing,

    /// A tool refused, or could not be run at all.
    ///
    /// Carries the exit status and nothing else — see the note on the enum for
    /// why there is no room for the output beside it.
    #[error("a login tool failed with status {code}")]
    SpawnFailed {
        /// The tool's exit status, or `-1` when it could not be started at all.
        code: i32,
    },

    /// `passwd -S` printed something this agent cannot read as an answer about
    /// the login it asked about.
    ///
    /// Its own variant and never folded into a `false`: "not locked" and
    /// "unreadable" are the same value to any caller that guesses, and the
    /// guess would certify a suspension nobody observed.
    #[error("the password status could not be read")]
    StatusUnreadable,

    /// `pkill` answered with neither "signalled" nor "nothing matched" while
    /// ending a suspended account's sessions.
    ///
    /// A failure and never a shrug: some of the account's processes may have
    /// been signalled and some may not, so the caller cannot report a suspension
    /// it has not observed. The remedy is to re-issue the suspension, which is
    /// idempotent — and safe, because the logins are already locked by the time
    /// the cull runs, so a failed cull leaves an account that refuses new logins
    /// rather than one whose access was restored.
    #[error("the session cull failed with status {code}")]
    SessionCullFailed {
        /// `pkill`'s exit status.
        code: i32,
    },

    /// Another operation for the hosting account is already running on this
    /// host.
    ///
    /// The per-account lock (`crate::accounts::account_lock`) is taken without
    /// waiting, so locking an account's logins while that account is being
    /// deleted, backed up or restored is refused rather than queued. Nothing
    /// was locked or unlocked, and the panel's answer is to retry.
    #[error("another operation is already running for the hosting account")]
    AccountBusy,

    /// The name is not a hosting account this panel may act on.
    ///
    /// Raised before any login is locked or any process is signalled, for
    /// every way the managed-account proof can refuse: a reserved name, an id
    /// below the distro's floor (uid 0 included) and a home other than the one
    /// this agent gives a hosting account. They are ONE variant with one fixed
    /// sentence on purpose: separate variants would let a caller tell a reserved
    /// name from a system identity from a wrong home by the message, which is
    /// the rule-shape oracle the threat note forbids. A refusal added to the
    /// type later lands here too, so it fails closed.
    #[error("the account is not a hosting account")]
    AccountNotManaged,

    /// A tool of this area outran its deadline or its output ceiling and was
    /// killed together with its process group.
    ///
    /// Never a verdict: a killed `usermod` has not said that the login is locked
    /// or open, and may have finished on the host after the agent stopped
    /// waiting, so this is not folded into [`Self::SpawnFailed`] or
    /// [`Self::SessionCullFailed`], which state what a tool answered.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),
}

impl From<SpawnError> for LoginsError {
    /// Maps a failed spawn onto this area's list.
    ///
    /// A bound becomes [`Self::Bound`]; every other failure means the program
    /// never produced an answer and becomes `Self::program_unavailable`.
    /// `SpawnError` is `#[non_exhaustive]`, so a variant added later lands on
    /// the same answer.
    fn from(error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::Bound(bound),
            _ => Self::program_unavailable(),
        }
    }
}

impl LoginsError {
    /// The error for a tool that could not be started at all.
    pub(crate) fn program_unavailable() -> Self {
        Self::SpawnFailed {
            code: PROGRAM_UNAVAILABLE,
        }
    }
}

#[cfg(test)]
#[path = "../tests/logins/logins_error_tests.rs"]
mod tests;
