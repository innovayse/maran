//! Tests for the closed privilege set a request's numbers become.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashSet;

use maran_agent_core::validation::db::database_privilege::DatabasePrivilege;

use super::validated_privilege_set;
use crate::proto::{DatabasePrivilege as WirePrivilege, ErrorCode};

/// Every defined, non-zero wire value.
fn wire_values() -> Vec<i32> {
    (1..=18).collect()
}

#[test]
fn every_defined_wire_value_maps_to_a_distinct_privilege() {
    // The census: the contract defines 18 values and each reaches a different
    // member of the closed set. A value added to the contract without an arm
    // fails to compile; one mapped onto an existing privilege fails here.
    let mut seen = HashSet::new();
    for number in wire_values() {
        assert!(WirePrivilege::try_from(number).is_ok(), "{number}");
        let set = validated_privilege_set(&[number]).expect("valid");
        let only: Vec<DatabasePrivilege> = set.iter().collect();
        assert_eq!(only.len(), 1, "{number}");
        assert!(seen.insert(only[0]), "{number} repeats a privilege");
    }

    assert_eq!(seen.len(), DatabasePrivilege::ALL.len());
}

#[test]
fn the_wire_order_and_the_rendering_order_agree() {
    let set = validated_privilege_set(&wire_values()).expect("valid");

    assert_eq!(set.iter().collect::<Vec<_>>(), DatabasePrivilege::ALL);
}

#[test]
fn the_named_wire_values_map_to_their_namesakes() {
    let set = validated_privilege_set(&[
        WirePrivilege::CreateTemporaryTables as i32,
        WirePrivilege::Select as i32,
    ])
    .expect("valid");

    assert_eq!(set.sql_list(), "SELECT, CREATE TEMPORARY TABLES");
}

#[test]
fn an_empty_list_is_the_empty_set() {
    assert!(validated_privilege_set(&[]).expect("valid").is_empty());
}

#[test]
fn the_unspecified_value_is_refused_and_never_defaulted() {
    let refused = validated_privilege_set(&[0]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_number_the_contract_does_not_define_is_refused() {
    for number in [19, 100, -1, i32::MAX, i32::MIN] {
        let refused = validated_privilege_set(&[number]).expect_err("must refuse");

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{number}");
    }
}

#[test]
fn one_bad_entry_refuses_the_whole_list_even_beside_good_ones() {
    let refused = validated_privilege_set(&[1, 2, 99]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_repeated_privilege_is_refused() {
    let refused = validated_privilege_set(&[1, 1]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_list_longer_than_the_closed_set_is_refused() {
    let refused = validated_privilege_set(&[1; 19]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.contains("19"));
}
