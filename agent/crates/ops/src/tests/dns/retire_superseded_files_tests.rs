//! Removing what a zone's new signing mode leaves behind.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_file_name::ZoneFileName;
use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::fake_zone_host::{
    FakeZoneHost, SAMPLE_KEY_TAG, directories, key_directory, sample_key, signed_directory,
    zone_directory,
};
use super::retire_superseded_files;
use crate::dns::model::zone_signing::ZoneSigning;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

fn host() -> FakeZoneHost {
    FakeZoneHost::new()
        .with_file(&zone_directory().join("example.com.db"), "plain\n")
        .with_file(&signed_directory().join("example.com.db"), "signed\n")
        .with_file(
            &signed_directory().join("example.com.db.signed"),
            "daemon\n",
        )
        .with_file(
            &key_directory().join(format!("Kexample.com.+013+{SAMPLE_KEY_TAG:05}.private")),
            "key\n",
        )
        .with_file(
            &key_directory().join("Kexample.com.+013+00007.private"),
            "old\n",
        )
}

#[test]
fn unsigned_removes_every_key_and_the_signed_copy_and_keeps_the_plain_file() {
    let host = host();

    retire_superseded_files(
        &host,
        &directories(),
        &zone(),
        &ZoneFileName::of(&zone()),
        &ZoneSigning::Unsigned,
    )
    .unwrap();

    assert_eq!(
        host.file_paths(),
        vec![zone_directory().join("example.com.db")]
    );
}

#[test]
fn signed_removes_the_plain_copy_and_a_replaced_key_and_keeps_the_signed_files() {
    let host = host();

    retire_superseded_files(
        &host,
        &directories(),
        &zone(),
        &ZoneFileName::of(&zone()),
        &ZoneSigning::Signed(sample_key()),
    )
    .unwrap();

    let remaining = host.file_paths();
    assert!(!remaining.contains(&zone_directory().join("example.com.db")));
    assert!(!remaining.contains(&key_directory().join("Kexample.com.+013+00007.private")));
    assert!(remaining.contains(&signed_directory().join("example.com.db")));
    assert!(remaining.contains(&signed_directory().join("example.com.db.signed")));
    assert!(
        remaining.contains(
            &key_directory().join(format!("Kexample.com.+013+{SAMPLE_KEY_TAG:05}.private"))
        )
    );
}

#[test]
fn withdrawing_is_retired_like_signed() {
    let host = host();

    retire_superseded_files(
        &host,
        &directories(),
        &zone(),
        &ZoneFileName::of(&zone()),
        &ZoneSigning::Withdrawing(sample_key()),
    )
    .unwrap();

    assert!(
        host.file(&key_directory().join(format!("Kexample.com.+013+{SAMPLE_KEY_TAG:05}.private")))
            .is_some()
    );
}
