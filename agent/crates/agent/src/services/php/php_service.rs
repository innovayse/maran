//! `PhpService`: which PHP runtimes this host has, which it could install, and
//! installing another, and which extensions each version loads.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_ops::php::{self, PhpHost};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::php_service_server::PhpService;
use crate::proto::{
    AgentError, ErrorCode, InstallPhpVersionOk, InstallPhpVersionRequest,
    InstallPhpVersionResponse, InstallablePhpVersion, ListInstallablePhpVersionsOk,
    ListInstallablePhpVersionsRequest, ListInstallablePhpVersionsResponse, ListPhpVersionsOk,
    ListPhpVersionsRequest, ListPhpVersionsResponse, PhpVersion as ProtoVersion, Progress,
    install_php_version_response, list_installable_php_versions_response,
    list_php_versions_response,
};
use crate::proto::{
    ApplyPhpPoolSettingsOk, ApplyPhpPoolSettingsRequest, ApplyPhpPoolSettingsResponse,
    apply_php_pool_settings_response,
};
use crate::proto::{
    ListPhpExtensionsOk, ListPhpExtensionsRequest, ListPhpExtensionsResponse, SetPhpExtensionOk,
    SetPhpExtensionRequest, SetPhpExtensionResponse, list_php_extensions_response,
    set_php_extension_response,
};
use crate::services::php::php_status::to_agent_error;
use crate::services::php::reported_php_extension::reported_php_extension;
use crate::services::php::validated_extension_request::validated_extension_request;
use crate::services::php::validated_pool_settings::validated_pool_settings;
use crate::services::wire::run_blocking::run_blocking;
use crate::services::wire::validated_account::validated_account;

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

/// How many messages an extension change may run ahead of the client.
///
/// The operation emits at most six progress messages and one terminal message,
/// so this is never a constraint in practice; it is bounded all the same, for
/// the reason `INSTALL_CHANNEL_CAPACITY` is.
const SET_EXTENSION_CHANNEL_CAPACITY: usize = 8;

/// The stream `SetPhpExtension` returns.
type SetExtensionStream =
    Pin<Box<dyn Stream<Item = Result<SetPhpExtensionResponse, Status>> + Send>>;

/// Serves the PHP operations over the wire.
///
/// Every rpc takes the same three steps: revalidate what the panel sent,
/// run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on — an unsupported version is information, not
/// a transport error (rules/proto.md).
pub struct PhpServiceImpl<H> {
    /// The machine the PHP operations run against.
    host: Arc<H>,
    /// Where platform facts come from — the service manager, the package
    /// name (which is the install unit's instance) and the pool directory of
    /// each version. A service never branches on a
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

    /// The stream `SetPhpExtension` returns.
    type SetPhpExtensionStream = SetExtensionStream;

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

