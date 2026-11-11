//! Adapter for the RHEL family (AlmaLinux, Rocky).

pub mod rhel_adapter;
pub mod rhel_apps;
pub mod rhel_package_candidates;
pub mod rhel_packages;
pub mod rhel_paths;
pub mod rhel_php_extensions;
pub mod rhel_removal;
pub mod rhel_services;

pub use rhel_adapter::RhelAdapter;
