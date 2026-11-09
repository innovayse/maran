//! Byte-for-byte goldens for a vhost rendered with a per-address request limit
//! (issue #159), and the guards that hold the limit to its promises.
//!
//! The golden diff is the review artifact: a reviewer reads what nginx will be
//! told for the limit, not a template's intention (rules/testing.md). A site
//! WITHOUT a limit is held byte-identical by the goldens in `golden_test.rs`,
//! which is the accepting control for everything here.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nginx::ip_rules::IpRules;
use maran_templates::nginx::php_site::PhpSite;
use maran_templates::nginx::proxy_site::ProxySite;
use maran_templates::nginx::request_rate_line::RequestRateLine;
use maran_templates::nginx::site_body::SiteBody;
use maran_templates::nginx::site_options_block::SiteOptionsBlock;
use maran_templates::nginx::ssl_block::SslBlock;
use maran_templates::nginx::static_site::StaticSite;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// Options that carry only a request limit.
fn limited<'a>(zone: &'a str, per_second: u32, burst: u32) -> SiteOptionsBlock<'a> {
    SiteOptionsBlock {
        request_rate: Some(RequestRateLine {
            zone,
            per_second,
            burst,
        }),
        ..SiteOptionsBlock::NONE
    }
}

/// Renders the PHP fixture's body for `options`.
fn php_body(options: &SiteOptionsBlock<'_>) -> String {
    SiteBody {
        access_log: "/var/log/maran/sites/acme/example.com.access.log",
        error_log: "/var/log/maran/sites/acme/example.com.error.log",
        document_root: "/home/acme/sites/example.com",
        fpm_socket: Some("/run/maran-php/acme-8.3.sock"),
        upstream: None,
        options,
    }
    .render_config()
    .unwrap()
}

/// Renders the PHP fixture, with or without a certificate, for `options`.
fn php_site(options: &SiteOptionsBlock<'_>, tls: bool) -> String {
    let aliases = vec!["www.example.com".to_owned()];
    let body = php_body(options);
    let ssl = tls.then_some(SslBlock {
        domain: "example.com",
        aliases: &aliases,
        certificate_path: "/etc/maran/certs/example.com/fullchain.pem",
        certificate_key_path: "/etc/maran/certs/example.com/privkey.pem",
        server_body: &body,
    });
    PhpSite {
        domain: "example.com",
        aliases: &aliases,
        document_root: "/home/acme/sites/example.com",
        body: &body,
        options,
        ssl,
    }
    .render_config()
    .unwrap()
}

#[test]
fn a_php_site_with_a_request_limit_renders_its_golden() {
    let options = limited("rl_example.com", 20, 40);

    assert_eq!(
        php_site(&options, false),
        golden("nginx/php_site_rate.conf")
    );
}

#[test]
fn a_php_site_with_a_request_limit_and_a_certificate_renders_its_golden() {
    let options = limited("rl_example.com", 20, 40);

    assert_eq!(
        php_site(&options, true),
        golden("nginx/php_site_rate_ssl.conf")
    );
}

#[test]
fn a_static_site_with_a_request_limit_and_no_burst_renders_its_golden() {
    let aliases = vec!["www.static.example".to_owned()];
    let options = limited("rl_static.example", 5, 0);
    let body = SiteBody {
        access_log: "/var/log/maran/sites/acme/static.example.access.log",
        error_log: "/var/log/maran/sites/acme/static.example.error.log",
        document_root: "/home/acme/sites/static.example",
        fpm_socket: None,
        upstream: None,
        options: &options,
    }
    .render_config()
    .unwrap();
    let site = StaticSite {
        domain: "static.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/static.example",
        body: &body,
        options: &options,
        ssl: None,
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/static_site_rate.conf")
    );
}

