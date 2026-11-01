//! Tests for [`SshPublicKey`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

const ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIG7OpD9/qnELjIQS7Z+hv9E4OBM+3IqncDtZr2x3fz0s";
const ECDSA_P256: &str = "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBBbfqh57sYJeeVQwCn5CcNB8galBzU3DfIwZPvY3i8o0BLGROfIdOghWa0C0JqXSzwQhCY77nSKmlfkD3xFsSkM=";
const ECDSA_P384: &str = "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBLKfX9aYPSTjMCSuSG6hTKsHBFGJLcux+pGng6tTD5hwI/MRKe3KYifLNRH9UqZBOK5nxsg5NTD1JkRkc81rJVCDupbjmteXi51VqKpxqg5yM6nE/KZ0C9nohO6nehm2qw==";
const RSA_3072: &str = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQC3CDLyIR6mdHi9dsMpwfYSvwHPGmb+pwznnBrZX8n1JguwJGH+SZojV3JTwha9iNOEmWyP6R2x1I9xxa5f8jTifz+oOPbx7NrHz4qVmXxfAZvjbgxBw7X7nVZQulX9DobD96IOtQTXbceVOP1FRZE6K9dqvZqMZOvO+okks+W84LlVOKLWmjYbrBvykzhrsJAYxZzN31Bi7Fs/sJvx4pRdhpON76rXUu0FVThqY05xAaFTjxh3bewENAgyC41hutqANknQ4FQr4zg3JQKTAnqeGXc5i2lDJMFCBTMQOGPeD5VnbI4nMCH6JESWScRQQkjDxtIPgZ2pa/syB3kd3A2N9+fjsDFykJox5eDWE+36VbJKJewCsrIez4HjUbPwajo5kvTl1x6+vp8LsoKNAGJhAooGKjtdgBq5C8Wod3SY+x7YMdPSianBml1QexmkId0nHhDh28s5xXw+T6wjqgyj+r9YSaK9FZLDbI0S6GNqh/Y3QCdnFHjxM0ySPPJj/K8=";
const RSA_1024: &str = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAAAgQDJEa0gxf9pfFgwb2LQiyLEwKV8+JL/6RNdgZXZo/Dg+ekU1sXnE0MXm4Ft+4xlswgiwk9TqVTmfGEz6WOZo6fpaD4IdHATq9C5DMy9Kv2flz2+0q3WHDPoweKQnVsX69Cgra+kJU4VcAm9NtkKSmMPbNOE7fmoPhU/NWIGX1HrHw==";

#[test]
fn an_ed25519_key_is_accepted_and_its_comment_dropped() {
    let key = SshPublicKey::parse(&format!("  {ED25519} dev@laptop  ")).unwrap();

    assert_eq!(key.algorithm(), "ssh-ed25519");
    assert_eq!(key.as_line(), ED25519);
}

#[test]
fn ecdsa_and_rsa_keys_of_accepted_sizes_are_accepted() {
    for text in [ECDSA_P256, ECDSA_P384, RSA_3072] {
        assert_eq!(SshPublicKey::parse(text).unwrap().as_line(), text);
    }
}

#[test]
fn an_rsa_key_under_2048_bits_is_refused() {
    assert_eq!(
        SshPublicKey::parse(RSA_1024),
        Err(SshPublicKeyError::RsaSizeRefused { bits: 1024 })
    );
}

#[test]
fn a_leading_command_option_is_refused_as_an_unsupported_algorithm() {
    let text = format!("command=\"/bin/sh\" {ED25519}");

    assert_eq!(
        SshPublicKey::parse(&text),
        Err(SshPublicKeyError::UnsupportedAlgorithm)
    );
}

#[test]
fn a_restrict_option_supplied_by_the_caller_is_refused_too() {
    assert_eq!(
        SshPublicKey::parse(&format!("restrict {ED25519}")),
        Err(SshPublicKeyError::UnsupportedAlgorithm)
    );
}

#[test]
fn a_dsa_key_is_refused() {
    assert_eq!(
        SshPublicKey::parse("ssh-dss AAAAB3NzaC1kc3M="),
        Err(SshPublicKeyError::UnsupportedAlgorithm)
    );
}

#[test]
fn a_newline_inside_the_comment_is_refused_before_anything_is_split() {
    let text = format!("{ED25519} me\n{RSA_3072}");

    assert_eq!(
        SshPublicKey::parse(&text),
        Err(SshPublicKeyError::ControlCharacter)
    );
}

#[test]
fn carriage_return_tab_and_nul_are_refused() {
    for control in ['\r', '\t', '\0'] {
        let text = format!("{ED25519}{control}");
        assert_eq!(
            SshPublicKey::parse(&text),
            Err(SshPublicKeyError::ControlCharacter)
        );
    }
}

