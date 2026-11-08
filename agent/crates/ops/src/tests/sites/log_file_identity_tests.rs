//! Tests for the name a traffic cursor gives a log file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::log_file_identity;
use crate::sites::fake_site_log_host::{FakeSiteLogHost, access_line};

const NOON: &str = "07/Nov/2026:12:00:00 +0000";

fn identity(host: &FakeSiteLogHost, name: &str, offset: u64) -> String {
    log_file_identity(host.open(name).as_ref(), offset).unwrap()
}

#[test]
fn a_file_keeps_its_identity_while_it_grows_past_the_cursor() {
    let host = FakeSiteLogHost::new();
    let first = access_line(NOON, 200, 1, "/a");
    host.put("log", first.as_bytes());
    let before = identity(&host, "log", first.len() as u64);

    host.append("log", access_line(NOON, 200, 1, "/b").as_bytes());

    assert_eq!(identity(&host, "log", first.len() as u64), before);
}

#[test]
fn two_files_with_the_same_inode_and_different_beginnings_have_different_identities() {
    let host = FakeSiteLogHost::new();
    let one = access_line(NOON, 200, 1, "/one");
    let two = access_line("08/Nov/2026:09:00:00 +0000", 200, 1, "/two");
    host.put_with_inode("one", 42, one.as_bytes());
    host.put_with_inode("two", 42, two.as_bytes());
    let offset = one.len().min(two.len()) as u64;

    assert_ne!(
        identity(&host, "one", offset),
        identity(&host, "two", offset),
        "a recycled inode is told apart by the file's head"
    );
}

#[test]
fn two_files_with_different_inodes_have_different_identities() {
    let host = FakeSiteLogHost::new();
    let bytes = access_line(NOON, 200, 1, "/a");
    host.put("one", bytes.as_bytes());
    host.put("two", bytes.as_bytes());

    assert_ne!(identity(&host, "one", 0), identity(&host, "two", 0));
}

#[test]
fn the_identity_covers_only_the_bytes_below_the_cursor() {
    let host = FakeSiteLogHost::new();
    let bytes = access_line(NOON, 200, 1, "/a");
    host.put_with_inode("one", 9, bytes.as_bytes());
    host.put_with_inode("two", 9, format!("{bytes}{bytes}").as_bytes());

    assert_eq!(
        identity(&host, "one", 10),
        identity(&host, "two", 10),
        "bytes the cursor never saw cannot change its file's name"
    );
}

#[test]
fn the_identity_carries_no_part_of_a_visitors_address() {
    let host = FakeSiteLogHost::new();
    let bytes = access_line(NOON, 200, 1, "/a");
    host.put("log", bytes.as_bytes());

    let name = identity(&host, "log", bytes.len() as u64);

    assert!(!name.contains("203.0.113"), "{name}");
    assert_eq!(name.split(':').count(), 3);
}
