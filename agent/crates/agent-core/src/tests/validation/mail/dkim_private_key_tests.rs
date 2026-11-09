//! Tests for the `dkim_private_key` module.
//!
//! The key is a credential and a file a root process writes: the cases are the
//! ways a document can be key-shaped without being a usable RSA key. Sizes other
//! than the two real fixtures are built as DER by hand, which exercises the
//! size bounds without generating a key of every size.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::{DkimPrivateKey, MAXIMUM_LENGTH};
use crate::validation::mail::dkim_private_key_error::DkimPrivateKeyError;

/// An inert 2048-bit RSA key generated for these tests; it signs nothing real.
const RSA_2048: &str = "-----BEGIN PRIVATE KEY-----
MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC8hkcO+IyzMduq
jmG5Zz98yYDDuyULuW1hex5dNOem4OF/Xc+D+vlaPDhb+0lCmElBhBM2tgyTOGh8
fZ1SqytDZ82BiSglU9aGykDsn6WzLo7sWqzCV6tGh+e74AOu+Roy2wLRGecyvNTc
IoWEqtX6rElvANN+fF+qDTQ8rRzCXwUISxYB2aI0d6ox8ciNFjXYR1Ksfa5+DfV+
fIoRjBrG0B24jdhYhSZJfmjJPNQkIlcEH735KpqkkM0KRu/z7QdFK+PAzIxiOSsw
Y7RkJi2KjbsC4x4ICtImuFvXWm1J9FO8h7jpoT6Ailfir1jUUwpHKTfNZB5tQ/fO
5WBcBxdDAgMBAAECggEAGZlWHsdLO63ieRdaV7WYXlZyQKGx1xS3sIvqRJfBeNyC
YiwX2IEEyObydDL78KoDEH1hqoUv0fHP5k+cRDRv7Hg05Ij+RYtLzwO5qyfGdT02
NQve94i1/h7ILa+ucFzs3Mj2afRNe4s+kCGz5Z7Y7W/ljMgc78jtqIageDi9dfr7
BeLrkoSZmoocYHv1VB254Id4iyilxx0rAwuHZaLhSWSpEwpVcf1K56pWaV97mS5h
9dmr5dGYRr55POtB1zQM2og8G2BOaykic2Tx4ZPuKQjxPdRvSAWDMbAlqtM+sOTK
+UL98Lxnxt8Iw1mT0ZK54+HRHLyA48Vx+WRvW/oaMQKBgQD1+n624fcVjOxuigHQ
Myq5zAriColqEn+20x59UTV+P1pXM8mCP/9zpJJfKU1VQVo201AJNXtzPqSgtg7N
kzbjkOifDDbCZTfGSwNKr2/BI2YSKOnWDUOOFfUYbNgRZedRkE5RYmDWArr6QEJH
oHa2Q1m/ZqNzIaylMhO5t/D4ywKBgQDENIyq6emfaB7IZa08RMY7K5UZP5CIbs7S
3043EDA1l4rgj8XnkAdw+ED8MuG+5xjtz/3FD7OyRtxwH0XzEjVu2zYLuocuX2cC
5kHL7l4djYcLPfpmX50WO8KQDYkw0n0lq4U6qMFqJujV14VIzi5yB8nrG/9dIG44
ugjkKY2kaQKBgQDkdziQlsrPOmgzt309qvipP6pwDwdY/TugTPEJxo7f5un7Cr/E
ebrD53yIZK1OXXf0akrxcrpp7Sotl0uih3S441QcvdRytF6cj/nLua0b/8wDW/rs
9L7lTuGqQRZi+npi2JzVnIGC5WBTpbGqI5F/coj+bM+t1P4tJZkKq51nBwKBgAo5
TsctLy5OR6Rbn/5O+yFkMP3C0S46z+4vEq1N26U/Q05WT5K0b57UYNaDdD31Xtgt
iWS2X7+/b4csKgHzhfND2vobeyXQdNay1uTgg+j4Ufwk+6JQAyiAbrAA9JAlt725
xVR8LK/yXRmfhGN50vPAMiZdLB5XLUcpcoMpIydZAoGBAKt+aw5aTjM/0OGqLpUX
PjY4563f+hOxLDnX31KaDaBiC6RojHNHBcDUNUb6P5RHKPlScHyzSdR43Nu6r2a6
XPO9UnLQmrr11Mc1Sjt0Wtl6gb98Tgi/8yxJ6kJnvw9s/Ey+ROMvvjzFlEiSPjNZ
V63OXg8KbomKwioobhcAFifx
-----END PRIVATE KEY-----
";

