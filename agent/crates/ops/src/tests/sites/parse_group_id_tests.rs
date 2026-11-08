//! Tests for the `parse_group_id` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_group_id;

#[test]
fn the_third_field_of_the_groups_own_row_is_its_id() {
    assert_eq!(parse_group_id("www-data", "www-data:x:33:\n"), Some(33));
    assert_eq!(
        parse_group_id("nginx", "nginx:x:991:nginx,maran\n"),
        Some(991)
    );
}

#[test]
fn a_row_for_another_group_is_not_read_as_this_ones() {
    // A `getent` with no key lists every group; the first row's id must never
    // be taken for the web server's.
    assert_eq!(
        parse_group_id("www-data", "root:x:0:\nwww-data:x:33:\n"),
        None
    );
}

#[test]
fn a_short_row_an_id_that_is_not_a_number_and_no_output_are_refused() {
    assert_eq!(parse_group_id("www-data", ""), None);
    assert_eq!(parse_group_id("www-data", "www-data:x\n"), None);
    assert_eq!(parse_group_id("www-data", "www-data:x:abc:\n"), None);
    assert_eq!(parse_group_id("www-data", "www-data:x:-1:\n"), None);
}
