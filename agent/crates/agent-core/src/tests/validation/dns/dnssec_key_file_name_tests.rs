//! Tests for the `dnssec_key_file_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::dnssec_key_file_name::DnssecKeyFileName;
use super::super::dnssec_key_file_name_error::DnssecKeyFileNameError;
use super::super::dnssec_key_material::DnssecKeyMaterial;
use super::super::zone_name::ZoneName;
use crate::secret_string::SecretString;

/// Base64 of the 64 bytes `0, 1, ... 63`; its tag is 59409 (see the key material tests).
const PUBLIC: &str =
    "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==";

/// Base64 of 32 bytes.
const PRIVATE: &str = "TUFSQU4tUExBTlRFRC1TQ0FMQVItMDEyMzQ1Njc4OSE=";

/// The key whose tag is 59409.
fn key() -> DnssecKeyMaterial {
    DnssecKeyMaterial::parse(PUBLIC, &SecretString::new(PRIVATE.to_owned()), 257, 59_409).unwrap()
}

/// The zone `example.com`.
fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

/// The name is `K<zone>.+013+<tag>` with the tag from the key, not from a caller.
#[test]
fn the_base_name_uses_the_zone_and_the_computed_tag() {
    let name = DnssecKeyFileName::of(&zone(), &key()).unwrap();

    assert_eq!(name.as_str(), "Kexample.com.+013+59409");
    assert_eq!(name.public_file(), "Kexample.com.+013+59409.key");
    assert_eq!(name.private_file(), "Kexample.com.+013+59409.private");
    assert_eq!(name.state_file(), "Kexample.com.+013+59409.state");
    assert_eq!(name.key_tag(), 59_409);
    assert_eq!(name.zone(), &zone());
}

/// A tag shorter than five digits is zero-padded, as the daemon's tools write it.
#[test]
fn a_short_tag_is_padded_to_five_digits() {
    // The all-ones key has the tag 1038.
    let public =
        "/////////////////////////////////////////////////////////////////////////////////////w==";
    let short =
        DnssecKeyMaterial::parse(public, &SecretString::new(PRIVATE.to_owned()), 257, 1_038)
            .unwrap();

    assert_eq!(
        DnssecKeyFileName::of(&zone(), &short).unwrap().as_str(),
        "Kexample.com.+013+01038"
    );
}

/// A zone name of the given length: three 60-character labels and a last label
/// that makes up the rest.
fn zone_of_length(length: usize) -> ZoneName {
    let label = "a".repeat(60);
    let last = "b".repeat(length - 3 * 61);

    ZoneName::parse(&format!("{label}.{label}.{label}.{last}")).unwrap()
}

/// The longest file of a key is `K<zone>.+013+<tag>.private`: 20 bytes beyond the
/// zone, so 235 characters of zone fit a 255-byte file name and 236 do not.
#[test]
fn a_zone_name_too_long_for_a_key_file_is_refused_at_the_exact_boundary() {
    let fits = zone_of_length(235);
    let too_long = zone_of_length(236);

    assert_eq!(fits.as_str().len(), 235);
    let name = DnssecKeyFileName::of(&fits, &key()).unwrap();
    assert_eq!(name.private_file().len(), 255);
    assert_eq!(
        DnssecKeyFileName::of(&too_long, &key()),
        Err(DnssecKeyFileNameError::TooLong)
    );
}

/// What the forward direction writes, the reverse reads, for all three suffixes.
#[test]
fn a_written_name_is_read_back_for_every_suffix() {
    let written = DnssecKeyFileName::of(&zone(), &key()).unwrap();

    for file in [
        written.public_file(),
        written.private_file(),
        written.state_file(),
    ] {
        assert_eq!(DnssecKeyFileName::parse(&file).unwrap(), written);
    }
}

/// Anything else in the directory is not a key file.
#[test]
fn a_stray_name_is_not_a_key_file() {
    for stray in [
        "",
        "Kexample.com.+013+59409",
        "Kexample.com.+013+59409.bak",
        "Kexample.com.+013+9409.key",
        "Kexample.com.+013+059409.key",
        "Kexample.com.+013+5940x.key",
        "Kexample.com.+013+99999.key",
        "Kexample.com.+008+59409.key",
        "kexample.com.+013+59409.key",
        "KExample.COM.+013+59409.key",
        "Kcom.+013+59409.key",
        "K.+013+59409.key",
        "K../x.+013+59409.key",
        "Kexample.com.+013+59409.key\n",
        "example.com.db",
    ] {
        assert_eq!(
            DnssecKeyFileName::parse(stray),
            Err(DnssecKeyFileNameError::NotAKeyFile),
            "{stray:?} must not be read as a key file"
        );
    }
}
