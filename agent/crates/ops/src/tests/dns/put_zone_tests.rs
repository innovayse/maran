//! Tests for `put_zone`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use super::super::dns_error::DnsError;
use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, sample_content, zone_directory};
use super::put_zone;

fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

#[test]
fn a_new_zone_is_written_checked_added_and_read_back() {
    let host = FakeZoneHost::new();
    let content = sample_content("example.com", 2_026_110_701);

    let outcome = put_zone(&host, distro(), &zone_directory(), &floor(), &content).unwrap();

    assert!(outcome.created);
    assert_eq!(outcome.serial, 2_026_110_701);
    let written = host.file(&zone_directory().join("example.com.db")).unwrap();
    assert!(written.contains("$ORIGIN example.com."));
    assert!(written.contains("www 300 IN A 192.0.2.10"));
    let commands = host.rndc_commands();
    let add = commands.iter().find(|c| c[0] == "addzone").unwrap();
    assert_eq!(add[1], "example.com");
    assert_eq!(add[2], "{ type primary; file \"/zones/example.com.db\"; };");
}

#[test]
fn the_zone_checker_reads_the_zone_at_its_real_path() {
    let host = FakeZoneHost::new();

    put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 5),
    )
    .unwrap();

    let check = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.get(1).is_some_and(|first| first == "-q"))
        .unwrap();
    assert_eq!(&check[1..], ["-q", "example.com", "/zones/example.com.db"]);
}

#[test]
fn a_zone_the_daemon_already_serves_is_reloaded_and_not_added() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "example.com", 1, "; old\n");

    let outcome = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 7),
    )
    .unwrap();

    assert!(!outcome.created);
    assert_eq!(outcome.serial, 7);
    let verbs: Vec<String> = host
        .rndc_commands()
        .into_iter()
        .map(|c| c[0].clone())
        .collect();
    assert!(verbs.contains(&"reload".to_owned()));
    assert!(!verbs.contains(&"addzone".to_owned()));
}

#[test]
fn putting_the_same_zone_twice_converges() {
    let host = FakeZoneHost::new();
    let content = sample_content("example.com", 9);

    let first = put_zone(&host, distro(), &zone_directory(), &floor(), &content).unwrap();
    let second = put_zone(&host, distro(), &zone_directory(), &floor(), &content).unwrap();

    assert!(first.created);
    assert!(!second.created);
    assert_eq!(second.serial, 9);
}

#[test]
fn a_zone_the_checker_refuses_is_never_loaded_and_the_old_file_is_kept() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "example.com", 1, "; old\n")
        .with_checker_refusing("dns_rdata_fromtext: near 'x': bad dotted quad");

    let refused = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert!(matches!(refused, Err(DnsError::ZoneRejected { .. })));
    assert_eq!(
        host.file(&zone_directory().join("example.com.db")).unwrap(),
        "; old\n"
    );
    assert_eq!(host.served_serial("example.com"), Some(1));
}

#[test]
fn a_new_zone_the_checker_refuses_leaves_no_file() {
    let host = FakeZoneHost::new().with_checker_refusing("refused");

    let refused = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert!(matches!(refused, Err(DnsError::ZoneRejected { .. })));
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none()
    );
    assert!(host.served_serial("example.com").is_none());
}

#[test]
fn a_daemon_that_refuses_the_zone_leaves_no_file_behind() {
    let host = FakeZoneHost::new().with_load_refused();

    let refused = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert!(matches!(refused, Err(DnsError::ControlRefused { .. })));
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none()
    );
}

#[test]
fn a_daemon_that_does_not_answer_is_refused_before_a_file_is_touched() {
    let host = FakeZoneHost::new().with_daemon_down();

    let refused = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert!(matches!(refused, Err(DnsError::ControlRefused { .. })));
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none()
    );
}

#[test]
fn a_filesystem_below_the_floor_refuses_before_anything_is_written() {
    let host = FakeZoneHost::new().with_available_bytes(1024);

    let refused = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert!(matches!(
        refused,
        Err(DnsError::ZoneBelowFloor {
            available: 1024,
            ..
        })
    ));
    assert!(host.spawns().is_empty());
}

#[test]
fn a_filesystem_that_cannot_be_measured_is_not_a_roomy_one() {
    let host = FakeZoneHost::new().with_unmeasurable_filesystem();

    let refused = put_zone(
        &host,
        distro(),
        &zone_directory(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert_eq!(refused, Err(DnsError::ZoneUnmeasurable));
    assert!(host.spawns().is_empty());
}
