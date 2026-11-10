//! Byte-for-byte goldens for the three DNSSEC texts the agent writes or asks the
//! daemon about, and the guards that hold what a golden cannot (issue #452).
//!
//! The golden diff is the review artifact: a reviewer reads exactly what lands in
//! a key file and what the daemon's checker is shown (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::bind::dnssec_private_key::DnssecPrivateKey;
use maran_templates::bind::dnssec_probe::DnssecProbe;
use maran_templates::bind::dnssec_public_key::DnssecPublicKey;
use maran_templates::bind::named_conf::NamedConf;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// A placeholder private scalar, recognisable in a golden and never a real key.
const PRIVATE_SENTINEL: &str = "SENTINELPRIVATEKEYBASE64SENTINELPRIVATEKEYBASE64=";

/// A placeholder public key, recognisable in a golden and never a real key.
const PUBLIC_SENTINEL: &str = "SENTINELPUBLICKEYBASE64";

/// The private-key file is its golden, byte for byte.
#[test]
fn the_private_key_file_renders_its_golden() {
    let rendered = DnssecPrivateKey {
        algorithm: 13,
        private_key: PRIVATE_SENTINEL,
    }
    .render_config()
    .unwrap();

    assert_eq!(rendered, golden("bind/dnssec_private_key.conf"));
}

/// The public-key file is its golden, byte for byte.
#[test]
fn the_public_key_file_renders_its_golden() {
    let rendered = DnssecPublicKey {
        zone: "example.com.",
        key_tag: 12345,
        flags: 257,
        protocol: 3,
        algorithm: 13,
        public_key: PUBLIC_SENTINEL,
    }
    .render_config()
    .unwrap();

    assert_eq!(rendered, golden("bind/dnssec_public_key.conf"));
}

/// The probe is its golden, byte for byte.
#[test]
fn the_probe_renders_its_golden() {
    assert_eq!(
        DnssecProbe {}.render_config().unwrap(),
        golden("bind/dnssec_probe.conf")
    );
}

/// Every input reaches the text under a spelling no golden shares: a value written
/// as a literal in the template would not follow.
#[test]
fn every_field_reaches_the_rendered_key_files() {
    let private = DnssecPrivateKey {
        algorithm: 8,
        private_key: "PlantedPrivate==",
    }
    .render_config()
    .unwrap();
    let public = DnssecPublicKey {
        zone: "planted.test.",
        key_tag: 4242,
        flags: 256,
        protocol: 3,
        algorithm: 8,
        public_key: "PlantedPublic==",
    }
    .render_config()
    .unwrap();

    assert!(private.contains("Algorithm: 8 "));
    assert!(private.contains("PrivateKey: PlantedPrivate==\n"));
    assert!(public.contains("keyid 4242, for planted.test."));
    assert!(public.contains("planted.test. IN DNSKEY 256 3 8 PlantedPublic==\n"));
}

/// The private-key file is the only one of the two that names the private half,
/// and the public file carries none of it: swapping the fields would leak.
#[test]
fn the_public_file_never_carries_the_private_half() {
    let public = DnssecPublicKey {
        zone: "example.com.",
        key_tag: 1,
        flags: 257,
        protocol: 3,
        algorithm: 13,
        public_key: PUBLIC_SENTINEL,
    }
    .render_config()
    .unwrap();

    assert!(!public.contains(PRIVATE_SENTINEL));
    assert!(!public.contains("PrivateKey"));
}

/// The drift guard: the policy line the probe shows the checker is the policy line
/// `named.conf` states. A probe that accepted one statement while the real file
/// stated another would be a check of nothing.
#[test]
fn the_probe_states_the_policy_named_conf_states() {
    let policy_line = DnssecProbe {}
        .render_config()
        .unwrap()
        .lines()
        .find(|line| line.starts_with("dnssec-policy"))
        .unwrap()
        .to_owned();
    let named = NamedConf {
        control_key_path: "/k".to_owned(),
        control_key_name: "n".to_owned(),
        control_port: 953,
        state_directory: "/s".to_owned(),
        dnssec_policy_supported: true,
    }
    .render_config()
    .unwrap();

    assert!(named.lines().any(|line| line == policy_line));
}

/// The inverse control for the guard above: a configuration that does not state
/// the policy has no such line, so the guard can fail.
#[test]
fn a_named_conf_without_the_policy_has_no_policy_line() {
    let named = NamedConf {
        control_key_path: "/k".to_owned(),
        control_key_name: "n".to_owned(),
        control_port: 953,
        state_directory: "/s".to_owned(),
        dnssec_policy_supported: false,
    }
    .render_config()
    .unwrap();

    assert!(!named.lines().any(|line| line.starts_with("dnssec-policy")));
}
