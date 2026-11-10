//! The Debian-family adapter implementation.

use std::path::PathBuf;

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;
use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::DistroAdapter;
use crate::account_programs::AccountPrograms;
use crate::adapter_binary::AdapterBinary;
use crate::backup_facts::BackupFacts;
use crate::catalogue_probe::CatalogueProbe;
use crate::database_facts::DatabaseFacts;
use crate::debian::{
    debian_package_candidates, debian_packages, debian_paths, debian_php_extensions,
    debian_services,
};
use crate::dns_facts::DnsFacts;
use crate::dovecot_major::DovecotMajor;
use crate::family::DistroFamily;
use crate::file_transfer_facts::FileTransferFacts;
use crate::firewall_facts::FirewallFacts;
use crate::key_value_server::KeyValueServer;
use crate::mail_facts::MailFacts;
use crate::package_facts::PackageFacts;
use crate::package_failure::PackageFailure;
use crate::package_query_failure::PackageQueryFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;
use crate::php_extension_facts::PhpExtensionFacts;
use crate::php_extension_switch::PhpExtensionSwitch;
use crate::php_facts::PhpFacts;
use crate::reboot_probe::RebootProbe;
use crate::reboot_probe_failure::RebootProbeFailure;
use crate::reboot_requirement::RebootRequirement;
use crate::scheduling_facts::SchedulingFacts;
use crate::service_manager_facts::ServiceManagerFacts;
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;
use crate::web_server_facts::WebServerFacts;

/// Implements the agent's operations the Debian way: apt, `sites-available`,
/// `www-data`. Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct DebianAdapter;

impl DistroAdapter for DebianAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Debian
    }
}

impl AccountPrograms for DebianAdapter {
    fn nologin_shell(&self) -> &'static str {
        debian_paths::nologin_shell()
    }

    fn bash_shell(&self) -> &'static str {
        debian_paths::bash_shell()
    }

    fn useradd_binary(&self) -> AdapterBinary {
        debian_services::useradd_binary()
    }

    fn userdel_binary(&self) -> AdapterBinary {
        debian_services::userdel_binary()
    }

    fn usermod_binary(&self) -> AdapterBinary {
        debian_services::usermod_binary()
    }

    fn getent_binary(&self) -> AdapterBinary {
        debian_services::getent_binary()
    }

    fn setquota_binary(&self) -> AdapterBinary {
        debian_services::setquota_binary()
    }

    fn quota_binary(&self) -> AdapterBinary {
        debian_services::quota_binary()
    }

    fn quotaon_binary(&self) -> AdapterBinary {
        debian_services::quotaon_binary()
    }

    fn pkill_binary(&self) -> AdapterBinary {
        debian_services::pkill_binary()
    }

    fn pgrep_binary(&self) -> AdapterBinary {
        debian_services::pgrep_binary()
    }

    fn atq_binary(&self) -> AdapterBinary {
        debian_services::atq_binary()
    }

    fn atrm_binary(&self) -> AdapterBinary {
        debian_services::atrm_binary()
    }

    fn loginctl_binary(&self) -> AdapterBinary {
        debian_services::loginctl_binary()
    }

    fn ipcrm_binary(&self) -> AdapterBinary {
        debian_services::ipcrm_binary()
    }

    fn at_spool_directory(&self) -> &'static str {
        debian_services::at_spool_directory()
    }

    fn mail_spool_directory(&self) -> &'static str {
        debian_services::mail_spool_directory()
    }

    fn passwd_binary(&self) -> AdapterBinary {
        debian_services::passwd_binary()
    }

    fn id_binary(&self) -> AdapterBinary {
        debian_services::id_binary()
    }

    fn chmod_binary(&self) -> AdapterBinary {
        debian_services::chmod_binary()
    }

    fn chgrp_binary(&self) -> AdapterBinary {
        debian_services::chgrp_binary()
    }

    fn chpasswd_binary(&self) -> AdapterBinary {
        debian_services::chpasswd_binary()
    }

    fn passwd_database(&self) -> &'static str {
        debian_services::passwd_database()
    }
}

impl WebServerFacts for DebianAdapter {
    fn nginx_binary(&self) -> AdapterBinary {
        debian_services::nginx_binary()
    }

    fn nginx_service(&self) -> &'static str {
        debian_services::nginx_service()
    }

    fn web_server_user(&self) -> &'static str {
        debian_services::web_server_user()
    }

    fn web_server_group(&self) -> &'static str {
        debian_services::web_server_group()
    }

    fn openssl_binary(&self) -> AdapterBinary {
        debian_services::openssl_binary()
    }
}

impl PhpFacts for DebianAdapter {
    fn php_fpm_pool_directory(&self, version: &str) -> String {
        debian_paths::php_fpm_pool_directory(version)
    }

    fn php_fpm_service(&self, version: &str) -> String {
        debian_services::php_fpm_service(version)
    }

    fn php_fpm_binary(&self, version: &PhpVersion) -> AdapterBinary {
        debian_services::php_fpm_binary(version)
    }

