//! Tests for `put_zone`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use super::super::dns_error::DnsError;
use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{
    FakeZoneHost, PLANTED_PRIVATE_KEY, SAMPLE_KEY_TAG, directories, key_directory, sample_content,
    sample_key, signed_directory, zone_directory,
};
use super::put_zone;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_content::ZoneContent;
use crate::dns::model::zone_signing::ZoneSigning;
use crate::dns::zone_statement_for::zone_statement_for;

fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

#[test]
fn a_new_zone_is_written_checked_added_and_read_back() {
    let host = FakeZoneHost::new();
    let content = sample_content("example.com", 2_026_110_701);

    let outcome = put_zone(&host, distro(), &directories(), &floor(), &content).unwrap();

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
        &directories(),
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
        &directories(),
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

    let first = put_zone(&host, distro(), &directories(), &floor(), &content).unwrap();
    let second = put_zone(&host, distro(), &directories(), &floor(), &content).unwrap();

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
        &directories(),
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
        &directories(),
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
        &directories(),
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
        &directories(),
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
        &directories(),
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
        &directories(),
        &floor(),
        &sample_content("example.com", 2),
    );

    assert_eq!(refused, Err(DnsError::ZoneUnmeasurable));
    assert!(host.spawns().is_empty());
}

/// The sample zone, signed.
fn signed_content(serial: u32) -> ZoneContent {
    sample_content("example.com", serial).with_signing(ZoneSigning::Signed(sample_key()))
}

/// The sample zone, withdrawing.
fn withdrawing_content(serial: u32) -> ZoneContent {
    sample_content("example.com", serial).with_signing(ZoneSigning::Withdrawing(sample_key()))
}

/// The statement the daemon holds for the sample zone in `mode`, as the agent
/// would have installed it.
fn installed_statement(mode: SigningMode) -> String {
    let directory = if mode == SigningMode::Unsigned {
        zone_directory()
    } else {
        signed_directory()
    };
    zone_statement_for(&directory.join("example.com.db"), &key_directory(), mode).unwrap()
}

/// The paths of the sample key's three files.
fn key_paths() -> [std::path::PathBuf; 3] {
    let base = format!("Kexample.com.+013+{SAMPLE_KEY_TAG:05}");
    [
        key_directory().join(format!("{base}.key")),
        key_directory().join(format!("{base}.private")),
        key_directory().join(format!("{base}.state")),
    ]
}

/// A host serving the sample zone as it is in `mode`, with its key files where
/// the agent and the daemon leave them.
fn host_serving(mode: SigningMode) -> FakeZoneHost {
    let directory = if mode == SigningMode::Unsigned {
        zone_directory()
    } else {
        signed_directory()
    };
    let mut host = FakeZoneHost::new().with_zone_statement(
        &directory,
        "example.com",
        1,
        &installed_statement(mode),
    );
    if mode != SigningMode::Unsigned {
        for path in key_paths() {
            host = host.with_file(&path, "key\n");
        }
    }
    host
}

/// The verbs of the `rndc` commands run, in order.
fn verbs(host: &FakeZoneHost) -> Vec<String> {
    host.rndc_commands()
        .into_iter()
        .map(|c| c[0].clone())
        .collect()
}

#[test]
fn a_new_signed_zone_is_written_to_the_signed_directory_and_added_with_the_signed_statement() {
    let host = FakeZoneHost::new();

    let outcome = put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap();

    assert!(outcome.created);
    assert!(
        host.file(&signed_directory().join("example.com.db"))
            .is_some()
    );
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none(),
        "a signed zone's file was also written to the plain directory"
    );
    let commands = host.rndc_commands();
    let add = commands.iter().find(|c| c[0] == "addzone").unwrap();
    assert_eq!(add[2], installed_statement(SigningMode::Signed));
    assert!(add[2].contains("dnssec-policy \"maran-csk\""));
}

#[test]
fn the_zone_checker_reads_a_signed_zone_at_its_path_in_the_signed_directory() {
    let host = FakeZoneHost::new();

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap();

    let check = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.get(1).is_some_and(|first| first == "-q"))
        .unwrap();
    assert_eq!(
        &check[1..],
        ["-q", "example.com", "/signed-zones/example.com.db"]
    );
}

