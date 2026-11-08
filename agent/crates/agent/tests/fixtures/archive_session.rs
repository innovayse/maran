//! A real agent in this process and the two archive rpcs of `FilesService`,
//! spoken to it over its real socket.
//!
//! Every call goes through the real codec, the real peer check, the real
//! handler and the real privileged child. A failure travels in the response
//! payload (rules/proto.md), so each call answers `Result<Ok, AgentError>` and
//! a transport-level status is a test failure. Every wait is bounded by
//! [`CALL_BUDGET`], so a child that hangs fails the test instead of the suite.

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
    AgentError, ArchiveFormat, CreateArchiveOk, CreateArchiveRequest, ErrorCode, ExtractArchiveOk,
    ExtractArchiveRequest, create_archive_response, extract_archive_response,
};
use tonic::transport::Channel;

use super::in_process_agent::InProcessAgent;

/// How long any single call is given before the test declares it stuck.
pub const CALL_BUDGET: Duration = Duration::from_secs(90);

/// Caps no archive in these tests comes near.
pub const ROOMY_ENTRIES: u64 = 10_000;
/// Byte cap no archive in these tests comes near.
pub const ROOMY_BYTES: u64 = 256 * 1024 * 1024;
/// Ratio no archive in these tests comes near.
pub const ROOMY_RATIO: u32 = 1000;

/// A running in-process agent with a `FilesService` client onto it.
pub struct ArchiveSession {
    /// Keeps the server alive for the life of the session.
    agent: InProcessAgent,
}

impl ArchiveSession {
    /// Starts the agent, admitting the uid the test runs as (root, in the polygon).
    pub async fn start() -> Self {
        Self {
            agent: InProcessAgent::start().await,
        }
    }

    fn client(&self) -> FilesServiceClient<Channel> {
        FilesServiceClient::new(self.agent.channel())
    }

    /// An extraction request under roomy caps.
    pub fn extract_request(
        account: &str,
        archive: &str,
        destination: &str,
        format: ArchiveFormat,
    ) -> ExtractArchiveRequest {
        ExtractArchiveRequest {
            account_username: account.to_owned(),
            archive_path: archive.to_owned(),
            destination_path: destination.to_owned(),
            format: format as i32,
            max_entries: ROOMY_ENTRIES,
            max_expanded_bytes: ROOMY_BYTES,
            max_ratio: ROOMY_RATIO,
        }
    }

    /// A creation request under roomy caps.
    pub fn create_request(
        account: &str,
        sources: &[&str],
        destination: &str,
        format: ArchiveFormat,
    ) -> CreateArchiveRequest {
        CreateArchiveRequest {
            account_username: account.to_owned(),
            source_paths: sources.iter().map(|s| (*s).to_owned()).collect(),
            destination_path: destination.to_owned(),
            format: format as i32,
            max_entries: ROOMY_ENTRIES,
            max_source_bytes: ROOMY_BYTES,
        }
    }

    /// Extracts one archive.
    ///
    /// # Panics
    ///
    /// Panics on a transport failure, an answer with neither arm, or a call that
    /// outlasts [`CALL_BUDGET`].
    pub async fn extract(
        &self,
        request: ExtractArchiveRequest,
    ) -> Result<ExtractArchiveOk, AgentError> {
        let mut client = self.client();
        let response = tokio::time::timeout(CALL_BUDGET, client.extract_archive(request))
            .await
            .expect("ExtractArchive did not answer within the budget")
            .expect("ExtractArchive failed at the transport")
            .into_inner();

        match response
            .result
            .expect("ExtractArchive answered with no arm")
        {
            extract_archive_response::Result::Ok(ok) => Ok(ok),
            extract_archive_response::Result::Error(error) => Err(error),
        }
    }

    /// Creates one archive.
    ///
    /// # Panics
    ///
    /// Same as [`Self::extract`].
    pub async fn create(
        &self,
        request: CreateArchiveRequest,
    ) -> Result<CreateArchiveOk, AgentError> {
        let mut client = self.client();
        let response = tokio::time::timeout(CALL_BUDGET, client.create_archive(request))
            .await
            .expect("CreateArchive did not answer within the budget")
            .expect("CreateArchive failed at the transport")
            .into_inner();

        match response.result.expect("CreateArchive answered with no arm") {
            create_archive_response::Result::Ok(ok) => Ok(ok),
            create_archive_response::Result::Error(error) => Err(error),
        }
    }
}

impl ArchiveSession {
    /// Asserts `result` is an error carrying `code`.
    pub fn assert_code<T: std::fmt::Debug>(result: &Result<T, AgentError>, code: ErrorCode) {
        match result {
            Err(error) => assert_eq!(error.code, code as i32, "{error:?}"),
            Ok(ok) => panic!("expected {code:?}, got {ok:?}"),
        }
    }
}
