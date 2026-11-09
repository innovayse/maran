//! SetPhpExtension: switching one catalogue extension on or off for one PHP
//! version's php-fpm.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, PhpExtensionSwitch};

use crate::php::list_php_extensions::{extension_reading, loaded_modules};
use crate::php::run_install_unit::run_install_unit;
use crate::php::write_pool::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::SafeWriteError;

/// Which way [`run_switch`] moves an extension.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Direction {
    /// Make the extension load.
    On,
    /// Make the extension stop loading.
    Off,
}

impl Direction {
    /// The other way, which is how a refused change is undone.
    fn inverse(self) -> Self {
        match self {
            Self::On => Self::Off,
            Self::Off => Self::On,
        }
    }
}

/// Runs the family's switch for `direction`, with the argv the adapter built
/// from the validated version and the catalogue name.
///
/// # Errors
///
/// Returns [`PhpOpError::ExtensionSwitchFailed`] when the switch cannot be
/// started or exits non-zero, and [`PhpOpError::Bound`] when it outlived its
/// deadline and was killed.
fn run_switch(
    host: &dyn PhpHost,
    switch: &PhpExtensionSwitch,
    direction: Direction,
) -> Result<(), PhpOpError> {
    let (program, arguments) = match direction {
        Direction::On => (&switch.enable_program, &switch.enable_arguments),
        Direction::Off => (&switch.disable_program, &switch.disable_arguments),
    };
    let argv: Vec<&str> = arguments.iter().map(String::as_str).collect();

    // `Mutation`: a short state-changing program that edits symlinks or ini
    // links and nothing else.
    let outcome = host
        .run(program, &argv, CallClass::Mutation)
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PhpOpError::Bound(bound),
            other => PhpOpError::ExtensionSwitchFailed {
                stderr: other.to_string(),
            },
        })?;
    if outcome.status != 0 {
        return Err(PhpOpError::ExtensionSwitchFailed {
            stderr: outcome.stderr,
        });
    }

    Ok(())
}

/// Checks the php-fpm configuration and reloads the service, writing nothing.
///
/// The same validator and reload a pool write hands the protocol: the version's
/// own `php-fpm -t`, then the service manager's `reload` of the version's
/// service, both programs and the service name from the adapter.
///
/// # Errors
///
/// Returns [`PhpOpError::PoolValidation`] when `php-fpm -t` refuses the tree,
/// [`PhpOpError::ReloadFailed`] when the reload is refused,
/// [`PhpOpError::Bound`] when either outlived its deadline, and
/// [`PhpOpError::ConfigWrite`] when a command cannot be started.
fn validate_and_reload(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
) -> Result<(), PhpOpError> {
    let validator_program = distro.php_fpm_binary(version);
    let validator = crate::safe_write::model::Validator {
        program: &validator_program,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    };
    let service = distro.php_fpm_service(version.as_str());
    // `--` before the unit: `systemctl` ends option parsing at `--`.
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, service.as_str()];
    let service_manager = distro.service_manager();
    let reload = crate::safe_write::model::Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };

    host.validate_and_reload(&validator, &reload)
}

