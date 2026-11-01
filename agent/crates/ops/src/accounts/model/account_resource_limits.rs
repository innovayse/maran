//! The three ceilings a plan puts on an account's systemd slice.

use maran_agent_core::validation::system::cpu_quota_percent::CpuQuotaPercent;
use maran_agent_core::validation::system::memory_max_mib::MemoryMaxMib;
use maran_agent_core::validation::system::tasks_max::TasksMax;

use crate::accounts::model::resource_ceiling::ResourceCeiling;

/// The CPU, memory and process ceilings one account's slice is held to.
///
/// All three travel together and are written together: `SetAccountResourceLimits`
/// replaces the whole set, so an account never holds one plan's CPU quota beside
/// another plan's memory ceiling, and a recycled uid never inherits a property a
/// partial write left behind.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountResourceLimits {
    /// The share of CPU time, in percent of one CPU.
    pub cpu: ResourceCeiling<CpuQuotaPercent>,
    /// The memory ceiling, in MiB.
    pub memory: ResourceCeiling<MemoryMaxMib>,
    /// The ceiling on processes and threads.
    pub tasks: ResourceCeiling<TasksMax>,
}

impl AccountResourceLimits {
    /// The three `Key=value` assignments `systemctl set-property` is handed, in
    /// a fixed order.
    ///
    /// Each "no ceiling" is spelled the way systemd resets that property: an
    /// empty `CPUQuota=` removes the quota, and `infinity` is the documented
    /// "no limit" of both `MemoryMax=` and `TasksMax=`. Every figure is a
    /// validated integer, so no assignment can carry a newline, a second
    /// assignment or an option (rules/security.md item 4).
    #[must_use]
    pub fn property_assignments(&self) -> [String; 3] {
        [
            match self.cpu {
                ResourceCeiling::Unlimited => "CPUQuota=".to_owned(),
                ResourceCeiling::Limited(quota) => quota.as_property(),
            },
            match self.memory {
                ResourceCeiling::Unlimited => "MemoryMax=infinity".to_owned(),
                ResourceCeiling::Limited(ceiling) => ceiling.as_property(),
            },
            match self.tasks {
                ResourceCeiling::Unlimited => "TasksMax=infinity".to_owned(),
                ResourceCeiling::Limited(ceiling) => ceiling.as_property(),
            },
        ]
    }
}

#[cfg(test)]
#[path = "../../tests/accounts/model/account_resource_limits_tests.rs"]
mod tests;
