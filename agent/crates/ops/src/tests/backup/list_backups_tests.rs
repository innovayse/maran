//! Listing a local destination's backups: every artifact it holds, readable
//! sidecar or not.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsString;
use std::fs::{File, Permissions, create_dir, create_dir_all, set_permissions, write};
use std::os::unix::ffi::OsStringExt;
use std::os::unix::fs::{PermissionsExt as _, symlink};

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;

use super::*;
use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_summary::SUMMARY_VERSION;
use crate::backup::model::unreadable_reason::UnreadableReason;

/// A directory a test can really write into, standing in for one account's
/// backup directory.
fn directory() -> tempfile::TempDir {
    tempfile::TempDir::new().expect("a temporary directory")
}

/// A backup id built from a fixed uuid string, so fixtures are readable.
fn id(uuid: &str) -> BackupId {
    BackupId::parse(uuid).expect("the fixture id is a valid uuid")
}

/// Writes an artifact (and, unless `with_sidecar` is `false`, a matching
/// sidecar) under `directory`.
fn write_artifact(directory: &std::path::Path, backup_id: &BackupId, sidecar: Option<&str>) {
    File::create(directory.join(format!("{}.tar.gz", backup_id.as_str())))
        .expect("the artifact fixture is writable");

    if let Some(contents) = sidecar {
        write(
            directory.join(format!("{}.meta.json", backup_id.as_str())),
            contents,
        )
        .expect("the sidecar fixture is writable");
    }
}

/// A sidecar whose JSON a listing can actually parse.
fn readable_sidecar(backup_id: &BackupId) -> String {
    serde_json::to_string(&BackupSummary::readable(
        backup_id.as_str().to_owned(),
        crate::backup::model::backup_manifest::BackupManifest {
            version: 1,
            account: "alice".to_owned(),
            backup_id: backup_id.as_str().to_owned(),
            created_at_unix: 0,
            home_bytes: 10,
            databases: Vec::new(),
            agent_version: "test".to_owned(),
        },
        123,
        "a".repeat(64),
    ))
    .expect("the fixture summary serialises")
}

/// **The distinguishing case.** A readable sidecar beside an unreadable one:
/// a listing that only ever produced the readable set would look correct
/// right up until the unreadable entries accumulated forever, because nothing
/// downstream of a silent skip can see what it dropped.
#[test]
fn an_unreadable_sidecar_is_listed_as_unreadable_beside_a_readable_one() {
    let directory = directory();
    let good = id("11111111-1111-1111-1111-111111111111");
    let corrupt = id("22222222-2222-2222-2222-222222222222");
    let missing = id("33333333-3333-3333-3333-333333333333");

    write_artifact(directory.path(), &good, Some(&readable_sidecar(&good)));
    write_artifact(directory.path(), &corrupt, Some("{ not json"));
    write_artifact(directory.path(), &missing, None);

    let mut summaries = list_in(directory.path()).expect("the directory lists");
    summaries.sort_by(|a, b| a.backup_id.cmp(&b.backup_id));

    assert_eq!(summaries.len(), 3);
    assert!(summaries[0].is_readable(), "{:?}", summaries[0]);
    assert_eq!(summaries[0].backup_id, good.as_str());
    assert!(!summaries[1].is_readable(), "{:?}", summaries[1]);
    assert_eq!(summaries[1].backup_id, corrupt.as_str());
    assert!(summaries[1].readable_details().is_none());
    assert!(!summaries[2].is_readable(), "{:?}", summaries[2]);
    assert_eq!(summaries[2].backup_id, missing.as_str());
}

