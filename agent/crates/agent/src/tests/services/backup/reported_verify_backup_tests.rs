//! Tests for how a backup verification reaches the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::backup::BackupVerification;

use super::reported_verify_backup;
use crate::proto::{AgentError, ErrorCode, VerifyBackupOk, verify_backup_response};

#[test]
fn a_verification_reaches_the_wire_with_the_three_figures_it_established() {
    let reported = reported_verify_backup(Ok(BackupVerification {
        member_count: 42,
        database_count: 2,
        artifact_bytes: 9_000_000_000,
    }));

    assert_eq!(
        reported,
        verify_backup_response::Result::Ok(VerifyBackupOk {
            member_count: 42,
            database_count: 2,
            artifact_bytes: 9_000_000_000,
        })
    );
}

#[test]
fn a_failure_reaches_the_wire_as_the_error_arm_and_never_as_an_empty_ok() {
    let error = AgentError {
        code: ErrorCode::ValidationFailed as i32,
        message: "the artifact does not match its checksum".to_owned(),
        tool_output: String::new(),
    };

    let reported = reported_verify_backup(Err(error.clone()));

    assert_eq!(reported, verify_backup_response::Result::Error(error));
}
