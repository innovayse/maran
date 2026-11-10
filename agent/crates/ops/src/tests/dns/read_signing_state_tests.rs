//! Observing how the daemon signs one zone.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, key_directory, signed_directory, zone_directory};
use super::read_signing_state;
use crate::dns::model::key_publication::KeyPublication;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_signing_observation::ZoneSigningObservation;
use crate::dns::zone_statement_for::zone_statement_for;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

fn serving(mode: SigningMode) -> FakeZoneHost {
    let directory = if mode == SigningMode::Unsigned {
        zone_directory()
    } else {
        signed_directory()
    };
    let statement =
        zone_statement_for(&directory.join("example.com.db"), &key_directory(), mode).unwrap();
    FakeZoneHost::new().with_zone_statement(&directory, "example.com", 1, &statement)
}

#[test]
fn a_signed_zone_with_a_finished_signing_reports_the_mode_and_the_key_tag() {
    let host = serving(SigningMode::Signed).with_signing_list(
        "example.com",
        "Done signing with key 59409/ECDSAP256SHA256\n",
    );

    assert_eq!(
        read_signing_state(&host, distro(), &zone()),
        ZoneSigningObservation {
            mode: Some(SigningMode::Signed),
            key: KeyPublication::Published(59_409),
        }
    );
}

#[test]
fn a_signed_zone_with_no_finished_signing_is_not_published() {
    let host = serving(SigningMode::Signed);

    assert_eq!(
        read_signing_state(&host, distro(), &zone()),
        ZoneSigningObservation {
            mode: Some(SigningMode::Signed),
            key: KeyPublication::NotPublished,
        }
    );
}

#[test]
fn a_withdrawing_zone_is_asked_for_its_key_too() {
    let host = serving(SigningMode::Withdrawing)
        .with_signing_list("example.com", "Done signing with key 7/ECDSAP256SHA256\n");

    let observed = read_signing_state(&host, distro(), &zone());

    assert_eq!(observed.mode, Some(SigningMode::Withdrawing));
    assert_eq!(observed.key, KeyPublication::Published(7));
}

#[test]
fn an_unsigned_zone_is_not_asked_for_a_key_and_has_none_published() {
    let host = serving(SigningMode::Unsigned);

    let observed = read_signing_state(&host, distro(), &zone());

    assert_eq!(observed.mode, Some(SigningMode::Unsigned));
    assert_eq!(observed.key, KeyPublication::NotPublished);
    assert!(
        host.rndc_commands()
            .iter()
            .all(|command| command[0] != "signing"),
        "the signing list was asked about an unsigned zone"
    );
}

#[test]
fn a_mode_that_cannot_be_read_leaves_everything_not_observed_and_asks_nothing_more() {
    let host = serving(SigningMode::Signed).with_showzone_refused();

    assert_eq!(
        read_signing_state(&host, distro(), &zone()),
        ZoneSigningObservation::NOT_OBSERVED
    );
    assert!(host.rndc_commands().iter().all(|c| c[0] != "signing"));
}

#[test]
fn a_signing_list_that_is_not_understood_leaves_the_key_not_observed_but_keeps_the_mode() {
    let host = serving(SigningMode::Signed).with_signing_list("example.com", "garbage\n");

    let observed = read_signing_state(&host, distro(), &zone());

    assert_eq!(observed.mode, Some(SigningMode::Signed));
    assert_eq!(observed.key, KeyPublication::NotObserved);
}

#[test]
fn the_signing_question_is_signing_list_with_the_zone() {
    let host = serving(SigningMode::Signed);
    let _ = read_signing_state(&host, distro(), &zone());

    assert!(host.rndc_commands().contains(&vec![
        "signing".to_owned(),
        "-list".to_owned(),
        "example.com".to_owned()
    ]));
}