#[test]
fn a_signed_zones_key_files_are_written_for_the_daemons_group_before_the_zone_is_loaded() {
    let host = FakeZoneHost::new();

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap();

    let [public, private, _] = key_paths();
    assert!(host.file(&public).unwrap().contains("IN DNSKEY 257 3 13 "));
    assert!(
        host.file(&private)
            .unwrap()
            .starts_with("Private-key-format: v1.3\nAlgorithm: 13 (ECDSAP256SHA256)\n")
    );
    assert_eq!(host.key_group(&private).as_deref(), Some("bind"));
    assert_eq!(host.key_group(&public).as_deref(), Some("bind"));
}

#[test]
fn the_planted_private_key_is_in_the_private_file_and_nowhere_else() {
    // The positive control comes first: the planted value IS findable where it
    // must be, so the searches below could have found it.
    let host = FakeZoneHost::new();
    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap();
    let [public, private, _] = key_paths();

    assert!(host.file(&private).unwrap().contains(PLANTED_PRIVATE_KEY));

    assert!(!host.file(&public).unwrap().contains(PLANTED_PRIVATE_KEY));
    let zone_file = host
        .file(&signed_directory().join("example.com.db"))
        .unwrap();
    assert!(!zone_file.contains(PLANTED_PRIVATE_KEY));
    for spawn in host.spawns() {
        assert!(
            spawn.iter().all(|part| !part.contains(PLANTED_PRIVATE_KEY)),
            "the private key reached a command line: {spawn:?}"
        );
    }
}

#[test]
fn no_error_of_a_signed_put_carries_the_planted_private_key() {
    // Positive control: the planted value is in the key file, so a leak into an
    // error would be findable by the same search.
    let host = FakeZoneHost::new().with_checker_refusing("dns_rdata_fromtext: bad");
    let refused = put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap_err();
    let written = FakeZoneHost::new();
    put_zone(
        &written,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap();
    assert!(
        written
            .file(&key_paths()[1])
            .unwrap()
            .contains(PLANTED_PRIVATE_KEY)
    );

    assert!(!format!("{refused}").contains(PLANTED_PRIVATE_KEY));
    assert!(!format!("{refused:?}").contains(PLANTED_PRIVATE_KEY));

    let unwritable = FakeZoneHost::new().with_key_write_refused();
    let refused = put_zone(
        &unwritable,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    )
    .unwrap_err();
    assert!(!format!("{refused}").contains(PLANTED_PRIVATE_KEY));
    assert!(!format!("{refused:?}").contains(PLANTED_PRIVATE_KEY));
}

#[test]
fn the_content_never_prints_the_planted_private_key() {
    let content = signed_content(7);

    assert!(!format!("{content:?}").contains(PLANTED_PRIVATE_KEY));
}

#[test]
fn signing_a_served_unsigned_zone_modifies_the_zone_and_removes_the_plain_copy() {
    let host = host_serving(SigningMode::Unsigned);

    let outcome = put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(8),
    )
    .unwrap();

    assert!(!outcome.created);
    let verbs = verbs(&host);
    assert!(verbs.contains(&"modzone".to_owned()), "{verbs:?}");
    assert!(!verbs.contains(&"reload".to_owned()));
    assert!(!verbs.contains(&"addzone".to_owned()));
    assert_eq!(
        host.statement("example.com").unwrap(),
        installed_statement(SigningMode::Signed)
    );
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none(),
        "the plain copy outlived the move"
    );
    assert!(
        host.file(&signed_directory().join("example.com.db"))
            .is_some()
    );
}

#[test]
fn a_served_signed_zone_put_again_with_the_same_mode_is_only_reloaded() {
    // The inverse control of the test above: nothing about signing changed.
    let host = host_serving(SigningMode::Signed);

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(8),
    )
    .unwrap();

    let verbs = verbs(&host);
    assert!(verbs.contains(&"reload".to_owned()), "{verbs:?}");
    assert!(!verbs.contains(&"modzone".to_owned()));
    assert!(!verbs.contains(&"addzone".to_owned()));
}

