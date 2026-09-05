//! The recording core a fake host composes: what was run, what to answer.

// A fake's lock can only be poisoned by a failing test, and a failing
// assertion IS the reporting mechanism there.
#![allow(clippy::unwrap_used)]

use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;

/// Records every argv it is handed and answers with a configured outcome.
///
/// Not a mock with expectations: tests assert on the recorded argv afterwards,
/// which is the thing worth pinning (`useradd --create-home` and `useradd -m`
/// differ by nothing a type system can see and by everything a customer's
/// data can). Fakes COMPOSE this — hold it in a field and delegate — so each
/// area's fake keeps its own trait impls and area-specific fixtures. Fakes
/// that answer per-argv (ssl) or per-unit (monitor) are a different kind on
/// purpose and do not use this.
pub(crate) struct RecordingCommands {
    /// Every argv handed to [`RecordingCommands::record`], in order.
    calls: Mutex<Vec<Vec<String>>>,
    /// The outcome the following `record` calls answer with.
    next: Mutex<(i32, String, String)>,
}

impl RecordingCommands {
    /// Creates a recorder that answers success with empty output.
    pub(crate) fn new() -> Self {
        Self {
            calls: Mutex::new(Vec::new()),
            next: Mutex::new((0, String::new(), String::new())),
        }
    }

    /// Records the argv and answers with the configured outcome.
    pub(crate) fn record(&self, program: &str, arguments: &[&str]) -> CommandOutcome {
        let mut command = vec![program.to_owned()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        let (status, stdout, stderr) = self.next.lock().unwrap().clone();
        CommandOutcome {
            status,
            stdout,
            stderr,
        }
    }

    /// Configures what every following `record` answers.
    pub(crate) fn set_next(&self, status: i32, stdout: &str, stderr: &str) {
        *self.next.lock().unwrap() = (status, stdout.to_owned(), stderr.to_owned());
    }

    /// Every recorded argv, in order.
    pub(crate) fn calls(&self) -> Vec<Vec<String>> {
        self.calls.lock().unwrap().clone()
    }

    /// The recorded argvs whose program equals `program`.
    pub(crate) fn calls_to(&self, program: &str) -> Vec<Vec<String>> {
        self.calls()
            .into_iter()
            .filter(|call| call.first().is_some_and(|first| first == program))
            .collect()
    }
}
