//! Tests for the wire error a stalled upload becomes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use super::StalledUpload;
use crate::proto::{AgentError, ErrorCode};

#[test]
fn a_stalled_upload_is_reported_as_a_dropped_stream_naming_the_silence() {
    let error = AgentError::from(StalledUpload {
        limit: Duration::from_secs(30),
    });

    assert_eq!(error.code, ErrorCode::StreamDropped as i32);
    assert!(error.message.contains("30 seconds"));
    assert!(error.tool_output.is_empty());
}
