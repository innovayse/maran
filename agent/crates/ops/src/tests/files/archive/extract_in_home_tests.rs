//! Tests for extracting an archive into a temporary home, as the test's own uid.
//!
//! Needs no root. What is observed is the tree left behind, because that is the
//! whole contract: what was written, what was refused, and what a refusal left.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, symlink};
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::extract_in_home;
use crate::files::archive::archive_fixtures::{
    TAR_CHARACTER_DEVICE, TAR_FIFO, TAR_SYMLINK, TarMember, ZipMember, set_zip_declared_size,
    tar_gz_bytes, tree_of, zip_bytes,
};
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::extract_archive_input::ExtractArchiveInput;

/// Caps generous enough that only the test that wants a cap meets one.
const ENTRIES: u64 = 1000;
const BYTES: u64 = 1 << 30;
const RATIO: u32 = 1000;

fn extract_with(
    home: &Path,
    format: ArchiveFormat,
    destination: &str,
    bounds: (u64, u64, u32),
) -> Result<ArchiveReport, ArchiveRefusal> {
    let archive_name = match format {
        ArchiveFormat::Zip => "upload.zip",
        ArchiveFormat::TarGz => "upload.tar.gz",
    };
    let input = ExtractArchiveInput {
        account: AccountName::parse("alice").unwrap(),
        archive: BrowsePath::parse(archive_name).unwrap(),
        destination: BrowsePath::parse(destination).unwrap(),
        format,
        bounds: ExtractBounds::parse(bounds.0, bounds.1, bounds.2).unwrap(),
    };
    extract_in_home(home, &input, current_uid().unwrap())
}

fn extract(
    home: &Path,
    format: ArchiveFormat,
    destination: &str,
) -> Result<ArchiveReport, ArchiveRefusal> {
    extract_with(home, format, destination, (ENTRIES, BYTES, RATIO))
}

fn put_zip(home: &Path, members: &[ZipMember]) {
    fs::write(home.join("upload.zip"), zip_bytes(members)).unwrap();
}

fn put_tar_gz(home: &Path, members: &[TarMember]) {
    fs::write(home.join("upload.tar.gz"), tar_gz_bytes(members)).unwrap();
}

/// The tree below the home, without the archive that was dropped into it.
fn tree_without_uploads(home: &Path) -> std::collections::BTreeMap<String, Option<Vec<u8>>> {
    let mut tree = tree_of(home);
    tree.remove("upload.zip");
    tree.remove("upload.tar.gz");
    tree
}

fn mode_of(path: &Path) -> u32 {
    fs::metadata(path).unwrap().mode() & 0o7777
}

#[test]
fn a_zip_is_extracted_into_a_new_destination_with_its_directories_and_content() {
    let home = tempfile::tempdir().unwrap();
    put_zip(
        home.path(),
        &[
            ZipMember::directory("site"),
            ZipMember::file("site/index.html", b"<html></html>"),
            ZipMember::file("site/css/main.css", b"body{}"),
            ZipMember::file("empty.txt", b""),
        ],
    );

    let report = extract(home.path(), ArchiveFormat::Zip, "out/deep").unwrap();

    let tree = tree_without_uploads(home.path());
    assert_eq!(
        tree["out/deep/site/index.html"].as_deref(),
        Some(&b"<html></html>"[..])
    );
    assert_eq!(
        tree["out/deep/site/css/main.css"].as_deref(),
        Some(&b"body{}"[..])
    );
    assert_eq!(tree["out/deep/empty.txt"].as_deref(), Some(&b""[..]));
    assert_eq!(
        tree["out/deep/site/css"], None,
        "a directory the archive only implied"
    );
    assert_eq!(report.bytes, 13 + 6);
    assert_eq!(
        report.entries, 4,
        "members, not the levels of the destination"
    );
}