    /// Lists the versions this host can install, newest first, each flagged
    /// as installed or not.
    async fn list_installable_php_versions(
        &self,
        _request: Request<ListInstallablePhpVersionsRequest>,
    ) -> Result<Response<ListInstallablePhpVersionsResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);

        let result = match run_blocking("PHP operation", to_agent_error, move || {
            php::list_installable_php_versions(host.as_ref(), distro)
        })
        .await
        {
            Ok(versions) => {
                list_installable_php_versions_response::Result::Ok(ListInstallablePhpVersionsOk {
                    versions: versions
                        .into_iter()
                        .map(|item| InstallablePhpVersion {
                            version: item.version,
                            installed: item.installed,
                        })
                        .collect(),
                })
            }
            Err(error) => list_installable_php_versions_response::Result::Error(error),
        };

        Ok(Response::new(ListInstallablePhpVersionsResponse {
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
    /// The agent does not run the package manager: it starts the installer's
    /// `maran-app-install@<package>` unit and waits for it, because its own
    /// sandbox keeps `/usr` read-only.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and
    /// blocking"), with nothing to reclaim afterwards. A package manager
    /// stopped part-way is a broken host, and the rpc is idempotent, so the
    /// panel's remedy for a stream it lost is to ask again rather than for the
    /// agent to undo anything. The next paragraph is the other half of that
    /// choice: work that outlives its client must still leave nothing
    /// accumulating on its behalf.
    ///
    /// It is bounded: the channel has a ceiling, and a client that drops the
    /// stream closes it, after which each remaining send fails immediately
    /// rather than blocking. The package manager is not interrupted, and
    /// the agent could not interrupt it: it runs in its own unit's cgroup, so
    /// neither a dropped client nor a restart of the agent stops it — half a
    /// package-manager run is a broken host, and the panel retries the rpc,
    /// which is idempotent — and nothing accumulates on its behalf.
    async fn install_php_version(
        &self,
        request: Request<InstallPhpVersionRequest>,
    ) -> Result<Response<Self::InstallPhpVersionStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
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

                    let outcome = outcome.map_err(|error| to_agent_error(&error));
                    // Settled on the terminal message, before it is sent.
                    audit.settle(&outcome);

                    let terminal = match outcome {
                        Ok(()) => install_php_version_response::Result::Ok(InstallPhpVersionOk {
                            version: installed,
                        }),
                        Err(error) => install_php_version_response::Result::Error(error),
                    };

                    let _ = sender.blocking_send(Ok(InstallPhpVersionResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                audit.settle::<()>(&Err(error.clone()));
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

    /// Lists the thirteen catalogue extensions of one installed version,
    /// alphabetical, each with whether php-fpm has it loaded and what this
    /// host can do with it. Read-only: it yields no audit line.
    async fn list_php_extensions(
        &self,
        request: Request<ListPhpExtensionsRequest>,
    ) -> Result<Response<ListPhpExtensionsResponse>, Status> {
        let request = request.into_inner();

        let result = match Self::validated(&request.version) {
            Err(error) => list_php_extensions_response::Result::Error(error),
            Ok(version) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);

                match run_blocking("PHP operation", to_agent_error, move || {
                    php::list_php_extensions(host.as_ref(), distro, &version)
                })
                .await
                {
                    Ok(extensions) => {
                        list_php_extensions_response::Result::Ok(ListPhpExtensionsOk {
                            extensions: extensions.iter().map(reported_php_extension).collect(),
                        })
                    }
                    Err(error) => list_php_extensions_response::Result::Error(error),
                }
            }
        };

        Ok(Response::new(ListPhpExtensionsResponse {
            result: Some(result),
        }))
    }

    /// Switches one catalogue extension on or off for one version, streaming
    /// `Progress` and ending with a terminal message.
    ///
    /// The stream is produced by `ops` and this only wraps it, exactly as for
    /// `install_php_version`: the progress callback is the only thing that turns
    /// a stage into a message, and the stream always ends with exactly one
    /// terminal message, the ok or the error. The audit slot names no account or
    /// site (the change is host-wide for the version) and is settled with the
    /// result that becomes the terminal message.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and
    /// blocking"), with nothing to reclaim afterwards. Turning an extension on
    /// runs a package manager in its own unit and then a switch and a reload;
    /// abandoning that part-way leaves a broken host or a switch applied but not
    /// checked, and a dropped client cannot stop the unit anyway. The rpc is
    /// idempotent, so the panel's remedy for a stream it lost is to ask again.
    /// The channel is bounded and a dropped client makes each remaining send
    /// fail at once rather than block.
    async fn set_php_extension(
        &self,
        request: Request<SetPhpExtensionRequest>,
    ) -> Result<Response<Self::SetPhpExtensionStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(SET_EXTENSION_CHANNEL_CAPACITY);

        match validated_extension_request(&request.version, &request.extension) {
            Ok((version, extension)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                let progress = sender.clone();
                let enabled = request.enabled;

                tokio::task::spawn_blocking(move || {
                    let outcome = php::set_php_extension(
                        host.as_ref(),
                        distro,
                        &version,
                        extension,
                        enabled,
                        |percent, stage| {
                            let _ = progress.blocking_send(Ok(SetPhpExtensionResponse {
                                result: Some(set_php_extension_response::Result::Progress(
                                    Progress {
                                        percent,
                                        stage: stage.to_owned(),
                                    },
                                )),
                            }));
                        },
                    );

                    let outcome = outcome.map_err(|error| to_agent_error(&error));
                    // Settled on the terminal message, before it is sent.
                    audit.settle(&outcome);

                    let terminal = match outcome {
                        Ok(()) => set_php_extension_response::Result::Ok(SetPhpExtensionOk {
                            version: version.as_str().to_owned(),
                            extension: extension.as_str().to_owned(),
                            enabled,
                        }),
                        Err(error) => set_php_extension_response::Result::Error(error),
                    };

                    let _ = sender.blocking_send(Ok(SetPhpExtensionResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                audit.settle::<()>(&Err(error.clone()));
                let _ = sender
                    .send(Ok(SetPhpExtensionResponse {
                        result: Some(set_php_extension_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Rewrites one account's existing pool with a new worker budget and a new
    /// set of php.ini settings, or answers that it already carries them.
    ///
    /// Takes the three steps and nothing more: the account, version, budget and
    /// settings are revalidated (the account is named on the audit slot as soon
    /// as it exists, so a refused request is still attributed), one operation
    /// decides whether the pool exists and differs and writes it through the
    /// shared config-write path, and the result is settled on the slot with the
    /// same `Result` that becomes the response.
    ///
    /// Cancellation class: run to completion. The write is one pool file
    /// swapped in, checked with `php-fpm -t` and reloaded, with the previous
    /// bytes restored on a refusal; stopping between the swap and the check
    /// would leave an unchecked pool live. The rpc is idempotent, so a client
    /// that lost the answer asks again, and there is nothing to reclaim.
    async fn apply_php_pool_settings(
        &self,
        request: Request<ApplyPhpPoolSettingsRequest>,
    ) -> Result<Response<ApplyPhpPoolSettingsResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let validated = validated_account(&request.account_username).and_then(|account| {
            audit.account(&account);
            validated_pool_settings(account, &request)
        });

        let result = match validated {
            Ok(input) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("PHP operation", to_agent_error, move || {
                    php::apply_php_pool_settings(host.as_ref(), distro, &input)
                })
                .await
            }
            Err(error) => Err(error),
        };

        audit.settle(&result);
        let result = match result {
            Ok(()) => apply_php_pool_settings_response::Result::Ok(ApplyPhpPoolSettingsOk {}),
            Err(error) => apply_php_pool_settings_response::Result::Error(error),
        };

        Ok(Response::new(ApplyPhpPoolSettingsResponse {
            result: Some(result),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/php/php_service_tests.rs"]
mod tests;
