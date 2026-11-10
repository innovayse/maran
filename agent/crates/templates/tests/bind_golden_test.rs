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
        dnssec_policy_supported: false,
    }
}

/// The same configuration for a daemon that accepts a signing policy.
fn debian_conf_with_dnssec_policy() -> NamedConf {
    NamedConf {
        dnssec_policy_supported: true,
        ..debian_conf()
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
        dnssec_policy_supported: false,
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

/// The signing-policy variant is its own golden: the review artifact for the one
/// statement a daemon without `dnssec-policy` would refuse the whole file over.
#[test]
fn the_daemon_configuration_with_a_signing_policy_renders_its_golden() {
    assert_eq!(
        debian_conf_with_dnssec_policy().render_config().unwrap(),
        golden("bind/named_with_dnssec_policy.conf")
    );
}

/// The unsupported render states no policy anywhere: the statement is absent
/// and not merely commented out, so a daemon that cannot sign reads nothing of it.
#[test]
fn a_daemon_that_cannot_sign_is_told_nothing_about_a_policy() {
    let rendered = debian_conf().render_config().unwrap();

    assert!(!rendered.contains("dnssec-policy"));
    assert!(!rendered.contains("maran-csk"));
    assert!(!rendered.contains("csk"));
}

/// The supported render states the policy exactly once, at the top level of the
/// file and after the `options` block, never inside it.
#[test]
fn the_signing_policy_is_a_top_level_statement_after_the_options_block() {
    let rendered = debian_conf_with_dnssec_policy().render_config().unwrap();
    let statement = "dnssec-policy \"maran-csk\" { keys { csk lifetime unlimited algorithm ecdsap256sha256; }; };\n";

    assert_eq!(rendered.matches("dnssec-policy").count(), 1);
    assert!(rendered.contains(&format!("\n{statement}")));
    let options_end = rendered.find("\n};\n").unwrap();
    assert!(rendered.find(statement).unwrap() > options_end);
}

/// Turning the field on adds the policy and changes nothing else: removing the
/// statement from the supported render gives the unsupported one back.
#[test]
fn the_supported_render_differs_from_the_default_by_the_policy_statement_only() {
    let plain = debian_conf().render_config().unwrap();
    let signing = debian_conf_with_dnssec_policy().render_config().unwrap();
    let statement = "\ndnssec-policy \"maran-csk\" { keys { csk lifetime unlimited algorithm ecdsap256sha256; }; };\n";

    assert_ne!(plain, signing);
    assert_eq!(signing.replace(statement, ""), plain);
    assert!(signing.ends_with('\n'));
    assert!(plain.ends_with("};\n"));
}

/// The recursion refusal and the rate limit are in the supported render too: the
/// field cannot be the way to lose them.
#[test]
fn the_security_statements_survive_the_signing_policy() {
    let rendered = debian_conf_with_dnssec_policy().render_config().unwrap();

    assert!(rendered.contains("    recursion no;\n"));
    assert!(rendered.contains("    allow-recursion { none; };\n"));
    assert!(rendered.contains("    rate-limit {\n"));
    assert!(rendered.contains("    allow-transfer { none; };\n"));
}
