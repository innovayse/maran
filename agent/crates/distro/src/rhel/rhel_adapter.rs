//! The RHEL-family adapter implementation.

use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::DistroAdapter;
use crate::account_programs::AccountPrograms;
use crate::adapter_binary::AdapterBinary;
use crate::backup_facts::BackupFacts;
use crate::catalogue_probe::CatalogueProbe;
use crate::database_facts::DatabaseFacts;
use crate::family::DistroFamily;
use crate::file_transfer_facts::FileTransferFacts;
use crate::firewall_facts::FirewallFacts;
use crate::package_facts::PackageFacts;
use crate::package_failure::PackageFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;
use crate::php_facts::PhpFacts;
use crate::reboot_probe::RebootProbe;
use crate::reboot_probe_failure::RebootProbeFailure;
use crate::reboot_requirement::RebootRequirement;
use crate::rhel::{rhel_packages, rhel_paths, rhel_services};
use crate::scheduling_facts::SchedulingFacts;
use crate::service_manager_facts::ServiceManagerFacts;
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;
use crate::web_server_facts::WebServerFacts;

/// Implements the agent's operations the RHEL way: dnf, `conf.d`, `nginx` user,
/// SELinux contexts. Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct RhelAdapter;

impl DistroAdapter for RhelAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Rhel
    }
}

impl AccountPrograms for RhelAdapter {
    fn nologin_shell(&self) -> &'static str {
        rhel_paths::nologin_shell()
    }

    fn useradd_binary(&self) -> AdapterBinary {
        rhel_services::useradd_binary()
    }

    fn userdel_binary(&self) -> AdapterBinary {
        rhel_services::userdel_binary()
    }

    fn usermod_binary(&self) -> AdapterBinary {
        rhel_services::usermod_binary()
    }

    fn getent_binary(&self) -> AdapterBinary {
        rhel_services::getent_binary()
    }

    fn setquota_binary(&self) -> AdapterBinary {
        rhel_services::setquota_binary()
    }

    fn quota_binary(&self) -> AdapterBinary {
        rhel_services::quota_binary()
    }

    fn quotaon_binary(&self) -> AdapterBinary {
        rhel_services::quotaon_binary()
    }

    fn pkill_binary(&self) -> AdapterBinary {
        rhel_services::pkill_binary()
    }

    fn pgrep_binary(&self) -> AdapterBinary {
        rhel_services::pgrep_binary()
    }

    fn atq_binary(&self) -> AdapterBinary {
        rhel_services::atq_binary()
    }

    fn atrm_binary(&self) -> AdapterBinary {
        rhel_services::atrm_binary()
    }

    fn loginctl_binary(&self) -> AdapterBinary {
        rhel_services::loginctl_binary()
    }

    fn ipcrm_binary(&self) -> AdapterBinary {
        rhel_services::ipcrm_binary()
    }

    fn at_spool_directory(&self) -> &'static str {
        rhel_services::at_spool_directory()
    }

    fn mail_spool_directory(&self) -> &'static str {
        rhel_services::mail_spool_directory()
    }

    fn passwd_binary(&self) -> AdapterBinary {
        rhel_services::passwd_binary()
    }

    fn id_binary(&self) -> AdapterBinary {
        rhel_services::id_binary()
    }

    fn chmod_binary(&self) -> AdapterBinary {
        rhel_services::chmod_binary()
    }

    fn chgrp_binary(&self) -> AdapterBinary {
        rhel_services::chgrp_binary()
    }

    fn chpasswd_binary(&self) -> AdapterBinary {
        rhel_services::chpasswd_binary()
    }

    fn passwd_database(&self) -> &'static str {
        rhel_services::passwd_database()
    }
}

impl WebServerFacts for RhelAdapter {
    fn nginx_binary(&self) -> AdapterBinary {
        rhel_services::nginx_binary()
    }

    fn nginx_service(&self) -> &'static str {
        rhel_services::nginx_service()
    }

    fn web_server_user(&self) -> &'static str {
        rhel_services::web_server_user()
    }

    fn web_server_group(&self) -> &'static str {
        rhel_services::web_server_group()
    }

    fn openssl_binary(&self) -> AdapterBinary {
        rhel_services::openssl_binary()
    }
}

impl PhpFacts for RhelAdapter {
    fn php_fpm_pool_directory(&self, version: &str) -> String {
        rhel_paths::php_fpm_pool_directory(version)
    }

    fn php_fpm_service(&self, version: &str) -> String {
        rhel_services::php_fpm_service(version)
    }

    fn php_fpm_binary(&self, version: &PhpVersion) -> AdapterBinary {
        rhel_services::php_fpm_binary(version)
    }

