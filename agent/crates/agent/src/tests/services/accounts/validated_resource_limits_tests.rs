//! Tests for the `validated_resource_limits` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::ResourceCeiling;

use super::validated_resource_limits;
use crate::proto::resource_ceiling::Ceiling;
use crate::proto::{
    ErrorCode, ResourceCeiling as WireCeiling, ResourceCeilingUnlimited,
    SetAccountResourceLimitsRequest,
};

fn limited(figure: u32) -> Option<WireCeiling> {
    Some(WireCeiling {
        ceiling: Some(Ceiling::Maximum(figure)),
    })
}

fn unlimited() -> Option<WireCeiling> {
    Some(WireCeiling {
        ceiling: Some(Ceiling::Unlimited(ResourceCeilingUnlimited {})),
    })
}

fn request(
    cpu: Option<WireCeiling>,
    memory: Option<WireCeiling>,
    tasks: Option<WireCeiling>,
) -> SetAccountResourceLimitsRequest {
    SetAccountResourceLimitsRequest {
        username: "alice".to_owned(),
        cpu_quota_percent: cpu,
        memory_max_mib: memory,
        tasks_max: tasks,
    }
}

#[test]
fn stated_ceilings_are_carried_through() {
    let limits =
        validated_resource_limits(&request(limited(50), unlimited(), limited(200))).unwrap();

    assert_eq!(
        limits.property_assignments(),
        ["CPUQuota=50%", "MemoryMax=infinity", "TasksMax=200"]
    );
    assert_eq!(limits.memory, ResourceCeiling::Unlimited);
}

#[test]
fn an_absent_ceiling_is_refused_rather_than_read_as_unlimited() {
    let refused = validated_resource_limits(&request(limited(50), None, limited(200))).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("memory_max_mib:"));
}

#[test]
fn a_ceiling_with_neither_case_set_is_refused() {
    let empty = Some(WireCeiling { ceiling: None });

    let refused = validated_resource_limits(&request(empty, unlimited(), unlimited())).unwrap_err();

    assert!(refused.message.starts_with("cpu_quota_percent:"));
}

#[test]
fn a_zero_figure_is_refused() {
    let refused =
        validated_resource_limits(&request(unlimited(), unlimited(), limited(0))).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("tasks_max:"));
}
