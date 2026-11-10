//! The SUSE-family adapter implementation.

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;
use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::DistroAdapter;
use crate::account_programs::AccountPrograms;
use crate::adapter_binary::AdapterBinary;
use crate::app_facts::AppFacts;
use crate::backup_facts::BackupFacts;
use crate::catalogue_probe::CatalogueProbe;
use crate::database_facts::DatabaseFacts;
use crate::dns_facts::DnsFacts;
use crate::dovecot_major::DovecotMajor;
use crate::family::DistroFamily;
use crate::file_transfer_facts::FileTransferFacts;
use crate::firewall_facts::FirewallFacts;
use crate::key_value_server::KeyValueServer;
use crate::mail_facts::MailFacts;
use crate::nodejs_packaging::NodejsPackaging;
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
use crate::suse::suse_apps;
use crate::suse::{
    suse_account_programs, suse_dns_services, suse_mail_services, suse_package_candidates,
    suse_packages, suse_paths, suse_php_extensions, suse_services,
};
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;
use crate::web_server_facts::WebServerFacts;

/// Implements the agent's operations the SUSE way: zypper, `conf.d`, `nginx` user,
/// SELinux, and the agent's nftables as the only firewall (the installer masks
/// firewalld). Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct SuseAdapter;

impl DistroAdapter for SuseAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Suse
    }
}

impl AccountPrograms for SuseAdapter {
    fn nologin_shell(&self) -> &'static str {
        suse_paths::nologin_shell()
    }

    fn bash_shell(&self) -> &'static str {
        suse_paths::bash_shell()
    }

    fn useradd_binary(&self) -> AdapterBinary {
        suse_account_programs::useradd_binary()
    }

    fn userdel_binary(&self) -> AdapterBinary {
        suse_account_programs::userdel_binary()
    }

    fn usermod_binary(&self) -> AdapterBinary {
        suse_account_programs::usermod_binary()
    }

    fn getent_binary(&self) -> AdapterBinary {
        suse_account_programs::getent_binary()
    }

    fn setquota_binary(&self) -> AdapterBinary {
        suse_account_programs::setquota_binary()
    }

    fn quota_binary(&self) -> AdapterBinary {
        suse_account_programs::quota_binary()
    }

    fn quotaon_binary(&self) -> AdapterBinary {
        suse_account_programs::quotaon_binary()
    }

    fn pkill_binary(&self) -> AdapterBinary {
        suse_account_programs::pkill_binary()
    }

    fn pgrep_binary(&self) -> AdapterBinary {
        suse_account_programs::pgrep_binary()
    }

    fn atq_binary(&self) -> AdapterBinary {
        suse_account_programs::atq_binary()
    }

    fn atrm_binary(&self) -> AdapterBinary {
        suse_account_programs::atrm_binary()
    }

    fn loginctl_binary(&self) -> AdapterBinary {
        suse_account_programs::loginctl_binary()
    }

    fn ipcrm_binary(&self) -> AdapterBinary {
        suse_account_programs::ipcrm_binary()
    }

    fn at_spool_directory(&self) -> &'static str {
        suse_account_programs::at_spool_directory()
    }

    fn mail_spool_directory(&self) -> &'static str {
        suse_account_programs::mail_spool_directory()
    }

    fn passwd_binary(&self) -> AdapterBinary {
        suse_account_programs::passwd_binary()
    }

    fn id_binary(&self) -> AdapterBinary {
        suse_account_programs::id_binary()
    }

    fn chmod_binary(&self) -> AdapterBinary {
        suse_account_programs::chmod_binary()
    }

    fn chgrp_binary(&self) -> AdapterBinary {
        suse_account_programs::chgrp_binary()
    }

    fn chpasswd_binary(&self) -> AdapterBinary {
        suse_account_programs::chpasswd_binary()
    }

    fn passwd_database(&self) -> &'static str {
        suse_account_programs::passwd_database()
    }
}

impl WebServerFacts for SuseAdapter {
    fn nginx_binary(&self) -> AdapterBinary {
        suse_services::nginx_binary()
    }

