//! GetHostMetrics: one reading of the machine's processor, memory, disk,
//! network and load.

use std::path::Path;

use crate::monitor::model::cpu_times::CpuTimes;
use crate::monitor::model::host_metrics::HostMetrics;
use crate::monitor::model::load_average::LoadAverage;
use crate::monitor::model::memory_usage::MemoryUsage;
use crate::monitor::model::network_counters::NetworkCounters;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// The filesystem the panel reports on: the one the operating system is
/// installed on.
///
/// Not a platform fact and not an agent-owned location — it is the root of
/// every POSIX filesystem tree, which is why it is a constant here rather than
/// a `DistroAdapter` method both families would answer identically or an
/// `AgentPaths` entry for a directory the agent did not create.
///
/// One filesystem and not every mount: a hosting server keeps its accounts, its
/// databases and its logs on this one, and reporting each mount separately is a
/// question the panel does not yet ask.
const ROOT_FILESYSTEM: &str = "/";

/// Reads a point-in-time snapshot of the host's resources.
///
/// The processor figure costs a wait — see
/// [`MonitorHost::pause_between_cpu_samples`] — because a percentage exists
/// only between two readings of a counter. It is taken FIRST and closed
/// immediately, so the other four readings happen after the wait rather than
/// straddling it: memory read a quarter of a second away from the processor
/// figure would be describing a slightly different machine.
///
/// Every reading that cannot be understood fails the whole call rather than
/// being reported as zero. A zero here is not a missing value, it is a claim —
/// a host with no memory, no traffic and no load — and the panel would draw it
/// as one.
///
/// # Errors
///
/// Returns [`MonitorError::HostStatisticsUnavailable`] when the kernel's
/// statistics cannot be read or cannot be understood, and
/// [`MonitorError::FilesystemUnavailable`] when the root filesystem cannot be
/// measured.
pub fn get_host_metrics(host: &dyn MonitorHost) -> Result<HostMetrics, MonitorError> {
    let first = read_cpu_times(host)?;
    host.pause_between_cpu_samples();
    let second = read_cpu_times(host)?;

    let memory =
        MemoryUsage::parse(&host.read_memory()?).ok_or(MonitorError::HostStatisticsUnavailable)?;
    let network = NetworkCounters::parse(&host.read_network_counters()?)
        .ok_or(MonitorError::HostStatisticsUnavailable)?;
    let load = LoadAverage::parse(&host.read_load_average()?)
        .ok_or(MonitorError::HostStatisticsUnavailable)?;
    let root_filesystem = host.filesystem_usage(Path::new(ROOT_FILESYSTEM))?;

    Ok(HostMetrics {
        cpu_percent: second.busy_percent_since(&first),
        memory,
        root_filesystem,
        network,
        load,
    })
}

/// One reading of the processor counters, read and understood.
///
/// Its own function because it happens twice and both readings must fail the
/// same way: a second reading that silently became zeroes would produce a
/// percentage from a counter that appeared to run backwards.
fn read_cpu_times(host: &dyn MonitorHost) -> Result<CpuTimes, MonitorError> {
    CpuTimes::parse(&host.read_cpu_times()?).ok_or(MonitorError::HostStatisticsUnavailable)
}

#[cfg(test)]
#[path = "../tests/monitor/get_host_metrics_tests.rs"]
mod tests;
