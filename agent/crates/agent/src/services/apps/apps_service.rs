//! `AppsService`: the app store of the host.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::validation::web::panel_host_names::PanelHostNames;
use maran_ops::apps::{self, AppKind, AppServiceStatus, AppsHost};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::apps_service_server::AppsService;
use crate::proto::{
    AppInfo, AppKind as WireKind, AppServiceStatus as WireStatus, ControlAppServiceOk,
    ControlAppServiceRequest, ControlAppServiceResponse, InstallAppOk, InstallAppRequest,
    InstallAppResponse, InstallableApp, ListAppsOk, ListAppsRequest, ListAppsResponse,
    ListInstallableAppsOk, ListInstallableAppsRequest, ListInstallableAppsResponse, Progress,
    UninstallAppOk, UninstallAppRequest, UninstallAppResponse, control_app_service_response,
    install_app_response, list_apps_response, list_installable_apps_response,
    uninstall_app_response,
};
use crate::services::apps::app_status::to_agent_error;
use crate::services::apps::validated_action::validated_action;
use crate::services::apps::validated_app_id::validated_app_id;
use crate::services::wire::run_blocking::run_blocking;

/// How many messages an install or a removal may run ahead of the client.
///
/// The operations emit at most five messages in total, so this is never a constraint in
/// practice. It is bounded all the same: a channel with no ceiling is a queue in the root daemon
/// whose size a client chooses by not reading (rules/rust.md "Streams stay bounded").
const STREAM_CHANNEL_CAPACITY: usize = 8;

/// The stream `InstallApp` returns.
type InstallStream = Pin<Box<dyn Stream<Item = Result<InstallAppResponse, Status>> + Send>>;

/// The stream `UninstallApp` returns.
type UninstallStream = Pin<Box<dyn Stream<Item = Result<UninstallAppResponse, Status>> + Send>>;

/// The wire kind of an app.
fn wire_kind(kind: AppKind) -> WireKind {
    match kind {
        AppKind::WebServer => WireKind::WebServer,
        AppKind::Runtime => WireKind::Runtime,
    }
}

/// The wire status of an app's service.
fn wire_status(status: AppServiceStatus) -> WireStatus {
    match status {
        AppServiceStatus::Active => WireStatus::Active,
        AppServiceStatus::Inactive => WireStatus::Inactive,
        AppServiceStatus::Failed => WireStatus::Failed,
        AppServiceStatus::None => WireStatus::None,
    }
}

/// Serves the app store over the wire.
///
/// Every rpc takes the same three steps: revalidate what the panel sent, run one operation, and
/// map the outcome into the response's `oneof`. Failures travel in the payload, not as a gRPC
/// status (rules/proto.md).
pub struct AppsServiceImpl<H> {
    /// The machine the app operations run against.
    host: Arc<H>,
    /// Where platform facts come from: package names, service names and binaries.
    distro: &'static dyn maran_distro::DistroAdapter,
    /// The names the panel itself is served under; the first one is what Apache's challenge
    /// virtual host answers for once Apache owns port 80.
    panel_host_names: PanelHostNames,
}

impl<H: AppsHost + 'static> AppsServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(
        host: H,
        distro: &'static dyn maran_distro::DistroAdapter,
        panel_host_names: PanelHostNames,
    ) -> Self {
        Self {
            host: Arc::new(host),
            distro,
            panel_host_names,
        }
    }
}

