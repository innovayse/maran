//! `FilesService`: files inside a hosting customer's home.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_ops::files::{self, ArchiveHost, BrowseHost, FilesHost};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status, Streaming};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::files_service_server::FilesService;
use crate::proto::{
    ChangePermissionsRequest, ChangePermissionsResponse, CopyEntryRequest, CopyEntryResponse,
    CreateArchiveRequest, CreateArchiveResponse, CreateDirectoryRequest, CreateDirectoryResponse,
    DeleteEntryOk, DeleteEntryRequest, DeleteEntryResponse, EmptyTrashRequest, EmptyTrashResponse,
    ExtractArchiveRequest, ExtractArchiveResponse, ForceDeleteEntryRequest,
    ForceDeleteEntryResponse, ListDirectoryRequest, ListDirectoryResponse, ListTrashRequest,
    ListTrashResponse, MoveEntryRequest, MoveEntryResponse, PurgeExpiredTrashRequest,
    PurgeExpiredTrashResponse, ReadFileRequest, ReadFileResponse, RestoreTrashEntryRequest,
    RestoreTrashEntryResponse, SearchEntriesRequest, SearchEntriesResponse, TrashEntryRequest,
    TrashEntryResponse, WriteFileOk, WriteFileRequest, WriteFileResponse, delete_entry_response,
    list_directory_response, read_file_response, search_entries_response, write_file_response,
};
use crate::services::files::changed_permissions::changed_permissions;
use crate::services::files::channel_upload::ChannelUpload;
use crate::services::files::copied_entry::copied_entry;
use crate::services::files::created_archive::created_archive;
use crate::services::files::created_directory::created_directory;
use crate::services::files::emptied_trash::emptied_trash;
use crate::services::files::extracted_archive::extracted_archive;
use crate::services::files::file_status::to_agent_error;
use crate::services::files::force_deleted_entry::force_deleted_entry;
use crate::services::files::listed_trash::listed_trash;
use crate::services::files::moved_entry::moved_entry;
use crate::services::files::purged_trash::purged_trash;
use crate::services::files::read_terminal::read_terminal;
use crate::services::files::restored_trash_entry::restored_trash_entry;
use crate::services::files::run_file_operation::run_file_operation;
use crate::services::files::stream_chunk_sink::StreamChunkSink;
use crate::services::files::to_wire_directory_listing::to_wire_directory_listing;
use crate::services::files::to_wire_search_outcome::to_wire_search_outcome;
use crate::services::files::trashed_entry::trashed_entry;
use crate::services::files::validated_archive::validated_archive;
use crate::services::files::validated_copy::validated_copy;
use crate::services::files::validated_create_directory::validated_create_directory;
use crate::services::files::validated_delete::validated_delete;
use crate::services::files::validated_extract::validated_extract;
use crate::services::files::validated_force_delete::validated_force_delete;
use crate::services::files::validated_listing::validated_listing;
use crate::services::files::validated_move::validated_move;
use crate::services::files::validated_permissions::validated_permissions;
use crate::services::files::validated_purge::validated_purge;
use crate::services::files::validated_read::validated_read;
use crate::services::files::validated_restore::validated_restore;
use crate::services::files::validated_search::validated_search;
use crate::services::files::validated_trash::validated_trash;
use crate::services::files::validated_write::validated_write;
use crate::services::wire::run_blocking::run_blocking;
use crate::services::wire::stream_pump::StreamPump;
use crate::services::wire::validated_account::validated_account;

/// The stream type `ReadFile` returns.
type ReadStream = Pin<Box<dyn Stream<Item = Result<ReadFileResponse, Status>> + Send>>;

/// How many chunks the read may run ahead of the client.
///
/// Eight slots of at most 64 KiB is half a megabyte per open read: the ceiling
/// on what one stream costs the root daemon, so a slow client slows the read
/// instead of making the agent buffer a customer's file. A full channel is not
/// the whole of the backpressure — `StreamChunkSink` retries a full channel for
/// a bounded time and then ends the read — the capacity only decides how much
/// slack a merely slow client gets before that clock starts.
const READ_CHANNEL_CAPACITY: usize = 8;

