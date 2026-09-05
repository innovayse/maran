//! `MonitorService`: what the host and its managed units are doing.

use std::sync::Arc;

use maran_distro::DistroAdapter;
use maran_ops::monitor::{self, MonitorHost};
use tonic::{Request, Response, Status};

use crate::proto::monitor_service_server::MonitorService;
use crate::proto::{
    AccountDiskUsage, GetAccountsDiskUsageOk, GetAccountsDiskUsageRequest,
    GetAccountsDiskUsageResponse, GetHostMetricsRequest, GetHostMetricsResponse,
    GetServiceStatusesOk, GetServiceStatusesRequest, GetServiceStatusesResponse, HostMetrics,
    ServiceStatus, get_accounts_disk_usage_response, get_host_metrics_response,
    get_service_statuses_response,
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
}
