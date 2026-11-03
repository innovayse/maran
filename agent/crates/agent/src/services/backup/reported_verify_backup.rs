//! What a backup verification looks like on the wire.

use maran_ops::backup::BackupVerification;

use crate::proto::{AgentError, VerifyBackupOk, verify_backup_response};

/// Maps one `VerifyBackup` outcome onto the response's `oneof`.
///
/// The ok arm carries exactly what the operation established and nothing it
/// did not: the three figures are copied from the [`BackupVerification`] the
/// operation returned only after every one of its checks ran to completion, so
/// a verification that could not read the artifact travels as the error arm and
/// never as an ok with zeroes (threat note R-G1). The error arm is passed
/// through untouched — it was already mapped from a typed `BackupError`, so no
/// archive-derived text is added here (R-F1).
#[must_use]
pub fn reported_verify_backup(
    outcome: Result<BackupVerification, AgentError>,
) -> verify_backup_response::Result {
    match outcome {
        Ok(verification) => verify_backup_response::Result::Ok(VerifyBackupOk {
            member_count: verification.member_count,
            database_count: verification.database_count,
            artifact_bytes: verification.artifact_bytes,
        }),
        Err(error) => verify_backup_response::Result::Error(error),
    }
}

#[cfg(test)]
#[path = "../../tests/services/backup/reported_verify_backup_tests.rs"]
mod tests;
