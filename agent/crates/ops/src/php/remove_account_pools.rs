//! Every pool one account owns, taken away together.

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::remove_pool::remove_pool;
use crate::php::supported_versions::SUPPORTED_VERSIONS;
use crate::php::{PhpHost, PhpOpError};

/// Removes every pool `account` has, across every PHP version the panel
/// supports.
///
/// The agent asks the CLOSED supported set rather than being told which
/// versions to clean up, and that is the point: nobody knows which versions an
/// account has used. The panel's row says what a site is bound to *now*; it
/// does not remember that the account ran 8.1 for a year, and a pool left over
/// from a version nothing currently uses is exactly the one that survives
/// every targeted cleanup and takes the host down later.
///
/// Six `stat`s and, in practice, at most one or two removals — the loop asks
/// [`remove_pool`], which does nothing at all for a pool that is not there.
///
/// Stops at the first refusal instead of pressing on. A caller that is about to
/// delete the account needs to know the host is not in the state it asked for:
/// continuing would report success while leaving behind precisely the file
/// this function exists to remove.
///
/// # Errors
///
/// Returns whatever [`remove_pool`] refused, for the first version it refused
/// on. Every version before it has been removed and every version after it is
/// untouched — which is safe to retry, because removal is idempotent.
pub fn remove_account_pools(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
) -> Result<(), PhpOpError> {
    for supported in SUPPORTED_VERSIONS {
        // Parsed rather than trusted, even though the list is this crate's own
        // constant: `remove_pool` takes a validated type because that is what
        // makes its path join safe, and reaching for an unchecked constructor
        // here would be the one call site that could hand it something else.
        let version = PhpVersion::parse(supported).map_err(|error| PhpOpError::ConfigWrite {
            reason: format!("the supported version list holds an invalid version: {error}"),
        })?;

        remove_pool(host, distro, account, &version)?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/php/remove_account_pools_tests.rs"]
mod tests;
