//! Reading a sidecar off disk, and refusing an unknown version before
//! anything downstream sees its fields.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::write;

use super::*;
use crate::backup::model::backup_summary::SUMMARY_VERSION;

/// A sidecar path a test can really write into.
fn sidecar_path() -> (tempfile::TempDir, std::path::PathBuf) {
    let dir = tempfile::TempDir::new().expect("a temporary directory");
    let path = dir.path().join("x.meta.json");
    (dir, path)
}

/// A sidecar JSON body that parses cleanly into today's `BackupSummary`
/// shape, differing from the real fixture only in `"version"` — the case a
/// check that only asks "did this parse?" cannot see.
fn sidecar_json_at_version(version: u32) -> String {
    serde_json::json!({
        "backup_id": "x",
        "version": version,
        "state": {
            "readable": {
                "manifest": {
                    "version": 1,
                    "account": "alice",
                    "backup_id": "x",
                    "created_at_unix": 0,
                    "home_bytes": 10,
                    "databases": [],
                    "agent_version": "test",
                },
                "artifact_bytes": 123,
                "artifact_sha256": "a".repeat(64),
            }
        },
    })
    .to_string()
}

/// A sidecar at today's version is read, manifest and all.
#[test]
fn a_sidecar_at_the_current_version_is_read() {
    let (_dir, path) = sidecar_path();
    write(&path, sidecar_json_at_version(SUMMARY_VERSION)).unwrap();

    let details = read_sidecar(&path).expect("a current-version sidecar reads");

    assert_eq!(details.artifact_bytes, 123);
    assert_eq!(details.artifact_sha256, "a".repeat(64));
}

/// **F2, at the one place a sidecar is parsed.** The document at the heart of
/// the finding — it claims `readable` and carries neither a manifest nor a
/// digest — is `Corrupt` here, not an `Ok` holding nothing. Before the
/// readable state became a struct this parsed cleanly and `list_backups`
/// reported it as a healthy backup with a `None` where the digest a restore
/// checks the artifact against belongs.
#[test]
fn a_sidecar_claiming_readable_with_no_manifest_is_corrupt() {
    let (_dir, path) = sidecar_path();
    write(
        &path,
        serde_json::json!({ "version": SUMMARY_VERSION, "readable": true }).to_string(),
    )
    .unwrap();

    let error = read_sidecar(&path).expect_err("a claim with nothing behind it is refused");

    assert_eq!(error, UnreadableReason::Corrupt);
}

/// A sidecar in the current shape that describes ITSELF as unreadable — one
/// this agent never writes, so one somebody else placed — is refused with the
/// reason it recorded, not passed on as a value each caller must re-check.
#[test]
fn a_sidecar_describing_itself_as_unreadable_is_refused_with_its_own_reason() {
    let (_dir, path) = sidecar_path();
    write(
        &path,
        serde_json::json!({
            "backup_id": "x",
            "version": SUMMARY_VERSION,
            "state": { "unreadable": "Corrupt" },
        })
        .to_string(),
    )
    .unwrap();

    let error = read_sidecar(&path).expect_err("a self-declared unreadable sidecar is refused");

    assert_eq!(error, UnreadableReason::Corrupt);
}

/// The case a naive `serde` error check cannot see: the JSON parses cleanly,
/// `readable` is even `true`, and the only thing wrong is a `version` this
/// agent has never heard of. `read_sidecar` must refuse it rather than hand
/// back the manifest it happens to carry.
#[test]
fn a_sidecar_naming_an_unknown_version_is_refused_even_though_it_parses() {
    let (_dir, path) = sidecar_path();
    let unknown = SUMMARY_VERSION + 1;
    write(&path, sidecar_json_at_version(unknown)).unwrap();

    let error = read_sidecar(&path).expect_err("an unknown version is refused");

    assert_eq!(error, UnreadableReason::UnknownVersion { version: unknown });
}

/// JSON that never parses at all is `Corrupt`, not `UnknownVersion` — there is
/// no version to report.
#[test]
fn unparseable_json_is_corrupt() {
    let (_dir, path) = sidecar_path();
    write(&path, "{ not json").unwrap();

    let error = read_sidecar(&path).expect_err("unparseable JSON is refused");

    assert_eq!(error, UnreadableReason::Corrupt);
}

/// A missing sidecar is `Corrupt`.
#[test]
fn a_missing_sidecar_is_corrupt() {
    let (_dir, path) = sidecar_path();

    let error = read_sidecar(&path).expect_err("a missing sidecar is refused");

    assert_eq!(error, UnreadableReason::Corrupt);
}
