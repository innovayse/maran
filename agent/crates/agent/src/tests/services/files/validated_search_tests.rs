//! Tests for `validated_search`: the account, the start and the query are
//! revalidated, and the bounds are passed on for clamping and never refused.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_search;
use crate::proto::{ErrorCode, SearchEntriesRequest};

/// A request that is valid, for the tests that change one thing.
fn request() -> SearchEntriesRequest {
    SearchEntriesRequest {
        account_username: "alice_01".to_owned(),
        path: "sites".to_owned(),
        query: "Readme".to_owned(),
        max_depth: 0,
        max_results: 0,
        include_hidden: false,
    }
}

#[test]
fn a_valid_search_is_accepted_with_the_agents_defaults_for_zero_bounds() {
    let input = validated_search(&request()).unwrap();

    assert_eq!(input.account().as_str(), "alice_01");
    assert_eq!(input.start().file_name(), "sites");
    assert_eq!(input.max_depth(), 8);
    assert_eq!(input.max_results(), 100);
    assert!(!input.include_hidden());
}

#[test]
fn bounds_above_the_ceilings_are_clamped_and_not_refused() {
    let input = validated_search(&SearchEntriesRequest {
        max_depth: 1_000,
        max_results: 100_000,
        include_hidden: true,
        ..request()
    })
    .unwrap();

    assert_eq!(input.max_depth(), 16);
    assert_eq!(input.max_results(), 500);
    assert!(input.include_hidden());
}

#[test]
fn an_empty_query_is_invalid_input() {
    let refused = validated_search(&SearchEntriesRequest {
        query: String::new(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_query_with_a_separator_is_invalid_input() {
    let refused = validated_search(&SearchEntriesRequest {
        query: "a/b".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_traversal_start_is_invalid_input() {
    let refused = validated_search(&SearchEntriesRequest {
        path: "sites/../../other".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_search(&SearchEntriesRequest {
        account_username: String::new(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_home_is_a_valid_start() {
    let input = validated_search(&SearchEntriesRequest {
        path: String::new(),
        ..request()
    })
    .unwrap();

    assert!(input.start().is_home());
}
