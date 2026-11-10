#![warn(missing_docs)]
// The compiler, not a grep, is the gate: `unsafe` exists in this workspace only
// in maran-agent-core::privs (rules/rust.md "unsafe"). `forbid` cannot be lowered
// by an `#[allow]` further down, so adding unsafe here does not compile at all.
#![forbid(unsafe_code)]
//! maran-distro — the only crate that may know a distribution's name.
//!
//! `detection/` answers what the host is; `adapter` turns that answer into
//! behaviour behind the [`DistroAdapter`] trait, with one implementation folder
//! per family, so every other crate stays free of `if debian` branches
//! (rules/architecture.md).

pub mod account_programs;
pub mod adapter;
pub mod adapter_binary;
pub mod adapter_for;
pub mod app_facts;
pub mod backup_facts;
pub mod catalogue_probe;
pub mod database_facts;
pub mod debian;
pub mod detection;
pub mod dns_facts;
pub mod dovecot_major;
pub mod family;
pub mod file_transfer_facts;
pub mod firewall_facts;
mod is_printable_token;
pub mod key_value_server;
pub mod mail_facts;
pub mod nodejs_packaging;
pub mod package_facts;
pub mod package_failure;
pub mod package_query_failure;
pub mod pending_update;
pub mod pending_updates_failure;
pub mod php_extension_facts;
pub mod php_extension_switch;
pub mod php_facts;
pub mod reboot_probe;
pub mod reboot_probe_failure;
pub mod reboot_requirement;
pub mod rhel;
pub mod scheduling_facts;
pub mod service_manager_facts;
pub mod site_web_server;
mod starts_with_http_status;
pub mod suse;
pub mod update_classification;
pub mod vsftpd_tls_version_keys;
pub mod web_server_facts;

pub use account_programs::AccountPrograms;
pub use adapter::DistroAdapter;
pub use adapter_binary::AdapterBinary;
pub use adapter_for::adapter_for;
pub use app_facts::AppFacts;
pub use backup_facts::BackupFacts;
pub use catalogue_probe::CatalogueProbe;
pub use database_facts::DatabaseFacts;
pub use detection::detect::detect;
pub use detection::detect_error::DetectError;
pub use detection::distro_info::DistroInfo;
pub use detection::os_release::parse;
pub use dns_facts::DnsFacts;
pub use dovecot_major::DovecotMajor;
pub use family::DistroFamily;
pub use file_transfer_facts::FileTransferFacts;
pub use firewall_facts::FirewallFacts;
pub use key_value_server::KeyValueServer;
pub use mail_facts::MailFacts;
pub use nodejs_packaging::NodejsPackaging;
pub use package_facts::PackageFacts;
pub use package_failure::PackageFailure;
pub use package_query_failure::PackageQueryFailure;
pub use pending_update::PendingUpdate;
pub use pending_updates_failure::PendingUpdatesFailure;
pub use php_extension_facts::PhpExtensionFacts;
pub use php_extension_switch::PhpExtensionSwitch;
pub use php_facts::PhpFacts;
pub use reboot_probe::RebootProbe;
pub use reboot_probe_failure::RebootProbeFailure;
pub use reboot_requirement::RebootRequirement;
pub use scheduling_facts::SchedulingFacts;
pub use service_manager_facts::ServiceManagerFacts;
pub use site_web_server::SiteWebServer;
pub use update_classification::UpdateClassification;
pub use vsftpd_tls_version_keys::VsftpdTlsVersionKeys;
pub use web_server_facts::WebServerFacts;