#[test]
fn a_proxy_site_with_a_request_limit_renders_its_golden() {
    let aliases: Vec<String> = vec![];
    let options = limited("rl_app.example", 100, 200);
    let body = SiteBody {
        access_log: "/var/log/maran/sites/acme/app.example.access.log",
        error_log: "/var/log/maran/sites/acme/app.example.error.log",
        document_root: "/home/acme/sites/app.example",
        fpm_socket: None,
        upstream: Some("127.0.0.1:3000"),
        options: &options,
    }
    .render_config()
    .unwrap();
    let site = ProxySite {
        domain: "app.example",
        aliases: &aliases,
        document_root: "/home/acme/sites/app.example",
        body: &body,
        options: &options,
        ssl: None,
    };

    assert_eq!(
        site.render_config().unwrap(),
        golden("nginx/proxy_site_rate.conf")
    );
}

#[test]
fn the_zone_a_limit_names_is_the_zone_the_file_declares() {
    // One zone name, two places: a `limit_req` naming a zone the file does not
    // declare makes nginx refuse the whole configuration.
    let options = limited("rl_example.com", 20, 40);
    let site = php_site(&options, false);

    assert_eq!(site.matches("zone=rl_example.com").count(), 2, "{site}");
    assert!(site.contains("limit_req_zone $binary_remote_addr zone=rl_example.com:1m rate=20r/s;"));
    assert!(site.contains("limit_req zone=rl_example.com burst=40 nodelay;"));
}

#[test]
fn the_zone_is_declared_before_the_first_server_block() {
    // `limit_req_zone` is valid only at http level, and the vhost is included
    // there; inside `server {` nginx would refuse it.
    let site = php_site(&limited("rl_example.com", 20, 40), false);

    let zone = site.find("limit_req_zone").unwrap();
    let server = site.find("server {").unwrap();
    assert!(zone < server, "{site}");
}

#[test]
fn a_certificate_site_writes_the_limit_once_in_the_block_that_serves() {
    // The port-80 block of a certificate site only redirects, so it must carry
    // no `limit_req`; the TLS block serves and carries the one.
    let site = php_site(&limited("rl_example.com", 20, 40), true);

    assert_eq!(site.matches("limit_req zone=").count(), 1, "{site}");
    assert_eq!(site.matches("limit_req_zone").count(), 1, "{site}");
    assert!(site.find("limit_req zone=").unwrap() > site.find("listen 443").unwrap());
}

#[test]
fn a_refused_request_is_answered_429_and_logged_at_warn() {
    let body = php_body(&limited("rl_example.com", 20, 40));

    assert!(body.contains("limit_req_status 429;"), "{body}");
    assert!(body.contains("limit_req_log_level warn;"), "{body}");
}

#[test]
fn a_limit_with_no_burst_writes_neither_burst_nor_nodelay() {
    let body = php_body(&limited("rl_example.com", 20, 0));

    assert!(body.contains("limit_req zone=rl_example.com;"), "{body}");
    assert!(!body.contains("burst"), "{body}");
    assert!(!body.contains("nodelay"), "{body}");
}

#[test]
fn a_site_without_a_limit_writes_no_limit_directive_at_all() {
    // The inverse control of the cases above: they would all pass for a
    // template that wrote the directives for every site.
    let site = php_site(&SiteOptionsBlock::NONE, false);

    assert!(!site.contains("limit_req"), "{site}");
}

#[test]
fn the_limit_is_written_before_the_access_rules_so_both_apply_to_every_location() {
    let networks = vec!["203.0.113.0/24".to_owned()];
    let options = SiteOptionsBlock {
        request_rate: Some(RequestRateLine {
            zone: "rl_example.com",
            per_second: 20,
            burst: 0,
        }),
        auth_user_file: Some("/etc/maran/nginx/auth/example.com.htpasswd"),
        ip_rules: Some(IpRules {
            allow_list: true,
            networks: &networks,
        }),
        ..SiteOptionsBlock::NONE
    };
    let body = php_body(&options);

    let limit = body.find("limit_req zone=").unwrap();
    let auth = body.find("auth_basic ").unwrap();
    let first_location = body.find("location ").unwrap();
    assert!(limit < auth && auth < first_location, "{body}");
}

#[test]
fn the_challenge_location_carries_no_limit_directive_of_its_own() {
    // `limit_req off;` is not a documented spelling; writing it would make
    // nginx refuse the vhost. The template comment says so.
    let site = php_site(&limited("rl_example.com", 20, 40), false);

    assert!(!site.contains("limit_req off"), "{site}");
}