/// Switches `extension` on or off for `version`'s php-fpm, reporting progress.
///
/// `extension` is a [`PhpExtensionName`], so the caller can only name one of the
/// catalogue's thirteen: nothing a caller wrote becomes a package, a file name
/// or an `extension=` line. The package comes from
/// `DistroAdapter::php_extension_package` and the switch argv from
/// `DistroAdapter::php_extension_switch`, both built from the validated
/// version and name.
///
/// Idempotent: the extension already in the requested state is a success with
/// NOTHING run beyond the one module listing that told so.
///
/// Turning ON installs the derived package through the same root install unit a
/// PHP version uses (`run_install_unit`), then runs the family's switch when it
/// has one, then checks the tree with `php-fpm -t` and reloads, and finally
/// reads the loaded modules again: an extension that is still absent is a
/// failure, never a success. Turning OFF needs the switch; a family without one
/// refuses rather than editing the distribution's ini files by hand. When the
/// check or the reload refuses, the inverse switch is run so the files are back
/// as they were, and the refusal is returned. The inverse is best effort: if it
/// fails too, that is logged and the original refusal is still the answer. A
/// family with no switch has nothing to undo (the installed package stays).
///
/// A change affects every account's pool on that PHP version at the reload; the
/// panel offers it to administrators only.
///
/// `progress` is called with a percentage and a stage name — `"preparing"`,
/// `"install"`, `"switch"`, `"reload"`, `"verify"` — markers around blocking
/// calls, not measurements.
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`], [`PhpOpError::PhpVersionNotInstalled`]
/// and [`PhpOpError::ExtensionProbeFailed`] as `list_php_extensions`
/// does. Returns [`PhpOpError::ExtensionNotPackaged`] when turning on an
/// extension the family has no package for, and
/// [`PhpOpError::ExtensionSwitchUnsupported`] when turning off one it has no
/// switch for. Returns the install unit's errors
/// ([`PhpOpError::PackageSourceRefused`], [`PhpOpError::PackageSourceUnreachable`],
/// [`PhpOpError::PackageManager`]), [`PhpOpError::ExtensionSwitchFailed`] when
/// the switch fails, [`PhpOpError::PoolValidation`] or
/// [`PhpOpError::ReloadFailed`] when php-fpm refuses the result (the switch
/// undone), [`PhpOpError::ExtensionNotLoaded`] when a turned-on extension is
/// still absent afterwards and [`PhpOpError::ExtensionStillLoaded`] when a
/// turned-off one is still present. Returns [`PhpOpError::Bound`] when a program
/// outlived its deadline and was killed: never a verdict, and for the install
/// unit the package may still arrive.
pub fn set_php_extension<P>(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    version: &PhpVersion,
    extension: PhpExtensionName,
    enabled: bool,
    mut progress: P,
) -> Result<(), PhpOpError>
where
    P: FnMut(u32, &str),
{
    progress(10, "preparing");

    // Checks the version and reads what the daemon loads now.
    let modules = loaded_modules(host, distro, version)?;
    let current = extension_reading(distro, version, &modules, extension);
    if current.enabled == enabled {
        progress(100, "verify");
        return Ok(());
    }

    let direction = if enabled {
        Direction::On
    } else {
        Direction::Off
    };
    let switch = distro.php_extension_switch(version, extension);

    match direction {
        Direction::On => {
            // The package is the adapter's name; the instance reaches the unit
            // as one argv element (see `run_install_unit`).
            let package = distro
                .php_extension_package(version, extension)
                .ok_or_else(|| PhpOpError::ExtensionNotPackaged {
                    extension: extension.as_str().to_owned(),
                    version: version.as_str().to_owned(),
                })?;
            progress(30, "install");
            run_install_unit(host, distro, &package)?;
        }
        Direction::Off => {
            if switch.is_none() {
                return Err(PhpOpError::ExtensionSwitchUnsupported {
                    extension: extension.as_str().to_owned(),
                });
            }
        }
    }

    if let Some(switch) = &switch {
        progress(60, "switch");
        run_switch(host, switch, direction)?;
    }

    progress(80, "reload");
    if let Err(refusal) = validate_and_reload(host, distro, version) {
        let undone = switch.as_ref().map_or(Ok(()), |switch| {
            run_switch(host, switch, direction.inverse())
        });
        if let Err(undo) = undone {
            tracing::warn!(
                extension = extension.as_str(),
                version = version.as_str(),
                error = %undo,
                "could not undo a PHP extension switch that php-fpm refused"
            );
        }
        return Err(refusal);
    }

    progress(90, "verify");
    let after = loaded_modules(host, distro, version)?;
    let now = extension_reading(distro, version, &after, extension).enabled;
    match (enabled, now) {
        (true, false) => Err(PhpOpError::ExtensionNotLoaded {
            extension: extension.as_str().to_owned(),
        }),
        (false, true) => Err(PhpOpError::ExtensionStillLoaded {
            extension: extension.as_str().to_owned(),
        }),
        _ => {
            progress(100, "verify");
            Ok(())
        }
    }
}

#[cfg(test)]
#[path = "../tests/php/set_php_extension_tests.rs"]
mod tests;
