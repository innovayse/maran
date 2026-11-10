//! Installing a zone's signing key where the daemon looks for it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{
    FakeZoneHost, PLANTED_PRIVATE_KEY, SAMPLE_KEY_TAG, key_directory, sample_key,
};
use super::write_signing_keys;
use crate::dns::dns_error::DnsError;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

fn private_path() -> std::path::PathBuf {
    key_directory().join(format!("Kexample.com.+013+{SAMPLE_KEY_TAG:05}.private"))
}

fn public_path() -> std::path::PathBuf {
    key_directory().join(format!("Kexample.com.+013+{SAMPLE_KEY_TAG:05}.key"))
}

#[test]
fn both_files_are_written_under_the_names_built_from_the_recomputed_tag() {
    let host = FakeZoneHost::new();

    write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap();

    assert!(host.file(&private_path()).is_some());
    assert!(host.file(&public_path()).is_some());
    assert_eq!(host.file_paths().len(), 2);
}

#[test]
fn the_private_file_is_in_the_daemons_key_format_and_holds_the_planted_scalar() {
    let host = FakeZoneHost::new();
    write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap();

    let private = host.file(&private_path()).unwrap();

    assert!(private.starts_with("Private-key-format: v1.3\n"));
    assert!(private.contains("Algorithm: 13 (ECDSAP256SHA256)\n"));
    assert!(private.contains(&format!("PrivateKey: {PLANTED_PRIVATE_KEY}\n")));
    for timing in ["Created: ", "Publish: ", "Activate: "] {
        assert!(private.contains(timing), "{timing} missing");
    }
}

#[test]
fn the_public_file_holds_the_dnskey_record_and_never_the_private_scalar() {
    let host = FakeZoneHost::new();
    write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap();

    let public = host.file(&public_path()).unwrap();

    assert!(public.contains(&format!("keyid {SAMPLE_KEY_TAG}, for example.com.")));
    assert!(public.contains("example.com. IN DNSKEY 257 3 13 AAECAwQFBgcI"));
    assert!(!public.contains(PLANTED_PRIVATE_KEY));
}

#[test]
fn both_files_are_handed_to_the_daemons_group() {
    let host = FakeZoneHost::new();
    write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap();

    assert_eq!(host.key_group(&private_path()).as_deref(), Some("bind"));
    assert_eq!(host.key_group(&public_path()).as_deref(), Some("bind"));
}

#[test]
fn writing_the_same_key_twice_converges_on_the_same_two_files() {
    let host = FakeZoneHost::new();
    write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap();
    let first = host.file(&private_path());

    write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap();

    assert_eq!(host.file(&private_path()), first);
    assert_eq!(host.file_paths().len(), 2);
}

#[test]
fn a_key_that_cannot_be_written_is_an_error_without_a_byte_of_the_key() {
    let host = FakeZoneHost::new().with_key_write_refused();

    let refused =
        write_signing_keys(&host, distro(), &key_directory(), &zone(), &sample_key()).unwrap_err();

    assert!(matches!(refused, DnsError::ConfigWrite { .. }));
    assert!(!refused.to_string().contains(PLANTED_PRIVATE_KEY));
    assert!(!format!("{refused:?}").contains(PLANTED_PRIVATE_KEY));
    assert!(host.file_paths().is_empty());
}
