//! Tests for the `account_resource_limits` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::cpu_quota_percent::CpuQuotaPercent;
use maran_agent_core::validation::system::memory_max_mib::MemoryMaxMib;
use maran_agent_core::validation::system::tasks_max::TasksMax;

use super::AccountResourceLimits;
use crate::accounts::ResourceCeiling;

#[test]
fn limited_ceilings_render_as_their_systemd_assignments() {
    let limits = AccountResourceLimits {
        cpu: ResourceCeiling::Limited(CpuQuotaPercent::parse(50).unwrap()),
        memory: ResourceCeiling::Limited(MemoryMaxMib::parse(512).unwrap()),
        tasks: ResourceCeiling::Limited(TasksMax::parse(100).unwrap()),
    };

    assert_eq!(
        limits.property_assignments(),
        ["CPUQuota=50%", "MemoryMax=512M", "TasksMax=100"]
    );
}

#[test]
fn unlimited_ceilings_render_as_systemd_resets() {
    let limits = AccountResourceLimits {
        cpu: ResourceCeiling::Unlimited,
        memory: ResourceCeiling::Unlimited,
        tasks: ResourceCeiling::Unlimited,
    };

    assert_eq!(
        limits.property_assignments(),
        ["CPUQuota=", "MemoryMax=infinity", "TasksMax=infinity"]
    );
}
