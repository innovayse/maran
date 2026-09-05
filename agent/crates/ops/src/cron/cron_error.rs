//! Why a per-account cron operation could not be done.

use maran_agent_core::privs::priv_error::PrivError;

/// The `code` reported when `crontab` could not be started, or could not be
/// handed a table at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `CrontabRefused { code: -1 }` knows
/// the program never got as far as reading a table, rather than looking up
/// status -1 in its manual.
pub(crate) const PROGRAM_UNAVAILABLE: i32 = -1;

/// What can go wrong while reading, installing or editing an account's crontab.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"), and a
/// deliberately narrow one: **no variant carries a program's output or a
/// path**. [`Self::Privilege`] is the one variant that is not an `i32` payload
/// itself — it wraps [`PrivError`], a type one crate away in `agent-core` — but
/// the same guarantee holds one level down: every `PrivError` variant is a unit
/// variant or carries only integers (an errno, a signal, a raw or translated
/// exit status), and the enum is `#[non_exhaustive]`, so a new variant added
/// there cannot silently start carrying a path or a program's output either.
/// Every other variant here is a plain `i32`, so there is no field a message
/// could be put in.
///
/// That shape is not inherited caution, it is specific to this area. A crontab
/// this agent installs carries the account's own environment assignments, and
/// an operator who sets `API_TOKEN=…` through the panel has put a secret on a
/// line that `crontab(1)` quotes back when it refuses the file. A variant that
/// could hold that output would put the secret into the operator log and into
/// every error path above it (rules/security.md item 8). A shape that cannot
/// hold a string cannot hold it.
///
/// The cost to an operator is accepted and real: a refusal that is not one of
/// the named conditions arrives as [`Self::CrontabRefused`] with the program's
/// exit status and nothing else. The status is enough to find the condition in
/// the tool's manual, and the panel's own record supplies the rest.
#[derive(Debug, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum CronError {
    /// The account already has a managed entry with this schedule and this
    /// command.
    ///
    /// The idempotent answer to a repeated creation, and it is decided BEFORE
    /// anything is written: a retry of a creation whose response was lost must
    /// not leave a second copy of the entry running twice a minute.
    ///
    /// Whether the twin is enabled makes no difference. A disabled entry is
    /// still the entry the customer created, and re-creating it would leave
    /// them with one they can see and one they cannot explain.
    #[error("the account already has that cron entry")]
    AlreadyExists,

    /// The account has no managed entry with that id.
    ///
    /// The idempotent answer to a repeated deletion, and the refusal an update,
    /// a toggle or an output read gives for an id this account does not own.
    #[error("no such cron entry")]
    NotFound,

    /// The account could not be resolved, or the privilege drop for work inside
    /// its home failed. Nothing under the home was changed.
    #[error("privileged work as the account failed: {0}")]
    Privilege(#[from] PrivError),

    /// `crontab` refused the table, or could not be run at all.
    ///
    /// The live crontab is whatever it was before in either case: the program
    /// installs a table or it does not, and there is no partial state between
    /// the two for this area to unwind.
    #[error("crontab refused the table with status {code}")]
    CrontabRefused {
        /// The program's exit status, or `-1` when it could not be started or
        /// could not be given a table to read.
        code: i32,
    },

    /// An entry's command file could not be written, or the account's cron
    /// directory could not be created.
    ///
    /// Its own variant rather than part of [`Self::CrontabRefused`], because
    /// the two leave opposite states behind: a crontab that would not install
    /// leaves nothing running, while a command file that would not write leaves
    /// an entry cron may already be scheduled to run.
    #[error("the entry's command file could not be written")]
    EntryFileUnwritable,

    /// An entry's command, output or exit file could not be read.
    ///
    /// A file that is simply not there is NOT this — it is `Ok(None)`, because
    /// an entry that has never run has no output and no exit status, and that
    /// is an answer rather than a failure. This variant is what is left: a file
    /// that is there and is not a plain file the account owns, or one the
    /// daemon was refused.
    #[error("the entry's files could not be read")]
    EntryFileUnreadable,

    /// An entry's files could not be removed.
    ///
    /// Reported after the crontab has already been installed without the entry,
    /// so cron is no longer running it — what is left behind is at most litter
    /// inside the account's own directory, never a live schedule.
    #[error("the entry's files could not be removed")]
    EntryFileUnremovable,

    /// No entry id could be minted, because the host's randomness source could
    /// not be read.
    ///
    /// Its own variant because it means the host is broken in a way that has
    /// nothing to do with the request: nothing was written, nothing was
    /// installed, and retrying the identical request is the correct response
    /// once the host is fixed.
    #[error("no cron entry id could be generated")]
    EntryIdUnavailable,
}

#[cfg(test)]
#[path = "../tests/cron/cron_error_tests.rs"]
mod tests;