#[test]
fn a_tar_gz_is_extracted_the_same_way() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::directory("./"),
            TarMember::directory("./site/"),
            TarMember::file("./site/index.html", b"<html></html>"),
            TarMember::file("site/css/main.css", b"body{}"),
        ],
    );

    let report = extract(home.path(), ArchiveFormat::TarGz, "").unwrap();

    let tree = tree_without_uploads(home.path());
    assert_eq!(
        tree["site/index.html"].as_deref(),
        Some(&b"<html></html>"[..])
    );
    assert_eq!(tree["site/css/main.css"].as_deref(), Some(&b"body{}"[..]));
    assert_eq!(report.bytes, 19);
}

#[test]
fn an_archive_with_no_members_still_brings_the_destination_into_being() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(home.path(), &[]);

    let report = extract(home.path(), ArchiveFormat::TarGz, "made/here").unwrap();

    assert!(home.path().join("made/here").is_dir());
    assert_eq!((report.entries, report.bytes), (0, 0));
}

#[test]
fn an_archive_with_a_dotdot_member_is_refused_before_anything_is_written() {
    for format in [ArchiveFormat::Zip, ArchiveFormat::TarGz] {
        let home = tempfile::tempdir().unwrap();
        match format {
            ArchiveFormat::Zip => put_zip(
                home.path(),
                &[
                    ZipMember::file("fine.txt", b"x"),
                    ZipMember::file("a/../../escape.txt", b"x"),
                ],
            ),
            ArchiveFormat::TarGz => put_tar_gz(
                home.path(),
                &[
                    TarMember::file("fine.txt", b"x"),
                    TarMember::file("a/../../escape.txt", b"x"),
                ],
            ),
        }

        let refusal = extract(home.path(), format, "out").unwrap_err();

        assert_eq!(refusal, ArchiveRefusal::UnsafeName, "{format:?}");
        assert!(
            tree_without_uploads(home.path()).is_empty(),
            "{format:?}: nothing, not even the destination"
        );
        assert!(!home.path().parent().unwrap().join("escape.txt").exists());
    }
}

#[test]
fn an_archive_with_an_absolute_member_is_refused() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[TarMember::file("/tmp/maran-absolute-test", b"x")],
    );

    assert_eq!(
        extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert!(!Path::new("/tmp/maran-absolute-test").exists());
}

#[test]
fn an_archive_with_a_link_member_is_refused_as_a_whole_and_nothing_is_written() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("first.txt", b"x"),
            TarMember::link("l", TAR_SYMLINK, "/etc/passwd"),
        ],
    );
    assert_eq!(
        extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err(),
        ArchiveRefusal::LinkMember
    );
    assert!(tree_without_uploads(home.path()).is_empty());

    put_zip(
        home.path(),
        &[
            ZipMember::file("first.txt", b"x"),
            ZipMember::with_mode("l", b"/etc/passwd", 0o120_777),
        ],
    );
    assert_eq!(
        extract(home.path(), ArchiveFormat::Zip, "out").unwrap_err(),
        ArchiveRefusal::LinkMember
    );
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn an_archive_with_a_device_or_fifo_member_is_refused() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("a", b"x"),
            TarMember::special("d", TAR_CHARACTER_DEVICE),
        ],
    );
    assert_eq!(
        extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err(),
        ArchiveRefusal::SpecialMember
    );

    put_tar_gz(home.path(), &[TarMember::special("p", TAR_FIFO)]);
    assert_eq!(
        extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err(),
        ArchiveRefusal::SpecialMember
    );
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn an_archive_over_the_stated_entry_cap_is_refused_and_nothing_is_written() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("a", b"x"),
            TarMember::file("b", b"x"),
            TarMember::file("c", b"x"),
        ],
    );

    let refusal =
        extract_with(home.path(), ArchiveFormat::TarGz, "out", (2, BYTES, RATIO)).unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::EntryLimit);
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn an_archive_over_the_stated_byte_cap_is_refused_and_nothing_is_written() {
    let home = tempfile::tempdir().unwrap();
    put_zip(
        home.path(),
        &[
            ZipMember::file("a", &vec![1_u8; 600]),
            ZipMember::file("b", &vec![2_u8; 600]),
        ],
    );

    let refusal = extract_with(
        home.path(),
        ArchiveFormat::Zip,
        "out",
        (ENTRIES, 1000, RATIO),
    )
    .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::ByteLimit);
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn an_archive_over_the_stated_ratio_is_refused_and_nothing_is_written() {
    let home = tempfile::tempdir().unwrap();
    put_zip(
        home.path(),
        &[ZipMember::file("zeros", &vec![0_u8; 4 * 1024 * 1024])],
    );

    let refusal =
        extract_with(home.path(), ArchiveFormat::Zip, "out", (ENTRIES, BYTES, 50)).unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::BombRatio);
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn a_member_whose_header_understates_its_size_fails_the_run_and_leaves_nothing() {
    let home = tempfile::tempdir().unwrap();
    let mut bytes = zip_bytes(&[ZipMember::with_mode("a", &vec![5_u8; 500], 0o100_644)]);
    set_zip_declared_size(&mut bytes, 10);
    fs::write(home.path().join("upload.zip"), bytes).unwrap();

    // The first pass reads headers only and sees nothing wrong; the second reads
    // the bytes, which are not what the header said, after the destination and
    // the file exist. The run must fail and take both away again.
    let outcome = extract(home.path(), ArchiveFormat::Zip, "fresh/dest");

    assert!(outcome.is_err(), "{outcome:?}");
    assert!(
        tree_without_uploads(home.path()).is_empty(),
        "the file and the levels it made are gone"
    );
}

