//! Tests for the names an archive has used.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;

use super::MemberRegistry;
use crate::files::archive::archive_member::ArchiveMember;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::member_kind::MemberKind;

fn member(name: &str, kind: MemberKind) -> ArchiveMember {
    ArchiveMember {
        name: ArchiveMemberName::parse(name.as_bytes()).unwrap(),
        kind,
        executable: false,
        declared_size: 0,
    }
}

#[test]
fn distinct_names_are_admitted() {
    let mut registry = MemberRegistry::new();

    registry.admit(&member("a", MemberKind::Directory)).unwrap();
    registry
        .admit(&member("a/b.txt", MemberKind::File))
        .unwrap();
    registry
        .admit(&member("a/c.txt", MemberKind::File))
        .unwrap();
    registry.admit(&member("d.txt", MemberKind::File)).unwrap();
}

#[test]
fn a_file_named_twice_is_refused() {
    let mut registry = MemberRegistry::new();
    registry.admit(&member("a.txt", MemberKind::File)).unwrap();

    assert_eq!(
        registry
            .admit(&member("a.txt", MemberKind::File))
            .unwrap_err(),
        ArchiveRefusal::DuplicateMember
    );
}

#[test]
fn a_directory_named_twice_is_the_same_directory() {
    let mut registry = MemberRegistry::new();
    registry.admit(&member("a", MemberKind::Directory)).unwrap();

    registry.admit(&member("a", MemberKind::Directory)).unwrap();
}

#[test]
fn a_directory_that_files_were_already_written_into_is_not_a_collision() {
    let mut registry = MemberRegistry::new();
    registry
        .admit(&member("a/b/c.txt", MemberKind::File))
        .unwrap();

    registry.admit(&member("a", MemberKind::Directory)).unwrap();
    registry
        .admit(&member("a/b", MemberKind::Directory))
        .unwrap();
}

#[test]
fn a_member_below_a_file_is_refused() {
    let mut registry = MemberRegistry::new();
    registry.admit(&member("a", MemberKind::File)).unwrap();

    assert_eq!(
        registry
            .admit(&member("a/b.txt", MemberKind::File))
            .unwrap_err(),
        ArchiveRefusal::FileAsDirectory
    );
    assert_eq!(
        registry
            .admit(&member("a/b/c", MemberKind::Directory))
            .unwrap_err(),
        ArchiveRefusal::FileAsDirectory
    );
}

#[test]
fn a_directory_over_a_file_and_a_file_over_a_directory_are_both_refused() {
    let mut registry = MemberRegistry::new();
    registry.admit(&member("a", MemberKind::File)).unwrap();
    registry.admit(&member("b", MemberKind::Directory)).unwrap();

    assert_eq!(
        registry
            .admit(&member("a", MemberKind::Directory))
            .unwrap_err(),
        ArchiveRefusal::FileAsDirectory
    );
    assert_eq!(
        registry.admit(&member("b", MemberKind::File)).unwrap_err(),
        ArchiveRefusal::FileAsDirectory
    );
}

#[test]
fn a_name_that_merely_starts_like_another_is_not_below_it() {
    let mut registry = MemberRegistry::new();
    registry.admit(&member("a", MemberKind::File)).unwrap();

    registry
        .admit(&member("ab/c.txt", MemberKind::File))
        .unwrap();
}
