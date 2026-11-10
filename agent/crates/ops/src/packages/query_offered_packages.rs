//! Asking the host which packages its package sources offer.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroAdapter};

use crate::packages::{OfferedPackages, PackageError};
use crate::safe_write::{ConfigHost, SafeWriteError};

/// Asks the host which of `packages` its package manager offers, from the metadata already on
/// disk: a single read-only, cache-only run (issue #466). The agent opens no connection, so a
/// package a source added since the host last refreshed is not offered until it refreshes.
///
/// The family that cannot tell a never-built catalogue from a host that offers nothing asks one
/// more constant question first, so an unbuilt catalogue is an error and never an empty list.
///
/// # Errors
///
/// Returns [`PackageError::CatalogueUnavailable`] when a query could not be started, exited
/// with a status that is not an answer, printed a line the adapter does not know, or the
/// catalogue was never built, and [`PackageError::Bound`] when a run was killed at its
/// deadline. None of these is an empty list.
pub fn query_offered_packages<H: ConfigHost + ?Sized>(
    host: &H,
    distro: &dyn DistroAdapter,
    packages: &[String],
) -> Result<OfferedPackages, PackageError> {
    if let Some(probe) = distro.catalogue_probe() {
        let outcome = run_query(host, &distro.package_query_binary(), probe.arguments)?;
        (probe.read)(outcome.status, &outcome.stdout).map_err(|reason| {
            PackageError::CatalogueUnavailable {
                reason: reason.to_string(),
            }
        })?;
    }

    let arguments = distro.package_candidate_arguments(packages);
    let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
    let outcome = run_query(host, &distro.package_candidate_binary(), &arguments)?;

    let names = distro
        .read_package_candidates(outcome.status, &outcome.stdout, &outcome.stderr, packages)
        .map_err(|reason| PackageError::CatalogueUnavailable {
            reason: reason.to_string(),
        })?;

    Ok(OfferedPackages {
        names,
        raw: outcome.stdout,
    })
}

/// Runs `program` with `arguments` in the `Query` class.
fn run_query<H: ConfigHost + ?Sized>(
    host: &H,
    program: &AdapterBinary,
    arguments: &[&str],
) -> Result<CommandOutcome, PackageError> {
    host.run(program, arguments, CallClass::Query)
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => PackageError::Bound(bound),
            other => PackageError::CatalogueUnavailable {
                reason: other.to_string(),
            },
        })
}
