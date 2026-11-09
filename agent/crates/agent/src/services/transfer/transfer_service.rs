//! `TransferService`: exporting an account's backup artifact from this host and
//! importing one received from another.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::backup::{self, BackupHost};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status, Streaming};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::transfer_service_server::TransferService;
use crate::proto::{
    ExportBackupRequest, ExportBackupResponse, ImportBackupRequest, ImportBackupResponse,
    export_backup_response,
};
use crate::services::transfer::channel_export_sink::ChannelExportSink;
use crate::services::transfer::channel_import_source::ChannelImportSource;
use crate::services::transfer::exported_backup::exported_backup;
use crate::services::transfer::import_failure::ImportFailure;
use crate::services::transfer::reported_import::reported_import;
use crate::services::transfer::terminal_of_export::terminal_of_export;
use crate::services::transfer::validated_export::validated_export;
use crate::services::transfer::validated_import::validated_import;
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

/// The stream `ExportBackup` returns.
type ExportStream = Pin<Box<dyn Stream<Item = Result<ExportBackupResponse, Status>> + Send>>;

/// Serves the two rpcs that move a backup artifact between servers.
///
/// Every rpc is the same three steps: revalidate what the panel sent, run one `ops`
/// operation, map the outcome into the response. The agent opens no network
/// connection for either: the bytes cross this host's unix socket and nothing else.
pub struct TransferServiceImpl<B> {
    /// The machine the artifact is listed and read against.
    host: Arc<B>,
    /// The free-space floor an import is refused below (issue #148).
    floor: FreeSpaceFloor,
}

impl<B: BackupHost + 'static> TransferServiceImpl<B> {
    /// Creates the service around the host it imports against and the floor it
    /// refuses below.
    #[must_use]
    pub fn new(host: B, floor: FreeSpaceFloor) -> Self {
        Self {
            host: Arc::new(host),
            floor,
        }
    }
}

#[tonic::async_trait]
impl<B: BackupHost + 'static> TransferService for TransferServiceImpl<B> {
    /// The stream `ExportBackup` returns.
    type ExportBackupStream = ExportStream;

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
}
