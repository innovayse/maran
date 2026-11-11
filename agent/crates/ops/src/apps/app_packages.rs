//! The packages behind an app, as the host's family names them.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_distro::DistroAdapter;

/// The packages an install of `id` hands the root install unit, in install order.
///
/// Every name comes from the distro adapter for an id that has been through
/// `ensure_in_catalogue`; nothing the caller sent reaches a package name except through that id.
pub(crate) fn packages_of(distro: &dyn DistroAdapter, id: &ValidatedAppId) -> Vec<String> {
    match id {
        ValidatedAppId::Nginx => vec![distro.nginx_package().to_owned()],
        ValidatedAppId::Apache => {
            let mut packages = vec![distro.apache_package().to_owned()];
            packages.extend(
                distro
                    .apache_extra_packages()
                    .iter()
                    .map(|package| (*package).to_owned()),
            );
            packages
        }
        ValidatedAppId::Php(version) => vec![distro.php_package(version.as_str())],
        ValidatedAppId::Nodejs(major) => {
            let mut packages = vec![distro.nodejs_package(major.as_str())];
            packages.extend(distro.nodejs_extra_packages(major.as_str()));
            packages
        }
    }
}

/// The service unit of `id`, or `None` for an app that has none (Node.js is an interpreter).
pub(crate) fn service_of(distro: &dyn DistroAdapter, id: &ValidatedAppId) -> Option<String> {
    match id {
        ValidatedAppId::Nginx => Some(distro.nginx_service().to_owned()),
        ValidatedAppId::Apache => Some(distro.apache_service().to_owned()),
        ValidatedAppId::Php(version) => Some(distro.php_fpm_service(version.as_str())),
        ValidatedAppId::Nodejs(_) => None,
    }
}
