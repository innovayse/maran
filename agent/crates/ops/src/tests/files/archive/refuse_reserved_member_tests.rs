//! Tests for keeping an archive out of the trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;

use super::refuse_reserved_member;
use crate::files::archive::archive_member::ArchiveMember;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::member_kind::MemberKind;

fn member(name: &str) -> ArchiveMember {
    ArchiveMember {
        name: ArchiveMemberName::parse(name.as_bytes()).unwrap(),
        kind: MemberKind::File,
        executable: false,
        declared_size: 0,
    }
}

#[test]
fn a_member_inside_the_trash_is_refused_when_the_destination_is_the_home() {
    assert_eq!(
        refuse_reserved_member(true, &member(".maran-trash/entries/x/payload")).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert_eq!(
        refuse_reserved_member(true, &member(".maran-trash")).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
}

#[test]
fn the_same_name_below_another_destination_is_an_ordinary_directory() {
    assert!(refuse_reserved_member(false, &member(".maran-trash/x")).is_ok());
}

#[test]
fn other_members_of_the_home_and_lookalikes_are_fine() {
    assert!(refuse_reserved_member(true, &member("site/index.html")).is_ok());
    assert!(refuse_reserved_member(true, &member(".maran-trash-not/x")).is_ok());
    assert!(refuse_reserved_member(true, &member("sub/.maran-trash/x")).is_ok());
}