    fn php_package(&self, version: &str) -> String {
        debian_packages::php_package(version)
    }
}

impl PhpExtensionFacts for DebianAdapter {
    fn php_extension_package(
        &self,
        version: &PhpVersion,
        extension: PhpExtensionName,
    ) -> Option<String> {
        debian_php_extensions::php_extension_package(version, extension)
    }

    fn php_extension_switch(
        &self,
        version: &PhpVersion,
        extension: PhpExtensionName,
    ) -> Option<PhpExtensionSwitch> {
        debian_php_extensions::php_extension_switch(version, extension)
    }

    fn php_ini_drop_in_directory(&self, version: &PhpVersion) -> String {
        debian_php_extensions::php_ini_drop_in_directory(version)
    }
}

impl PackageFacts for DebianAdapter {
    fn package_manager(&self) -> &'static str {
        debian_packages::package_manager()
    }

    fn classify_package_failure(&self, status: i32, output: &str) -> PackageFailure {
        debian_packages::classify_package_failure(status, output)
    }

    fn journal_binary(&self) -> AdapterBinary {
        debian_services::journal_binary()
    }

    fn package_query_binary(&self) -> AdapterBinary {
        debian_services::package_query_binary()
    }

    fn package_candidate_binary(&self) -> AdapterBinary {
        debian_package_candidates::package_candidate_binary()
    }

    fn package_candidate_arguments(&self, packages: &[String]) -> Vec<String> {
        debian_package_candidates::package_candidate_arguments(packages)
    }

    fn read_package_candidates(
        &self,
        status: i32,
        stdout: &str,
        _stderr: &str,
        packages: &[String],
    ) -> Result<Vec<String>, PackageQueryFailure> {
        debian_package_candidates::read_package_candidates(status, stdout, packages)
    }

    fn catalogue_probe(&self) -> Option<CatalogueProbe> {
        Some(CatalogueProbe {
            arguments: debian_packages::CATALOGUE_PROBE_ARGUMENTS,
            read: debian_packages::read_catalogue_probe,
        })
    }

    fn pending_updates_arguments(&self) -> &'static [&'static str] {
        debian_packages::PENDING_UPDATES_ARGUMENTS
    }

    fn read_pending_updates(
        &self,
        status: i32,
        stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
        debian_packages::read_pending_updates(status, stdout)
    }

    fn security_pending_updates_arguments(&self) -> Option<Vec<String>> {
        // The simulated upgrade prints each package's origins, so the first
        // reading already classifies every row.
        None
    }

    fn mark_security_updates(
        &self,
        updates: Vec<PendingUpdate>,
        _status: i32,
        _stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
        Ok(updates)
    }

    fn reboot_probe(&self) -> RebootProbe {
        RebootProbe::Marker(PathBuf::from(debian_paths::reboot_required_marker()))
    }

    fn read_reboot_probe_run(&self, status: i32) -> Result<RebootRequirement, RebootProbeFailure> {
        debian_packages::read_reboot_probe_run(status)
    }
}

impl DnsFacts for DebianAdapter {
    fn dns_checkconf_binary(&self) -> AdapterBinary {
        debian_services::dns_checkconf_binary()
    }

    fn dns_checkzone_binary(&self) -> AdapterBinary {
        debian_services::dns_checkzone_binary()
    }

    fn dns_control_binary(&self) -> AdapterBinary {
        debian_services::dns_control_binary()
    }

    fn dns_user(&self) -> &'static str {
        debian_services::dns_user()
    }

    fn dns_group(&self) -> &'static str {
        debian_services::dns_group()
    }

    fn dns_state_directory(&self) -> &'static str {
        debian_services::dns_state_directory()
    }

    fn dns_zone_directory(&self) -> &'static str {
        debian_services::dns_zone_directory()
    }

    fn dns_signed_zone_directory(&self) -> &'static str {
        debian_services::dns_signed_zone_directory()
    }

    fn dns_key_directory(&self) -> &'static str {
        debian_services::dns_key_directory()
    }
}

impl FileTransferFacts for DebianAdapter {
    fn sftp_group(&self) -> &'static str {
        debian_services::sftp_group()
    }

    fn vsftpd_binary(&self) -> AdapterBinary {
        debian_services::vsftpd_binary()
    }

    fn ftps_group(&self) -> &'static str {
        debian_services::ftps_group()
    }

    fn vsftpd_tls_version_keys(&self) -> VsftpdTlsVersionKeys {
        debian_services::vsftpd_tls_version_keys()
    }

    fn rsync_binary(&self) -> AdapterBinary {
        debian_services::rsync_binary()
    }

    fn ssh_keygen_binary(&self) -> AdapterBinary {
        debian_services::ssh_keygen_binary()
    }

    fn ssh_binary(&self) -> AdapterBinary {
        debian_services::ssh_binary()
    }

    fn ssh_keyscan_binary(&self) -> AdapterBinary {
        debian_services::ssh_keyscan_binary()
    }

    fn ssh_service(&self) -> &'static str {
        debian_services::ssh_service()
    }

    fn sshd_config_path(&self) -> &'static str {
        debian_services::sshd_config_path()
    }
}

