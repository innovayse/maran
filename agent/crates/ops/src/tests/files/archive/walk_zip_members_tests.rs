//! Tests for the walk over a zip archive.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Read;

use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;

use super::walk_zip_members;
use crate::files::archive::archive_fixtures::{
    ZipMember, archive_file, set_zip_declared_entries, set_zip_declared_size, set_zip_flag_bits,
    set_zip_method, zip_bytes,
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
    walk_zip_members(
        &file,
        length,
        access,
        &mut budget,
        &mut |member: &ArchiveMember, data: &mut dyn Read| {
            let mut content = Vec::new();
            data.read_to_end(&mut content)
                .map_err(|_| ArchiveRefusal::Unreadable)?;
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

fn walk_all(bytes: &[u8]) -> Result<Seen, ArchiveRefusal> {
    walk(bytes, DataAccess::Read, 100, 1 << 30, 1000)
}

#[test]
fn an_ordinary_zip_yields_its_members_with_kinds_sizes_and_content() {
    let bytes = zip_bytes(&[
        ZipMember::directory("site"),
        ZipMember::file("site/index.html", b"<html></html>"),
        ZipMember::with_mode("site/run.sh", b"#!/bin/sh\n", 0o100_755),
    ]);

    let seen = walk_all(&bytes).unwrap();

    assert_eq!(seen.len(), 3);
    assert_eq!(seen[0].0, "site");
    assert_eq!(seen[0].1, MemberKind::Directory);
    assert_eq!(seen[1].0, "site/index.html");
    assert_eq!(seen[1].1, MemberKind::File);
    assert!(!seen[1].2);
    assert_eq!(seen[1].3, 13);
    assert_eq!(seen[1].4, b"<html></html>");
    assert!(seen[2].2, "an execute bit is kept");
}

#[test]
fn skipping_the_data_judges_every_member_and_reads_none() {
    let bytes = zip_bytes(&[ZipMember::file("a.txt", b"hello")]);

    let seen = walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000).unwrap();

    assert_eq!(seen.len(), 1);
    assert_eq!(seen[0].3, 5, "the declared size is known");
    assert!(seen[0].4.is_empty(), "no bytes were read");
}

#[test]
fn a_zip_without_unix_modes_is_read_as_files_and_directories_by_name() {
    let mut plain = ZipMember::file("a.txt", b"x");
    plain.mode = None;
    let mut dir = ZipMember::directory("d");
    dir.mode = None;

    let seen = walk_all(&zip_bytes(&[dir, plain])).unwrap();

    assert_eq!(seen[0].1, MemberKind::Directory);
    assert_eq!(seen[1].1, MemberKind::File);
}

#[test]
fn a_member_that_climbs_out_of_the_destination_refuses_the_whole_archive() {
    let bytes = zip_bytes(&[
        ZipMember::file("ok.txt", b"x"),
        ZipMember::file("../escape.txt", b"x"),
    ]);

    assert_eq!(walk_all(&bytes).unwrap_err(), ArchiveRefusal::UnsafeName);
}

#[test]
fn an_absolute_name_and_a_backslash_traversal_each_refuse_the_archive() {
    assert_eq!(
        walk_all(&zip_bytes(&[ZipMember::file("/etc/cron.d/x", b"x")])).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
    assert_eq!(
        walk_all(&zip_bytes(&[ZipMember::file("..\\..\\x", b"x")])).unwrap_err(),
        ArchiveRefusal::UnsafeName
    );
}

#[test]
fn a_symlink_member_refuses_the_whole_archive() {
    let bytes = zip_bytes(&[ZipMember::with_mode("link", b"/etc/passwd", 0o120_777)]);

    assert_eq!(walk_all(&bytes).unwrap_err(), ArchiveRefusal::LinkMember);
}

#[test]
fn a_device_and_a_fifo_member_each_refuse_the_whole_archive() {
    for mode in [0o020_600_u32, 0o060_600, 0o010_600, 0o140_600] {
        let bytes = zip_bytes(&[ZipMember::with_mode("node", b"", mode)]);

        assert_eq!(
            walk_all(&bytes).unwrap_err(),
            ArchiveRefusal::SpecialMember,
            "{mode:o}"
        );
    }
}

#[test]
fn an_encrypted_member_refuses_the_archive() {
    let mut bytes = zip_bytes(&[ZipMember::file("secret.txt", b"x")]);
    set_zip_flag_bits(&mut bytes, 1);

    assert_eq!(
        walk_all(&bytes).unwrap_err(),
        ArchiveRefusal::EncryptedMember
    );
}

#[test]
fn a_compression_method_other_than_stored_or_deflate_refuses_the_archive() {
    let mut bytes = zip_bytes(&[ZipMember::file("a.txt", b"x")]);
    set_zip_method(&mut bytes, 12);

    assert_eq!(
        walk_all(&bytes).unwrap_err(),
        ArchiveRefusal::UnsupportedCompression
    );
}

#[test]
fn a_member_below_a_file_refuses_the_archive() {
    let bytes = zip_bytes(&[ZipMember::file("a", b"x"), ZipMember::file("a/b", b"x")]);

    assert_eq!(
        walk_all(&bytes).unwrap_err(),
        ArchiveRefusal::FileAsDirectory
    );
}

#[test]
fn more_members_than_the_cap_refuse_the_archive() {
    let bytes = zip_bytes(&[
        ZipMember::file("a", b"x"),
        ZipMember::file("b", b"x"),
        ZipMember::file("c", b"x"),
    ]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 2, 1 << 30, 1000).unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
}

#[test]
fn a_central_directory_declaring_far_more_entries_than_the_cap_is_refused_before_it_is_parsed() {
    let mut bytes = zip_bytes(&[ZipMember::file("a", b"x")]);
    set_zip_declared_entries(&mut bytes, 60_000);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 1000).unwrap_err(),
        ArchiveRefusal::EntryLimit
    );
}

#[test]
fn declared_sizes_over_the_byte_cap_refuse_the_archive_before_anything_is_read() {
    let bytes = zip_bytes(&[
        ZipMember::file("a", &vec![0_u8; 600]),
        ZipMember::file("b", &vec![0_u8; 600]),
    ]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1000, 1000).unwrap_err(),
        ArchiveRefusal::ByteLimit
    );
}

