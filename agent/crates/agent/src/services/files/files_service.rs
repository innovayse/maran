//! `FilesService`: files inside a hosting customer's home.

use std::pin::Pin;
use std::sync::Arc;

use maran_ops::files::{self, FilesHost};
use tokio_stream::Stream;
use tonic::{Request, Response, Status, Streaming};

use crate::proto::files_service_server::FilesService;
use crate::proto::{
    ChangePermissionsRequest, ChangePermissionsResponse, CreateArchiveRequest,
    CreateArchiveResponse, CreateDirectoryRequest, CreateDirectoryResponse, DeleteEntryOk,
    DeleteEntryRequest, DeleteEntryResponse, ExtractArchiveRequest, ExtractArchiveResponse,
    ListDirectoryRequest, ListDirectoryResponse, MoveEntryRequest, MoveEntryResponse,
    ReadFileRequest, ReadFileResponse, WriteFileOk, WriteFileRequest, WriteFileResponse,
    delete_entry_response, write_file_response,
};
use crate::services::files::file_status::to_agent_error;
use crate::services::files::validated_delete::validated_delete;
use crate::services::files::validated_write::validated_write;
use crate::services::wire::run_blocking::run_blocking;

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
}

#[tonic::async_trait]
impl<H: FilesHost + 'static> FilesService for FilesServiceImpl<H> {
    type ReadFileStream = ReadStream;

    /// Writes one file inside the account's home, as the account.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"), and
    /// it is the only one of the five that gets it for free. This is the one
    /// client-streaming rpc, so the upload IS what drives the work: a sender
    /// that disappears makes the next message an error, `validated_write`
    /// refuses, and no filesystem work has happened yet — an upload whose
    /// sender is gone is incomplete by definition and must not be written. The
    /// drop after the drain is a different question and is answered by
    /// `run_blocking` like every unary rpc: one small already-validated write
    /// finishes rather than being torn in half.
    async fn write_file(
        &self,
        request: Request<Streaming<WriteFileRequest>>,
    ) -> Result<Response<WriteFileResponse>, Status> {
        let result = match validated_write(request.into_inner()).await {
            Ok(input) => {
                let host = Arc::clone(&self.host);
                run_blocking("file operation", to_agent_error, move || {
                    files::write_file(host.as_ref(), &input)
                })
                .await
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
                run_blocking("file operation", to_agent_error, move || {
                    files::delete_entry(host.as_ref(), &input)
                })
                .await
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
    ///
    /// **It therefore has no cancellation class** (rules/rust.md, "Async and
    /// blocking"), and that is worth one line because the contract declares it
    /// server-streaming and two readings of this tree have counted it as a live
    /// streaming rpc. It refuses before any host work exists, so there is
    /// nothing a dropped client could stop, finish or orphan. Whoever
    /// implements it inherits the obligation: a bounded-chunk read of a
    /// customer's file is pure observation, so it will be the **stop** class,
    /// alongside `TailSiteLog`.
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