impl BackupFacts for DebianAdapter {
    fn tar_binary(&self) -> AdapterBinary {
        debian_services::tar_binary()
    }

    fn gzip_binary(&self) -> AdapterBinary {
        debian_services::gzip_binary()
    }

    fn database_dump_binary(&self) -> AdapterBinary {
        debian_services::database_dump_binary()
    }
}

impl DatabaseFacts for DebianAdapter {
    fn mysql_client_binary(&self) -> AdapterBinary {
        debian_services::mysql_client_binary()
    }

    fn mysql_service(&self) -> &'static str {
        debian_services::mysql_service()
    }
}

impl ServiceManagerFacts for DebianAdapter {
    fn service_manager(&self) -> AdapterBinary {
        debian_services::service_manager()
    }

    fn systemd_run_binary(&self) -> AdapterBinary {
        debian_services::systemd_run_binary()
    }

    fn timedatectl_binary(&self) -> AdapterBinary {
        debian_services::timedatectl_binary()
    }

    fn systemd_unit_directory(&self) -> &'static str {
        debian_services::systemd_unit_directory()
    }

    fn machine_id_path(&self) -> &'static str {
        debian_services::machine_id_path()
    }

    fn managed_units(&self) -> [&'static str; 4] {
        debian_services::managed_units()
    }
}

impl SchedulingFacts for DebianAdapter {
    fn crontab_binary(&self) -> AdapterBinary {
        debian_services::crontab_binary()
    }

    fn sh_binary(&self) -> &'static str {
        debian_services::sh_binary()
    }

    fn cron_service(&self) -> &'static str {
        debian_services::cron_service()
    }
}

impl FirewallFacts for DebianAdapter {
    fn nft_binary(&self) -> AdapterBinary {
        debian_services::nft_binary()
    }
}

impl MailFacts for DebianAdapter {
    fn postfix_binary(&self) -> AdapterBinary {
        debian_services::postfix_binary()
    }

    fn postconf_binary(&self) -> AdapterBinary {
        debian_services::postconf_binary()
    }

    fn postmap_binary(&self) -> AdapterBinary {
        debian_services::postmap_binary()
    }

    fn postqueue_binary(&self) -> AdapterBinary {
        debian_services::postqueue_binary()
    }

    fn postsuper_binary(&self) -> AdapterBinary {
        debian_services::postsuper_binary()
    }

    fn doveadm_binary(&self) -> AdapterBinary {
        debian_services::doveadm_binary()
    }

    fn doveconf_binary(&self) -> AdapterBinary {
        debian_services::doveconf_binary()
    }

    fn rspamadm_binary(&self) -> AdapterBinary {
        debian_services::rspamadm_binary()
    }

    fn postfix_service(&self) -> &'static str {
        debian_services::postfix_service()
    }

    fn dovecot_service(&self) -> &'static str {
        debian_services::dovecot_service()
    }

    fn rspamd_service(&self) -> &'static str {
        debian_services::rspamd_service()
    }

    fn clamd_service(&self) -> &'static str {
        debian_services::clamd_service()
    }

    fn freshclam_service(&self) -> &'static str {
        debian_services::freshclam_service()
    }

    fn postfix_user(&self) -> &'static str {
        debian_services::postfix_user()
    }

    fn postdrop_group(&self) -> &'static str {
        debian_services::postdrop_group()
    }

    fn dovecot_user(&self) -> &'static str {
        debian_services::dovecot_user()
    }

    fn dovecot_login_user(&self) -> &'static str {
        debian_services::dovecot_login_user()
    }

    fn rspamd_user(&self) -> &'static str {
        debian_services::rspamd_user()
    }

    fn clamd_user(&self) -> &'static str {
        debian_services::clamd_user()
    }

    fn postfix_config_directory(&self) -> &'static str {
        debian_paths::postfix_config_directory()
    }

    fn dovecot_config_directory(&self) -> &'static str {
        debian_paths::dovecot_config_directory()
    }

    fn rspamd_config_directory(&self) -> &'static str {
        debian_paths::rspamd_config_directory()
    }

    fn clamd_config_file(&self) -> &'static str {
        debian_paths::clamd_config_file()
    }

    fn mail_packages(&self) -> &'static [&'static str] {
        debian_packages::mail_packages()
    }

    fn antivirus_packages(&self) -> &'static [&'static str] {
        debian_packages::antivirus_packages()
    }

    fn postfix_map_type(&self) -> &'static str {
        debian_packages::postfix_map_type()
    }

    fn mail_password_scheme(&self) -> MailPasswordScheme {
        debian_packages::mail_password_scheme()
    }

    fn dovecot_major(&self, version_id: &str) -> Option<DovecotMajor> {
        debian_packages::dovecot_major(version_id)
    }

    fn key_value_server(&self, version_id: &str) -> Option<KeyValueServer> {
        debian_packages::key_value_server(version_id)
    }
}

#[cfg(test)]
#[path = "../tests/debian/debian_adapter_tests.rs"]
mod tests;
