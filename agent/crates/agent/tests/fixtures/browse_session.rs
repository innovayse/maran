//! A real agent in this process and the three browse rpcs of `FilesService`,
//! spoken to it over its real socket.
//!
//! Every call goes through the real codec, the real peer check, the real
//! handler and the real privileged child: nothing is stubbed. A failure travels
//! in the response payload (rules/proto.md), so each call answers
//! `Result<Ok, AgentError>` and a transport-level status is a test failure.
//!
//! Every wait is bounded by [`CALL_BUDGET`]. A browse that hangs in the kernel
//! (a FIFO opened without `O_NONBLOCK`) would otherwise hang the suite, and a
//! hang is read as a flaky runner and retried.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use std::time::Duration;

use maran_agent::proto::files_service_client::FilesServiceClient;
use maran_agent::proto::{
    AgentError, ErrorCode, ListDirectoryOk, ListDirectoryRequest, ReadFileRequest,
    ReadFileResponse, SearchEntriesOk, SearchEntriesRequest, list_directory_response,
    read_file_response, search_entries_response,
};
use tonic::Streaming;
use tonic::transport::Channel;

use super::in_process_agent::InProcessAgent;
use crate::held_read::HeldRead;
use crate::read_outcome::ReadOutcome;

/// How long any single browse call is given before the test declares it stuck.
///
/// The agent's own patience for a child is two minutes; this is far below it on
/// purpose, so a removed non-blocking open fails the test rather than waiting
/// out the child's deadline.
pub const CALL_BUDGET: Duration = Duration::from_secs(60);

/// A running in-process agent with a `FilesService` client onto it.
pub struct BrowseSession {
    /// Keeps the server alive for the life of the session.
    agent: InProcessAgent,
}

impl BrowseSession {
    /// Starts the agent, admitting the uid the test runs as (root, in the polygon).
    pub async fn start() -> Self {
        Self {
            agent: InProcessAgent::start().await,
        }
    }

    /// A client onto the agent's socket.
    fn client(&self) -> FilesServiceClient<Channel> {
        FilesServiceClient::new(self.agent.channel())
    }

    /// A listing request for `path` in `account` with every optional field left
    /// at its default.
    pub fn list_request(account: &str, path: &str) -> ListDirectoryRequest {
        ListDirectoryRequest {
            account_username: account.to_owned(),
            path: path.to_owned(),
            ..Default::default()
        }
    }

    /// A read request for `path` in `account` with a ceiling of `max_bytes`.
    pub fn read_request(account: &str, path: &str, max_bytes: u64) -> ReadFileRequest {
        ReadFileRequest {
            account_username: account.to_owned(),
            path: path.to_owned(),
            max_bytes,
        }
    }

    /// A search request from `path` in `account` for `query`, other fields at
    /// their defaults.
    pub fn search_request(account: &str, path: &str, query: &str) -> SearchEntriesRequest {
        SearchEntriesRequest {
            account_username: account.to_owned(),
            path: path.to_owned(),
            query: query.to_owned(),
            ..Default::default()
        }
    }

    /// Lists one directory.
    ///
    /// # Panics
    ///
    /// Panics on a transport failure, on an answer with neither arm, or when the
    /// call outlasts [`CALL_BUDGET`].
    pub async fn list(&self, request: ListDirectoryRequest) -> Result<ListDirectoryOk, AgentError> {
        let mut client = self.client();
        let response = tokio::time::timeout(CALL_BUDGET, client.list_directory(request))
            .await
            .expect("ListDirectory did not answer within the budget")
            .expect("ListDirectory failed at the transport")
            .into_inner();

        match response.result.expect("ListDirectory answered with no arm") {
            list_directory_response::Result::Ok(ok) => Ok(ok),
            list_directory_response::Result::Error(error) => Err(error),
        }
    }

