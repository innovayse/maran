//! Tests for [`tail_terminal`].
//!
//! One decision, four cases, and the case that matters is the one that was
//! missing: an ending the AGENT chose must produce a message. Without this
//! test, deleting that arm changed nothing any test could see — which a
//! mutation run found, and which is why the decision was pulled out of the
//! handler and given a name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::sites::{SitesOpError, TailEnd};

use super::tail_terminal;
use crate::proto::ErrorCode;

#[test]
fn a_client_that_closed_its_own_stream_is_told_nothing() {
    assert!(tail_terminal(&Ok(TailEnd::ClientClosed)).is_none());
}

#[test]
fn a_client_the_agent_dropped_is_told_that_it_was_dropped() {
    let error = tail_terminal(&Ok(TailEnd::ClientStalled))
        .expect("an ending the agent chose must be reported");

    assert_eq!(error.code, ErrorCode::StreamDropped as i32);
    assert!(
        error.message.contains("stopped reading"),
        "the operator must be able to tell why, got {:?}",
        error.message
    );
}

#[test]
fn an_idled_out_stream_says_so_rather_than_just_stopping() {
    let error =
        tail_terminal(&Ok(TailEnd::Idle)).expect("an ending the agent chose must be reported");

    // Its own code, and NOT `SYSTEM_FAILURE`: an idle-out is benign and
    // expected, and reporting it under the code a failed `nginx -t` gets would
    // make every quiet site look like a fault.
    assert_eq!(error.code, ErrorCode::StreamIdle as i32);
    assert_ne!(error.code, ErrorCode::SystemFailure as i32);

    // The two agent-chosen endings are told apart by their CODE. The previous
    // version of this test asserted the two messages differ, which pinned
    // English prose as the contract and would have turned a translation into a
    // test failure — the panel must never have to string-match to know what
    // happened.
    let stalled = tail_terminal(&Ok(TailEnd::ClientStalled)).unwrap();
    assert_ne!(error.code, stalled.code);
}

#[test]
fn a_failed_tail_still_reports_its_own_error_and_not_an_ending() {
    let error = tail_terminal(&Err(SitesOpError::LogUnreadable {
        path: "/home/acme/logs/example.com.access.log".to_owned(),
    }))
    .expect("a failure must be reported");

    assert!(
        error.message.contains("example.com.access.log"),
        "the operation's own message must survive, got {:?}",
        error.message
    );
}
