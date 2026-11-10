//! A scripted [`SourceShell`] for the inventory tests: it answers each command
//! from a table and remembers what it was asked.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::cell::RefCell;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;

use crate::transfer::{RemoteCommand, SourceShell, TransferOpError};

/// What the fake does when a command is run.
#[derive(Clone)]
pub(crate) enum Answer {
    /// The command ran and exited with `status`, printing `stdout`.
    Output {
        /// The exit status.
        status: i32,
        /// The standard output.
        stdout: String,
    },
    /// The command crossed its deadline or output cap.
    Bound,
    /// The connection could not be made.
    Unreachable,
}

/// A source whose answers are written out command by command.
///
/// A command with no answer behaves like a tool that is not installed: it exits
/// 127 and prints nothing, so a test that forgets to script a command sees a
/// warning and not a silently empty success.
pub(crate) struct FakeSourceShell {
    /// The scripted answers, first match wins.
    answers: Vec<(RemoteCommand, Answer)>,
    /// Every command run, in order.
    ran: RefCell<Vec<RemoteCommand>>,
}

impl FakeSourceShell {
    /// A source that knows no command.
    pub(crate) fn new() -> Self {
        Self {
            answers: Vec::new(),
            ran: RefCell::new(Vec::new()),
        }
    }

    /// Scripts `command` to exit 0 printing `stdout`.
    pub(crate) fn answering(self, command: RemoteCommand, stdout: &str) -> Self {
        self.exiting(command, 0, stdout)
    }

    /// Scripts `command` to exit with `status` printing `stdout`.
    pub(crate) fn exiting(mut self, command: RemoteCommand, status: i32, stdout: &str) -> Self {
        self.answers.push((
            command,
            Answer::Output {
                status,
                stdout: stdout.to_owned(),
            },
        ));
        self
    }

    /// Scripts `command` to cross its output bound.
    pub(crate) fn bounded(mut self, command: RemoteCommand) -> Self {
        self.answers.push((command, Answer::Bound));
        self
    }

    /// Scripts `command` to fail because the source is unreachable.
    pub(crate) fn unreachable(mut self, command: RemoteCommand) -> Self {
        self.answers.push((command, Answer::Unreachable));
        self
    }

    /// Every command that was run, in order.
    pub(crate) fn ran(&self) -> Vec<RemoteCommand> {
        self.ran.borrow().clone()
    }
}

impl SourceShell for FakeSourceShell {
    /// Records the command and answers it from the table.
    fn run(&self, command: &RemoteCommand) -> Result<CommandOutcome, TransferOpError> {
        self.ran.borrow_mut().push(command.clone());
        let answer = self
            .answers
            .iter()
            .find(|(scripted, _)| scripted == command)
            .map(|(_, answer)| answer.clone());
        match answer {
            Some(Answer::Output { status, stdout }) => Ok(CommandOutcome {
                status,
                stdout,
                stderr: String::new(),
            }),
            Some(Answer::Bound) => Err(TransferOpError::OutputBound(BoundExceeded::TimedOut {
                program: "ssh".to_owned(),
                deadline: Duration::from_secs(1),
            })),
            Some(Answer::Unreachable) => Err(TransferOpError::Unreachable),
            None => Ok(CommandOutcome {
                status: 127,
                stdout: String::new(),
                stderr: String::new(),
            }),
        }
    }
}
