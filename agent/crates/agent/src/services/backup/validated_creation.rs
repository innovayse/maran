//! The checks a `CreateBackup` request passes before `ops` is asked anything.

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::{AgentError, CreateBackupRequest};
use crate::services::backup::validated_destination::validated_destination;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Revalidates everything a creation carries.
///
/// A free function rather than a method so that the three checks are one unit a
/// test can drive without a host (rules/rust.md "Service anatomy": a decision
/// inlined in a handler can be deleted without a test going red).
///
/// # Errors
///
/// The wire error for an account name, a backup id or a destination the agent
/// will not accept.
pub fn validated_creation(
    request: &CreateBackupRequest,
) -> Result<(AccountName, BackupId, LocalBackupRoot), AgentError> {
    let account = validated_account(&request.account_username)?;
    let backup_id =
        BackupId::parse(&request.backup_id).map_err(|error| invalid_input(error.to_string()))?;
    let root = validated_destination(request.destination.as_ref())?;

    Ok((account, backup_id, root))
}
