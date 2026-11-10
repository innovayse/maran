//! `TransferService`: exporting an account's backup artifact from this host and
//! importing one received from another, and registering and reading a remote
//! server to migrate from.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::backup::{self, BackupHost};
use maran_ops::transfer::inventory::model::inventory_summary::InventorySummary;
use maran_ops::transfer::{self, DatabaseLoader, TransferHost, TransferWorkHost};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status, Streaming};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::transfer_service_server::TransferService;
use crate::proto::{
    ExportBackupRequest, ExportBackupResponse, GenerateSourceKeyRequest, GenerateSourceKeyResponse,
    ImportBackupRequest, ImportBackupResponse, PullAccountFilesRequest, PullAccountFilesResponse,
    ReadSourceInventoryRequest, ReadSourceInventoryResponse, ScanHostKeyRequest,
    ScanHostKeyResponse, StreamRemoteDatabaseRequest, StreamRemoteDatabaseResponse,
    TestSourceRequest, TestSourceResponse, export_backup_response,
};
use crate::services::transfer::channel_dump_sink::ChannelDumpSink;
use crate::services::transfer::channel_export_sink::ChannelExportSink;
use crate::services::transfer::channel_import_source::ChannelImportSource;
use crate::services::transfer::channel_inventory_sink::ChannelInventorySink;
use crate::services::transfer::channel_pull_sink::ChannelPullSink;
use crate::services::transfer::exported_backup::exported_backup;
use crate::services::transfer::import_failure::ImportFailure;
use crate::services::transfer::read_inventory::read_inventory;
use crate::services::transfer::reported_generated_source_key::reported_generated_source_key;
use crate::services::transfer::reported_import::reported_import;
use crate::services::transfer::reported_scanned_host_keys::reported_scanned_host_keys;
use crate::services::transfer::reported_tested_source::reported_tested_source;
use crate::services::transfer::run_source_call::run_source_call;
use crate::services::transfer::settle_source_call::settle_source_call;
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::terminal_of_dump::terminal_of_dump;
use crate::services::transfer::terminal_of_export::terminal_of_export;
use crate::services::transfer::terminal_of_inventory::terminal_of_inventory;
use crate::services::transfer::terminal_of_pull::terminal_of_pull;
use crate::services::transfer::validated_dump_request::validated_dump_request;
use crate::services::transfer::validated_export::validated_export;
use crate::services::transfer::validated_import::validated_import;
use crate::services::transfer::validated_pull_request::validated_pull_request;
use crate::services::transfer::validated_source_endpoint::validated_source_endpoint;
use crate::services::transfer::validated_source_inputs::validated_source_inputs;
use crate::services::wire::stream_pump::StreamPump;
use crate::services::wire::system_failure::system_failure;

/// How many messages an export may run ahead of the client.
///
/// Eight slots of at most 1 MiB is the ceiling on what one open export costs the
/// root daemon, so a slow client slows the read instead of making the agent buffer
/// an account's backup (rules/rust.md, "no unbounded buffering"). A full channel is
/// not the whole of the back-pressure: the sink gives up on a client that stays
/// silent past its patience.
const EXPORT_CHANNEL_CAPACITY: usize = 8;

/// How many messages an inventory read may run ahead of the client.
///
/// An account is a small message, but a source can hold thousands of them, so the
/// channel is bounded for the reason the export's is: a slow client slows the read
/// instead of making the root daemon buffer a whole server's inventory.
const INVENTORY_CHANNEL_CAPACITY: usize = 32;

/// How many messages a pull or a database transfer may run ahead of the client.
///
/// Their messages are a counter and a step, so a small bound is plenty; it exists
/// so a client that stops reading slows the work at its next message instead of
/// making the root daemon buffer anything.
const TRANSFER_CHANNEL_CAPACITY: usize = 8;

/// The stream `ExportBackup` returns.
type ExportStream = Pin<Box<dyn Stream<Item = Result<ExportBackupResponse, Status>> + Send>>;

/// The stream `ReadSourceInventory` returns.
type InventoryStream =
    Pin<Box<dyn Stream<Item = Result<ReadSourceInventoryResponse, Status>> + Send>>;

