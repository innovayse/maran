//! Tests for the root-side log tail.
//!
//! These are the tests the security of this change rests on, and none of them
//! needs root. A hosting customer owns their log directory; a test owns a
//! `tempfile::TempDir` and runs as its own uid, which is the same relationship.
//! `follow_as` exists precisely so that the uid can come from
//! `current_uid()` here and from `getpwnam_r` in production, with everything
//! below the split identical.
//!
//! Each of the four attacks the threat note names has a test: a symlink, a
//! hardlink, a FIFO, and a directory swapped mid-tail — the last in BOTH
//! variants, because the rename-then-symlink one is refused by `O_NOFOLLOW`
//! alone and therefore cannot prove that the directory descriptor is pinned.
//!
//! Truncation has two tests and they are deliberately not one. A truncation
//! landing between two polls is caught by a length comparison; one landing
//! inside a poll, between the `fstat` and the read, is caught by a short read.
//! A concurrent test can only ever produce the first — a 500 ms poll interval
//! against a microsecond-scale truncation lands in the gap essentially every
//! time — so the second is built directly, by handing `read_window` the stale
//! length that race produces. A single test named for both would be one that
//! cannot fail when half of what it claims to cover is broken, which is what an
//! earlier version of this file was.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsString;
use std::os::unix::fs::symlink;
use std::path::Path;
use std::time::Duration;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::system::name::AccountName;

use super::{
    Window, follow_as, follow_with_patience, open_directory, open_log, read_exact_at, read_window,
    window, window_bounds,
};
use crate::sites::SitesOpError;
use crate::sites::log_sink::LogSink;
use crate::sites::model::log_tail_request::LogTailRequest;
use crate::sites::model::tail_end::TailEnd;

/// The log name every test uses.
const LOG: &str = "example.com.access.log";

/// A sink that records what it is given and stops after `patience` polls.
///
/// The patience is what keeps a test from waiting out `MAXIMUM_IDLE`: the
/// follow loop asks `is_listening` at the top of every poll, so answering
/// `false` on the n-th call ends the tail in n × 500 ms.
#[derive(Debug)]
struct Recorder {
    /// Lines delivered, with their `historical` flag.
    lines: Vec<(String, bool)>,
    /// How many more polls this sink will allow.
    patience: u32,
}

impl Recorder {
    /// A sink that allows `patience` polls before ending the tail.
    fn new(patience: u32) -> Self {
        Self {
            lines: Vec::new(),
            patience,
        }
    }

    /// Just the line text, for the assertions that do not care about the flag.
    fn text(&self) -> Vec<String> {
        self.lines.iter().map(|(line, _)| line.clone()).collect()
    }
}

impl LogSink for Recorder {
    fn line(&mut self, line: &str, historical: bool) -> Result<(), TailEnd> {
        self.lines.push((line.to_owned(), historical));
        Ok(())
    }

    fn is_listening(&mut self) -> bool {
        if self.patience == 0 {
            return false;
        }
        self.patience -= 1;
        true
    }
}

/// A sink that ends the tail with `ending` on the first line it is offered.
struct Refusing {
    /// The ending this sink reports.
    ending: TailEnd,
}

impl LogSink for Refusing {
    fn line(&mut self, _line: &str, _historical: bool) -> Result<(), TailEnd> {
        Err(self.ending)
    }

    fn is_listening(&mut self) -> bool {
        true
    }
}

/// A request for `LOG` in `directory`, asking for `history_lines` of history.
fn request(directory: &Path, history_lines: u32) -> LogTailRequest {
    LogTailRequest {
        account: AccountName::parse("acme").unwrap(),
        directory: directory.to_path_buf(),
        file_name: OsString::from(LOG),
        history_lines,
    }
}

/// Runs a tail that ends immediately after the history batch.
fn tail_history(directory: &Path, history_lines: u32) -> Result<Recorder, SitesOpError> {
    let mut sink = Recorder::new(0);
    follow_as(
        &request(directory, history_lines),
        current_uid().unwrap(),
        &mut sink,
    )?;
    Ok(sink)
}

/// Asserts the outcome is the one refusal this module reports.
fn assert_refused(outcome: Result<Recorder, SitesOpError>, what: &str) {
    match outcome {
        Err(SitesOpError::LogUnreadable { .. }) => {}
        other => panic!("{what} must be refused as unreadable, got {other:?}"),
    }
}

