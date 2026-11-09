//! Tests for the walk over a gzip-compressed tar archive.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Read;

use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;

use super::walk_tar_gz_members;
use crate::files::archive::archive_fixtures::{
    TAR_CHARACTER_DEVICE, TAR_FIFO, TAR_HARDLINK, TAR_SPARSE, TAR_SYMLINK, TarMember, archive_file,
    gzip, tar_bytes, tar_gz_bytes,
};
use crate::files::archive::archive_member::ArchiveMember;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::data_access::DataAccess;
use crate::files::archive::expansion_budget::ExpansionBudget;
use crate::files::archive::member_kind::MemberKind;

/// What a walk saw: the member and, when read, its bytes.
type Seen = Vec<(String, MemberKind, bool, u64, Vec<u8>)>;

fn walk(
    bytes: &[u8],
    access: DataAccess,
    entries: u64,
    expanded: u64,
    ratio: u32,
) -> Result<Seen, ArchiveRefusal> {
    let (_keep, file, length) = archive_file(bytes);
    let mut budget = ExpansionBudget::for_extraction(
        &ExtractBounds::parse(entries, expanded, ratio).unwrap(),
        length,
    );
    let mut seen: Seen = Vec::new();
    walk_tar_gz_members(
        &file,
        access,
        &mut budget,
        &mut |member: &ArchiveMember, data: &mut dyn Read| {
            let mut content = Vec::new();
            data.read_to_end(&mut content).unwrap();
            seen.push((
                member.name.as_str().to_owned(),
                member.kind,
                member.executable,
                member.declared_size,
                content,
            ));
            Ok(())
        },
    )?;
    Ok(seen)
}

fn walk_all(members: &[TarMember]) -> Result<Seen, ArchiveRefusal> {
    walk(&tar_gz_bytes(members), DataAccess::Read, 100, 1 << 30, 1000)
}

#[test]
fn an_ordinary_tar_gz_yields_its_members_with_kinds_sizes_and_content() {
    let seen = walk_all(&[
        TarMember::directory("site/"),
        TarMember::file("site/index.html", b"<html></html>"),
        TarMember::file_with_mode("site/run.sh", b"#!/bin/sh\n", 0o755),
    ])
    .unwrap();

    assert_eq!(seen.len(), 3);
    assert_eq!(
        (seen[0].0.as_str(), seen[0].1),
        ("site", MemberKind::Directory)
    );
    assert_eq!(
        (seen[1].0.as_str(), seen[1].1),
        ("site/index.html", MemberKind::File)
    );
    assert_eq!(seen[1].3, 13);
    assert_eq!(seen[1].4, b"<html></html>");
    assert!(!seen[1].2);
    assert!(seen[2].2, "an execute bit is kept");
}

#[test]
fn skipping_the_data_judges_every_member_and_hands_over_none() {
    let bytes = tar_gz_bytes(&[TarMember::file("a.txt", b"hello")]);

    let seen = walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000).unwrap();

    assert_eq!(seen.len(), 1);
    assert_eq!(seen[0].3, 5);
    assert!(seen[0].4.is_empty());
}

#[test]
fn a_leading_dot_slash_is_accepted_and_the_archive_root_is_skipped() {
    let seen = walk_all(&[
        TarMember::directory("./"),
        TarMember::directory("./sub/"),
        TarMember::file("./sub/a.txt", b"x"),
    ])
    .unwrap();

    let names: Vec<&str> = seen.iter().map(|member| member.0.as_str()).collect();
    assert_eq!(names, ["sub", "sub/a.txt"]);
}

