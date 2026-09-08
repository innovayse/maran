//! `BackupService`: creating, restoring, listing and deleting an account's
//! backups.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_ops::backup::{self, BackupHost};
use maran_ops::db::DbHost;
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status};

use crate::proto::backup_service_server::BackupService;
use crate::proto::{
    AgentError, CreateBackupOk, CreateBackupRequest, CreateBackupResponse, DeleteBackupOk,
    DeleteBackupRequest, DeleteBackupResponse, ListBackupsOk, ListBackupsRequest,
    ListBackupsResponse, ProbeDestinationRequest, ProbeDestinationResponse, RestoreBackupOk,
    RestoreBackupRequest, RestoreBackupResponse, create_backup_response, delete_backup_response,
    list_backups_response, probe_destination_response, restore_backup_response,
};
use crate::services::backup::backup_status::to_agent_error;
use crate::services::backup::channel_progress_sink::ChannelProgressSink;
use crate::services::backup::channel_restore_sink::ChannelRestoreSink;
use crate::services::backup::db_host_catalog::DbHostCatalog;
use crate::services::backup::home_group::home_group;
use crate::services::backup::to_backup_info::to_backup_info;
use crate::services::backup::validated_destination::validated_destination;
use crate::services::backup::validated_restore::ValidatedRestore;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::run_blocking::run_blocking;
use crate::services::wire::system_failure::system_failure;
use crate::services::wire::validated_account::validated_account;

/// How many messages a long-running backup rpc may run ahead of the client.
///
/// A creation emits at most a report per database plus a few stage boundaries,
/// and a restore a few more, so this is rarely a constraint. It is bounded all
/// the same: a channel with no ceiling is a queue inside the root daemon whose
/// size a client chooses by not reading (rules/rust.md, "no unbounded
/// buffering"). Progress that does not fit is dropped rather than waited on —
/// see the two sinks — and the terminal message is sent by the handler.
const BACKUP_CHANNEL_CAPACITY: usize = 16;

/// The stream `CreateBackup` returns.
type CreateStream = Pin<Box<dyn Stream<Item = Result<CreateBackupResponse, Status>> + Send>>;

/// The stream `RestoreBackup` returns.
type RestoreStream = Pin<Box<dyn Stream<Item = Result<RestoreBackupResponse, Status>> + Send>>;

/// Serves the backup operations over the wire.
///
/// Every rpc is the same three steps: revalidate what the panel sent, run one
/// `ops` operation, map the outcome into the response's `oneof`. Domain
/// failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on (rules/proto.md).
///
/// **Two things this service decides that `ops` cannot.** First, the
/// destination: `ops::backup`'s operations take a local root and nothing else,
/// so an S3 destination is refused here — see
/// [`validated_destination`](crate::services::backup::validated_destination)
/// and the module doc. Second, the composition `ops::backup` deliberately
/// leaves open: the databases an account owns are the database area's
/// knowledge, and the backup area asks for them through a seam this service
/// fills with [`DbHostCatalog`].
pub struct BackupServiceImpl<B, D> {
    /// The machine the archiving, dumping and loading runs against.
    host: Arc<B>,
    /// The database server the account's databases are listed from and
    /// reloaded into.
    db_host: Arc<D>,
    /// Where platform facts come from. The one fact this service asks for is
    /// the web server's group, which a restore re-applies to the home root; a
    /// service never branches on a distribution itself (rules/rust.md).
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<B: BackupHost + 'static, D: DbHost + 'static> BackupServiceImpl<B, D> {
    /// Creates the service around the two hosts it runs operations against.
    #[must_use]
    pub fn new(host: B, db_host: D, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            db_host: Arc::new(db_host),
            distro,
        }
    }
}

#[tonic::async_trait]
impl<B: BackupHost + 'static, D: DbHost + 'static> BackupService for BackupServiceImpl<B, D> {
    /// The stream `CreateBackup` returns.
    type CreateBackupStream = CreateStream;

    /// The stream `RestoreBackup` returns.
    type RestoreBackupStream = RestoreStream;