/// The stream `PullAccountFiles` returns.
type PullStream = Pin<Box<dyn Stream<Item = Result<PullAccountFilesResponse, Status>> + Send>>;

/// The stream `StreamRemoteDatabase` returns.
type DumpStream = Pin<Box<dyn Stream<Item = Result<StreamRemoteDatabaseResponse, Status>> + Send>>;

/// Serves the rpcs that move a backup artifact between servers and the four that
/// register and read a remote server to migrate from.
///
/// Every rpc is the same three steps: revalidate what the panel sent, run one `ops`
/// operation, map the outcome into the response. The two backup rpcs open no
/// network connection: the bytes cross this host's unix socket and nothing else.
/// The four source rpcs are the agent's only outbound client (issue #197); they
/// connect to the one host the request names, over SSH, and run nothing the closed
/// command list of `ops::transfer` does not contain.
pub struct TransferServiceImpl<B, T, W, L> {
    /// The machine the artifact is listed and read against.
    host: Arc<B>,
    /// The machine the SSH client is run on and the scratch area lives on.
    transfer_host: Arc<T>,
    /// The machine a pull runs `rsync` as an account on and a dump is written on
    /// (issue #197, phase 2).
    work_host: Arc<W>,
    /// Loads a dump a source produced through the restricted loader.
    loader: Arc<L>,
    /// Where platform facts come from: the absolute paths of `ssh`, `ssh-keyscan`
    /// and `ssh-keygen`. A service never branches on a distribution itself
    /// (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
    /// The free-space floor an import is refused below (issue #148).
    floor: FreeSpaceFloor,
}

impl<B, T, W, L> TransferServiceImpl<B, T, W, L>
where
    B: BackupHost + 'static,
    T: TransferHost + 'static,
    W: TransferWorkHost + 'static,
    L: DatabaseLoader + 'static,
{
    /// Creates the service around the hosts it works against, the platform facts
    /// and the floor it refuses below.
    #[must_use]
    pub fn new(
        host: B,
        transfer_host: T,
        work_host: W,
        loader: L,
        distro: &'static dyn maran_distro::DistroAdapter,
        floor: FreeSpaceFloor,
    ) -> Self {
        Self {
            host: Arc::new(host),
            transfer_host: Arc::new(transfer_host),
            work_host: Arc::new(work_host),
            loader: Arc::new(loader),
            distro,
            floor,
        }
    }
}

