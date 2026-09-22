//! Readings the monitoring operations return, and the parsed shapes they are
//! built from — one type per file (rules/rust.md "Operation anatomy").

pub mod account_disk_usage;
pub mod cpu_times;
pub mod filesystem_usage;
pub mod host_metrics;
pub mod load_average;
pub mod machine_identity;
pub mod memory_usage;
pub mod network_counters;
pub mod primary_interface;
pub mod quota_enforceability_status;
pub mod server_fingerprint_inputs;
pub mod service_state;
pub mod service_status;
pub mod sftp_jail_status;
pub mod unit_report;
