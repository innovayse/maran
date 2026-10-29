//! Tests for [`run_bounded`], driven through the private `run_until` seam so
//! that the timeout path runs in milliseconds instead of the 60 s of the
//! shortest call class.
//!
//! Every program is a stand-in that needs no shell. Every wait is a poll with a
//! timeout: a test that would hang on a broken core fails by name inside its
//! outer `recv_timeout` instead.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Write as _;
use std::path::Path;
use std::sync::mpsc::{self, Receiver};
use std::time::{Duration, Instant};

use super::{run_bounded, run_until};
use crate::command_outcome::CommandOutcome;
use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::call_class::CallClass;
use crate::utils::child_input::ChildInput;
use crate::utils::child_output::ChildOutput;
use crate::utils::kill_group_and_reap::REAP_CEILING;
use crate::utils::output_stream::OutputStream;
use crate::utils::process_probe::{
    FIND, KillOnDrop, SLEEP, as_strs, find_sleep_arguments, grandchild_pids, is_gone, kill_pid,
    leader_pids, pids_with_argv, unique_length, wait_until,
};
use crate::utils::spawn_error::SpawnError;
use crate::utils::spawn_request::SpawnRequest;

/// The result type every case receives from a call that ran on its own thread.
type Finished = Result<CommandOutcome, SpawnError>;

/// Builds a request with the shortest class; `run_until` ignores the class.
fn request<'a>(
    program: &'a Path,
    arguments: &'a [&'a str],
    input: ChildInput<'a>,
    output: ChildOutput,
) -> SpawnRequest<'a> {
    SpawnRequest {
        program,
        arguments,
        class: CallClass::Query,
        input,
        output,
    }
}

/// Runs `program` on a thread of its own with a null input, so the test thread
/// can look at `/proc` while the call is still running.
fn run_on_thread(
    program: &'static str,
    arguments: Vec<String>,
    output: ChildOutput,
    deadline: Duration,
) -> Receiver<Finished> {
    let (sender, receiver) = mpsc::channel();

    std::thread::spawn(move || {
        let borrowed = as_strs(&arguments);
        let result = run_until(
            request(Path::new(program), &borrowed, ChildInput::Null, output),
            deadline,
        );
        let _ = sender.send(result);
    });

    receiver
}

/// How long a test waits for a call that must return by its own deadline.
fn patience(deadline: Duration) -> Duration {
    deadline + REAP_CEILING + Duration::from_secs(2)
}

/// A child that outlives its deadline is killed with its grandchild, and the
/// call returns the typed timeout.
#[test]
fn a_child_that_outlives_its_deadline_is_killed_with_its_grandchild_and_the_call_returns_timed_out()
{
    let length = unique_length(1);
    let _cleanup = KillOnDrop {
        argv: vec![SLEEP.to_owned(), length.clone()],
    };
    let deadline = Duration::from_secs(2);

    let receiver = run_on_thread(
        FIND,
        find_sleep_arguments(&length),
        ChildOutput::DEFAULT,
        deadline,
    );

    // Positive control: the grandchild exists while the call runs, so "gone"
    // below is a kill and not a stand-in that never started.
    assert!(
        wait_until(Duration::from_millis(1_500), || !grandchild_pids(&length)
            .is_empty()),
        "the grandchild never appeared; the stand-in did not run"
    );
    let grandchildren = grandchild_pids(&length);

    let result = receiver
        .recv_timeout(patience(deadline))
        .expect("the call did not return within the deadline plus the reap ceiling");

    match result {
        Err(SpawnError::Bound(BoundExceeded::TimedOut {
            program,
            deadline: reported,
        })) => {
            assert_eq!(program, FIND);
            assert_eq!(reported, deadline);
        }
        other => panic!("expected a timeout, got {other:?}"),
    }
    assert!(
        wait_until(Duration::from_secs(5), || grandchildren
            .iter()
            .all(|pid| is_gone(*pid))),
        "the grandchild survived the group kill"
    );
    assert!(
        wait_until(Duration::from_secs(5), || leader_pids(&length)
            .iter()
            .all(|pid| is_gone(*pid))),
        "the leader survived"
    );
}

