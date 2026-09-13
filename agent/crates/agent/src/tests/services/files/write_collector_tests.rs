//! Tests for the assembly of a client-streamed write.
//!
//! Every one of these drives `WriteCollector` itself. The first version of this
//! file tested only the private budget arithmetic, and three protections —
//! the one-header rule, the cap's VALUE, and refuse-rather-than-mask on the mode
//! — survived mutation because nothing drove the function they lived in.
//!
//! The byte counts below are LITERALS. Deriving them from `MAXIMUM_CONTENT`
//! pins the arithmetic and leaves the number free, which is how a sixteenfold
//! rise in the daemon's per-rpc memory bound went unnoticed in review.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_CONTENT, WriteCollector, within_budget};
use crate::proto::{ErrorCode, WriteFileHeader, WriteFileRequest};

/// The challenge path a real ACME issuance asks for.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// A header for `CHALLENGE` with `mode`.
fn header(mode: u32) -> WriteFileHeader {
    WriteFileHeader {
        account_username: "acme".to_owned(),
        path: CHALLENGE.to_owned(),
        mode,
    }
}

/// The first message of an ordinary write: header plus the whole body.
fn first(body: &[u8]) -> WriteFileRequest {
    WriteFileRequest {
        header: Some(header(0o644)),
        chunk: body.to_vec(),
    }
}

/// A continuation message carrying `body` and no header.
fn chunk(body: &[u8]) -> WriteFileRequest {
    WriteFileRequest {
        header: None,
        chunk: body.to_vec(),
    }
}

#[test]
fn a_header_and_a_body_become_the_operations_input() {
    let mut collector = WriteCollector::new();
    collector.accept(first(b"token123.key-auth")).unwrap();

    let input = collector.finish().unwrap();

    assert_eq!(input.account.as_str(), "acme");
    assert_eq!(input.path.file_name(), "token123");
    assert_eq!(input.contents, b"token123.key-auth");
    assert_eq!(input.mode.bits(), 0o644);
}

#[test]
fn the_chunks_are_joined_in_the_order_they_arrived() {
    let mut collector = WriteCollector::new();
    collector.accept(first(b"one")).unwrap();
    collector.accept(chunk(b"two")).unwrap();
    collector.accept(chunk(b"three")).unwrap();

    assert_eq!(collector.finish().unwrap().contents, b"onetwothree");
}

#[test]
fn a_stream_that_never_sent_a_header_is_refused() {
    let mut collector = WriteCollector::new();
    collector
        .accept(chunk(b"bytes for a file nobody named"))
        .unwrap();

    let refused = collector.finish().unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_stream_is_refused_rather_than_writing_an_empty_file_nowhere() {
    assert_eq!(
        WriteCollector::new().finish().unwrap_err().code,
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_second_header_cannot_redirect_a_write_that_is_already_under_way() {
    let mut collector = WriteCollector::new();
    collector.accept(first(b"bytes sent for one file")).unwrap();

    let refused = collector
        .accept(WriteFileRequest {
            header: Some(WriteFileHeader {
                account_username: "acme".to_owned(),
                path: "sites/example.com/.well-known/acme-challenge/other".to_owned(),
                mode: 0o644,
            }),
            chunk: Vec::new(),
        })
        .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_header_arriving_after_the_first_message_is_refused_even_when_it_is_the_only_one() {
    let mut collector = WriteCollector::new();
    collector.accept(chunk(b"bytes before the header")).unwrap();

    let refused = collector.accept(first(b"more")).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_body_of_exactly_one_mebibyte_is_accepted() {
    let mut collector = WriteCollector::new();

    collector.accept(first(&vec![0_u8; 1_048_576])).unwrap();

    assert_eq!(collector.finish().unwrap().contents.len(), 1_048_576);
}

#[test]
fn a_body_of_one_mebibyte_and_one_byte_is_refused_rather_than_truncated() {
    let mut collector = WriteCollector::new();

    let refused = collector.accept(first(&vec![0_u8; 1_048_577])).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_cap_is_on_the_whole_body_and_not_on_one_chunk() {
    let mut collector = WriteCollector::new();
    collector.accept(first(&vec![0_u8; 1_048_576])).unwrap();

    let refused = collector.accept(chunk(b"x")).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_setuid_mode_is_refused_at_the_service_boundary_and_never_masked_into_a_plain_one() {
    let mut collector = WriteCollector::new();
    collector
        .accept(WriteFileRequest {
            header: Some(header(0o4755)),
            chunk: Vec::new(),
        })
        .unwrap();

    let refused = collector.finish().unwrap_err();

    assert_eq!(
        refused.code,
        ErrorCode::InvalidInput as i32,
        "a mode the agent will not set is a refusal, not a mode to be trimmed"
    );
}

#[test]
fn an_account_name_the_agent_refuses_is_refused_here_too() {
    let mut collector = WriteCollector::new();
    collector
        .accept(WriteFileRequest {
            header: Some(WriteFileHeader {
                account_username: "ROOT".to_owned(),
                path: CHALLENGE.to_owned(),
                mode: 0o644,
            }),
            chunk: Vec::new(),
        })
        .unwrap();

    assert_eq!(
        collector.finish().unwrap_err().code,
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_path_that_traverses_out_of_the_home_is_refused_here_too() {
    let mut collector = WriteCollector::new();
    collector
        .accept(WriteFileRequest {
            header: Some(WriteFileHeader {
                account_username: "acme".to_owned(),
                path: "../../etc/shadow".to_owned(),
                mode: 0o644,
            }),
            chunk: Vec::new(),
        })
        .unwrap();

    assert_eq!(
        collector.finish().unwrap_err().code,
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn the_cap_constant_is_the_one_mebibyte_the_contract_promises() {
    // Stated as a literal on both sides on purpose: this is the one test whose
    // job is the NUMBER rather than the arithmetic, and `files.proto` names the
    // same number to callers.
    assert_eq!(MAXIMUM_CONTENT, 1_048_576);
}

#[test]
fn a_length_that_would_overflow_the_sum_is_refused_rather_than_wrapping_into_success() {
    assert!(!within_budget(usize::MAX, 1));
    assert!(!within_budget(usize::MAX / 2 + 1, usize::MAX / 2 + 1));
}
