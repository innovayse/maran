//! Validating a client-streamed write, one message at a time.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::upload_limit::UploadLimit;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::files::WriteFileInput;

use crate::proto::{AgentError, WriteFileRequest};
use crate::services::files::accepted::Accepted;
use crate::services::wire::chunk_collector::ChunkCollector;
use crate::services::wire::invalid_input::invalid_input;

/// A write stream being validated, message by message.
///
/// A state machine and not a loop inside the rpc handler, and the reason is
/// evidence rather than taste: a `tonic::Streaming` cannot be built by a unit
/// test, so every decision made inside that loop — the header rules, the byte
/// cap — would be reachable only through a private helper. This type takes one
/// decoded message at a time, which is exactly the streaming semantics and
/// exactly what a test can drive.
///
/// It holds NO body bytes (the body is streamed to the account's child, never
/// collected in the root daemon): it keeps the cap and the running count, and the
/// count is taken before a chunk is passed on, so the cap is a bound on what the
/// agent forwards rather than a report on what it already did.
pub struct WriteCollector {
    /// The cap the header stated, once there is a header.
    limit: Option<UploadLimit>,
    /// Body bytes accepted so far.
    received: u64,
}

impl WriteCollector {
    /// A collector before the first message.
    #[must_use]
    pub fn new() -> Self {
        Self {
            limit: None,
            received: 0,
        }
    }

    /// Takes one message of the stream.
    ///
    /// Refusals, all `INVALID_INPUT` and all before anything of the message is
    /// passed on:
    ///
    /// - **No header on the first message.** The stream names its target up front
    ///   or not at all.
    /// - **A header anywhere but on the first message.** ONE check covers both
    ///   promises of the contract — a header arriving late, and a second header
    ///   trying to redirect a write already under way, which can only ever arrive
    ///   on a message that is not the first.
    /// - **An account, path, mode or cap the agent's own types refuse**, including
    ///   a cap above the 256 MiB ceiling and a path naming the home or the
    ///   reserved trash directory.
    /// - **A body over the cap**, refused as the crossing chunk arrives. The cap
    ///   is the one the header stated, or one mebibyte when it stated none, which
    ///   is what keeps the ACME caller exactly as bounded as it was.
    ///
    /// # Errors
    ///
    /// Returns the wire error for each refusal above.
    pub fn accept(&mut self, message: WriteFileRequest) -> Result<Accepted, AgentError> {
        let input = match (self.limit, message.header) {
            (None, Some(header)) => {
                let input = validated_header(&header)?;
                self.limit = Some(input.limit);
                Some(input)
            }
            (None, None) => {
                return Err(invalid_input(
                    "the first message of a write stream carries the header".to_owned(),
                ));
            }
            (Some(_), Some(_)) => {
                return Err(invalid_input(
                    "the header belongs on the first message of a write stream, and there is \
                     exactly one of it"
                        .to_owned(),
                ));
            }
            (Some(_), None) => None,
        };

        let limit = self.limit.map_or(0, UploadLimit::bytes);
        if !within_budget(self.received, message.chunk.len(), limit) {
            return Err(invalid_input(format!(
                "the file exceeds the {limit} byte limit for this write"
            )));
        }
        self.received += message.chunk.len() as u64;

        Ok(Accepted {
            input,
            chunk: message.chunk,
        })
    }
}

impl ChunkCollector<WriteFileRequest> for WriteCollector {
    /// The body of a later message, judged by [`WriteCollector::accept`]: the header
    /// rules and the byte cap hold for every message a pump forwards.
    fn accept_chunk(&mut self, message: WriteFileRequest) -> Result<Vec<u8>, AgentError> {
        self.accept(message).map(|accepted| accepted.chunk)
    }
}

impl Default for WriteCollector {
    fn default() -> Self {
        Self::new()
    }
}

/// Turns the header into the validated operation input.
///
/// The account, the path, the mode and the cap are revalidated by the agent's own
/// types even though the panel validated them (rules/rust.md "Validation first"),
/// and each is a type that cannot hold an invalid value once built, so nothing
/// below this point checks them again.
///
/// # Errors
///
/// Returns the wire error when the account name, the path, the mode or the cap is
/// refused.
fn validated_header(header: &crate::proto::WriteFileHeader) -> Result<WriteFileInput, AgentError> {
    Ok(WriteFileInput {
        account: AccountName::parse(&header.account_username)
            .map_err(|error| invalid_input(error.to_string()))?,
        path: MutablePath::parse(&header.path).map_err(|error| invalid_input(error.to_string()))?,
        mode: FileMode::parse(header.mode).map_err(|error| invalid_input(error.to_string()))?,
        fail_if_exists: header.fail_if_exists,
        limit: UploadLimit::parse(header.max_bytes)
            .map_err(|error| invalid_input(error.to_string()))?,
    })
}

/// Whether `incoming` more bytes may be added to `collected` already accepted.
///
/// The addition is checked rather than wrapping: a root process must not panic on
/// input (rules/rust.md), and a wrapped or saturated sum would let an enormous
/// count pass.
fn within_budget(collected: u64, incoming: usize, limit: u64) -> bool {
    collected
        .checked_add(incoming as u64)
        .is_some_and(|total| total <= limit)
}

#[cfg(test)]
#[path = "../../tests/services/files/write_collector_tests.rs"]
mod tests;
