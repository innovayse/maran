//! Byte-for-byte golden for the DNS daemon's `named.conf`, and the guards that
//! hold the security statements in it.
//!
//! The golden diff is the review artifact: a reviewer reads exactly what the
//! daemon will be told about recursion and rate limiting, not a template's
//! intention (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::bind::named_conf::NamedConf;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// The configuration of the Debian family's state directory.
fn debian_conf() -> NamedConf {
    NamedConf {
        control_key_path: "/etc/maran/bind/rndc.key".to_owned(),
        control_key_name: "maran-rndc".to_owned(),
        control_port: 953,
        state_directory: "/var/lib/bind/maran".to_owned(),
    }
}

/// The default render is the golden, byte for byte.
#[test]
fn the_daemon_configuration_renders_its_golden() {
    assert_eq!(
        debian_conf().render_config().unwrap(),
        golden("bind/named.conf")
    );
}

/// Every field reaches the text: the golden uses the values a family would
/// answer with, and the one family fact that differs (the state directory) is a
/// field and not a literal. Sentinel spellings must each appear.
#[test]
fn every_field_reaches_the_rendered_configuration() {
    let rendered = NamedConf {
        control_key_path: "/sentinel/key-path".to_owned(),
        control_key_name: "sentinel-key-name".to_owned(),
        control_port: 40_953,
        state_directory: "/sentinel/state".to_owned(),
    }
    .render_config()
    .unwrap();

    assert!(rendered.contains("include \"/sentinel/key-path\";"));
    assert!(rendered.contains("keys { \"sentinel-key-name\"; }"));
    assert!(rendered.contains("port 40953 "));
    assert!(rendered.contains("directory \"/sentinel/state\";"));
    assert!(rendered.contains("new-zones-directory \"/sentinel/state\";"));
}

/// The three statements that make this an authoritative-only server are in
/// every render, whatever the fields are. A render type with a field that could
/// switch one off would fail here.
#[test]
fn recursion_is_refused_and_responses_are_rate_limited_in_every_render() {
    let rendered = debian_conf().render_config().unwrap();

    assert!(rendered.contains("    recursion no;\n"));
    assert!(rendered.contains("    allow-recursion { none; };\n"));
    assert!(rendered.contains("    allow-query-cache { none; };\n"));
    assert!(rendered.contains("    rate-limit {\n"));
    assert!(rendered.contains("    allow-transfer { none; };\n"));
}

/// The control channel is bound to the loopback address and no other.
#[test]
fn the_control_channel_listens_on_loopback_only() {
    let rendered = debian_conf().render_config().unwrap();

    assert!(rendered.contains("inet 127.0.0.1 port 953 allow { 127.0.0.1; }"));
    assert!(!rendered.contains("inet *"));
    assert!(!rendered.contains("inet 0.0.0.0"));
}
