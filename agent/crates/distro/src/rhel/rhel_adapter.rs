//! The RHEL-family adapter implementation.

use crate::DistroAdapter;
use crate::family::DistroFamily;
use crate::rhel::{rhel_packages, rhel_paths, rhel_services};

/// Implements the agent's operations the RHEL way: dnf, `conf.d`, `nginx` user,
/// SELinux contexts. Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct RhelAdapter;

impl DistroAdapter for RhelAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Rhel
    }

    fn nologin_shell(&self) -> &'static str {
        rhel_paths::nologin_shell()
    }

    fn nginx_binary(&self) -> &'static str {
        rhel_services::nginx_binary()
    }

    fn nginx_service(&self) -> &'static str {
        rhel_services::nginx_service()
    }

    fn service_manager(&self) -> &'static str {
        rhel_services::service_manager()
    }

    fn web_server_user(&self) -> &'static str {
        rhel_services::web_server_user()
    }

    fn web_server_group(&self) -> &'static str {
        rhel_services::web_server_group()
    }

    fn php_fpm_pool_directory(&self, version: &str) -> String {
        rhel_paths::php_fpm_pool_directory(version)
    }

    fn php_fpm_service(&self, version: &str) -> String {
        rhel_services::php_fpm_service(version)
    }

    fn php_fpm_binary(&self, version: &str) -> String {
        rhel_services::php_fpm_binary(version)
    }

    fn php_package(&self, version: &str) -> String {
        rhel_packages::php_package(version)
    }

    fn openssl_binary(&self) -> &'static str {
        rhel_services::openssl_binary()
    }

    fn package_manager(&self) -> &'static str {
        rhel_packages::package_manager()
    }
}