/// A sidecar JSON body whose fields all parse into today's [`BackupSummary`]
/// shape, but whose `"version"` claims `version` — the case a naive
/// `serde` error check cannot see, because nothing fails to parse.
fn sidecar_json_at_version(backup_id: &BackupId, version: u32) -> String {
    serde_json::json!({
        "backup_id": backup_id.as_str(),
        "version": version,
        "state": {
            "readable": {
                "manifest": {
                    "version": 1,
                    "account": "alice",
                    "backup_id": backup_id.as_str(),
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

/// **The case a naive `serde` error check cannot see.** This sidecar's JSON
/// parses cleanly into today's [`BackupSummary`] shape — every field is the
/// right type, `readable` is even `true` — and the ONLY thing wrong with it
/// is a `version` this agent has never heard of. A check that only asks
/// "did this parse?" says yes and would go on to hand a caller a manifest and
/// a digest this agent was never told how to interpret.
#[test]
fn a_sidecar_naming_an_unknown_version_is_listed_as_unreadable_with_that_version() {
    let directory = directory();
    let future = id("55555555-5555-5555-5555-555555555555");
    let unknown_version = SUMMARY_VERSION + 1;

    write_artifact(
        directory.path(),
        &future,
        Some(&sidecar_json_at_version(&future, unknown_version)),
    );

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert_eq!(summaries.len(), 1);
    let summary = &summaries[0];
    assert!(!summary.is_readable(), "{summary:?}");
    assert!(summary.readable_details().is_none(), "{summary:?}");
    assert_eq!(
        summary.reason(),
        Some(&UnreadableReason::UnknownVersion {
            version: unknown_version
        }),
        "{summary:?}"
    );
}

/// A sidecar written before the version field existed at all — the shape a
/// pre-versioning agent left behind — has no `"version"` key rather than a
/// wrong one. It must be treated exactly like a known-but-foreign version
/// (`0`), never like a current one and never like a corrupt one.
#[test]
fn a_sidecar_missing_the_version_field_entirely_is_treated_as_version_zero() {
    let directory = directory();
    let legacy = id("66666666-6666-6666-6666-666666666666");

    let legacy_json = serde_json::json!({
        "state": {
            "readable": {
                "manifest": {
                    "version": 1,
                    "account": "alice",
                    "backup_id": legacy.as_str(),
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
    .to_string();

    write_artifact(directory.path(), &legacy, Some(&legacy_json));

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert_eq!(summaries.len(), 1);
    let summary = &summaries[0];
    assert!(!summary.is_readable(), "{summary:?}");
    assert!(summary.readable_details().is_none(), "{summary:?}");
    assert_eq!(
        summary.reason(),
        Some(&UnreadableReason::UnknownVersion { version: 0 }),
        "{summary:?}"
    );
}

/// The inverse control a refusing gate needs: a sidecar at today's
/// [`SUMMARY_VERSION`] is accepted, not swept into "unknown" by an
/// off-by-one or an inverted comparison.
#[test]
fn a_sidecar_at_the_current_version_is_accepted() {
    let directory = directory();
    let current = id("77777777-7777-7777-7777-777777777777");

    write_artifact(
        directory.path(),
        &current,
        Some(&readable_sidecar(&current)),
    );

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert_eq!(summaries.len(), 1);
    assert!(summaries[0].is_readable(), "{:?}", summaries[0]);
    assert!(summaries[0].reason().is_none(), "{:?}", summaries[0]);
    assert!(
        summaries[0].readable_details().is_some(),
        "{:?}",
        summaries[0]
    );
}

/// A `.partial` artifact — Task 3's name for one that never finished — is
/// never listed, whether or not the run that left it behind also cleaned it
/// up. Listing must not depend on that cleanup having run.
#[test]
fn a_partial_artifact_is_never_listed() {
    let directory = directory();
    let partial = id("44444444-4444-4444-4444-444444444444");

    File::create(
        directory
            .path()
            .join(format!("{}.tar.gz.partial", partial.as_str())),
    )
    .expect("the partial fixture is writable");

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert!(summaries.is_empty(), "{summaries:?}");
}

/// An account with no backups yet lists as empty, not as an error.
#[test]
fn an_empty_directory_lists_as_empty() {
    let directory = directory();
    create_dir_all(directory.path()).expect("the fixture directory exists");

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert!(summaries.is_empty(), "{summaries:?}");
}

/// **F2, at the listing.** A sidecar whose JSON parses and claims to be
/// readable while carrying no manifest and no digest must be listed as
/// UNREADABLE, not as a backup with holes in it.
///
/// This is the test that tells the two shapes apart. A test asserting only
/// that the listing "works" passes on both: before, the entry came back with
/// `readable` true, `manifest` `None` and `artifact_sha256` `None`, and the
/// panel would have shown it as a healthy backup while a restore had nothing
/// to check the artifact's bytes against. The entry is still listed — it is
/// litter retention must be able to see — but it is listed for what it is.
#[test]
fn a_sidecar_claiming_readable_with_no_manifest_is_listed_as_unreadable() {
    let directory = directory();
    let claimed = id("88888888-8888-8888-8888-888888888888");

    write_artifact(
        directory.path(),
        &claimed,
        Some(&serde_json::json!({ "version": SUMMARY_VERSION, "readable": true }).to_string()),
    );

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert_eq!(summaries.len(), 1);
    let summary = &summaries[0];
    assert!(!summary.is_readable(), "{summary:?}");
    assert_eq!(
        summary.reason(),
        Some(&UnreadableReason::Corrupt),
        "{summary:?}"
    );
    assert_eq!(summary.backup_id, claimed.as_str());
}

/// **F4, case three, the directory.** A directory named `<valid id>.tar.gz`
/// wears an artifact's name convincingly: the suffix matches and the stem
/// parses. Listed as an ordinary backup it is an entry retention counts,
/// tries to prune, and can NEVER prune, because `delete_backup`'s
/// `remove_file` fails on a directory forever. It is listed, and it is listed
/// as unreadable.
#[test]
fn a_directory_wearing_an_artifacts_name_is_listed_as_not_a_regular_file() {
    let directory = directory();
    let impostor = id("99999999-9999-9999-9999-999999999999");

    create_dir(
        directory
            .path()
            .join(format!("{}.tar.gz", impostor.as_str())),
    )
    .expect("the directory fixture is creatable");

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert_eq!(summaries.len(), 1);
    let summary = &summaries[0];
    assert!(!summary.is_readable(), "{summary:?}");
    assert_eq!(
        summary.reason(),
        Some(&UnreadableReason::NotARegularFile),
        "{summary:?}"
    );
}

/// **F4, case three, the symbolic link — and the one with teeth.** A link
/// named `<valid id>.tar.gz` pointing at a real artifact passes every test a
/// name can be given, and a listing that never asked the inode reported it as
/// an ordinary backup a restore would then read THROUGH, to bytes chosen by
/// whoever placed the link. The entry type `read_dir` already returned does
/// not follow the link, so the answer is available for free.
#[test]
fn a_symlink_wearing_an_artifacts_name_is_listed_as_not_a_regular_file() {
    let directory = directory();
    let real = id("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    let link = id("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    write_artifact(directory.path(), &real, Some(&readable_sidecar(&real)));
    symlink(
        directory.path().join(format!("{}.tar.gz", real.as_str())),
        directory.path().join(format!("{}.tar.gz", link.as_str())),
    )
    .expect("the symlink fixture is creatable");

    let mut summaries = list_in(directory.path()).expect("the directory lists");
    summaries.sort_by(|a, b| a.backup_id.cmp(&b.backup_id));

    assert_eq!(summaries.len(), 2);
    assert!(summaries[0].is_readable(), "{:?}", summaries[0]);
    assert_eq!(summaries[0].backup_id, real.as_str());
    assert_eq!(
        summaries[1].reason(),
        Some(&UnreadableReason::NotARegularFile),
        "{:?}",
        summaries[1]
    );
    assert_eq!(summaries[1].backup_id, link.as_str());
}

/// **F4, case two.** A `.tar.gz` whose stem is not a backup id is refused,
/// naming the file. It used to be skipped, which is the invisible-to-retention
/// outcome this module refuses for a corrupt sidecar; it is not listed either,
/// because the only field that could carry the name is the one the panel sends
/// back as an identifier.
#[test]
fn an_artifact_whose_stem_is_not_a_backup_id_is_refused_by_name() {
    let directory = directory();
    File::create(directory.path().join("not-a-uuid.tar.gz"))
        .expect("the litter fixture is writable");

    let error = list_in(directory.path()).expect_err("an unminted artifact name is refused");

    assert_eq!(
        error,
        BackupError::UnmintedArtifactName {
            name: "not-a-uuid.tar.gz".to_owned()
        }
    );
}

/// **F4, case one.** A name that is not valid UTF-8 but ends in `.tar.gz` is
/// refused too. It used to disappear at `to_str()`, before anything had asked
/// whether it was claiming to be an artifact — which is why the suffix is now
/// tested on the raw bytes.
///
/// The fixture creates a file whose name is not valid UTF-8. Linux file names
/// are byte strings, so this is creatable on the filesystems this project
/// runs on; the test would fail rather than pass quietly on one that refused
/// the name.
#[test]
fn an_artifact_name_that_is_not_utf8_is_refused() {
    let directory = directory();
    let name = OsString::from_vec(b"\xff\xfe.tar.gz".to_vec());
    File::create(directory.path().join(&name)).expect("a non-UTF-8 name is creatable here");

    let error = list_in(directory.path()).expect_err("a non-UTF-8 artifact name is refused");

    assert!(
        matches!(error, BackupError::UnmintedArtifactName { .. }),
        "{error:?}"
    );
}

/// The control the two refusals above need: litter that does NOT wear the
/// artifact extension is none of this function's business and is still passed
/// over in silence. This directory has never claimed to hold nothing but
/// artifacts — sidecars live here, and so may an operator's notes.
#[test]
fn litter_that_is_not_an_artifact_name_is_passed_over() {
    let directory = directory();
    let good = id("cccccccc-cccc-cccc-cccc-cccccccccccc");

    write_artifact(directory.path(), &good, Some(&readable_sidecar(&good)));
    write(directory.path().join("notes.txt"), "an operator was here")
        .expect("the litter fixture is writable");
    File::create(directory.path().join("not-a-uuid.tar.gz.partial"))
        .expect("the partial fixture is writable");

    let summaries = list_in(directory.path()).expect("the directory lists");

    assert_eq!(summaries.len(), 1);
    assert_eq!(summaries[0].backup_id, good.as_str());
}

/// **Finding 8.** Listing an account that has never been backed up answers an
/// empty list and leaves NO directory behind — a read creates nothing.
///
/// The assertion that carries the finding is the second one. The first would
/// pass just as well against the previous implementation, which called the
/// PREPARING entry point and returned an empty list from a directory it had
/// just created; only the `!exists()` can tell the two apart, which is why the
/// listing takes its owner as a parameter at all.
#[test]
fn listing_an_account_with_no_directory_answers_empty_and_creates_nothing() {
    let root = root_only_directory();

    let summaries = list_under(root.path(), &account(), owner()).expect("the root lists");

    assert!(summaries.is_empty(), "{summaries:?}");
    assert!(
        !root.path().join(account().as_str()).exists(),
        "a listing must not leave an inode behind"
    );
}

/// **The inverse control for the test above.** A listing of an account that DOES
/// have a directory reads it, so the empty answer above is an absence and not a
/// gate that refuses everything.
#[test]
fn listing_an_account_whose_directory_exists_reads_its_artifacts() {
    let root = root_only_directory();
    let directory = root.path().join(account().as_str());
    create_dir(&directory).expect("the account directory is creatable");
    set_permissions(&directory, Permissions::from_mode(0o700)).expect("the fixture is chmodable");
    let backup = id("11111111-1111-4111-8111-111111111111");
    write_artifact(&directory, &backup, Some(&readable_sidecar(&backup)));

    let summaries = list_under(root.path(), &account(), owner()).expect("the root lists");

    assert_eq!(summaries.len(), 1);
    assert_eq!(summaries[0].backup_id, backup.as_str());
}

/// A directory a test can really create, owned by the running user and
/// reachable by nobody else — what the inode check demands of a backup root.
fn root_only_directory() -> tempfile::TempDir {
    let directory = tempfile::TempDir::new().expect("a temporary directory");
    set_permissions(directory.path(), Permissions::from_mode(0o700))
        .expect("the fixture is chmodable");
    directory
}

/// The account these root-level fixtures belong to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The uid the fixtures are really owned by, since a test is not root.
fn owner() -> u32 {
    maran_agent_core::utils::current_uid::current_uid().expect("the current uid is readable")
}
