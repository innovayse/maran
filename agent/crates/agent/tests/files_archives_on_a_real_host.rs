//! The archive rpcs against a real home, a real account and the real privilege
//! drop (issue #168): the ordinary round trip, and every hostile archive the
//! threat note names.
//!
//! The archives are hand-built (`fixtures/tar_member.rs` and `fixtures/zip_member.rs`), so the crates
//! under test never made their own input. A refusal is judged by what it left:
//! the tree of the home, and the places a slip would have reached.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/archive_session.rs"]
mod archive_session;
#[path = "fixtures/crc32.rs"]
mod crc32;
#[path = "fixtures/gzip_stored.rs"]
mod gzip_stored;
#[path = "fixtures/in_process_agent.rs"]
mod in_process_agent;
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_home_tree.rs"]
mod polygon_home_tree;
#[path = "fixtures/tar_member.rs"]
mod tar_member;
#[path = "fixtures/zip_member.rs"]
mod zip_member;

use std::os::unix::fs::MetadataExt;

use maran_agent::proto::{ArchiveFormat, ErrorCode};

use archive_session::ArchiveSession;
use polygon_account::PolygonAccount;
use polygon_home_tree::PolygonHomeTree;
use tar_member::{TAR_DEVICE, TAR_FIFO, TarMember};
use zip_member::ZipMember;

