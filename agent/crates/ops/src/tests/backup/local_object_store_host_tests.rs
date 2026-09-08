//! The local destination, put through the same seam as the remote one.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, create_dir_all, set_permissions, write};
use std::os::unix::fs::{MetadataExt as _, PermissionsExt as _};

use tempfile::TempDir;

use super::*;

/// A destination rooted at a temporary directory.
///
/// `LocalBackupRoot::parse` refuses `/tmp` and everything under it — that is
/// Decision 3's point about world-writable directories — so these tests build
/// the host from its resolved root directly rather than asking the validated
/// type to approve a path it is right to refuse.
fn host_rooted_at(root: &TempDir) -> LocalObjectStoreHost {
    LocalObjectStoreHost {
        root: root
            .path()
            .canonicalize()
            .expect("the fixture root resolves"),
    }
}

/// A sink that keeps what it was told.
#[derive(Default)]
struct RecordingSink {
    /// Every report, in order.
    reports: Vec<(BackupStage, u32)>,
}

impl ProgressSink for RecordingSink {
    /// Records the report.
    fn report(&mut self, stage: BackupStage, percent: u32) {
        self.reports.push((stage, percent));
    }
}

/// The local destination keeps the seam's whole contract: an object goes in,
/// comes back, is listed, and is removed.
#[test]
fn an_object_round_trips_through_the_local_destination() {
    let root = TempDir::new().expect("a temporary directory");
    let scratch = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);
    let source = scratch.path().join("artifact.tar.gz");
    write(&source, b"the artifact").expect("the fixture is writable");
    let mut sink = RecordingSink::default();

    let sent = host
        .put("backups/alice/one.tar.gz", &source, &mut sink)
        .expect("the put succeeds");
    assert_eq!(sent, 12);

    let into = scratch.path().join("fetched.tar.gz");
    assert_eq!(
        host.get("backups/alice/one.tar.gz", &into)
            .expect("the get succeeds"),
        12
    );
    assert_eq!(
        std::fs::read(&into).expect("the fetched file is readable"),
        b"the artifact"
    );

    assert_eq!(
        host.list("backups/alice/").expect("the listing succeeds"),
        vec![ObjectSummary {
            key: "backups/alice/one.tar.gz".to_owned(),
            bytes: 12,
        }]
    );

    host.delete("backups/alice/one.tar.gz")
        .expect("the delete succeeds");
    assert!(
        host.list("backups/alice/")
            .expect("the listing succeeds")
            .is_empty()
    );
}

/// A published object is root's alone: mode `0600` and one hard link.
///
/// It holds every file in a customer's home and a full dump of every database
/// they own, and the directory above it being root-only is one `chmod` away
/// from not being true.
#[test]
fn a_published_object_is_readable_by_its_owner_alone() {
    let root = TempDir::new().expect("a temporary directory");
    let scratch = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);
    let source = scratch.path().join("artifact.tar.gz");
    write(&source, b"secret").expect("the fixture is writable");
    // The source is world-readable; the published object must not inherit that.
    set_permissions(&source, Permissions::from_mode(0o644)).expect("the fixture is chmodable");

    host.put(
        "backups/alice/one.tar.gz",
        &source,
        &mut RecordingSink::default(),
    )
    .expect("the put succeeds");

    let published = root.path().join("backups/alice/one.tar.gz");
    let metadata = published
        .symlink_metadata()
        .expect("the object was published");
    assert_eq!(metadata.mode() & 0o7777, 0o600, "{:o}", metadata.mode());
    assert_eq!(metadata.nlink(), 1);
}

