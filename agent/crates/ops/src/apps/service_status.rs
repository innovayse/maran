//! What the service manager says about one unit.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::apps::model::app_service_status::AppServiceStatus;
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::PackageError;
use crate::safe_write::SafeWriteError;

/// The service-manager subcommand that prints a unit's state on one line.
const IS_ACTIVE_SUBCOMMAND: &str = "is-active";

/// Asks the service manager whether `unit` runs.
///
/// `systemctl is-active` prints one word and exits non-zero for every state but `active`, so the
/// word is the answer and the exit status is not read. `failed` is its own state; every other
/// word that is not `active` (`inactive`, `activating`, `deactivating`, an unknown unit) is
/// reported as not running.
///
/// # Errors
///
/// Returns [`AppsOpError::Package`] with a bound when the service manager outlived its deadline
/// and [`AppsOpError::ServiceManager`] when it could not be started at all.
pub(crate) fn read_service_status<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    unit: &str,
) -> Result<AppServiceStatus, AppsOpError> {
    let outcome = host
        .run(
            &distro.service_manager(),
            &[IS_ACTIVE_SUBCOMMAND, END_OF_OPTIONS, unit],
            CallClass::Query,
        )
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => AppsOpError::Package(PackageError::Bound(bound)),
            other => AppsOpError::ServiceManager {
                stderr: other.to_string(),
            },
        })?;

    Ok(match outcome.stdout.trim() {
        "active" => AppServiceStatus::Active,
        "failed" => AppServiceStatus::Failed,
        _ => AppServiceStatus::Inactive,
    })
}
