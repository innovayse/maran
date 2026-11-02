//! Assembling a client-streamed write, one message at a time.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::files::WriteFileInput;

use crate::proto::{AgentError, WriteFileRequest};
use crate::services::wire::invalid_input::invalid_input;

/// The most content the agent will accept for one file.
///
/// The whole body is buffered before the write, because the bytes have to be in
/// memory when `fork_as_account` forks — a child cannot read from the tokio
/// runtime it did not inherit. So this is a bound on the ROOT DAEMON's memory,
/// and the caller is the one choosing the number: without it, one rpc holds as
/// much of the daemon as the panel cares to send, and the panel is not the only
/// thing that will ever hold a socket to the agent.
///
/// One mebibyte is roughly ten thousand times what the only caller sends — an
/// ACME key authorization is under a hundred bytes — and small enough that a
/// hundred concurrent writes are a hundred megabytes rather than a machine.
///
/// The tests state their byte counts as literals rather than deriving them from
/// this constant. That is the point of writing it down: a test that computes its
/// body size from the number under test pins the arithmetic and leaves the NUMBER
/// free, so raising this sixteenfold — a sixteenfold change in the daemon's
/// per-rpc memory bound — would be silent.
const MAXIMUM_CONTENT: usize = 1024 * 1024;

/// A write stream being assembled, message by message.
///
/// A state machine and not a loop inside the rpc handler, and the reason is
/// evidence rather than taste: a `tonic::Streaming` cannot be built by a unit
/// test, so every decision made inside that loop — the header rules, the byte
/// cap — was reachable only through the private helper the tests actually
/// called. Three protections survived mutation because of it. This type takes
/// one decoded message at a time, which is exactly the streaming semantics and
/// exactly what a test can drive.
///
/// The bound is enforced on arrival, not at the end: a chunk that would cross
/// the line is refused before it is appended, so the cap is a bound on what the
/// daemon holds rather than a report on what it already held.
pub struct WriteCollector {
    /// How the stream opened: not yet, with a header, or without one. This is
    /// how "first message" is decided, and a header held with nothing started
    /// cannot be written down.
    opening: Opening,
    /// The body so far, never longer than [`MAXIMUM_CONTENT`].
    contents: Vec<u8>,
}

/// How a write stream opened.
enum Opening {
    /// No message has arrived.
    NotStarted,
    /// The first message arrived carrying the header.
    Headed(crate::proto::WriteFileHeader),
    /// The first message arrived without a header, so the write can never be
    /// finished: a later message may not supply one.
    Headerless,
}

impl WriteCollector {
    /// An empty collector, before the first message.
    #[must_use]
    pub fn new() -> Self {
        Self {
            opening: Opening::NotStarted,
            contents: Vec::new(),
        }
    }

    /// Takes one message of the stream.
    ///
    /// Two refusals, and there are two rather than three on purpose:
    ///
    /// - **A header anywhere but on the first message.** This is ONE check and
    ///   it covers both things the contract promises — a header arriving late,
    ///   and a second header trying to redirect a write already under way, which
    ///   can only ever arrive on a message that is not the first. An earlier
    ///   version had both as separate `if`s; the second was unreachable, and
    ///   mutation showed it: deleting it changed nothing anywhere. Two checks
    ///   where one is dead is worse than one check, because the dead one reads
    ///   as protection while a later edit to the live one silently removes both.
    /// - **A body over the cap**, refused as the crossing chunk arrives rather
    ///   than after it has been appended, so the cap bounds what the daemon
    ///   holds rather than reporting on what it held.
    ///
    /// # Errors
    ///
    /// Returns the wire error for both.
    pub fn accept(&mut self, message: WriteFileRequest) -> Result<(), AgentError> {
        let first = matches!(self.opening, Opening::NotStarted);
        if message.header.is_some() && !first {
            return Err(invalid_input(
                "the header belongs on the first message of a write stream, and there is \
                 exactly one of it"
                    .to_owned(),
            ));
        }
        if first {
            self.opening = match message.header {
                Some(sent) => Opening::Headed(sent),
                None => Opening::Headerless,
            };
        }

        if !within_budget(self.contents.len(), message.chunk.len()) {
            return Err(invalid_input(format!(
                "the file exceeds the agent's {MAXIMUM_CONTENT} byte limit for one write"
            )));
        }
        self.contents.extend_from_slice(&message.chunk);

        Ok(())
    }

    /// Turns everything collected into the validated operation input.
    ///
    /// The account, the path and the mode are revalidated by the agent's own
    /// types even though the panel validated them (rules/rust.md "Validation
    /// first"), and each is a type that cannot hold an invalid value once built,
    /// so nothing below this point checks them again.
    ///
    /// # Errors
    ///
    /// Returns the wire error when no header ever arrived, when the account name
    /// or the path is invalid, or when the mode is not a plain permission mode.
    pub fn finish(self) -> Result<WriteFileInput, AgentError> {
        let Opening::Headed(header) = self.opening else {
            return Err(invalid_input(
                "the first message of a write stream carries the header".to_owned(),
            ));
        };

        let account = AccountName::parse(&header.account_username)
            .map_err(|error| invalid_input(error.to_string()))?;
        let path =
            RelativePath::parse(&header.path).map_err(|error| invalid_input(error.to_string()))?;
        let mode =
            FileMode::parse(header.mode).map_err(|error| invalid_input(error.to_string()))?;

        Ok(WriteFileInput {
            account,
            path,
            contents: self.contents,
            mode,
        })
    }
}

impl Default for WriteCollector {
    fn default() -> Self {
        Self::new()
    }
}

/// Whether `incoming` more bytes may be added to `collected` already held.
///
/// The addition saturates rather than wrapping: a root process must not panic on
/// input (rules/rust.md), and a wrapped sum would need the caller to have already
/// sent exabytes — but the check that stops that is this one, so it cannot rely
/// on itself having worked.
fn within_budget(collected: usize, incoming: usize) -> bool {
    collected.saturating_add(incoming) <= MAXIMUM_CONTENT
}

#[cfg(test)]
#[path = "../../tests/services/files/write_collector_tests.rs"]
mod tests;
