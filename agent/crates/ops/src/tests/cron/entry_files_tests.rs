//! Tests for the root-side reads of the files one cron entry owns.
//!
//! These are the tests the security of the read side rests on, and none of them
//! needs root. A hosting customer owns their home; a test owns a
//! `tempfile::TempDir` and runs as its own uid, which is the same relationship.
//! `read_entry_file` takes the HOME and the uid as parameters precisely so that
//! they can come from `current_uid()` here and from `getpwnam_r` in production,
//! with everything below the split identical.
//!
//! It takes the home rather than the cron directory for a reason a review
//! measured: `O_NOFOLLOW` refuses only the TRAILING component of a path, so a
//! single open of `<home>/.maran/cron` follows a symlink planted at `.maran`.
//! The levels between the home and the cron directory therefore have to be
//! inside the walk, which means inside these tests too.
//!
//! Each of the five things a customer can leave along that path has a test: a
//! symlink at an intermediate component, a symlink at the cron directory, a
//! symlink at the file, a hardlink, and a FIFO — plus a level that is not
//! theirs. The swapped-directory case is not among them and cannot be: the
//! descriptors are opened and used inside one call, so there is no window
//! between them for a test to reach into. What the pinning buys is stated on
//! the function and is the same property `openat` gives every other caller in
//! this workspace.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::fs;
use std::os::unix::fs::symlink;
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::current_uid::current_uid;

use super::{FileContents, read_entry_file};
use crate::cron::cron_error::CronError;

/// The entry file name every test asks for.
const ENTRY: &str = "11111111-1111-4111-8111-111111111111.log";

/// A ceiling far above anything these tests write.
const ROOMY: u64 = 4096;

/// A home with its cron directory already made, as an account's would be.
fn home_with_cron_directory() -> (tempfile::TempDir, PathBuf) {
    let home = tempfile::tempdir().unwrap();
    let cron = home.path().join(AgentPaths::ACCOUNT_CRON_DIRECTORY);
    fs::create_dir_all(&cron).unwrap();

    (home, cron)
}

/// Reads `ENTRY` out of `home`'s cron directory as this process's own uid.
fn read(home: &Path, ceiling: u64) -> Result<Option<FileContents>, CronError> {
    read_entry_file(
        home,
        OsStr::new(ENTRY),
        current_uid().expect("this process has a uid"),
        ceiling,
    )
}

/// A home that was never created reads as absent.
#[test]
fn a_home_that_is_not_there_reads_as_absent() {
    let root = tempfile::tempdir().unwrap();

    let answer = read(&root.path().join("never-made"), ROOMY).unwrap();

    assert!(answer.is_none());
}

/// A cron directory that was never created reads as absent.
#[test]
fn a_cron_directory_that_is_not_there_reads_as_absent() {
    let home = tempfile::tempdir().unwrap();

    let answer = read(home.path(), ROOMY).unwrap();

    assert!(answer.is_none());
}

/// An entry that has never run has no file, which is an answer not a failure.
#[test]
fn a_file_that_is_not_there_reads_as_absent() {
    let (home, _cron) = home_with_cron_directory();

    let answer = read(home.path(), ROOMY).unwrap();

    assert!(answer.is_none());
}

/// The file's own bytes come back.
#[test]
fn a_plain_file_the_account_owns_reads_as_its_contents() {
    let (home, cron) = home_with_cron_directory();
    fs::write(cron.join(ENTRY), "the last run said this\n").unwrap();

    let contents = read(home.path(), ROOMY).unwrap().expect("a file");

    assert_eq!(contents.text, "the last run said this\n");
    assert!(!contents.saturated);
}