/// An inert 1024-bit RSA key generated for these tests; below the accepted size.
const RSA_1024: &str = "-----BEGIN PRIVATE KEY-----
MIICdwIBADANBgkqhkiG9w0BAQEFAASCAmEwggJdAgEAAoGBAMKTTJAYw17zOatM
NNUFa4i2ylhbteqEw4D/Pnat9m9I7P7DzloJ7fywEq3IWxEgG1aR7OS522diSuRQ
6O5izO33vSNzFRheFTJrJ1hcf+4Wj7H5Wcvkks/5Nbq07YlIHQcRRun6u3ucPbm/
4iqKbddx8/LKZ1BgfCQU20Eb7FshAgMBAAECgYEAouifJtqE4T+1nhVM+rnyAeJB
Yl9O3Y7Q/XU3gDhA54XgX6HjaIF9z54/1V612I3QNdgxxoNSDqo+WBDiJY11U6OW
AYsJ3/noKN1EWUA9/GV2UjR7IO+n0M1pIvyOCOGyRdf8zCFbEpvvdJPtuofGvRi6
dCIYLJmDP9jH5/nPXtECQQD/APSUUHeKwe2fdYsh1uSI3eYw7jH1EsPk+2+qMBTD
X5KhFRqK5duWbpgpuSDH+o8jTX2+VZeJYbElfmH86oFtAkEAw1Xn2OWeKZp/cFpF
ISJiOUKEL+6UChSkDmVo43YSNpjaAAO7v25xfTQMSDsxrgc5xaVF2AY2tQ4nWp06
e2akBQJBALEaLMNFD4EjJrF0pWifIqGb219oTvHyUnMZ3XfJZOI74k5VpdWJxnQ3
4x43IPlCn17ZVtd2AoS7gJiVqeBJzhECQFAFgVV5u7GPeWWtlWRdkZ+jgEOimUMH
8a10HeQRGqUDOUuIPAHQXO3NxLpShaeljiVJ77Lv2LmTcsvMXYjwI4ECQDMjzvDC
aXMK51XpiYy8LQSxqPuz3EcX6LoszPxLdiWViZZKjpBOW1puKbhBWw5RuxJbPxIP
17lRZNH6NSukS5E=
-----END PRIVATE KEY-----
";

/// An inert Ed25519 key generated for these tests: a PKCS#8 key of another algorithm.
const ED25519: &str = "-----BEGIN PRIVATE KEY-----
MC4CAQAwBQYDK2VwBCIEIDfN5eADX0mBEmwPve+R63fiKehVMU1qN6yfz54UVGi1
-----END PRIVATE KEY-----
";

/// Encodes one DER element: the tag, a minimal length and the content.
fn der(tag: u8, content: &[u8]) -> Vec<u8> {
    let mut out = vec![tag];
    if content.len() < 0x80 {
        out.push(u8::try_from(content.len()).unwrap());
    } else if content.len() < 0x100 {
        out.extend([0x81, u8::try_from(content.len()).unwrap()]);
    } else {
        out.push(0x82);
        out.extend(u16::try_from(content.len()).unwrap().to_be_bytes());
    }
    out.extend(content);
    out
}

/// A PKCS#8 RSA key whose modulus is `modulus` (a DER INTEGER's content).
fn pkcs8(modulus: &[u8]) -> Vec<u8> {
    let rsa = der(
        0x30,
        &[der(0x02, &[0]), der(0x02, modulus), der(0x02, &[1, 0, 1])].concat(),
    );
    let algorithm = der(
        0x30,
        &[
            der(
                0x06,
                &[0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01],
            ),
            der(0x05, &[]),
        ]
        .concat(),
    );
    der(
        0x30,
        &[der(0x02, &[0]), algorithm, der(0x04, &rsa)].concat(),
    )
}

/// The content of a positive INTEGER of exactly `bits` bits.
fn modulus_of(bits: usize) -> Vec<u8> {
    let mut modulus = vec![0x00, 0xC0];
    modulus.extend(vec![0x11; bits / 8 - 1]);
    modulus
}

/// A PEM document with 64-character lines.
fn pem_of(der: &[u8]) -> String {
    let encoded = STANDARD.encode(der);
    let lines: Vec<&str> = encoded
        .as_bytes()
        .chunks(64)
        .map(|chunk| std::str::from_utf8(chunk).unwrap())
        .collect();
    format!(
        "-----BEGIN PRIVATE KEY-----\n{}\n-----END PRIVATE KEY-----\n",
        lines.join("\n")
    )
}

#[test]
fn a_real_2048_bit_rsa_key_is_accepted() {
    let key = DkimPrivateKey::parse(RSA_2048).unwrap();

    assert_eq!(key.expose(), RSA_2048);
}

