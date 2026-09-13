//! The check that decides whether the pair belongs together.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sites::fake_site_host::distro;
use crate::ssl::fake_ssl_host::{FakeSslHost, KEY_PEM, matching_material, mismatched_material};
use crate::ssl::key_matches_certificate::key_matches_certificate;
use crate::ssl::ssl_op_error::SslOpError;

#[test]
fn a_pair_with_one_public_key_matches() {
    let host = FakeSslHost::passing();

    assert!(key_matches_certificate(&host, distro(), &matching_material()).unwrap());
}

#[test]
fn a_pair_with_two_public_keys_does_not() {
    let host = FakeSslHost::passing().with_mismatched_key();

    assert!(!key_matches_certificate(&host, distro(), &mismatched_material()).unwrap());
}

#[test]
fn both_halves_are_handed_to_the_tool_on_standard_input() {
    let host = FakeSslHost::passing();

    key_matches_certificate(&host, distro(), &matching_material()).unwrap();

    // Not `-in <path>`, and not the PEM as an argument: an argv is readable by
    // every user on the host through `/proc/<pid>/cmdline`, and a temporary
    // file survives a crash.
    assert_eq!(
        host.arguments(),
        vec![
            vec!["x509".to_owned(), "-noout".to_owned(), "-pubkey".to_owned()],
            vec![
                "pkey".to_owned(),
                "-pubout".to_owned(),
                // An empty passphrase, supplied explicitly: without it an
                // encrypted key makes openssl prompt, and whether that fails or
                // blocks the agent forever depends on there being a tty.
                "-passin".to_owned(),
                "pass:".to_owned(),
            ],
        ]
    );
}

#[test]
fn a_key_the_tool_refuses_produces_an_error_carrying_nothing() {
    let host = FakeSslHost::passing();
    // The tool echoing the key it choked on is the leak this design makes
    // impossible: `run_with_private_key` returns an outcome that has no stderr
    // field for the operation to reach, so there is no filter to get wrong.
    host.refuse("pkey", KEY_PEM);

    let failure = key_matches_certificate(&host, distro(), &matching_material()).unwrap_err();

    assert!(matches!(failure, SslOpError::MalformedPrivateKey));
    assert!(!failure.to_string().contains("PRIVATE KEY"));
}

#[test]
fn a_certificate_the_tool_refuses_carries_the_tools_own_words() {
    let host = FakeSslHost::passing();
    host.refuse("x509", "unable to load certificate");

    let failure = key_matches_certificate(&host, distro(), &matching_material()).unwrap_err();

    match failure {
        SslOpError::MalformedCertificate { reason } => {
            // Carried unfiltered on purpose: this process was fed the
            // certificate, which every visitor is handed anyway, so openssl's
            // complaint is evidence an operator needs and not a secret.
            assert_eq!(reason, "unable to load certificate");
        }
        other => panic!("expected a malformed certificate, got {other:?}"),
    }
}
