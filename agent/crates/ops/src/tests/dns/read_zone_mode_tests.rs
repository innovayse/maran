//! Asking the daemon how it signs one zone.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, key_directory, signed_directory, zone_directory};
use super::read_zone_mode;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::zone_statement_for::zone_statement_for;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

fn statement(mode: SigningMode) -> String {
    let directory = if mode == SigningMode::Unsigned {
        zone_directory()
    } else {
        signed_directory()
    };
    zone_statement_for(&directory.join("example.com.db"), &key_directory(), mode).unwrap()
}

#[test]
fn each_mode_the_daemon_holds_is_read_back() {
    for mode in [
        SigningMode::Unsigned,
        SigningMode::Signed,
        SigningMode::Withdrawing,
    ] {
        let host = FakeZoneHost::new().with_zone_statement(
            &zone_directory(),
            "example.com",
            1,
            &statement(mode),
        );

        assert_eq!(read_zone_mode(&host, distro(), &zone()), Some(mode));
    }
}

#[test]
fn a_zone_the_daemon_does_not_know_is_not_observed() {
    let host = FakeZoneHost::new();

    assert_eq!(read_zone_mode(&host, distro(), &zone()), None);
}

#[test]
fn a_refused_question_is_not_observed_and_never_unsigned() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "example.com", 1, "; x\n")
        .with_showzone_refused();

    assert_eq!(read_zone_mode(&host, distro(), &zone()), None);
}

#[test]
fn a_daemon_that_does_not_answer_is_not_observed() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "example.com", 1, "; x\n")
        .with_daemon_down();

    assert_eq!(read_zone_mode(&host, distro(), &zone()), None);
}

#[test]
fn the_question_is_showzone_with_the_zone_and_nothing_else() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "example.com", 1, "; x\n");
    let _ = read_zone_mode(&host, distro(), &zone());

    assert_eq!(host.rndc_commands(), vec![vec!["showzone", "example.com"]]);
}
