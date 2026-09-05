//! Tests for the permission bits the agent will hand a customer's file.
//!
//! The three bits are asserted one at a time rather than together: a single
//! test over a mask would stay green while two of the three stopped being
//! refused.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::FileMode;
use crate::validation::fs::file_mode_error::FileModeError;

#[test]
fn the_mode_an_acme_challenge_needs_is_accepted_and_kept_exactly() {
    assert_eq!(FileMode::parse(0o644).unwrap().bits(), 0o644);
}

#[test]
fn every_plain_permission_mode_is_accepted_including_the_widest_and_the_narrowest() {
    assert_eq!(FileMode::parse(0o000).unwrap().bits(), 0o000);
    assert_eq!(FileMode::parse(0o777).unwrap().bits(), 0o777);
    assert_eq!(FileMode::parse(0o600).unwrap().bits(), 0o600);
}

#[test]
fn a_setuid_mode_is_refused_rather_than_masked() {
    assert_eq!(
        FileMode::parse(0o4755).unwrap_err(),
        FileModeError::NotAPlainPermissionMode
    );
}

#[test]
fn a_setgid_mode_is_refused_rather_than_masked() {
    assert_eq!(
        FileMode::parse(0o2755).unwrap_err(),
        FileModeError::NotAPlainPermissionMode
    );
}

#[test]
fn a_sticky_mode_is_refused_rather_than_masked() {
    assert_eq!(
        FileMode::parse(0o1755).unwrap_err(),
        FileModeError::NotAPlainPermissionMode
    );
}

#[test]
fn a_file_type_bit_a_caller_has_no_business_sending_is_refused_too() {
    // `S_IFREG`. A caller that sent a whole `st_mode` rather than its low bits
    // is a caller whose request the agent does not understand.
    assert_eq!(
        FileMode::parse(0o100_644).unwrap_err(),
        FileModeError::NotAPlainPermissionMode
    );
}