#[test]
fn an_archive_that_expands_past_the_ratio_is_a_bomb() {
    // Two megabytes of zeros deflate to a couple of kilobytes: past 1 MiB of
    // expansion, a 100:1 allowance is exceeded.
    let bytes = zip_bytes(&[ZipMember::file("zeros", &vec![0_u8; 2 * 1024 * 1024])]);

    assert_eq!(
        walk(&bytes, DataAccess::Skip, 100, 1 << 30, 100).unwrap_err(),
        ArchiveRefusal::BombRatio
    );
}

#[test]
fn a_member_whose_header_understates_its_size_is_cut_at_what_it_declared() {
    let mut bytes = zip_bytes(&[ZipMember::with_mode("a", &vec![5_u8; 500], 0o100_644)]);
    set_zip_declared_size(&mut bytes, 10);

    // The walk either refuses the archive (the crate sees a size mismatch) or
    // delivers no more than the 10 bytes the header declared: never the 500.
    match walk_all(&bytes) {
        Ok(seen) => assert!(seen[0].4.len() <= 10),
        Err(refusal) => assert_eq!(refusal, ArchiveRefusal::Unreadable),
    }
}

#[test]
fn bytes_that_are_not_a_zip_are_unreadable() {
    assert_eq!(
        walk_all(&vec![0x42_u8; 4096]).unwrap_err(),
        ArchiveRefusal::Unreadable
    );
}

#[test]
fn a_visitor_that_refuses_ends_the_walk_with_its_refusal() {
    let bytes = zip_bytes(&[ZipMember::file("a", b"x"), ZipMember::file("b", b"x")]);
    let (_keep, file, length) = archive_file(&bytes);
    let mut budget =
        ExpansionBudget::for_extraction(&ExtractBounds::parse(10, 1000, 100).unwrap(), length);
    let mut calls = 0;

    let outcome = walk_zip_members(
        &file,
        length,
        DataAccess::Read,
        &mut budget,
        &mut |_member: &ArchiveMember, _data: &mut dyn Read| {
            calls += 1;
            Err(ArchiveRefusal::WriteFailed)
        },
    );

    assert_eq!(outcome.unwrap_err(), ArchiveRefusal::WriteFailed);
    assert_eq!(calls, 1);
}
