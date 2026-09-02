//! Tests for the `invocation` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `invocation.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::{Invocation, OptionsError, USAGE};

/// The uid the caller passes in when `--allow-uid` is absent.
const DEFAULT_UID: u32 = 1000;

/// Builds an argument vector from string literals.
fn arguments(values: &[&str]) -> Vec<String> {
    values.iter().map(|value| (*value).to_owned()).collect()
}

/// The options of an invocation that is expected to run the daemon.
///
/// # Panics
///
/// Panics when the command line asked for the usage text instead, which in
/// these tests means the parse did something other than what was being checked.
fn running(invocation: Invocation) -> super::AgentOptions {
    match invocation {
        Invocation::Run(options) => options,
        Invocation::ShowUsage => panic!("expected a runnable invocation, got the usage text"),
    }
}

#[test]
fn no_arguments_yields_the_production_socket_and_the_default_uid() {
    let options = running(Invocation::parse(&arguments(&[]), DEFAULT_UID).unwrap());

    assert_eq!(options.socket_path(), Path::new("/run/maran/agent.sock"));
    assert_eq!(options.allow_uid, DEFAULT_UID);
}

#[test]
fn socket_and_allow_uid_flags_are_honoured() {
    let options = running(
        Invocation::parse(
            &arguments(&["--socket", "/tmp/maran-test.sock", "--allow-uid", "4242"]),
            DEFAULT_UID,
        )
        .unwrap(),
    );

    assert_eq!(options.socket_path(), Path::new("/tmp/maran-test.sock"));
    assert_eq!(options.allow_uid, 4242);
}

#[test]
fn an_unknown_argument_is_refused_rather_than_skipped() {
    // This used to pass by IGNORING the unknown flag and honouring the rest, and
    // the consequence was observed rather than imagined: `maran-agent --help`
    // parsed as an empty command line and started a root daemon on the default
    // socket with the default uid, taking the socket from the agent already
    // serving it. A flag this binary does not define means the operator and the
    // binary disagree about what is being started, and the safe answer to that
    // is to start nothing.
    let error = Invocation::parse(
        &arguments(&["--from-a-newer-unit-file", "--allow-uid", "77"]),
        DEFAULT_UID,
    )
    .expect_err("an unrecognised flag must not parse");

    assert_eq!(
        error,
        OptionsError::UnknownFlag {
            flag: "--from-a-newer-unit-file".to_owned()
        }
    );
}

#[test]
fn asking_for_help_prints_usage_instead_of_starting_a_daemon() {
    for flag in ["--help", "-h"] {
        let invocation = Invocation::parse(&arguments(&[flag]), DEFAULT_UID)
            .unwrap_or_else(|error| panic!("{flag} must parse, got {error}"));

        assert_eq!(invocation, Invocation::ShowUsage, "{flag}");
    }

    // The text has to name both flags it documents, or it is not usage.
    assert!(USAGE.contains("--socket"));
    assert!(USAGE.contains("--allow-uid"));
}

#[test]
fn help_wins_over_a_malformed_flag_beside_it() {
    // Otherwise the one command that explains the mistake is the one refused
    // for containing it.
    let invocation = Invocation::parse(&arguments(&["--nonsense", "--help"]), DEFAULT_UID)
        .expect("help must parse even beside an unknown flag");

    assert_eq!(invocation, Invocation::ShowUsage);
}

#[test]
fn non_numeric_allow_uid_is_rejected_instead_of_falling_back() {
    let error = Invocation::parse(&arguments(&["--allow-uid", "panel"]), DEFAULT_UID)
        .expect_err("a non-numeric uid must not parse");

    assert_eq!(
        error,
        OptionsError::InvalidUid {
            value: "panel".to_owned()
        }
    );
}

#[test]
fn allow_uid_without_a_value_is_rejected_instead_of_falling_back() {
    let error = Invocation::parse(&arguments(&["--allow-uid"]), DEFAULT_UID)
        .expect_err("a dangling flag must not parse");

    assert_eq!(
        error,
        OptionsError::MissingValue {
            flag: "--allow-uid"
        }
    );
}

#[test]
fn socket_without_a_value_is_rejected() {
    let error = Invocation::parse(&arguments(&["--socket"]), DEFAULT_UID)
        .expect_err("a dangling flag must not parse");

    assert_eq!(error, OptionsError::MissingValue { flag: "--socket" });
}

#[test]
fn a_help_flag_in_a_value_position_is_still_help() {
    // Deliberate, and recorded because it is surprising: the help sweep runs over
    // the whole vector, so `--socket -h` prints usage instead of binding a socket
    // named `-h`. A socket path or a uid spelled `-h` or `--help` is not a thing
    // anyone means, and answering the question is better than binding it.
    let invocation = Invocation::parse(&arguments(&["--socket", "-h"]), DEFAULT_UID)
        .expect("a help flag anywhere must parse");

    assert_eq!(invocation, Invocation::ShowUsage);
}

#[test]
fn a_flag_used_as_another_flags_value_is_refused_rather_than_swallowed() {
    // `--socket --allow-uid 5` reads `--allow-uid` as the socket path and then
    // meets a bare `5`. The end state is safe either way — nothing starts — but
    // this pins WHICH refusal it is, so a future change that starts a daemon on
    // a socket literally named `--allow-uid` cannot pass unnoticed.
    let error = Invocation::parse(&arguments(&["--socket", "--allow-uid", "5"]), DEFAULT_UID)
        .expect_err("a flag consumed as a value must not yield a running daemon");

    assert_eq!(
        error,
        OptionsError::UnknownFlag {
            flag: "5".to_owned()
        }
    );
}