#[test]
fn the_hand_built_fixture_is_accepted_at_2048_bits() {
    // Guards the builder every size test below relies on.
    assert!(DkimPrivateKey::parse(&pem_of(&pkcs8(&modulus_of(2048)))).is_ok());
}

#[test]
fn the_size_bounds_hold_at_both_ends() {
    assert!(DkimPrivateKey::parse(&pem_of(&pkcs8(&modulus_of(4096)))).is_ok());
    assert_eq!(
        DkimPrivateKey::parse(&pem_of(&pkcs8(&modulus_of(1024)))),
        Err(DkimPrivateKeyError::KeySizeRefused {
            bits: 1024,
            minimum: 2048,
            maximum: 4096
        })
    );
    assert_eq!(
        DkimPrivateKey::parse(&pem_of(&pkcs8(&modulus_of(8192)))),
        Err(DkimPrivateKeyError::KeySizeRefused {
            bits: 8192,
            minimum: 2048,
            maximum: 4096
        })
    );
}

#[test]
fn a_real_1024_bit_key_is_refused_as_too_small() {
    assert_eq!(
        DkimPrivateKey::parse(RSA_1024),
        Err(DkimPrivateKeyError::KeySizeRefused {
            bits: 1024,
            minimum: 2048,
            maximum: 4096
        })
    );
}

#[test]
fn a_key_of_another_algorithm_is_refused() {
    assert_eq!(
        DkimPrivateKey::parse(ED25519),
        Err(DkimPrivateKeyError::NotAnRsaKey)
    );
}

#[test]
fn a_missing_trailing_line_feed_is_accepted_and_the_stored_form_has_one() {
    let key = DkimPrivateKey::parse(RSA_2048.trim_end()).unwrap();

    assert_eq!(key.expose(), RSA_2048);
}

#[test]
fn another_pem_label_or_no_document_at_all_is_refused() {
    let rsa_label = RSA_2048.replace("PRIVATE KEY", "RSA PRIVATE KEY");
    let public = RSA_2048.replace("PRIVATE KEY", "PUBLIC KEY");

    for document in [
        rsa_label.as_str(),
        public.as_str(),
        "",
        "not a key",
        "-----BEGIN PRIVATE KEY-----\n-----END PRIVATE KEY-----\n",
    ] {
        assert_eq!(
            DkimPrivateKey::parse(document),
            Err(DkimPrivateKeyError::NotPkcs8Pem),
            "{document:?}"
        );
    }
}

#[test]
fn text_before_or_after_the_armour_is_refused() {
    assert_eq!(
        DkimPrivateKey::parse(&format!("junk\n{RSA_2048}")),
        Err(DkimPrivateKeyError::NotPkcs8Pem)
    );
    assert_eq!(
        DkimPrivateKey::parse(&format!("{RSA_2048}junk")),
        Err(DkimPrivateKeyError::NotPkcs8Pem)
    );
}

#[test]
fn an_empty_or_over_long_body_line_is_refused() {
    let blank_line = RSA_2048.replacen('\n', "\n\n", 2);
    let document = pem_of(&pkcs8(&modulus_of(2048)));
    let (banner, body) = document.split_once('\n').unwrap();
    let long_line = format!("{banner}\n{}\n{body}", "A".repeat(80));

    assert_eq!(
        DkimPrivateKey::parse(&blank_line),
        Err(DkimPrivateKeyError::NotPkcs8Pem)
    );
    assert_eq!(
        DkimPrivateKey::parse(&long_line),
        Err(DkimPrivateKeyError::NotPkcs8Pem)
    );
}

#[test]
fn a_control_character_is_refused_before_anything_else() {
    for control in ['\r', '\t', '\0', '\u{1b}'] {
        let tampered = RSA_2048.replacen("MII", &format!("MI{control}I"), 1);

        assert_eq!(
            DkimPrivateKey::parse(&tampered),
            Err(DkimPrivateKeyError::ControlCharacter),
            "{control:?}"
        );
    }
}

#[test]
fn a_body_that_is_not_canonical_base64_is_refused() {
    let tampered = RSA_2048.replacen("MII", "MI!", 1);

    assert_eq!(
        DkimPrivateKey::parse(&tampered),
        Err(DkimPrivateKeyError::InvalidEncoding)
    );
}

#[test]
fn a_document_beyond_the_ceiling_is_refused() {
    let big = format!("{RSA_2048}{}", "A".repeat(MAXIMUM_LENGTH));

    assert_eq!(
        DkimPrivateKey::parse(&big),
        Err(DkimPrivateKeyError::TooLong {
            maximum: MAXIMUM_LENGTH
        })
    );
}

