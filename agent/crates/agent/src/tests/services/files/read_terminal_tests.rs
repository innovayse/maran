//! Tests for `read_terminal`: which endings of a read owe the client a message.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::FilesOpError;

use super::read_terminal;
use crate::proto::ErrorCode;
use crate::services::wire::send_end::SendEnd;

#[test]
fn a_read_that_completed_owes_nothing() {
    assert_eq!(read_terminal(&Ok(10), None), None);
}

#[test]
fn a_failed_read_is_told_its_typed_error() {
    let error = read_terminal(&Err(FilesOpError::TooLarge), None).unwrap();

    assert_eq!(error.code, ErrorCode::TooLarge as i32);
}

#[test]
fn a_failed_read_is_told_its_error_even_when_the_client_also_left() {
    let error = read_terminal(&Err(FilesOpError::NotARegularFile), Some(SendEnd::Closed));

    assert_eq!(error.unwrap().code, ErrorCode::ValidationFailed as i32);
}

#[test]
fn a_stalled_client_is_told_the_stream_was_dropped_and_not_left_with_a_clean_end() {
    let error = read_terminal(&Ok(65_536), Some(SendEnd::Stalled)).unwrap();

    assert_eq!(error.code, ErrorCode::StreamDropped as i32);
    assert!(error.tool_output.is_empty());
}

#[test]
fn a_client_that_closed_the_stream_has_nobody_to_tell() {
    assert_eq!(read_terminal(&Ok(65_536), Some(SendEnd::Closed)), None);
}