    /// Searches names below one directory.
    ///
    /// # Panics
    ///
    /// Same as [`Self::list`].
    pub async fn search(
        &self,
        request: SearchEntriesRequest,
    ) -> Result<SearchEntriesOk, AgentError> {
        let mut client = self.client();
        let response = tokio::time::timeout(CALL_BUDGET, client.search_entries(request))
            .await
            .expect("SearchEntries did not answer within the budget")
            .expect("SearchEntries failed at the transport")
            .into_inner();

        match response.result.expect("SearchEntries answered with no arm") {
            search_entries_response::Result::Ok(ok) => Ok(ok),
            search_entries_response::Result::Error(error) => Err(error),
        }
    }

    /// Reads one file to the end of its stream.
    ///
    /// # Panics
    ///
    /// Panics on a transport failure or when the stream outlasts [`CALL_BUDGET`].
    pub async fn read(&self, request: ReadFileRequest) -> ReadOutcome {
        let mut client = self.client();
        let mut stream = tokio::time::timeout(CALL_BUDGET, client.read_file(request))
            .await
            .expect("ReadFile did not open within the budget")
            .expect("ReadFile failed at the transport")
            .into_inner();

        let mut outcome = ReadOutcome {
            bytes: Vec::new(),
            chunks: 0,
            error: None,
        };
        while let Some(message) = tokio::time::timeout(CALL_BUDGET, stream.message())
            .await
            .expect("ReadFile stalled within the budget")
            .expect("ReadFile failed at the transport")
        {
            match message.result.expect("ReadFile sent a message with no arm") {
                read_file_response::Result::Ok(chunk) => {
                    outcome.chunks += 1;
                    outcome.bytes.extend_from_slice(&chunk.data);
                }
                read_file_response::Result::Error(error) => {
                    outcome.error = Some(error);
                    break;
                }
            }
        }

        outcome
    }

    /// Opens a read, takes its first message and leaves the stream open.
    /// `Err` is the refusal the agent opened the stream with.
    ///
    /// # Panics
    ///
    /// Panics on a transport failure or when the first message outlasts
    /// [`CALL_BUDGET`].
    pub async fn open_read(&self, request: ReadFileRequest) -> Result<HeldRead, AgentError> {
        // A connection of its own, because this stream is left unread: see
        // `InProcessAgent::fresh_channel` for what an unread stream does to a shared one.
        let mut client = FilesServiceClient::new(self.agent.fresh_channel().await);
        let mut stream = tokio::time::timeout(CALL_BUDGET, client.read_file(request))
            .await
            .expect("ReadFile did not open within the budget")
            .expect("ReadFile failed at the transport")
            .into_inner();

        let first = tokio::time::timeout(CALL_BUDGET, stream.message())
            .await
            .expect("ReadFile sent nothing within the budget")
            .expect("ReadFile failed at the transport")
            .expect("ReadFile ended before its first message");

        match first.result.expect("ReadFile sent a message with no arm") {
            read_file_response::Result::Ok(_) => Ok(HeldRead { stream }),
            read_file_response::Result::Error(error) => Err(error),
        }
    }
}

impl BrowseSession {
    /// Requires `error` to carry `code`.
    ///
    /// # Panics
    ///
    /// Panics, quoting the error, when the code differs.
    pub fn assert_code(error: &AgentError, code: ErrorCode) {
        assert_eq!(
            error.code,
            code as i32,
            "expected {code:?}, the agent said {:?}: {}",
            ErrorCode::try_from(error.code),
            error.message
        );
    }

    /// Requires `error` to be a refusal of the request or of the tree: the agent's
    /// own validation or its containment check, and not a fault of the host.
    ///
    /// # Panics
    ///
    /// Panics when the code is anything else, naming it.
    pub fn assert_refused(error: &AgentError) {
        let code = ErrorCode::try_from(error.code).ok();
        assert!(
            matches!(
                code,
                Some(ErrorCode::InvalidInput | ErrorCode::ValidationFailed)
            ),
            "expected a refusal (INVALID_INPUT or VALIDATION_FAILED), the agent said {code:?}: {}",
            error.message
        );
    }
}
