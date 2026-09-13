//! Tests for the listing entry the panel reads.
//!
//! The proposition: a readable entry ALWAYS crosses the wire with its manifest,
//! and an unreadable one always crosses as the unreadable arm with a named
//! reason — never as a readable-looking entry with holes in it. That is the
//! contradiction the structural `BackupState` removed in the agent, and this
//! file is where it is pinned on the wire.
//!
//! The check is made on the ENCODED bytes and not only on the struct in hand,
//! because the wire is where the guarantee stops being total: proto3's `oneof`
//! makes the arm exactly-one and does not make `ReadableBackup.manifest`
//! present. A decode is the only way to observe what a reader would actually
//! receive.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::backup::{BackupManifest, BackupSummary, ManifestDatabase, UnreadableReason};
use prost::Message;

use super::to_backup_info;
use crate::proto::{BackupInfo, UnreadableKind, backup_info};

/// The manifest a readable entry carries.
fn manifest() -> BackupManifest {
    BackupManifest {
        version: 1,
        account: "alice".to_owned(),
        backup_id: "3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(),
        created_at_unix: 1_756_000_000,
        home_bytes: 4096,
        databases: vec![ManifestDatabase {
            name: "alice_shop".to_owned(),
            bytes: 128,
            sha256: "a".repeat(64),
        }],
        agent_version: "0.1.0".to_owned(),
    }
}

/// A readable summary as a completed creation writes one.
fn readable() -> BackupSummary {
    BackupSummary::readable(
        "3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(),
        manifest(),
        2048,
        "b".repeat(64),
    )
}

/// What a peer would actually receive: the message, encoded and decoded again.
fn over_the_wire(info: &BackupInfo) -> BackupInfo {
    let mut bytes = Vec::new();
    info.encode(&mut bytes).expect("the message encodes");
    BackupInfo::decode(bytes.as_slice()).expect("the message decodes")
}

#[test]
fn a_readable_backup_crosses_the_wire_with_its_manifest() {
    let received = over_the_wire(&to_backup_info(readable()));

    match received.state {
        Some(backup_info::State::Readable(details)) => {
            let manifest = details
                .manifest
                .expect("a readable arm carries its manifest over the wire");
            assert_eq!(manifest.account, "alice");
            assert_eq!(manifest.databases.len(), 1);
            assert_eq!(details.artifact_bytes, 2048);
            assert_eq!(details.artifact_sha256, "b".repeat(64));
        }
        other => panic!("expected the readable arm, got {other:?}"),
    }
}

#[test]
fn the_flat_fields_mirror_the_readable_arm_they_predate() {
    let info = to_backup_info(readable());

    assert_eq!(info.size_bytes, 2048);
    assert_eq!(info.created_at_unix, 1_756_000_000);
    assert_eq!(info.sha256, "b".repeat(64));
}

#[test]
fn an_unreadable_backup_crosses_as_the_unreadable_arm_with_its_reason() {
    for (reason, expected, version) in [
        (UnreadableReason::Corrupt, UnreadableKind::Corrupt, 0),
        (
            UnreadableReason::UnknownVersion { version: 7 },
            UnreadableKind::UnknownVersion,
            7,
        ),
        (
            UnreadableReason::NotARegularFile,
            UnreadableKind::NotARegularFile,
            0,
        ),
    ] {
        let summary =
            BackupSummary::unreadable("3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(), reason);
        let received = over_the_wire(&to_backup_info(summary));

        match received.state {
            Some(backup_info::State::Unreadable(unreadable)) => {
                assert_eq!(unreadable.kind, expected as i32);
                assert_eq!(unreadable.version, version);
            }
            other => panic!("expected the unreadable arm, got {other:?}"),
        }
    }
}

#[test]
fn an_unreadable_backup_carries_no_size_no_time_and_no_digest() {
    let summary = BackupSummary::unreadable(
        "3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(),
        UnreadableReason::Corrupt,
    );
    let info = to_backup_info(summary);

    // Zero and empty rather than a plausible-looking number: all three are
    // sidecar facts, and this entry's sidecar could not be trusted.
    assert_eq!(info.size_bytes, 0);
    assert_eq!(info.created_at_unix, 0);
    assert!(info.sha256.is_empty());
}

#[test]
fn every_entry_names_one_arm_and_keeps_the_backups_id() {
    for summary in [
        readable(),
        BackupSummary::unreadable(
            "3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(),
            UnreadableReason::NotARegularFile,
        ),
    ] {
        let received = over_the_wire(&to_backup_info(summary));

        assert!(
            received.state.is_some(),
            "an entry with no arm set is one a reader cannot classify"
        );
        assert_eq!(received.backup_id, "3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    }
}

#[test]
fn the_wire_permits_a_readable_arm_with_no_manifest_which_is_why_a_reader_must_refuse_it() {
    // The honest limit, exhibited rather than asserted in prose: proto3 makes
    // the ARM exactly-one and does not make `manifest` present, so a peer other
    // than this agent can send this. The agent is only a PRODUCER of
    // `BackupInfo`, so the refusal cannot live here — a branch on a value no
    // call site can supply is unreachable code, which is the defect the
    // object-store seam was found to have. It belongs to every reader, and
    // this test is what says so with something a reader can run.
    let hazard = BackupInfo {
        backup_id: "3f2504e0-4f89-41d3-9a0c-0305e82c3301".to_owned(),
        size_bytes: 0,
        created_at_unix: 0,
        sha256: String::new(),
        state: Some(backup_info::State::Readable(crate::proto::ReadableBackup {
            manifest: None,
            artifact_bytes: 0,
            artifact_sha256: String::new(),
        })),
    };

    let received = over_the_wire(&hazard);

    match received.state {
        Some(backup_info::State::Readable(details)) => assert!(
            details.manifest.is_none(),
            "the wire carried the manifest this message never had"
        ),
        other => panic!("expected the readable arm, got {other:?}"),
    }
}

#[test]
fn this_agent_never_produces_the_hazard_it_exhibits() {
    // The inverse control for the test above: every summary this agent can
    // build and hand to the mapper crosses with its manifest, so the hazard is
    // a property of the wire and not of this producer.
    let received = over_the_wire(&to_backup_info(readable()));

    match received.state {
        Some(backup_info::State::Readable(details)) => assert!(details.manifest.is_some()),
        other => panic!("expected the readable arm, got {other:?}"),
    }
}
