//! Tests for opening the archive an extraction reads.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::symlink;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::open_archive;
use crate::files::archive::archive_fixtures::{TarMember, ZipMember, tar_gz_bytes, zip_bytes};
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::opened_archive::OpenedArchive;
use crate::files::model::archive_format::ArchiveFormat;

fn open(
    home: &std::path::Path,
    path: &str,
    format: ArchiveFormat,
) -> Result<OpenedArchive, ArchiveRefusal> {
    open_archive(
        home,
        &BrowsePath::parse(path).unwrap(),
        format,
        current_uid().unwrap(),
    )
}

#[test]
fn a_zip_and_a_tar_gz_open_as_what_they_are_and_report_their_length() {
    let home = tempfile::tempdir().unwrap();
    let zip = zip_bytes(&[ZipMember::file("a", b"x")]);
    let tar = tar_gz_bytes(&[TarMember::file("a", b"x")]);
    fs::create_dir(home.path().join("up")).unwrap();
    fs::write(home.path().join("up/a.zip"), &zip).unwrap();
    fs::write(home.path().join("b.tar.gz"), &tar).unwrap();

    assert_eq!(
        open(home.path(), "up/a.zip", ArchiveFormat::Zip)
            .unwrap()
            .length,
        zip.len() as u64
    );
    assert_eq!(
        open(home.path(), "b.tar.gz", ArchiveFormat::TarGz)
            .unwrap()
            .length,
        tar.len() as u64
    );
}

#[test]
fn a_zip_presented_as_tar_gz_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::write(
        home.path().join("a.zip"),
        zip_bytes(&[ZipMember::file("a", b"x")]),
    )
    .unwrap();

    assert_eq!(
        open(home.path(), "a.zip", ArchiveFormat::TarGz).unwrap_err(),
        ArchiveRefusal::Unreadable
    );
}

#[test]
fn a_tar_gz_presented_as_zip_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::write(
        home.path().join("a.tgz"),
        tar_gz_bytes(&[TarMember::file("a", b"x")]),
    )
    .unwrap();

    assert_eq!(
        open(home.path(), "a.tgz", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::Unreadable
    );
}

#[test]
fn a_file_shorter_than_its_magic_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("tiny"), b"P").unwrap();

    assert_eq!(
        open(home.path(), "tiny", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::Unreadable
    );
}

#[test]
fn an_absent_archive_is_not_found() {
    let home = tempfile::tempdir().unwrap();

    assert_eq!(
        open(home.path(), "nope.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::NotFound
    );
}

#[test]
fn a_symlink_named_as_the_archive_is_refused_not_followed() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("real.zip");
    fs::write(&target, zip_bytes(&[ZipMember::file("a", b"x")])).unwrap();
    symlink(&target, home.path().join("link.zip")).unwrap();

    assert_eq!(
        open(home.path(), "link.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::NotARegularFile
    );
}

#[test]
fn a_directory_named_as_the_archive_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("dir.zip")).unwrap();

    assert_eq!(
        open(home.path(), "dir.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::NotARegularFile
    );
}

#[test]
fn a_fifo_named_as_the_archive_is_refused_without_blocking() {
    let home = tempfile::tempdir().unwrap();
    let made = std::process::Command::new("mkfifo")
        .arg(home.path().join("pipe.zip"))
        .status()
        .unwrap();
    assert!(made.success(), "the test needs mkfifo to build its fixture");

    assert_eq!(
        open(home.path(), "pipe.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::NotARegularFile
    );
}

#[test]
fn the_home_itself_is_not_an_archive() {
    let home = tempfile::tempdir().unwrap();

    assert_eq!(
        open(home.path(), "", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::NotARegularFile
    );
}

#[test]
fn a_symlink_at_a_level_above_the_archive_is_refused() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::write(
        elsewhere.path().join("a.zip"),
        zip_bytes(&[ZipMember::file("a", b"x")]),
    )
    .unwrap();
    symlink(elsewhere.path(), home.path().join("up")).unwrap();

    assert_eq!(
        open(home.path(), "up/a.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::DirectoryUnusable
    );
}
