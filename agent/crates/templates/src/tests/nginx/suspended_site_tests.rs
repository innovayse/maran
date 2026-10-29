//! Tests for the `suspended_site` module: the current stub carries the link rule
//! in its challenge location, and the legacy render is that stub without it.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md).
//! `suspended_site.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach the private rule line.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{SYMLINK_RULE_LINE, SuspendedSite};

/// The directive, as it appears in a render, without indentation.
const DIRECTIVE: &str = "disable_symlinks if_not_owner;";

/// Renders the stub for a site with the given aliases.
fn stub(aliases: &[String], legacy: bool) -> String {
    let site = SuspendedSite {
        domain: "gone.example",
        aliases,
        document_root: "/home/acme/sites/gone.example",
        access_log: "/var/log/maran/sites/acme/gone.example.access.log",
        error_log: "/var/log/maran/sites/acme/gone.example.error.log",
    };
    if legacy {
        site.render_legacy_config().unwrap()
    } else {
        site.render_config().unwrap()
    }
}

/// The text of the challenge location, from its opening line to its closing brace.
fn challenge_location(render: &str) -> &str {
    let start = render
        .find("location ^~ /.well-known/acme-challenge/ {")
        .expect("the stub has a challenge location");
    let rest = &render[start..];
    let end = rest.find("\n    }").expect("the challenge location closes");
    &rest[..end]
}

#[test]
fn the_current_stub_writes_the_link_rule_once_and_inside_its_challenge_location() {
    let render = stub(&[], false);

    assert_eq!(render.matches(DIRECTIVE).count(), 1);
    assert!(challenge_location(&render).contains(DIRECTIVE));
}

#[test]
fn the_rule_line_constant_is_the_line_the_current_stub_carries() {
    let render = stub(&[], false);

    assert_eq!(render.matches(SYMLINK_RULE_LINE).count(), 1);
    // A shorter indent would still be a substring of the rendered line, so the
    // constant must also be the WHOLE line: preceded by a line break.
    let whole_line = format!("\n{SYMLINK_RULE_LINE}");
    assert_eq!(render.matches(whole_line.as_str()).count(), 1);
}

#[test]
fn the_legacy_stub_carries_no_link_rule_at_all() {
    let render = stub(&[], true);

    assert!(!render.contains("disable_symlinks"));
}

#[test]
fn the_legacy_stub_is_the_current_stub_with_exactly_the_rule_line_removed() {
    let aliases = vec!["www.gone.example".to_owned()];
    let current = stub(&aliases, false);
    let legacy = stub(&aliases, true);

    assert_eq!(current.replacen(SYMLINK_RULE_LINE, "", 1), legacy);
    assert_eq!(current.lines().count(), legacy.lines().count() + 1);
    assert_ne!(current, legacy);
}

#[test]
fn the_legacy_stub_keeps_the_challenge_location_and_the_refusal() {
    let legacy = stub(&[], true);

    assert!(challenge_location(&legacy).contains("root /home/acme/sites/gone.example;"));
    assert!(legacy.contains("return 403 \"This site has been suspended.\";"));
}

#[test]
fn the_legacy_stub_ends_the_way_the_current_stub_does() {
    let current = stub(&[], false);
    let legacy = stub(&[], true);

    assert!(current.ends_with("}\n"));
    assert!(legacy.ends_with("}\n"));
}