#[test]
fn a_fifo_in_place_of_the_log_is_refused_and_does_not_hang() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);

    // The denial of service: opening a FIFO with no writer blocks in the kernel
    // forever, on a thread from the pool every agent operation shares. If
    // `O_NONBLOCK` were dropped from LOG_FLAGS this test would never return, so
    // it fails as a timeout rather than as an assertion — which is the honest
    // shape for this particular bug.
    // Spawned rather than called through libc: this crate is
    // `#![forbid(unsafe_code)]`, and a test is not a reason to lower that. One
    // argv array, no shell.
    let made = std::process::Command::new("mkfifo")
        .arg(&path)
        .status()
        .expect("mkfifo must be available");
    assert!(made.success());

    assert_refused(tail_history(directory.path(), 10), "a FIFO");
}

#[test]
fn a_hardlink_to_a_file_outside_the_home_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    let target = outside.path().join("secret");
    std::fs::write(&target, "not the customer's\n").unwrap();

    // Not a symlink, and the path really is inside the log directory, so every
    // path check passes it. Only `nlink != 1` catches it.
    std::fs::hard_link(&target, directory.path().join(LOG)).unwrap();

    assert_refused(tail_history(directory.path(), 10), "a hardlink");
}

#[test]
fn a_symlink_in_place_of_the_log_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    let target = outside.path().join("secret");
    std::fs::write(&target, "not the customer's\n").unwrap();

    symlink(&target, directory.path().join(LOG)).unwrap();

    assert_refused(tail_history(directory.path(), 10), "a symlink");
}

#[test]
fn a_log_directory_the_account_does_not_own_is_refused_by_the_whole_tail() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "line\n").unwrap();

    // A test cannot chown to another user without root, so the expectation is
    // moved instead: the tail is told to expect a uid that is not the one that
    // owns these files. It reaches the same `metadata.uid() != uid` branch a
    // recycled uid or a planted directory would hit in production.
    //
    // This covers the check in `open_directory`. The one on the FILE is a
    // separate branch and needs its own test, below — running both through
    // `follow_as` would let the directory check mask the file check, which is
    // exactly what it was doing before a mutation run caught it.
    let mut sink = Recorder::new(0);
    let outcome = follow_as(
        &request(directory.path(), 10),
        current_uid().unwrap().wrapping_add(1),
        &mut sink,
    );

    match outcome {
        Err(SitesOpError::LogUnreadable { .. }) => {}
        other => panic!("a log directory the account does not own must be refused, got {other:?}"),
    }
}

#[test]
fn a_log_directory_the_account_does_not_own_is_refused_before_the_log_is_opened() {
    let directory = tempfile::tempdir().unwrap();

    // Straight at `open_directory`, for the same reason the file check gets its
    // own test: driven through `follow_as`, the file check would refuse this
    // too, so each would mask the other and neither could fail alone. A
    // mutation run found exactly that.
    let outcome = open_directory(directory.path(), current_uid().unwrap().wrapping_add(1));

    match outcome {
        Err(SitesOpError::LogUnreadable { .. }) => {}
        other => panic!("a log directory the account does not own must be refused, got {other:?}"),
    }
}

#[test]
fn a_log_file_the_account_does_not_own_is_refused() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "line\n").unwrap();

    // Straight at `open_log`, with a directory descriptor that passed its own
    // ownership check, so the ONLY thing that can refuse here is the check on
    // the file's inode. That is the branch a planted or recycled-uid log hits,
    // and through `follow_as` it is unreachable — the directory is checked
    // first and fails identically.
    let pinned = open_directory(directory.path(), current_uid().unwrap()).unwrap();
    let outcome = open_log(
        &pinned,
        std::ffi::OsStr::new(LOG),
        current_uid().unwrap().wrapping_add(1),
    );

    match outcome {
        Err(SitesOpError::LogUnreadable { .. }) => {}
        other => panic!("a log the account does not own must be refused, got {other:?}"),
    }
}