    /// Creates a backup, streaming `Progress` and ending with a terminal
    /// message.
    ///
    /// The stream always ends with exactly one terminal message — the ok or the
    /// error — and never with a bare gRPC status for a domain outcome. A
    /// refused input ends it immediately with that same typed refusal, so a
    /// caller handles one shape whether the work started or not.
    async fn create_backup(
        &self,
        request: Request<CreateBackupRequest>,
    ) -> Result<Response<Self::CreateBackupStream>, Status> {
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(BACKUP_CHANNEL_CAPACITY);

        match validated_creation(&request) {
            Ok((account, backup_id, root)) => {
                let host = Arc::clone(&self.host);
                let catalog = DbHostCatalog::new(Arc::clone(&self.db_host));
                let progress = sender.clone();

                tokio::task::spawn_blocking(move || {
                    let mut sink = ChannelProgressSink::new(progress);
                    let outcome = backup::create_backup(
                        host.as_ref(),
                        &catalog,
                        &account,
                        &backup_id,
                        &root,
                        &mut sink,
                    );

                    let terminal = match outcome {
                        Ok(summary) => match summary.readable_details() {
                            Some(details) => create_backup_response::Result::Ok(CreateBackupOk {
                                size_bytes: details.artifact_bytes,
                                sha256: details.artifact_sha256.clone(),
                                database_count: details.manifest.databases.len() as u64,
                            }),
                            // Unreachable through `create_backup`, which
                            // returns the summary it just wrote and can only
                            // write a readable one. Answered rather than
                            // asserted because a root daemon does not panic on
                            // a reasoning step (rules/rust.md), and answered as
                            // a failure rather than as a success with zeroes: a
                            // terminal ok carrying an empty digest is a backup
                            // the panel would record and no restore could ever
                            // verify.
                            None => create_backup_response::Result::Error(system_failure(
                                "the created backup describes nothing".to_owned(),
                            )),
                        },
                        Err(error) => create_backup_response::Result::Error(to_agent_error(&error)),
                    };

                    let _ = sender.blocking_send(Ok(CreateBackupResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                let _ = sender
                    .send(Ok(CreateBackupResponse {
                        result: Some(create_backup_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Restores an account from a backup, streaming `Progress` and ending with
    /// a terminal message.
    ///
    /// The terminal ok reports what the restore DID — whether the home was
    /// swapped, and how many of the databases it set out to replace it
    /// replaced. Nothing here collapses those into a boolean: a caller states
    /// the verdict by comparing them, which is the shape `RestoreOutcome` was
    /// given after a cascade reported completion over rows it never touched.
    async fn restore_backup(
        &self,
        request: Request<RestoreBackupRequest>,
    ) -> Result<Response<Self::RestoreBackupStream>, Status> {
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(BACKUP_CHANNEL_CAPACITY);

        match validated_restoration(&request, self.distro) {
            Ok((account, input, group)) => {
                let host = Arc::clone(&self.host);
                let progress = sender.clone();

                tokio::task::spawn_blocking(move || {
                    let mut sink = ChannelRestoreSink::new(progress);
                    let outcome = backup::restore_backup(
                        host.as_ref(),
                        &account,
                        &input.backup_id,
                        &input.root,
                        &input.allowed_databases,
                        &input.expected_sha256,
                        group,
                        &mut sink,
                    );

                    let terminal = match outcome {
                        Ok(restored) => restore_backup_response::Result::Ok(RestoreBackupOk {
                            files_restored: restored.files_restored,
                            databases_restored: restored.databases_restored,
                            databases_total: restored.databases_total,
                            // Always empty here, and read from the outcome
                            // rather than invented: a restore that reaches its
                            // ok message rolled nothing back. The lists that
                            // are not empty ride on `BackupError::RolledBack`
                            // and `RolledBackPartially`, which arrive as the
                            // error arm and name them in the message.
                            not_rolled_back: Vec::new(),
                        }),
                        Err(error) => {
                            restore_backup_response::Result::Error(to_agent_error(&error))
                        }
                    };

                    let _ = sender.blocking_send(Ok(RestoreBackupResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                let _ = sender
                    .send(Ok(RestoreBackupResponse {
                        result: Some(restore_backup_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Lists the backups this agent holds for an account.
    ///
    /// The request carries no destination, so the root is the agent's own — the
    /// same one every local creation writes to, which is why a per-call
    /// subdirectory is refused on the other rpcs rather than honoured.
    async fn list_backups(
        &self,
        request: Request<ListBackupsRequest>,
    ) -> Result<Response<ListBackupsResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_account(&request.account_username) {
            Ok(account) => {
                match run_blocking("backup listing", to_agent_error, move || {
                    backup::list_backups(&LocalBackupRoot::default(), &account)
                })
                .await
                {
                    Ok(summaries) => list_backups_response::Result::Ok(ListBackupsOk {
                        backups: summaries.into_iter().map(to_backup_info).collect(),
                    }),
                    Err(error) => list_backups_response::Result::Error(error),
                }
            }
            Err(error) => list_backups_response::Result::Error(error),
        };

        Ok(Response::new(ListBackupsResponse {
            result: Some(result),
        }))
    }

    /// Deletes a backup's artifact and its sidecar.
    ///
    /// Idempotent: a backup that is not there answers
    /// [`ErrorCode::NotFound`](crate::proto::ErrorCode::NotFound), which a
    /// caller reads as a converged retry rather than as a fault.
    async fn delete_backup(
        &self,
        request: Request<DeleteBackupRequest>,
    ) -> Result<Response<DeleteBackupResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_removal(&request) {
            Ok((account, backup_id, root)) => {
                match run_blocking("backup deletion", to_agent_error, move || {
                    backup::delete_backup(&root, &account, &backup_id)
                })
                .await
                {
                    Ok(()) => delete_backup_response::Result::Ok(DeleteBackupOk {}),
                    Err(error) => delete_backup_response::Result::Error(error),
                }
            }
            Err(error) => delete_backup_response::Result::Error(error),
        };

        Ok(Response::new(DeleteBackupResponse {
            result: Some(result),
        }))
    }

    /// Answers whether a destination would serve this product's archives to
    /// anyone.
    ///
    /// This agent performs no probe: the operation that writes the canary
    /// object and fetches it back anonymously does not exist in `ops`, and a
    /// service may not compose one — that would be business logic in a
    /// translation layer (rules/rust.md "Service anatomy"). So every request is
    /// refused, and the refusal is the same one an S3 destination gets
    /// everywhere else in this service.
    ///
    /// Refused, and never answered
    /// [`PublicReadVerdict::Private`](crate::proto::PublicReadVerdict::Private):
    /// a probe that was not made establishes nothing, and reporting a
    /// destination as private on the strength of a request nobody sent is
    /// exactly the reading the three-state verdict exists to prevent.
    async fn probe_destination(
        &self,
        request: Request<ProbeDestinationRequest>,
    ) -> Result<Response<ProbeDestinationResponse>, Status> {
        let request = request.into_inner();

        // The destination is validated first, so a malformed one is still
        // reported as malformed, and then refused whichever kind it named — a
        // local directory has no anonymous reader to ask about, and the remote
        // arm has no probe.
        let error = match validated_destination(request.destination.as_ref()) {
            Ok(_local) => {
                crate::services::backup::remote_destination_refused::remote_destination_refused()
            }
            Err(error) => error,
        };

        Ok(Response::new(ProbeDestinationResponse {
            result: Some(probe_destination_response::Result::Error(error)),
        }))
    }
}

/// Revalidates everything a creation carries.
///
/// A free function rather than a method so that the three checks are one unit a
/// test can drive without a host (rules/rust.md "Service anatomy": a decision
/// inlined in a handler can be deleted without a test going red).
///
/// # Errors
///
/// The wire error for an account name, a backup id or a destination the agent
/// will not accept.
fn validated_creation(
    request: &CreateBackupRequest,
) -> Result<
    (
        maran_agent_core::validation::system::name::AccountName,
        BackupId,
        LocalBackupRoot,
    ),
    AgentError,
> {
    let account = validated_account(&request.account_username)?;
    let backup_id =
        BackupId::parse(&request.backup_id).map_err(|error| invalid_input(error.to_string()))?;
    let root = validated_destination(request.destination.as_ref())?;

    Ok((account, backup_id, root))
}

/// Revalidates everything a deletion carries.
///
/// # Errors
///
/// As [`validated_creation`].
fn validated_removal(
    request: &DeleteBackupRequest,
) -> Result<
    (
        maran_agent_core::validation::system::name::AccountName,
        BackupId,
        LocalBackupRoot,
    ),
    AgentError,
> {
    let account = validated_account(&request.account_username)?;
    let backup_id =
        BackupId::parse(&request.backup_id).map_err(|error| invalid_input(error.to_string()))?;
    let root = validated_destination(request.destination.as_ref())?;

    Ok((account, backup_id, root))
}

/// Revalidates everything a restore carries, and resolves the group its home
/// must end up owned by.
///
/// The group is resolved BEFORE the operation starts rather than inside it, so
/// a host whose web server group is missing refuses the restore instead of
/// discovering it at the finalising step — after the databases have been
/// replaced.
///
/// # Errors
///
/// The wire error for an account name, a destination, an id, a checksum or a
/// database name the agent will not accept, or a system failure when the web
/// server's group cannot be resolved.
fn validated_restoration(
    request: &RestoreBackupRequest,
    distro: &dyn maran_distro::DistroAdapter,
) -> Result<
    (
        maran_agent_core::validation::system::name::AccountName,
        ValidatedRestore,
        u32,
    ),
    AgentError,
> {
    let account = validated_account(&request.account_username)?;
    let input = ValidatedRestore::from_request(request, &account)?;
    let group = home_group(distro)?;

    Ok((account, input, group))
}

#[cfg(test)]
#[path = "../../tests/services/backup/backup_service_tests.rs"]
mod tests;
