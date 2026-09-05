//! `PhpService`: which PHP runtimes this host has, and installing another.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_ops::php::{self, PhpHost};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status};

use crate::proto::php_service_server::PhpService;
use crate::proto::{
    AgentError, ErrorCode, InstallPhpVersionOk, InstallPhpVersionRequest,
    InstallPhpVersionResponse, ListPhpVersionsOk, ListPhpVersionsRequest, ListPhpVersionsResponse,
    PhpVersion as ProtoVersion, Progress, install_php_version_response, list_php_versions_response,
};
use crate::services::php::php_status::to_agent_error;
use crate::services::wire::run_blocking::run_blocking;

/// How many messages an installation may run ahead of the client.
///
/// The operation emits five in total — four stages and one terminal message —
/// so this is never a constraint in practice. It is bounded all the same: a
/// channel with no ceiling is a queue in the root daemon whose size a client
/// chooses by not reading, and every stream in this crate is bounded for that
/// reason (rules/rust.md "Streams stay bounded").
const INSTALL_CHANNEL_CAPACITY: usize = 8;

/// The stream `InstallPhpVersion` returns.
type InstallStream = Pin<Box<dyn Stream<Item = Result<InstallPhpVersionResponse, Status>> + Send>>;

/// Serves the PHP operations over the wire.
///
/// Two rpcs and the same three steps in each: revalidate what the panel sent,
/// run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on — an unsupported version is information, not
/// a transport error (rules/proto.md).
pub struct PhpServiceImpl<H> {
    /// The machine the PHP operations run against.
    host: Arc<H>,
    /// Where platform facts come from — the package manager, the package name
    /// and the pool directory of each version. A service never branches on a
    /// distribution itself (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<H: PhpHost + 'static> PhpServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            distro,
        }
    }

    /// Revalidates a version string arriving from the panel.
    ///
    /// The API validated it already. This is the agent's own check, and it
    /// exists because the agent runs as root and the API does not
    /// (rules/security.md item 1, which requires revalidation in the agent and
    /// not only at the API boundary): the version becomes half a package name
    /// and a socket path.
    ///
    /// # Errors
    ///
    /// Returns the wire error for a version that is not two components.
    fn validated(version: &str) -> Result<PhpVersion, AgentError> {
        PhpVersion::parse(version).map_err(|error| AgentError {
            code: ErrorCode::InvalidInput as i32,
            message: error.to_string(),
            tool_output: String::new(),
        })
    }
}

#[tonic::async_trait]
impl<H: PhpHost + 'static> PhpService for PhpServiceImpl<H> {
    /// The stream `InstallPhpVersion` returns.
    type InstallPhpVersionStream = InstallStream;

    /// Lists the versions installed on this host, newest first.
    async fn list_php_versions(
        &self,
        _request: Request<ListPhpVersionsRequest>,
    ) -> Result<Response<ListPhpVersionsResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);

        let result = match run_blocking("PHP operation", to_agent_error, move || {
            php::list_php_versions(host.as_ref(), distro)
        })
        .await
        {
            Ok(versions) => list_php_versions_response::Result::Ok(ListPhpVersionsOk {
                versions: versions
                    .into_iter()
                    .map(|installed| ProtoVersion {
                        version: installed.version,
                        fpm_socket_directory: installed.socket_directory,
                        // Left UNSET, not false. `list_php_versions` does not
                        // establish which version is the host's default CLI
                        // PHP, and a service may not go and find out — that
                        // would be filesystem access in a translation layer
                        // (rules/rust.md "Service anatomy"). `false` on the
                        // wire is a claim the agent cannot support, and the
                        // panel could not tell it from "not known"; the field
                        // has explicit presence so that absence says so.
                        is_default: None,
                    })
                    .collect(),
            }),
            Err(error) => list_php_versions_response::Result::Error(error),
        };

        Ok(Response::new(ListPhpVersionsResponse {
            result: Some(result),
        }))
    }

    /// Installs a version, streaming `Progress` and ending with a terminal
    /// message.
    ///
    /// The stream is produced by `ops` and this only wraps it
    /// (rules/rust.md): `install_php_version` calls a progress callback, and
    /// the callback here is the only thing that turns a stage into a message.
    /// The stream always ends with exactly one terminal message — the ok or
    /// the error — and never with a bare gRPC status for a domain outcome.
    ///
    /// It is bounded: the channel has a ceiling, and a client that drops the
    /// stream closes it, after which each remaining send fails immediately
    /// rather than blocking. The package manager itself is not interrupted —
    /// half an `apt-get install` is a broken host, and the panel retries the
    /// rpc, which is idempotent — but nothing accumulates on its behalf.
    async fn install_php_version(
        &self,
        request: Request<InstallPhpVersionRequest>,
    ) -> Result<Response<Self::InstallPhpVersionStream>, Status> {
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(INSTALL_CHANNEL_CAPACITY);

        match Self::validated(&request.version) {
            Ok(version) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                let progress = sender.clone();
                let installed = version.as_str().to_owned();

                tokio::task::spawn_blocking(move || {
                    let outcome = php::install_php_version(
                        host.as_ref(),
                        distro,
                        &version,
                        |percent, stage| {
                            let _ = progress.blocking_send(Ok(InstallPhpVersionResponse {
                                result: Some(install_php_version_response::Result::Progress(
                                    Progress {
                                        percent,
                                        stage: stage.to_owned(),
                                    },
                                )),
                            }));
                        },
                    );

                    let terminal = match outcome {
                        Ok(()) => install_php_version_response::Result::Ok(InstallPhpVersionOk {
                            version: installed,
                        }),
                        Err(error) => {
                            install_php_version_response::Result::Error(to_agent_error(&error))
                        }
                    };

                    let _ = sender.blocking_send(Ok(InstallPhpVersionResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                // One terminal message and then the end of the stream: the
                // caller gets the same typed refusal a unary rpc would give.
                let _ = sender
                    .send(Ok(InstallPhpVersionResponse {
                        result: Some(install_php_version_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }
}
