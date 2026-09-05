//! Adapter for the RHEL family (AlmaLinux, Rocky).

pub mod rhel_adapter;
pub mod rhel_packages;
pub mod rhel_paths;
pub mod rhel_services;

pub use rhel_adapter::RhelAdapter;
