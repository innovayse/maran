//! Tests for [`BoundExceeded`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::output_stream::OutputStream;

/// A timeout names the program and the deadline, and says the kill does not
/// cancel work handed to somebody else.
#[test]
fn a_timeout_names_the_program_and_the_deadline() {
    let error = BoundExceeded::TimedOut {
        program: "/usr/bin/nginx".to_owned(),
        deadline: Duration::from_secs(60),
    };
    let text = error.to_string();

    assert!(text.contains("/usr/bin/nginx"), "{text}");
    assert!(text.contains("60s"), "{text}");
    assert!(text.contains("process group"), "{text}");
    assert!(text.contains("may still run"), "{text}");
}

/// An overflow names the program, the stream and the cap, and says no output was kept.
#[test]
fn an_output_overflow_names_the_program_the_stream_and_the_cap() {
    let error = BoundExceeded::OutputTooLarge {
        program: "/usr/sbin/nft".to_owned(),
        stream: OutputStream::Stderr,
        cap: 4_194_304,
    };
    let text = error.to_string();

    assert!(text.contains("/usr/sbin/nft"), "{text}");
    assert!(text.contains("standard error"), "{text}");
    assert!(text.contains("4194304"), "{text}");
    assert!(text.contains("none of its output"), "{text}");
}
