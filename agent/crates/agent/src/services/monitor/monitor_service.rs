//! `MonitorService`: what the host and its managed units are doing.

use std::sync::Arc;

use maran_distro::DistroAdapter;
use maran_ops::monitor::{
    self, MachineIdentity, MonitorHost, PrimaryInterface, QuotaEnforceabilityStatus, SftpJailStatus,
};
use tonic::{Request, Response, Status};

use crate::proto::monitor_service_server::MonitorService;
use crate::proto::{
    AccountDiskUsage, GetAccountsDiskUsageOk, GetAccountsDiskUsageRequest,
    GetAccountsDiskUsageResponse, GetHostMetricsRequest, GetHostMetricsResponse,
    GetQuotaEnforceabilityOk, GetQuotaEnforceabilityRequest, GetQuotaEnforceabilityResponse,
    GetServerFingerprintInputsOk, GetServerFingerprintInputsRequest,
    GetServerFingerprintInputsResponse, GetServiceStatusesOk, GetServiceStatusesRequest,
    GetServiceStatusesResponse, GetSftpJailStatusOk, GetSftpJailStatusRequest,
    GetSftpJailStatusResponse, HostMetrics, MachineIdStatus, PrimaryInterfaceStatus,
    QuotaEnforceability, QuotaEnforceabilityReason, ServiceStatus, SftpJailState,
    get_accounts_disk_usage_response, get_host_metrics_response, get_quota_enforceability_response,
    get_server_fingerprint_inputs_response, get_service_statuses_response,
    get_sftp_jail_status_response,
};
use crate::services::monitor::managed_service::managed_service;
use crate::services::monitor::monitor_status::to_agent_error;
use crate::services::monitor::reported_state::reported_state;
use crate::services::wire::run_blocking::run_blocking;

/// The uptime the agent reports for every unit.
///
/// Zero, and `monitor.proto` marks the field deprecated and says why: the
/// service manager reports a unit's start timestamp, not an uptime, and turning
/// one into the other needs a clock reading this agent deliberately does not
/// take. It is a constant here rather than a literal in the mapping so that a
/// reader sees the field is unproduced rather than merely happening to be 0.
const UNPRODUCED_UPTIME: u64 = 0;

/// The quota the agent reports for every account: none.
///
/// A quota is the PANEL's own data — it is chosen when an account is created
/// and stored by the Accounts module — so the agent reports used bytes and
/// nothing else. `monitor.proto` marks the field deprecated and says the same.
const UNPRODUCED_QUOTA: u64 = 0;

/// Serves the read-only host and service observability over the wire.
///
/// Every rpc follows the same three steps: there is nothing to validate (none
/// of the three requests carries a field), so it runs one operation and maps
/// the outcome into the response's `oneof`. Failures travel in the payload
/// rather than as a gRPC status (rules/proto.md).
///
/// **Nothing here changes the machine, and nothing here can.** The operations
/// behind these rpcs read `/proc`, ask a filesystem how full it is, read the
/// password database, and run the service manager's reporting subcommand. No
/// rpc accepts a unit name, an account name or a path, so nothing a caller
/// supplies reaches a program or a file at all.
///
/// The distro adapter is held because two of the operations need platform
/// facts: where the password database lives, and the closed set of units this
/// family calls by name. The service asks no question of it itself.
pub struct MonitorServiceImpl<H> {
    /// The machine the readings are taken from.
    host: Arc<H>,
    /// The password database's path and the managed units, per family.
    distro: &'static dyn DistroAdapter,
}

impl<H: MonitorHost + 'static> MonitorServiceImpl<H> {
    /// Creates the service around the host it takes readings from.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            distro,
        }
    }
}

