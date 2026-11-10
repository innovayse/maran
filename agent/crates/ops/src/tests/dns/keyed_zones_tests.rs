//! Which zones have a complete signing key on the host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::fake_zone_host::{FakeZoneHost, key_directory};
use super::keyed_zones;

fn key(name: &str) -> std::path::PathBuf {
    key_directory().join(name)
}

#[test]
fn a_zone_with_both_files_of_one_key_is_keyed() {
    let host = FakeZoneHost::new()
        .with_file(&key("Kexample.com.+013+11111.key"), "x\n")
        .with_file(&key("Kexample.com.+013+11111.private"), "x\n");

    let keyed = keyed_zones(&host, &key_directory()).unwrap();

    assert!(keyed.contains("example.com"));
}

#[test]
fn a_zone_with_only_the_private_file_is_not_keyed() {
    let host = FakeZoneHost::new().with_file(&key("Kexample.com.+013+11111.private"), "x\n");

    assert!(keyed_zones(&host, &key_directory()).unwrap().is_empty());
}

#[test]
fn a_zone_with_only_the_public_file_is_not_keyed() {
    let host = FakeZoneHost::new().with_file(&key("Kexample.com.+013+11111.key"), "x\n");

    assert!(keyed_zones(&host, &key_directory()).unwrap().is_empty());
}

#[test]
fn the_two_files_must_be_of_the_same_key() {
    let host = FakeZoneHost::new()
        .with_file(&key("Kexample.com.+013+11111.key"), "x\n")
        .with_file(&key("Kexample.com.+013+22222.private"), "x\n");

    assert!(keyed_zones(&host, &key_directory()).unwrap().is_empty());
}

#[test]
fn a_state_file_alone_does_not_make_a_key() {
    let host = FakeZoneHost::new().with_file(&key("Kexample.com.+013+11111.state"), "x\n");

    assert!(keyed_zones(&host, &key_directory()).unwrap().is_empty());
}

#[test]
fn a_stray_file_is_not_a_key() {
    let host = FakeZoneHost::new().with_file(&key("README.key"), "x\n");

    assert!(keyed_zones(&host, &key_directory()).unwrap().is_empty());
}
