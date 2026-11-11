//! What a removal would take with it, asked of the package manager before the removal starts.

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::DistroAdapter;

use crate::apps::{AppsHost, AppsOpError};
use crate::packages::PackageError;
use crate::safe_write::SafeWriteError;

/// Refuses a removal of `packages` (the packages of the app `id`) whose transaction would take
/// anything else with it (issue #527).
///
/// The removal unit runs the package manager with `-y`, which resolves a package that needs the
/// one named by removing it too, silently. So each package gets a dry run first (the family's own
/// `apt-get -s remove`, `dnf remove --assumeno` or `zypper remove --dry-run`, read by the
/// adapter), and the transaction is accepted only when:
///
/// - everything the dry run removes BECAUSE the named package goes (the package itself and what
///   needs it) is one of the app's own packages (`packages`, and the companions the family's
///   package manager installs with Apache, `apache_companion_packages`): ANY other package, an
///   operator's or the installer's, a certbot web server plugin, an add-on, `npm` for Node.js, is
///   never removed as a side effect and needs a decision by a person; and
/// - the panel's own front, the nginx package, is nowhere in the transaction, unused
///   dependencies included.
///
/// The dependencies the package manager reports as unused (`dnf` lists them, `apt` and `zypper`
/// do not remove them) are not held to the app's own set, and that is deliberate: they are the
/// libraries the app's install pulled in and nothing else needs (`libbrotli`, `mod_http2` for
/// `httpd`, in a recorded run), the real removal drops them, and a rule that refused them would
/// refuse every `httpd` removal. What the installer put on the host on purpose is installed
/// explicitly, which every manager records, so it is never reported as unused; the one package
/// whose loss would be fatal anyway, nginx, is checked by name.
///
/// A dry run that fails or prints a plan the adapter does not read (a line it does not know
/// included: the readers fail closed) refuses the removal as well: a removal that cannot be
/// understood is not started.
///
/// # Errors
///
/// Returns [`AppsOpError::RemovalWouldRemoveOthers`] naming the other packages,
/// [`AppsOpError::RemovalNotSimulated`] when the dry run gave no usable answer, and
/// [`AppsOpError::Package`] with a bound when the dry run outlived its deadline.
pub(crate) fn ensure_removal_is_contained<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    packages: &[String],
) -> Result<(), AppsOpError> {
    let program = distro.package_query_binary();
    let front = distro.nginx_package();
    let allowed = allowed_packages(distro, id, packages);
    let mut others: Vec<String> = Vec::new();

    for package in packages {
        let arguments = distro.removal_simulation_arguments(package);
        let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
        let outcome =
            host.run(&program, &arguments, CallClass::Query)
                .map_err(|error| match error {
                    SafeWriteError::Bound(bound) => {
                        AppsOpError::Package(PackageError::Bound(bound))
                    }
                    other => AppsOpError::RemovalNotSimulated {
                        app: id.as_id(),
                        reason: other.to_string(),
                    },
                })?;
        let plan = distro
            .read_removal_simulation(outcome.status, &outcome.stdout)
            .map_err(|reason| AppsOpError::RemovalNotSimulated {
                app: id.as_id(),
                reason: reason.to_string(),
            })?;

        let touches_front = plan
            .removed
            .iter()
            .chain(plan.unused.iter())
            .any(|name| name == front);
        if touches_front && !others.iter().any(|name| name == front) {
            others.push(front.to_owned());
        }
        for name in plan.removed {
            if !allowed.contains(&name) && !others.contains(&name) {
                others.push(name);
            }
        }
    }

    if others.is_empty() {
        return Ok(());
    }
    Err(AppsOpError::RemovalWouldRemoveOthers {
        app: id.as_id(),
        packages: others.join(", "),
    })
}

/// The packages a removal of `id` may take: the app's own `packages` and, for Apache, the
/// companions the family installs with it.
fn allowed_packages(
    distro: &dyn DistroAdapter,
    id: &ValidatedAppId,
    packages: &[String],
) -> Vec<String> {
    let mut allowed = packages.to_vec();
    if matches!(id, ValidatedAppId::Apache) {
        allowed.extend(
            distro
                .apache_companion_packages()
                .iter()
                .map(|package| (*package).to_owned()),
        );
    }
    allowed
}
