//! The states `BackupSummary` can be built into, their JSON, and the shapes
//! of JSON that must NOT become a readable one.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// A manifest fixture, values unimportant beyond being a valid one.
fn manifest() -> BackupManifest {
    BackupManifest {
        version: 1,
        account: "alice".to_owned(),
        backup_id: "b1".to_owned(),
        created_at_unix: 0,
        home_bytes: 10,
        databases: Vec::new(),
        agent_version: "test".to_owned(),
    }
}

/// `readable` sets every fact a caller can act on and tags it with today's
/// version.
#[test]
fn readable_carries_the_current_version_and_every_fact() {
    let summary = BackupSummary::readable("b1".to_owned(), manifest(), 123, "a".repeat(64));

    assert!(summary.is_readable());
    assert_eq!(summary.version, SUMMARY_VERSION);
    assert!(summary.reason().is_none());
    let details = summary.readable_details().expect("a readable summary");
    assert_eq!(details.artifact_bytes, 123);
    assert_eq!(details.artifact_sha256, "a".repeat(64));
}

/// `unreadable` with `Corrupt` reports no data and version `0` — the state
/// for a sidecar that is missing or whose JSON never parsed at all.
#[test]
fn unreadable_reports_corrupt_with_no_data() {
    let summary = BackupSummary::unreadable("b1".to_owned(), UnreadableReason::Corrupt);

    assert!(!summary.is_readable());
    assert_eq!(summary.reason(), Some(&UnreadableReason::Corrupt));
    assert_eq!(summary.version, 0);
    assert!(summary.readable_details().is_none());
}

/// `unreadable` with an unknown version records that version rather than a
/// second, separately-passed copy of it — the reason and the number cannot
/// disagree because only one of them is supplied.
#[test]
fn unknown_version_reports_the_version_and_not_corrupt() {
    let summary = BackupSummary::unreadable(
        "b1".to_owned(),
        UnreadableReason::UnknownVersion { version: 7 },
    );

    assert!(!summary.is_readable());
    assert_eq!(
        summary.reason(),
        Some(&UnreadableReason::UnknownVersion { version: 7 })
    );
    assert_eq!(summary.version, 7);
    assert!(summary.readable_details().is_none());
}

/// An entry that is not a regular file is its own reason, and carries no
/// version: no sidecar was ever consulted for it.
#[test]
fn not_a_regular_file_reports_that_reason_with_no_version() {
    let summary = BackupSummary::unreadable("b1".to_owned(), UnreadableReason::NotARegularFile);

    assert!(!summary.is_readable());
    assert_eq!(summary.reason(), Some(&UnreadableReason::NotARegularFile));
    assert_eq!(summary.version, 0);
}

/// **F2, at the type.** The document that used to deserialise into a
/// readable summary with no manifest and no digest — `readable` asserted as a
/// bare flag, every fact beside it absent — must not parse into this type at
/// all. This is the whole reason the readable state is a struct: the promise
/// "readable implies the manifest and the digest are here" now has to be kept
/// by `serde`, which never read the doc comment that used to state it.
#[test]
fn a_document_claiming_readable_with_no_manifest_does_not_parse() {
    let claimed = serde_json::json!({ "version": SUMMARY_VERSION, "readable": true }).to_string();

    let parsed = serde_json::from_str::<BackupSummary>(&claimed);

    assert!(parsed.is_err(), "{parsed:?}");
}

/// The readable state's own shape with the MANIFEST left out: refused, for
/// the same structural reason. The manifest is the field the finding named,
/// and it is pinned separately from the digest so that weakening either one
/// back to an `Option` has a test of its own to fail.
#[test]
fn a_readable_state_missing_the_manifest_does_not_parse() {
    let claimed = serde_json::json!({
        "backup_id": "b1",
        "version": SUMMARY_VERSION,
        "state": {
            "readable": { "artifact_bytes": 123, "artifact_sha256": "a".repeat(64) }
        },
    })
    .to_string();

    let parsed = serde_json::from_str::<BackupSummary>(&claimed);

    assert!(parsed.is_err(), "{parsed:?}");
}

/// The same document in the readable state's own shape, but with the digest
/// left out: still refused, because `ReadableBackup`'s fields are not
/// optional. The control that says the refusal above is about the missing
/// facts and not about the outer key name.
#[test]
fn a_readable_state_missing_the_digest_does_not_parse() {
    let claimed = serde_json::json!({
        "backup_id": "b1",
        "version": SUMMARY_VERSION,
        "state": { "readable": { "manifest": manifest(), "artifact_bytes": 123 } },
    })
    .to_string();

    let parsed = serde_json::from_str::<BackupSummary>(&claimed);

    assert!(parsed.is_err(), "{parsed:?}");
}

/// A `readable` summary round-trips through JSON unchanged — the sidecar
/// this agent writes is the sidecar this agent reads back.
#[test]
fn a_readable_summary_round_trips_through_json() {
    let summary = BackupSummary::readable("b1".to_owned(), manifest(), 123, "a".repeat(64));

    let json = serde_json::to_string(&summary).expect("a readable summary serialises");
    let parsed: BackupSummary =
        serde_json::from_str(&json).expect("a readable summary's own JSON parses back");

    assert_eq!(parsed, summary);
}
