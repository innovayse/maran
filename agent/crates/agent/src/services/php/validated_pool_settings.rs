//! What an `ApplyPhpPoolSettings` request carries, revalidated.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_ops::php::PoolInput;

use crate::proto::{AgentError, ApplyPhpPoolSettingsRequest};
use crate::services::sites::validated_overrides::validated_overrides;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the version, worker budget and settings of an
/// `ApplyPhpPoolSettings` request and bundles them with the already validated
/// `account` into the input the pool writer takes.
///
/// The account arrives validated because the handler names it on the audit
/// slot before anything else can fail, so a refused request is still attributed
/// to the account it was aimed at. The version is parsed here as the closed
/// two-component grammar; whether it is one the agent supports, and installed,
/// is the operation's decision. The worker budget is carried as sent: its range
/// is the operation's decision too, and it refuses rather than clamps. The
/// settings go through the same whitelist revalidation `CreateSite` and
/// `UpdateSitePhpVersion` use, so a name off the list is refused here and never
/// dropped, and a `post_max_size` below the `upload_max_filesize` of the same
/// set is refused with the rest.
///
/// # Errors
///
/// Returns the wire error for a version that is not two numeric components,
/// and for any setting that is not on the whitelist, is malformed or out of
/// range, carries a control character, or leaves the set unable to hold the
/// largest upload it allows.
pub fn validated_pool_settings(
    account: AccountName,
    request: &ApplyPhpPoolSettingsRequest,
) -> Result<PoolInput, AgentError> {
    let version =
        PhpVersion::parse(&request.version).map_err(|error| invalid_input(error.to_string()))?;
    let overrides = validated_overrides(&request.overrides)?;

    Ok(PoolInput {
        account,
        version,
        max_children: request.max_children,
        overrides,
    })
}

#[cfg(test)]
#[path = "../../tests/services/php/validated_pool_settings_tests.rs"]
mod tests;