/// Inverse control for the timeout case: a child that finishes in time returns
/// its outcome.
#[test]
fn a_child_that_finishes_before_its_deadline_returns_its_outcome() {
    let receiver = run_on_thread(
        SLEEP,
        vec!["0.2".to_owned()],
        ChildOutput::DEFAULT,
        Duration::from_secs(10),
    );

    let outcome = receiver
        .recv_timeout(Duration::from_secs(15))
        .expect("a 0.2 s child did not finish")
        .expect("a child that finishes in time is not an error");

    assert_eq!(outcome.status, 0);
}

/// The leader the core killed is collected: its pid is absent from `/proc`, not
/// left as a zombie, because this test process is its parent.
#[test]
fn a_timed_out_leader_is_reaped_and_not_left_a_zombie() {
    let length = unique_length(2);
    let _cleanup = KillOnDrop {
        argv: vec![SLEEP.to_owned(), length.clone()],
    };
    let deadline = Duration::from_millis(300);

    let receiver = run_on_thread(SLEEP, vec![length.clone()], ChildOutput::DEFAULT, deadline);
    let result = receiver
        .recv_timeout(patience(deadline))
        .expect("the call did not return");

    assert!(matches!(
        result,
        Err(SpawnError::Bound(BoundExceeded::TimedOut { .. }))
    ));
    assert!(
        grandchild_pids(&length).is_empty(),
        "the killed leader is still in /proc (a zombie, or alive): {:?}",
        grandchild_pids(&length)
    );
}

/// Output past the cap is an error naming the stream and the cap, returned long
/// before the deadline: `yes` never exits, so only the cap kill can end it.
#[test]
fn output_past_the_cap_is_an_error_and_returns_no_captured_byte() {
    let started = Instant::now();
    let receiver = run_on_thread(
        "/usr/bin/yes",
        Vec::new(),
        ChildOutput::Capture {
            stdout_cap: 1024,
            stderr_cap: ChildOutput::DEFAULT_CAP,
        },
        Duration::from_secs(60),
    );

    let result = receiver
        .recv_timeout(Duration::from_secs(30))
        .expect("the cap did not end the call");

    match result {
        Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
            program,
            stream,
            cap,
        })) => {
            assert_eq!(program, "/usr/bin/yes");
            assert_eq!(stream, OutputStream::Stdout);
            assert_eq!(cap, 1024);
        }
        other => panic!("expected an output overflow, got {other:?}"),
    }
    assert!(
        started.elapsed() < Duration::from_secs(30),
        "the call ended by its deadline and not by the cap"
    );
}

/// Exactly the cap is accepted.
#[test]
fn stdout_of_exactly_the_cap_is_accepted() {
    let receiver = run_on_thread(
        "/usr/bin/head",
        vec!["-c".to_owned(), "1024".to_owned(), "/dev/zero".to_owned()],
        ChildOutput::Capture {
            stdout_cap: 1024,
            stderr_cap: 1024,
        },
        Duration::from_secs(30),
    );

    let outcome = receiver
        .recv_timeout(Duration::from_secs(40))
        .expect("the call did not return")
        .expect("exactly the cap is accepted");

    assert_eq!(outcome.stdout.len(), 1024);
}

/// One byte over the cap is refused (the inverse of the case above).
#[test]
fn one_byte_over_the_cap_is_refused() {
    let receiver = run_on_thread(
        "/usr/bin/head",
        vec!["-c".to_owned(), "1024".to_owned(), "/dev/zero".to_owned()],
        ChildOutput::Capture {
            stdout_cap: 1023,
            stderr_cap: 1023,
        },
        Duration::from_secs(30),
    );

    let result = receiver
        .recv_timeout(Duration::from_secs(40))
        .expect("the call did not return");

    assert!(matches!(
        result,
        Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
            stream: OutputStream::Stdout,
            cap: 1023,
            ..
        }))
    ));
}

/// The cap applies to standard error too, and the error names that stream.
#[test]
fn stderr_past_its_cap_is_refused_naming_standard_error() {
    let receiver = run_on_thread(
        "/usr/bin/cat",
        vec![
            "/nonexistent-1".to_owned(),
            "/nonexistent-2".to_owned(),
            "/nonexistent-3".to_owned(),
        ],
        ChildOutput::Capture {
            stdout_cap: ChildOutput::DEFAULT_CAP,
            stderr_cap: 16,
        },
        Duration::from_secs(30),
    );

    let result = receiver
        .recv_timeout(Duration::from_secs(40))
        .expect("the call did not return");

    assert!(matches!(
        result,
        Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
            stream: OutputStream::Stderr,
            cap: 16,
            ..
        }))
    ));
}

