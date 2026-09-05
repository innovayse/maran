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

    fn mysql_client_binary(&self) -> &'static str {
        rhel_services::mysql_client_binary()
    }

    fn mysql_service(&self) -> &'static str {
        rhel_services::mysql_service()
    }

    fn sftp_group(&self) -> &'static str {
        rhel_services::sftp_group()
    }

    fn useradd_binary(&self) -> &'static str {
        rhel_services::useradd_binary()
    }

    fn userdel_binary(&self) -> &'static str {
        rhel_services::userdel_binary()
    }

    fn usermod_binary(&self) -> &'static str {
        rhel_services::usermod_binary()
    }

    fn setquota_binary(&self) -> &'static str {
        rhel_services::setquota_binary()
    }

    fn quota_binary(&self) -> &'static str {
        rhel_services::quota_binary()
    }

    fn id_binary(&self) -> &'static str {
        rhel_services::id_binary()
    }

    fn chmod_binary(&self) -> &'static str {
        rhel_services::chmod_binary()
    }

    fn chgrp_binary(&self) -> &'static str {
        rhel_services::chgrp_binary()
    }

    fn chpasswd_binary(&self) -> &'static str {
        rhel_services::chpasswd_binary()
    }

    fn systemd_unit_directory(&self) -> &'static str {
        rhel_services::systemd_unit_directory()
    }

    fn passwd_database(&self) -> &'static str {
        rhel_services::passwd_database()
    }

    fn crontab_binary(&self) -> &'static str {
        rhel_services::crontab_binary()
    }

    fn sh_binary(&self) -> &'static str {
        rhel_services::sh_binary()
    }

    fn nft_binary(&self) -> &'static str {
        rhel_services::nft_binary()
    }

    fn nftables_include_target(&self) -> &'static str {
        rhel_paths::nftables_include_target()
    }

    fn firewall_service(&self) -> &'static str {
        rhel_services::firewall_service()
    }

    fn cron_service(&self) -> &'static str {
        rhel_services::cron_service()
    }

    fn ssh_service(&self) -> &'static str {
        rhel_services::ssh_service()
    }

    fn managed_units(&self) -> [&'static str; 4] {
        rhel_services::managed_units()
    }
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_adapter_tests.rs"]
mod tests;