#[test]
fn a_directory_swapped_mid_tail_does_not_redirect_the_read() {
    let root = tempfile::tempdir().unwrap();
    let logs = root.path().join("logs");
    std::fs::create_dir(&logs).unwrap();
    std::fs::write(logs.join(LOG), "real\n").unwrap();

    let decoy = root.path().join("decoy");
    std::fs::create_dir(&decoy).unwrap();
    std::fs::write(decoy.join(LOG), "planted\n").unwrap();

    // Two polls of patience, and the swap happens before the tail even starts
    // its follow — the descriptor was taken at the first open, so the rename
    // and the symlink below must have no effect on what it reads.
    let mut sink = Recorder::new(2);
    std::thread::scope(|scope| {
        scope.spawn(|| {
            std::thread::sleep(std::time::Duration::from_millis(200));
            std::fs::rename(&logs, root.path().join("logs.old")).unwrap();
            symlink(&decoy, &logs).unwrap();
            std::fs::write(root.path().join("logs.old").join(LOG), "real\nmore\n").unwrap();
        });

        follow_as(&request(&logs, 10), current_uid().unwrap(), &mut sink).unwrap();
    });

    assert!(
        !sink.text().iter().any(|line| line == "planted"),
        "the pinned descriptor must not be redirected by a swapped path, got {:?}",
        sink.text()
    );
    assert!(sink.text().contains(&"real".to_owned()));
}

#[test]
fn a_real_directory_replacing_the_pinned_one_does_not_redirect_the_read() {
    let root = tempfile::tempdir().unwrap();
    let logs = root.path().join("logs");
    std::fs::create_dir(&logs).unwrap();
    std::fs::write(logs.join(LOG), "real\n").unwrap();

    // `mv logs logs.old && mkdir logs` — a real directory replaced by a real
    // directory, with no symlink anywhere in it. This is the variant that
    // distinguishes the two protections: the rename-then-symlink case is
    // refused by `O_NOFOLLOW` whether or not the directory descriptor is
    // pinned, so it cannot prove pinning. Nothing here is a symlink, so only
    // the pinned fd can keep the planted file out.
    let mut sink = Recorder::new(3);
    std::thread::scope(|scope| {
        scope.spawn(|| {
            std::thread::sleep(std::time::Duration::from_millis(200));
            std::fs::rename(&logs, root.path().join("logs.old")).unwrap();
            std::fs::create_dir(&logs).unwrap();
            std::fs::write(logs.join(LOG), "planted\n").unwrap();
            // Appended to the ORIGINAL inode, which the pinned descriptor still
            // names — so a correct tail keeps reading this one.
            std::fs::write(
                root.path().join("logs.old").join(LOG),
                "real\nstill the old inode\n",
            )
            .unwrap();
        });

        follow_as(&request(&logs, 10), current_uid().unwrap(), &mut sink).unwrap();
    });

    assert!(
        !sink.text().iter().any(|line| line == "planted"),
        "a real directory swapped in must not be read: the descriptor names an inode, got {:?}",
        sink.text()
    );
    assert!(
        sink.text().contains(&"still the old inode".to_owned()),
        "the tail must keep reading the inode it pinned, got {:?}",
        sink.text()
    );
}

#[test]
fn the_history_is_capped_at_the_contract_ceiling_however_much_is_asked_for() {
    let directory = tempfile::tempdir().unwrap();
    let body: String = (0..5000).map(|n| format!("line {n}\n")).collect();
    std::fs::write(directory.path().join(LOG), &body).unwrap();

    // A caller asking for a million gets the cap, and the cap is applied by the
    // operation and not by the service that called it.
    let sink = tail_history(directory.path(), 1_000_000).unwrap();

    assert_eq!(sink.lines.len(), 1000);
    assert!(sink.lines.iter().all(|(_, historical)| *historical));
    // The LAST thousand, not the first: a tail shows the end of the file.
    assert_eq!(sink.text().last().unwrap(), "line 4999");
    assert_eq!(sink.text().first().unwrap(), "line 4000");
}

#[test]
fn an_enormous_log_is_not_read_into_memory_to_find_its_last_lines() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);

    // A sparse file: 512 MiB of hole, then the lines that matter. Reading it
    // forwards would allocate half a gigabyte in the root daemon, which is the
    // attack `truncate -s 50G` is the cheap version of. The backwards scan
    // touches only the tail, so this test is fast and its allocation is small.
    let file = std::fs::File::create(&path).unwrap();
    file.set_len(512 * 1024 * 1024).unwrap();
    drop(file);
    let mut appended = std::fs::OpenOptions::new()
        .append(true)
        .open(&path)
        .unwrap();
    std::io::Write::write_all(&mut appended, b"\nfirst\nsecond\nthird\n").unwrap();
    drop(appended);

    let sink = tail_history(directory.path(), 2).unwrap();

    assert_eq!(sink.text(), vec!["second".to_owned(), "third".to_owned()]);
}

