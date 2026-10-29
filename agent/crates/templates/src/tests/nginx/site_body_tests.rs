//! Tests for the `site_body` module: a file-serving body writes the symbolic-link
//! rule once at server level beside its `root`, and a proxied body writes none.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md).
//! `site_body.rs` declares this file with `#[path]`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SiteBody;

/// The one rule line a file-serving body must carry, trimmed.
const RULE: &str = "disable_symlinks if_not_owner;";

/// Renders a PHP site's body.
fn php_body() -> String {
    SiteBody {
        access_log: "/var/log/maran/sites/acme/example.com.access.log",
        error_log: "/var/log/maran/sites/acme/example.com.error.log",
        document_root: "/home/acme/sites/example.com",
        fpm_socket: Some("/run/maran-php/acme-8.3.sock"),
        upstream: None,
    }
    .render_config()
    .unwrap()
}

/// Renders a static site's body.
fn static_body() -> String {
    SiteBody {
        access_log: "/var/log/maran/sites/acme/static.example.access.log",
        error_log: "/var/log/maran/sites/acme/static.example.error.log",
        document_root: "/home/acme/sites/static.example",
        fpm_socket: None,
        upstream: None,
    }
    .render_config()
    .unwrap()
}

/// Renders a proxied site's body.
fn proxy_body() -> String {
    SiteBody {
        access_log: "/var/log/maran/sites/acme/app.example.access.log",
        error_log: "/var/log/maran/sites/acme/app.example.error.log",
        document_root: "/home/acme/sites/app.example",
        fpm_socket: None,
        upstream: Some("127.0.0.1:3000"),
    }
    .render_config()
    .unwrap()
}

/// Every line of a render that is a `disable_symlinks` directive, trimmed.
///
/// Comment lines are not directives, so the prose that explains the rule does
/// not count as one.
fn rule_lines(render: &str) -> Vec<&str> {
    render
        .lines()
        .map(str::trim)
        .filter(|line| line.starts_with("disable_symlinks"))
        .collect()
}

/// Asserts the rule sits after `root` and before the first `location`, which is
/// what makes every location in the block inherit it.
fn assert_rule_is_between_root_and_first_location(render: &str) {
    let root = render.find("\n    root ").expect("the body writes a root");
    let rule = render
        .find("\n    disable_symlinks if_not_owner;")
        .expect("the body writes the rule at server level");
    let location = render
        .find("\n    location ")
        .expect("the body writes a location");

    assert!(root < rule, "the rule must follow the root");
    assert!(rule < location, "the rule must precede every location");
}

#[test]
fn a_php_body_writes_the_link_rule_once() {
    assert_eq!(rule_lines(&php_body()), vec![RULE]);
}

#[test]
fn a_static_body_writes_the_link_rule_once() {
    assert_eq!(rule_lines(&static_body()), vec![RULE]);
}

#[test]
fn a_php_body_places_the_rule_where_every_location_inherits_it() {
    assert_rule_is_between_root_and_first_location(&php_body());
}

#[test]
fn a_static_body_places_the_rule_where_every_location_inherits_it() {
    assert_rule_is_between_root_and_first_location(&static_body());
}

#[test]
fn a_proxied_body_writes_no_root_and_no_link_rule() {
    let render = proxy_body();

    assert!(rule_lines(&render).is_empty());
    assert!(!render.contains("root "));
}

#[test]
fn no_body_names_a_from_prefix_or_turns_the_rule_off() {
    for render in [php_body(), static_body(), proxy_body()] {
        for line in rule_lines(&render) {
            assert!(
                !line.contains("from="),
                "a from= prefix is followed unchecked"
            );
            assert!(!line.contains(" off"));
            assert!(!line.contains(" on"));
        }
    }
}

#[test]
fn a_body_does_not_start_with_the_comment_that_explains_it() {
    for render in [php_body(), static_body(), proxy_body()] {
        assert!(render.starts_with("    access_log "));
    }
}
