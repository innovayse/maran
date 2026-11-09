//! Tests for the copy of one member's bytes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::{Cursor, Error, ErrorKind, Read, Write};

use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;

use super::copy_member_data;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::expansion_budget::ExpansionBudget;

fn budget(max_bytes: u64) -> ExpansionBudget {
    ExpansionBudget::for_extraction(
        &ExtractBounds::parse(10, max_bytes, 1000).unwrap(),
        1024 * 1024 * 1024,
    )
}

/// A reader that fails after handing out `before` bytes.
struct FailingReader {
    before: usize,
}

impl Read for FailingReader {
    fn read(&mut self, buffer: &mut [u8]) -> std::io::Result<usize> {
        if self.before == 0 {
            return Err(Error::new(ErrorKind::InvalidData, "damaged"));
        }
        let length = self.before.min(buffer.len());
        buffer[..length].fill(1);
        self.before -= length;
        Ok(length)
    }
}

/// A writer that refuses everything with `code`.
struct RefusingWriter {
    code: i32,
}

impl Write for RefusingWriter {
    fn write(&mut self, _buffer: &[u8]) -> std::io::Result<usize> {
        Err(Error::from_raw_os_error(self.code))
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

#[test]
fn a_member_whose_bytes_match_its_declared_size_is_copied_whole() {
    let mut target = Vec::new();
    let mut budget = budget(1000);

    let copied = copy_member_data(
        &mut Cursor::new(vec![9_u8; 300]),
        &mut target,
        &mut budget,
        300,
    )
    .unwrap();

    assert_eq!(copied, 300);
    assert_eq!(target, vec![9_u8; 300]);
    assert_eq!(budget.bytes(), 300);
}

#[test]
fn a_member_larger_than_the_chunk_is_copied_whole() {
    let mut target = Vec::new();
    let size = 200_000_u64;

    copy_member_data(
        &mut Cursor::new(vec![3_u8; 200_000]),
        &mut target,
        &mut budget(1_000_000),
        size,
    )
    .unwrap();

    assert_eq!(target.len(), 200_000);
}

#[test]
fn a_member_that_expands_beyond_its_declared_size_stops_at_the_byte_cap() {
    // The header says 10 bytes, the stream holds far more, and the cap is 100.
    let mut target = Vec::new();

    let refusal = copy_member_data(
        &mut std::io::repeat(0).take(1_000_000),
        &mut target,
        &mut budget(100),
        10,
    )
    .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::ByteLimit);
    assert!(target.len() <= 100, "nothing past the cap was written");
}

#[test]
fn a_member_that_holds_more_than_it_declared_is_a_lying_header() {
    let mut target = Vec::new();

    let refusal = copy_member_data(
        &mut Cursor::new(vec![1_u8; 50]),
        &mut target,
        &mut budget(1000),
        10,
    )
    .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::SizeMismatch);
}

#[test]
fn a_member_that_ends_before_its_declared_size_is_damaged() {
    let mut target = Vec::new();

    let refusal = copy_member_data(
        &mut Cursor::new(vec![1_u8; 5]),
        &mut target,
        &mut budget(1000),
        10,
    )
    .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::Unreadable);
}

#[test]
fn a_source_that_fails_is_a_damaged_archive() {
    let mut target = Vec::new();

    let refusal = copy_member_data(
        &mut FailingReader { before: 4 },
        &mut target,
        &mut budget(1000),
        100,
    )
    .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::Unreadable);
}

#[test]
fn a_quota_error_on_the_target_is_a_quota_refusal_and_a_full_disk_is_not() {
    let quota = copy_member_data(
        &mut Cursor::new(vec![1_u8; 5]),
        &mut RefusingWriter { code: libc::EDQUOT },
        &mut budget(1000),
        5,
    )
    .unwrap_err();
    let full = copy_member_data(
        &mut Cursor::new(vec![1_u8; 5]),
        &mut RefusingWriter { code: libc::ENOSPC },
        &mut budget(1000),
        5,
    )
    .unwrap_err();

    assert_eq!(quota, ArchiveRefusal::Quota);
    assert_eq!(full, ArchiveRefusal::WriteFailed);
}

#[test]
fn an_empty_member_with_a_zero_size_is_fine() {
    let mut target = Vec::new();

    let copied = copy_member_data(
        &mut Cursor::new(Vec::new()),
        &mut target,
        &mut budget(10),
        0,
    )
    .unwrap();

    assert_eq!(copied, 0);
}
