//! Tests for the `site_options_block` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SiteOptionsBlock;
use crate::nginx::ip_rules::IpRules;
use crate::nginx::redirect_line::RedirectLine;

/// One exact redirect, for the cases that need a rule.
const REDIRECTS: [RedirectLine<'static>; 1] = [RedirectLine {
    path: "/old",
    prefix: false,
    target: "/new",
    permanent: true,
}];

#[test]
fn no_options_restrict_nothing_and_add_no_location() {
    let none = SiteOptionsBlock::NONE;

    assert!(!none.restricts_access());
    assert!(!none.has_locations(true));
    assert!(!none.has_locations(false));
}

#[test]
fn a_password_or_an_address_list_each_restrict_access() {
    let networks = vec!["203.0.113.0/24".to_owned()];
    let password = SiteOptionsBlock {
        auth_user_file: Some("/etc/maran/nginx/auth/example.com.htpasswd"),
        ..SiteOptionsBlock::NONE
    };
    let list = SiteOptionsBlock {
        ip_rules: Some(IpRules {
            allow_list: false,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };

    assert!(password.restricts_access());
    assert!(list.restricts_access());
}

#[test]
fn an_upload_size_alone_restricts_nothing() {
    let options = SiteOptionsBlock {
        client_max_body_size_mib: Some(10),
        ..SiteOptionsBlock::NONE
    };

    assert!(!options.restricts_access());
    assert!(!options.has_locations(true));
}

#[test]
fn a_cache_rule_is_a_location_only_for_a_body_that_serves_files() {
    let options = SiteOptionsBlock {
        static_cache_days: Some(30),
        ..SiteOptionsBlock::NONE
    };

    assert!(options.has_locations(true));
    assert!(!options.has_locations(false));
    assert_eq!(options.render_locations(false, false).unwrap(), "");
}

#[test]
fn a_redirect_is_a_location_for_every_kind_of_body() {
    let options = SiteOptionsBlock {
        redirects: &REDIRECTS,
        ..SiteOptionsBlock::NONE
    };

    assert!(options.has_locations(true));
    assert!(options.has_locations(false));
    assert_eq!(
        options.render_locations(false, false).unwrap(),
        "    location = /old {\n        return 301 /new;\n    }"
    );
}

#[test]
fn the_cache_rule_falls_back_to_the_front_controller_only_when_asked() {
    let options = SiteOptionsBlock {
        static_cache_days: Some(1),
        ..SiteOptionsBlock::NONE
    };

    assert!(
        options
            .render_locations(true, true)
            .unwrap()
            .contains("try_files $uri /index.php?$query_string;")
    );
    assert!(
        options
            .render_locations(true, false)
            .unwrap()
            .contains("try_files $uri =404;")
    );
}

#[test]
fn an_allow_list_ends_in_deny_all_and_a_deny_list_in_allow_all() {
    let networks = vec!["203.0.113.0/24".to_owned(), "2001:db8::/32".to_owned()];
    let allow = SiteOptionsBlock {
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };
    let deny = SiteOptionsBlock {
        ip_rules: Some(IpRules {
            allow_list: false,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };

    assert_eq!(
        allow.render_access_rules().unwrap(),
        "    allow 203.0.113.0/24;\n    allow 2001:db8::/32;\n    deny all;"
    );
    assert_eq!(
        deny.render_access_rules().unwrap(),
        "    deny 203.0.113.0/24;\n    deny 2001:db8::/32;\n    allow all;"
    );
}

#[test]
fn a_password_renders_before_the_address_list() {
    let networks = vec!["203.0.113.0/24".to_owned()];
    let options = SiteOptionsBlock {
        auth_user_file: Some("/etc/maran/nginx/auth/example.com.htpasswd"),
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };

    assert_eq!(
        options.render_access_rules().unwrap(),
        "    auth_basic \"Restricted\";\n    auth_basic_user_file /etc/maran/nginx/auth/example.com.htpasswd;\n    allow 203.0.113.0/24;\n    deny all;"
    );
}

#[test]
fn a_rule_that_is_not_the_last_is_separated_from_the_next_by_a_blank_line() {
    let redirects = [
        RedirectLine {
            path: "/a",
            prefix: false,
            target: "/b",
            permanent: false,
        },
        RedirectLine {
            path: "/c/",
            prefix: true,
            target: "/d/",
            permanent: true,
        },
    ];
    let options = SiteOptionsBlock {
        redirects: &redirects,
        ..SiteOptionsBlock::NONE
    };

    assert_eq!(
        options.render_locations(true, false).unwrap(),
        "    location = /a {\n        return 302 /b;\n    }\n\n    location ^~ /c/ {\n        rewrite ^/c/(.*)$ /d/$1 permanent;\n    }"
    );
}

#[test]
fn a_request_limit_alone_restricts_nothing_and_adds_no_location() {
    let options = SiteOptionsBlock {
        request_rate: Some(crate::nginx::request_rate_line::RequestRateLine {
            zone: "rl_example.com",
            per_second: 5,
            burst: 0,
        }),
        ..SiteOptionsBlock::NONE
    };

    assert!(options.limits_requests());
    assert!(!options.restricts_access());
    assert!(!options.has_locations(true));
}

#[test]
fn no_options_limit_no_requests_and_render_no_limit_lines() {
    assert!(!SiteOptionsBlock::NONE.limits_requests());
    assert_eq!(SiteOptionsBlock::NONE.render_request_limit().unwrap(), "");
}

#[test]
fn the_request_limit_lines_render_without_a_trailing_newline() {
    let options = SiteOptionsBlock {
        request_rate: Some(crate::nginx::request_rate_line::RequestRateLine {
            zone: "rl_example.com",
            per_second: 5,
            burst: 3,
        }),
        ..SiteOptionsBlock::NONE
    };

    assert_eq!(
        options.render_request_limit().unwrap(),
        "    limit_req zone=rl_example.com burst=3 nodelay;\n    limit_req_status 429;\n    limit_req_log_level warn;"
    );
}