#[test]
fn lines_appended_after_the_history_arrive_marked_as_live() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    std::fs::write(&path, "old\n").unwrap();

    let mut sink = Recorder::new(3);
    std::thread::scope(|scope| {
        scope.spawn(|| {
            std::thread::sleep(std::time::Duration::from_millis(200));
            let mut file = std::fs::OpenOptions::new()
                .append(true)
                .open(&path)
                .unwrap();
            std::io::Write::write_all(&mut file, b"fresh\n").unwrap();
        });

        follow_as(
            &request(directory.path(), 10),
            current_uid().unwrap(),
            &mut sink,
        )
        .unwrap();
    });

    assert_eq!(
        sink.lines,
        vec![("old".to_owned(), true), ("fresh".to_owned(), false)]
    );
}

#[test]
fn a_shrink_seen_between_two_polls_restarts_instead_of_failing() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    std::fs::write(&path, "before\n").unwrap();

    // The truncation lands in the gap between two polls, so the LENGTH check
    // catches it. Named for what it actually exercises: an earlier version of
    // this test was called "truncated under the tail" and was believed to cover
    // the mid-read race as well. It does not, and cannot — see
    // `a_stale_length_is_reported_as_a_shrunken_file_and_not_as_an_error` for
    // that half, and this file's header for why the two must be separate.
    let mut sink = Recorder::new(4);
    std::thread::scope(|scope| {
        scope.spawn(|| {
            std::thread::sleep(std::time::Duration::from_millis(200));
            std::fs::write(&path, "").unwrap();
            std::thread::sleep(std::time::Duration::from_millis(600));
            std::fs::write(&path, "after\n").unwrap();
        });

        follow_as(
            &request(directory.path(), 10),
            current_uid().unwrap(),
            &mut sink,
        )
        .expect("a rotation is not a failure");
    });

    assert!(
        sink.text().contains(&"after".to_owned()),
        "the tail must pick up the rotated file, got {:?}",
        sink.text()
    );
}

#[test]
fn a_stale_length_is_reported_as_a_shrunken_file_and_not_as_an_error() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    std::fs::write(&path, "one\ntwo\n").unwrap();
    let mut file = std::fs::File::open(&path).unwrap();

    // THE mid-read race, constructed without a race.
    //
    // `read_window` is handed the `end` its caller read from an `fstat`. A
    // `copytruncate` landing between that `fstat` and the read is, from inside
    // this function, indistinguishable from being handed an `end` that is
    // simply larger than the file — the function cannot tell whether the number
    // came from a syscall a nanosecond ago or a second ago. So the state is
    // built directly rather than raced for: no sleep, no thread, and no
    // dependence on a poll interval outrunning a microsecond-scale truncation,
    // which is the trap the previous version of this file fell into.
    let stale_end = 9_000;
    let outcome = read_window(&mut file, 0, stale_end, std::ffi::OsStr::new(LOG)).unwrap();

    assert!(
        outcome.is_none(),
        "a file shorter than the length it was opened with is a shrink, not a fault"
    );
}

#[test]
fn the_reason_a_sink_ended_the_tail_is_carried_back_to_the_caller() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "line\n").unwrap();

    // The tail must not flatten the three endings into "it finished". A client
    // that closed is nobody's business; a client the agent dropped is something
    // the operator has to be told, and the service can only say so if the
    // reason survives the trip back.
    for ending in [TailEnd::ClientClosed, TailEnd::ClientStalled] {
        let mut sink = Refusing { ending };
        let outcome = follow_as(
            &request(directory.path(), 10),
            current_uid().unwrap(),
            &mut sink,
        )
        .unwrap();

        assert_eq!(outcome, ending);
    }
}

#[test]
fn a_sink_that_stops_listening_ends_the_tail_as_the_clients_own_choice() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "line\n").unwrap();

    let mut sink = Recorder::new(0);
    let outcome = follow_as(
        &request(directory.path(), 10),
        current_uid().unwrap(),
        &mut sink,
    )
    .unwrap();

    assert_eq!(outcome, TailEnd::ClientClosed);
    assert!(!outcome.is_involuntary(), "nobody is owed an explanation");
}