/// Plants `bytes` as `relative` in the account's home.
fn upload(tree: &PolygonHomeTree<'_>, relative: &str, bytes: &[u8]) {
    tree.file(relative, bytes);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_zip_and_a_tar_gz_extract_owned_by_the_account_with_normalised_modes() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive1");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    upload(
        &tree,
        "a.zip",
        &ZipMember::archive(&[
            ZipMember::file("site/index.html", b"<html></html>"),
            ZipMember::with_mode("site/run.sh", b"#!/bin/sh\n", 0o104_755),
        ]),
    );
    upload(
        &tree,
        "b.tar.gz",
        &TarMember::gzipped_archive(&[
            TarMember::file_with_mode("setuid", b"x", 0o4755),
            TarMember::directory("sub/"),
            TarMember::file("sub/f.txt", b"payload"),
        ]),
    );

    let zip = session
        .extract(ArchiveSession::extract_request(
            name,
            "a.zip",
            "out-zip",
            ArchiveFormat::Zip,
        ))
        .await
        .unwrap();
    let tgz = session
        .extract(ArchiveSession::extract_request(
            name,
            "b.tar.gz",
            "out-tgz",
            ArchiveFormat::TarGz,
        ))
        .await
        .unwrap();

    assert_eq!((zip.entry_count, tgz.entry_count), (2, 3));
    let home = account.home();
    for path in ["out-zip/site/index.html", "out-tgz/sub/f.txt"] {
        let meta = std::fs::metadata(home.join(path)).unwrap();
        assert_eq!(
            meta.uid(),
            account.ids().uid(),
            "{path} is the account's, not root's"
        );
        assert_eq!(meta.mode() & 0o7777, 0o644);
    }
    assert_eq!(
        std::fs::metadata(home.join("out-zip/site/run.sh"))
            .unwrap()
            .mode()
            & 0o7777,
        0o755
    );
    assert_eq!(
        std::fs::metadata(home.join("out-tgz/setuid"))
            .unwrap()
            .mode()
            & 0o7777,
        0o755
    );
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn an_archive_with_a_dotdot_or_absolute_member_is_refused_and_nothing_reaches_the_host() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive2");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    let slip = "../../../../tmp/maran-archive-slip";
    upload(
        &tree,
        "tar-slip.tar.gz",
        &TarMember::gzipped_archive(&[
            TarMember::file("ok.txt", b"x"),
            TarMember::file(slip, b"x"),
        ]),
    );
    upload(
        &tree,
        "zip-slip.zip",
        &ZipMember::archive(&[ZipMember::file("ok.txt", b"x"), ZipMember::file(slip, b"x")]),
    );
    upload(
        &tree,
        "abs.tar.gz",
        &TarMember::gzipped_archive(&[TarMember::file("/tmp/maran-archive-abs", b"x")]),
    );

    for (archive, format) in [
        ("tar-slip.tar.gz", ArchiveFormat::TarGz),
        ("zip-slip.zip", ArchiveFormat::Zip),
        ("abs.tar.gz", ArchiveFormat::TarGz),
    ] {
        let result = session
            .extract(ArchiveSession::extract_request(
                name, archive, "out", format,
            ))
            .await;
        ArchiveSession::assert_code(&result, ErrorCode::ArchiveUnsafe);
    }

    assert!(!std::path::Path::new("/tmp/maran-archive-slip").exists());
    assert!(!std::path::Path::new("/tmp/maran-archive-abs").exists());
    assert!(
        !account.home().join("out").exists(),
        "nothing, not even the destination"
    );
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn link_device_and_fifo_members_each_refuse_the_whole_archive() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive3");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    upload(
        &tree,
        "link.tar.gz",
        &TarMember::gzipped_archive(&[
            TarMember::file("first", b"x"),
            TarMember::symlink("l", "/etc/passwd"),
        ]),
    );
    upload(
        &tree,
        "dev.tar.gz",
        &TarMember::gzipped_archive(&[TarMember::special("d", TAR_DEVICE)]),
    );
    upload(
        &tree,
        "fifo.tar.gz",
        &TarMember::gzipped_archive(&[TarMember::special("p", TAR_FIFO)]),
    );
    upload(
        &tree,
        "link.zip",
        &ZipMember::archive(&[ZipMember::with_mode("l", b"/etc/passwd", 0o120_777)]),
    );

    for (archive, format) in [
        ("link.tar.gz", ArchiveFormat::TarGz),
        ("dev.tar.gz", ArchiveFormat::TarGz),
        ("fifo.tar.gz", ArchiveFormat::TarGz),
        ("link.zip", ArchiveFormat::Zip),
    ] {
        let result = session
            .extract(ArchiveSession::extract_request(
                name, archive, "out", format,
            ))
            .await;
        ArchiveSession::assert_code(&result, ErrorCode::ArchiveUnsafe);
    }
    assert!(!account.home().join("out").exists());
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn the_stated_caps_stop_an_extraction_and_leave_nothing() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive4");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    upload(
        &tree,
        "many.tar.gz",
        &TarMember::gzipped_archive(&[
            TarMember::file("a", &[1; 600]),
            TarMember::file("b", &[2; 600]),
            TarMember::file("c", &[3; 600]),
        ]),
    );

    let mut entries =
        ArchiveSession::extract_request(name, "many.tar.gz", "out", ArchiveFormat::TarGz);
    entries.max_entries = 2;
    ArchiveSession::assert_code(&session.extract(entries).await, ErrorCode::LimitReached);

    let mut bytes =
        ArchiveSession::extract_request(name, "many.tar.gz", "out", ArchiveFormat::TarGz);
    bytes.max_expanded_bytes = 1000;
    ArchiveSession::assert_code(&session.extract(bytes).await, ErrorCode::LimitReached);

    let mut zero =
        ArchiveSession::extract_request(name, "many.tar.gz", "out", ArchiveFormat::TarGz);
    zero.max_ratio = 0;
    ArchiveSession::assert_code(&session.extract(zero).await, ErrorCode::InvalidInput);

    assert!(!account.home().join("out").exists());
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn extracting_over_an_existing_file_reports_already_exists_and_changes_nothing() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive5");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    tree.file("out/b.txt", b"mine");
    upload(
        &tree,
        "a.tar.gz",
        &TarMember::gzipped_archive(&[
            TarMember::file("a.txt", b"new"),
            TarMember::file("b.txt", b"theirs"),
        ]),
    );

    let result = session
        .extract(ArchiveSession::extract_request(
            name,
            "a.tar.gz",
            "out",
            ArchiveFormat::TarGz,
        ))
        .await;

    ArchiveSession::assert_code(&result, ErrorCode::AlreadyExists);
    assert_eq!(
        std::fs::read(account.home().join("out/b.txt")).unwrap(),
        b"mine"
    );
    assert!(
        !account.home().join("out/a.txt").exists(),
        "the run took back what it wrote"
    );
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_symlink_planted_in_the_destination_is_not_followed() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive6");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    tree.directory("out");
    tree.symlink("out/sub", "/etc");
    upload(
        &tree,
        "a.tar.gz",
        &TarMember::gzipped_archive(&[TarMember::file("sub/maran-planted", b"x")]),
    );

    let result = session
        .extract(ArchiveSession::extract_request(
            name,
            "a.tar.gz",
            "out",
            ArchiveFormat::TarGz,
        ))
        .await;

    ArchiveSession::assert_code(&result, ErrorCode::AlreadyExists);
    assert!(!std::path::Path::new("/etc/maran-planted").exists());
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_zip_presented_as_tar_gz_and_a_fifo_as_the_archive_are_refused_without_hanging() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive7");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    upload(
        &tree,
        "really.zip",
        &ZipMember::archive(&[ZipMember::file("a", b"x")]),
    );
    tree.fifo("pipe.zip");

    let mismatch = session
        .extract(ArchiveSession::extract_request(
            name,
            "really.zip",
            "out",
            ArchiveFormat::TarGz,
        ))
        .await;
    let fifo = session
        .extract(ArchiveSession::extract_request(
            name,
            "pipe.zip",
            "out",
            ArchiveFormat::Zip,
        ))
        .await;

    ArchiveSession::assert_code(&mismatch, ErrorCode::ValidationFailed);
    ArchiveSession::assert_code(&fifo, ErrorCode::ValidationFailed);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_created_zip_and_tar_gz_extract_to_identical_trees() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive8");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    tree.file("site/index.html", b"<html></html>");
    tree.file("site/css/main.css", &vec![b'x'; 100_000]);
    tree.directory("site/empty");
    tree.file("notes.txt", b"remember");

    for (format, archive) in [
        (ArchiveFormat::Zip, "backup.zip"),
        (ArchiveFormat::TarGz, "backup.tar.gz"),
    ] {
        let created = session
            .create(ArchiveSession::create_request(
                name,
                &["site", "notes.txt"],
                archive,
                format,
            ))
            .await
            .unwrap();
        let size = std::fs::metadata(account.home().join(archive)).unwrap();
        assert_eq!(created.size_bytes, size.len());
        assert_eq!(size.uid(), account.ids().uid());

        let out = format!("restored-{archive}");
        session
            .extract(ArchiveSession::extract_request(name, archive, &out, format))
            .await
            .unwrap();
        let root = account.home().join(&out);
        assert_eq!(
            std::fs::read(root.join("site/index.html")).unwrap(),
            b"<html></html>"
        );
        assert_eq!(
            std::fs::read(root.join("site/css/main.css")).unwrap().len(),
            100_000
        );
        assert!(root.join("site/empty").is_dir());
        assert_eq!(std::fs::read(root.join("notes.txt")).unwrap(), b"remember");
    }
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn archiving_a_tree_with_a_link_or_into_itself_is_refused_and_an_existing_archive_is_kept() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("parchive9");
    let tree = PolygonHomeTree::new(&account);
    let session = ArchiveSession::start().await;
    let name = account.name().as_str();
    tree.file("site/a.txt", b"x");
    tree.symlink("site/passwd", "/etc/passwd");
    tree.file("clean/a.txt", b"x");
    tree.file("taken.zip", b"precious");

    let linked = session
        .create(ArchiveSession::create_request(
            name,
            &["site"],
            "out.zip",
            ArchiveFormat::Zip,
        ))
        .await;
    let into_itself = session
        .create(ArchiveSession::create_request(
            name,
            &["clean"],
            "clean/out.zip",
            ArchiveFormat::Zip,
        ))
        .await;
    let taken = session
        .create(ArchiveSession::create_request(
            name,
            &["clean"],
            "taken.zip",
            ArchiveFormat::Zip,
        ))
        .await;

    ArchiveSession::assert_code(&linked, ErrorCode::ValidationFailed);
    ArchiveSession::assert_code(&into_itself, ErrorCode::InvalidInput);
    ArchiveSession::assert_code(&taken, ErrorCode::AlreadyExists);
    assert!(!account.home().join("out.zip").exists());
    assert_eq!(
        std::fs::read(account.home().join("taken.zip")).unwrap(),
        b"precious"
    );
}