#[test]
fn extracting_over_an_existing_file_reports_already_exists_and_changes_nothing() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("out")).unwrap();
    fs::write(home.path().join("out/b.txt"), b"mine").unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("a.txt", b"new"),
            TarMember::file("b.txt", b"theirs"),
            TarMember::file("c.txt", b"new"),
        ],
    );
    let before = tree_without_uploads(home.path());

    let refusal = extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert_eq!(
        tree_without_uploads(home.path()),
        before,
        "a.txt was written and then taken away again"
    );
    assert_eq!(fs::read(home.path().join("out/b.txt")).unwrap(), b"mine");
}

#[test]
fn extracting_the_same_archive_twice_is_refused_the_second_time_and_leaves_the_first_alone() {
    let home = tempfile::tempdir().unwrap();
    put_zip(home.path(), &[ZipMember::file("a.txt", b"one")]);
    extract(home.path(), ArchiveFormat::Zip, "out").unwrap();
    let after_first = tree_without_uploads(home.path());

    assert_eq!(
        extract(home.path(), ArchiveFormat::Zip, "out").unwrap_err(),
        ArchiveRefusal::AlreadyExists
    );
    assert_eq!(tree_without_uploads(home.path()), after_first);
}

#[test]
fn extracting_into_an_existing_directory_merges_without_overwriting() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("out/site")).unwrap();
    fs::write(home.path().join("out/site/old.txt"), b"old").unwrap();
    put_zip(
        home.path(),
        &[
            ZipMember::directory("site"),
            ZipMember::file("site/new.txt", b"new"),
        ],
    );

    extract(home.path(), ArchiveFormat::Zip, "out").unwrap();

    assert_eq!(
        fs::read(home.path().join("out/site/old.txt")).unwrap(),
        b"old"
    );
    assert_eq!(
        fs::read(home.path().join("out/site/new.txt")).unwrap(),
        b"new"
    );
}

#[test]
fn a_member_directory_whose_name_is_held_by_a_file_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("out")).unwrap();
    fs::write(home.path().join("out/site"), b"a file").unwrap();
    put_zip(
        home.path(),
        &[
            ZipMember::directory("site"),
            ZipMember::file("site/x", b"x"),
        ],
    );

    assert_eq!(
        extract(home.path(), ArchiveFormat::Zip, "out").unwrap_err(),
        ArchiveRefusal::AlreadyExists
    );
    assert_eq!(fs::read(home.path().join("out/site")).unwrap(), b"a file");
}

