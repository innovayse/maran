//! WriteOpcacheDropIn: the hardened OPcache ini file of one PHP version.

use std::path::PathBuf;

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;
use maran_templates::php_fpm::opcache_drop_in::OpcacheDropIn;

use crate::php::list_php_versions::is_installed;
use crate::php::supported_versions::ensure_supported;
use crate::php::write_pool::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::model::{Reload, Validator};

/// The drop-in's file name inside the version's additional ini directory.
///
/// The `99-` prefix makes it the last file the version's php-fpm reads, so its
/// lines win over any earlier ini file that sets the same OPcache directive. The
/// directory is the adapter's (`php_ini_drop_in_directory`); the name is a Maran
/// fact, the same on every family.
const DROP_IN_FILE_NAME: &str = "99-maran-opcache.ini";

/// `opcache.memory_consumption`, in MiB.
///
/// Maran-chosen, not a measurement: 128 MiB, which is also PHP's default. It is
/// a constant until an operator has a reason to size it per host, and then it
/// becomes an input.
const MEMORY_CONSUMPTION_MIB: u32 = 128;

/// `opcache.interned_strings_buffer`, in MiB.
///
/// Maran-chosen: 16 MiB, double PHP's default of 8, because one cache serves
/// every account of the version and interned strings are shared between them.
const INTERNED_STRINGS_BUFFER_MIB: u32 = 16;

/// `opcache.max_accelerated_files`.
///
/// Maran-chosen: 10000 scripts, which is also PHP's default, enough for several
/// mid-sized applications sharing one cache.
const MAX_ACCELERATED_FILES: u32 = 10_000;

/// The text of the drop-in this agent writes, the same for every version.
///
/// The ONE renderer: [`write_opcache_drop_in`] writes its result and compares it
/// with the file on disk, so the bytes compared are by construction the bytes
/// that would be written.
///
/// # Errors
///
/// Returns [`PhpOpError::Render`] when the template fails, which can only happen
/// if the template and its render type have drifted apart.
fn render_drop_in() -> Result<String, PhpOpError> {
    OpcacheDropIn {
        memory_consumption_mib: MEMORY_CONSUMPTION_MIB,
        interned_strings_buffer_mib: INTERNED_STRINGS_BUFFER_MIB,
        max_accelerated_files: MAX_ACCELERATED_FILES,
    }
    .render_config()
    .map_err(|error| PhpOpError::Render {
        reason: error.to_string(),
    })
}

/// Brings the OPcache drop-in of one installed PHP version to the current
/// render.
///
/// One php-fpm master serves every account's pool of a version, so its OPcache
/// is shared by every tenant. The drop-in sets the isolation directives that keep
/// one tenant from reading another's cached scripts or steering the cache
/// (`validate_permission`, `validate_root`, `restrict_api`) and the cache's sizes.
///
/// Idempotent by comparison: when the file on disk already equals the render,
/// NOTHING is run, no write, no `php-fpm -t` and no reload, so the install
/// retry path and the start-up pass cost one read per version. A file that
/// differs, or is absent or unreadable, goes through the one config-write path:
/// render, temporary file, `fsync`, atomic rename, `php-fpm<version> -t`,
/// reload, and the previous file restored if either refuses (rules/rust.md
/// "Config writes"). A hand edit of the file is therefore overwritten at the
/// next start, which is intended.
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`] for a version outside the supported
/// set and [`PhpOpError::PhpVersionNotInstalled`] when the version is supported
/// but its php-fpm package is absent. Returns [`PhpOpError::Render`] when the
/// template fails, [`PhpOpError::PoolValidation`] when `php-fpm -t` rejects the
/// result and [`PhpOpError::ReloadFailed`] when the reload refuses it, both with
/// the previous file restored, [`PhpOpError::Bound`] when a program outlived its
/// deadline and was killed (not a verdict on the file), and
/// [`PhpOpError::ConfigWrite`] for any other failure of the protocol.
pub fn write_opcache_drop_in(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
) -> Result<(), PhpOpError> {
    ensure_supported(version)?;

    if !is_installed(host, distro, version) {
        return Err(PhpOpError::PhpVersionNotInstalled {
            version: version.as_str().to_owned(),
        });
    }

    let contents = render_drop_in()?;
    let target = PathBuf::from(distro.php_ini_drop_in_directory(version)).join(DROP_IN_FILE_NAME);

    if host.read_config(&target).as_deref() == Some(contents.as_str()) {
        return Ok(());
    }

    let validator_program = distro.php_fpm_binary(version);
    // A pure check, so the short class: `php-fpm -t` reads the tree and changes nothing.
    let validator = Validator {
        program: &validator_program,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    };
    let service = distro.php_fpm_service(version.as_str());
    // `--` before the unit: `systemctl` reads its options with `getopt_long`.
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, service.as_str()];
    // The absolute path of the service manager, from the adapter: `ops` names no
    // binary path of its own.
    let service_manager = distro.service_manager();
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };

    host.write_config(&target, &contents, &validator, &reload)
}

#[cfg(test)]
#[path = "../tests/php/write_opcache_drop_in_tests.rs"]
mod tests;
