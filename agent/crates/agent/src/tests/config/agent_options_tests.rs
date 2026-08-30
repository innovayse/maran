//! Tests for the `agent_options` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `agent_options.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::{AgentOptions, OptionsError};

/// The uid the caller passes in when `--allow-uid` is absent.
const DEFAULT_UID: u32 = 1000;

/// Builds an argument vector from string literals.
fn arguments(values: &[&str]) -> Vec<String> {
    values.iter().map(|value| (*value).to_owned()).collect()
}

#[test]
fn no_arguments_yields_the_production_socket_and_the_default_uid() {
    let options = AgentOptions::parse(&arguments(&[]), DEFAULT_UID).unwrap();

    assert_eq!(options.socket_path(), Path::new("/run/maran/agent.sock"));
    assert_eq!(options.allow_uid, DEFAULT_UID);
}

#[test]
fn socket_and_allow_uid_flags_are_honoured() {
    let options = AgentOptions::parse(
        &arguments(&["--socket", "/tmp/maran-test.sock", "--allow-uid", "4242"]),
        DEFAULT_UID,
    )
    .unwrap();

    assert_eq!(options.socket_path(), Path::new("/tmp/maran-test.sock"));
    assert_eq!(options.allow_uid, 4242);
}

#[test]
fn unknown_arguments_are_ignored() {
    let options = AgentOptions::parse(
        &arguments(&["--from-a-newer-unit-file", "--allow-uid", "77"]),
        DEFAULT_UID,
    )
    .unwrap();

    assert_eq!(options.allow_uid, 77);
}

#[test]
fn non_numeric_allow_uid_is_rejected_instead_of_falling_back() {
    let error = AgentOptions::parse(&arguments(&["--allow-uid", "panel"]), DEFAULT_UID)
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
    let error = AgentOptions::parse(&arguments(&["--allow-uid"]), DEFAULT_UID)
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
    let error = AgentOptions::parse(&arguments(&["--socket"]), DEFAULT_UID)
        .expect_err("a dangling flag must not parse");

    assert_eq!(error, OptionsError::MissingValue { flag: "--socket" });
}
