//! Tests for the `caa_tag` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::caa_tag::CaaTag;
use super::super::caa_tag_error::CaaTagError;

#[test]
fn the_three_tags_parse_ignoring_case() {
    assert_eq!(CaaTag::parse("issue").unwrap(), CaaTag::Issue);
    assert_eq!(CaaTag::parse("IssueWild").unwrap(), CaaTag::IssueWild);
    assert_eq!(CaaTag::parse("IODEF").unwrap(), CaaTag::Iodef);
    assert_eq!(CaaTag::IssueWild.as_str(), "issuewild");
}

#[test]
fn anything_else_is_refused() {
    for bad in ["", "issue ", "issue\n", "contactemail", "issue\" 0 \"x"] {
        assert_eq!(CaaTag::parse(bad), Err(CaaTagError::Unknown), "{bad:?}");
    }
}