/// An idle ceiling short enough that a test can wait it out, and shorter than
/// one poll so it fires on the first.
const SHORT_PATIENCE: Duration = Duration::from_millis(50);

#[test]
fn a_tail_on_a_silent_log_ends_itself_rather_than_holding_the_thread() {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join(LOG), "old\n").unwrap();

    // Nothing is ever appended, and the sink keeps listening — a client that
    // opened a tab on a quiet site and walked away. The ONLY thing that should
    // end this is the idle ceiling, and a `spawn_blocking` task cannot be
    // aborted from outside, so if the ceiling does not fire the thread is held
    // for the life of the process. Enough of those and every site, SSL, PHP and
    // account operation in the agent stops, because they all share that pool.
    //
    // The sink's own patience is finite and generous — six polls, three seconds
    // — so that a BROKEN ceiling fails this test instead of hanging it. A test
    // that hangs when the thing it guards is removed reports a CI timeout
    // rather than a red test, which is a worse signal than the bug.
    let mut sink = Recorder::new(6);
    let outcome = follow_with_patience(
        &request(directory.path(), 10),
        current_uid().unwrap(),
        SHORT_PATIENCE,
        &mut sink,
    )
    .unwrap();

    assert_eq!(
        outcome,
        TailEnd::Idle,
        "a silent tail must end itself on its idle ceiling, not run until the sink gives up"
    );
    assert!(
        outcome.is_involuntary(),
        "the operator must be told the agent stopped watching"
    );
}

#[test]
fn a_log_that_keeps_being_written_to_keeps_its_tail_alive() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    std::fs::write(&path, "old\n").unwrap();

    // The other half of the ceiling, and the half that would otherwise let it
    // be set to "always fire": a busy log must NOT be cut off. The patience is
    // longer than one poll and shorter than two, so the clock has to be reset
    // by each poll's delivery or the second poll ends the tail as `Idle`.
    let patience = Duration::from_millis(600);
    let writing = std::sync::atomic::AtomicBool::new(true);

    let mut sink = Recorder::new(3);
    let outcome = std::thread::scope(|scope| {
        scope.spawn(|| {
            while writing.load(std::sync::atomic::Ordering::Relaxed) {
                let mut file = std::fs::OpenOptions::new()
                    .append(true)
                    .open(&path)
                    .unwrap();
                std::io::Write::write_all(&mut file, b"tick\n").unwrap();
                std::thread::sleep(Duration::from_millis(150));
            }
        });

        let outcome = follow_with_patience(
            &request(directory.path(), 10),
            current_uid().unwrap(),
            patience,
            &mut sink,
        )
        .unwrap();
        writing.store(false, std::sync::atomic::Ordering::Relaxed);
        outcome
    });

    assert_eq!(
        outcome,
        TailEnd::ClientClosed,
        "activity must reset the idle clock; the tail ended as {outcome:?} instead"
    );
    assert!(sink.text().iter().any(|line| line == "tick"));
}

#[test]
fn a_shrunken_log_is_routed_to_a_restart_however_the_shrink_was_noticed() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    std::fs::write(&path, "one\ntwo\n").unwrap();
    let mut file = std::fs::File::open(&path).unwrap();
    let name = std::ffi::OsStr::new(LOG);

    // The JOIN, which is what neither half's test reaches. Both ways of
    // noticing a rotation must arrive at the same answer, and inline in the
    // poll loop this routing could be replaced with an error and the whole
    // suite stayed green.
    //
    // Seen between two polls: the length is behind where we were reading.
    assert!(matches!(
        window(&mut file, 100, 8, name).unwrap(),
        Window::Restart
    ));

    // Seen inside a poll: the length is one the file no longer has, which is
    // the entirety of what a `copytruncate` between the `fstat` and the read
    // produces here.
    assert!(matches!(
        window(&mut file, 0, 9_000, name).unwrap(),
        Window::Restart
    ));

    // And an ordinary poll is neither.
    match window(&mut file, 0, 8, name).unwrap() {
        Window::Ready { text, next, .. } => {
            assert_eq!(text, "one\ntwo\n");
            assert_eq!(next, 8);
        }
        other => panic!("an ordinary window must deliver its lines, got {other:?}"),
    }

    // A log nobody has written to since the last poll is not a rotation.
    assert!(matches!(
        window(&mut file, 8, 8, name).unwrap(),
        Window::Unchanged
    ));
}