#[test]
fn a_structure_that_is_not_a_pkcs8_rsa_key_is_malformed() {
    let valid = pkcs8(&modulus_of(2048));
    let mut trailing = valid.clone();
    trailing.push(0);

    for bad in [
        // Not a SEQUENCE at the top.
        der(0x04, &valid),
        // Bytes after the outer SEQUENCE.
        trailing,
        // An empty SEQUENCE.
        der(0x30, &[]),
        // A SEQUENCE of a version and nothing else.
        der(0x30, &der(0x02, &[0])),
        // A non-zero version.
        der(
            0x30,
            &[der(0x02, &[1]), der(0x30, &[]), der(0x04, &[])].concat(),
        ),
        // A modulus with a redundant leading zero.
        pkcs8(&[0x00, 0x00, 0xC0, 0x11]),
        // A negative modulus.
        pkcs8(&[0xC0, 0x11]),
        // An empty modulus.
        pkcs8(&[]),
    ] {
        assert_eq!(
            DkimPrivateKey::parse(&pem_of(&bad)),
            Err(DkimPrivateKeyError::Malformed),
            "{bad:02x?}"
        );
    }
}

#[test]
fn a_length_that_overruns_the_input_is_malformed_and_does_not_panic() {
    // A SEQUENCE claiming 65535 bytes of content that are not there.
    let hostile = [0x30, 0x82, 0xFF, 0xFF, 0x02, 0x01, 0x00];

    assert_eq!(
        DkimPrivateKey::parse(&pem_of(&hostile)),
        Err(DkimPrivateKeyError::Malformed)
    );
}

#[test]
fn the_key_never_appears_in_debug_output() {
    let key = DkimPrivateKey::parse(RSA_2048).unwrap();

    assert!(!format!("{key:?}").contains("MII"));
    assert!(format!("{key:?}").contains("redacted"));
}

/// The encoded `rsaEncryption` object identifier, as an element.
fn rsa_oid() -> Vec<u8> {
    der(
        0x06,
        &[0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01],
    )
}

/// The inner `RSAPrivateKey` SEQUENCE content for a 2048-bit modulus.
fn rsa_content() -> Vec<u8> {
    [
        der(0x02, &[0]),
        der(0x02, &modulus_of(2048)),
        der(0x02, &[1, 0, 1]),
    ]
    .concat()
}

/// A PKCS#8 wrapper around the given algorithm content and `OCTET STRING` content.
fn wrapped(algorithm: &[u8], key: &[u8]) -> Vec<u8> {
    der(
        0x30,
        &[der(0x02, &[0]), der(0x30, algorithm), der(0x04, key)].concat(),
    )
}

/// A SEQUENCE whose three-byte length starts with a zero byte, which DER forbids.
fn padded_length_sequence(content: &[u8]) -> Vec<u8> {
    let length = u16::try_from(content.len()).unwrap().to_be_bytes();
    let mut out = vec![0x30, 0x83, 0x00, length[0], length[1]];
    out.extend(content);
    out
}

#[test]
fn bytes_left_over_at_any_inner_level_are_malformed() {
    let rsa = der(0x30, &rsa_content());
    let null = der(0x05, &[]);
    let good_algorithm = [rsa_oid(), null.clone()].concat();

    // The wrapper is only the control: it must accept the unmodified parts.
    assert!(DkimPrivateKey::parse(&pem_of(&wrapped(&good_algorithm, &rsa))).is_ok());

    for bad in [
        // A byte after the algorithm's NULL.
        wrapped(&[good_algorithm.clone(), vec![0]].concat(), &rsa),
        // A second element after the algorithm's NULL.
        wrapped(&[good_algorithm.clone(), null.clone()].concat(), &rsa),
        // A NULL carrying content.
        wrapped(&[rsa_oid(), der(0x05, &[0])].concat(), &rsa),
        // No NULL at all.
        wrapped(&rsa_oid(), &rsa),
        // A byte after the RSAPrivateKey inside the OCTET STRING.
        wrapped(&good_algorithm, &[rsa.clone(), vec![0]].concat()),
        // A byte after the OCTET STRING inside the outer SEQUENCE.
        der(
            0x30,
            &[
                der(0x02, &[0]),
                der(0x30, &good_algorithm),
                der(0x04, &rsa),
                vec![0],
            ]
            .concat(),
        ),
        // A length with a leading zero byte on the inner SEQUENCE.
        wrapped(&good_algorithm, &padded_length_sequence(&rsa_content())),
    ] {
        assert_eq!(
            DkimPrivateKey::parse(&pem_of(&bad)),
            Err(DkimPrivateKeyError::Malformed),
            "{bad:02x?}"
        );
    }
}
