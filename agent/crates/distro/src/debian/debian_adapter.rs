//! The Debian-family adapter implementation.

use crate::DistroAdapter;
use crate::debian::{debian_packages, debian_paths, debian_services};
use crate::family::DistroFamily;

/// Implements the agent's operations the Debian way: apt, `sites-available`,
/// `www-data`. Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct DebianAdapter;

impl DistroAdapter for DebianAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Debian
    }

    fn nologin_shell(&self) -> &'static str {
        debian_paths::nologin_shell()
    }

    fn nginx_binary(&self) -> &'static str {
        debian_services::nginx_binary()
    }

    fn nginx_service(&self) -> &'static str {
        debian_services::nginx_service()
    }

    fn service_manager(&self) -> &'static str {
        debian_services::service_manager()
    }

    fn web_server_user(&self) -> &'static str {
        debian_services::web_server_user()
    }

    fn web_server_group(&self) -> &'static str {
        debian_services::web_server_group()
    }

    fn php_fpm_pool_directory(&self, version: &str) -> String {
        debian_paths::php_fpm_pool_directory(version)
    }

    fn php_fpm_service(&self, version: &str) -> String {
        debian_services::php_fpm_service(version)
    }

    fn php_fpm_binary(&self, version: &str) -> String {
        debian_services::php_fpm_binary(version)
    }

    fn php_package(&self, version: &str) -> String {
        debian_packages::php_package(version)
    }

    fn openssl_binary(&self) -> &'static str {
        debian_services::openssl_binary()
    }

    fn package_manager(&self) -> &'static str {
        debian_packages::package_manager()
    }
}
