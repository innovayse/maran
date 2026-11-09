//! Reading an `ExportBackup` request into validated input, or refusing it.

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::backup::package::package_signing_key::PackageSigningKey;

use crate::proto::{AgentError, ExportBackupRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Revalidates what an `ExportBackup` request carries: the account, the backup id
/// and the signing key.
///
/// The request is taken by value so the key bytes can be overwritten once the
/// library has parsed them: a best effort (the allocator may have copied them on
/// the way in, and the compiler may elide a dead store), but the copy this process
/// controls does not outlive the parse. The key is never part of an error message.
///
/// # Errors
///
/// `INVALID_INPUT` for a name or id that does not parse and for a key that is not
/// an unencrypted PKCS#8 ECDSA P-384 private key.
pub fn validated_export(
    mut request: ExportBackupRequest,
) -> Result<(AccountName, BackupId, PackageSigningKey), AgentError> {
    let account = validated_account(&request.account_username)?;
    let backup_id =
        BackupId::parse(&request.backup_id).map_err(|error| invalid_input(error.to_string()))?;

    let parsed = PackageSigningKey::from_pkcs8(&request.signing_key);
    request.signing_key.iter_mut().for_each(|byte| *byte = 0);
    let key = parsed.map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, backup_id, key))
}

#[cfg(test)]
#[path = "../../tests/services/transfer/validated_export_tests.rs"]
mod tests;