#[test]
fn a_log_that_does_not_exist_yet_is_not_an_error() {
    let directory = tempfile::tempdir().unwrap();

    // A site that has served no request has no access log, and that must read
    // as "nothing to show" rather than as a fault.
    let sink = tail_history(directory.path(), 10).unwrap();

    assert!(sink.lines.is_empty());
}

#[test]
fn a_log_directory_that_is_a_symlink_is_refused_before_the_loop_starts() {
    let root = tempfile::tempdir().unwrap();
    let real = root.path().join("elsewhere");
    std::fs::create_dir(&real).unwrap();
    std::fs::write(real.join(LOG), "line\n").unwrap();
    let logs = root.path().join("logs");
    symlink(&real, &logs).unwrap();

    let mut sink = Recorder::new(0);
    let outcome = follow_as(&request(&logs, 10), current_uid().unwrap(), &mut sink);

    match outcome {
        Err(SitesOpError::LogUnreadable { .. }) => {}
        other => panic!("a symlinked log directory must be refused, got {other:?}"),
    }
}

#[test]
fn a_short_read_is_reported_as_a_shrunken_file_and_not_as_an_error() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    std::fs::write(&path, "1234567890").unwrap();
    let mut file = std::fs::File::open(&path).unwrap();

    // The truncation race, tested where it is deterministic: ask for more bytes
    // than the file holds, exactly as a `copytruncate` landing between the
    // `fstat` and the read would produce.
    let mut buffer = [0_u8; 64];
    let read = read_exact_at(&mut file, 0, &mut buffer, std::ffi::OsStr::new(LOG)).unwrap();

    assert!(!read, "a short read is a shrunken file, not a failure");
}

#[test]
fn a_window_larger_than_the_ceiling_is_skipped_and_the_hole_is_reported() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join(LOG);
    // Comfortably past FOLLOW_CEILING, so the window must drop the front.
    let body: String = (0..60_000).map(|n| format!("line {n}\n")).collect();
    std::fs::write(&path, &body).unwrap();
    let mut file = std::fs::File::open(&path).unwrap();

    let end = body.len() as u64;
    let (text, next, skipped) = read_window(&mut file, 0, end, std::ffi::OsStr::new(LOG))
        .unwrap()
        .unwrap();

    assert!(skipped > 0, "the excess must be reported, not buffered");
    assert!(
        text.len() <= 256 * 1024,
        "the window must be capped at the ceiling, got {}",
        text.len()
    );
    assert_eq!(next, end, "the window must end at the file's end");
    // The reported hole includes the partial line dropped at the front, not
    // only the bytes the window never reached.
    assert_eq!(skipped as usize, end as usize - text.len());
}

#[test]
fn window_bounds_with_offset_past_end_collapses_to_an_empty_window() {
    // `read_window`'s own `debug_assert` fires on this input, so it can only
    // ever be exercised through the assertion, never past it — going through
    // `read_window` here would prove the assertion exists, not that the
    // arithmetic beneath it is safe once the assertion is gone. Calling
    // `window_bounds` directly is the only way to see what a release build,
    // where that assertion compiles to nothing, actually does.
    //
    // Red without the fix: reverting `window_bounds`'s
    // `end.saturating_sub(begin)` to a plain `end - begin` underflows a `u64`
    // for this input. `cargo test`'s default profile keeps overflow checks on
    // (nothing in this workspace turns them off), so the mutation panics this
    // test with "attempt to subtract with overflow" rather than returning a
    // wrong-but-quiet value — a release build has no such check, and the same
    // mutation there does not panic, it wraps into a size near `u64::MAX` and
    // asks the allocator to satisfy it.
    assert_eq!(window_bounds(500, 100), (500, 0, 0));
    // `offset == end` is filtered out by `window` before it ever reaches
    // `read_window`, same as `offset > end` — but it shares the same
    // arithmetic, so it is worth the same proof rather than an assumption.
    assert_eq!(window_bounds(500, 500), (500, 0, 0));
}