#[test]
fn a_dot_dot_after_the_dot_slash_is_still_a_traversal() {
    assert_eq!(
        walk_all(&[TarMember::file("./../escape.txt", b"x")]).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
}

#[test]
fn a_member_that_climbs_out_of_the_destination_refuses_the_whole_archive() {
    let members = [
        TarMember::file("ok.txt", b"x"),
        TarMember::file("a/../../escape.txt", b"x"),
    ];

    assert_eq!(walk_all(&members).unwrap_err(), ArchiveRefusal::UnsafeName);
}

#[test]
fn an_absolute_name_and_a_bare_dot_dot_each_refuse_the_archive() {
    assert_eq!(
        walk_all(&[TarMember::file("/etc/passwd", b"x")]).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert_eq!(
        walk_all(&[TarMember::directory("..")]).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
}

#[test]
fn a_name_that_is_not_utf8_refuses_the_archive() {
    assert_eq!(
        walk_all(&[TarMember::raw_named(b"bad-\xff\xfe.txt", b"x")]).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
}

#[test]
fn a_file_named_with_a_trailing_slash_refuses_the_archive() {
    assert_eq!(
        walk_all(&[TarMember::file("a/", b"x")]).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
}

#[test]
fn a_symlink_member_refuses_the_whole_archive() {
    let members = [
        TarMember::file("ok.txt", b"x"),
        TarMember::link("l", TAR_SYMLINK, "/etc/passwd"),
    ];

    assert_eq!(walk_all(&members).unwrap_err(), ArchiveRefusal::LinkMember);
}

#[test]
fn a_hard_link_member_refuses_the_whole_archive() {
    assert_eq!(
        walk_all(&[TarMember::link("l", TAR_HARDLINK, "ok.txt")]).unwrap_err(),
        ArchiveRefusal::LinkMember
    );
}

#[test]
fn a_device_and_a_fifo_member_each_refuse_the_whole_archive() {
    assert_eq!(
        walk_all(&[TarMember::special("dev", TAR_CHARACTER_DEVICE)]).unwrap_err(),
        ArchiveRefusal::SpecialMember
    );
    assert_eq!(
        walk_all(&[TarMember::special("pipe", TAR_FIFO)]).unwrap_err(),
        ArchiveRefusal::SpecialMember
    );
}

#[test]
fn a_sparse_member_refuses_the_whole_archive() {
    let outcome = walk_all(&[TarMember::special("sparse", TAR_SPARSE)]);

    assert!(
        matches!(
            outcome,
            Err(ArchiveRefusal::SparseMember | ArchiveRefusal::Unreadable)
        ),
        "{outcome:?}"
    );
}

#[test]
fn two_files_of_one_name_refuse_the_archive() {
    let members = [TarMember::file("a", b"1"), TarMember::file("a", b"2")];

    assert_eq!(
        walk_all(&members).unwrap_err(),
        ArchiveRefusal::DuplicateMember
    );
}

#[test]
fn a_member_below_a_file_refuses_the_archive() {
    let members = [TarMember::file("a", b"1"), TarMember::file("a/b", b"2")];

    assert_eq!(
        walk_all(&members).unwrap_err(),
        ArchiveRefusal::FileAsDirectory
    );
}

#[test]
fn more_members_than_the_cap_refuse_the_archive() {
    let bytes = tar_gz_bytes(&[
        TarMember::file("a", b"x"),
        TarMember::file("b", b"x"),
        TarMember::file("c", b"x"),
    ]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 2, 1 << 30, 1000).unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
}

#[test]
fn declared_sizes_over_the_byte_cap_refuse_the_archive() {
    let bytes = tar_gz_bytes(&[
        TarMember::file("a", &vec![0_u8; 600]),
        TarMember::file("b", &vec![0_u8; 600]),
    ]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1000, 1000).unwrap_err(),
        ArchiveRefusal::ByteLimit
    );
}

#[test]
fn an_archive_that_expands_past_the_ratio_is_a_bomb() {
    let bytes = tar_gz_bytes(&[TarMember::file("zeros", &vec![0_u8; 3 * 1024 * 1024])]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 100).unwrap_err(),
        ArchiveRefusal::BombRatio
    );
}

#[test]
fn a_gzip_stream_that_outgrows_what_the_caps_allow_is_stopped() {
    // A valid tar followed by a long run of zero blocks: nothing declared it.
    let mut tar = tar_bytes(&[TarMember::file("a", b"x")]);
    tar.extend(std::iter::repeat_n(0_u8, 8 * 1024 * 1024));
    let bytes = gzip(&tar);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 1, 100, 1000).unwrap_err(),
        ArchiveRefusal::ByteLimit
    );
}

#[test]
fn non_zero_bytes_after_the_end_of_the_tar_refuse_the_archive() {
    let mut tar = tar_bytes(&[TarMember::file("a", b"x")]);
    tar.extend_from_slice(b"a second archive hides here");
    let bytes = gzip(&tar);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000).unwrap_err(),
        ArchiveRefusal::TrailingData
    );
}

#[test]
fn zero_padding_after_the_end_of_the_tar_is_fine() {
    let mut tar = tar_bytes(&[TarMember::file("a", b"x")]);
    tar.extend(std::iter::repeat_n(0_u8, 8192));
    let bytes = gzip(&tar);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000)
            .unwrap()
            .len(),
        1
    );
}

#[test]
fn a_plain_tar_without_gzip_is_unreadable() {
    let bytes = tar_bytes(&[TarMember::file("a", b"x")]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000).unwrap_err(),
        ArchiveRefusal::Unreadable
    );
}

#[test]
fn a_gzip_stream_cut_short_is_unreadable() {
    let mut bytes = tar_gz_bytes(&[TarMember::file("a", &vec![7_u8; 50_000])]);
    bytes.truncate(bytes.len() - 20);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000).unwrap_err(),
        ArchiveRefusal::Unreadable
    );
}

#[test]
fn a_visitor_that_refuses_ends_the_walk_with_its_refusal() {
    let bytes = tar_gz_bytes(&[TarMember::file("a", b"x"), TarMember::file("b", b"x")]);
    let (_keep, file, length) = archive_file(&bytes);
    let mut budget =
        ExpansionBudget::for_extraction(&ExtractBounds::parse(10, 1000, 100).unwrap(), length);
    let mut calls = 0;

    let outcome = walk_tar_gz_members(
        &file,
        DataAccess::Read,
        &mut budget,
        &mut |_member: &ArchiveMember, _data: &mut dyn Read| {
            calls += 1;
            Err(ArchiveRefusal::Quota)
        },
    );

    assert_eq!(outcome.unwrap_err(), ArchiveRefusal::Quota);
    assert_eq!(calls, 1);
}
