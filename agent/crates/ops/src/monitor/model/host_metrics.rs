//! One point-in-time reading of the whole host.

use crate::monitor::model::filesystem_usage::FilesystemUsage;
use crate::monitor::model::load_average::LoadAverage;
use crate::monitor::model::memory_usage::MemoryUsage;
use crate::monitor::model::network_counters::NetworkCounters;

/// Everything the panel asks about the machine in one call.
///
/// One structure rather than four calls, because the numbers are compared with
/// each other on the same dashboard row: memory pressure read half a minute
/// after the load average tells a different story than the two read together.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct HostMetrics {
    /// Processor utilisation over the sampling interval, 0–100.
    pub cpu_percent: f64,
    /// The host's memory.
    pub memory: MemoryUsage,
    /// The filesystem the operating system is installed on.
    pub root_filesystem: FilesystemUsage,
    /// Bytes carried since boot, loopback excluded — counters, not rates.
    pub network: NetworkCounters,
    /// The kernel's run-queue averages.
    pub load: LoadAverage,
}