#[test]
fn an_unsigned_zone_put_again_unsigned_is_only_reloaded() {
    let host = host_serving(SigningMode::Unsigned);

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &sample_content("example.com", 9),
    )
    .unwrap();

    let verbs = verbs(&host);
    assert!(verbs.contains(&"reload".to_owned()));
    assert!(!verbs.contains(&"modzone".to_owned()));
}

#[test]
fn withdrawing_a_signed_zone_switches_the_policy_and_keeps_the_key_files() {
    let host = host_serving(SigningMode::Signed);

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &withdrawing_content(9),
    )
    .unwrap();

    assert!(verbs(&host).contains(&"modzone".to_owned()));
    let statement = host.statement("example.com").unwrap();
    assert!(statement.contains("dnssec-policy \"insecure\""));
    assert!(!statement.contains("maran-csk"));
    for path in key_paths() {
        assert!(host.file(&path).is_some(), "{path:?} was removed too early");
    }
}

#[test]
fn unsigning_a_signed_zone_removes_every_key_file_and_the_signed_copy() {
    let host = host_serving(SigningMode::Withdrawing);
    let unsigned = sample_content("example.com", 10);

    put_zone(&host, distro(), &directories(), &floor(), &unsigned).unwrap();

    assert!(verbs(&host).contains(&"modzone".to_owned()));
    assert_eq!(
        host.statement("example.com").unwrap(),
        installed_statement(SigningMode::Unsigned)
    );
    for path in key_paths() {
        assert!(host.file(&path).is_none(), "{path:?} survived UNSIGNED");
    }
    assert!(
        host.file(&signed_directory().join("example.com.db"))
            .is_none()
    );
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_some()
    );
}

#[test]
fn an_unsigned_put_leaves_the_key_files_of_other_zones_alone() {
    let other = key_directory().join("Kother.example.+013+00001.private");
    let host = host_serving(SigningMode::Signed).with_file(&other, "other\n");

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &sample_content("example.com", 10),
    )
    .unwrap();

    assert!(
        host.file(&other).is_some(),
        "another zone's key was removed"
    );
    assert!(host.file(&key_paths()[1]).is_none());
}

#[test]
fn an_unsigned_zone_never_gets_a_key_file() {
    let host = FakeZoneHost::new();

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &sample_content("example.com", 3),
    )
    .unwrap();

    assert!(
        host.file_paths()
            .iter()
            .all(|path| path.parent() != Some(key_directory().as_path()))
    );
}

#[test]
fn a_statement_the_daemon_cannot_show_is_modified_and_never_reloaded() {
    let host = host_serving(SigningMode::Unsigned).with_showzone_refused();

    put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(8),
    )
    .unwrap();

    let verbs = verbs(&host);
    assert!(verbs.contains(&"modzone".to_owned()));
    assert!(!verbs.contains(&"reload".to_owned()));
}

#[test]
fn a_key_that_cannot_be_written_stops_the_put_before_the_zone_is_touched() {
    let host = FakeZoneHost::new().with_key_write_refused();

    let refused = put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    );

    assert!(matches!(refused, Err(DnsError::ConfigWrite { .. })));
    assert!(
        host.file(&signed_directory().join("example.com.db"))
            .is_none()
    );
    assert!(host.served_serial("example.com").is_none());
}

#[test]
fn a_signed_zone_the_checker_refuses_is_never_loaded() {
    let host = FakeZoneHost::new().with_checker_refusing("refused");

    let refused = put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    );

    assert!(matches!(refused, Err(DnsError::ZoneRejected { .. })));
    assert!(host.served_serial("example.com").is_none());
}

#[test]
fn the_free_space_floor_is_asked_of_the_directory_the_file_lands_in() {
    let host = FakeZoneHost::new().with_available_bytes(1024);

    let refused = put_zone(
        &host,
        distro(),
        &directories(),
        &floor(),
        &signed_content(7),
    );

    assert!(matches!(refused, Err(DnsError::ZoneBelowFloor { .. })));
    assert!(host.file_paths().is_empty());
}
