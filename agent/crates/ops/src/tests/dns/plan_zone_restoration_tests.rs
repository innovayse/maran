//! Deciding, from the files on the host, how each zone is served again.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::fake_zone_host::{
    FakeZoneHost, directories, key_directory, signed_directory, zone_directory,
};
use super::plan_zone_restoration;
use crate::dns::model::zone_placement::ZonePlacement;

fn plan(host: &FakeZoneHost) -> Vec<(String, ZonePlacement)> {
    plan_zone_restoration(host, &directories())
        .unwrap()
        .into_iter()
        .map(|(file, placement)| (file.zone().as_str().to_owned(), placement))
        .collect()
}

fn with_key(host: FakeZoneHost, zone: &str) -> FakeZoneHost {
    host.with_file(
        &key_directory().join(format!("K{zone}.+013+11111.key")),
        "x\n",
    )
    .with_file(
        &key_directory().join(format!("K{zone}.+013+11111.private")),
        "x\n",
    )
}

#[test]
fn a_plain_file_is_unsigned() {
    let host = FakeZoneHost::new().with_unserved_file(&zone_directory(), "a.example");

    assert_eq!(
        plan(&host),
        vec![("a.example".to_owned(), ZonePlacement::Unsigned)]
    );
}

#[test]
fn a_signed_file_with_a_complete_key_is_signed() {
    let host = with_key(
        FakeZoneHost::new().with_unserved_file(&signed_directory(), "a.example"),
        "a.example",
    );

    assert_eq!(
        plan(&host),
        vec![("a.example".to_owned(), ZonePlacement::Signed)]
    );
}

#[test]
fn a_signed_file_without_a_key_is_not_to_be_served() {
    // The planted failure: serving it would have the daemon invent a key.
    let host = FakeZoneHost::new().with_unserved_file(&signed_directory(), "a.example");

    assert_eq!(
        plan(&host),
        vec![("a.example".to_owned(), ZonePlacement::KeyMissing)]
    );
}

#[test]
fn a_zone_in_both_directories_follows_its_key() {
    let both = |host: FakeZoneHost| {
        host.with_unserved_file(&zone_directory(), "a.example")
            .with_unserved_file(&signed_directory(), "a.example")
    };

    assert_eq!(
        plan(&with_key(both(FakeZoneHost::new()), "a.example")),
        vec![("a.example".to_owned(), ZonePlacement::Signed)]
    );
    assert_eq!(
        plan(&both(FakeZoneHost::new())),
        vec![("a.example".to_owned(), ZonePlacement::Unsigned)]
    );
}

#[test]
fn the_daemons_files_beside_a_signed_zone_are_not_zones() {
    let host = with_key(
        FakeZoneHost::new()
            .with_unserved_file(&signed_directory(), "a.example")
            .with_file(&signed_directory().join("a.example.db.signed"), "x\n")
            .with_file(&signed_directory().join("a.example.db.jnl"), "x\n"),
        "a.example",
    );

    assert_eq!(plan(&host).len(), 1);
}

#[test]
fn the_plan_is_sorted_by_zone_name() {
    let host = FakeZoneHost::new()
        .with_unserved_file(&zone_directory(), "zeta.example")
        .with_unserved_file(&zone_directory(), "alpha.example");

    let names: Vec<String> = plan(&host).into_iter().map(|(zone, _)| zone).collect();

    assert_eq!(names, ["alpha.example", "zeta.example"]);
}
