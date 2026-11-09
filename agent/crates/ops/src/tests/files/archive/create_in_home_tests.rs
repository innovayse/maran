//! Tests for creating an archive in a temporary home, as the test's own uid.
//!
//! Needs no root. Archives are read back with the extraction itself, so a created
//! archive is judged by the same walk a customer's upload is.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, PermissionsExt, symlink};
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::create_in_home;
use crate::files::archive::archive_fixtures::tree_of;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::extract_in_home::extract_in_home;
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;
use crate::files::model::extract_archive_input::ExtractArchiveInput;

const TEMPORARY: &str = ".maran-archive-test";

fn create_with(
    home: &Path,
    sources: &[&str],
    destination: &str,
    format: ArchiveFormat,
    bounds: (u64, u64),
) -> Result<ArchiveReport, ArchiveRefusal> {
    let input = CreateArchiveInput::new(
        AccountName::parse("alice").unwrap(),
        sources
            .iter()
            .map(|s| BrowsePath::parse(s).unwrap())
            .collect(),
        BrowsePath::parse(destination).unwrap(),
        format,
        CreateBounds::parse(bounds.0, bounds.1).unwrap(),
    )
    .unwrap();
    create_in_home(home, &input, TEMPORARY, current_uid().unwrap())
}

fn create(
    home: &Path,
    sources: &[&str],
    destination: &str,
    format: ArchiveFormat,
) -> Result<ArchiveReport, ArchiveRefusal> {
    create_with(home, sources, destination, format, (1000, 1 << 30))
}

fn extract(
    home: &Path,
    archive: &str,
    destination: &str,
    format: ArchiveFormat,
) -> Result<ArchiveReport, ArchiveRefusal> {
    let input = ExtractArchiveInput {
        account: AccountName::parse("alice").unwrap(),
        archive: BrowsePath::parse(archive).unwrap(),
        destination: BrowsePath::parse(destination).unwrap(),
        format,
        bounds: ExtractBounds::parse(1000, 1 << 30, 1000).unwrap(),
    };
    extract_in_home(home, &input, current_uid().unwrap())
}

/// A home with a small site, a notes file and an empty directory.
fn populated_home() -> tempfile::TempDir {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("site/css")).unwrap();
    fs::create_dir_all(home.path().join("site/empty")).unwrap();
    fs::write(home.path().join("site/index.html"), b"<html></html>").unwrap();
    fs::write(home.path().join("site/css/main.css"), vec![b'x'; 70_000]).unwrap();
    fs::write(home.path().join("notes.txt"), b"remember").unwrap();
    home
}

#[test]
fn a_created_zip_and_tar_gz_extract_to_identical_trees() {
    for (format, name) in [
        (ArchiveFormat::Zip, "backup.zip"),
        (ArchiveFormat::TarGz, "backup.tar.gz"),
    ] {
        let home = populated_home();

        let report = create(home.path(), &["site", "notes.txt"], name, format).unwrap();
        extract(home.path(), name, "restored", format).unwrap();

        let original = tree_of(&home.path().join("site"));
        let restored = tree_of(&home.path().join("restored/site"));
        assert_eq!(original, restored, "{format:?}");
        assert_eq!(
            fs::read(home.path().join("restored/notes.txt")).unwrap(),
            b"remember"
        );
        assert!(
            home.path().join("restored/site/empty").is_dir(),
            "an empty directory survives"
        );
        assert_eq!(
            report.entries, 6,
            "{format:?}: site, css, empty, three files and notes"
        );
        assert_eq!(
            report.bytes,
            fs::metadata(home.path().join(name)).unwrap().len()
        );
    }
}

#[test]
fn a_member_name_is_the_path_below_the_common_parent_of_the_sources() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("sites/a/public")).unwrap();
    fs::write(home.path().join("sites/a/public/index.html"), b"x").unwrap();
    fs::write(home.path().join("sites/a/notes.txt"), b"n").unwrap();

    create(
        home.path(),
        &["sites/a/public", "sites/a/notes.txt"],
        "out.zip",
        ArchiveFormat::Zip,
    )
    .unwrap();
    extract(home.path(), "out.zip", "back", ArchiveFormat::Zip).unwrap();

    assert_eq!(
        fs::read(home.path().join("back/public/index.html")).unwrap(),
        b"x"
    );
    assert_eq!(fs::read(home.path().join("back/notes.txt")).unwrap(), b"n");
}

