//! Adapter for the SUSE family (openSUSE Leap, SLES): zypper, systemd, SELinux.

pub mod suse_account_programs;
pub mod suse_adapter;
pub mod suse_apps;
pub mod suse_dns_services;
pub mod suse_mail_services;
pub mod suse_package_candidates;
pub mod suse_packages;
pub mod suse_paths;
pub mod suse_php_extensions;
pub mod suse_removal;
pub mod suse_services;

pub use suse_adapter::SuseAdapter;
