//! Tests for the validation of a client-streamed write.
//!
//! Every one of these drives `WriteCollector` itself, because a `tonic::Streaming`
//! cannot be built by a unit test and anything decided in the transport loop would
//! be reachable by none.
//!
//! The byte counts below are LITERALS. Deriving them from the cap under test pins
//! the arithmetic and leaves the number free.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{WriteCollector, within_budget};
use crate::proto::{ErrorCode, WriteFileHeader, WriteFileRequest};

/// The challenge path a real ACME issuance asks for.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// A header for `CHALLENGE` with `mode` and none of the optional fields.
fn header(mode: u32) -> WriteFileHeader {
    WriteFileHeader {
        account_username: "acme".to_owned(),
        path: CHALLENGE.to_owned(),
        mode,
        fail_if_exists: false,
        max_bytes: 0,
    }
}

/// The first message of an ordinary write: header plus the whole body.
fn first(body: &[u8]) -> WriteFileRequest {
    WriteFileRequest {
        header: Some(header(0o644)),
        chunk: body.to_vec(),
    }
}

/// A first message carrying `header` and no body.
fn opening(header: WriteFileHeader) -> WriteFileRequest {
    WriteFileRequest {
        header: Some(header),
        chunk: Vec::new(),
    }
}

/// A continuation message carrying `body` and no header.
fn chunk(body: &[u8]) -> WriteFileRequest {
    WriteFileRequest {
        header: None,
        chunk: body.to_vec(),
    }
}

/// The wire code of the refusal `accept` produced.
fn refused_code(message: WriteFileRequest) -> i32 {
    WriteCollector::new().accept(message).err().unwrap().code
}

#[test]
fn a_header_becomes_the_operations_input_and_the_body_is_passed_on() {
    let accepted = WriteCollector::new()
        .accept(first(b"token123.key-auth"))
        .unwrap();

    let input = accepted.input.unwrap();
    assert_eq!(input.account.as_str(), "acme");
    assert_eq!(input.path.file_name(), "token123");
    assert_eq!(input.mode.bits(), 0o644);
    assert!(!input.fail_if_exists);
    assert_eq!(input.limit.bytes(), 1_048_576);
    assert_eq!(accepted.chunk, b"token123.key-auth");
}

#[test]
fn later_messages_carry_chunks_and_no_input() {
    let mut collector = WriteCollector::new();
    collector.accept(first(b"one")).unwrap();

    let second = collector.accept(chunk(b"two")).unwrap();

    assert!(second.input.is_none());
    assert_eq!(second.chunk, b"two");
}

#[test]
fn the_new_header_fields_reach_the_input() {
    let accepted = WriteCollector::new()
        .accept(opening(WriteFileHeader {
            fail_if_exists: true,
            max_bytes: 50_000_000,
            ..header(0o600)
        }))
        .unwrap();

    let input = accepted.input.unwrap();
    assert!(input.fail_if_exists);
    assert_eq!(input.limit.bytes(), 50_000_000);
    assert_eq!(input.mode.bits(), 0o600);
}

#[test]
fn a_stream_whose_first_message_has_no_header_is_refused() {
    assert_eq!(
        refused_code(chunk(b"bytes for a file nobody named")),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_second_header_cannot_redirect_a_write_that_is_already_under_way() {
    let mut collector = WriteCollector::new();
    collector.accept(first(b"bytes sent for one file")).unwrap();

    let refused = collector
        .accept(opening(WriteFileHeader {
            path: "sites/example.com/.well-known/acme-challenge/other".to_owned(),
            ..header(0o644)
        }))
        .err()
        .unwrap();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_body_of_exactly_one_mebibyte_is_accepted_when_no_cap_is_stated() {
    let accepted = WriteCollector::new()
        .accept(first(&vec![0_u8; 1_048_576]))
        .unwrap();

    assert_eq!(accepted.chunk.len(), 1_048_576);
}

#[test]
fn a_body_of_one_mebibyte_and_one_byte_is_refused_when_no_cap_is_stated() {
    assert_eq!(
        refused_code(first(&vec![0_u8; 1_048_577])),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn the_cap_is_on_the_whole_body_and_not_on_one_chunk() {
    let mut collector = WriteCollector::new();
    collector.accept(first(&vec![0_u8; 1_048_576])).unwrap();

    let refused = collector.accept(chunk(b"x")).err().unwrap();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_stated_cap_replaces_the_default_in_both_directions() {
    let mut roomy = WriteCollector::new();
    let big = vec![0_u8; 3_000_000];
    roomy
        .accept(WriteFileRequest {
            header: Some(WriteFileHeader {
                max_bytes: 5_000_000,
                ..header(0o644)
            }),
            chunk: big,
        })
        .unwrap();

    let mut tight = WriteCollector::new();
    let refused = tight
        .accept(WriteFileRequest {
            header: Some(WriteFileHeader {
                max_bytes: 10,
                ..header(0o644)
            }),
            chunk: vec![0_u8; 11],
        })
        .err()
        .unwrap();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_cap_above_the_agents_ceiling_is_refused_and_the_ceiling_itself_is_accepted() {
    assert_eq!(
        refused_code(opening(WriteFileHeader {
            max_bytes: 268_435_457,
            ..header(0o644)
        })),
        ErrorCode::InvalidInput as i32
    );
    assert!(
        WriteCollector::new()
            .accept(opening(WriteFileHeader {
                max_bytes: 268_435_456,
                ..header(0o644)
            }))
            .is_ok()
    );
}

#[test]
fn a_setuid_mode_is_refused_at_the_service_boundary_and_never_masked_into_a_plain_one() {
    assert_eq!(
        refused_code(opening(header(0o4755))),
        ErrorCode::InvalidInput as i32,
        "a mode the agent will not set is a refusal, not a mode to be trimmed"
    );
}

#[test]
fn an_account_name_the_agent_refuses_is_refused_here_too() {
    assert_eq!(
        refused_code(opening(WriteFileHeader {
            account_username: "ROOT".to_owned(),
            ..header(0o644)
        })),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn paths_that_traverse_name_the_home_or_name_the_trash_are_refused() {
    for path in [
        "../../etc/shadow",
        "",
        ".maran-trash/entries/x",
        "/etc/passwd",
    ] {
        assert_eq!(
            refused_code(opening(WriteFileHeader {
                path: path.to_owned(),
                ..header(0o644)
            })),
            ErrorCode::InvalidInput as i32,
            "{path:?}"
        );
    }
}

#[test]
fn a_deep_file_manager_path_that_the_old_challenge_type_would_refuse_is_accepted() {
    let deep = ["d"; 12].join("/") + "/file.txt";

    let accepted = WriteCollector::new()
        .accept(opening(WriteFileHeader {
            path: deep,
            ..header(0o644)
        }))
        .unwrap();

    assert_eq!(accepted.input.unwrap().path.file_name(), "file.txt");
}

#[test]
fn a_length_that_would_overflow_the_sum_is_refused_rather_than_wrapping_into_success() {
    assert!(!within_budget(u64::MAX, 1, u64::MAX));
    assert!(within_budget(10, 0, 10));
    assert!(!within_budget(10, 1, 10));
}