    fn nginx_service(&self) -> &'static str {
        suse_services::nginx_service()
    }

    fn web_server_user(&self) -> &'static str {
        suse_services::web_server_user()
    }

    fn web_server_group(&self) -> &'static str {
        suse_services::web_server_group()
    }

    fn openssl_binary(&self) -> AdapterBinary {
        suse_services::openssl_binary()
    }

    fn apache_binary(&self) -> AdapterBinary {
        suse_services::apache_binary()
    }

    fn apache_service(&self) -> &'static str {
        suse_services::apache_service()
    }

    fn apache_user(&self) -> &'static str {
        suse_services::apache_user()
    }

    fn apache_group(&self) -> &'static str {
        suse_services::apache_group()
    }

    fn node_executable(&self) -> &'static str {
        suse_services::node_executable()
    }

    fn npm_executable(&self) -> &'static str {
        suse_services::npm_executable()
    }
}

impl AppFacts for SuseAdapter {
    fn nginx_package(&self) -> &'static str {
        suse_apps::nginx_package()
    }

    fn apache_package(&self) -> &'static str {
        suse_apps::apache_package()
    }

    fn apache_extra_packages(&self) -> &'static [&'static str] {
        suse_apps::apache_extra_packages()
    }

    fn apache_include_snippet_path(&self) -> &'static str {
        suse_apps::apache_include_snippet_path()
    }

    fn apache_required_modules(&self) -> &'static [&'static str] {
        suse_apps::apache_required_modules()
    }

    fn apache_module_enabler(&self) -> Option<AdapterBinary> {
        suse_apps::apache_module_enabler()
    }

    fn nodejs_packaging(&self) -> NodejsPackaging {
        NodejsPackaging::PerMajorPackages
    }

    fn nodejs_package(&self, major: &str) -> String {
        suse_apps::nodejs_package(major)
    }

    fn nodejs_candidate_major(&self, _candidate_query_stdout: &str) -> Option<String> {
        None
    }

    fn node_binary(&self) -> AdapterBinary {
        suse_apps::node_binary()
    }
}

impl PhpFacts for SuseAdapter {
    fn php_fpm_pool_directory(&self, version: &str) -> String {
        suse_paths::php_fpm_pool_directory(version)
    }

    fn php_fpm_service(&self, version: &str) -> String {
        suse_services::php_fpm_service(version)
    }

    fn php_fpm_binary(&self, version: &PhpVersion) -> AdapterBinary {
        suse_services::php_fpm_binary(version)
    }

    fn php_package(&self, version: &str) -> String {
        suse_packages::php_package(version)
    }
}

impl PhpExtensionFacts for SuseAdapter {
    fn php_extension_package(
        &self,
        version: &PhpVersion,
        extension: PhpExtensionName,
    ) -> Option<String> {
        suse_php_extensions::php_extension_package(version, extension)
    }

    fn php_extension_switch(
        &self,
        version: &PhpVersion,
        extension: PhpExtensionName,
    ) -> Option<PhpExtensionSwitch> {
        suse_php_extensions::php_extension_switch(version, extension)
    }

    fn php_ini_drop_in_directory(&self, version: &PhpVersion) -> String {
        suse_php_extensions::php_ini_drop_in_directory(version)
    }
}

impl PackageFacts for SuseAdapter {
    fn package_manager(&self) -> &'static str {
        suse_packages::package_manager()
    }

    fn classify_package_failure(&self, status: i32, output: &str) -> PackageFailure {
        suse_packages::classify_package_failure(status, output)
    }

    fn journal_binary(&self) -> AdapterBinary {
        suse_services::journal_binary()
    }

    fn package_query_binary(&self) -> AdapterBinary {
        suse_services::package_query_binary()
    }

    fn package_candidate_binary(&self) -> AdapterBinary {
        suse_package_candidates::package_candidate_binary()
    }

    fn package_candidate_arguments(&self, packages: &[String]) -> Vec<String> {
        suse_package_candidates::package_candidate_arguments(packages)
    }

    fn read_package_candidates(
        &self,
        status: i32,
        stdout: &str,
        stderr: &str,
        packages: &[String],
    ) -> Result<Vec<String>, PackageQueryFailure> {
        suse_package_candidates::read_package_candidates(status, stdout, stderr, packages)
    }

    fn catalogue_probe(&self) -> Option<CatalogueProbe> {
        // UNVERIFIED: read from `man zypper`, confirm with `zypper --no-refresh
        // list-updates` on a host whose repositories were never refreshed. A
        // cache-only run against a repository with no cached metadata skips it and
        // exits with a status other than 0 and 100, which
        // `suse_packages::read_pending_updates` refuses as a failure, so a host that
        // never built its catalogue cannot read as "up to date" and no constant
        // probe is needed.
        None
    }

    fn pending_updates_arguments(&self) -> &'static [&'static str] {
        suse_packages::PENDING_UPDATES_ARGUMENTS
    }

    fn read_pending_updates(
        &self,
        status: i32,
        stdout: &str,
    ) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
        suse_packages::read_pending_updates(status, stdout)
    }

    fn security_pending_updates_arguments(&self) -> Option<Vec<String>> {
        Some(
            suse_packages::SECURITY_PENDING_UPDATES_ARGUMENTS
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
        suse_packages::mark_security_updates(updates, status, stdout)
    }

    fn reboot_probe(&self) -> RebootProbe {
        RebootProbe::Program {
            binary: suse_services::zypper_binary(),
            arguments: suse_packages::REBOOT_PROBE_ARGUMENTS
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        }
    }

    fn read_reboot_probe_run(&self, status: i32) -> Result<RebootRequirement, RebootProbeFailure> {
        suse_packages::read_reboot_probe_run(status)
    }
}

