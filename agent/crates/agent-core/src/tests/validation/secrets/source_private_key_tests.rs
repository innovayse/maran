//! Tests for the `source_private_key` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::{SourcePrivateKey, SourcePrivateKeyError};

/// A planted marker inside the key material.
const SENTINEL: &[u8] = b"PLANTED-KEY-SENTINEL-0123456789";

/// A key document whose decoded body starts with the OpenSSH magic and carries
/// the sentinel, wrapped at 70 characters like `ssh-keygen` does.
fn document() -> String {
    let mut raw = b"openssh-key-v1\0".to_vec();
    raw.extend_from_slice(SENTINEL);
    raw.extend_from_slice(&[9_u8; 120]);
    let encoded = STANDARD.encode(raw);
    let wrapped: Vec<&str> = encoded
        .as_bytes()
        .chunks(70)
        .map(|chunk| core::str::from_utf8(chunk).unwrap())
        .collect();

    format!(
        "-----BEGIN OPENSSH PRIVATE KEY-----\n{}\n-----END OPENSSH PRIVATE KEY-----\n",
        wrapped.join("\n")
    )
}

#[test]
fn a_well_formed_key_document_is_accepted_and_exposed_unchanged() {
    let text = document();
    let key = SourcePrivateKey::parse(text.as_bytes()).unwrap();

    assert_eq!(key.expose(), text);
}

#[test]
fn a_document_without_a_final_line_feed_gets_exactly_one() {
    let text = document();
    let without = text.trim_end_matches('\n');
    let key = SourcePrivateKey::parse(without.as_bytes()).unwrap();

    assert_eq!(key.expose(), text);
    assert!(key.expose().ends_with("-----\n"));
    assert!(!key.expose().ends_with("\n\n"));
}

#[test]
fn debug_of_the_wrapper_never_prints_the_key() {
    let key = SourcePrivateKey::parse(document().as_bytes()).unwrap();
    let body_line = key.expose().lines().nth(1).unwrap().to_owned();
    let printed = format!("{key:?}");

    assert!(printed.contains("«redacted»"));
    assert!(!printed.contains(&body_line));
    assert!(!printed.contains("BEGIN"));
    // The positive control: the hunt can find the body line where it is.
    assert!(key.expose().contains(&body_line));
}

#[test]
fn an_empty_candidate_is_refused() {
    assert_eq!(
        SourcePrivateKey::parse(b"").unwrap_err(),
        SourcePrivateKeyError::Empty
    );
}

#[test]
fn a_candidate_over_sixteen_kib_is_refused() {
    let big = vec![b'A'; SourcePrivateKey::MAX_LENGTH + 1];

    assert_eq!(
        SourcePrivateKey::parse(&big).unwrap_err(),
        SourcePrivateKeyError::TooLong { limit: 16 * 1024 }
    );
}

#[test]
fn control_characters_and_non_ascii_bytes_are_refused() {
    let good = document();
    for bad in [
        good.replacen('\n', "\r\n", 1).into_bytes(),
        good.replacen('A', "\0", 1).into_bytes(),
        good.replacen('A', "\t", 1).into_bytes(),
        good.replacen('A', "\u{e9}", 1).into_bytes(),
        vec![0xff, 0xfe, b'\n'],
    ] {
        assert_eq!(
            SourcePrivateKey::parse(&bad).unwrap_err(),
            SourcePrivateKeyError::ForbiddenCharacter,
            "{bad:?}"
        );
    }
}

#[test]
fn another_pem_type_or_a_missing_header_is_refused() {
    let good = document();
    for bad in [
        good.replace("OPENSSH PRIVATE KEY", "RSA PRIVATE KEY"),
        good.replacen("-----BEGIN OPENSSH PRIVATE KEY-----\n", "", 1),
        format!("junk\n{good}"),
        format!(" {good}"),
    ] {
        assert_eq!(
            SourcePrivateKey::parse(bad.as_bytes()).unwrap_err(),
            SourcePrivateKeyError::MissingHeader,
            "{bad:?}"
        );
    }
}

#[test]
fn a_missing_footer_or_trailing_content_is_refused() {
    let good = document();
    let no_footer = good.replace("-----END OPENSSH PRIVATE KEY-----\n", "");
    let trailing = format!("{good}extra\n");
    let blank_after = format!("{good}\n");

    for bad in [no_footer, trailing, blank_after] {
        assert_eq!(
            SourcePrivateKey::parse(bad.as_bytes()).unwrap_err(),
            SourcePrivateKeyError::MissingFooter,
            "{bad:?}"
        );
    }
}

#[test]
fn two_documents_in_a_row_are_refused_because_the_first_footer_is_not_base64() {
    let good = document();

    assert_eq!(
        SourcePrivateKey::parse(format!("{good}{good}").as_bytes()).unwrap_err(),
        SourcePrivateKeyError::MalformedBody
    );
}

#[test]
fn a_header_and_footer_with_nothing_between_are_refused() {
    let empty =
        "-----BEGIN OPENSSH PRIVATE KEY-----\n-----END OPENSSH PRIVATE KEY-----\n".to_owned();

    assert_eq!(
        SourcePrivateKey::parse(empty.as_bytes()).unwrap_err(),
        SourcePrivateKeyError::MalformedBody
    );
}

#[test]
fn a_body_that_is_not_base64_or_lacks_the_magic_is_refused() {
    let frame = |body: &str| {
        format!("-----BEGIN OPENSSH PRIVATE KEY-----\n{body}\n-----END OPENSSH PRIVATE KEY-----\n")
    };
    let wrong_magic = STANDARD.encode(b"not-an-openssh-key-document-at-all");

    for body in [
        "!!!! not base64 !!!!".to_owned(),
        wrong_magic,
        "AAAA\n\nAAAA".to_owned(),
        "A".repeat(300),
        "-----BEGIN OPENSSH PRIVATE KEY-----".to_owned(),
    ] {
        assert_eq!(
            SourcePrivateKey::parse(frame(&body).as_bytes()).unwrap_err(),
            SourcePrivateKeyError::MalformedBody,
            "{body:?}"
        );
    }
}

#[test]
fn a_refusal_never_names_the_key() {
    let text = document();
    let body_line = text.lines().nth(1).unwrap().to_owned();
    let mut bad = text.into_bytes();
    bad.push(0);
    let message = SourcePrivateKey::parse(&bad).unwrap_err().to_string();

    assert!(!message.contains(&body_line));
    assert!(!message.contains("BEGIN"));
}
