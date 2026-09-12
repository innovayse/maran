//! Tests for [`config_tree_lock`] — the race, driven rather than described.
//!
//! Every other test of the config-write protocol drives ONE writer. The defect
//! this lock closes only exists between two, so these tests start two real
//! threads, make them overlap on purpose, and assert what each one is able to
//! observe of the other's in-flight state. The argument for why that window
//! exists at all is in [`config_tree_lock`]'s own doc comment.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside
//! the unit they exercise (rules/testing.md).

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::sync::mpsc::{Receiver, Sender, channel};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, write_config};

/// How long the first writer's validator waits to be told the second writer
/// reached ITS validator.
///
/// Under the lock this budget is spent in full on every run, by design: the
/// second writer cannot reach its validator, because it cannot get past the
/// first line of `write_config`. That expiry is not a timing guess, it is the
/// observation — see the assertions, which distinguish "the budget expired and
/// the neighbour's file is untouched" from "the neighbour swapped its content
/// in while we were validating".
const OVERLAP_BUDGET: Duration = Duration::from_millis(750);

/// What the first writer's target holds before either thread starts.
const FIRST_PREVIOUS: &str = "server { server_name first.previous.test; }\n";

/// What the first writer swaps in.
const FIRST_NEW: &str = "server { server_name first.new.test; }\n";

/// What the second writer's target holds before either thread starts.
const SECOND_PREVIOUS: &str = "server { server_name second.previous.test; }\n";

/// What the second writer swaps in — the content the first writer's validator
/// must never be able to see, because seeing it is the defect.
const SECOND_NEW: &str = "server { server_name second.new.test; }\n";

/// The ordered record both threads write their progress into.
type Journal = Arc<Mutex<Vec<String>>>;

/// Appends one line to the shared journal.
fn note(journal: &Journal, line: &str) {
    journal
        .lock()
        .expect("the fixture lock is never poisoned")
        .push(line.to_owned());
}

/// The first writer's host: its validator announces itself, waits for the
/// second writer to announce that it has swapped its own content in, and then
/// records what the second writer's target actually holds at that instant.
struct FirstWriterHost {
    journal: Journal,
    /// Told when this host's validator has been entered, so the test can
    /// release the second thread at exactly that point.
    entered_validator: Sender<()>,
    /// Waited on for the second writer's announcement, for [`OVERLAP_BUDGET`].
    second_swapped: Mutex<Receiver<()>>,
    /// The second writer's target, read at validation time.
    second_target: PathBuf,
    /// What this host saw at [`Self::second_target`] while it was validating.
    observed: Mutex<Option<String>>,
    /// Whether the second writer had announced its swap before the budget ran
    /// out.
    heard_second_swap: Mutex<bool>,
}