#[test]
fn the_archive_is_mode_0644_and_no_temporary_is_left() {
    let home = populated_home();

    create(home.path(), &["notes.txt"], "n.zip", ArchiveFormat::Zip).unwrap();

    assert_eq!(
        fs::metadata(home.path().join("n.zip")).unwrap().mode() & 0o7777,
        0o644
    );
    assert!(!home.path().join(TEMPORARY).exists());
}

#[test]
fn an_executable_stays_executable_and_every_other_mode_is_normalised() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("tool"), b"x").unwrap();
    fs::write(home.path().join("loose"), b"x").unwrap();
    fs::set_permissions(home.path().join("tool"), fs::Permissions::from_mode(0o750)).unwrap();
    fs::set_permissions(home.path().join("loose"), fs::Permissions::from_mode(0o666)).unwrap();

    for (format, name) in [
        (ArchiveFormat::Zip, "a.zip"),
        (ArchiveFormat::TarGz, "a.tgz"),
    ] {
        create(home.path(), &["tool", "loose"], name, format).unwrap();
        extract(home.path(), name, &format!("out-{name}"), format).unwrap();

        let out = home.path().join(format!("out-{name}"));
        assert_eq!(
            fs::metadata(out.join("tool")).unwrap().mode() & 0o7777,
            0o755,
            "{format:?}"
        );
        assert_eq!(
            fs::metadata(out.join("loose")).unwrap().mode() & 0o7777,
            0o644,
            "{format:?}"
        );
    }
}

#[test]
fn archiving_a_tree_with_a_link_is_refused_and_leaves_no_archive_and_no_temporary() {
    for format in [ArchiveFormat::Zip, ArchiveFormat::TarGz] {
        let home = populated_home();
        symlink("/etc/passwd", home.path().join("site/css/passwd")).unwrap();
        let before = tree_of(home.path());

        let refusal = create(home.path(), &["site"], "out.archive", format).unwrap_err();

        assert_eq!(refusal, ArchiveRefusal::UnsupportedEntry, "{format:?}");
        assert_eq!(tree_of(home.path()), before, "{format:?}");
    }
}

#[test]
fn archiving_a_tree_with_a_fifo_is_refused_without_blocking() {
    let home = populated_home();
    let made = std::process::Command::new("mkfifo")
        .arg(home.path().join("site/pipe"))
        .status()
        .unwrap();
    assert!(made.success(), "the test needs mkfifo to build its fixture");

    assert_eq!(
        create(home.path(), &["site"], "out.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::UnsupportedEntry
    );
    assert!(!home.path().join("out.zip").exists());
}

#[test]
fn an_existing_destination_is_never_replaced() {
    let home = populated_home();
    fs::write(home.path().join("out.zip"), b"precious").unwrap();

    let refusal = create(home.path(), &["notes.txt"], "out.zip", ArchiveFormat::Zip).unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert_eq!(fs::read(home.path().join("out.zip")).unwrap(), b"precious");
    assert!(!home.path().join(TEMPORARY).exists());
}

#[test]
fn a_destination_that_is_a_symlink_is_a_taken_name_and_its_target_survives() {
    let home = populated_home();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("precious");
    fs::write(&target, b"keep").unwrap();
    symlink(&target, home.path().join("out.zip")).unwrap();

    let refusal = create(home.path(), &["notes.txt"], "out.zip", ArchiveFormat::Zip).unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert_eq!(fs::read(&target).unwrap(), b"keep");
}

#[test]
fn a_destination_directory_that_does_not_exist_is_refused() {
    let home = populated_home();

    assert_eq!(
        create(
            home.path(),
            &["notes.txt"],
            "absent/out.zip",
            ArchiveFormat::Zip
        )
        .unwrap_err(),
        ArchiveRefusal::DirectoryUnusable
    );
}

#[test]
fn sources_over_the_entry_cap_are_refused_and_leave_nothing() {
    let home = populated_home();
    let before = tree_of(home.path());

    let refusal = create_with(
        home.path(),
        &["site"],
        "out.zip",
        ArchiveFormat::Zip,
        (3, 1 << 30),
    )
    .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::EntryLimit);
    assert_eq!(tree_of(home.path()), before);
}

