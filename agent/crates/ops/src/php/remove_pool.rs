//! Taking one php-fpm pool away through the same protocol that writes one.

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;
use maran_distro::DistroAdapter;

use crate::php::model::pool_paths::PoolPaths;
use crate::php::write_pool::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::php::{PhpHost, PhpOpError};

/// Removes `account`'s pool at `version`, then validates and reloads that
/// version's php-fpm.
///
/// This is the operation whose absence armed the worst trap in the product. A
/// pool file names the account it runs as, and php-fpm resolves that name at
/// startup: once the account is gone, `php-fpm -t` answers
/// `cannot get uid for user '<account>'` and **the master refuses to start or
/// reload at all**. So one deleted customer did not break one customer's
/// sites — it left a file that made the NEXT reload, hours or days later and
/// for a completely unrelated reason, take PHP down for every tenant on the
/// server. Cause and symptom separated by days is the worst shape a defect
/// can have, and nothing removed a pool anywhere in the agent.
///
/// The validator and the reload are built exactly as
/// [`super::write_pool::write_pool`] builds them, from the same constants and
/// the same per-version [`DistroAdapter`] methods: validating 8.3's pool with
/// 8.1's binary, or reloading a different master, would be a second opinion
/// about which php-fpm this file belongs to.
///
/// A pool that is not there is a success that runs nothing — no validator, no
/// reload. Every caller is in that position: a pool exists only if the account
/// ever used that version, and no caller knows which versions it used.
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`] for a version outside the closed
/// set. Returns [`PhpOpError::PoolValidation`] when `php-fpm -t` refuses the
/// tree once the file is gone and [`PhpOpError::ReloadFailed`] when the reload
/// refuses — in both cases the pool has been put back — and
/// [`PhpOpError::ConfigWrite`] for every other failure of the protocol.
pub fn remove_pool(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    version: &PhpVersion,
) -> Result<(), PhpOpError> {
    crate::php::supported_versions::ensure_supported(version)?;

    let paths = PoolPaths::for_pool(distro, account, version);

    // Both derived from validated types, never formatted from anything the
    // caller sent: an `AccountName` cannot contain `/` or `..` and a
    // `PhpVersion` is two groups of digits, so the path below cannot leave the
    // version's own pool directory. That matters more for a REMOVAL than for a
    // write — a write that escaped its directory creates a file somebody will
    // notice, and an unlink that escaped it destroys one nobody will.
    let validator_program = distro.php_fpm_binary(version.as_str());
    let validator = crate::safe_write::model::Validator {
        program: &validator_program,
        arguments: &[VALIDATE_ARGUMENT],
    };
    let service = distro.php_fpm_service(version.as_str());
    let reload_arguments = [RELOAD_SUBCOMMAND, service.as_str()];
    let reload = crate::safe_write::model::Reload {
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.remove_config(&paths.config_path, &validator, &reload)
}

#[cfg(test)]
#[path = "../tests/php/remove_pool_tests.rs"]
mod tests;
