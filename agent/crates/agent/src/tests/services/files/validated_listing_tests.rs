//! Tests for `validated_listing`: paging values, the sort and the path are
//! revalidated, and an unknown sort is refused rather than defaulted.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::directory_sort::DirectorySort;

use super::validated_listing;
use crate::proto::{DirectorySort as WireSort, ErrorCode, ListDirectoryRequest};

/// A request that is valid, for the tests that change one thing.
fn request() -> ListDirectoryRequest {
    ListDirectoryRequest {
        account_username: "alice_01".to_owned(),
        path: "sites".to_owned(),
        offset: 0,
        limit: 0,
        include_hidden: false,
        sort: WireSort::Name as i32,
        descending: false,
    }
}

#[test]
fn a_valid_listing_is_accepted_with_the_default_page() {
    let input = validated_listing(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.window.limit(), 200);
    assert_eq!(input.window.offset(), 0);
    assert!(!input.window.include_hidden());
    assert!(!input.window.descending());
}

#[test]
fn the_home_is_a_valid_path() {
    let input = validated_listing(&ListDirectoryRequest {
        path: String::new(),
        ..request()
    })
    .unwrap();

    assert!(input.path.is_home());
}

#[test]
fn every_known_sort_is_carried_to_the_window() {
    for (wire, expected) in [
        (WireSort::Name, DirectorySort::Name),
        (WireSort::Size, DirectorySort::Size),
        (WireSort::Modified, DirectorySort::Modified),
    ] {
        let input = validated_listing(&ListDirectoryRequest {
            sort: wire as i32,
            descending: true,
            offset: 40,
            include_hidden: true,
            ..request()
        })
        .unwrap();

        assert_eq!(input.window.sort(), expected);
        assert!(input.window.descending());
        assert_eq!(input.window.offset(), 40);
        assert!(input.window.include_hidden());
    }
}

#[test]
fn an_unknown_sort_is_invalid_input_and_not_the_default() {
    let refused = validated_listing(&ListDirectoryRequest {
        sort: 99,
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_page_above_the_ceiling_is_invalid_input_and_one_at_it_is_accepted() {
    let above = validated_listing(&ListDirectoryRequest {
        limit: 1_001,
        ..request()
    })
    .unwrap_err();
    let at = validated_listing(&ListDirectoryRequest {
        limit: 1_000,
        ..request()
    });

    assert_eq!(above.code, ErrorCode::InvalidInput as i32);
    assert!(at.is_ok(), "inverse control: the ceiling itself is allowed");
}

#[test]
fn a_traversal_path_is_invalid_input() {
    let refused = validated_listing(&ListDirectoryRequest {
        path: "..".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_listing(&ListDirectoryRequest {
        account_username: "Root!".to_owned(),
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
