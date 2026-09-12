//! Tests for the `command_outcome` module.
//!
//! The tests that matter are the two shadow ones. `ops::accounts` reads an
//! account's shadow entry with `getent shadow <account>` and the whole entry —
//! password hash included — lands in `CommandOutcome::stdout`. The question is
//! not whether anybody formats an outcome today (nobody does); it is whether a
//! `{:?}` anywhere COULD print the hash. So each test carries its own positive
//! control: it first asserts the hash is really in the value, and only then
//! that the formatting does not show it. Without that control the assertion
//! would pass just as loudly against an outcome that never held a hash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::CommandOutcome;

/// A real-shaped shadow entry: `name:hash:lastchange:min:max:warn:::`.
const SHADOW_ENTRY: &str =
    "acme:$y$j9T$Fd1TQEXAMPLEsaltEXAMPLE$b8dEXAMPLEhashEXAMPLEhashEXAMPLEhash:19700:0:99999:7:::\n";

/// The hash field alone, which is the part that must never be printed.
const SHADOW_HASH: &str = "$y$j9T$Fd1TQEXAMPLEsaltEXAMPLE$b8dEXAMPLEhashEXAMPLEhashEXAMPLEhash";

/// The outcome `ops::accounts::stored_password` actually holds.
fn shadow_outcome() -> CommandOutcome {
    CommandOutcome {
        status: 0,
        stdout: SHADOW_ENTRY.to_owned(),
        stderr: String::new(),
    }
}

#[test]
fn formatting_an_outcome_holding_a_shadow_entry_does_not_print_the_password_hash() {
    let outcome = shadow_outcome();

    // Positive control: the hash IS in the value, so the assertion below has
    // something to fail on.
    assert!(
        outcome.stdout.contains(SHADOW_HASH),
        "the fixture must hold the hash, or the next assertion proves nothing"
    );

    let rendered = format!("{outcome:?}");

    assert!(
        !rendered.contains(SHADOW_HASH),
        "the hash was printed: {rendered}"
    );
    assert!(
        !rendered.contains("acme:"),
        "the shadow entry was printed: {rendered}"
    );
}

#[test]
fn an_outcome_inside_a_derived_debug_struct_still_prints_no_hash() {
    /// The shape the leak actually travels in: a struct whose `Debug` is
    /// derived and which is logged whole.
    #[derive(Debug)]
    struct ShadowRead {
        program: &'static str,
        outcome: CommandOutcome,
    }

    let read = ShadowRead {
        program: "/usr/bin/getent",
        outcome: shadow_outcome(),
    };

    // Positive control, on the composed struct this time.
    assert!(
        read.outcome.stdout.contains(SHADOW_HASH),
        "the fixture must hold the hash, or the next assertion proves nothing"
    );

    let rendered = format!("{read:?}");

    assert!(
        rendered.contains(read.program),
        "the derived Debug must still print its other fields: {rendered}"
    );
    assert!(
        !rendered.contains(SHADOW_HASH),
        "the hash was printed through the derived Debug: {rendered}"
    );
}

#[test]
fn debug_prints_the_status_and_the_two_capture_lengths() {
    let outcome = CommandOutcome {
        status: 3,
        stdout: "abcde".to_owned(),
        stderr: "xy".to_owned(),
    };

    assert_eq!(
        format!("{outcome:?}"),
        "CommandOutcome { status: 3, stdout_len: 5, stderr_len: 2 }"
    );
}

#[test]
fn a_customers_crontab_in_stdout_is_not_printed_either() {
    let outcome = CommandOutcome {
        status: 0,
        stdout: "* * * * * /home/acme/secret-job.sh\n".to_owned(),
        stderr: String::new(),
    };

    assert!(
        outcome.stdout.contains("secret-job.sh"),
        "the fixture must hold the crontab line, or the next assertion proves nothing"
    );
    assert!(!format!("{outcome:?}").contains("secret-job.sh"));
}