#[test]
fn a_symlink_standing_where_a_member_directory_belongs_is_not_followed() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("out")).unwrap();
    symlink(elsewhere.path(), home.path().join("out/site")).unwrap();
    put_tar_gz(home.path(), &[TarMember::file("site/planted.txt", b"x")]);

    let refusal = extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert!(
        fs::read_dir(elsewhere.path()).unwrap().next().is_none(),
        "nothing went through the link"
    );
}

#[test]
fn a_symlink_standing_where_a_member_file_belongs_is_not_written_through() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("precious.txt");
    fs::write(&target, b"keep").unwrap();
    fs::create_dir(home.path().join("out")).unwrap();
    symlink(&target, home.path().join("out/a.txt")).unwrap();
    put_tar_gz(home.path(), &[TarMember::file("a.txt", b"overwritten")]);

    let refusal = extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert_eq!(fs::read(&target).unwrap(), b"keep");
}

#[test]
fn a_symlink_as_a_level_of_the_destination_is_not_followed() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    symlink(elsewhere.path(), home.path().join("out")).unwrap();
    put_tar_gz(home.path(), &[TarMember::file("a.txt", b"x")]);

    assert!(extract(home.path(), ArchiveFormat::TarGz, "out/inner").is_err());
    assert!(fs::read_dir(elsewhere.path()).unwrap().next().is_none());
}

#[test]
fn a_setuid_or_world_writable_mode_in_an_archive_has_no_effect() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file_with_mode("setuid", b"x", 0o4755),
            TarMember::file_with_mode("loose", b"x", 0o666),
            TarMember::file_with_mode("tool", b"x", 0o750),
            TarMember::file_with_mode("private", b"x", 0o600),
        ],
    );

    extract(home.path(), ArchiveFormat::TarGz, "").unwrap();

    assert_eq!(
        mode_of(&home.path().join("setuid")),
        0o755,
        "executable, but not setuid"
    );
    assert_eq!(mode_of(&home.path().join("loose")), 0o644);
    assert_eq!(mode_of(&home.path().join("tool")), 0o755);
    assert_eq!(mode_of(&home.path().join("private")), 0o644);
}

#[test]
fn directories_the_archive_creates_are_0755_whatever_it_says() {
    let home = tempfile::tempdir().unwrap();
    put_zip(
        home.path(),
        &[
            ZipMember::with_mode("open/", b"", 0o040_777),
            ZipMember::file("open/x", b"x"),
        ],
    );

    extract(home.path(), ArchiveFormat::Zip, "out").unwrap();

    assert_eq!(mode_of(&home.path().join("out/open")), 0o755);
}

#[test]
fn a_failed_extraction_removes_only_what_it_created() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("existing")).unwrap();
    // The second member collides, so the first has already been written.
    fs::create_dir_all(home.path().join("existing/new/sub")).unwrap();
    fs::write(home.path().join("existing/new/sub/taken.txt"), b"mine").unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("first.txt", b"x"),
            TarMember::directory("dir/"),
            TarMember::file("dir/inner.txt", b"x"),
            TarMember::file("sub/taken.txt", b"x"),
        ],
    );
    let before = tree_without_uploads(home.path());

    let refusal = extract(home.path(), ArchiveFormat::TarGz, "existing/new").unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert_eq!(
        tree_without_uploads(home.path()),
        before,
        "only what the run made is gone"
    );
}

#[test]
fn an_archive_the_first_pass_refuses_never_brings_the_destination_into_being() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("a.txt", b"x"),
            TarMember::file("a.txt/b", b"x"),
        ],
    );

    let refusal = extract(home.path(), ArchiveFormat::TarGz, "fresh/dest").unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::FileAsDirectory);
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn a_zip_presented_as_tar_gz_and_a_tar_gz_presented_as_zip_are_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::write(
        home.path().join("upload.tar.gz"),
        zip_bytes(&[ZipMember::file("a", b"x")]),
    )
    .unwrap();
    assert_eq!(
        extract(home.path(), ArchiveFormat::TarGz, "out").unwrap_err(),
        ArchiveRefusal::Unreadable
    );

    fs::write(
        home.path().join("upload.zip"),
        tar_gz_bytes(&[TarMember::file("a", b"x")]),
    )
    .unwrap();
    assert_eq!(
        extract(home.path(), ArchiveFormat::Zip, "out").unwrap_err(),
        ArchiveRefusal::Unreadable
    );
    assert!(!home.path().join("out").exists());
}

