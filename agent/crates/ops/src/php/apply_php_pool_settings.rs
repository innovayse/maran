//! ApplyPhpPoolSettings: rewriting an existing pool with a new worker budget
//! and a new set of customer settings.

use maran_distro::DistroAdapter;

use crate::php::list_php_versions::is_installed;
use crate::php::model::pool_input::PoolInput;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::render_pool::render_pool;
use crate::php::supported_versions::ensure_supported;
use crate::php::write_pool::{ensure_worker_budget, write_pool_under_lock};
use crate::php::{PhpHost, PhpOpError};

/// Makes the pool of `input.account` at `input.version` carry exactly the
/// worker budget and the settings in `input`, writing it only when it differs.
///
/// The pool is rendered through `render_pool`, the renderer the pool writer
/// itself uses, and compared byte for byte with the file on disk, as
/// `sites::converge_php_site` compares a pool. A pool that already equals its
/// render is not touched: no `php-fpm -t` and no reload. That is what makes a
/// repeated request, a panel that re-applies every account's pool after a plan
/// edit, and a start-up pass over thousands of accounts cheap instead of a
/// reload storm. A pool that differs is written by `write_pool_under_lock`,
/// so the config-tree lock, `php-fpm -t`, the reload and the rollback all
/// apply unchanged.
///
/// The request REPLACES the pool's setting set: a setting `input` does not
/// carry is not carried over from the file, it returns to the pool's default.
/// The settings arrive as [`crate::php::PhpOverride`]s, which only the
/// whitelist's parser can make, so this operation adds no setting name and
/// changes no bound.
///
/// It can never create a pool. The file must already exist, which is the
/// difference between this and `write_pool`: pool creation stays with
/// `CreateSite` and `UpdateSitePhpVersion`, which also write the vhost that
/// names the pool's socket. A pool written here for an account with no site
/// would be a listening socket nothing connects to, and one written for an
/// account that is being deleted would be the file naming a vanished user that
/// takes php-fpm down for every tenant.
///
/// The account's lock is taken FIRST and held to the end, because this
/// operation decides on a read: "the pool exists" and "the pool differs" are
/// facts about a file a concurrent deletion removes, and writing after that
/// removal is the exact interleaving the lock exists to exclude. It is the
/// wait-free lock of `write_pool`, taken at this entry point and not by
/// anything called below, so it adds no wait-for edge (rules/rust.md "What this
/// agent serialises").
///
/// # Errors
///
/// Returns [`PhpOpError::AccountBusy`] when another operation for this account
/// holds its lock; nothing is read or written. Returns
/// [`PhpOpError::UnsupportedVersion`] for a version outside the supported set,
/// [`PhpOpError::WorkerBudgetOutOfRange`] for a budget outside the range the
/// agent renders, and [`PhpOpError::PhpVersionNotInstalled`] for a supported
/// version this host lacks. Returns [`PhpOpError::PoolMissing`] when the
/// account has no pool file at that version. Returns
/// [`PhpOpError::OverridePostSizeBelowUpload`] when the set cannot hold the
/// largest upload it allows, and every other variant `write_pool_under_lock`
/// documents when a changed pool cannot be written: [`PhpOpError::PoolValidation`]
/// and [`PhpOpError::ReloadFailed`] with the previous pool restored,
/// [`PhpOpError::Bound`], [`PhpOpError::ConfigWrite`] and [`PhpOpError::Render`].
pub fn apply_php_pool_settings(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    input: &PoolInput,
) -> Result<(), PhpOpError> {
    // Wait-free, process-local and released by every return below, exactly as
    // in `write_pool`; nothing this function calls takes it again.
    let _guard = crate::accounts::take_account_lock(&input.account).ok_or_else(|| {
        PhpOpError::AccountBusy {
            username: input.account.as_str().to_owned(),
        }
    })?;

    ensure_supported(&input.version)?;
    ensure_worker_budget(input.max_children)?;

    if !is_installed(host, distro, &input.version) {
        return Err(PhpOpError::PhpVersionNotInstalled {
            version: input.version.as_str().to_owned(),
        });
    }

    let paths = PoolPaths::for_pool(distro, &input.account, &input.version);
    let Some(current) = host.read_config(&paths.config_path) else {
        return Err(PhpOpError::PoolMissing {
            username: input.account.as_str().to_owned(),
            version: input.version.as_str().to_owned(),
        });
    };

    // Also where the post_max_size / upload_max_filesize consistency is
    // enforced, before anything is compared or written.
    let wanted = render_pool(distro, input, &paths)?;
    if current == wanted {
        return Ok(());
    }

    write_pool_under_lock(host, distro, input)
}

#[cfg(test)]
#[path = "../tests/php/apply_php_pool_settings_tests.rs"]
mod tests;