/// A symlink at an INTERMEDIATE component of the path is refused.
#[test]
fn a_symlink_at_an_intermediate_component_is_refused() {
    // The measured hole this walk exists to close. `O_NOFOLLOW` applies to the
    // trailing component only, so a single open of `<home>/.maran/cron` follows
    // a symlink planted at `.maran` — and the account owns the home, so it can
    // plant one. Only a per-component descent refuses it AT that component.
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::create_dir_all(elsewhere.path().join("cron")).unwrap();
    fs::write(elsewhere.path().join("cron").join(ENTRY), "redirected\n").unwrap();
    symlink(elsewhere.path(), home.path().join(".maran")).unwrap();

    let refusal = read(home.path(), ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// A symlink left at the entry's name is refused rather than followed.
#[test]
fn a_symlink_in_place_of_an_entry_file_is_refused() {
    // The customer owns the directory, so the name can point at `/etc/shadow`
    // by the time the daemon opens it.
    let (home, cron) = home_with_cron_directory();
    let elsewhere = cron.join("secret");
    fs::write(&elsewhere, "not yours\n").unwrap();
    symlink(&elsewhere, cron.join(ENTRY)).unwrap();

    let refusal = read(home.path(), ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// A symlink left in place of the cron directory itself is refused.
#[test]
fn a_symlink_in_place_of_the_cron_directory_is_refused() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join(".maran")).unwrap();
    symlink(elsewhere.path(), home.path().join(".maran").join("cron")).unwrap();

    let refusal = read(home.path(), ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// A hardlink to somebody else's file is refused, though no path check can see
/// it.
#[test]
fn a_hardlinked_file_is_refused() {
    // `ln /etc/shadow <id>.log` is not a symlink and lives at a path that
    // really is inside the home. Only the link count gives it away.
    let (home, cron) = home_with_cron_directory();
    let original = cron.join("elsewhere");
    fs::write(&original, "not yours\n").unwrap();
    fs::hard_link(&original, cron.join(ENTRY)).unwrap();

    let refusal = read(home.path(), ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// A FIFO left at the entry's name is refused instead of read.
#[test]
fn a_fifo_in_place_of_an_entry_file_is_refused() {
    // Opening a FIFO with no writer blocks in the kernel forever, and it is not
    // a symlink, so `O_NOFOLLOW` says nothing about it. `O_NONBLOCK` is what
    // makes the open return at all; this refusal is what stops the read. If
    // `O_NONBLOCK` were dropped from the flags this test would never return, so
    // it fails as a timeout rather than as an assertion — the honest shape for
    // this particular bug.
    //
    // Spawned rather than called through libc: this crate is
    // `#![forbid(unsafe_code)]`, and a test is not a reason to lower that. One
    // argv array, no shell — the same choice `follow_log`'s own FIFO test makes.
    let (home, cron) = home_with_cron_directory();
    let outcome = std::process::Command::new("mkfifo")
        .arg(cron.join(ENTRY))
        .status()
        .expect("mkfifo must be available");
    assert!(outcome.success(), "the fifo was created");

    let refusal = read(home.path(), ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// A level of the descent that is not the account's is refused.
#[test]
fn a_directory_owned_by_somebody_else_is_refused() {
    let (home, cron) = home_with_cron_directory();
    fs::write(cron.join(ENTRY), "mine\n").unwrap();
    let stranger = current_uid().expect("this process has a uid") + 1;

    let refusal = read_entry_file(home.path(), OsStr::new(ENTRY), stranger, ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// A directory where a file should be is refused rather than read.
#[test]
fn a_directory_in_place_of_an_entry_file_is_refused() {
    let (home, cron) = home_with_cron_directory();
    fs::create_dir(cron.join(ENTRY)).unwrap();

    let refusal = read(home.path(), ROOMY);

    assert_eq!(refusal.unwrap_err(), CronError::EntryFileUnreadable);
}

/// Only the last bytes of an oversized file are read into the daemon.
#[test]
fn only_the_tail_of_an_oversized_file_is_read() {
    // The file is written by a command the customer chose, so its size is
    // theirs to decide; the whole of it must never reach the daemon's memory.
    let (home, cron) = home_with_cron_directory();
    let mut written = "a".repeat(1000);
    written.push_str("the end");
    fs::write(cron.join(ENTRY), &written).unwrap();

    let contents = read(home.path(), 7).unwrap().expect("a file");

    assert_eq!(contents.text, "the end");
    assert_eq!(contents.metadata.len(), written.len() as u64);
}

/// A read that stopped at the ceiling says so, and one that did not says so.
#[test]
fn a_read_that_stops_at_the_ceiling_reports_that_it_saturated() {
    // The honest form of "is this file too big", and the only one that survives
    // the account growing the file between the `fstat` and the read: it is
    // taken from what was actually read, not from a length measured before the
    // bytes were.
    let (home, cron) = home_with_cron_directory();
    fs::write(cron.join(ENTRY), "0123456789").unwrap();

    assert!(read(home.path(), 4).unwrap().expect("a file").saturated);
    assert!(!read(home.path(), 64).unwrap().expect("a file").saturated);
}

/// A file inside the ceiling comes back whole.
#[test]
fn a_file_within_the_ceiling_comes_back_whole() {
    let (home, cron) = home_with_cron_directory();
    fs::write(cron.join(ENTRY), "short\n").unwrap();

    let contents = read(home.path(), ROOMY).unwrap().expect("a file");

    assert_eq!(contents.text, "short\n");
}

/// Bytes that are not valid UTF-8 are decoded rather than refused.
#[test]
fn output_that_is_not_valid_utf8_is_decoded_lossily() {
    // A program that emits one invalid sequence must not make its own output
    // unreadable in the panel.
    let (home, cron) = home_with_cron_directory();
    fs::write(cron.join(ENTRY), [b'o', b'k', 0xff, b'\n']).unwrap();

    let contents = read(home.path(), ROOMY).unwrap().expect("a file");

    assert!(contents.text.starts_with("ok"));
    assert!(contents.text.ends_with('\n'));
}