#[tonic::async_trait]
impl<H: AppsHost + 'static> AppsService for AppsServiceImpl<H> {
    /// The stream `InstallApp` returns.
    type InstallAppStream = InstallStream;

    /// The stream `UninstallApp` returns.
    type UninstallAppStream = UninstallStream;

    /// Lists the installed apps, probed. Read-only: it yields no audit line.
    async fn list_apps(
        &self,
        _request: Request<ListAppsRequest>,
    ) -> Result<Response<ListAppsResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);

        let result = match run_blocking("app operation", to_agent_error, move || {
            apps::list_apps(host.as_ref(), distro)
        })
        .await
        {
            Ok(installed) => list_apps_response::Result::Ok(ListAppsOk {
                apps: installed
                    .into_iter()
                    .map(|app| AppInfo {
                        app_id: app.id.as_id(),
                        kind: wire_kind(app.kind) as i32,
                        version: app.version,
                        service_status: wire_status(app.service_status) as i32,
                    })
                    .collect(),
            }),
            Err(error) => list_apps_response::Result::Error(error),
        };

        Ok(Response::new(ListAppsResponse {
            result: Some(result),
        }))
    }

    /// Lists the apps this host can install. Read-only: it yields no audit line.
    async fn list_installable_apps(
        &self,
        _request: Request<ListInstallableAppsRequest>,
    ) -> Result<Response<ListInstallableAppsResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);

        let result = match run_blocking("app operation", to_agent_error, move || {
            apps::list_installable_apps(host.as_ref(), distro)
        })
        .await
        {
            Ok(installable) => list_installable_apps_response::Result::Ok(ListInstallableAppsOk {
                apps: installable
                    .into_iter()
                    .map(|app| InstallableApp {
                        app_id: app.id.as_id(),
                        kind: wire_kind(app.kind) as i32,
                        installed: app.installed,
                    })
                    .collect(),
            }),
            Err(error) => list_installable_apps_response::Result::Error(error),
        };

        Ok(Response::new(ListInstallableAppsResponse {
            result: Some(result),
        }))
    }

    /// Installs an app, streaming `Progress` and ending with exactly one terminal message.
    ///
    /// The stream is produced by `ops` and this only wraps it: the progress callback is the only
    /// thing that turns a stage into a message. The agent does not run the package manager; it
    /// starts the installer's `maran-app-install@<package>` unit and waits for it.
    ///
    /// **Cancellation class: run to completion** (rules/rust.md, "Async and blocking"), with
    /// nothing to reclaim. A package manager stopped part-way is a broken host and the rpc is
    /// idempotent, so the panel's remedy for a stream it lost is to ask again. The channel is
    /// bounded and a dropped client makes each remaining send fail at once rather than block.
    async fn install_app(
        &self,
        request: Request<InstallAppRequest>,
    ) -> Result<Response<Self::InstallAppStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(STREAM_CHANNEL_CAPACITY);

        match validated_app_id(&request.app_id) {
            Ok(id) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                let progress = sender.clone();
                let panel_host_name = self
                    .panel_host_names
                    .primary()
                    .map(|name| name.as_str().to_owned());

                tokio::task::spawn_blocking(move || {
                    let outcome = apps::install_app(
                        host.as_ref(),
                        distro,
                        &id,
                        panel_host_name.as_deref(),
                        |percent, stage| {
                            let _ = progress.blocking_send(Ok(InstallAppResponse {
                                result: Some(install_app_response::Result::Progress(Progress {
                                    percent,
                                    stage: stage.to_owned(),
                                })),
                            }));
                        },
                    )
                    .map_err(|error| to_agent_error(&error));
                    // Settled on the terminal message, before it is sent.
                    audit.settle(&outcome);

                    let terminal = match outcome {
                        Ok(()) => {
                            install_app_response::Result::Ok(InstallAppOk { app_id: id.as_id() })
                        }
                        Err(error) => install_app_response::Result::Error(error),
                    };
                    let _ = sender.blocking_send(Ok(InstallAppResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                audit.settle::<()>(&Err(error.clone()));
                // One terminal message and then the end of the stream: the same typed refusal a
                // unary rpc would give.
                let _ = sender
                    .send(Ok(InstallAppResponse {
                        result: Some(install_app_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Removes an app, streaming `Progress` and ending with exactly one terminal message. The
    /// same shape and the same cancellation class as `install_app`, through the installer's
    /// `maran-app-remove@<package>` unit.
    async fn uninstall_app(
        &self,
        request: Request<UninstallAppRequest>,
    ) -> Result<Response<Self::UninstallAppStream>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(STREAM_CHANNEL_CAPACITY);

        match validated_app_id(&request.app_id) {
            Ok(id) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                let progress = sender.clone();

                tokio::task::spawn_blocking(move || {
                    let outcome =
                        apps::uninstall_app(host.as_ref(), distro, &id, |percent, stage| {
                            let _ = progress.blocking_send(Ok(UninstallAppResponse {
                                result: Some(uninstall_app_response::Result::Progress(Progress {
                                    percent,
                                    stage: stage.to_owned(),
                                })),
                            }));
                        })
                        .map_err(|error| to_agent_error(&error));
                    audit.settle(&outcome);

                    let terminal = match outcome {
                        Ok(()) => uninstall_app_response::Result::Ok(UninstallAppOk {
                            app_id: id.as_id(),
                        }),
                        Err(error) => uninstall_app_response::Result::Error(error),
                    };
                    let _ = sender.blocking_send(Ok(UninstallAppResponse {
                        result: Some(terminal),
                    }));
                });
            }
            Err(error) => {
                audit.settle::<()>(&Err(error.clone()));
                let _ = sender
                    .send(Ok(UninstallAppResponse {
                        result: Some(uninstall_app_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Starts, stops, restarts or reloads the service of one installed app.
    ///
    /// Cancellation class: run to completion. A `systemctl` action abandoned part-way leaves the
    /// service in whatever state the service manager reached, which the next listing reports.
    async fn control_app_service(
        &self,
        request: Request<ControlAppServiceRequest>,
    ) -> Result<Response<ControlAppServiceResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let validated = validated_app_id(&request.app_id)
            .and_then(|id| validated_action(request.action).map(|action| (id, action)));

        let result = match validated {
            Ok((id, action)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("app operation", to_agent_error, move || {
                    apps::control_app_service(host.as_ref(), distro, &id, action)
                })
                .await
            }
            Err(error) => Err(error),
        };

        audit.settle(&result);
        let result = match result {
            Ok(status) => control_app_service_response::Result::Ok(ControlAppServiceOk {
                service_status: wire_status(status) as i32,
            }),
            Err(error) => control_app_service_response::Result::Error(error),
        };

        Ok(Response::new(ControlAppServiceResponse {
            result: Some(result),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/apps/apps_service_tests.rs"]
mod tests;