#[test]
fn an_empty_key_is_refused() {
    assert_eq!(SshPublicKey::parse("   "), Err(SshPublicKeyError::Empty));
}

#[test]
fn a_key_over_the_ceiling_is_refused() {
    let text = format!("{ED25519} {}", "a".repeat(SshPublicKey::MAX_LENGTH));

    assert!(matches!(
        SshPublicKey::parse(&text),
        Err(SshPublicKeyError::TooLong { .. })
    ));
}

#[test]
fn a_missing_or_undecodable_data_field_is_refused() {
    assert_eq!(
        SshPublicKey::parse("ssh-ed25519"),
        Err(SshPublicKeyError::InvalidEncoding)
    );
    assert_eq!(
        SshPublicKey::parse("ssh-ed25519 not*base64"),
        Err(SshPublicKeyError::InvalidEncoding)
    );
}

#[test]
fn a_blob_naming_another_algorithm_is_refused() {
    let data = ED25519.split(' ').nth(1).unwrap();

    assert_eq!(
        SshPublicKey::parse(&format!("ssh-rsa {data}")),
        Err(SshPublicKeyError::AlgorithmMismatch)
    );
}

#[test]
fn bytes_trailing_the_key_are_refused() {
    let data = ED25519.split(' ').nth(1).unwrap();
    let mut blob = STANDARD.decode(data).unwrap();
    blob.extend_from_slice(&[0, 0, 0, 1, b'x']);

    assert_eq!(
        SshPublicKey::parse(&format!("ssh-ed25519 {}", STANDARD.encode(blob))),
        Err(SshPublicKeyError::Malformed)
    );
}

#[test]
fn a_truncated_key_is_refused() {
    let data = ED25519.split(' ').nth(1).unwrap();
    let mut blob = STANDARD.decode(data).unwrap();
    blob.truncate(blob.len() - 1);

    assert_eq!(
        SshPublicKey::parse(&format!("ssh-ed25519 {}", STANDARD.encode(blob))),
        Err(SshPublicKeyError::Malformed)
    );
}

fn wire(fields: &[&[u8]]) -> String {
    let mut blob = Vec::new();
    for field in fields {
        blob.extend_from_slice(&u32::try_from(field.len()).unwrap().to_be_bytes());
        blob.extend_from_slice(field);
    }
    STANDARD.encode(blob)
}

#[test]
fn an_ecdsa_point_of_another_curves_length_is_refused() {
    let mut point = vec![0x04_u8; 97];
    point[1] = 1;
    let data = wire(&[b"ecdsa-sha2-nistp256", b"nistp256", &point]);

    assert_eq!(
        SshPublicKey::parse(&format!("ecdsa-sha2-nistp256 {data}")),
        Err(SshPublicKeyError::Malformed)
    );
}

#[test]
fn an_ecdsa_key_naming_another_curve_is_refused() {
    let point = vec![0x04_u8; 65];
    let data = wire(&[b"ecdsa-sha2-nistp256", b"nistp384", &point]);

    assert_eq!(
        SshPublicKey::parse(&format!("ecdsa-sha2-nistp256 {data}")),
        Err(SshPublicKeyError::Malformed)
    );
}

#[test]
fn a_compressed_ecdsa_point_is_refused() {
    let point = vec![0x02_u8; 65];
    let data = wire(&[b"ecdsa-sha2-nistp256", b"nistp256", &point]);

    assert_eq!(
        SshPublicKey::parse(&format!("ecdsa-sha2-nistp256 {data}")),
        Err(SshPublicKeyError::Malformed)
    );
}

#[test]
fn a_security_key_ed25519_carries_its_application_field() {
    let data = wire(&[b"sk-ssh-ed25519@openssh.com", &[7_u8; 32], b"ssh:"]);

    assert!(SshPublicKey::parse(&format!("sk-ssh-ed25519@openssh.com {data}")).is_ok());
}

#[test]
fn an_rsa_key_over_16384_bits_is_refused() {
    let mut modulus = vec![0xff_u8; 2049];
    modulus[0] = 0x01;
    let data = wire(&[b"ssh-rsa", &[1, 0, 1], &modulus]);

    assert_eq!(
        SshPublicKey::parse(&format!("ssh-rsa {data}")),
        Err(SshPublicKeyError::RsaSizeRefused { bits: 16385 })
    );
}

#[test]
fn an_rsa_key_with_an_empty_exponent_is_refused() {
    let data = wire(&[b"ssh-rsa", &[0], &[0xff_u8; 256]]);

    assert_eq!(
        SshPublicKey::parse(&format!("ssh-rsa {data}")),
        Err(SshPublicKeyError::Malformed)
    );
}
