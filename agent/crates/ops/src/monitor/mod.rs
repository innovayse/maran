//! Reading the host: its resources, the state of the units the panel watches,
//! and what each hosting account occupies on disk.
//!
//! **Nothing in this area changes the machine.** It is the one `ops` area with
//! no write, no spawn that acts, and no rollback, and that shapes everything
//! else here: [`MonitorHost`] exposes only readings, and the single program it
//! runs is the service manager's reporting subcommand.
//!
//! Two decisions carry the area, and both are about what to say when the
//! machine has not clearly answered.
//!
//! **A stopped service is an ANSWER, not an error.** The only failure this area
//! reports about a unit is a failure to reach the service manager at all. A
//! unit that is down comes back as [`ServiceState::Stopped`], because that is
//! exactly the fact the caller asked for; returning an error instead would show
//! the panel a broken monitor where it asked to be shown a broken service.
//!
//! **A monitor that does not know says so.** [`ServiceState`] has three values
//! rather than two, and the third is not decoration. On the Debian family the
//! enabled unit is `ssh.socket`: the service it fronts is inactive from boot
//! until the first connection and active from then on, so asking the service
//! whether it is active reports an outage on a perfectly healthy host, once per
//! reboot, on every host of that family. The socket is asked instead, and a
//! unit waiting behind a listening one is reported as not yet started. The same
//! discipline runs through the metrics: a statistics file that cannot be
//! understood fails the call rather than being reported as zero, because a zero
//! is a claim about the machine and not a missing value.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`MonitorHost`]), one file that really touches the machine
//! ([`ProcessMonitorHost`]), one error enum ([`MonitorError`]) that
//! structurally cannot carry a tool's output, and `model/` for the readings and
//! the parsed shapes they are built from. The parsers are pure functions over
//! text, tested against captures taken from both supported images.

#[cfg(test)]
#[path = "../tests/monitor/fake_monitor_host.rs"]
pub(crate) mod fake_monitor_host;
mod get_accounts_disk_usage;
mod get_host_metrics;
mod get_service_statuses;
pub mod model;
mod monitor_error;
mod monitor_host;
mod process_monitor_host;

pub use get_accounts_disk_usage::get_accounts_disk_usage;
pub use get_host_metrics::get_host_metrics;
pub use get_service_statuses::get_service_statuses;
pub use model::account_disk_usage::AccountDiskUsage;
pub use model::filesystem_usage::FilesystemUsage;
pub use model::host_metrics::HostMetrics;
pub use model::service_state::ServiceState;
pub use model::service_status::ServiceStatus;
pub use monitor_error::MonitorError;
pub use monitor_host::MonitorHost;
pub use process_monitor_host::ProcessMonitorHost;