#[test]
fn an_archive_that_does_not_exist_is_not_found() {
    let home = tempfile::tempdir().unwrap();

    assert_eq!(
        extract(home.path(), ArchiveFormat::Zip, "out").unwrap_err(),
        ArchiveRefusal::NotFound
    );
}

#[test]
fn a_home_the_account_does_not_own_is_refused() {
    let home = tempfile::tempdir().unwrap();
    put_zip(home.path(), &[ZipMember::file("a", b"x")]);
    let input = ExtractArchiveInput {
        account: AccountName::parse("alice").unwrap(),
        archive: BrowsePath::parse("upload.zip").unwrap(),
        destination: BrowsePath::parse("out").unwrap(),
        format: ArchiveFormat::Zip,
        bounds: ExtractBounds::parse(10, 1000, 10).unwrap(),
    };
    let someone_else = current_uid().unwrap() + 1;

    assert_eq!(
        extract_in_home(home.path(), &input, someone_else).unwrap_err(),
        ArchiveRefusal::HomeUnusable
    );
}

#[test]
fn an_archive_that_would_write_into_the_trash_of_the_home_is_refused_before_anything_is_written() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(
        home.path(),
        &[
            TarMember::file("fine.txt", b"x"),
            TarMember::file(".maran-trash/entries/x/payload", b"x"),
        ],
    );

    assert_eq!(
        extract(home.path(), ArchiveFormat::TarGz, "").unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn the_same_member_name_below_another_destination_is_an_ordinary_directory() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(home.path(), &[TarMember::file(".maran-trash/x", b"x")]);

    extract(home.path(), ArchiveFormat::TarGz, "elsewhere").unwrap();

    assert!(home.path().join("elsewhere/.maran-trash/x").is_file());
}

#[test]
fn a_destination_inside_the_trash_is_refused_and_nothing_is_created() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(home.path(), &[TarMember::file("a.txt", b"x")]);

    for destination in [".maran-trash", ".maran-trash/entries/x/payload"] {
        assert_eq!(
            extract(home.path(), ArchiveFormat::TarGz, destination).unwrap_err(),
            ArchiveRefusal::UnsafeName,
            "{destination}"
        );
    }
    assert!(tree_without_uploads(home.path()).is_empty());
}

#[test]
fn an_archive_standing_inside_the_trash_is_refused_before_it_is_opened() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join(".maran-trash/entries")).unwrap();
    fs::write(
        home.path().join(".maran-trash/entries/upload.tar.gz"),
        tar_gz_bytes(&[TarMember::file("a.txt", b"x")]),
    )
    .unwrap();
    let input = ExtractArchiveInput {
        account: AccountName::parse("alice").unwrap(),
        archive: BrowsePath::parse(".maran-trash/entries/upload.tar.gz").unwrap(),
        destination: BrowsePath::parse("out").unwrap(),
        format: ArchiveFormat::TarGz,
        bounds: ExtractBounds::parse(ENTRIES, BYTES, RATIO).unwrap(),
    };

    assert_eq!(
        extract_in_home(home.path(), &input, current_uid().unwrap()).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert!(!home.path().join("out").exists());
}

#[test]
fn the_trash_name_deeper_in_the_destination_is_an_ordinary_directory() {
    let home = tempfile::tempdir().unwrap();
    put_tar_gz(home.path(), &[TarMember::file("a.txt", b"x")]);

    extract(home.path(), ArchiveFormat::TarGz, "sub/.maran-trash").unwrap();

    assert!(home.path().join("sub/.maran-trash/a.txt").is_file());
}