#[tonic::async_trait]
impl<H: MonitorHost + 'static> MonitorService for MonitorServiceImpl<H> {
    /// Takes one reading of the host's processor, memory, disk, network and
    /// load.
    async fn get_host_metrics(
        &self,
        _request: Request<GetHostMetricsRequest>,
    ) -> Result<Response<GetHostMetricsResponse>, Status> {
        let host = Arc::clone(&self.host);
        let result = run_blocking("monitoring reading", to_agent_error, move || {
            monitor::get_host_metrics(host.as_ref())
        })
        .await;

        let result = match result {
            Ok(metrics) => get_host_metrics_response::Result::Ok(HostMetrics {
                cpu_percent: metrics.cpu_percent,
                memory_used_bytes: metrics.memory.used_bytes,
                memory_total_bytes: metrics.memory.total_bytes,
                disk_used_bytes: metrics.root_filesystem.used_bytes,
                disk_total_bytes: metrics.root_filesystem.total_bytes,
                network_rx_bytes: metrics.network.received_bytes,
                network_tx_bytes: metrics.network.transmitted_bytes,
                load_average_1m: metrics.load.one_minute,
                load_average_5m: metrics.load.five_minutes,
                load_average_15m: metrics.load.fifteen_minutes,
            }),
            Err(error) => get_host_metrics_response::Result::Error(error),
        };

        Ok(Response::new(GetHostMetricsResponse {
            result: Some(result),
        }))
    }

    /// Reports the state of every unit the panel watches, in the adapter's
    /// fixed order.
    async fn get_service_statuses(
        &self,
        _request: Request<GetServiceStatusesRequest>,
    ) -> Result<Response<GetServiceStatusesResponse>, Status> {
        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("monitoring reading", to_agent_error, move || {
            monitor::get_service_statuses(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(statuses) => get_service_statuses_response::Result::Ok(GetServiceStatusesOk {
                services: statuses
                    .into_iter()
                    .enumerate()
                    .map(|(position, status)| {
                        let (state, running) = reported_state(status.state);

                        ServiceStatus {
                            service: managed_service(position) as i32,
                            running,
                            uptime_seconds: UNPRODUCED_UPTIME,
                            state: state as i32,
                            detail: status.detail,
                        }
                    })
                    .collect(),
            }),
            Err(error) => get_service_statuses_response::Result::Error(error),
        };

        Ok(Response::new(GetServiceStatusesResponse {
            result: Some(result),
        }))
    }

    /// Reports what each hosting account occupies under its home.
    async fn get_accounts_disk_usage(
        &self,
        _request: Request<GetAccountsDiskUsageRequest>,
    ) -> Result<Response<GetAccountsDiskUsageResponse>, Status> {
        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("monitoring reading", to_agent_error, move || {
            monitor::get_accounts_disk_usage(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(accounts) => get_accounts_disk_usage_response::Result::Ok(GetAccountsDiskUsageOk {
                accounts: accounts
                    .into_iter()
                    .map(|usage| AccountDiskUsage {
                        account_username: usage.account.as_str().to_owned(),
                        used_bytes: usage.used_bytes,
                        quota_bytes: UNPRODUCED_QUOTA,
                    })
                    .collect(),
            }),
            Err(error) => get_accounts_disk_usage_response::Result::Error(error),
        };

        Ok(Response::new(GetAccountsDiskUsageResponse {
            result: Some(result),
        }))
    }

    /// Reports whether the installer's `Match Group` block — the block that
    /// jails every SFTP login — is still present and intact in the live
    /// `sshd_config`.
    async fn get_sftp_jail_status(
        &self,
        _request: Request<GetSftpJailStatusRequest>,
    ) -> Result<Response<GetSftpJailStatusResponse>, Status> {
        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("monitoring reading", to_agent_error, move || {
            monitor::get_sftp_jail_status(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(SftpJailStatus::Intact) => {
                get_sftp_jail_status_response::Result::Ok(GetSftpJailStatusOk {
                    state: SftpJailState::Intact as i32,
                    missing: Vec::new(),
                })
            }
            Ok(SftpJailStatus::Drifted { missing }) => {
                get_sftp_jail_status_response::Result::Ok(GetSftpJailStatusOk {
                    state: SftpJailState::Drifted as i32,
                    missing,
                })
            }
            Err(error) => get_sftp_jail_status_response::Result::Error(error),
        };

        Ok(Response::new(GetSftpJailStatusResponse {
            result: Some(result),
        }))
    }

    /// Reports whether the filesystem holding hosting accounts' homes can
    /// currently enforce a per-user disk quota — a remount can change this
    /// without touching any account, so nothing else re-checks it after the
    /// installer's own one-time preflight warning.
    async fn get_quota_enforceability(
        &self,
        _request: Request<GetQuotaEnforceabilityRequest>,
    ) -> Result<Response<GetQuotaEnforceabilityResponse>, Status> {
        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("monitoring reading", to_agent_error, move || {
            monitor::get_quota_enforceability(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(QuotaEnforceabilityStatus::Enforceable) => {
                get_quota_enforceability_response::Result::Ok(GetQuotaEnforceabilityOk {
                    enforceability: QuotaEnforceability::Enforceable as i32,
                    reason: QuotaEnforceabilityReason::Unspecified as i32,
                })
            }
            Ok(QuotaEnforceabilityStatus::NotEnforceable(reason)) => {
                get_quota_enforceability_response::Result::Ok(GetQuotaEnforceabilityOk {
                    enforceability: QuotaEnforceability::NotEnforceable as i32,
                    reason: to_wire_quota_enforceability_reason(reason) as i32,
                })
            }
            // `QuotaEnforceabilityStatus` is `#[non_exhaustive]`: a future
            // variant answers Unspecified rather than failing to build.
            Ok(_) => get_quota_enforceability_response::Result::Ok(GetQuotaEnforceabilityOk {
                enforceability: QuotaEnforceability::Unspecified as i32,
                reason: QuotaEnforceabilityReason::Unspecified as i32,
            }),
            Err(error) => get_quota_enforceability_response::Result::Error(error),
        };

        Ok(Response::new(GetQuotaEnforceabilityResponse {
            result: Some(result),
        }))
    }

    /// Reports the two raw values a licence's server fingerprint (spec §228)
    /// is computed from — this host's `machine-id` and the interface
    /// carrying its IPv4 default route. Reports only; computes and compares
    /// nothing.
    async fn get_server_fingerprint_inputs(
        &self,
        _request: Request<GetServerFingerprintInputsRequest>,
    ) -> Result<Response<GetServerFingerprintInputsResponse>, Status> {
        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("monitoring reading", to_agent_error, move || {
            monitor::get_server_fingerprint_inputs(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(inputs) => {
                get_server_fingerprint_inputs_response::Result::Ok(GetServerFingerprintInputsOk {
                    machine_id: Some(to_wire_machine_id(inputs.machine_id)),
                    primary_interface: Some(to_wire_primary_interface(inputs.primary_interface)),
                })
            }
            Err(error) => get_server_fingerprint_inputs_response::Result::Error(error),
        };

        Ok(Response::new(GetServerFingerprintInputsResponse {
            result: Some(result),
        }))
    }
}

/// Converts the agent's own [`MachineIdentity`] onto its wire shape.
///
/// `MachineIdentity` is `#[non_exhaustive]`: a wildcard reports `present:
/// false` for a future variant this match does not yet know, the same
/// "unspecified/absent" fallback every other conversion in this file uses,
/// rather than a broken build.
#[must_use]
fn to_wire_machine_id(identity: MachineIdentity) -> MachineIdStatus {
    match identity {
        MachineIdentity::Present(value) => MachineIdStatus {
            present: true,
            value,
        },
        _ => MachineIdStatus {
            present: false,
            value: String::new(),
        },
    }
}

/// Converts the agent's own [`PrimaryInterface`] onto its wire shape. Same
/// non-exhaustive fallback as [`to_wire_machine_id`].
#[must_use]
fn to_wire_primary_interface(interface: PrimaryInterface) -> PrimaryInterfaceStatus {
    match interface {
        PrimaryInterface::Present(value) => PrimaryInterfaceStatus {
            present: true,
            value,
        },
        _ => PrimaryInterfaceStatus {
            present: false,
            value: String::new(),
        },
    }
}

/// Converts the agent's own reason onto its wire mirror.
///
/// `QuotaUnenforceableReason` is `#[non_exhaustive]`: a wildcard covers a
/// future variant with the same "unspecified" wire fallback the accounts
/// service's own `to_wire_quota_state` uses, rather than a broken build.
#[must_use]
fn to_wire_quota_enforceability_reason(
    reason: maran_ops::accounts::QuotaUnenforceableReason,
) -> QuotaEnforceabilityReason {
    match reason {
        maran_ops::accounts::QuotaUnenforceableReason::MountedWithoutQuotaAccounting => {
            QuotaEnforceabilityReason::MountedWithoutQuotaAccounting
        }
        maran_ops::accounts::QuotaUnenforceableReason::AccountingNotEnabled => {
            QuotaEnforceabilityReason::AccountingNotEnabled
        }
        _ => QuotaEnforceabilityReason::Unspecified,
    }
}
