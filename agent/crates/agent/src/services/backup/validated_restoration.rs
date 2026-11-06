//! The checks a `RestoreBackup` request passes before `ops` is asked anything.

use maran_agent_core::validation::system::name::AccountName;

use crate::proto::{AgentError, RestoreBackupRequest};
use crate::services::backup::home_group::home_group;
use crate::services::backup::validated_restore::ValidatedRestore;
use crate::services::wire::validated_account::validated_account;

/// Revalidates everything a restore carries, and resolves the group its home
/// must end up owned by.
///
/// The group is resolved BEFORE the operation starts rather than inside it, so
/// a host whose web server group is missing refuses the restore instead of
/// discovering it at the finalising step — after the databases have been
/// replaced.
///
/// # Errors
///
/// The wire error for an account name, a destination, an id, a checksum or a
/// database name the agent will not accept, or a system failure when the web
/// server's group cannot be resolved.
pub fn validated_restoration(
    request: &RestoreBackupRequest,
    distro: &dyn maran_distro::DistroAdapter,
) -> Result<(AccountName, ValidatedRestore, u32), AgentError> {
    let account = validated_account(&request.account_username)?;
    let input = ValidatedRestore::from_request(request, &account)?;
    let group = home_group(distro)?;

    Ok((account, input, group))
}
