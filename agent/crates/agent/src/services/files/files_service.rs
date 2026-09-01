//! `FilesService`: files inside a hosting customer's home.

use std::pin::Pin;
use std::sync::Arc;

use maran_ops::files::{self, FilesHost, FilesOpError};
use tokio_stream::Stream;
use tonic::{Request, Response, Status, Streaming};

use crate::proto::files_service_server::FilesService;
use crate::proto::{
    AgentError, ChangePermissionsRequest, ChangePermissionsResponse, CreateArchiveRequest,
    CreateArchiveResponse, CreateDirectoryRequest, CreateDirectoryResponse, DeleteEntryOk,
    DeleteEntryRequest, DeleteEntryResponse, ErrorCode, ExtractArchiveRequest,
    ExtractArchiveResponse, ListDirectoryRequest, ListDirectoryResponse, MoveEntryRequest,
    MoveEntryResponse, ReadFileRequest, ReadFileResponse, WriteFileOk, WriteFileRequest,
    WriteFileResponse, delete_entry_response, write_file_response,
};
use crate::services::files::file_status::to_agent_error;
use crate::services::files::validated_delete::validated_delete;
use crate::services::files::validated_write::validated_write;

/// The stream type `ReadFile` would return.
///
/// Named even though the rpc is unimplemented, because the tonic trait requires
/// the associated type to exist before the method can refuse.
type ReadStream = Pin<Box<dyn Stream<Item = Result<ReadFileResponse, Status>> + Send>>;

/// What an unimplemented rpc says, and why it says it there.
///
/// A gRPC `UNIMPLEMENTED` status and NOT an `AgentError` in the payload.
/// rules/proto.md reserves the transport status for transport problems and puts
/// domain outcomes in the payload — and "this build does not have that method"
/// is neither a domain outcome nor a fault of the request. It is the one thing
/// gRPC's own status set describes exactly, and a client library distinguishes
/// it from a failure without reading a code.
const NOT_BUILT: &str = "this agent implements FilesService.WriteFile and FilesService.DeleteEntry \
                         only; see proto/agent/v1/files.proto";

/// Serves the customer-file operations over the wire.
///
/// **Two of the nine rpcs are implemented, and that is the design.** The panel's
/// ACME issuance writes a challenge token into a site's document root and takes
/// it away again; nothing else in the product calls this service yet. The other
/// seven answer `UNIMPLEMENTED` rather than being written speculatively against
/// a caller that does not exist — a method nobody calls is a method nobody
/// exercises, and this is the service that reaches into customers' homes.
///
/// Every implemented rpc follows the same three steps: revalidate what the panel
/// sent, run one operation on the blocking pool, and map the outcome into the
/// response's `oneof`. Failures travel in the payload rather than as a gRPC
/// status, because they are answers the panel acts on — a challenge file that is
/// already gone is information, not a transport error (rules/proto.md).
pub struct FilesServiceImpl<H> {
    /// The machine the file operations run against.
    host: Arc<H>,
}

impl<H: FilesHost + 'static> FilesServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H) -> Self {
        Self {
            host: Arc::new(host),
        }
    }

    /// Runs one operation on the blocking pool and maps its failure onto the
    /// wire error — the shape both rpcs here share, written once so that adding
    /// an rpc cannot forget to leave the runtime or map an error differently
    /// from its neighbour.
    ///
    /// Leaving the runtime is not optional here: every operation forks and then
    /// waits in `waitpid`, which on a runtime worker stalls every other
    /// in-flight command (rules/rust.md "Async and blocking").
    ///
    /// # Errors
    ///
    /// Returns the [`to_agent_error`] mapping of whatever the operation failed
    /// on. A blocking task that panicked has no domain answer to give and is
    /// reported as a system failure — not as a gRPC status, which rules/proto.md
    /// reserves for transport problems, and a panic inside the agent is not one.
    async fn run<T, F>(operation: F) -> Result<T, AgentError>
    where
        F: FnOnce() -> Result<T, FilesOpError> + Send + 'static,
        T: Send + 'static,
    {
        match tokio::task::spawn_blocking(operation).await {
            Ok(outcome) => outcome.map_err(|error| to_agent_error(&error)),
            Err(error) => Err(AgentError {
                code: ErrorCode::SystemFailure as i32,
                message: format!("the file operation did not finish: {error}"),
                tool_output: String::new(),
            }),
        }
    }
}

#[tonic::async_trait]
impl<H: FilesHost + 'static> FilesService for FilesServiceImpl<H> {
    type ReadFileStream = ReadStream;

    /// Writes one file inside the account's home, as the account.
    async fn write_file(
        &self,
        request: Request<Streaming<WriteFileRequest>>,
    ) -> Result<Response<WriteFileResponse>, Status> {
        let result = match validated_write(request.into_inner()).await {
            Ok(input) => {
                let host = Arc::clone(&self.host);
                Self::run(move || files::write_file(host.as_ref(), &input)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(bytes_written) => write_file_response::Result::Ok(WriteFileOk { bytes_written }),
            Err(error) => write_file_response::Result::Error(error),
        };

        Ok(Response::new(WriteFileResponse {
            result: Some(result),
        }))
    }

    /// Removes one file inside the account's home, as the account.
    async fn delete_entry(
        &self,
        request: Request<DeleteEntryRequest>,
    ) -> Result<Response<DeleteEntryResponse>, Status> {
        let result = match validated_delete(&request.into_inner()) {
            Ok(input) => {
                let host = Arc::clone(&self.host);
                Self::run(move || files::delete_entry(host.as_ref(), &input)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => delete_entry_response::Result::Ok(DeleteEntryOk {}),
            Err(error) => delete_entry_response::Result::Error(error),
        };

        Ok(Response::new(DeleteEntryResponse {
            result: Some(result),
        }))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn list_directory(
        &self,
        _request: Request<ListDirectoryRequest>,
    ) -> Result<Response<ListDirectoryResponse>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn read_file(
        &self,
        _request: Request<ReadFileRequest>,
    ) -> Result<Response<Self::ReadFileStream>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn move_entry(
        &self,
        _request: Request<MoveEntryRequest>,
    ) -> Result<Response<MoveEntryResponse>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn create_directory(
        &self,
        _request: Request<CreateDirectoryRequest>,
    ) -> Result<Response<CreateDirectoryResponse>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn change_permissions(
        &self,
        _request: Request<ChangePermissionsRequest>,
    ) -> Result<Response<ChangePermissionsResponse>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn create_archive(
        &self,
        _request: Request<CreateArchiveRequest>,
    ) -> Result<Response<CreateArchiveResponse>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }

    /// Not built: this agent implements `WriteFile` and `DeleteEntry` only,
    /// so this answers `UNIMPLEMENTED` (see `proto/agent/v1/files.proto`).
    async fn extract_archive(
        &self,
        _request: Request<ExtractArchiveRequest>,
    ) -> Result<Response<ExtractArchiveResponse>, Status> {
        Err(Status::unimplemented(NOT_BUILT))
    }
}