/// Serves the customer-file operations over the wire.
///
/// **All seventeen rpcs are implemented.**
/// The panel's ACME issuance writes a challenge token into a site's document
/// root and takes it away again (`WriteFile`, `DeleteEntry`); the file manager's
/// read side lists, reads and searches (`ListDirectory`, `ReadFile`,
/// `SearchEntries`, issue #166) and its mutating side creates, moves, copies,
/// changes permissions, removes and trashes (issue #167), and unpacks and packs
/// zip and tar.gz archives (`ExtractArchive`, `CreateArchive`, issue #168).
///
/// Three hosts, because the three halves evolve independently: [`FilesHost`] is the
/// write side, [`BrowseHost`] the read side and [`ArchiveHost`] the one area that
/// parses a format a customer chose, and no trait knows another's operations.
///
/// Every implemented rpc follows the same three steps: revalidate what the panel
/// sent, run one operation on the blocking pool, and map the outcome into the
/// response's `oneof`. Failures travel in the payload rather than as a gRPC
/// status, because they are answers the panel acts on — a challenge file that is
/// already gone is information, not a transport error (rules/proto.md).
pub struct FilesServiceImpl<H, B, A> {
    /// The free-space floor an upload is refused below (issue #148).
    floor: FreeSpaceFloor,
    /// The machine the write and delete operations run against.
    host: Arc<H>,
    /// The machine the list, read and search operations run against.
    browse: Arc<B>,
    /// The machine the archive operations run against (issue #168).
    archive: Arc<A>,
}

impl<H: FilesHost + 'static, B: BrowseHost + 'static, A: ArchiveHost + 'static>
    FilesServiceImpl<H, B, A>
{
    /// Creates the service around the hosts it runs operations against.
    #[must_use]
    pub fn new(host: H, browse: B, archive: A, floor: FreeSpaceFloor) -> Self {
        Self {
            floor,
            host: Arc::new(host),
            browse: Arc::new(browse),
            archive: Arc::new(archive),
        }
    }
}