/// A child that fills standard error while standard output waits does not
/// deadlock the call: the streams are drained concurrently.
#[test]
fn a_child_flooding_standard_error_while_standard_output_waits_does_not_deadlock() {
    let mut payload = tempfile::NamedTempFile::new().unwrap();
    payload.write_all(&vec![b'a'; 200 * 1024]).unwrap();
    payload.flush().unwrap();

    // The file goes to standard output first, then every missing path puts a
    // line on standard error: well over one MiB, far past a pipe buffer.
    let mut arguments = vec![payload.path().display().to_string()];
    arguments.extend((0..20_000).map(|index| format!("/nonexistent-{index}")));

    let receiver = run_on_thread(
        "/usr/bin/cat",
        arguments,
        ChildOutput::DEFAULT,
        Duration::from_secs(20),
    );
    let outcome = receiver
        .recv_timeout(Duration::from_secs(30))
        .expect("the call did not return")
        .expect("both streams fit their caps");

    assert_eq!(outcome.status, 1);
    assert_eq!(outcome.stdout.len(), 200 * 1024);
    assert!(
        outcome.stderr.len() > 1_000_000,
        "standard error was {} bytes",
        outcome.stderr.len()
    );
}

/// A descendant that leaves the process group and keeps the pipe open cannot
/// hold the call past the deadline plus the reap ceiling: the leader exited, the
/// pipe is still held, and the call counts as timed out.
#[test]
fn a_descendant_that_leaves_the_group_cannot_hold_the_call_past_deadline_plus_reap_ceiling() {
    let length = unique_length(3);
    let _cleanup = KillOnDrop {
        argv: vec![SLEEP.to_owned(), length.clone()],
    };
    let deadline = Duration::from_millis(300);

    let receiver = run_on_thread(
        "/usr/bin/setsid",
        vec!["--fork".to_owned(), SLEEP.to_owned(), length.clone()],
        ChildOutput::DEFAULT,
        deadline,
    );
    let result = receiver
        .recv_timeout(patience(deadline))
        .expect("a descendant outside the group held the call past the deadline");

    assert!(
        matches!(
            result,
            Err(SpawnError::Bound(BoundExceeded::TimedOut { .. }))
        ),
        "got {result:?}"
    );
    // The detached sleep is outside the group, so it is still alive: the case
    // really exercised "left the group and kept the pipe".
    assert!(
        !grandchild_pids(&length).iter().all(|pid| is_gone(*pid)),
        "the detached sleep never ran; the stand-in did not exercise the case"
    );
}