/// A key that tries to leave the destination's root is refused, and nothing is
/// written.
///
/// No key this product builds can look like this — `object_key` composes
/// validated values — so this is the check for the keys it did not build. In a
/// bucket a `..` segment aims a write at another account's prefix; on a
/// filesystem it aims one at `/etc`.
#[test]
fn a_key_that_leaves_the_destination_root_is_refused() {
    let root = TempDir::new().expect("a temporary directory");
    let scratch = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);
    let source = scratch.path().join("artifact.tar.gz");
    write(&source, b"payload").expect("the fixture is writable");
    let escape = scratch.path().join("escaped");

    for key in [
        "../escaped",
        "/etc/cron.d/pwn",
        "backups/../../escaped",
        "",
        "backups//one.tar.gz",
    ] {
        let refusal = host.put(key, &source, &mut RecordingSink::default());

        assert!(
            matches!(refusal, Err(BackupError::ObjectStoreFailed { .. })),
            "{key} was accepted"
        );
    }

    assert!(!escape.exists(), "a refused key still wrote something");
}

/// Deleting an object the destination does not hold is success.
#[test]
fn deleting_an_absent_object_from_the_local_destination_reports_success() {
    let root = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);

    assert_eq!(host.delete("backups/alice/absent.tar.gz"), Ok(()));
}

/// Listing a prefix whose directory does not exist is an empty list; listing
/// one that exists and holds nothing is too.
///
/// Both are the same fact — this account has no backups — and neither is an
/// error. What IS an error is a directory that cannot be read, which is a
/// different thing from an empty one and is why this test's sibling case is not
/// written as "any failure is empty".
#[test]
fn listing_an_account_with_no_backups_answers_an_empty_list() {
    let root = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);
    create_dir_all(root.path().join("backups/bob")).expect("the fixture directory is creatable");

    assert!(
        host.list("backups/alice/")
            .expect("absent is empty")
            .is_empty()
    );
    assert!(
        host.list("backups/bob/")
            .expect("empty is empty")
            .is_empty()
    );
}

/// A listing reports only files, and reports them under the prefix it was
/// asked about.
#[test]
fn a_listing_reports_files_under_the_prefix_and_ignores_directories() {
    let root = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);
    let directory = root.path().join("backups/alice");
    create_dir_all(directory.join("not-an-object")).expect("the fixture is creatable");
    write(directory.join("one.tar.gz"), b"12345").expect("the fixture is writable");
    write(directory.join("two.tar.gz"), b"1234567").expect("the fixture is writable");

    let listed = host.list("backups/alice/").expect("the listing succeeds");

    assert_eq!(
        listed,
        vec![
            ObjectSummary {
                key: "backups/alice/one.tar.gz".to_owned(),
                bytes: 5,
            },
            ObjectSummary {
                key: "backups/alice/two.tar.gz".to_owned(),
                bytes: 7,
            },
        ]
    );
}

/// A local destination's public-read probe is `Unproven`, and says why.
///
/// **Not `Private`.** There is no anonymous HTTP client for a directory on this
/// machine, so there is no fetch to make and nothing to observe; answering
/// `Private` would be a green result produced by a check that made no
/// observation. What keeps a local root off the internet is `LocalBackupRoot`'s
/// refusal of a path under a document root and the inode check before each
/// write, and neither of those is this method.
#[test]
fn a_local_destination_reports_its_public_read_as_unproven_and_never_private() {
    let root = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);

    let verdict = host
        .probe_public_read("backups/alice/canary")
        .expect("the probe answers");

    let PublicReadVerdict::Unproven { reason } = verdict else {
        panic!("a local destination cannot prove anything about an HTTP fetch");
    };
    assert!(!reason.is_empty(), "an unproven verdict must say why");
}

/// A publication that fails leaves no `.partial` behind and publishes nothing.
#[test]
fn a_failed_publication_leaves_no_partial_behind() {
    let root = TempDir::new().expect("a temporary directory");
    let scratch = TempDir::new().expect("a temporary directory");
    let host = host_rooted_at(&root);

    let refusal = host.put(
        "backups/alice/one.tar.gz",
        &scratch.path().join("there-is-no-such-file"),
        &mut RecordingSink::default(),
    );

    assert!(matches!(refusal, Err(BackupError::ChecksumUnreadable)));
    assert!(!root.path().join("backups/alice/one.tar.gz").exists());
    assert!(
        !root
            .path()
            .join("backups/alice/one.tar.gz.partial")
            .exists()
    );
}
