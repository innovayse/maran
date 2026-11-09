//! The checks a `VerifyBackup` request passes before `ops` is asked anything.

use maran_agent_core::validation::secrets::archive_key::ArchiveKey;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::{AgentError, VerifyBackupRequest};
use crate::services::backup::validated_archive_key::validated_archive_key;
use crate::services::backup::validated_destination::validated_destination;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Revalidates everything a verification carries: the account, the backup id
/// and the destination.
///
/// A free function rather than inline code in the handler so the three checks
/// are one unit a test can drive without a host (rules/rust.md "Service
/// anatomy"). Nothing in the request is a path: the artifact's location is
/// derived further down from the validated values and the agent's own root
/// (threat note R-C1, R-C2).
///
/// # Errors
///
/// The wire error for an account name, a backup id, a destination or an archive
/// key the agent will not accept; a destination that is not local is refused as
/// not implemented.
pub fn validated_verification(
    request: &VerifyBackupRequest,
) -> Result<(AccountName, BackupId, LocalBackupRoot, Option<ArchiveKey>), AgentError> {
    let account = validated_account(&request.account_username)?;
    let backup_id =
        BackupId::parse(&request.backup_id).map_err(|error| invalid_input(error.to_string()))?;
    let root = validated_destination(request.destination.as_ref())?;
    let key = validated_archive_key(request.destination.as_ref())?;

    Ok((account, backup_id, root, key))
}

#[cfg(test)]
#[path = "../../tests/services/backup/validated_verification_tests.rs"]
mod tests;