impl DnsFacts for SuseAdapter {
    fn dns_checkconf_binary(&self) -> AdapterBinary {
        suse_dns_services::dns_checkconf_binary()
    }

    fn dns_checkzone_binary(&self) -> AdapterBinary {
        suse_dns_services::dns_checkzone_binary()
    }

    fn dns_control_binary(&self) -> AdapterBinary {
        suse_dns_services::dns_control_binary()
    }

    fn dns_user(&self) -> &'static str {
        suse_dns_services::dns_user()
    }

    fn dns_group(&self) -> &'static str {
        suse_dns_services::dns_group()
    }

    fn dns_state_directory(&self) -> &'static str {
        suse_dns_services::dns_state_directory()
    }

    fn dns_zone_directory(&self) -> &'static str {
        suse_dns_services::dns_zone_directory()
    }

    fn dns_signed_zone_directory(&self) -> &'static str {
        suse_dns_services::dns_signed_zone_directory()
    }

    fn dns_key_directory(&self) -> &'static str {
        suse_dns_services::dns_key_directory()
    }
}

impl FileTransferFacts for SuseAdapter {
    fn sftp_group(&self) -> &'static str {
        suse_services::sftp_group()
    }

    fn vsftpd_binary(&self) -> AdapterBinary {
        suse_services::vsftpd_binary()
    }

    fn ftps_group(&self) -> &'static str {
        suse_services::ftps_group()
    }

    fn vsftpd_tls_version_keys(&self) -> VsftpdTlsVersionKeys {
        suse_services::vsftpd_tls_version_keys()
    }

    fn rsync_binary(&self) -> AdapterBinary {
        suse_services::rsync_binary()
    }

    fn ssh_keygen_binary(&self) -> AdapterBinary {
        suse_services::ssh_keygen_binary()
    }

    fn ssh_binary(&self) -> AdapterBinary {
        suse_services::ssh_binary()
    }

    fn ssh_keyscan_binary(&self) -> AdapterBinary {
        suse_services::ssh_keyscan_binary()
    }

    fn ssh_service(&self) -> &'static str {
        suse_services::ssh_service()
    }

    fn sshd_config_path(&self) -> &'static str {
        suse_services::sshd_config_path()
    }
}

impl BackupFacts for SuseAdapter {
    fn tar_binary(&self) -> AdapterBinary {
        suse_services::tar_binary()
    }

    fn gzip_binary(&self) -> AdapterBinary {
        suse_services::gzip_binary()
    }

    fn database_dump_binary(&self) -> AdapterBinary {
        suse_services::database_dump_binary()
    }
}

impl DatabaseFacts for SuseAdapter {
    fn mysql_client_binary(&self) -> AdapterBinary {
        suse_services::mysql_client_binary()
    }

    fn mysql_service(&self) -> &'static str {
        suse_services::mysql_service()
    }
}

impl ServiceManagerFacts for SuseAdapter {
    fn service_manager(&self) -> AdapterBinary {
        suse_services::service_manager()
    }

    fn systemd_run_binary(&self) -> AdapterBinary {
        suse_services::systemd_run_binary()
    }

