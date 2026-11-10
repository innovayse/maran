//! Tests for the Apache `site_options_block` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SiteOptionsBlock;
use crate::nginx::ip_rules::IpRules;

#[test]
fn a_site_without_options_gets_the_default_body_limit_of_512_mib() {
    assert_eq!(SiteOptionsBlock::NONE.body_limit_bytes(), 512 * 1_048_576);
}

#[test]
fn a_configured_body_limit_replaces_the_default() {
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(100),
        ..SiteOptionsBlock::NONE
    };

    assert_eq!(options.body_limit_bytes(), 100 * 1_048_576);
}

#[test]
fn a_site_without_options_restricts_nothing() {
    assert!(!SiteOptionsBlock::NONE.restricts_access());
    assert!(!SiteOptionsBlock::NONE.has_locations(true));
}

#[test]
fn a_password_or_an_address_list_restricts_access() {
    let networks = vec!["203.0.113.0/24".to_owned()];
    let password = SiteOptionsBlock {
        auth_user_file: Some("/etc/maran/nginx/auth/x.htpasswd"),
        ..SiteOptionsBlock::NONE
    };
    let list = SiteOptionsBlock {
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };

    assert!(password.restricts_access());
    assert!(list.restricts_access());
}

#[test]
fn a_cache_rule_is_a_location_only_for_a_site_that_serves_files() {
    let options = SiteOptionsBlock {
        static_cache_days: Some(7),
        ..SiteOptionsBlock::NONE
    };

    assert!(options.has_locations(true));
    assert!(!options.has_locations(false));
}
