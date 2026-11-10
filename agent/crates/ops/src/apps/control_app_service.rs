//! ControlAppService: starting, stopping, restarting or reloading one app's service.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::DistroAdapter;

use crate::apps::app_catalogue::ensure_in_catalogue;
use crate::apps::app_packages::service_of;
use crate::apps::installed_probe::is_installed;
use crate::apps::model::app_service_action::AppServiceAction;
use crate::apps::model::app_service_status::AppServiceStatus;
use crate::apps::service_status::read_service_status;
use crate::apps::{AppsHost, AppsOpError};
use crate::packages::PackageError;
use crate::safe_write::SafeWriteError;

/// Does `action` to the service of the installed app `id` and returns what the service manager
/// reports afterwards.
///
/// The unit is the adapter's answer for the id and the subcommand is one fixed word per action
/// (`AppServiceAction::subcommand`), so nothing the caller sent reaches the service manager's
/// argv. Stopping the `nginx` app is refused: nginx is also the panel's own front, and stopping
/// it would take the panel off the air with no way back from the panel.
///
/// # Errors
///
/// Returns [`AppsOpError::UnsupportedApp`] for an id outside the catalogue,
/// [`AppsOpError::NotInstalled`] for an app that is not there, [`AppsOpError::ActionRefused`]
/// for an app with no service or for stopping the panel's front, [`AppsOpError::ServiceManager`]
/// when the service manager refused the action, and [`AppsOpError::Package`] with a bound when it
/// outlived its deadline.
pub fn control_app_service<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    action: AppServiceAction,
) -> Result<AppServiceStatus, AppsOpError> {
    ensure_in_catalogue(id)?;
    if !is_installed(host, distro, id)? {
        return Err(AppsOpError::NotInstalled { app: id.as_id() });
    }
    let Some(unit) = service_of(distro, id) else {
        return Err(AppsOpError::ActionRefused {
            app: id.as_id(),
            action: "controlled",
            reason: "the app has no service",
        });
    };
    if matches!(id, ValidatedAppId::Nginx) && action == AppServiceAction::Stop {
        return Err(AppsOpError::ActionRefused {
            app: id.as_id(),
            action: "stopped",
            reason: "nginx is also the panel's own front, and stopping it would take the panel off the air",
        });
    }

    let outcome = host
        .run(
            &distro.service_manager(),
            &[action.subcommand(), END_OF_OPTIONS, &unit],
            CallClass::Reload,
        )
        .map_err(|error| match error {
            SafeWriteError::Bound(bound) => AppsOpError::Package(PackageError::Bound(bound)),
            other => AppsOpError::ServiceManager {
                stderr: other.to_string(),
            },
        })?;
    if outcome.status != 0 {
        return Err(AppsOpError::ServiceManager {
            stderr: outcome.stderr,
        });
    }

    read_service_status(host, distro, &unit)
}

#[cfg(test)]
#[path = "../tests/apps/control_app_service_tests.rs"]
mod tests;