#[tonic::async_trait]
impl<B, T, W, L> TransferService for TransferServiceImpl<B, T, W, L>
where
    B: BackupHost + 'static,
    T: TransferHost + 'static,
    W: TransferWorkHost + 'static,
    L: DatabaseLoader + 'static,
{
    /// The stream `ExportBackup` returns.
    type ExportBackupStream = ExportStream;

    /// The stream `ReadSourceInventory` returns.
    type ReadSourceInventoryStream = InventoryStream;

    /// The stream `PullAccountFiles` returns.
    type PullAccountFilesStream = PullStream;

    /// The stream `StreamRemoteDatabase` returns.
    type StreamRemoteDatabaseStream = DumpStream;

    /// Streams one published artifact out, after a manifest signed for the host that
    /// will receive it, and ends with exactly one terminal message.
    ///
    /// A refused input ends the stream at once with the same typed refusal a unary
    /// rpc would have given, so a caller handles one shape whether the read started
    /// or not.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). The only
    /// product of the work is the stream and nothing on the host is written, so a
    /// client that goes away or stops reading makes the sink fail, which ends the
    /// read and releases the account's lock. The blocking task is detached, not
    /// aborted — a dropped future cancels nothing — but it checks its sink on every
    /// chunk, so it stops within one send patience of the client leaving. Nothing is
    /// left to reclaim.
    ///
    /// Audited as a mutating rpc although it changes nothing (issue #127, decision
    /// D12): customer data leaving the host is the event the trail exists for.
    async fn export_backup(
        &self,
        request: Request<ExportBackupRequest>,
    ) -> Result<Response<Self::ExportBackupStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (sender, receiver) = mpsc::channel(EXPORT_CHANNEL_CAPACITY);

        match validated_export(request.into_inner()) {
            Ok((account, backup_id, key)) => {
                audit.account(&account);

                tokio::task::spawn_blocking(move || {
                    let mut sink = ChannelExportSink::new(sender);
                    let outcome = backup::export_backup(
                        &account,
                        &backup_id,
                        &LocalBackupRoot::default(),
                        &key,
                        &mut sink,
                    );

                    let result = exported_backup(outcome);
                    audit.settle(&result);
                    sink.terminal(terminal_of_export(result));
                });
            }
            Err(error) => {
                audit.settle::<()>(&Err(error.clone()));
                let _ = sender
                    .send(Ok(ExportBackupResponse {
                        result: Some(export_backup_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Receives one artifact, verifies it against the signed manifest and the
    /// fingerprint the operator confirmed, and publishes it.
    ///
    /// **Cancellation class: stop and reclaim** (rules/rust.md, "Async and
    /// blocking"). The upload itself drives the work: a sender that disappears or
    /// breaks the protocol makes the pump drop the channel without its terminator,
    /// the importer sees an interrupted stream, removes its stage and scratch and
    /// publishes nothing. Publishing is the final rename, so there is no midpoint to
    /// protect, and nothing outlives the call but, at most, an empty backup
    /// directory.
    async fn import_backup(
        &self,
        request: Request<Streaming<ImportBackupRequest>>,
    ) -> Result<Response<ImportBackupResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let mut stream = request.into_inner();

        let outcome = match validated_import(&mut stream).await {
            Ok((import_request, collector)) => {
                let (chunks, pump) = StreamPump::start(stream, collector, Vec::new(), "import");
                let mut source = ChannelImportSource::new(chunks);
                let host = Arc::clone(&self.host);
                let floor = self.floor;

                let imported = tokio::task::spawn_blocking(move || {
                    backup::import_backup(
                        host.as_ref(),
                        &import_request,
                        &mut source,
                        &LocalBackupRoot::default(),
                        &floor,
                    )
                })
                .await;
                // The pump's refusal, if it had one, outranks the importer's
                // failure, which is only its consequence (an interrupted stream).
                let refusal = pump.finish();

                match imported {
                    Ok(Ok(imported)) => Ok(imported),
                    Ok(Err(error)) => {
                        Err(refusal.map_or_else(|| ImportFailure::of(&error), ImportFailure::plain))
                    }
                    Err(join) => Err(ImportFailure::plain(system_failure(format!(
                        "the backup import did not finish: {join}"
                    )))),
                }
            }
            Err(error) => Err(ImportFailure::plain(error)),
        };

        // Recorded only for an import that verified: until the signature holds, the
        // account named in the manifest is a stranger's claim and has no place in
        // the trail.
        match &outcome {
            Ok(imported) => {
                if let Ok(account) = AccountName::parse(&imported.account) {
                    audit.account(&account);
                }
                audit.ok();
            }
            Err(failure) => audit.settle::<()>(&Err(failure.error.clone())),
        }

        Ok(Response::new(reported_import(outcome)))
    }

    /// Mints one ed25519 key pair for a remote source and answers both halves.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and
    /// blocking"). It is a local, bounded step: the key is generated in a scratch
    /// directory only the agent can read, which is removed on every path, so a
    /// client that goes away mid-call leaves nothing behind and there is nothing to
    /// stop or reclaim. No network is touched.
    async fn generate_source_key(
        &self,
        request: Request<GenerateSourceKeyRequest>,
    ) -> Result<Response<GenerateSourceKeyResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (host, distro) = (Arc::clone(&self.transfer_host), self.distro);
        let result =
            run_source_call(move || transfer::generate_source_key(host.as_ref(), distro)).await;

        settle_source_call(&audit, &result);
        Ok(Response::new(reported_generated_source_key(result)))
    }

    /// Reads the host keys a remote host offers and answers their fingerprints.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). The
    /// work is one bounded `ssh-keyscan` run whose only product is the answer, so
    /// a client that goes away has no use for it; the run is detached, not
    /// aborted, because a dropped future cancels nothing, but it is bounded by
    /// its own deadline and writes nothing on either host, so there is nothing to
    /// reclaim.
    async fn scan_host_key(
        &self,
        request: Request<ScanHostKeyRequest>,
    ) -> Result<Response<ScanHostKeyResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let endpoint = validated_source_endpoint(request.into_inner().endpoint);

        let result = match endpoint {
            Ok(endpoint) => {
                let (host, distro) = (Arc::clone(&self.transfer_host), self.distro);
                run_source_call(move || transfer::scan_host_key(host.as_ref(), distro, &endpoint))
                    .await
            }
            Err(failure) => Err(failure),
        };

        settle_source_call(&audit, &result);
        Ok(Response::new(reported_scanned_host_keys(result)))
    }

    /// Connects to a registered source with the pinned host key and runs the fixed
    /// probes.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). Every
    /// probe is bounded by a deadline and nothing on either host changes; the
    /// private key lives in a scratch directory that its guard removes when the
    /// operation returns, on every path, so a client that goes away costs at most
    /// the probes still in flight and leaves nothing to reclaim.
    async fn test_source(
        &self,
        request: Request<TestSourceRequest>,
    ) -> Result<Response<TestSourceResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();
        let inputs = validated_source_inputs(request.endpoint, request.credential);

        let result = match inputs {
            Ok((endpoint, credential)) => {
                let (host, distro) = (Arc::clone(&self.transfer_host), self.distro);
                run_source_call(move || {
                    transfer::test_source(host.as_ref(), distro, &endpoint, &credential)
                })
                .await
            }
            Err(failure) => Err(failure),
        };

        settle_source_call(&audit, &result);
        Ok(Response::new(reported_tested_source(result)))
    }

    /// Reads a registered source's inventory and streams it account by account,
    /// ending with exactly one terminal message.
    ///
    /// A refused input ends the stream at once with the same typed refusal a unary
    /// rpc would have given, so a caller handles one shape whether the read started
    /// or not.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). The only
    /// product of the work is the stream and nothing on either host changes, so a
    /// client that goes away or stops reading makes the sink fail, which ends the
    /// read at its next check without asking the source for anything more. The
    /// blocking task is detached, not aborted — a dropped future cancels nothing —
    /// but it checks its sink between commands and each command has its own
    /// deadline. The private key is removed from the scratch directory when the
    /// task returns, which is when the stream ends or the client leaves; nothing is
    /// left to reclaim.
    async fn read_source_inventory(
        &self,
        request: Request<ReadSourceInventoryRequest>,
    ) -> Result<Response<Self::ReadSourceInventoryStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (sender, receiver) = mpsc::channel(INVENTORY_CHANNEL_CAPACITY);
        let request = request.into_inner();
        let inputs = validated_source_inputs(request.endpoint, request.credential);

        match inputs {
            Ok((endpoint, credential)) => {
                let (host, distro) = (Arc::clone(&self.transfer_host), self.distro);

                tokio::task::spawn_blocking(move || {
                    let mut sink = ChannelInventorySink::new(sender);
                    let result =
                        read_inventory(host.as_ref(), distro, &endpoint, &credential, &mut sink)
                            .map_err(|error| SourceFailure::of(&error));

                    settle_source_call(&audit, &result);
                    let (terminal, reason) = terminal_of_inventory(result);
                    sink.terminal(terminal, reason);
                });
            }
            Err(failure) => {
                let result: Result<InventorySummary, SourceFailure> = Err(failure);
                settle_source_call(&audit, &result);
                let (terminal, reason) = terminal_of_inventory(result);
                let _ = sender
                    .send(Ok(ReadSourceInventoryResponse {
                        result: Some(terminal),
                        failure_reason: reason as i32,
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Copies an account's home from a registered source, slice by slice, and ends
    /// with exactly one terminal message.
    ///
    /// A refused input ends the stream at once with the same typed refusal a unary
    /// rpc would have given, so a caller handles one shape whether the pull started
    /// or not.
    ///
    /// **Cancellation class: stop** (rules/rust.md, "Async and blocking"). The pull
    /// asks its sink between slices whether the client is still there, and a client
    /// that went away or stopped reading ends it at the next slice boundary, at most
    /// one slice deadline later. The partial state in the account's home is kept on
    /// purpose, so that calling the rpc again resumes (this is how the panel pauses
    /// and cancels); the private key directories are removed when the task returns.
    /// A dropped future cancels nothing here, because the whole body runs in
    /// `spawn_blocking`.
    async fn pull_account_files(
        &self,
        request: Request<PullAccountFilesRequest>,
    ) -> Result<Response<Self::PullAccountFilesStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (sender, receiver) = mpsc::channel(TRANSFER_CHANNEL_CAPACITY);

        match validated_pull_request(request.into_inner()) {
            Ok(pull) => {
                audit.account(&pull.account);
                let (host, work, distro) = (
                    Arc::clone(&self.transfer_host),
                    Arc::clone(&self.work_host),
                    self.distro,
                );

                tokio::task::spawn_blocking(move || {
                    let mut sink = ChannelPullSink::new(sender);
                    let result = transfer::pull_account_files(
                        host.as_ref(),
                        work.as_ref(),
                        distro,
                        &pull,
                        &mut sink,
                    )
                    .map_err(|error| SourceFailure::of(&error));

                    settle_source_call(&audit, &result);
                    let (terminal, reason, transfer_reason) = terminal_of_pull(result);
                    sink.terminal(terminal, reason, transfer_reason);
                });
            }
            Err(failure) => {
                let result: Result<transfer::model::pull_summary::PullSummary, SourceFailure> =
                    Err(failure);
                settle_source_call(&audit, &result);
                let (terminal, reason, transfer_reason) = terminal_of_pull(result);
                let _ = sender
                    .send(Ok(PullAccountFilesResponse {
                        result: Some(terminal),
                        failure_reason: reason as i32,
                        transfer_failure_reason: transfer_reason as i32,
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Dumps one database of a registered source and loads it into one here
    /// through the restricted loader, and ends with exactly one terminal message.
    ///
    /// **Cancellation class: run to completion and reclaim** (rules/rust.md,
    /// "Async and blocking"). Loading a dump has no safe midpoint: a load stopped
    /// halfway leaves a database that is neither the source's nor empty. A client
    /// that goes away therefore does not stop the work (the steps it can no longer
    /// hear are simply not delivered), and the dump file and the loader's temporary
    /// user are removed on every path. The panel's remedy for a transfer it lost is
    /// to drop the half-loaded database and ask again.
    async fn stream_remote_database(
        &self,
        request: Request<StreamRemoteDatabaseRequest>,
    ) -> Result<Response<Self::StreamRemoteDatabaseStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (sender, receiver) = mpsc::channel(TRANSFER_CHANNEL_CAPACITY);
        let request = request.into_inner();
        if let Ok(account) = AccountName::parse(&request.account_username) {
            audit.account(&account);
        }

        match validated_dump_request(request) {
            Ok(dump) => {
                let (host, work, loader, distro) = (
                    Arc::clone(&self.transfer_host),
                    Arc::clone(&self.work_host),
                    Arc::clone(&self.loader),
                    self.distro,
                );

                tokio::task::spawn_blocking(move || {
                    let mut sink = ChannelDumpSink::new(sender);
                    let result = transfer::dump_remote_database(
                        host.as_ref(),
                        work.as_ref(),
                        loader.as_ref(),
                        distro,
                        &dump,
                        &mut sink,
                    )
                    .map_err(|error| SourceFailure::of(&error));

                    settle_source_call(&audit, &result);
                    let (terminal, reason, transfer_reason) = terminal_of_dump(result);
                    sink.terminal(terminal, reason, transfer_reason);
                });
            }
            Err(failure) => {
                let result: Result<transfer::model::dump_summary::DumpSummary, SourceFailure> =
                    Err(failure);
                settle_source_call(&audit, &result);
                let (terminal, reason, transfer_reason) = terminal_of_dump(result);
                let _ = sender
                    .send(Ok(StreamRemoteDatabaseResponse {
                        result: Some(terminal),
                        failure_reason: reason as i32,
                        transfer_failure_reason: transfer_reason as i32,
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }
}