/// Bytes given as input reach the child, and its output comes back.
#[test]
fn bytes_given_as_input_reach_the_child_and_its_output_comes_back() {
    let outcome = run_until(
        request(
            Path::new("/usr/bin/cat"),
            &[],
            ChildInput::Bytes(b"hello from the core\n"),
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(30),
    )
    .unwrap();

    assert_eq!(outcome.status, 0);
    assert_eq!(outcome.stdout, "hello from the core\n");
}

/// A file given as input is the child's standard input.
#[test]
fn a_file_given_as_input_is_the_childs_standard_input() {
    let mut source = tempfile::NamedTempFile::new().unwrap();
    source.write_all(b"from a file\n").unwrap();
    source.flush().unwrap();
    let file = std::fs::File::open(source.path()).unwrap();

    let outcome = run_until(
        request(
            Path::new("/usr/bin/cat"),
            &[],
            ChildInput::File(file),
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(30),
    )
    .unwrap();

    assert_eq!(outcome.stdout, "from a file\n");
}

/// Discarded output comes back empty and is never refused for its size.
#[test]
fn discarded_output_comes_back_empty_and_is_never_refused_for_size() {
    let outcome = run_until(
        request(
            Path::new("/usr/bin/head"),
            &["-c", "10485760", "/dev/zero"],
            ChildInput::Null,
            ChildOutput::Discard,
        ),
        Duration::from_secs(30),
    )
    .unwrap();

    assert_eq!(outcome.status, 0);
    assert!(outcome.stdout.is_empty());
    assert!(outcome.stderr.is_empty());
}

/// A child killed by a signal the core did not send reports status minus one.
#[test]
fn a_child_killed_by_a_signal_the_core_did_not_send_reports_minus_one() {
    let length = unique_length(4);
    let _cleanup = KillOnDrop {
        argv: vec![SLEEP.to_owned(), length.clone()],
    };

    let receiver = run_on_thread(
        SLEEP,
        vec![length.clone()],
        ChildOutput::DEFAULT,
        Duration::from_secs(60),
    );
    assert!(
        wait_until(Duration::from_secs(5), || !grandchild_pids(&length)
            .is_empty()),
        "the stand-in never appeared"
    );
    for pid in pids_with_argv(&[SLEEP, &length]) {
        kill_pid(pid);
    }

    let outcome = receiver
        .recv_timeout(Duration::from_secs(30))
        .expect("the call did not notice the child was killed")
        .expect("a signalled child is an outcome, not an error");

    assert_eq!(outcome.status, -1);
}

/// A megabyte of input: well past the pipe buffer, so the writer is still
/// blocked, or failing, when a child that never reads has already gone.
const LARGE_INPUT: usize = 1024 * 1024;

/// A child that exits non-zero without reading its input reports its OWN
/// status, not an input failure: the writer's broken pipe must not pre-empt the
/// child's verdict, whichever finishes first.
#[test]
fn a_child_that_exits_nonzero_before_reading_its_input_reports_its_own_status() {
    let input = vec![b'x'; LARGE_INPUT];

    let outcome = run_until(
        request(
            Path::new("/usr/bin/false"),
            &[],
            ChildInput::Bytes(&input),
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(10),
    )
    .expect("a non-zero exit is the child's verdict, not an error");

    assert_eq!(outcome.status, 1);
}

/// Inverse control for the case above: a child that exits `0` without reading
/// its input did not get what it was sent, and that stays an input failure.
#[test]
fn a_child_that_exits_zero_without_reading_its_input_is_an_input_failure() {
    let input = vec![b'x'; LARGE_INPUT];

    let result = run_until(
        request(
            Path::new("/usr/bin/true"),
            &[],
            ChildInput::Bytes(&input),
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(10),
    );

    assert!(
        matches!(result, Err(SpawnError::Input { .. })),
        "expected an input failure, got {result:?}"
    );
}

/// Invalid UTF-8 on standard output is replaced, not refused.
#[test]
fn invalid_utf8_output_is_decoded_lossily_not_refused() {
    let outcome = run_until(
        request(
            Path::new("/usr/bin/printf"),
            &["\\377"],
            ChildInput::Null,
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(10),
    )
    .unwrap();

    assert_eq!(outcome.stdout, "\u{fffd}");
}

/// Control for the case above: valid UTF-8 comes back untouched.
#[test]
fn valid_utf8_output_is_returned_unchanged() {
    let outcome = run_until(
        request(
            Path::new("/usr/bin/printf"),
            &["h\u{e9}llo"],
            ChildInput::Null,
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(10),
    )
    .unwrap();

    assert_eq!(outcome.stdout, "h\u{e9}llo");
}

/// `run_bounded` itself (not only the seam) runs a real program under its class.
#[test]
fn run_bounded_runs_a_program_under_its_call_class() {
    let outcome = run_bounded(SpawnRequest {
        program: Path::new("/usr/bin/echo"),
        arguments: &["via the class"],
        class: CallClass::Query,
        input: ChildInput::Null,
        output: ChildOutput::DEFAULT,
    })
    .unwrap();

    assert_eq!(outcome.stdout, "via the class\n");
}

/// The cleared environment holds for a child that is given input bytes, which
/// takes the other branch of the input choice.
///
/// The bytes are empty because `env` never reads its standard input: a non-empty
/// payload races the child's exit and fails the write with a broken pipe, which is
/// a real `Input` error and not what this test is about.
#[test]
fn a_child_given_input_bytes_receives_only_the_declared_environment() {
    let outcome = run_until(
        request(
            Path::new("/usr/bin/env"),
            &[],
            ChildInput::Bytes(b""),
            ChildOutput::DEFAULT,
        ),
        Duration::from_secs(30),
    )
    .unwrap();

    let mut seen: Vec<&str> = outcome.stdout.lines().collect();
    seen.sort_unstable();
    let mut declared: Vec<String> = crate::utils::apply_child_environment::CHILD_ENVIRONMENT
        .iter()
        .map(|(name, value)| format!("{name}={value}"))
        .collect();
    declared.sort_unstable();

    assert_eq!(seen, declared, "the child saw:\n{}", outcome.stdout);
}
