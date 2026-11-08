//! A finished import onto its response.

use maran_ops::backup::ImportedBackup;

use crate::proto::{
    ImportBackupOk, ImportBackupResponse, ImportRefusalReason, import_backup_response,
};
use crate::services::transfer::import_failure::ImportFailure;

/// Builds the response an import ends with. The refusal reason travels beside the
/// error and only with it.
#[must_use]
pub fn reported_import(outcome: Result<ImportedBackup, ImportFailure>) -> ImportBackupResponse {
    match outcome {
        Ok(imported) => ImportBackupResponse {
            result: Some(import_backup_response::Result::Ok(ImportBackupOk {
                backup_id: imported.backup_id,
                artifact_bytes: imported.artifact_bytes,
                artifact_sha256: imported.artifact_sha256,
                database_count: imported.database_count,
                already_present: imported.already_present,
            })),
            refusal_reason: ImportRefusalReason::Unspecified as i32,
        },
        Err(failure) => ImportBackupResponse {
            result: Some(import_backup_response::Result::Error(failure.error)),
            refusal_reason: failure.reason as i32,
        },
    }
}
