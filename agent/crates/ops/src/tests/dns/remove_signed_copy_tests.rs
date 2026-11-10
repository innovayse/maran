//! Taking a zone out of the signed-zone directory.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::fake_zone_host::{FakeZoneHost, signed_directory};
use super::remove_signed_copy;

fn file() -> ZoneFileName {
    ZoneFileName::of(&ZoneName::parse("example.com").unwrap())
}

#[test]
fn the_zone_file_and_the_files_the_daemon_keeps_beside_it_are_removed() {
    let mut host = FakeZoneHost::new();
    for suffix in ["", ".jnl", ".signed", ".signed.jnl"] {
        host = host.with_file(
            &signed_directory().join(format!("example.com.db{suffix}")),
            "x\n",
        );
    }

    remove_signed_copy(&host, &signed_directory(), &file()).unwrap();

    assert!(host.file_paths().is_empty(), "{:?}", host.file_paths());
}

#[test]
fn another_zones_files_are_not_removed() {
    let other = signed_directory().join("other.example.db");
    let host = FakeZoneHost::new().with_file(&other, "x\n");

    remove_signed_copy(&host, &signed_directory(), &file()).unwrap();

    assert!(host.file(&other).is_some());
}

#[test]
fn removing_what_is_not_there_is_success() {
    let host = FakeZoneHost::new();

    assert!(remove_signed_copy(&host, &signed_directory(), &file()).is_ok());
}
