//! Tests for the `certificate_bytes` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::{FleetIdentityError, certificate_bytes};

/// A DER-looking blob: a SEQUENCE tag, then a short body.
const DER: [u8; 5] = [0x30, 0x03, 0x01, 0x02, 0x03];

/// Wraps `der` as one PEM block under `label`.
fn pem(label: &str, der: &[u8]) -> String {
    format!(
        "-----BEGIN {label}-----\n{}\n-----END {label}-----\n",
        STANDARD.encode(der)
    )
}

/// The reason a text was refused, panicking when it was accepted.
fn refusal(text: &str) -> &'static str {
    match certificate_bytes(Path::new("panel.pem"), text) {
        Err(FleetIdentityError::CertificateMalformed { reason, .. }) => reason,
        other => panic!("expected a malformed-certificate refusal, got {other:?}"),
    }
}

#[test]
fn one_certificate_block_yields_its_der_bytes() {
    let der = certificate_bytes(Path::new("panel.pem"), &pem("CERTIFICATE", &DER)).unwrap();

    assert_eq!(der, DER);
}

#[test]
fn surrounding_whitespace_and_windows_line_endings_are_tolerated() {
    let text = format!("\n\n{}\n\n", pem("CERTIFICATE", &DER).replace('\n', "\r\n"));

    assert_eq!(certificate_bytes(Path::new("p"), &text).unwrap(), DER);
}

#[test]
fn a_body_wrapped_over_several_lines_decodes_to_the_same_bytes() {
    let encoded = STANDARD.encode(DER);
    let (first, second) = encoded.split_at(4);
    let text =
        format!("-----BEGIN CERTIFICATE-----\n{first}\n{second}\n-----END CERTIFICATE-----\n");

    assert_eq!(certificate_bytes(Path::new("p"), &text).unwrap(), DER);
}

#[test]
fn a_private_key_block_is_not_accepted_as_a_certificate() {
    assert!(refusal(&pem("PRIVATE KEY", &DER)).contains("BEGIN CERTIFICATE"));
}

#[test]
fn two_certificates_in_one_file_are_refused_not_reduced_to_the_first() {
    let two = format!("{}{}", pem("CERTIFICATE", &DER), pem("CERTIFICATE", &DER));

    assert!(refusal(&two).contains("more than one"));
}

#[test]
fn text_before_or_after_the_block_is_refused() {
    let before = format!("# my panel\n{}", pem("CERTIFICATE", &DER));
    let after = format!("{}trailing\n", pem("CERTIFICATE", &DER));

    assert!(refusal(&before).contains("BEGIN CERTIFICATE"));
    assert!(refusal(&after).contains("after the END"));
}

#[test]
fn a_block_with_no_end_line_is_refused() {
    let text = format!("-----BEGIN CERTIFICATE-----\n{}\n", STANDARD.encode(DER));

    assert!(refusal(&text).contains("no END"));
}

#[test]
fn an_empty_body_and_an_empty_file_are_refused() {
    assert!(refusal("-----BEGIN CERTIFICATE-----\n-----END CERTIFICATE-----\n").contains("empty"));
    assert!(refusal("").contains("BEGIN CERTIFICATE"));
}

#[test]
fn a_body_that_is_not_base64_is_refused() {
    let text = "-----BEGIN CERTIFICATE-----\nnot base64 at all!\n-----END CERTIFICATE-----\n";

    assert!(refusal(text).contains("base64"));
}

#[test]
fn bytes_that_are_not_a_der_sequence_are_refused() {
    assert!(refusal(&pem("CERTIFICATE", &[0x31, 0x00])).contains("DER"));
}

#[test]
fn a_certificate_larger_than_any_real_one_is_refused() {
    let mut big = vec![0x30_u8];
    big.extend(std::iter::repeat_n(0_u8, 16 * 1024));

    assert!(refusal(&pem("CERTIFICATE", &big)).contains("too large"));
}
