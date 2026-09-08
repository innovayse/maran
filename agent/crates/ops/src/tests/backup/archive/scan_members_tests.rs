//! The pre-scan: which member lists this agent will unpack, and which it will
//! not.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use crate::backup::recording_backup_host::RecordingBackupHost;

use super::*;

/// Runs the pre-scan over an archive whose members are `names`.
fn scan(names: &[&str]) -> Result<Vec<String>, BackupError> {
    let host = RecordingBackupHost::new();
    host.holds_members(names);

    scan_members(&host, &PathBuf::from("/var/backups/maran/alice/one.tar.gz"))
}

/// The layout this agent writes is accepted — the inverse control, without
/// which a scan mutated to refuse everything would pass every other test here.
#[test]
fn the_layout_this_agent_writes_is_accepted() {
    let names = [
        "manifest.json",
        "home/",
        "home/public/index.php",
        "home/.bashrc",
        "databases/",
        "databases/alice_shop.sql",
    ];

    assert!(scan(&names).is_ok());
}

/// A member outside the three prefixes is refused, and the refusal names it.
#[test]
fn a_member_outside_the_layout_is_refused_by_name() {
    let error = scan(&["manifest.json", "../../etc/cron.d/pwn"]).unwrap_err();

    match error {
        BackupError::UnexpectedArchiveMember { name } => {
            assert!(name.contains("etc/cron.d/pwn"), "{name}");
        }
        other => panic!("expected a refused member, got {other:?}"),
    }
}

/// A member that walks upwards from inside an allowed prefix is refused too —
/// the prefix rule alone accepts it, which is why the `..` check is its own.
#[test]
fn a_member_that_walks_up_from_inside_home_is_refused() {
    let error = scan(&["home/../../etc/cron.d/pwn"]).unwrap_err();

    assert!(matches!(error, BackupError::UnexpectedArchiveMember { .. }));
}

/// An absolute member is refused by the prefix rule, with no separate check for
/// a leading separator.
#[test]
fn an_absolute_member_is_refused() {
    let error = scan(&["/etc/shadow"]).unwrap_err();

    assert!(matches!(error, BackupError::UnexpectedArchiveMember { .. }));
}

/// A name that merely starts with the letters of an allowed prefix is not
/// inside it — `homework` is not `home/`.
#[test]
fn a_name_that_only_looks_like_a_prefix_is_refused() {
    let error = scan(&["homework/payload"]).unwrap_err();

    assert!(matches!(error, BackupError::UnexpectedArchiveMember { .. }));
}

/// A refused name reaches the error escaped, so a newline in it cannot split an
/// operator's log line into two entries.
#[test]
fn a_refused_member_name_carries_no_raw_control_character() {
    let rendered = refused_member_name("evil\nMAILTO=attacker\t\u{1b}[2J");

    assert!(!rendered.contains('\n'), "{rendered}");
    assert!(!rendered.contains('\t'), "{rendered}");
    assert!(!rendered.contains('\u{1b}'), "{rendered}");
    assert!(rendered.starts_with("evil"), "{rendered}");
}

/// A very long name is truncated, so an error message stays a log line.
#[test]
fn a_refused_member_name_is_truncated() {
    let rendered = refused_member_name(&"a".repeat(4096));

    assert_eq!(rendered.len(), 96);
}
