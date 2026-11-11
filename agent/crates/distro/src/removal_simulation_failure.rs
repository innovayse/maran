//! Why a dry run of a package removal produced no answer.

/// Why a dry run of a removal produced no answer.
///
/// Carries an exit status and never a byte the package manager printed, so it can be logged and
/// returned without redaction. A failure is never an empty transaction: "nothing would be
/// removed" and "could not be asked" are different facts, and a removal that is not understood
/// is not started.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
pub enum RemovalSimulationFailure {
    /// The program exited with a status the family's reader treats as a failure of the dry run
    /// itself: a package database that could not be read, a package manager that was not started.
    #[error("the package manager's dry run exited with status {status}")]
    Unsuccessful {
        /// The exit status the program ended with.
        status: i32,
    },
    /// The output holds no transaction in the form the family's reader knows: no list of removed
    /// packages, or a section of the plan the reader does not understand.
    #[error("the package manager's dry run printed no removal plan this agent reads")]
    Unrecognised,
}
