//! Tests for [`starts_with_http_status`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::starts_with_http_status;

#[test]
fn a_three_digit_status_followed_by_a_space_is_an_http_answer() {
    assert!(starts_with_http_status("404  Not Found"));
    assert!(starts_with_http_status("503 for http://mirror/repomd.xml"));
}

#[test]
fn text_that_is_not_a_status_line_is_not_an_http_answer() {
    assert!(!starts_with_http_status("Temporary failure resolving 'x'"));
    assert!(!starts_with_http_status("40 Not Found"));
    assert!(!starts_with_http_status("4040 Not Found"));
    assert!(!starts_with_http_status("404"));
    assert!(!starts_with_http_status(""));
}
