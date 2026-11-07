//! Tests for the agent's own check of the panel's host name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_panel_host_name;
use crate::proto::ErrorCode;

#[test]
fn a_domain_name_is_accepted() {
    let domain = validated_panel_host_name("panel.example.com").unwrap();

    assert_eq!(domain.as_str(), "panel.example.com");
}

#[test]
fn a_host_name_that_is_not_a_domain_is_refused_as_invalid_input() {
    for bad in [
        "",
        "panel.example.com\nOU=evil",
        "a,b.example.com",
        "a/b.example.com",
        "a=b.example.com",
        "-bad.example.com",
        "panel example.com",
    ] {
        let refused = validated_panel_host_name(bad).unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{bad:?}");
    }
}
