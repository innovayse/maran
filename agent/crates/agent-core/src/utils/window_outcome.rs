//! How a program run for a window ended.

use crate::command_outcome::CommandOutcome;

/// The result of running a program for a window, where reaching the end of the
/// window is the SUCCESS case.
///
/// The inverse of an ordinary deadline: a daemon candidate that is still running
/// when the window closes has read its configuration, bound its socket and had
/// no objection.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum WindowOutcome {
    /// The program ended inside the window, and this is what it printed.
    Exited(CommandOutcome),
    /// The program was still running when the window closed; its whole process
    /// group has been killed and collected.
    Outlived,
}