    fn php_package(&self, version: &str) -> String {
        rhel_packages::php_package(version)
    }

    fn installable_php_versions(&self) -> &'static [&'static str] {
        rhel_packages::installable_php_versions()
    }
}

impl PackageFacts for RhelAdapter {
    fn package_manager(&self) -> &'static str {
        rhel_packages::package_manager()
    }

    fn classify_package_failure(&self, status: i32, output: &str) -> PackageFailure {
        rhel_packages::classify_package_failure(status, output)
    }

    fn journal_binary(&self) -> AdapterBinary {
        rhel_services::journal_binary()
    }

    fn package_query_binary(&self) -> AdapterBinary {
        rhel_services::package_query_binary()
    }

    fn catalogue_probe(&self) -> Option<CatalogueProbe> {
        // `dnf check-update -C` fails by itself against a cache that was never
        // built, so this family needs no probe.
        None
    }

    fn pending_updates_arguments(&self) -> &'static [&'static str] {
        rhel_packages::PENDING_UPDATES_ARGUMENTS
    }

    fn read_pending_updates(
        &self,
        status: i32,
        stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
        rhel_packages::read_pending_updates(status, stdout)
    }

    fn security_pending_updates_arguments(&self) -> Option<Vec<String>> {
        Some(
            rhel_packages::SECURITY_PENDING_UPDATES_ARGUMENTS
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        )
    }

    fn mark_security_updates(
        &self,
        updates: Vec<PendingUpdate>,
        status: i32,
        stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
        rhel_packages::mark_security_updates(updates, status, stdout)
    }

    fn reboot_probe(&self) -> RebootProbe {
        RebootProbe::Program {
            binary: rhel_services::needs_restarting_binary(),
            arguments: rhel_packages::REBOOT_PROBE_ARGUMENTS
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        }
    }

    fn read_reboot_probe_run(&self, status: i32) -> Result<RebootRequirement, RebootProbeFailure> {
        rhel_packages::read_reboot_probe_run(status)
    }
}

impl FileTransferFacts for RhelAdapter {
    fn sftp_group(&self) -> &'static str {
        rhel_services::sftp_group()
    }

    fn vsftpd_binary(&self) -> AdapterBinary {
        rhel_services::vsftpd_binary()
    }

    fn ftps_group(&self) -> &'static str {
        rhel_services::ftps_group()
    }

    fn vsftpd_tls_version_keys(&self) -> VsftpdTlsVersionKeys {
        rhel_services::vsftpd_tls_version_keys()
    }

    fn ssh_keygen_binary(&self) -> AdapterBinary {
        rhel_services::ssh_keygen_binary()
    }

    fn ssh_service(&self) -> &'static str {
        rhel_services::ssh_service()
    }

    fn sshd_config_path(&self) -> &'static str {
        rhel_services::sshd_config_path()
    }
}

impl BackupFacts for RhelAdapter {
    fn tar_binary(&self) -> AdapterBinary {
        rhel_services::tar_binary()
    }

    fn gzip_binary(&self) -> AdapterBinary {
        rhel_services::gzip_binary()
    }

    fn database_dump_binary(&self) -> AdapterBinary {
        rhel_services::database_dump_binary()
    }
}

impl DatabaseFacts for RhelAdapter {
    fn mysql_client_binary(&self) -> AdapterBinary {
        rhel_services::mysql_client_binary()
    }

    fn mysql_service(&self) -> &'static str {
        rhel_services::mysql_service()
    }
}

impl ServiceManagerFacts for RhelAdapter {
    fn service_manager(&self) -> AdapterBinary {
        rhel_services::service_manager()
    }

    fn systemd_run_binary(&self) -> AdapterBinary {
        rhel_services::systemd_run_binary()
    }

    fn timedatectl_binary(&self) -> AdapterBinary {
        rhel_services::timedatectl_binary()
    }

    fn systemd_unit_directory(&self) -> &'static str {
        rhel_services::systemd_unit_directory()
    }

    fn machine_id_path(&self) -> &'static str {
        rhel_services::machine_id_path()
    }

    fn managed_units(&self) -> [&'static str; 4] {
        rhel_services::managed_units()
    }
}

impl SchedulingFacts for RhelAdapter {
    fn crontab_binary(&self) -> AdapterBinary {
        rhel_services::crontab_binary()
    }

    fn sh_binary(&self) -> &'static str {
        rhel_services::sh_binary()
    }

    fn cron_service(&self) -> &'static str {
        rhel_services::cron_service()
    }
}

impl FirewallFacts for RhelAdapter {
    fn nft_binary(&self) -> AdapterBinary {
        rhel_services::nft_binary()
    }
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_adapter_tests.rs"]
mod tests;
