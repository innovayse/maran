//! Failures of the file-transfer login enumeration and its lock.

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
/// payload is an `i32`, so there is no field a message could be put in. The
/// realistic leak here is not a careless log line — it is a tool quoting back
/// the line it refused, which in this area's neighbourhood contains a
/// customer's password in full. A shape that cannot hold a string cannot hold
/// that (rules/security.md item 8).
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

    /// The cull of a suspended account's open sessions refused to run, because
    /// the account's own password-database row carries uid 0.
    ///
    /// Its own variant rather than a folded-in failure, because the two answers
    /// an operator needs are different: this one says the panel's record names
    /// something that is not a hosting account, and no retry will change it. The
    /// account-name grammar accepts `root`, so this is a guard against a name
    /// collision and not a defensive nicety — see
    /// `end_account_sessions`.
    #[error("the hosting account resolves to uid 0, so no session cull was attempted")]
    SessionCullRefusedUid,

    /// The cull refused because the account's row is not homed where this agent
    /// homes a hosting account.
    ///
    /// Separate from [`Self::SessionCullRefusedUid`] because it catches a
    /// different collision — a system user at a low uid, `mail` at uid 8 — and
    /// because a reviewer must be able to see that the two guards are two.
    #[error(
        "the hosting account is not homed where this agent homes one, so no session cull was attempted"
    )]
    SessionCullRefusedHome,

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
}

impl LoginsError {
    /// The error for a tool that could not be started at all.
    pub(crate) fn program_unavailable() -> Self {
        Self::SpawnFailed {
            code: PROGRAM_UNAVAILABLE,
        }
    }
}
