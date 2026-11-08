//! Tests for the counters of one archive request.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;

use super::ExpansionBudget;
use crate::files::archive::archive_refusal::ArchiveRefusal;

const MEBIBYTE: u64 = 1024 * 1024;

fn extraction(entries: u64, bytes: u64, ratio: u32, archive_length: u64) -> ExpansionBudget {
    ExpansionBudget::for_extraction(
        &ExtractBounds::parse(entries, bytes, ratio).unwrap(),
        archive_length,
    )
}

#[test]
fn entries_up_to_the_cap_are_admitted_and_the_next_is_refused() {
    let mut budget = extraction(2, 1000, 100, 100);

    budget.admit_entry().unwrap();
    budget.admit_entry().unwrap();

    assert_eq!(
        budget.admit_entry().unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
}

#[test]
fn bytes_up_to_the_cap_are_admitted_and_one_more_is_refused() {
    let mut budget = extraction(10, 1000, 1000, 10 * MEBIBYTE);

    budget.admit_bytes(600).unwrap();
    budget.admit_bytes(400).unwrap();

    assert_eq!(budget.bytes(), 1000);
    assert_eq!(
        budget.admit_bytes(1).unwrap_err(),
        ArchiveRefusal::ByteLimit
    );
}

#[test]
fn a_huge_declared_size_cannot_overflow_the_counter() {
    let mut budget = extraction(10, 1000, 1000, 10 * MEBIBYTE);
    budget.admit_bytes(1).unwrap();

    assert_eq!(
        budget.admit_bytes(u64::MAX).unwrap_err(),
        ArchiveRefusal::ByteLimit
    );
}

#[test]
fn expansion_past_the_ratio_is_a_bomb_once_the_floor_is_passed() {
    // 1 KiB archive at 10:1 may expand to 10 KiB, but the floor is 1 MiB.
    let mut budget = extraction(10, 100 * MEBIBYTE, 10, 1024);

    budget.admit_bytes(MEBIBYTE).unwrap();
    assert_eq!(
        budget.admit_bytes(1).unwrap_err(),
        ArchiveRefusal::BombRatio
    );
}

#[test]
fn a_small_expansion_is_never_judged_by_the_ratio() {
    let mut budget = extraction(10, 100 * MEBIBYTE, 1, 10);

    budget.admit_bytes(MEBIBYTE).unwrap();
}

#[test]
fn expansion_inside_the_ratio_passes_above_the_floor() {
    let mut budget = extraction(10, 100 * MEBIBYTE, 10, MEBIBYTE);

    budget.admit_bytes(10 * MEBIBYTE).unwrap();
    assert_eq!(
        budget.admit_bytes(1).unwrap_err(),
        ArchiveRefusal::BombRatio
    );
}

#[test]
fn a_creation_has_no_ratio() {
    let mut budget =
        ExpansionBudget::for_creation(&CreateBounds::parse(5, 100 * MEBIBYTE).unwrap());

    budget.admit_bytes(50 * MEBIBYTE).unwrap();
}

#[test]
fn a_creation_stops_at_its_byte_cap_and_its_entry_cap() {
    let mut budget = ExpansionBudget::for_creation(&CreateBounds::parse(1, 10).unwrap());

    budget.admit_entry().unwrap();
    assert_eq!(
        budget.admit_entry().unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
    assert_eq!(
        budget.admit_bytes(11).unwrap_err(),
        ArchiveRefusal::ByteLimit
    );
}

#[test]
fn a_declared_entry_count_over_the_cap_is_refused_without_being_charged() {
    let budget = extraction(100, 1000, 10, 1000);

    assert!(budget.check_declared_entries(100).is_ok());
    assert_eq!(
        budget.check_declared_entries(101).unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
}

#[test]
fn the_tar_stream_limit_comes_from_the_caps_and_always_exceeds_them() {
    let budget = extraction(10, 1000, 10, 1000);

    let limit = budget.tar_stream_limit();

    assert!(limit > 1000 + 10 * 512);
    assert_eq!(limit, 1000 + 10 * 4096 + MEBIBYTE);
}