#[test]
fn sources_over_the_byte_cap_are_refused_and_leave_nothing() {
    for format in [ArchiveFormat::Zip, ArchiveFormat::TarGz] {
        let home = populated_home();
        let before = tree_of(home.path());

        let refusal =
            create_with(home.path(), &["site"], "out.archive", format, (1000, 1000)).unwrap_err();

        assert_eq!(refusal, ArchiveRefusal::ByteLimit, "{format:?}");
        assert_eq!(
            tree_of(home.path()),
            before,
            "{format:?}: no archive and no temporary"
        );
    }
}

#[test]
fn an_absent_source_is_not_found() {
    let home = populated_home();

    assert_eq!(
        create(home.path(), &["nope"], "out.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::NotFound
    );
}

#[test]
fn a_symlink_standing_at_a_level_above_a_source_is_not_followed() {
    let home = populated_home();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::write(elsewhere.path().join("a.txt"), b"x").unwrap();
    symlink(elsewhere.path(), home.path().join("up")).unwrap();

    assert_eq!(
        create(home.path(), &["up/a.txt"], "out.zip", ArchiveFormat::Zip).unwrap_err(),
        ArchiveRefusal::DirectoryUnusable
    );
}

#[test]
fn a_large_file_is_archived_whole() {
    let home = tempfile::tempdir().unwrap();
    let content: Vec<u8> = (0..300_000_u32).flat_map(u32::to_le_bytes).collect();
    fs::write(home.path().join("big.bin"), &content).unwrap();

    for (format, name) in [
        (ArchiveFormat::Zip, "big.zip"),
        (ArchiveFormat::TarGz, "big.tgz"),
    ] {
        create(home.path(), &["big.bin"], name, format).unwrap();
        extract(home.path(), name, &format!("out-{name}"), format).unwrap();

        assert_eq!(
            fs::read(home.path().join(format!("out-{name}/big.bin"))).unwrap(),
            content,
            "{format:?}"
        );
    }
}

#[test]
fn a_source_inside_the_trash_is_refused_and_no_archive_is_made() {
    let home = populated_home();
    fs::create_dir_all(home.path().join(".maran-trash/entries/x")).unwrap();
    fs::write(home.path().join(".maran-trash/entries/x/payload"), b"x").unwrap();

    for source in [".maran-trash", ".maran-trash/entries/x/payload"] {
        assert_eq!(
            create(home.path(), &[source], "out.zip", ArchiveFormat::Zip).unwrap_err(),
            ArchiveRefusal::UnsafeName,
            "{source}"
        );
    }
    assert!(!home.path().join("out.zip").exists());
}

#[test]
fn a_destination_inside_the_trash_is_refused_and_nothing_is_written_there() {
    let home = populated_home();
    fs::create_dir_all(home.path().join(".maran-trash/entries")).unwrap();

    assert_eq!(
        create(
            home.path(),
            &["notes.txt"],
            ".maran-trash/entries/out.zip",
            ArchiveFormat::Zip
        )
        .unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert!(
        fs::read_dir(home.path().join(".maran-trash/entries"))
            .unwrap()
            .next()
            .is_none()
    );
}

#[test]
fn the_trash_name_deeper_in_a_destination_is_an_ordinary_directory() {
    let home = populated_home();
    fs::create_dir_all(home.path().join("sub/.maran-trash")).unwrap();

    create(
        home.path(),
        &["notes.txt"],
        "sub/.maran-trash/out.zip",
        ArchiveFormat::Zip,
    )
    .unwrap();

    assert!(home.path().join("sub/.maran-trash/out.zip").is_file());
}
