//! Failures of the database operations.

/// The signal reported when a client died of one the platform would not name.
///
/// It cannot happen on the systems this agent supports — a status that is not
/// an exit code is a signal, and Linux hands the number back — but a root
/// daemon does not panic to say so, and reporting a killed client as a client
/// that was never started would be the very confusion this file has just
/// removed.
const UNKNOWN_SIGNAL: i32 = 0;

/// The prefix the client puts in front of the number it failed with.
///
/// The client prints `ERROR 1045 (28000): Access denied for user …` on standard
/// error. The number, not the sentence, is what this module decides on: the
/// sentence is localised, reworded between releases, and — the reason that
/// matters here — is the place the server echoes back the credential it
/// refused.
const ERROR_PREFIX: &str = "ERROR ";

/// The most digits an error number may have before this module stops reading.
///
/// MySQL's numbers are four digits today. Six is a ceiling with room to spare,
/// and it exists so that a line of digits arriving on standard error is bounded
/// work rather than an allocation the size of whatever was printed.
const MAXIMUM_NUMBER_DIGITS: usize = 6;

/// Access was refused by the server.
const ACCESS_DENIED: i32 = 1045;

/// Access to that specific database was refused by the server.
const DATABASE_ACCESS_DENIED: i32 = 1044;

/// Access was refused for an account that has no password.
const ACCESS_DENIED_NO_PASSWORD: i32 = 1698;

/// `CREATE DATABASE` refused because the database is already there.
const DATABASE_EXISTS: i32 = 1007;

/// `CREATE USER` refused because the user is already there.
const USER_EXISTS: i32 = 1396;

/// `DROP DATABASE` refused because the database is not there.
const DATABASE_MISSING: i32 = 1008;

/// A statement named a database the server does not have.
const UNKNOWN_DATABASE: i32 = 1049;

/// What can go wrong while creating, dropping, listing or measuring a database.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"), and a
/// deliberately narrow one: **no variant of this enum can hold the client's
/// output**. Every payload here is an `i32`, so there is no field for a message
/// to be put in, which is the point rather than an accident. The realistic leak
/// in this area is not a careless log line: it is the server quoting back what
/// it refused — `Access denied for user 'alice_shop'@'localhost'`, and on some
/// paths the statement that failed, which for `CREATE USER … IDENTIFIED BY '…'`
/// is the customer's password in full. A shape that cannot carry a string
/// cannot carry that (rules/security.md item 8).
///
/// The consequence for an operator is real and accepted: a failure that is not
/// one of the named conditions arrives as [`Self::ClientFailed`] with the
/// server's error number and nothing else. The number is enough to look the
/// condition up in the server's manual; the panel's own record of what it asked
/// for supplies the rest, and never leaves the panel.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum DbError {
    /// The database is already on this server.
    ///
    /// The idempotent answer for a repeated `CreateDatabase` (`db.proto`:
    /// *"if a database and user with the same names already exist, returns
    /// AlreadyExists without changing the existing password"*). A retry after a
    /// timeout is the common way to reach it, and it must converge rather than
    /// fail — which is also why it must not be folded into
    /// [`Self::ClientFailed`]: the panel distinguishes "it is there" from "the
    /// server said no".
    #[error("the database already exists")]
    AlreadyExists,

    /// The database is not on this server.
    ///
    /// The idempotent answer for a repeated `DropDatabase`, and the answer for
    /// measuring something that is not there.
    #[error("the database was not found")]
    NotFound,

    /// The client refused for a reason this area does not name.
    ///
    /// Carries the server's error number and nothing else — see the note on the
    /// enum for why there is no room for the message beside it.
    ///
    /// **It always means the client ran and answered.** It used to mean three
    /// further things as well, all of them wearing `code: -1`: a statement this
    /// host refused to send, a client that could never be started, and a client
    /// killed by a signal. Those are [`Self::StatementRefused`],
    /// [`Self::ClientUnavailable`] and [`Self::ClientKilled`] now, one variant
    /// each, because an operator reading one number cannot tell a broken host
    /// from a refused request from a machine killing processes — and neither
    /// could the sessions that spent two lanes attributing a flake in this very
    /// area to the wrong one of the three.
    #[error("the database client failed with error {code}")]
    ClientFailed {
        /// The server's error number when it printed one, and the client's own
        /// exit status when it did not. Never negative: a client that produced
        /// no status at all is one of the three variants above.
        code: i32,
    },

    /// The statement was refused by this host before any client was started.
    ///
    /// The server was never asked, and nothing was spawned. It is a fault of
    /// the agent's own caller — the only statements that can reach the refusal
    /// are ones carrying a second statement or exceeding the size the client's
    /// pipe is guaranteed to take — so it is deliberately not shaped like a
    /// server answer: there is no number to look up, because no server saw it.
    #[error("the database client was never asked: the statement was refused")]
    StatementRefused,

    /// The client could not be started, or its input could not be delivered.
    ///
    /// The server was never reached. The two halves are one variant on purpose:
    /// both mean the process this host needs is not usable on this machine —
    /// the binary is missing or not executable, the fork failed, or the pipe
    /// broke before the statement was through — and an operator's next step is
    /// the same for all of them. What matters is that this is no longer
    /// indistinguishable from a server that answered.
    #[error("the database client could not be started")]
    ClientUnavailable,

    /// The client was killed by a signal before it could answer.
    ///
    /// Its own variant because it is the one condition here that is a statement
    /// about the MACHINE and not about the server or the request: the kernel's
    /// out-of-memory killer, an operator, or a systemd stop reached the client
    /// mid-statement. Reported as an exit code it was indistinguishable from a
    /// client that never started, and the panel would have retried a statement
    /// that may well have been executed.
    #[error("the database client was killed by signal {signal}")]
    ClientKilled {
        /// The signal number, which is a number and not a name for the reason
        /// the enum's own note gives: no variant here has anywhere to put text.
        signal: i32,
    },

    /// The client's answer was not in the shape this operation reads.
    ///
    /// A size that is not a number, or output longer than the ceiling this
    /// agent will read. Refused rather than guessed at: a size the panel
    /// records wrongly is a quota decision made wrongly.
    #[error("the database client's output could not be read")]
    Unparsable,

    /// The server refused the agent's own connection.
    ///
    /// Its own variant rather than a [`Self::ClientFailed`] number, because it
    /// is the one failure here that is a misconfigured host rather than a bad
    /// request: the agent authenticates as `root` over the local socket through
    /// the `unix_socket` plugin and holds no password, so this means the plugin
    /// is not enabled for `root@localhost`. That is a condition the installer
    /// verifies, and an operator needs to be pointed at it by name.
    #[error("the database server refused the agent's connection")]
    AccessDenied,
}

