//! Adapter for the Debian family (Ubuntu, Debian).

pub mod debian_adapter;
pub mod debian_apps;
pub mod debian_package_candidates;
pub mod debian_packages;
pub mod debian_paths;
pub mod debian_php_extensions;
pub mod debian_services;

pub use debian_adapter::DebianAdapter;