    fn timedatectl_binary(&self) -> AdapterBinary {
        suse_services::timedatectl_binary()
    }

    fn systemd_unit_directory(&self) -> &'static str {
        suse_services::systemd_unit_directory()
    }

    fn machine_id_path(&self) -> &'static str {
        suse_services::machine_id_path()
    }

    fn managed_units(&self) -> [&'static str; 4] {
        suse_services::managed_units()
    }
}

impl SchedulingFacts for SuseAdapter {
    fn crontab_binary(&self) -> AdapterBinary {
        suse_services::crontab_binary()
    }

    fn sh_binary(&self) -> &'static str {
        suse_services::sh_binary()
    }

    fn cron_service(&self) -> &'static str {
        suse_services::cron_service()
    }
}

impl FirewallFacts for SuseAdapter {
    fn nft_binary(&self) -> AdapterBinary {
        suse_services::nft_binary()
    }
}

impl MailFacts for SuseAdapter {
    fn postfix_binary(&self) -> AdapterBinary {
        suse_mail_services::postfix_binary()
    }

    fn postconf_binary(&self) -> AdapterBinary {
        suse_mail_services::postconf_binary()
    }

    fn postmap_binary(&self) -> AdapterBinary {
        suse_mail_services::postmap_binary()
    }

    fn postqueue_binary(&self) -> AdapterBinary {
        suse_mail_services::postqueue_binary()
    }

    fn postsuper_binary(&self) -> AdapterBinary {
        suse_mail_services::postsuper_binary()
    }

    fn doveadm_binary(&self) -> AdapterBinary {
        suse_mail_services::doveadm_binary()
    }

    fn doveconf_binary(&self) -> AdapterBinary {
        suse_mail_services::doveconf_binary()
    }

    fn rspamadm_binary(&self) -> AdapterBinary {
        suse_mail_services::rspamadm_binary()
    }

    fn postfix_service(&self) -> &'static str {
        suse_mail_services::postfix_service()
    }

    fn dovecot_service(&self) -> &'static str {
        suse_mail_services::dovecot_service()
    }

    fn rspamd_service(&self) -> &'static str {
        suse_mail_services::rspamd_service()
    }

    fn clamd_service(&self) -> &'static str {
        suse_mail_services::clamd_service()
    }

    fn freshclam_service(&self) -> &'static str {
        suse_mail_services::freshclam_service()
    }

    fn postfix_user(&self) -> &'static str {
        suse_mail_services::postfix_user()
    }

    fn postdrop_group(&self) -> &'static str {
        suse_mail_services::postdrop_group()
    }

    fn dovecot_user(&self) -> &'static str {
        suse_mail_services::dovecot_user()
    }

    fn dovecot_login_user(&self) -> &'static str {
        suse_mail_services::dovecot_login_user()
    }

    fn rspamd_user(&self) -> &'static str {
        suse_mail_services::rspamd_user()
    }

    fn clamd_user(&self) -> &'static str {
        suse_mail_services::clamd_user()
    }

    fn postfix_config_directory(&self) -> &'static str {
        suse_paths::postfix_config_directory()
    }

    fn dovecot_config_directory(&self) -> &'static str {
        suse_paths::dovecot_config_directory()
    }

    fn rspamd_config_directory(&self) -> &'static str {
        suse_paths::rspamd_config_directory()
    }

    fn clamd_config_file(&self) -> &'static str {
        suse_paths::clamd_config_file()
    }

    fn mail_packages(&self) -> &'static [&'static str] {
        suse_mail_services::mail_packages()
    }

    fn antivirus_packages(&self) -> &'static [&'static str] {
        suse_mail_services::antivirus_packages()
    }

    fn postfix_map_type(&self) -> &'static str {
        suse_mail_services::postfix_map_type()
    }

    fn mail_password_scheme(&self) -> MailPasswordScheme {
        suse_mail_services::mail_password_scheme()
    }

    fn dovecot_major(&self, version_id: &str) -> Option<DovecotMajor> {
        suse_mail_services::dovecot_major(version_id)
    }

    fn key_value_server(&self, version_id: &str) -> Option<KeyValueServer> {
        suse_mail_services::key_value_server(version_id)
    }
}

#[cfg(test)]
#[path = "../tests/suse/suse_adapter_tests.rs"]
mod tests;