impl ConfigHost for FirstWriterHost {
    fn run(&self, _program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        // Only the validator observes; the reload that follows it must not
        // wait a second budget or re-read anything.
        if arguments.first() == Some(&"-t") {
            note(&self.journal, "first: validating");
            self.entered_validator
                .send(())
                .expect("the test thread is still listening");

            let heard = self
                .second_swapped
                .lock()
                .expect("the fixture lock is never poisoned")
                .recv_timeout(OVERLAP_BUDGET)
                .is_ok();
            *self
                .heard_second_swap
                .lock()
                .expect("the fixture lock is never poisoned") = heard;

            let seen = std::fs::read_to_string(&self.second_target)
                .expect("the second writer's target exists throughout");
            note(&self.journal, "first: read the other target");
            *self
                .observed
                .lock()
                .expect("the fixture lock is never poisoned") = Some(seen);
        }

        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

/// The second writer's host: its validator announces that this writer's
/// content is renamed in and live, which is the exact moment the first
/// writer's validator must not be able to reach.
struct SecondWriterHost {
    journal: Journal,
    swapped: Sender<()>,
}

impl ConfigHost for SecondWriterHost {
    fn run(&self, _program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        if arguments.first() == Some(&"-t") {
            note(&self.journal, "second: validating");
            // A send with no listener is not a failure here: the first writer
            // stops listening once its budget expires, which is the whole
            // point of the locked run.
            let _ = self.swapped.send(());
        }

        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

/// The validator argv both hosts branch on.
fn validator() -> Validator<'static> {
    Validator {
        program: "/usr/sbin/nginx",
        arguments: &["-t"],
    }
}

/// The reload argv, which neither host does anything with.
fn reload() -> Reload<'static> {
    Reload {
        program: "/usr/bin/systemctl",
        arguments: &["reload", "nginx"],
    }
}

/// Seeds `path` with `contents`.
fn seed(path: &Path, contents: &str) {
    std::fs::write(path, contents).expect("the fixture directory is writable");
}

#[test]
fn a_writer_validating_cannot_see_another_writers_swapped_in_content() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let first_target = directory.path().join("first.conf");
    let second_target = directory.path().join("second.conf");
    seed(&first_target, FIRST_PREVIOUS);
    seed(&second_target, SECOND_PREVIOUS);

    let journal: Journal = Arc::new(Mutex::new(Vec::new()));
    let (entered_validator, validator_entered) = channel();
    let (swapped, second_swapped) = channel();

    let first_host = Arc::new(FirstWriterHost {
        journal: Arc::clone(&journal),
        entered_validator,
        second_swapped: Mutex::new(second_swapped),
        second_target: second_target.clone(),
        observed: Mutex::new(None),
        heard_second_swap: Mutex::new(false),
    });

    let first_thread = {
        let host = Arc::clone(&first_host);
        let target = first_target.clone();
        let journal = Arc::clone(&journal);
        std::thread::spawn(move || {
            note(&journal, "first: entering the protocol");
            write_config(host.as_ref(), &target, FIRST_NEW, &validator(), &reload())
        })
    };

    // The second thread is released at the exact instant the first one is
    // inside its validator, which is the middle of its critical section: after
    // its capture, after its rename, before its commit.
    validator_entered
        .recv_timeout(Duration::from_secs(30))
        .expect("the first writer must reach its validator");

    let second_thread = {
        let journal = Arc::clone(&journal);
        let target = second_target.clone();
        std::thread::spawn(move || {
            let host = SecondWriterHost {
                journal: Arc::clone(&journal),
                swapped,
            };
            note(&journal, "second: entering the protocol");
            write_config(&host, &target, SECOND_NEW, &validator(), &reload())
        })
    };

    let first = first_thread.join().expect("the first writer must finish");
    let second = second_thread.join().expect("the second writer must finish");

    // The positive control, first: BOTH operations really ran, both were
    // accepted, and both left their own content on disk. A test in which the
    // second writer never started would satisfy every assertion below it and
    // prove nothing at all.
    assert!(first.is_ok(), "the first write must be accepted: {first:?}");
    assert!(
        second.is_ok(),
        "the second write must be accepted: {second:?}"
    );
    assert_eq!(
        std::fs::read_to_string(&first_target).expect("the first target is readable"),
        FIRST_NEW
    );
    assert_eq!(
        std::fs::read_to_string(&second_target).expect("the second target is readable"),
        SECOND_NEW
    );

    // The finding itself. While the first writer was validating — which for a
    // real `nginx -t` means the whole tree, every tenant's file included — the
    // second writer's target still held what it held before. Without the lock
    // it holds the second writer's swapped-in content, and a real validator
    // would answer about THAT.
    assert_eq!(
        first_host
            .observed
            .lock()
            .expect("the fixture lock is never poisoned")
            .clone(),
        Some(SECOND_PREVIOUS.to_owned()),
        "a writer's validation must not see another writer's in-flight content"
    );
    assert!(
        !*first_host
            .heard_second_swap
            .lock()
            .expect("the fixture lock is never poisoned"),
        "the second writer must not have reached its validator while the first held the lock"
    );

    // And the second half of that control: the two really overlapped. The
    // second writer had entered `write_config` while the first was inside its
    // validator — the journal's order is the evidence, and it is asserted as a
    // whole value rather than as "contains".
    assert_eq!(
        journal
            .lock()
            .expect("the fixture lock is never poisoned")
            .clone(),
        vec![
            "first: entering the protocol".to_owned(),
            "first: validating".to_owned(),
            "second: entering the protocol".to_owned(),
            "first: read the other target".to_owned(),
            "second: validating".to_owned(),
        ],
        "the second writer must have entered the protocol while the first was validating, \
         and must not have reached its own validator until the first had finished"
    );
}