impl DbError {
    /// Classifies a failed client run.
    ///
    /// `exit_status` is the process's status and `stderr` everything it wrote to
    /// standard error. The server's own error number is preferred when the
    /// output carries one, because the client's exit status is `1` for very
    /// nearly everything — the status alone cannot tell "the database is
    /// already there" from "the server refused you".
    ///
    /// `stderr` is READ here and never stored: the number is taken out of it and
    /// the string is dropped at the end of this call. That is the whole reason
    /// classification lives in one function instead of at each call site — a
    /// caller that never sees the client's output cannot pass it on.
    pub(crate) fn from_client(exit_status: i32, stderr: &str) -> Self {
        let code = mysql_error_number(stderr).unwrap_or(exit_status);

        match code {
            ACCESS_DENIED | DATABASE_ACCESS_DENIED | ACCESS_DENIED_NO_PASSWORD => {
                Self::AccessDenied
            }
            DATABASE_EXISTS | USER_EXISTS => Self::AlreadyExists,
            DATABASE_MISSING | UNKNOWN_DATABASE => Self::NotFound,
            other => Self::ClientFailed { code: other },
        }
    }

    /// The error for a client the platform reported as killed.
    ///
    /// `signal` is what the operating system said, and [`UNKNOWN_SIGNAL`] is
    /// what is reported when it said nothing — the caller has already
    /// established that the client produced no exit code, which on every
    /// supported system means a signal.
    pub(crate) fn client_killed(signal: Option<i32>) -> Self {
        Self::ClientKilled {
            signal: signal.unwrap_or(UNKNOWN_SIGNAL),
        }
    }
}

/// The server's error number, if the client printed one.
///
/// Reads at most [`MAXIMUM_NUMBER_DIGITS`] digits per candidate line, so a
/// standard error made of nothing but digits costs a fixed amount of work.
fn mysql_error_number(stderr: &str) -> Option<i32> {
    stderr.lines().find_map(|line| {
        let rest = line.trim_start().strip_prefix(ERROR_PREFIX)?;
        let digits: String = rest
            .chars()
            .take(MAXIMUM_NUMBER_DIGITS)
            .take_while(char::is_ascii_digit)
            .collect();

        digits.parse().ok()
    })
}
