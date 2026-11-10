//! What the strict identity check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::panel_host_names::PanelHostNames;

use super::validated_identity;
use crate::proto::ErrorCode;

#[test]
fn an_ordinary_account_and_domain_are_accepted() {
    let (account, domain) =
        validated_identity("acme", "example.com", &PanelHostNames::none()).expect("accepted");

    assert_eq!(account.as_str(), "acme");
    assert_eq!(domain.as_str(), "example.com");
}

#[test]
fn a_reserved_domain_is_refused_as_invalid_input() {
    for reserved in ["203.0.113.7", "localhost", "x.local", "intranet.internal"] {
        let error = validated_identity("acme", reserved, &PanelHostNames::none())
            .map(|_| ())
            .expect_err("a reserved name must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{reserved}");
    }
}

#[test]
fn a_malformed_domain_is_still_refused() {
    assert!(validated_identity("acme", "bad_name.example.com", &PanelHostNames::none()).is_err());
}

/// The names of a host whose panel is served under `panel.example.com` and `srv1.provider.net`.
fn the_panel_names() -> PanelHostNames {
    PanelHostNames::parse_list("panel.example.com,srv1.provider.net").unwrap()
}

#[test]
fn a_domain_the_panel_is_served_under_is_refused_as_invalid_input() {
    for panel in [
        "panel.example.com",
        "srv1.provider.net",
        "www.panel.example.com",
        "PANEL.example.com",
    ] {
        let error = validated_identity("acme", panel, &the_panel_names())
            .map(|_| ())
            .expect_err("a panel name must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{panel}");
    }
}

#[test]
fn an_unrelated_domain_is_accepted_beside_the_panel_names() {
    let (_, domain) =
        validated_identity("acme", "shop.example.com", &the_panel_names()).expect("accepted");

    assert_eq!(domain.as_str(), "shop.example.com");
}
