//! Tests for the clamping of a search request's bounds.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::search_query::SearchQuery;
use maran_agent_core::validation::system::name::AccountName;

use super::SearchInput;

/// A request with the given depth and result bounds.
fn with(depth: u32, results: u32) -> SearchInput {
    SearchInput::new(
        AccountName::parse("alice1").unwrap(),
        BrowsePath::parse("").unwrap(),
        SearchQuery::parse("log").unwrap(),
        depth,
        results,
        false,
    )
}

#[test]
fn zero_means_the_default_depth_and_the_default_result_count() {
    let input = with(0, 0);

    assert_eq!(input.max_depth(), 8);
    assert_eq!(input.max_results(), 100);
}

#[test]
fn depth_above_the_ceiling_is_lowered_to_sixteen() {
    assert_eq!(with(17, 1).max_depth(), 16);
    assert_eq!(with(u32::MAX, 1).max_depth(), 16);
}

#[test]
fn depth_at_the_ceiling_and_below_is_kept() {
    assert_eq!(with(16, 1).max_depth(), 16);
    assert_eq!(with(3, 1).max_depth(), 3);
}

#[test]
fn results_above_the_ceiling_are_lowered_to_five_hundred() {
    assert_eq!(with(1, 501).max_results(), 500);
    assert_eq!(with(1, u32::MAX).max_results(), 500);
}

#[test]
fn results_at_the_ceiling_and_below_are_kept() {
    assert_eq!(with(1, 500).max_results(), 500);
    assert_eq!(with(1, 7).max_results(), 7);
}

#[test]
fn the_account_start_query_and_hidden_flag_are_kept() {
    let input = with(1, 1);

    assert_eq!(input.account().as_str(), "alice1");
    assert!(input.start().is_home());
    assert!(input.query().matches("Catalog"));
    assert!(!input.include_hidden());
}