#[tonic::async_trait]
impl<H: FilesHost + 'static, B: BrowseHost + 'static, A: ArchiveHost + 'static> FilesService
    for FilesServiceImpl<H, B, A>
{
    type ReadFileStream = ReadStream;

    /// Writes one file inside the account's home, as the account, streaming the body.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). This is the one
    /// client-streaming rpc, so the upload IS what drives the work, and an upload whose sender
    /// disappears is incomplete by definition and must not be published. The body is forwarded
    /// to the account's forked child through a bounded channel as it arrives; the child writes
    /// a temporary file beside the target and renames it into place only after the terminator.
    /// A stream that errors, is dropped, or exceeds its cap closes the channel without the
    /// terminator, the child removes its temporary file, and the destination is as it was.
    /// Nothing about the file exists at the destination until the whole body has arrived.
    async fn write_file(
        &self,
        request: Request<Streaming<WriteFileRequest>>,
    ) -> Result<Response<WriteFileResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let mut stream = request.into_inner();
        let result = match validated_write(&mut stream).await {
            Ok((input, collector, first_chunk)) => {
                audit.account(&input.account);
                let (chunks, pump) = StreamPump::start(stream, collector, first_chunk, "write");
                let mut upload = ChannelUpload::new(chunks);
                let host = Arc::clone(&self.host);
                let floor = self.floor;
                let written = run_blocking("file operation", to_agent_error, move || {
                    files::write_file(host.as_ref(), &input, &mut upload, &floor)
                })
                .await;
                // The pump's refusal outranks the writer's failure, which is only its
                // consequence (an aborted upload); a writer that succeeded stands, because
                // the terminator is sent only after a clean end.
                let refusal = pump.finish();
                written.map_err(|error| refusal.unwrap_or(error))
            }
            Err(error) => Err(error),
        };
        // Settled after the upload is drained and the write result is known.
        audit.settle(&result);

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
        let audit = AuditSlot::of(request.extensions());
        let result = match validated_delete(&request.into_inner()) {
            Ok(input) => {
                audit.account(&input.account);
                let host = Arc::clone(&self.host);
                run_blocking("file operation", to_agent_error, move || {
                    files::delete_entry(host.as_ref(), &input)
                })
                .await
            }
            Err(error) => Err(error),
        };
        audit.settle(&result);

        let result = match result {
            Ok(()) => delete_entry_response::Result::Ok(DeleteEntryOk {}),
            Err(error) => delete_entry_response::Result::Error(error),
        };

        Ok(Response::new(DeleteEntryResponse {
            result: Some(result),
        }))
    }

    /// Lists one page of a directory inside the account's home, as the account.
    ///
    /// Read-only and unaudited (the trail covers what the agent changed, and a
    /// file manager polls this). A dropped client lets the short already-forked
    /// child finish, as every `run_blocking` rpc does; nothing is left behind.
    async fn list_directory(
        &self,
        request: Request<ListDirectoryRequest>,
    ) -> Result<Response<ListDirectoryResponse>, Status> {
        let result = match validated_listing(&request.into_inner()) {
            Ok(input) => {
                let browse = Arc::clone(&self.browse);
                run_blocking("file operation", to_agent_error, move || {
                    files::list_directory(browse.as_ref(), &input)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(listing) => list_directory_response::Result::Ok(to_wire_directory_listing(listing)),
            Err(error) => list_directory_response::Result::Error(error),
        };

        Ok(Response::new(ListDirectoryResponse {
            result: Some(result),
        }))
    }

    /// Streams one file of the account's home in chunks of at most 64 KiB.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). The
    /// read is pure observation: the pump asks the sink for each chunk whether
    /// the channel is still open and returns when it is not, and the forked child
    /// that opened the file is already gone (the descriptor was handed back
    /// before the first byte), so a dropped client costs one closed descriptor.
    ///
    /// The one disclosing rpc, so the one read that is audited: the slot carries
    /// the validated account and is settled at the END of the stream by the pump,
    /// before it drops its sender, with the outcome the client was given. The
    /// line never carries the path.
    async fn read_file(
        &self,
        request: Request<ReadFileRequest>,
    ) -> Result<Response<Self::ReadFileStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (sender, receiver) = mpsc::channel(READ_CHANNEL_CAPACITY);

        match validated_read(&request.into_inner()) {
            Ok(input) => {
                audit.account(input.account());
                let browse = Arc::clone(&self.browse);
                let mut sink = StreamChunkSink::new(sender);

                tokio::task::spawn_blocking(move || {
                    let outcome = files::read_file(browse.as_ref(), &input, &mut sink);

                    // Every ending that is not the client's own choice sends a
                    // terminal error, so a stream never just stops (the same
                    // rule `TailSiteLog` follows).
                    let terminal = read_terminal(&outcome, sink.ending());
                    match &terminal {
                        Some(error) => audit.settle::<()>(&Err(error.clone())),
                        None => audit.ok(),
                    }
                    if let Some(error) = terminal {
                        // Through the SINK, which gives it the deadline a chunk
                        // gets, so this thread comes back whether or not anyone
                        // is listening.
                        sink.terminal(error);
                    }
                });
            }
            Err(error) => {
                audit.settle::<()>(&Err(error.clone()));
                // One message and then the end of the stream: the caller gets
                // the same typed refusal a unary rpc would have given it.
                let _ = sender
                    .send(Ok(ReadFileResponse {
                        result: Some(read_file_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Searches the account's tree for entry names containing a substring.
    ///
    /// Read-only and unaudited (names only). A bound that stops the search is a
    /// successful answer with `truncated` set, never an error. A dropped client
    /// lets the already-forked child finish, as every `run_blocking` rpc does.
    async fn search_entries(
        &self,
        request: Request<SearchEntriesRequest>,
    ) -> Result<Response<SearchEntriesResponse>, Status> {
        let result = match validated_search(&request.into_inner()) {
            Ok(input) => {
                let browse = Arc::clone(&self.browse);
                run_blocking("file operation", to_agent_error, move || {
                    files::search_entries(browse.as_ref(), &input)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(outcome) => search_entries_response::Result::Ok(to_wire_search_outcome(outcome)),
            Err(error) => search_entries_response::Result::Error(error),
        };

        Ok(Response::new(SearchEntriesResponse {
            result: Some(result),
        }))
    }

    /// Renames one entry inside the account's home, as the account, never replacing.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"): one atomic
    /// rename with no safe midpoint to stop at, so a dropped client lets the child finish.
    async fn move_entry(
        &self,
        request: Request<MoveEntryRequest>,
    ) -> Result<Response<MoveEntryResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_move,
            |input| &input.account,
            |host, input| files::move_entry(host, input),
            moved_entry,
        )
        .await)
    }

    /// Creates one directory inside the account's home, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"): a single
    /// `mkdir`, so a dropped client lets the child finish.
    async fn create_directory(
        &self,
        request: Request<CreateDirectoryRequest>,
    ) -> Result<Response<CreateDirectoryResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_create_directory,
            |input| &input.account,
            |host, input| files::create_directory(host, input),
            created_directory,
        )
        .await)
    }

    /// Changes the permission bits of an entry, or of a whole tree, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"), and
    /// idempotent: a recursive change abandoned half-way is finished by asking
    /// again, and stopping it would leave a tree with two modes.
    async fn change_permissions(
        &self,
        request: Request<ChangePermissionsRequest>,
    ) -> Result<Response<ChangePermissionsResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_permissions,
            |input| &input.account,
            |host, input| files::change_permissions(host, input),
            changed_permissions,
        )
        .await)
    }

    /// Copies a file or a tree inside the account's home, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"): the copy is
    /// staged and renamed into place, so what stops early leaves nothing visible
    /// at the destination, and abandoning it mid-way would only waste the work.
    async fn copy_entry(
        &self,
        request: Request<CopyEntryRequest>,
    ) -> Result<Response<CopyEntryResponse>, Status> {
        let floor = self.floor;
        Ok(run_file_operation(
            request,
            &self.host,
            validated_copy,
            |input| &input.account,
            move |host, input| files::copy_entry(host, input, &floor),
            copied_entry,
        )
        .await)
    }

    /// Removes an entry of any kind, and everything under it, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking") and
    /// idempotent: a removal that stopped half-way is finished by running it
    /// again, so the panel's remedy for a lost stream is to ask once more.
    async fn force_delete_entry(
        &self,
        request: Request<ForceDeleteEntryRequest>,
    ) -> Result<Response<ForceDeleteEntryResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_force_delete,
            |input| &input.account,
            |host, input| files::force_delete_entry(host, input),
            force_deleted_entry,
        )
        .await)
    }

    /// Moves an entry into the account's trash, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"): one rename
    /// inside the home, with nothing to reclaim.
    async fn trash_entry(
        &self,
        request: Request<TrashEntryRequest>,
    ) -> Result<Response<TrashEntryResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_trash,
            |input| &input.account,
            |host, input| files::trash_entry(host, input),
            trashed_entry,
        )
        .await)
    }

    /// Lists the entries of the account's trash, newest first.
    ///
    /// Read-only and unaudited. **Cancellation class: stop** in the only sense
    /// that applies to a read: nothing is changed, so a dropped client costs one
    /// short already-forked child that finishes on its own.
    async fn list_trash(
        &self,
        request: Request<ListTrashRequest>,
    ) -> Result<Response<ListTrashResponse>, Status> {
        let result = match validated_account(&request.into_inner().account_username) {
            Ok(account) => {
                let host = Arc::clone(&self.host);
                run_blocking("file operation", to_agent_error, move || {
                    files::list_trash(host.as_ref(), &account)
                })
                .await
            }
            Err(error) => Err(error),
        };

        Ok(Response::new(listed_trash(result)))
    }

    /// Moves an entry out of the account's trash, never replacing what is there.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"): one
    /// no-replace rename, so a dropped client lets the child finish.
    async fn restore_trash_entry(
        &self,
        request: Request<RestoreTrashEntryRequest>,
    ) -> Result<Response<RestoreTrashEntryResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_restore,
            |input| &input.account,
            |host, input| files::restore_trash_entry(host, input),
            restored_trash_entry,
        )
        .await)
    }

    /// Removes everything in the account's trash, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking") and
    /// idempotent: asking again finishes what an abandoned run left.
    async fn empty_trash(
        &self,
        request: Request<EmptyTrashRequest>,
    ) -> Result<Response<EmptyTrashResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            |request| validated_account(&request.account_username),
            |account| account,
            |host, account| files::empty_trash(host, account),
            emptied_trash,
        )
        .await)
    }

    /// Removes trash entries older than the stated number of days, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking") and
    /// idempotent: asking again finishes what an abandoned run left. The agent
    /// has no scheduler, so the panel decides when to ask.
    async fn purge_expired_trash(
        &self,
        request: Request<PurgeExpiredTrashRequest>,
    ) -> Result<Response<PurgeExpiredTrashResponse>, Status> {
        Ok(run_file_operation(
            request,
            &self.host,
            validated_purge,
            |input| &input.account,
            |host, input| files::purge_expired_trash(host, input),
            purged_trash,
        )
        .await)
    }

    /// Creates one archive of the account's files, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and
    /// blocking"). The archive is written to a temporary file and published with a
    /// rename that cannot replace, in one forked child, so it is complete at its
    /// destination or absent; a caller that stops listening changes nothing and
    /// leaves nothing to reclaim. (A child killed by the agent's patience may leave
    /// a hidden `.maran-archive-*` temporary the account owns.)
    async fn create_archive(
        &self,
        request: Request<CreateArchiveRequest>,
    ) -> Result<Response<CreateArchiveResponse>, Status> {
        let floor = self.floor;
        Ok(run_file_operation(
            request,
            &self.archive,
            validated_archive,
            |input| input.account(),
            move |host, input| files::create_archive(host, input, &floor),
            created_archive,
        )
        .await)
    }

    /// Extracts one archive into the account's home, as the account.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and
    /// blocking"). The extraction is one forked child that either finishes or
    /// removes what it made, so a caller that stops listening changes nothing and
    /// there is no half-made state to reclaim. (A child killed by the agent's
    /// patience cannot clean up and may leave files it had written; they belong to
    /// the account.) The archive is parsed in that child, never in the root parent.
    async fn extract_archive(
        &self,
        request: Request<ExtractArchiveRequest>,
    ) -> Result<Response<ExtractArchiveResponse>, Status> {
        let floor = self.floor;
        Ok(run_file_operation(
            request,
            &self.archive,
            validated_extract,
            |input| &input.account,
            move |host, input| files::extract_archive(host, input, &floor),
            extracted_archive,
        )
        .await)
    }
}
