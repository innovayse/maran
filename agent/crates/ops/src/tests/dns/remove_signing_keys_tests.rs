//! Taking a zone's signing keys off the host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::fake_zone_host::{FakeZoneHost, key_directory};
use super::remove_signing_keys;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

fn key(name: &str) -> std::path::PathBuf {
    key_directory().join(name)
}

/// A host holding the key files of `example.com` at tag 11111 and of
/// `other.example` at tag 22222.
fn host() -> FakeZoneHost {
    let mut host = FakeZoneHost::new();
    for name in [
        "Kexample.com.+013+11111.key",
        "Kexample.com.+013+11111.private",
        "Kexample.com.+013+11111.state",
        "Kother.example.+013+22222.key",
        "Kother.example.+013+22222.private",
    ] {
        host = host.with_file(&key(name), "content\n");
    }
    host
}

#[test]
fn every_key_file_of_the_zone_is_removed_and_no_other_zones() {
    let host = host();

    remove_signing_keys(&host, &key_directory(), &zone(), None).unwrap();

    for name in [
        "Kexample.com.+013+11111.key",
        "Kexample.com.+013+11111.private",
        "Kexample.com.+013+11111.state",
    ] {
        assert!(host.file(&key(name)).is_none(), "{name} survived");
    }
    assert!(host.file(&key("Kother.example.+013+22222.key")).is_some());
    assert!(
        host.file(&key("Kother.example.+013+22222.private"))
            .is_some()
    );
}

#[test]
fn the_key_to_keep_stays_and_a_replaced_one_goes() {
    let host = host().with_file(&key("Kexample.com.+013+33333.private"), "new\n");

    remove_signing_keys(&host, &key_directory(), &zone(), Some(33333)).unwrap();

    assert!(host.file(&key("Kexample.com.+013+33333.private")).is_some());
    assert!(host.file(&key("Kexample.com.+013+11111.private")).is_none());
}

#[test]
fn a_zone_whose_name_ends_in_this_ones_is_not_touched() {
    let host = FakeZoneHost::new().with_file(&key("Kwww.example.com.+013+44444.private"), "x\n");

    remove_signing_keys(&host, &key_directory(), &zone(), None).unwrap();

    assert!(
        host.file(&key("Kwww.example.com.+013+44444.private"))
            .is_some()
    );
}

#[test]
fn a_stray_file_is_left_alone() {
    let host = FakeZoneHost::new().with_file(&key("README"), "x\n");

    remove_signing_keys(&host, &key_directory(), &zone(), None).unwrap();

    assert!(host.file(&key("README")).is_some());
}

#[test]
fn removing_from_an_empty_directory_is_success() {
    let host = FakeZoneHost::new();

    assert!(remove_signing_keys(&host, &key_directory(), &zone(), None).is_ok());
}
