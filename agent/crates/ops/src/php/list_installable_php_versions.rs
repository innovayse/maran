//! ListInstallablePhpVersions: which versions the operator may install here.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{AdapterBinary, DistroAdapter};

use crate::php::list_php_versions::is_installed;
use crate::php::model::installable_php_version::InstallablePhpVersion;
use crate::php::supported_versions::SUPPORTED_VERSIONS;
use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::SafeWriteError;

/// Lists the PHP versions that can be installed on this host, newest first,
/// each saying whether it already is.
///
/// # Why the host is asked
///
/// A version is installable when it is in the panel's closed supported set AND
/// the host's package manager has a candidate package for it. The second half
/// used to be a list written into the adapter ("Sury and Remi publish 7.4 to
/// 8.4"), which is a claim about repositories, not about this host: an Ubuntu
/// 24.04 whose configured sources carry only `php8.3-fpm` was offered 8.2, and
/// choosing it ran the install unit into `E: Unable to locate package`
/// (issue #466). The answer is now read from the host's own package metadata,
/// through the one query the adapter names
/// ([`maran_distro::PackageFacts::package_candidate_arguments`]): a single
/// read-only, cache-only run for all the supported packages at once.
///
/// What the query cannot do: it reads the metadata already on disk, so a
/// version a source added since the host last refreshed is not offered until it
/// refreshes, and nothing here refreshes it (the agent opens no connection).
/// The family that cannot tell a never-built catalogue from a host that offers
/// nothing asks one more constant question first
/// ([`maran_distro::PackageFacts::catalogue_probe`]), so an unbuilt catalogue is
/// an error and never an empty list.
///
/// Installed versions stay in the list with `installed` set rather than
/// disappearing, and an installed version is listed even if the sources no
/// longer carry it: it IS on the host, and hiding it would show the operator a
/// PHP that is running as absent. Installed-ness is the pool-directory check
/// shared with `list_php_versions`, a `stat` per version. Newest first comes from
/// reversing the fixed oldest-first list, not from sorting version text.
///
/// Both runs are the bounded `CallClass::Query`.
///
/// # Errors
///
/// Returns [`PhpOpError::PackageCatalogueUnavailable`] when a query could not be
/// started, exited with a status that is not an answer, printed a line the
/// adapter's reader does not know, or the catalogue was never built, and
/// [`PhpOpError::Bound`] when a run was killed at its deadline. None of these is
/// an empty list.
pub fn list_installable_php_versions(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<InstallablePhpVersion>, PhpOpError> {
    // The supported set holds only well-formed versions; a failed parse would
    // mean the constant was edited wrongly, and that version is then simply not
    // offered.
    let candidates: Vec<(&str, PhpVersion, String)> = SUPPORTED_VERSIONS
        .iter()
        .rev()
        .filter_map(|text| {
            let version = PhpVersion::parse(text).ok()?;
            let package = distro.php_package(version.as_str());
            Some((*text, version, package))
        })
        .collect();
    let packages: Vec<String> = candidates
        .iter()
        .map(|(_, _, package)| package.clone())
        .collect();

    let offered = offered_packages(host, distro, &packages)?;

    Ok(candidates
        .into_iter()
        .filter_map(|(text, version, package)| {
            let installed = is_installed(host, distro, &version);
            (installed || offered.contains(&package)).then(|| InstallablePhpVersion {
                version: text.to_owned(),
                installed,
            })
        })
        .collect())
}

/// Asks the host which of `packages` its package manager offers.
///
/// # Errors
///
/// As [`list_installable_php_versions`].
fn offered_packages(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    packages: &[String],
) -> Result<Vec<String>, PhpOpError> {
    if let Some(probe) = distro.catalogue_probe() {
        let outcome = run_query(host, &distro.package_query_binary(), probe.arguments)?;
        (probe.read)(outcome.status, &outcome.stdout).map_err(|reason| {
            PhpOpError::PackageCatalogueUnavailable {
                reason: reason.to_string(),
            }
        })?;
    }

    let arguments = distro.package_candidate_arguments(packages);
    let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
    let outcome = run_query(host, &distro.package_candidate_binary(), &arguments)?;

    distro
        .read_package_candidates(outcome.status, &outcome.stdout, &outcome.stderr, packages)
        .map_err(|reason| PhpOpError::PackageCatalogueUnavailable {
            reason: reason.to_string(),
        })
}

/// Runs `program` with `arguments` in the `Query` class.
///
/// # Errors
///
/// Returns [`PhpOpError::Bound`] for a run killed at its deadline, unchanged: a
/// killed run has said nothing about the host. Any other failure to run is
/// [`PhpOpError::PackageCatalogueUnavailable`].
fn run_query(
    host: &dyn PhpHost,
    program: &AdapterBinary,
    arguments: &[&str],
) -> Result<CommandOutcome, PhpOpError> {
    host.run(program, arguments, CallClass::Query)
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PhpOpError::Bound(bound),
            other => PhpOpError::PackageCatalogueUnavailable {
                reason: other.to_string(),
            },
        })
}

#[cfg(test)]
#[path = "../tests/php/list_installable_php_versions_tests.rs"]
mod tests;
